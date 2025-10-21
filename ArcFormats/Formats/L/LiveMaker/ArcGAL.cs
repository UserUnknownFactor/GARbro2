using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;
using GameRes.Compression;
using Newtonsoft.Json;

namespace GameRes.Formats.LiveMaker
{
    public class GalLayerEntry : ImageEntry
    {
        public int FrameIndex;
        public int LayerIndex;
        public bool IsAlpha;
    }

    internal class FrameLayerInfo
    {
        public Entry Entry { get; set; }
        public Entry AlphaEntry { get; set; }
        public int Frame { get; set; }
        public int Layer { get; set; }
    }

    [Export (typeof (ArchiveFormat))]
    public class GalArchiveOpener : ArchiveFormat
    {
        public override string         Tag { get { return "GAL/ARC"; } }
        public override string Description { get { return "LiveMaker multi-frame image as archive"; } }
        public override uint     Signature { get { return  0x656C6147; } } // 'Gale'
        public override bool  IsHierarchic { get { return  false; } }
        public override bool      CanWrite { get { return  false; } }

        static readonly string METADATA_FILE = ".galmetadata";

        public override ArcFile TryOpen (ArcView file)
        {
            GalMetaData meta;
            List<GalArchiveReader.FrameInfo> frameInfos;

            using (var input = file.CreateStream ())
            {
                meta = ReadGalMetaData (input);
                if (meta == null)
                    return null;

                input.Position = 11;
                var header = new byte[meta.DataOffset - 11];
                input.Read (header, 0, header.Length);

                byte unknown2 = 0;
                byte unknown3 = 0;
                int unknown1 = 0;

                if (meta.Version > 102)
                {
                    unknown2 = header[0x14];
                    unknown3 = header[0x17];
                    if (meta.Version >= 106)
                        unknown1 = header.ToInt32 (0x24);
                }

                using (var reader = new GalArchiveReader (input, meta, 0))
                {
                    reader.ReadFrameStructure ();
                    frameInfos = reader.FrameInfos;
                }

                var dir = new List<Entry> ();

                var archiveMetadata = new GalArchiveMetadata
                {
                    Version = meta.Version,
                    Width = meta.Width,
                    Height = meta.Height,
                    BPP = meta.BPP,
                    Shuffled = meta.Shuffled,
                    Compression = meta.Compression,
                    Mask = meta.Mask,
                    BlockWidth = meta.BlockWidth,
                    BlockHeight = meta.BlockHeight,
                    Unknown2 = unknown2,
                    Unknown3 = unknown3,
                    Unknown1 = unknown1,
                    Frames = frameInfos.Select (f => new GalFrameMetadata {
                        FrameName = f.FrameName,
                        Width = f.Width,
                        Height = f.Height,
                        BPP = f.BPP,
                        FrameMask = f.FrameMask,
                        ReservedBytes = f.ReservedBytes,
                        FrameFooter = f.FrameFooter,
                        FrameFooterExtra = f.FrameFooterExtra,
                        Palette = f.Palette,
                        Layers = f.Layers.Select (l => new GalLayerMetadata {
                            LayerName = l.LayerName,
                            Left = l.Left,
                            Top = l.Top,
                            Visibility = l.Visibility,
                            TransColor = l.TransColor,
                            Alpha = l.Alpha,
                            AlphaOn = l.AlphaOn,
                            Lock = l.Lock,
                            HasAlpha = l.HasAlpha
                        }).ToList ()
                    }).ToList ()
                };

                for (int f = 0; f < frameInfos.Count; f++)
                {
                    for (int l = 0; l < frameInfos[f].Layers.Count; l++)
                    {
                        var layerInfo = frameInfos[f].Layers[l];
                        string extension = meta.Compression == 2 ? "jpg" : "png";

                        dir.Add (new GalLayerEntry {
                            Name = string.Format ("frame_{0:D2}_layer_{1:D2}.{2}", f, l, extension),
                            Type = "image",
                            Offset = 0,
                            Size = 0,
                            FrameIndex = f,
                            LayerIndex = l,
                            IsAlpha = false
                        });

                        if (layerInfo.HasAlpha)
                        {
                            dir.Add (new GalLayerEntry {
                                Name = string.Format ("frame_{0:D2}_layer_{1:D2}_alpha.png", f, l),
                                Type = "image",
                                Offset = 0,
                                Size = 0,
                                FrameIndex = f,
                                LayerIndex = l,
                                IsAlpha = true
                            });
                        }
                    }
                }

                string json = JsonConvert.SerializeObject (archiveMetadata, Formatting.Indented);
                var jsonBytes = Encoding.UTF8.GetBytes (json);

                dir.Insert (0, new MetadataEntry {
                    Name = METADATA_FILE,
                    Type = "script",
                    Offset = 0,
                    Size = (uint)jsonBytes.Length,
                    JsonContent = jsonBytes
                });

                return new GalArchive (file, this, dir, meta);
            }
        }

