using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GARbro.GUI
{
    public class SpriteSheetAnimator
    {
        public enum SpriteLayout
        {
            Static,
            Grid
        }

        private BitmapSource _sourceImage;

        public BitmapSource Source => _sourceImage;
        public List<BitmapSource> Frames { get; private set; }
        public bool HasFrames => Frames != null && Frames.Count > 0;

        private List<int> _detectedColumnBoundaries;
        private List<int> _detectedRowBoundaries;

        // Background color detection
        private struct PixelColor
        {
            public byte R, G, B, A;

            public bool Equals(PixelColor other, int tolerance = 10)
            {
                return Math.Abs(R - other.R) <= tolerance &&
                       Math.Abs(G - other.G) <= tolerance &&
                       Math.Abs(B - other.B) <= tolerance &&
                       Math.Abs(A - other.A) <= tolerance;
            }

            public override string ToString()
            {
                return $"RGBA({R},{G},{B},{A})";
            }
        }

        private PixelColor _backgroundColor;
        private bool _backgroundDetected = false;

        public SpriteSheetAnimator(BitmapSource sourceImage)
        {
            _sourceImage = sourceImage;
            Frames = new List<BitmapSource>();
            _detectedColumnBoundaries = null;
            _detectedRowBoundaries = null;
        }

        public void ExtractGridFrames(int columns, int rows)
        {
            Frames.Clear();
            if (_sourceImage == null || columns <= 0 || rows <= 0)
                return;

            BitmapSource processedSource = _sourceImage;
            if (_sourceImage.Format != PixelFormats.Pbgra32 && 
                    _sourceImage.Format != PixelFormats.Bgra32 &&
                    _sourceImage.Format != PixelFormats.Bgr32)
            {
                processedSource = new FormatConvertedBitmap(_sourceImage, PixelFormats.Pbgra32, null, 0);
            }

            double frameWidth = processedSource.PixelWidth / columns;
            double frameHeight = processedSource.PixelHeight / rows;

            for (int row = 0; row < rows; row++)
            for (int col = 0; col < columns; col++)
            {
                var rect = new System.Windows.Int32Rect(
                    (int)(col * frameWidth),
                    (int)(row * frameHeight),
                    (int)frameWidth,
                    (int)frameHeight);

                var croppedBitmap = new CroppedBitmap(processedSource, rect);
                if (croppedBitmap.Format != PixelFormats.Pbgra32 && 
                    croppedBitmap.Format != PixelFormats.Bgra32)
                {
                    var convertedFrame = new FormatConvertedBitmap(croppedBitmap, PixelFormats.Pbgra32, null, 0);
                    Frames.Add(convertedFrame);
                }
                else
                    Frames.Add(croppedBitmap);
            }
        }

        public SpriteLayout AutoDetectLayout(out int columns, out int rows)
        {
            columns = 0;
            rows = 0;

            if (_sourceImage == null)
                return SpriteLayout.Static;

            // Get pixel data
            int width = _sourceImage.PixelWidth;
            int height = _sourceImage.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            _sourceImage.CopyPixels(pixels, stride, 0);

            DetectBackgroundColor(pixels, width, height, stride);

            var gridInfo = DetectGridUsingProjections(pixels, width, height, stride);
            if (gridInfo.HasValue)
            {
                columns = gridInfo.Value.columns;
                rows = gridInfo.Value.rows;

                if (columns > 0 && rows > 0 && columns * rows <= 10000) // Allow up to 100x100 grids
                    return SpriteLayout.Grid;
            }

            return SpriteLayout.Static;
        }

        private void DetectBackgroundColor(byte[] pixels, int width, int height, int stride)
        {
            // Strategy 1: Check corner pixels (0,0), (width-1,0), (0,height-1), (width-1,height-1)
            var cornerColors = new List<PixelColor>();

            // Top-left
            cornerColors.Add(GetPixelColor(pixels, 0, 0, stride));
            // Top-right
            cornerColors.Add(GetPixelColor(pixels, width - 1, 0, stride));
            // Bottom-left
            cornerColors.Add(GetPixelColor(pixels, 0, height - 1, stride));
            // Bottom-right
            cornerColors.Add(GetPixelColor(pixels, width - 1, height - 1, stride));

            var firstCorner = cornerColors[0];
            bool cornersAgree = cornerColors.All(c => c.Equals(firstCorner, 10));

            if (cornersAgree)
            {
                _backgroundColor = firstCorner;
                _backgroundDetected = true;
                System.Diagnostics.Debug.WriteLine($"Background detected from corners: {_backgroundColor}");
                return;
            }

            // Strategy 2: Find the most common color in the image
            var colorCounts = new Dictionary<uint, int>();
            var colorMap = new Dictionary<uint, PixelColor>();

            // Sample every 10th pixel for performance
            for (int y = 0; y < height; y += 10)
            {
                for (int x = 0; x < width; x += 10)
                {
                    var color = GetPixelColor(pixels, x, y, stride);
                    uint key = ((uint)color.R << 24) | ((uint)color.G << 16) | ((uint)color.B << 8) | color.A;

                    if (!colorCounts.ContainsKey(key))
                    {
                        colorCounts[key] = 0;
                        colorMap[key] = color;
                    }
                    colorCounts[key]++;
                }
            }

            if (colorCounts.Count > 0)
            {
                var mostCommon = colorCounts.OrderByDescending(kvp => kvp.Value).First();
                _backgroundColor = colorMap[mostCommon.Key];
                _backgroundDetected = true;
                System.Diagnostics.Debug.WriteLine($"Background detected as most common color: {_backgroundColor} (appears {mostCommon.Value} times)");
            }
            else
            {
                // Default: treat full transparency as background
                _backgroundColor = new PixelColor { R = 0, G = 0, B = 0, A = 0 };
                _backgroundDetected = true;
                System.Diagnostics.Debug.WriteLine("Using default transparent background");
            }
        }

        private PixelColor GetPixelColor(byte[] pixels, int x, int y, int stride)
        {
            int idx = (y * stride) + (x * 4);
            if (idx + 3 < pixels.Length)
            {
                return new PixelColor
                {
                    B = pixels[idx],
                    G = pixels[idx + 1],
                    R = pixels[idx + 2],
                    A = pixels[idx + 3]
                };
            }
            return new PixelColor { R = 0, G = 0, B = 0, A = 0 };
        }

        private bool IsBackgroundPixel(byte[] pixels, int x, int y, int width, int stride)
        {
            var pixelColor = GetPixelColor(pixels, x, y, stride);

            // Consider pixels with low alpha as background (anti-aliasing)
            if (pixelColor.A < 50)
                return true;

            if (!_backgroundDetected)
                return false;

            // Check if pixel matches background color (with tolerance)
            bool matchesBackground = pixelColor.Equals(_backgroundColor, 10);

            return matchesBackground;
        }

        private bool IsContentPixel(byte[] pixels, int x, int y, int width, int stride)
        {
            return !IsBackgroundPixel(pixels, x, y, width, stride);
        }

        private (int columns, int rows)? DetectGridUsingProjections(byte[] pixels, int width, int height, int stride)
        {
            // Calculate horizontal and vertical projections
            var horizontalProjection = new int[height];
            var verticalProjection = new int[width];

            // Build projections - count non-background pixels
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (IsContentPixel(pixels, x, y, width, stride))
                    {
                        horizontalProjection[y]++;
                        verticalProjection[x]++;
                    }
                }
            }

            // Find gaps in projections (potential grid lines)
            var horizontalGaps = FindSignificantGaps(horizontalProjection, height * 0.01);
            var verticalGaps = FindSignificantGaps(verticalProjection, width * 0.01);

            System.Diagnostics.Debug.WriteLine($"Found {verticalGaps.Count} vertical gaps: {string.Join(",", verticalGaps)}");
            System.Diagnostics.Debug.WriteLine($"Found {horizontalGaps.Count} horizontal gaps: {string.Join(",", horizontalGaps)}");

            // Analyze gap patterns to find regular grid
            var columnBoundaries = FindRegularGrid(verticalGaps, width);
            var rowBoundaries = FindRegularGrid(horizontalGaps, height);

            System.Diagnostics.Debug.WriteLine($"Column boundaries: {string.Join(",", columnBoundaries)}");
            System.Diagnostics.Debug.WriteLine($"Row boundaries: {string.Join(",", rowBoundaries)}");

            if (columnBoundaries.Count > 1 && rowBoundaries.Count > 1)
            {
                int cols = columnBoundaries.Count - 1;
                int rows = rowBoundaries.Count - 1;

                System.Diagnostics.Debug.WriteLine($"Detected boundaries suggest: {cols}x{rows}");

                // Check if the detected grid seems suspicious
                bool suspicious = false;

                // Check column intervals for irregularity
                var colIntervals = new List<int>();
                for (int i = 1; i < columnBoundaries.Count; i++)
                    colIntervals.Add(columnBoundaries[i] - columnBoundaries[i - 1]);

                var rowIntervals = new List<int>();
                for (int i = 1; i < rowBoundaries.Count; i++)
                    rowIntervals.Add(rowBoundaries[i] - rowBoundaries[i - 1]);

                // Calculate coefficient of variation (CV) to measure irregularity
                if (colIntervals.Count > 1)
                {
                    double colMean = colIntervals.Average();
                    double colStdDev = Math.Sqrt(colIntervals.Select(x => Math.Pow(x - colMean, 2)).Average());
                    double colCV = colStdDev / colMean;

                    if (colCV > 0.4) // High variation suggests irregular grid
                    {
                        System.Diagnostics.Debug.WriteLine($"Column intervals are irregular (CV={colCV:F2})");
                        suspicious = true;
                    }
                }

                if (rowIntervals.Count > 1)
                {
                    double rowMean = rowIntervals.Average();
                    double rowStdDev = Math.Sqrt(rowIntervals.Select(x => Math.Pow(x - rowMean, 2)).Average());
                    double rowCV = rowStdDev / rowMean;

                    if (rowCV > 0.4) // High variation suggests irregular grid
                    {
                        System.Diagnostics.Debug.WriteLine($"Row intervals are irregular (CV={rowCV:F2})");
                        suspicious = true;
                    }
                }

                // If suspicious, try common grids first
                if (suspicious)
                {
                    System.Diagnostics.Debug.WriteLine("Detected grid seems irregular, trying common sizes first...");
                    var commonResult = TryCommonGridSizes(pixels, width, height, stride);
                    if (commonResult.HasValue)
                    {
                        return commonResult;
                    }
                }

                // Continue with existing validation logic...
                System.Diagnostics.Debug.WriteLine($"Column intervals: {string.Join(",", colIntervals)}");
                System.Diagnostics.Debug.WriteLine($"Row intervals: {string.Join(",", rowIntervals)}");
                // Check if any interval is approximately a multiple of the median
                int missingCols = CountMissingCells(colIntervals);
                int missingRows = CountMissingCells(rowIntervals);

                int possibleCols = cols + missingCols;
                int possibleRows = rows + missingRows;

                if (possibleCols != cols || possibleRows != rows)
                {
                    System.Diagnostics.Debug.WriteLine($"Intervals suggest possible {possibleCols}x{possibleRows} grid with empty cells (missing {missingCols} cols, {missingRows} rows)");
                    if (ValidateGridDoesntCutSprites(pixels, width, height, stride, possibleCols, possibleRows, checkThoroughly: true))
                    {
                        System.Diagnostics.Debug.WriteLine($"✓ Grid {possibleCols}x{possibleRows} validated (with empty cells)");
                        return (possibleCols, possibleRows);
                    }
                }

                // Try the detected grid first
                if (TryValidateGrid(pixels, width, height, stride, cols, rows, columnBoundaries, rowBoundaries))
                {
                    return (cols, rows);
                }

                // If detected grid fails, try nearby variations
                var nearbyGrids = GenerateNearbyGrids(cols, rows, width, height);
                foreach (var (testCols, testRows) in nearbyGrids)
                {
                    System.Diagnostics.Debug.WriteLine($"Trying nearby grid: {testCols}x{testRows}");
                    if (ValidateGridDoesntCutSprites(pixels, width, height, stride, testCols, testRows, checkThoroughly: true))
                    {
                        System.Diagnostics.Debug.WriteLine($"Nearby grid {testCols}x{testRows} works!");
                        return (testCols, testRows);
                    }
                }

                System.Diagnostics.Debug.WriteLine($"All nearby grids failed for {cols}x{rows}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Insufficient boundaries found: {columnBoundaries.Count - 1} cols, {rowBoundaries.Count - 1} rows");
            }

            // If projection method fails, try connected components with better filtering
            System.Diagnostics.Debug.WriteLine("Trying connected components fallback");
            return DetectGridUsingConnectedComponents(pixels, width, height, stride);
        }

        private (int columns, int rows)? TryCommonGridSizes(byte[] pixels, int width, int height, int stride)
        {
            // Common sprite sheet configurations, ordered by likelihood
            var commonGrids = new List<(int cols, int rows)>
            {
                // Square grids first (most common)
                (2, 2), (3, 3), (4, 4), (5, 5), (6, 6), (7, 7), (8, 8),
                // Near-square ratios
                (4, 3), (3, 4), (5, 4), (4, 5),
                (6, 5), (5, 6), (6, 4), (4, 6),
                (3, 2), (2, 3), (5, 3), (3, 5),
                // 2-row layouts (very common for sprite sheets)
                (3, 2), (4, 2), (5, 2), (6, 2), (8, 2), (10, 2),
                (2, 3), (2, 4), (2, 5), (2, 6), (2, 8),
                // Single row/column (less common for sprite sheets)
                (2, 1), (3, 1), (4, 1), (5, 1), (6, 1), (8, 1), (10, 1),
                (1, 2), (1, 3), (1, 4), (1, 5), (1, 6), (1, 8), (1, 10),
                // Larger grids
                (9, 9), (10, 10), (12, 12), (16, 16)
            };

            System.Diagnostics.Debug.WriteLine("Trying common grid sizes...");

            var validGrids = new List<(int cols, int rows, double score)>();

            foreach (var (cols, rows) in commonGrids)
            {
                // Quick check if this grid size makes sense for the image
                double cellWidth = (double)width / cols;
                double cellHeight = (double)height / rows;

                // Skip if cells would be too small
                if (cellWidth < 16 || cellHeight < 16) continue;

                // Only test if it divides evenly
                if (width % cols == 0 && height % rows == 0)
                {
                    if (ValidateGridDoesntCutSprites(pixels, width, height, stride, cols, rows, checkThoroughly: false))
                    {
                        // Calculate how square the cells are (1.0 = perfect square)
                        double aspectRatio = cellWidth / cellHeight;
                        double squareness = aspectRatio > 1 ? 1.0 / aspectRatio : aspectRatio;

                        // Prefer grids with more cells (but not too many)
                        double cellCountScore = Math.Min(1.0, (cols * rows) / 20.0);

                        // Combined score
                        double score = squareness * 0.7 + cellCountScore * 0.3;

                        System.Diagnostics.Debug.WriteLine($"✓ Grid {cols}x{rows} works! Cell size: {cellWidth:F0}x{cellHeight:F0}, squareness: {squareness:F2}, score: {score:F2}");
                        validGrids.Add((cols, rows, score));
                    }
                }
            }

            if (validGrids.Count > 0)
            {
                // Return the grid with the best score
                var best = validGrids.OrderByDescending(g => g.score).First();
                System.Diagnostics.Debug.WriteLine($"Selected best grid: {best.cols}x{best.rows} (score: {best.score:F2})");
                return (best.cols, best.rows);
            }

            return null;
        }

        private int CountMissingCells(List<int> intervals)
        {
            if (intervals.Count < 2) return 0;

            // Find the median interval (more robust than mean)
            var sorted = intervals.OrderBy(i => i).ToList();
            double median = sorted.Count % 2 == 0
                ? (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2.0
                : sorted[sorted.Count / 2];

            System.Diagnostics.Debug.WriteLine($"Intervals: {string.Join(",", intervals)}, median: {median:F1}");

            int missingCells = 0;
            foreach (var interval in intervals)
            {
                // Check if this interval is approximately a multiple of the median
                double ratio = interval / median;
                int nearestMultiple = (int)Math.Round(ratio);

                System.Diagnostics.Debug.WriteLine($"  Interval {interval}: ratio={ratio:F2}, nearest multiple={nearestMultiple}");

                if (nearestMultiple > 1 && Math.Abs(ratio - nearestMultiple) < 0.3) // 30% tolerance
                {
                    // This interval likely spans multiple cells
                    int emptyCells = nearestMultiple - 1;
                    missingCells += emptyCells;
                    System.Diagnostics.Debug.WriteLine($"  -> Interval {interval} spans {nearestMultiple} cells, adding {emptyCells} empty cells");
                }
            }

            System.Diagnostics.Debug.WriteLine($"Total missing cells detected: {missingCells}");
            return missingCells;
        }

        private bool TryValidateGrid(byte[] pixels, int width, int height, int stride, int cols, int rows,
            List<int> columnBoundaries, List<int> rowBoundaries)
        {
            // Store the boundaries for later use
            _detectedColumnBoundaries = columnBoundaries;
            _detectedRowBoundaries = rowBoundaries;

            // First check if detected boundaries are clean
            if (ValidateGridWithActualBoundaries(pixels, width, height, stride, columnBoundaries, rowBoundaries))
            {
                System.Diagnostics.Debug.WriteLine($"Detected boundaries are clean, now checking uniform grid {cols}x{rows}");

                // Now check if a uniform grid with this many columns/rows would work
                if (ValidateGridDoesntCutSprites(pixels, width, height, stride, cols, rows, checkThoroughly: true))
                {
                    System.Diagnostics.Debug.WriteLine($"✓ Grid {cols}x{rows} fully validated");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Grid {cols}x{rows} uniform division would cut sprites");

                }
                return true;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Grid {cols}x{rows} detected boundaries would cut sprites");
            }
            return false;
        }

        private bool ValidateGridWithActualBoundaries(byte[] pixels, int width, int height, int stride,
    List<int> columnBoundaries, List<int> rowBoundaries)
        {
            System.Diagnostics.Debug.WriteLine($"Checking detected boundaries (not uniform grid):");
            System.Diagnostics.Debug.WriteLine($"  Column boundaries: {string.Join(",", columnBoundaries)}");
            System.Diagnostics.Debug.WriteLine($"  Row boundaries: {string.Join(",", rowBoundaries)}");

            // Check vertical lines at actual detected boundary positions
            for (int c = 1; c < columnBoundaries.Count - 1; c++)
            {
                int lineX = columnBoundaries[c];

                if (lineX >= 0 && lineX < width)
                {
                    int contentPixelCount = 0;
                    for (int y = 0; y < height; y++)
                    {
                        if (IsContentPixel(pixels, lineX, y, width, stride))
                        {
                            contentPixelCount++;
                        }
                    }

                    if (contentPixelCount > height * 0.01)  // Strict: 1% threshold
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Detected boundary at x={lineX} cuts through sprites ({contentPixelCount}/{height} pixels)");

                        // Show what uniform grid would use instead
                        int cols = columnBoundaries.Count - 1;
                        int uniformX = (width * c) / cols;
                        System.Diagnostics.Debug.WriteLine($"  (Uniform grid would put line at x={uniformX}, difference: {uniformX - lineX:+#;-#;0})");

                        return false;
                    }
                }
            }

            // Check horizontal lines at actual detected boundary positions
            for (int r = 1; r < rowBoundaries.Count - 1; r++)
            {
                int lineY = rowBoundaries[r];

                if (lineY >= 0 && lineY < height)
                {
                    int contentPixelCount = 0;
                    for (int x = 0; x < width; x++)
                    {
                        if (IsContentPixel(pixels, x, lineY, width, stride))
                        {
                            contentPixelCount++;
                        }
                    }

                    if (contentPixelCount > width * 0.01)  // Strict: 1% threshold
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Detected boundary at y={lineY} cuts through sprites ({contentPixelCount}/{width} pixels)");

                        // Show what uniform grid would use instead
                        int rows = rowBoundaries.Count - 1;
                        int uniformY = (height * r) / rows;
                        System.Diagnostics.Debug.WriteLine($"  (Uniform grid would put line at y={uniformY}, difference: {uniformY - lineY:+#;-#;0})");

                        return false;
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine("✓ Detected boundaries don't cut sprites");
            return true;
        }

        private List<(int cols, int rows)> GenerateNearbyGrids(int originalCols, int originalRows, int width, int height)
        {
            var nearbyGrids = new List<(int cols, int rows)>();

            // Generate variations: +/-1 and +/-2 in each dimension
            for (int deltaCol = -2; deltaCol <= 2; deltaCol++)
            {
                for (int deltaRow = -2; deltaRow <= 2; deltaRow++)
                {
                    if (deltaCol == 0 && deltaRow == 0) continue; // Skip original

                    int newCols = originalCols + deltaCol;
                    int newRows = originalRows + deltaRow;

                    // Sanity checks
                    if (newCols >= 1 && newRows >= 1 && newCols <= 100 && newRows <= 100)
                    {
                        // Avoid extreme aspect ratios (like 1x100 strips) unless image is actually that shape
                        double gridAspectRatio = (double)newCols / newRows;
                        double imageAspectRatio = (double)width / height;

                        // Allow grid aspect ratio to be different from image, but not extremely different
                        bool reasonableAspectRatio =
                            gridAspectRatio >= 0.1 && gridAspectRatio <= 10.0 && // Not too extreme
                            (gridAspectRatio <= imageAspectRatio * 3.0 && gridAspectRatio >= imageAspectRatio / 3.0); // Within 3x of image ratio

                        if (reasonableAspectRatio)
                        {
                            nearbyGrids.Add((newCols, newRows));
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Skipping {newCols}x{newRows} - unreasonable aspect ratio {gridAspectRatio:F2} vs image {imageAspectRatio:F2}");
                        }
                    }
                }
            }

            // Sort by how close they are to the original (prefer smaller changes)
            nearbyGrids.Sort((a, b) =>
            {
                int distA = Math.Abs(a.cols - originalCols) + Math.Abs(a.rows - originalRows);
                int distB = Math.Abs(b.cols - originalCols) + Math.Abs(b.rows - originalRows);
                return distA.CompareTo(distB);
            });

            System.Diagnostics.Debug.WriteLine($"Generated nearby grids for {originalCols}x{originalRows}: {string.Join(", ", nearbyGrids.Select(g => $"{g.cols}x{g.rows}"))}");

            return nearbyGrids;
        }

        private bool ValidateGridDoesntCutSprites(byte[] pixels, int width, int height, int stride, int columns, int rows, bool checkThoroughly = false)
        {
            double cellWidth = (double)width / columns;
            double cellHeight = (double)height / rows;

            if (width % columns > 0 || height % rows > 0)
                return false;

            System.Diagnostics.Debug.WriteLine($"=== Checking grid {columns}x{rows} ===");
            System.Diagnostics.Debug.WriteLine($"Image size: {width}x{height}");
            System.Diagnostics.Debug.WriteLine($"Cell size: {cellWidth:F2}x{cellHeight:F2}");
            System.Diagnostics.Debug.WriteLine($"Background color: {_backgroundColor}");

            for (int c = 1; c < columns; c++)
            {
                int lineX = (width * c) / columns;

                System.Diagnostics.Debug.WriteLine($"Checking vertical line {c}/{columns} at x={lineX} (from {c} * {width} / {columns})");

                if (lineX > 0 && lineX < width)
                {
                    int contentPixelCount = 0;
                    int backgroundPixelCount = 0;

                    // Check the exact line position
                    for (int y = 0; y < height; y++)
                    {
                        if (IsContentPixel(pixels, lineX, y, width, stride))
                        {
                            contentPixelCount++;

                            // Log first few content pixels for debugging
                            if (contentPixelCount <= 5)
                            {
                                var color = GetPixelColor(pixels, lineX, y, stride);
                                System.Diagnostics.Debug.WriteLine($"  Content pixel at ({lineX},{y}): {color}");
                            }
                        }
                        else
                        {
                            backgroundPixelCount++;
                        }
                    }

                    double contentRatio = (double)contentPixelCount / height;
                    System.Diagnostics.Debug.WriteLine($"Vertical line {c} at x={lineX}: {contentPixelCount} content, {backgroundPixelCount} background pixels ({contentRatio:P1} content)");

                    if (contentPixelCount > 20 || contentRatio > 0.05)  // More than 5 pixels or 1%
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ CUTTING DETECTED: Vertical line {c} at x={lineX} cuts through sprites!");

                        // Check adjacent pixels to see if slight adjustment would help
                        for (int offset = -2; offset <= 2; offset++)
                        {
                            if (offset == 0) continue;
                            int testX = lineX + offset;
                            if (testX >= 0 && testX < width)
                            {
                                int testContentCount = 0;
                                for (int y = 0; y < height && testContentCount < 10; y++)
                                {
                                    if (IsContentPixel(pixels, testX, y, width, stride))
                                        testContentCount++;
                                }
                                if (testContentCount < 3)
                                {
                                    System.Diagnostics.Debug.WriteLine($"  (Line at x={testX} (offset {offset:+#;-#;0}) would have only {testContentCount} content pixels)");
                                }
                            }
                        }

                        return false;
                    }
                }
            }

            for (int r = 1; r < rows; r++)
            {
                int lineY = (height * r) / rows;

                System.Diagnostics.Debug.WriteLine($"Checking horizontal line {r}/{rows} at y={lineY} (from {r} * {height} / {rows})");

                if (lineY > 0 && lineY < height)
                {
                    int contentPixelCount = 0;
                    int backgroundPixelCount = 0;

                    // Check the exact line position
                    for (int x = 0; x < width; x++)
                    {
                        if (IsContentPixel(pixels, x, lineY, width, stride))
                        {
                            contentPixelCount++;

                            // Log first few content pixels for debugging
                            if (contentPixelCount <= 5)
                            {
                                var color = GetPixelColor(pixels, x, lineY, stride);
                                System.Diagnostics.Debug.WriteLine($"  Content pixel at ({x},{lineY}): {color}");
                            }
                        }
                        else
                        {
                            backgroundPixelCount++;
                        }
                    }

                    double contentRatio = (double)contentPixelCount / width;
                    System.Diagnostics.Debug.WriteLine($"Horizontal line {r} at y={lineY}: {contentPixelCount} content, {backgroundPixelCount} background pixels ({contentRatio:P1} content)");

                    // Be strict - even a few content pixels means we're cutting
                    if (contentPixelCount > 5 || contentRatio > 0.01)  // More than 5 pixels or 1%
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ CUTTING DETECTED: Horizontal line {r} at y={lineY} cuts through sprites!");

                        // Check adjacent pixels to see if slight adjustment would help
                        for (int offset = -2; offset <= 2; offset++)
                        {
                            if (offset == 0) continue;
                            int testY = lineY + offset;
                            if (testY >= 0 && testY < height)
                            {
                                int testContentCount = 0;
                                for (int x = 0; x < width && testContentCount < 10; x++)
                                {
                                    if (IsContentPixel(pixels, x, testY, width, stride))
                                        testContentCount++;
                                }
                                if (testContentCount < 3)
                                {
                                    System.Diagnostics.Debug.WriteLine($"  (Line at y={testY} (offset {offset:+#;-#;0}) would have only {testContentCount} content pixels)");
                                }
                            }
                        }

                        return false;
                    }
                }
            }

            // Additional validation: check if cells have reasonable content distribution
            if (checkThoroughly)
            {
                int emptyCells = 0;
                int totalCells = columns * rows;

                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < columns; c++)
                    {
                        // Use the SAME calculation as ExtractGridFrames!
                        int x1 = (width * c) / columns;
                        int y1 = (height * r) / rows;
                        int x2 = (width * (c + 1)) / columns;
                        int y2 = (height * (r + 1)) / rows;

                        if (!CellHasContent(pixels, width, stride, x1, y1, x2, y2))
                        {
                            emptyCells++;
                        }
                    }
                }

                double emptyRatio = (double)emptyCells / totalCells;
                System.Diagnostics.Debug.WriteLine($"Grid has {emptyCells}/{totalCells} empty cells ({emptyRatio:P0})");

                // Reject if more than 75% of cells are empty
                if (emptyRatio > 0.75)
                {
                    System.Diagnostics.Debug.WriteLine($"Grid {columns}x{rows} REJECTED - too many empty cells");
                    return false;
                }
            }

            System.Diagnostics.Debug.WriteLine($"✓ Grid {columns}x{rows} PASSED - no cutting detected");
            return true;
        }

        private List<int> FindSignificantGaps(int[] projection, double minGapScore)
        {
            var gaps = new List<int>();

            // Calculate statistics
            double mean = projection.Where(p => p > 0).DefaultIfEmpty(0).Average();
            double max = projection.Max();

            // Use a much stricter threshold - only consider truly empty areas
            double threshold = Math.Max(2, mean * 0.02); // 2% of mean, minimum 2

            // Also require gaps to be significant relative to the image size
            int minGapSize = Math.Max(5, projection.Length / 200); // At least 0.5% of image dimension

            System.Diagnostics.Debug.WriteLine($"Projection stats: mean={mean:F1}, max={max}, threshold={threshold:F1}, minGapSize={minGapSize}");

            bool inGap = false;
            int gapStart = 0;
            var allGaps = new List<(int position, int size, double density, double relativeSize)>();

            for (int i = 0; i < projection.Length; i++)
            {
                if (projection[i] <= threshold)
                {
                    if (!inGap)
                    {
                        inGap = true;
                        gapStart = i;
                    }
                }
                else
                {
                    if (inGap)
                    {
                        inGap = false;
                        int gapSize = i - gapStart;
                        int gapCenter = (gapStart + i) / 2;

                        // Collect all potential gaps
                        if (gapSize >= minGapSize &&
                            gapStart > projection.Length * 0.05 &&
                            i < projection.Length * 0.95)
                        {
                            double gapDensity = 0;
                            for (int j = gapStart; j < i; j++)
                            {
                                gapDensity += projection[j];
                            }
                            gapDensity /= gapSize;

                            if (gapDensity <= threshold)
                            {
                                double relativeSize = (double)gapSize / projection.Length;
                                allGaps.Add((gapCenter, gapSize, gapDensity, relativeSize));
                            }
                        }
                    }
                }
            }

            // Sort gaps by size (largest first) and quality
            var sortedGaps = allGaps.OrderByDescending(g => g.size).ThenBy(g => g.density).ToList();

            System.Diagnostics.Debug.WriteLine($"All gaps by size: {string.Join(", ", sortedGaps.Select(g => $"{g.position}(size:{g.size}, rel:{g.relativeSize:F3})"))}");

            // Filter out gaps that are too small compared to the median gap size
            if (sortedGaps.Count > 0)
            {
                // Calculate median gap size (more robust than using largest)
                var gapSizes = sortedGaps.Select(g => g.size).OrderBy(s => s).ToList();
                double medianGapSize = gapSizes.Count % 2 == 0
                    ? (gapSizes[gapSizes.Count / 2 - 1] + gapSizes[gapSizes.Count / 2]) / 2.0
                    : gapSizes[gapSizes.Count / 2];

                // Filter out gaps smaller than 40% of median
                double minAcceptableSize = medianGapSize * 0.4;
                var significantGaps = sortedGaps.Where(g => g.size >= minAcceptableSize).ToList();

                System.Diagnostics.Debug.WriteLine($"Median gap size: {medianGapSize:F1}, filtering gaps smaller than {minAcceptableSize:F1}");
                System.Diagnostics.Debug.WriteLine($"Significant gaps: {string.Join(", ", significantGaps.Select(g => $"{g.position}(size:{g.size})"))}");

                bool isLikelyHeight = projection.Length < 700; // Simple height detection

                if (isLikelyHeight)
                {
                    // For height: be more conservative, take max 5 gaps (6 rows)
                    int maxHeightGaps = 5;
                    gaps = significantGaps.Take(maxHeightGaps).Select(g => g.position).OrderBy(p => p).ToList();
                }
                else
                {
                    // For width: take more gaps
                    int maxWidthGaps = 8;
                    gaps = significantGaps.Take(maxWidthGaps).Select(g => g.position).OrderBy(p => p).ToList();
                }

                System.Diagnostics.Debug.WriteLine($"Selected gaps: {string.Join(",", gaps)}");
            }

            // Add boundaries
            gaps.Insert(0, 0);
            gaps.Add(projection.Length);

            System.Diagnostics.Debug.WriteLine($"Final gaps: {string.Join(",", gaps)}");
            return gaps;
        }

        private List<int> FindRegularGrid(List<int> gaps, int totalSize)
        {
            if (gaps.Count < 3) // Need at least 3 points to form 2 cells
            {
                System.Diagnostics.Debug.WriteLine($"Not enough gaps for regular grid: {gaps.Count}");
                return new List<int>();
            }

            // Calculate intervals between gaps
            var intervals = new List<int>();
            for (int i = 1; i < gaps.Count; i++)
            {
                intervals.Add(gaps[i] - gaps[i - 1]);
            }

            System.Diagnostics.Debug.WriteLine($"Intervals: {string.Join(",", intervals)}");

            // Special case: if we only have 2 intervals (3 gaps total), we might still have a valid 2-cell grid
            if (intervals.Count == 2)
            {
                System.Diagnostics.Debug.WriteLine("Only 2 intervals - accepting as 2-cell grid");
                return gaps;
            }

            // If intervals are reasonably similar (within 50% of each other), use all gaps
            double minInterval = intervals.Min();
            double maxInterval = intervals.Max();

            if (maxInterval <= minInterval * 2.0) // Max is at most 2x the min
            {
                System.Diagnostics.Debug.WriteLine($"Intervals are reasonably similar ({minInterval}-{maxInterval}), using all gaps");
                return gaps;
            }

            // If we have 4-6 gaps (3-5 cells), which is common for sprite grids, be more lenient
            if (gaps.Count >= 4 && gaps.Count <= 6)
            {
                // Check if most intervals are similar (allow one outlier)
                var sortedIntervals = intervals.OrderBy(i => i).ToList();
                var middleIntervals = sortedIntervals.Skip(1).Take(sortedIntervals.Count - 2).ToList();

                if (middleIntervals.Count > 0)
                {
                    double middleMin = middleIntervals.Min();
                    double middleMax = middleIntervals.Max();

                    if (middleMax <= middleMin * 1.5) // Middle intervals are similar
                    {
                        System.Diagnostics.Debug.WriteLine($"Most intervals are similar ({middleMin}-{middleMax}), using all gaps for {gaps.Count - 1}-cell grid");
                        return gaps;
                    }
                }
            }

            // Find the most common interval (with tolerance)
            var intervalGroups = GroupSimilarValues(intervals, totalSize * 0.05);

            if (intervalGroups.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("No interval groups found");
                return new List<int>();
            }

            // Get the most frequent interval
            var mostCommon = intervalGroups.Where(g => g.Count >= 2 || intervals.Count <= 3)
                                          .OrderByDescending(g => g.Count)
                                          .FirstOrDefault();

            if (mostCommon == null)
            {
                System.Diagnostics.Debug.WriteLine("No regular pattern found, but trying to use all gaps anyway");
                return gaps; // Use all gaps even if not perfectly regular
            }

            double regularInterval = mostCommon.Average;
            System.Diagnostics.Debug.WriteLine($"Regular interval: {regularInterval:F1} (appears {mostCommon.Count} times)");

            // Build regular grid based on this interval
            var gridLines = new List<int> { 0 };
            double position = regularInterval;

            while (position < totalSize)
            {
                // Find the closest actual gap to this position
                var closest = gaps.OrderBy(g => Math.Abs(g - position)).First();

                // If it's close enough, use it
                if (Math.Abs(closest - position) < regularInterval * 0.4) // More lenient tolerance
                {
                    if (!gridLines.Contains(closest))
                        gridLines.Add(closest);
                    position = closest + regularInterval;
                }
                else
                {
                    // No matching gap, might not be a regular grid
                    System.Diagnostics.Debug.WriteLine($"No gap found near expected position {position:F1}");
                    break;
                }
            }

            // Add the last boundary
            if (gridLines.Count > 1 && !gridLines.Contains(totalSize))
                gridLines.Add(totalSize);

            System.Diagnostics.Debug.WriteLine($"Final grid lines: {string.Join(",", gridLines)}");
            return gridLines;
        }

        private (int columns, int rows)? DetectGridUsingConnectedComponents(byte[] pixels, int width, int height, int stride)
        {
            // Find connected components (sprites)
            var components = FindConnectedComponents(pixels, width, height, stride);

            if (components.Count < 2)
                return null;

            // Analyze component positions to infer grid
            var result = InferGridFromComponents(components, width, height);

            if (result.HasValue)
            {
                // Validate this grid doesn't cut sprites
                if (ValidateGridDoesntCutSprites(pixels, width, height, stride, result.Value.columns, result.Value.rows, checkThoroughly: true))
                    return result;

                // Try nearby variations
                var nearbyGrids = GenerateNearbyGrids(result.Value.columns, result.Value.rows, width, height);
                foreach (var (testCols, testRows) in nearbyGrids)
                {
                    //System.Diagnostics.Debug.WriteLine($"Trying nearby grid from components: {testCols}x{testRows}");
                    if (ValidateGridDoesntCutSprites(pixels, width, height, stride, testCols, testRows, checkThoroughly: true))
                    {
                        System.Diagnostics.Debug.WriteLine($"Component-based nearby grid {testCols}x{testRows} works!");
                        return (testCols, testRows);
                    }
                }
            }

            return null;
        }

        private List<Rectangle> FindConnectedComponents(byte[] pixels, int width, int height, int stride)
        {
            var components = new List<Rectangle>();
            bool[,] visited = new bool[width, height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!visited[x, y])
                    {
                        if (IsContentPixel(pixels, x, y, width, stride))
                        {
                            var bounds = FloodFill(pixels, width, height, stride, x, y, visited);

                            // Filter out very small components (noise)
                            if (bounds.Width >= 4 && bounds.Height >= 4)
                            {
                                components.Add(bounds);
                            }
                        }
                        else
                        {
                            visited[x, y] = true; // Mark background pixels as visited
                        }
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"Found {components.Count} connected components");
            return components;
        }

        private Rectangle FloodFill(byte[] pixels, int width, int height, int stride, int startX, int startY, bool[,] visited)
        {
            int minX = startX, maxX = startX;
            int minY = startY, maxY = startY;

            var stack = new Stack<(int x, int y)>();
            stack.Push((startX, startY));
            visited[startX, startY] = true;

            while (stack.Count > 0)
            {
                var (x, y) = stack.Pop();

                // Update bounds
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);

                // Check 4-connected neighbors (not 8 to avoid merging diagonal sprites)
                int[] dx = { -1, 1, 0, 0 };
                int[] dy = { 0, 0, -1, 1 };

                for (int i = 0; i < 4; i++)
                {
                    int nx = x + dx[i];
                    int ny = y + dy[i];

                    if (nx >= 0 && nx < width && ny >= 0 && ny < height && !visited[nx, ny])
                    {
                        if (IsContentPixel(pixels, nx, ny, width, stride))
                        {
                            visited[nx, ny] = true;
                            stack.Push((nx, ny));
                        }
                    }
                }
            }

            return new Rectangle
            {
                Left = minX,
                Top = minY,
                Width = maxX - minX + 1,
                Height = maxY - minY + 1
            };
        }

        private (int columns, int rows)? InferGridFromComponents(List<Rectangle> components, int imageWidth, int imageHeight)
        {
            if (components.Count < 2)
                return null;

            // Get unique X and Y positions with clustering
            var xPositions = components.Select(c => c.Left).OrderBy(x => x).ToList();
            var yPositions = components.Select(c => c.Top).OrderBy(y => y).ToList();

            // Find median sprite dimensions
            var widths = components.Select(c => c.Width).OrderBy(w => w).ToList();
            var heights = components.Select(c => c.Height).OrderBy(h => h).ToList();

            double medianWidth = widths[widths.Count / 2];
            double medianHeight = heights[heights.Count / 2];

            System.Diagnostics.Debug.WriteLine($"Median sprite size: {medianWidth}x{medianHeight}");

            // Cluster positions to find grid columns and rows
            var columnPositions = ClusterPositions(xPositions, medianWidth * 0.5);
            var rowPositions = ClusterPositions(yPositions, medianHeight * 0.5);

            int detectedColumns = columnPositions.Count;
            int detectedRows = rowPositions.Count;

            System.Diagnostics.Debug.WriteLine($"Component clustering detected: {detectedColumns} columns, {detectedRows} rows");

            // Validate the detected grid
            if (detectedColumns > 0 && detectedRows > 0)
            {
                // Check if most components fit in this grid
                int fitted = 0;
                double cellWidth = (double)imageWidth / detectedColumns;
                double cellHeight = (double)imageHeight / detectedRows;

                foreach (var comp in components)
                {
                    int gridX = (int)(comp.Left / cellWidth);
                    int gridY = (int)(comp.Top / cellHeight);

                    if (gridX >= 0 && gridX < detectedColumns && gridY >= 0 && gridY < detectedRows)
                    {
                        fitted++;
                    }
                }

                System.Diagnostics.Debug.WriteLine($"Component fit: {fitted}/{components.Count} components fit in {detectedColumns}x{detectedRows} grid");

                // If at least 60% of components fit, it's likely a grid
                if (fitted >= components.Count * 0.6)
                {
                    return (detectedColumns, detectedRows);
                }
            }

            // Fallback: estimate from median sprite size
            int estimatedCols = Math.Max(1, (int)Math.Round(imageWidth / (medianWidth * 1.1)));
            int estimatedRows = Math.Max(1, (int)Math.Round(imageHeight / (medianHeight * 1.1)));

            System.Diagnostics.Debug.WriteLine($"Fallback estimate from median size: {estimatedCols}x{estimatedRows}");
            return (estimatedCols, estimatedRows);
        }

        private List<double> ClusterPositions(List<int> positions, double tolerance)
        {
            var clusters = new List<double>();

            if (positions.Count == 0)
                return clusters;

            clusters.Add(positions[0]);

            for (int i = 1; i < positions.Count; i++)
            {
                if (positions[i] - clusters.Last() > tolerance)
                {
                    clusters.Add(positions[i]);
                }
            }

            return clusters;
        }

        private bool ValidateGrid(byte[] pixels, int width, int height, int stride,
            List<int> columnBoundaries, List<int> rowBoundaries)
        {
            int cols = columnBoundaries.Count - 1;
            int rows = rowBoundaries.Count - 1;

            // Find the largest sprites to check if cells can fit them
            var sprites = FindConnectedComponents(pixels, width, height, stride);
            if (sprites.Count == 0)
                return false;

            // Get the largest sprites (top 30%)
            var sortedByArea = sprites.OrderByDescending(s => s.Width * s.Height).ToList();
            var largestSprites = sortedByArea.Take(Math.Max(1, sprites.Count / 3)).ToList();

            double maxSpriteWidth = largestSprites.Max(s => s.Width);
            double maxSpriteHeight = largestSprites.Max(s => s.Height);

            double cellWidth = (double)width / cols;
            double cellHeight = (double)height / rows;

            // Cells must be big enough to fit the largest sprites
            bool cellsCanFitSprites = cellWidth >= maxSpriteWidth * 0.9 && cellHeight >= maxSpriteHeight * 0.9;

            // Grid shouldn't be too fine
            bool reasonableGridSize = cols <= 50 && rows <= 50;

            System.Diagnostics.Debug.WriteLine($"Grid {cols}x{rows}: cell={cellWidth:F1}x{cellHeight:F1}, max sprite={maxSpriteWidth}x{maxSpriteHeight}");
            System.Diagnostics.Debug.WriteLine($"Cells can fit sprites: {cellsCanFitSprites}, reasonable size: {reasonableGridSize}");

            return cellsCanFitSprites && reasonableGridSize;
        }

        private bool CellHasContent(byte[] pixels, int width, int stride, int x1, int y1, int x2, int y2)
        {
            int contentPixelCount = 0;
            int totalPixels = (x2 - x1) * (y2 - y1);

            for (int y = y1; y < y2 && y < pixels.Length / stride; y++)
            {
                for (int x = x1; x < x2 && x < width; x++)
                {
                    if (IsContentPixel(pixels, x, y, width, stride))
                    {
                        contentPixelCount++;
                    }
                }
            }

            // Cell has content if at least 1% of pixels are non-background
            return contentPixelCount > totalPixels * 0.01;
        }

        private class ValueGroup
        {
            public List<int> Values { get; set; } = new List<int>();
            public double Average => Values.Average();
            public int Count => Values.Count;
        }

        private List<ValueGroup> GroupSimilarValues(List<int> values, double tolerance)
        {
            var groups = new List<ValueGroup>();

            foreach (var value in values)
            {
                bool added = false;
                foreach (var group in groups)
                {
                    if (Math.Abs(value - group.Average) <= tolerance)
                    {
                        group.Values.Add(value);
                        added = true;
                        break;
                    }
                }

                if (!added)
                {
                    groups.Add(new ValueGroup { Values = new List<int> { value } });
                }
            }

            return groups;
        }

        private class Rectangle
        {
            public int Left { get; set; }
            public int Top { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
        }
    }
}