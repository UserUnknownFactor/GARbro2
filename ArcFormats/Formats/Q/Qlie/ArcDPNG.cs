using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;
using Newtonsoft.Json;

namespace GameRes.Formats.Qlie
{
    public class DpngTileEntry : ImageEntry
    {
        public int PosX;
        public int PosY;
        public int TileWidth;
        public int TileHeight;
        public int TileIndex;
    }

    [Export(typeof(ArchiveFormat))]
    public class DpngArchiveOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DPNG/ARC"; } }
        public override string Description { get { return "QLIE tiled image archive"; } }
        public override uint     Signature { get { return  0x474E5044; } } // 'DPNG'
        public override bool  IsHierarchic { get { return  false; } }
        public override bool      CanWrite { get { return  true; } }

        static readonly string METADATA_FILE = ".dpngmetadata";

        public DpngArchiveOpener()
        {
            Extensions = new string[] { "dpng", "png" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            var header = file.View.ReadBytes (0, 0x14);
            if (!header.AsciiEqual (0, "DPNG"))
                return null;

            int tileCount = header.ToInt32 (8);
            if (!IsSaneCount (tileCount))
                return null;

            uint canvasWidth = header.ToUInt32 (0xC);
            uint canvasHeight = header.ToUInt32 (0x10);

            if (canvasWidth == 0 || canvasHeight == 0 || canvasWidth > 16000 || canvasHeight > 16000)
                return null;

            var dir = new List<Entry>();
            uint currentOffset = 0x14;

            var metadata = new DpngMetadata
            {
                CanvasWidth = canvasWidth,
                CanvasHeight = canvasHeight,
                Entries = new List<DpngTileMetadata>()
            };

            for (int i = 0; i < tileCount; ++i)
            {
                if (currentOffset + 0x1C > file.MaxOffset)
                    return null;

                int x      = file.View.ReadInt32  (currentOffset);
                int y      = file.View.ReadInt32  (currentOffset + 4);
                int width  = file.View.ReadInt32  (currentOffset + 8);
                int height = file.View.ReadInt32  (currentOffset + 12);
                uint size  = file.View.ReadUInt32 (currentOffset + 16);
                // Skip unknown fields (8 bytes)
                currentOffset += 0x1C;

                if (size == 0 || currentOffset + size > file.MaxOffset)
                {
                    currentOffset += size;
                    continue;
                }

                var entry = new DpngTileEntry {
                    Name = $"tile_{i:D4}_{x}_{y}.png",
                    Type = "image",
                    Offset = currentOffset,
                    Size = size,
                    PosX = x,
                    PosY = y,
                    TileWidth = width,
                    TileHeight = height,
                    TileIndex = i
                };

                dir.Add (entry);

                metadata.Entries.Add (new DpngTileMetadata {
                    Index = i,
                    Name = Path.GetFileNameWithoutExtension (entry.Name),
                    PosX = x,
                    PosY = y,
                    Width = width,
                    Height = height
                });

                currentOffset += size;
            }

            if (dir.Count == 0)
                return null;

            // Add metadata as first entry
            string json = JsonConvert.SerializeObject (metadata, Formatting.Indented);
            var jsonBytes = Encoding.UTF8.GetBytes (json);

            dir.Insert (0, new MetadataEntry {
                Name = METADATA_FILE,
                Type = "script",
                Offset = 0,
                Size = (uint)jsonBytes.Length,
                JsonContent = jsonBytes
            });

            return new DpngArchive (file, this, dir, metadata);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Name.EndsWith (METADATA_FILE))
            {
                var metaEntry = entry as MetadataEntry;
                if (metaEntry != null && metaEntry.JsonContent != null)
                    return new MemoryStream (metaEntry.JsonContent);
            }
            return base.OpenEntry (arc, entry);
        }

        public override void Create (
            Stream output, IEnumerable<Entry> list, ResourceOptions options,
            EntryCallback callback)
        {
            var entries = list.Where (e => !e.Name.EndsWith (METADATA_FILE)).ToList();

            DpngMetadata metadata = null;
            var metadataFile = list.FirstOrDefault (e => e.Name.EndsWith (METADATA_FILE));
            if (metadataFile != null)
            {
                try
                {
                    using (var metaStream = File.OpenRead (metadataFile.Name))
                    using (var reader = new StreamReader (metaStream))
                    {
                        string json = reader.ReadToEnd();
                        metadata = JsonConvert.DeserializeObject<DpngMetadata>(json);
                    }
                }
                catch { }
            }

            uint canvasWidth = 800, canvasHeight = 600;
            var tileMetadataList = new List<DpngTileMetadata>();

            if (metadata != null)
            {
                canvasWidth = metadata.CanvasWidth;
                canvasHeight = metadata.CanvasHeight;

                // Sort entries based on metadata order
                if (metadata.Entries != null && metadata.Entries.Count > 0)
                {
                    var orderDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < metadata.Entries.Count; i++)
                        orderDict[metadata.Entries[i].Name] = i;

                    entries.Sort((a, b) =>
                    {
                        var nameA = Path.GetFileNameWithoutExtension (a.Name);
                        var nameB = Path.GetFileNameWithoutExtension (b.Name);

                        int orderA = orderDict.TryGetValue (nameA, out int posA) ? posA : int.MaxValue;
                        int orderB = orderDict.TryGetValue (nameB, out int posB) ? posB : int.MaxValue;

                        return orderA.CompareTo (orderB);
                    });
                }
            }
            else
            {
                var dpngOptions = GetOptions<DpngOptions>(options);
                if (dpngOptions != null)
                {
                    canvasWidth = dpngOptions.CanvasWidth;
                    canvasHeight = dpngOptions.CanvasHeight;
                }
            }