        private GalMetaData ReadGalMetaData (IBinaryStream stream)
        {
            stream.Position = 0;
            var header = new byte[0x30];
            if (11 != stream.Read (header, 0, 11))
                return null;

            uint sig = header.ToUInt32 (0);
            if (sig != Signature)
                return null;

            int version = header[4] * 100 + header[5] * 10 + header[6] - 5328;
            if (version < 100 || version > 107)
                return null;

            if (version > 102)
            {
                int header_size = header.ToInt32 (7);
                if (header_size < 0x28 || header_size > 0x100)
                    return null;
                if (header_size > header.Length)
                    header = new byte[header_size];
                if (header_size != stream.Read (header, 0, header_size))
                    return null;

                if (version != header.ToInt32 (0))
                    return null;

                return new GalMetaData
                {
                    Width       = header.ToUInt32 (4),
                    Height      = header.ToUInt32 (8),
                    BPP         = header.ToInt32 (0xC),
                    Version     = version,
                    FrameCount  = header.ToInt32 (0x10),
                    Shuffled    = header[0x15] != 0,
                    Compression = header[0x16],
                    Mask        = header.ToUInt32 (0x18),
                    BlockWidth  = header.ToInt32 (0x1C),
                    BlockHeight = header.ToInt32 (0x20),
                    DataOffset  = header_size + 11
                };
            }
            else
            {
                stream.Position = 0;
                stream.Read (header, 0, 0x10);
                uint name_length = stream.ReadUInt32 ();
                stream.ReadBytes ((int)name_length + 17);

                return new GalMetaData {
                    Width       = stream.ReadUInt32 (),
                    Height      = stream.ReadUInt32 (),
                    BPP         = stream.ReadInt32 (),
                    Version     = version,
                    FrameCount  = 1,
                    Mask        = header.ToUInt32 (0xC),
                    DataOffset  = 0x10,
                    Shuffled    = false,
                    Compression = -1,
                    BlockWidth  = 0,
                    BlockHeight = 0
                };
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Name == METADATA_FILE)
            {
                var metaEntry = entry as MetadataEntry;
                if (metaEntry != null && metaEntry.JsonContent != null)
                    return new MemoryStream (metaEntry.JsonContent);
            }

            var galArc = arc as GalArchive;
            var layerEntry = entry as GalLayerEntry;
            if (galArc == null || layerEntry == null)
                return base.OpenEntry (arc, entry);

            bool useJpeg = galArc.Metadata.Compression == 2;

            using (var input = arc.File.CreateStream ())
            {
                if (layerEntry.IsAlpha)
                {
                    using (var reader = new GalReader (input, galArc.Metadata, 0))
                    {
                        reader.UnpackAllFrames ();

                        var frame = reader.Frames[layerEntry.FrameIndex];
                        var layer = frame.Layers[layerEntry.LayerIndex];

                        if (layer.Alpha == null)
                            throw new InvalidOperationException ("Layer has no alpha channel");

                        var bitmap = BitmapSource.Create (
                            frame.Width, frame.Height,
                            96, 96,
                            PixelFormats.Gray8,
                            null,
                            layer.Alpha,
                            frame.AlphaStride);

                        bitmap.Freeze ();

                        var encoder = new PngBitmapEncoder ();
                        encoder.Frames.Add (BitmapFrame.Create (bitmap));

                        var ms = new MemoryStream ();
                        encoder.Save (ms);
                        ms.Position = 0;
                        return ms;
                    }
                }
                else if (useJpeg)
                {
                    var reader = new GalJpegExtractor (input, galArc.Metadata);
                    return reader.ExtractLayerAsJpeg (layerEntry.FrameIndex, layerEntry.LayerIndex);
                }
                else
                {
                    using (var reader = new GalReader (input, galArc.Metadata, 0))
                    {
                        reader.UnpackAllFrames ();

                        var frame = reader.Frames[layerEntry.FrameIndex];
                        var layer = frame.Layers[layerEntry.LayerIndex];

                        BitmapSource bitmap;
                        PixelFormat format;
                        BitmapPalette palette = null;

                        if (layer.Alpha != null)
                        {
                            var frameData = reader.FlattenFrame (frame);
                            bitmap = BitmapSource.Create (
                                frame.Width, frame.Height,
                                96, 96,
                                frameData.Format, frameData.Palette,
                                frameData.Data, frameData.Stride);
                        }
                        else
                        {
                            if (frame.BPP <= 8 && frame.Palette != null)
                            {
                                palette = new BitmapPalette (frame.Palette);
                                format = frame.BPP == 8 ? PixelFormats.Indexed8 : PixelFormats.Indexed4;
                            }
                            else if (frame.BPP == 16) format = PixelFormats.Bgr565;
                            else if (frame.BPP == 24) format = PixelFormats.Bgr24;
                            else                      format = PixelFormats.Bgr32;

                            bitmap = BitmapSource.Create (
                                frame.Width, frame.Height,
                                96, 96, format, palette,
                                layer.Pixels, frame.Stride);
                        }

                        bitmap.Freeze ();

                        var encoder = new PngBitmapEncoder ();
                        encoder.Frames.Add (BitmapFrame.Create (bitmap));

                        var ms = new MemoryStream ();
                        encoder.Save (ms);
                        ms.Position = 0;
                        return ms;
                    }
                }
            }
        }

