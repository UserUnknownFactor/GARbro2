using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Utility
{
    public class PaletteQuantizer
    {
        private readonly int m_maxColors;
        private OctreeNode m_root;
        private int m_leafCount;
        private List<OctreeNode>[] m_reducibleNodes;

        public PaletteQuantizer (int maxColors)
        {
            if (maxColors < 2 || maxColors > 256)
                throw new ArgumentOutOfRangeException (nameof(maxColors), "Colors must be between 2 and 256");
            
            m_maxColors = maxColors;
            m_reducibleNodes = new List<OctreeNode>[9];
            for (int i = 0; i < 9; i++)
                m_reducibleNodes[i] = new List<OctreeNode>();
        }

        public BitmapSource Quantize (BitmapSource source)
        {
            // Convert to Bgra32 if needed
            if (source.Format != PixelFormats.Bgra32)
                source = new FormatConvertedBitmap (source, PixelFormats.Bgra32, null, 0);

            int width = source.PixelWidth;
            int height = source.PixelHeight;
            int stride = width * 4;
            var pixels = new byte[stride * height];
            source.CopyPixels (pixels, stride, 0);

            // Build octree
            m_root = new OctreeNode (0, this);
            m_leafCount = 0;

            // Add all pixels to octree
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int offset = y * stride + x * 4;
                    byte b = pixels[offset];
                    byte g = pixels[offset + 1];
                    byte r = pixels[offset + 2];
                    byte a = pixels[offset + 3];
                    
                    AddColor (r, g, b, a);
                }
            }

            // Reduce tree to requested color count
            while (m_leafCount > m_maxColors)
                ReduceTree();

            var palette = BuildPalette();
            
            // Create indexed image
            var indexed = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int src = y * stride + x * 4;
                    int dst = y * width + x;
                    byte b = pixels[src];
                    byte g = pixels[src + 1];
                    byte r = pixels[src + 2];
                    byte a = pixels[src + 3];
                    
                    indexed[dst] = (byte)GetPaletteIndex (r, g, b, a);
                }
            }

            return BitmapSource.Create (width, height, 96, 96, 
                PixelFormats.Indexed8, palette, indexed, width);
        }

        private void AddColor (byte r, byte g, byte b, byte a)
        {
            m_root.AddColor (r, g, b, a, 0);
        }

        private void ReduceTree()
        {
            // Find deepest level with reducible nodes
            int level = 7;
            while (level >= 0 && m_reducibleNodes[level].Count == 0)
                level--;

            if (level < 0)
                return;

            // Reduce the last node at this level
            var node = m_reducibleNodes[level][m_reducibleNodes[level].Count - 1];
            m_reducibleNodes[level].RemoveAt (m_reducibleNodes[level].Count - 1);
            
            m_leafCount -= node.Reduce();
            m_leafCount++;
        }

        private BitmapPalette BuildPalette()
        {
            var colors = new List<Color>();
            m_root.GetColors (colors);
            
            // Pad palette if needed
            while (colors.Count < m_maxColors && colors.Count < 256)
                colors.Add (Colors.Black);
            
            return new BitmapPalette (colors);
        }

        private int GetPaletteIndex (byte r, byte g, byte b, byte a)
        {
            return m_root.GetPaletteIndex (r, g, b, a, 0);
        }

        private class OctreeNode
        {
            private readonly OctreeNode[] m_children;
            private readonly PaletteQuantizer m_quantizer;
            private readonly int m_level;
            private bool m_isLeaf;
            private int m_pixelCount;
            private int m_redSum;
            private int m_greenSum;
            private int m_blueSum;
            private int m_alphaSum;
            private int m_paletteIndex;
            private static int s_nextPaletteIndex;

            public OctreeNode (int level, PaletteQuantizer quantizer)
            {
                m_level = level;
                m_quantizer = quantizer;
                m_children = new OctreeNode[8];
                
                if (level == 7)
                {
                    m_isLeaf = true;
                    m_quantizer.m_leafCount++;
                }
                else
                {
                    m_quantizer.m_reducibleNodes[level].Add (this);
                }
            }

            public void AddColor (byte r, byte g, byte b, byte a, int level)
            {
                if (m_isLeaf)
                {
                    m_pixelCount++;
                    m_redSum += r;
                    m_greenSum += g;
                    m_blueSum += b;
                    m_alphaSum += a;
                }
                else
                {
                    int shift = 7 - level;
                    int index = ((r >> shift) & 1) << 2 |
                               ((g >> shift) & 1) << 1 |
                               ((b >> shift) & 1);
                    
                    if (m_children[index] == null)
                    {
                        m_children[index] = new OctreeNode (level + 1, m_quantizer);
                    }
                    
                    m_children[index].AddColor (r, g, b, a, level + 1);
                }
            }

            public int Reduce()
            {
                int childCount = 0;
                
                for (int i = 0; i < 8; i++)
                {
                    if (m_children[i] != null)
                    {
                        m_pixelCount += m_children[i].m_pixelCount;
                        m_redSum += m_children[i].m_redSum;
                        m_greenSum += m_children[i].m_greenSum;
                        m_blueSum += m_children[i].m_blueSum;
                        m_alphaSum += m_children[i].m_alphaSum;
                        m_children[i] = null;
                        childCount++;
                    }
                }
                
                m_isLeaf = true;
                return childCount - 1;
            }

            public void GetColors (List<Color> colors)
            {
                if (m_isLeaf)
                {
                    if (m_pixelCount > 0)
                    {
                        byte r = (byte)(m_redSum / m_pixelCount);
                        byte g = (byte)(m_greenSum / m_pixelCount);
                        byte b = (byte)(m_blueSum / m_pixelCount);
                        byte a = (byte)(m_alphaSum / m_pixelCount);
                        colors.Add (Color.FromArgb (a, r, g, b));
                        m_paletteIndex = s_nextPaletteIndex++;
                    }
                }
                else
                {
                    for (int i = 0; i < 8; i++)
                    {
                        if (m_children[i] != null)
                            m_children[i].GetColors (colors);
                    }
                }
            }

            public int GetPaletteIndex (byte r, byte g, byte b, byte a, int level)
            {
                if (m_isLeaf)
                    return m_paletteIndex;
                
                int shift = 7 - level;
                int index = ((r >> shift) & 1) << 2 |
                           ((g >> shift) & 1) << 1 |
                           ((b >> shift) & 1);
                
                if (m_children[index] != null)
                    return m_children[index].GetPaletteIndex (r, g, b, a, level + 1);
                
                // Find closest child if exact match not found
                for (int i = 0; i < 8; i++)
                {
                    if (m_children[i] != null)
                        return m_children[i].GetPaletteIndex (r, g, b, a, level + 1);
                }
                
                return 0;
            }
        }
    }

    public class BitWriter : IDisposable
    {
        private Stream m_stream;
        private byte m_buffer;
        private int m_bitCount;
        private bool m_leaveOpen;

        public BitWriter (Stream output, bool leaveOpen = true)
        {
            m_stream = output;
            m_buffer = 0;
            m_bitCount = 0;
            m_leaveOpen = leaveOpen;
        }

        public void WriteBit (int bit)
        {
            WriteBits (bit, 1);
        }

        public void WriteBits (int value, int count)
        {
            while (count > 0)
            {
                int bitsToWrite = Math.Min (count, 8 - m_bitCount);
                int mask = (1 << bitsToWrite) - 1;
                int bits = (value >> (count - bitsToWrite)) & mask;
                
                m_buffer |= (byte)(bits << (8 - m_bitCount - bitsToWrite));
                m_bitCount += bitsToWrite;
                count -= bitsToWrite;
                
                if (m_bitCount == 8)
                {
                    m_stream.WriteByte (m_buffer);
                    m_buffer = 0;
                    m_bitCount = 0;
                }
            }
        }

        public void WriteByte (byte value)
        {
            WriteBits (value, 8);
        }

        public void WriteBytes (byte[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                WriteByte (buffer[offset + i]);
            }
        }

        public void Flush()
        {
            if (m_bitCount > 0)
            {
                m_stream.WriteByte (m_buffer);
                m_buffer = 0;
                m_bitCount = 0;
            }
            m_stream.Flush();
        }

        public void Dispose()
        {
            Flush();
            if (!m_leaveOpen)
                m_stream?.Dispose();
        }
    }

    public class MedianCutQuantizer
    {
        private readonly int m_maxColors;

        public MedianCutQuantizer (int maxColors)
        {
            m_maxColors = Math.Min (256, Math.Max (2, maxColors));
        }

        public BitmapSource Quantize (BitmapSource source)
        {
            if (source.Format != PixelFormats.Bgra32)
                source = new FormatConvertedBitmap (source, PixelFormats.Bgra32, null, 0);

            int width = source.PixelWidth;
            int height = source.PixelHeight;
            int stride = width * 4;
            var pixels = new byte[stride * height];
            source.CopyPixels (pixels, stride, 0);

            // Collect unique colors
            var colorSet = new HashSet<uint>();
            for (int i = 0; i < pixels.Length; i += 4)
            {
                uint color = (uint)(pixels[i] | (pixels[i + 1] << 8) | 
                                   (pixels[i + 2] << 16) | (pixels[i + 3] << 24));
                colorSet.Add (color);
            }

            // Convert to list for processing
            var uniqueColors = colorSet.Select (c => new ColorInfo {
                B = (byte)(c & 0xFF),
                G = (byte)((c >> 8) & 0xFF),
                R = (byte)((c >> 16) & 0xFF),
                A = (byte)((c >> 24) & 0xFF)
            }).ToList();

            // If already within palette size, use as-is
            if (uniqueColors.Count <= m_maxColors)
            {
                var directPalette = new BitmapPalette (uniqueColors.Select (
                    c => Color.FromArgb (c.A, c.R, c.G, c.B)).ToList());
                return CreateIndexedImage (source, directPalette);
            }

            // Perform median cut
            var boxes = new List<ColorBox> { new ColorBox (uniqueColors) };
            
            while (boxes.Count < m_maxColors)
            {
                var largestBox = boxes.OrderByDescending (b => b.Volume).First();
                if (!largestBox.CanSplit)
                    break;
                    
                boxes.Remove (largestBox);
                var (box1, box2) = largestBox.Split();
                boxes.Add (box1);
                boxes.Add (box2);
            }

            // Build palette from boxes
            var colors = boxes.Select (b => b.GetAverageColor()).ToList();
            while (colors.Count < m_maxColors && colors.Count < 256)
                colors.Add (Colors.Black);
                
            var palette = new BitmapPalette (colors);
            return CreateIndexedImage (source, palette);
        }

        private BitmapSource CreateIndexedImage (BitmapSource source, BitmapPalette palette)
        {
            int width = source.PixelWidth;
            int height = source.PixelHeight;
            int stride = width * 4;
            var pixels = new byte[stride * height];
            source.CopyPixels (pixels, stride, 0);

            var indexed = new byte[width * height];
            var paletteColors = palette.Colors.ToArray();
            
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int src = y * stride + x * 4;
                    int dst = y * width + x;
                    
                    indexed[dst] = (byte)FindClosestColor (
                        pixels[src + 2], pixels[src + 1], pixels[src], pixels[src + 3],
                        paletteColors);
                }
            }

            return BitmapSource.Create (width, height, 96, 96,
                PixelFormats.Indexed8, palette, indexed, width);
        }

        private int FindClosestColor (byte r, byte g, byte b, byte a, Color[] palette)
        {
            int bestIndex = 0;
            int bestDistance = int.MaxValue;
            
            for (int i = 0; i < palette.Length; i++)
            {
                int dr = r - palette[i].R;
                int dg = g - palette[i].G;
                int db = b - palette[i].B;
                int da = a - palette[i].A;
                int distance = dr * dr + dg * dg + db * db + da * da;
                
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }
            
            return bestIndex;
        }

        private class ColorInfo
        {
            public byte R, G, B, A;
        }

        private class ColorBox
        {
            private List<ColorInfo> m_colors;
            
            public int Volume { get; private set; }
            public bool CanSplit => m_colors.Count > 1;

            public ColorBox (List<ColorInfo> colors)
            {
                m_colors = colors;
                CalculateVolume();
            }

            private void CalculateVolume()
            {
                if (m_colors.Count == 0)
                {
                    Volume = 0;
                    return;
                }

                int minR = 255, maxR = 0;
                int minG = 255, maxG = 0;
                int minB = 255, maxB = 0;
                
                foreach (var c in m_colors)
                {
                    minR = Math.Min (minR, c.R);
                    maxR = Math.Max (maxR, c.R);
                    minG = Math.Min (minG, c.G);
                    maxG = Math.Max (maxG, c.G);
                    minB = Math.Min (minB, c.B);
                    maxB = Math.Max (maxB, c.B);
                }
                
                Volume = (maxR - minR) * (maxG - minG) * (maxB - minB);
            }

            public (ColorBox, ColorBox) Split()
            {
                // Find largest dimension
                int minR = 255, maxR = 0;
                int minG = 255, maxG = 0;
                int minB = 255, maxB = 0;
                
                foreach (var c in m_colors)
                {
                    minR = Math.Min (minR, c.R);
                    maxR = Math.Max (maxR, c.R);
                    minG = Math.Min (minG, c.G);
                    maxG = Math.Max (maxG, c.G);
                    minB = Math.Min (minB, c.B);
                    maxB = Math.Max (maxB, c.B);
                }
                
                int rangeR = maxR - minR;
                int rangeG = maxG - minG;
                int rangeB = maxB - minB;
                
                // Sort by largest dimension
                if (rangeR >= rangeG && rangeR >= rangeB)
                    m_colors.Sort ((a, b) => a.R.CompareTo (b.R));
                else if (rangeG >= rangeB)
                    m_colors.Sort ((a, b) => a.G.CompareTo (b.G));
                else
                    m_colors.Sort ((a, b) => a.B.CompareTo (b.B));
                
                // Split at median
                int mid = m_colors.Count / 2;
                var box1 = new ColorBox (m_colors.Take (mid).ToList());
                var box2 = new ColorBox (m_colors.Skip (mid).ToList());
                
                return (box1, box2);
            }

            public Color GetAverageColor()
            {
                if (m_colors.Count == 0)
                    return Colors.Black;
                    
                long sumR = 0, sumG = 0, sumB = 0, sumA = 0;
                foreach (var c in m_colors)
                {
                    sumR += c.R;
                    sumG += c.G;
                    sumB += c.B;
                    sumA += c.A;
                }
                
                return Color.FromArgb (
                    (byte)(sumA / m_colors.Count),
                    (byte)(sumR / m_colors.Count),
                    (byte)(sumG / m_colors.Count),
                    (byte)(sumB / m_colors.Count));
            }
        }
    }
}