            // Write DPNG header
            using (var writer = new BinaryWriter (output, Encoding.ASCII, true))
            {
                writer.Write (new byte[] { (byte)'D', (byte)'P', (byte)'N', (byte)'G' });
                writer.Write((uint)0); // Unknown field
                writer.Write (entries.Count);
                writer.Write (canvasWidth);
                writer.Write (canvasHeight);

                int tileIndex = 0;
                foreach (var entry in entries)
                {
                    if (callback != null)
                        callback (entries.Count, entry, Localization._T ("MsgAddingFile"));

                    var nameWithoutExt = Path.GetFileNameWithoutExtension (entry.Name);
                    DpngTileMetadata tileMeta = null;

                    // Try to get metadata
                    if (metadata?.Entries != null)
                    {
                        tileMeta = metadata.Entries.FirstOrDefault (e =>
                            e.Name.Equals (nameWithoutExt, StringComparison.OrdinalIgnoreCase));
                    }

                    // If no metadata, try to parse from filename (tile_0000_x_y)
                    if (tileMeta == null)
                    {
                        tileMeta = new DpngTileMetadata
                        {
                            Index = tileIndex,
                            Name = nameWithoutExt,
                            PosX = 0,
                            PosY = 0
                        };

                        var parts = nameWithoutExt.Split('_');
                        if (parts.Length >= 4 && parts[0] == "tile")
                        {
                            int x, y;
                            if (int.TryParse (parts[2], out x) && int.TryParse (parts[3], out y))
                            {
                                tileMeta.PosX = x;
                                tileMeta.PosY = y;
                            }
                        }
                    }

                    // Read PNG to get dimensions if not in metadata
                    byte[] pngData;
                    using (var input = File.OpenRead (entry.Name))
                    {
                        pngData = new byte[input.Length];
                        input.Read (pngData, 0, pngData.Length);
                    }

                    if (tileMeta.Width == 0 || tileMeta.Height == 0)
                    {
                        try
                        {
                            using (var ms = new MemoryStream (pngData))
                            {
                                var decoder = new PngBitmapDecoder (ms,
                                    BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                                if (decoder.Frames.Count > 0)
                                {
                                    tileMeta.Width = decoder.Frames[0].PixelWidth;
                                    tileMeta.Height = decoder.Frames[0].PixelHeight;
                                }
                            }
                        }
                        catch
                        {
                            tileMeta.Width = 1;
                            tileMeta.Height = 1;
                        }
                    }

                    // Write tile header
                    writer.Write (tileMeta.PosX);
                    writer.Write (tileMeta.PosY);
                    writer.Write (tileMeta.Width);
                    writer.Write (tileMeta.Height);
                    writer.Write ((uint)pngData.Length);
                    writer.Write ((uint)0); // Unknown field 1
                    writer.Write ((uint)0); // Unknown field 2

                    // Write PNG data
                    writer.Write (pngData);

                    tileIndex++;
                }
            }
        }

        public override ResourceOptions GetDefaultOptions()
        {
            return new DpngOptions {
                CanvasWidth = 1920,
                CanvasHeight = 1080
            };
        }

        public override object GetCreationWidget()
        {
            return new GUI.CreateDpngWidget();
        }
    }

    internal class DpngArchive : ArcFile
    {
        public DpngMetadata Metadata { get; set; }

        public DpngArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, DpngMetadata metadata)
            : base(arc, impl, dir)
        {
            Metadata = metadata;
        }
    }

    internal class MetadataEntry : Entry
    {
        public byte[] JsonContent { get; set; }
    }

    public class DpngMetadata
    {
        public               uint CanvasWidth { get; set; }
        public              uint CanvasHeight { get; set; }
        public List<DpngTileMetadata> Entries { get; set; }
    }

    public class DpngTileMetadata
    {
        public   int Index { get; set; }
        public string Name { get; set; }
        public    int PosX { get; set; }
        public    int PosY { get; set; }
        public   int Width { get; set; }
        public  int Height { get; set; }
    }

    public class DpngOptions : ResourceOptions
    {
        public  uint CanvasWidth { get; set; }
        public uint CanvasHeight { get; set; }
    }
}