        public override void Create (
            Stream output, IEnumerable<Entry> list, ResourceOptions options,
            EntryCallback callback)
        {
            var entries = list.Where (e => !e.Name.EndsWith (METADATA_FILE)).ToList ();

            GalArchiveMetadata metadata = null;
            var metadataFile = list.FirstOrDefault (e => e.Name.EndsWith (METADATA_FILE));
            if (metadataFile != null)
            {
                try
                {
                    using (var metaStream = File.OpenRead (metadataFile.Name))
                    using (var reader = new StreamReader (metaStream))
                    {
                        string json = reader.ReadToEnd ();
                        metadata = JsonConvert.DeserializeObject<GalArchiveMetadata> (json);
                    }
                }
                catch { }
            }

            if (metadata == null)
            {
                metadata = new GalArchiveMetadata {
                    Version = 107,
                    Width = 0,
                    Height = 0,
                    BPP = 32,
                    Shuffled = false,
                    Compression = 0,
                    Mask = 0xFFFFFFC0u,
                    BlockWidth = 16,
                    BlockHeight = 16,
                    Unknown2 = 1,
                    Unknown3 = 0,
                    Unknown1 = 0x00004600,
                    Frames = new List<GalFrameMetadata>()
                };

                GalFormat.ApplyGalSettings (metadata);
            }

            var frameGroups = new List<IGrouping<int, FrameLayerInfo>> ();
            var tempGroups = new Dictionary<string, FrameLayerInfo> ();

            foreach (var entry in entries)
            {
                var name = Path.GetFileNameWithoutExtension (entry.Name);

                if (name.EndsWith ("_alpha"))
                {
                    var baseName = name.Substring (0, name.Length - 6);
                    if (tempGroups.ContainsKey (baseName))
                        tempGroups[baseName].AlphaEntry = entry;
                }
                else
                {
                    var parts = name.Split ('_');
                    if (parts.Length >= 4 && parts[0] == "frame" && parts[2] == "layer")
                    {
                        var info = new FrameLayerInfo {
                            Entry = entry,
                            Frame = int.Parse (parts[1]),
                            Layer = int.Parse (parts[3])
                        };
                        tempGroups[name] = info;
                    }
                }
            }

            frameGroups = tempGroups.Values
                .GroupBy (x => x.Frame)
                .OrderBy (g => g.Key)
                .ToList ();

            WriteGalArchive (output, frameGroups, metadata, callback);
        }

        private void WriteGalArchive (
            Stream output,
            List<IGrouping<int, FrameLayerInfo>> frameGroups,
            GalArchiveMetadata metadata,
            EntryCallback callback)
        {
            using (var writer = new BinaryWriter (output, Encoding.ASCII, true))
            {
                writer.Write (Signature);
                writer.Write ((byte)('0' + metadata.Version / 100));
                writer.Write ((byte)('0' + (metadata.Version / 10) % 10));
                writer.Write ((byte)('0' + metadata.Version % 10));

                if (metadata.Version <= 102)
                {
                    throw new NotImplementedException ("GAL v102 archive creation not implemented");
                    //writer.Write (new byte[9]);
                    //writer.Write (metadata.Mask);
                    //writer.Write (0u);
                }
                else
                {
                    int headerSize = metadata.Version >= 107 ? 0x30 : 0x28;
                    writer.Write (headerSize);

                    writer.Write (metadata.Version);
                    writer.Write (metadata.Width);
                    writer.Write (metadata.Height);
                    writer.Write (metadata.BPP);
                    writer.Write (frameGroups.Count);
                    writer.Write (metadata.Unknown2);
                    writer.Write (metadata.Shuffled ? (byte)1 : (byte)0);
                    writer.Write ((byte)metadata.Compression);
                    writer.Write (metadata.Unknown3);
                    writer.Write (metadata.Mask);

                    if (metadata.Version >= 104)
                    {
                        writer.Write (metadata.BlockWidth);
                        writer.Write (metadata.BlockHeight);
                    }

                    if (metadata.Version >= 106)
                        writer.Write (metadata.Unknown1);

                    if (metadata.Version >= 107)
                        writer.Write ((ushort)0);
                }

                int current = 0;
                for (int frameIndex = 0; frameIndex < frameGroups.Count; frameIndex++)
                {
                    var frameGroup = frameGroups[frameIndex];
                    var frameMeta = frameGroup.Key < metadata.Frames.Count
                        ? metadata.Frames[frameGroup.Key]
                        : metadata.Frames[0];

                    var layers = frameGroup.OrderBy (x => x.Layer).ToList ();

                    WriteFrameData (writer, layers, frameMeta, metadata, callback, ref current);
                    WriteFrameFooter (writer, frameGroups, frameMeta, metadata, frameIndex);
                }
            }
        }

        private static void WriteFrameFooter (
            BinaryWriter writer, List<IGrouping<int, FrameLayerInfo>> frameGroups, 
            GalFrameMetadata frameMeta, GalArchiveMetadata metadata, int frameIndex)
        {
            if (metadata.Version == 105 && frameIndex < frameGroups.Count - 1)
            {
                if (frameMeta.FrameFooter != null && frameMeta.FrameFooter.Length >= 2)
                {
                    writer.Write (frameMeta.FrameFooter[0]);
                    writer.Write (frameMeta.FrameFooter[1]);
                }
                else
                {
                    writer.Write (0);
                    writer.Write (0);
                }
            }
            else if (metadata.Version >= 106)
            {
                if (frameMeta.FrameFooter != null && frameMeta.FrameFooter.Length >= 6)
                {
                    for (int i = 0; i < 6; i++)
                        writer.Write (frameMeta.FrameFooter[i]);

                    if (frameMeta.FrameFooter[1] == 3 &&
                        frameMeta.FrameFooterExtra != null)
                    {
                        writer.Write (frameMeta.FrameFooterExtra);
                    }
                }
                else
                {
                    writer.Write (0);
                    writer.Write (1);
                    writer.Write (0);
                    writer.Write (0);
                    writer.Write (frameMeta.Width);
                    writer.Write (frameMeta.Height);
                }
            }
        }

        private void WriteFrameData (BinaryWriter writer,
            List<FrameLayerInfo> layers,
            GalFrameMetadata frameMeta,
            GalArchiveMetadata globalMeta,
            EntryCallback callback,
            ref int current)
        {
            // Write frame header
            var frameNameBytes = Encoding.ASCII.GetBytes (frameMeta.FrameName ?? "");
            writer.Write ((uint)frameNameBytes.Length);
            if (frameNameBytes.Length > 0)
                writer.Write (frameNameBytes);

            writer.Write (frameMeta.FrameMask);
            writer.Write (frameMeta.ReservedBytes ?? new byte[9]);

            writer.Write (layers.Count);
            writer.Write (frameMeta.Width);
            writer.Write (frameMeta.Height);
            writer.Write (frameMeta.BPP);

            if (frameMeta.BPP <= 8)
            {
                if (frameMeta.Palette != null && frameMeta.Palette.Length == 1024)
                    writer.Write (frameMeta.Palette);
                else 
                { 
                    for (int i = 0; i < 256; i++)
                        writer.Write ((uint)0xFF000000);
                }
            }

            for (int l = 0; l < layers.Count; l++)
            {
                var layerInfo = layers[l];

                if (callback != null)
                    callback (++current, layerInfo.Entry, Localization._T ("MsgAddingFile"));

                var layerMeta = l < frameMeta.Layers.Count
                    ? frameMeta.Layers[l]
                    : new GalLayerMetadata ();

                writer.Write (layerMeta.Left);
                writer.Write (layerMeta.Top);
                writer.Write (layerMeta.Visibility);
                writer.Write (layerMeta.TransColor);
                writer.Write (layerMeta.Alpha);
                writer.Write (layerMeta.AlphaOn);

                var layerNameBytes = Encoding.ASCII.GetBytes (layerMeta.LayerName ?? "");
                writer.Write ((uint)layerNameBytes.Length);
                if (layerNameBytes.Length > 0)
                    writer.Write (layerNameBytes);

                if (globalMeta.Version >= 107)
                    writer.Write (layerMeta.Lock);

                // Handle JPEG files
                if (layerInfo.Entry.Name.EndsWith (".jpg", StringComparison.OrdinalIgnoreCase))
                {
                    var jpegData = File.ReadAllBytes (layerInfo.Entry.Name);
                    writer.Write (jpegData.Length);
                    writer.Write (jpegData);

                    if (layerMeta.Alpha != 0)
                    {
                        if (layerInfo.AlphaEntry != null)
                        {
                            var alphaData = GalFormat.LoadAlphaFromImage (layerInfo.AlphaEntry.Name);
                            GalFormat.WriteCompressedAlpha (
                                writer, alphaData, frameMeta.Width, frameMeta.Height,
                                globalMeta.BlockWidth, globalMeta.BlockHeight
                            );
                        }
                        else
                        {
                            GalFormat.WriteOpaqueAlpha (
                                writer, frameMeta.Width, frameMeta.Height,
                                globalMeta.BlockWidth, globalMeta.BlockHeight
                            );
                        }
                    }
                    else
                    {
                        writer.Write (0); // No alpha
                    }
                }
                // Handle PNG files
                else if (layerInfo.Entry.Name.EndsWith (".png", StringComparison.OrdinalIgnoreCase))
                {
                    using (var input = VFS.OpenBinaryStream (layerInfo.Entry))
                    {
                        var image = ImageFormat.Read (input);
                        if (image == null)
                            throw new InvalidFormatException ($"Unable to load image {layerInfo.Entry.Name}");

                        var bitmap = image.Bitmap;

                        if (globalMeta.Compression == 2)
                        {
                            // JPEG compression
                            var rgbBitmap = bitmap;
                            if (rgbBitmap.Format != PixelFormats.Bgr24)
                                rgbBitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgr24, null, 0);

                            int width = rgbBitmap.PixelWidth;
                            int height = rgbBitmap.PixelHeight;
                            int stride = width * 3;
                            var pixels = new byte[height * stride];
                            rgbBitmap.CopyPixels (pixels, stride, 0);

                            var tempMeta = new GalMetaData
                            {
                                Compression = globalMeta.Compression,
                                BlockWidth = globalMeta.BlockWidth,
                                BlockHeight = globalMeta.BlockHeight
                            };
                            GalFormat.WriteLayerData (
                                writer, pixels, tempMeta, width, height, 24, globalMeta.Shuffled, 0
                            );

                            if (layerMeta.Alpha != 0)
                            {
                                byte[] alphaData = null;

                                if (layerInfo.AlphaEntry != null)
                                {
                                    alphaData = GalFormat.LoadAlphaFromImage (layerInfo.AlphaEntry.Name);
                                }
                                else if (bitmap.Format == PixelFormats.Bgra32 || 
                                        bitmap.Format == PixelFormats.Pbgra32)
                                {
                                    alphaData = GalFormat.ExtractAlphaChannel (bitmap, bitmap.PixelWidth, bitmap.PixelHeight);
                                }
                                else
                                {
                                    int alphaStride = (bitmap.PixelWidth + 3) & ~3;
                                    alphaData = new byte[bitmap.PixelHeight * alphaStride];
                                    for (int i = 0; i < alphaData.Length; i++)
                                        alphaData[i] = 0xFF;
                                }

                                GalFormat.WriteCompressedAlpha (
                                    writer, alphaData, bitmap.PixelWidth, bitmap.PixelHeight,
                                    globalMeta.BlockWidth, globalMeta.BlockHeight
                                );
                            }
                            else
                            {
                                writer.Write (0); // No alpha
                            }
                        }
                        else
                        {
                            // Non-JPEG compression
                            var targetFormat = GalFormat.GetTargetPixelFormat (frameMeta.BPP);
                            var converted = bitmap;

                            byte[] separateAlpha = null;
                            if (layerInfo.AlphaEntry != null && layerMeta.Alpha != 0)
                            {
                                separateAlpha = GalFormat.LoadAlphaFromImage (layerInfo.AlphaEntry.Name);
                            }

                            if (converted.Format != targetFormat)
                                converted = new FormatConvertedBitmap (bitmap, targetFormat, null, 0);

                            int width = converted.PixelWidth;
                            int height = converted.PixelHeight;
                            int bytesPerPixel = (targetFormat.BitsPerPixel + 7) / 8;
                            int stride = (width * bytesPerPixel + 3) & ~3;
                            var pixels = new byte[height * stride];
                            converted.CopyPixels (pixels, stride, 0);

                            var tempMeta = new GalMetaData
                            {
                                Compression = globalMeta.Compression,
                                BlockWidth = globalMeta.BlockWidth,
                                BlockHeight = globalMeta.BlockHeight
                            };
                            GalFormat.WriteLayerData (
                                writer, pixels, tempMeta, width, height, frameMeta.BPP, globalMeta.Shuffled, 0
                            );

                            if (layerMeta.Alpha != 0 && frameMeta.BPP != 32)
                            {
                                byte[] alphaData = separateAlpha;

                                if (alphaData == null)
                                {
                                    if (bitmap.Format == PixelFormats.Bgra32 || bitmap.Format == PixelFormats.Pbgra32)
                                        alphaData = GalFormat.ExtractAlphaChannel (bitmap, width, height);
                                    else
                                    {
                                        int alphaStride = (width + 3) & ~3;
                                        alphaData = new byte[height * alphaStride];
                                        for (int i = 0; i < alphaData.Length; i++)
                                            alphaData[i] = 0xFF;
                                    }
                                }

                                GalFormat.WriteAlphaData (
                                    writer, alphaData, width, height,
                                    globalMeta.Compression, globalMeta.BlockWidth, globalMeta.BlockHeight,
                                    globalMeta.Shuffled, 0
                                );
                            }
                            else
                            {
                                writer.Write (0); // No separate alpha
                            }
                        }
                    }
                }
                else
                {
                    throw new InvalidFormatException ($"Unknown file format: {layerInfo.Entry.Name}");
                }
            }
        }
    }

    internal class GalArchive : ArcFile
    {
        public GalMetaData Metadata { get; set; }

        public GalArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, GalMetaData metadata)
            : base (arc, impl, dir)
        {
            Metadata = metadata;
        }
    }

    internal class MetadataEntry : Entry
    {
        public byte[] JsonContent { get; set; }
    }

    public class GalArchiveMetadata
    {
        public     int Version { get; set; }
        public      uint Width { get; set; }
        public     uint Height { get; set; }
        public         int BPP { get; set; }
        public   bool Shuffled { get; set; }
        public int Compression { get; set; }
        public       uint Mask { get; set; }
        public  int BlockWidth { get; set; }
        public int BlockHeight { get; set; }
        public   byte Unknown2 { get; set; }
        public   byte Unknown3 { get; set; }
        public    int Unknown1 { get; set; }

        public List<GalFrameMetadata> Frames { get; set; }
    }

    public class GalFrameMetadata
    {
        public        string FrameName { get; set; }
        public               int Width { get; set; }
        public              int Height { get; set; }
        public                 int BPP { get; set; }
        public          uint FrameMask { get; set; }
        public    byte[] ReservedBytes { get; set; }
        public       int[] FrameFooter { get; set; }
        public byte[] FrameFooterExtra { get; set; }
        public          byte[] Palette { get; set; }

        public List<GalLayerMetadata> Layers { get; set; }
    }

    public class GalLayerMetadata
    {
        public string LayerName { get; set; }
        public         int Left { get; set; }
        public          int Top { get; set; }
        public  byte Visibility { get; set; }
        public   int TransColor { get; set; }
        public        int Alpha { get; set; }
        public     byte AlphaOn { get; set; }
        public        byte Lock { get; set; }
        public    bool HasAlpha { get; set; }
    }

    internal class GalArchiveReader : GalReader
    {
        public List<FrameInfo> FrameInfos { get; private set; }

        public class FrameInfo
        {
            public  string FrameName;
            public     int Width, Height, BPP;
            public    uint FrameMask;
            public  byte[] ReservedBytes;
            public   int[] FrameFooter;
            public  byte[] FrameFooterExtra;
            public  byte[] Palette;

            public List<LayerInfo> Layers = new List<LayerInfo> ();
        }

        public class LayerInfo
        {
            public string LayerName;
            public    int Left, Top;
            public   byte Visibility;
            public    int TransColor, Alpha;
            public   byte AlphaOn, Lock;
            public   bool HasAlpha;
        }

        public GalArchiveReader (IBinaryStream input, GalMetaData info, uint key)
            : base (input, info, key)
        {
            FrameInfos = new List<FrameInfo> ();
        }

        public void ReadFrameStructure ()
        {
            m_input.Position = m_info.DataOffset;

            for (int frameIndex = 0; frameIndex < m_info.FrameCount; frameIndex++)
            {
                var frameInfo = new FrameInfo ();

                uint name_length = m_input.ReadUInt32 ();
                if (name_length > 0 && name_length < 256)
                {
                    var nameBytes = m_input.ReadBytes ((int)name_length);
                    frameInfo.FrameName = Encoding.ASCII.GetString (nameBytes);
                }
                else if (name_length > 0)
                    m_input.ReadBytes ((int)name_length);

                frameInfo.FrameMask     = m_input.ReadUInt32 ();
                frameInfo.ReservedBytes = m_input.ReadBytes (9);

                int layer_count  = m_input.ReadInt32 ();
                frameInfo.Width  = m_input.ReadInt32 ();
                frameInfo.Height = m_input.ReadInt32 ();
                frameInfo.BPP    = m_input.ReadInt32 ();

                if (frameInfo.BPP <= 8)
                {
                    frameInfo.Palette = new byte[1024];
                    m_input.Read (frameInfo.Palette, 0, 1024);
                }

                for (int i = 0; i < layer_count; ++i)
                {
                    var layerInfo = new LayerInfo
                    {
                        Left       = m_input.ReadInt32 (),
                        Top        = m_input.ReadInt32 (),
                        Visibility = m_input.ReadUInt8 (),
                        TransColor = m_input.ReadInt32 (),
                        Alpha      = m_input.ReadInt32 (),
                        AlphaOn    = m_input.ReadUInt8 ()
                    };

                    name_length = m_input.ReadUInt32 ();
                    if (name_length > 0 && name_length < 256)
                    {
                        var nameBytes = m_input.ReadBytes ((int)name_length);
                        layerInfo.LayerName = Encoding.ASCII.GetString (nameBytes);
                    }
                    else if (name_length > 0)
                        m_input.ReadBytes ((int)name_length);

                    if (m_info.Version >= 107)
                        layerInfo.Lock = m_input.ReadUInt8 ();

                    int layer_size = m_input.ReadInt32 ();
                    m_input.ReadBytes (layer_size);

                    int alpha_size = m_input.ReadInt32 ();
                    if (alpha_size != 0)
                    {
                        m_input.ReadBytes (alpha_size);
                        layerInfo.HasAlpha = true;
                    }

                    frameInfo.Layers.Add (layerInfo);
                }

                if (m_info.Version == 105)
                {
                    frameInfo.FrameFooter = new int[2];
                    frameInfo.FrameFooter[0] = m_input.ReadInt32 ();
                    frameInfo.FrameFooter[1] = m_input.ReadInt32 ();
                }
                else if (m_info.Version >= 106)
                {
                    frameInfo.FrameFooter = new int[6];
                    for (int i = 0; i < 6; i++)
                        frameInfo.FrameFooter[i] = m_input.ReadInt32 ();

                    if (frameInfo.FrameFooter[1] == 3 && m_input.Position + 32 <= m_input.Length)
                        frameInfo.FrameFooterExtra = m_input.ReadBytes (32);
                }

                FrameInfos.Add (frameInfo);
            }
        }
    }

    internal class GalJpegExtractor
    {
        private IBinaryStream m_input;
        private GalMetaData m_info;

        public GalJpegExtractor (IBinaryStream input, GalMetaData info)
        {
            m_input = input;
            m_info = info;
        }

        public Stream ExtractLayerAsJpeg (int frameIndex, int layerIndex)
        {
            m_input.Position = m_info.DataOffset;

            for (int f = 0; f <= frameIndex; f++)
            {
                uint name_length = m_input.ReadUInt32 ();
                if (name_length > 0)
                    m_input.ReadBytes ((int)name_length);

                m_input.ReadUInt32 ();
                m_input.ReadBytes (9);

                int layer_count = m_input.ReadInt32 ();
                int width       = m_input.ReadInt32 ();
                int height      = m_input.ReadInt32 ();
                int bpp         = m_input.ReadInt32 ();

                if (bpp <= 8)
                    m_input.ReadBytes (1024);

                for (int l = 0; l < layer_count; l++)
                {
                    m_input.ReadInt32 ();
                    m_input.ReadInt32 ();
                    m_input.ReadByte  ();
                    m_input.ReadInt32 ();
                    m_input.ReadInt32 ();
                    m_input.ReadByte  ();

                    name_length = m_input.ReadUInt32 ();
                    if (name_length > 0)
                        m_input.ReadBytes ((int)name_length);

                    if (m_info.Version >= 107)
                        m_input.ReadByte ();

                    int layer_size = m_input.ReadInt32 ();

                    if (f == frameIndex && l == layerIndex)
                    {
                        var jpegData = new byte[layer_size];
                        m_input.Read (jpegData, 0, layer_size);

                        var ms = new MemoryStream ();
                        ms.Write (jpegData, 0, jpegData.Length);
                        ms.Position = 0;
                        return ms;
                    }

                    m_input.ReadBytes (layer_size);

                    int alpha_size = m_input.ReadInt32 ();
                    if (alpha_size != 0)
                        m_input.ReadBytes (alpha_size);
                }

                if (f < frameIndex)
                {
                    if (m_info.Version == 105)
                    {
                        m_input.ReadInt32 ();
                        m_input.ReadInt32 ();
                    }
                    else if (m_info.Version >= 106)
                    {
                        int[] footer = new int[6];
                        for (int i = 0; i < 6; i++)
                            footer[i] = m_input.ReadInt32 ();

                        if (footer[1] == 3 && m_input.Position + 32 <= m_input.Length)
                            m_input.ReadBytes (32);
                    }
                }
            }

            throw new InvalidOperationException ("Layer not found");
        }
    }
}