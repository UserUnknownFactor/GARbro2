using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.LiveMaker
{
    internal abstract class SerializableBase
    {
        protected static void WriteString (BinaryWriter writer, string str)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes (str ?? "");
            writer.Write (bytes.Length);
            if (bytes.Length > 0)
                writer.Write (bytes);
        }

        protected static string ReadString (BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length <= 0) return "";
            var bytes = reader.ReadBytes (length);
            return System.Text.Encoding.UTF8.GetString (bytes);
        }
    }

    internal class GalMetaData : AnimationMetaData
    {
        public int    Version;
        public bool   Shuffled;
        public int    Compression;
        public uint   Mask;
        public int    BlockWidth;
        public int    BlockHeight;
        public int    Unknown1;
        public int    DataOffset;
        public byte   Unknown2;
        public byte[] ReservedBytes;
        public uint   FrameMask;
        public byte   Unknown3;

        public List<GalFrameInfo> Frames;

        public void Serialize (BinaryWriter writer)
        {
            writer.Write (Version);
            writer.Write (FrameCount);
            writer.Write (BPP);
            writer.Write (Shuffled);
            writer.Write (Compression);
            writer.Write (Mask);
            writer.Write (BlockWidth);
            writer.Write (BlockHeight);
            writer.Write (Unknown1);
            writer.Write ((int)Unknown2);
            writer.Write (Unknown3);
            writer.Write (FrameMask);

            writer.Write (ReservedBytes?.Length ?? 9);
            writer.Write (ReservedBytes ?? new byte[9]);

            writer.Write (Frames?.Count ?? 0);
            if (Frames != null)
            {
                foreach (var frame in Frames)
                    frame.Serialize (writer);
            }
        }

        public static GalMetaData Deserialize (
            BinaryReader reader, bool fromGalStream = false)
        {
            if (fromGalStream)
                return FromStream (reader);

            var meta = new GalMetaData
            {
                Version = reader.ReadInt32(),
                FrameCount = reader.ReadInt32(),
                BPP = reader.ReadInt32(),
                Shuffled = reader.ReadBoolean(),
                Compression = reader.ReadInt32(),
                Mask = reader.ReadUInt32(),
                BlockWidth = reader.ReadInt32(),
                BlockHeight = reader.ReadInt32(),
                Unknown1 = reader.ReadInt32(),
                Unknown2 = (byte)reader.ReadInt32(),
                Unknown3 = reader.ReadByte(),
                FrameMask = reader.ReadUInt32(),
                Frames = new List<GalFrameInfo>()
            };

            int reservedLen = reader.ReadInt32();
            meta.ReservedBytes = reader.ReadBytes (reservedLen);

            int frameCount = reader.ReadInt32();
            for (int i = 0; i < frameCount; i++)
                meta.Frames.Add (GalFrameInfo.Deserialize (reader));

            return meta;
        }

        private static GalMetaData FromStream (BinaryReader reader)
        {
            var stream = reader.BaseStream;
            stream.Position = 0;

            var header = new byte[0x30];
            if (11 != stream.Read (header, 0, 11))
                return null;

            uint sig = BitConverter.ToUInt32 (header, 0);
            if (sig != 0x656C6147) // 'Gale'
                return null;

            int version = header[4] * 100 + header[5] * 10 + header[6] - 5328;
            if (version < 100 || version > 107)
                return null;

            if (version > 102)
            {
                int header_size = BitConverter.ToInt32 (header, 7);
                if (header_size < 0x28 || header_size > 0x100)
                    return null;
                if (header_size > header.Length)
                    header = new byte[header_size];
                if (header_size != stream.Read (header, 0, header_size))
                    return null;

                if (version != BitConverter.ToInt32 (header, 0))
                    return null;

                return new GalMetaData {
                    Width       = BitConverter.ToUInt32 (header, 4  ),
                    Height      = BitConverter.ToUInt32 (header, 8  ),
                    BPP         = BitConverter.ToInt32  (header, 0xC),
                    Version     = version,
                    FrameCount  = BitConverter.ToInt32  (header, 0x10),
                    Unknown2    = header[0x14],
                    Shuffled    = header[0x15] != 0,
                    Compression = header[0x16],
                    Unknown3    = header[0x17],
                    Mask        = BitConverter.ToUInt32 (header, 0x18),
                    BlockWidth  = BitConverter.ToInt32  (header, 0x1C),
                    BlockHeight = BitConverter.ToInt32  (header, 0x20),
                    Unknown1    = BitConverter.ToInt32  (header, 0x24),
                    DataOffset  = header_size + 11,
                    Frames      = new List<GalFrameInfo>()
                };
            }
            else
            {
                // Handle legacy format
                stream.Position = 0;
                stream.Read (header, 0, 0x10);
                uint name_length = reader.ReadUInt32();
                stream.Seek (name_length+17, SeekOrigin.Current);
                uint width  = reader.ReadUInt32();
                uint height = reader.ReadUInt32();
                int  bpp    = reader.ReadInt32();

                return new GalMetaData {
                    Width   = width,
                    Height  = height,
                    BPP     = bpp,
                    Version = version,
                    FrameCount = 1,
                    Mask = BitConverter.ToUInt32 (header, 0xC),
                    DataOffset = 0x10,
                    Unknown2 = 0,
                    Frames = new List<GalFrameInfo>()
                };
            }
        }
    }

    internal class GalFrameInfo : SerializableBase
    {
        public string FrameName;
        public List<GalLayerInfo> Layers;
        public int Width;
        public int Height;
        public int BPP;

        public void Serialize (BinaryWriter writer)
        {
            WriteString (writer, FrameName ?? "");
            writer.Write (Width);
            writer.Write (Height);
            writer.Write (BPP);

            writer.Write (Layers?.Count ?? 0);
            if (Layers != null)
            {
                foreach (var layer in Layers)
                    layer.Serialize (writer);
            }
        }

        public static GalFrameInfo Deserialize (BinaryReader reader)
        {
            var frame = new GalFrameInfo
            {
                FrameName = ReadString (reader),
                Width = reader.ReadInt32(),
                Height = reader.ReadInt32(),
                BPP = reader.ReadInt32(),
                Layers = new List<GalLayerInfo>()
            };

            int layerCount = reader.ReadInt32();
            for (int i = 0; i < layerCount; i++)
                frame.Layers.Add (GalLayerInfo.Deserialize (reader));

            return frame;
        }
    }

    internal class GalLayerInfo : SerializableBase
    {
        public string LayerName;
        public int Left;
        public int Top;
        public byte Visibility;
        public int TransColor;
        public int Alpha;
        public byte AlphaOn;
        public byte Lock;
        public byte[] Pixels;
        public byte[] AlphaChannel;

        public void Serialize (BinaryWriter writer)
        {
            WriteString (writer, LayerName ?? "");
            writer.Write (Left);
            writer.Write (Top);
            writer.Write ((int)Visibility);
            writer.Write (TransColor);
            writer.Write (Alpha);
            writer.Write ((int)AlphaOn);
            writer.Write ((int)Lock);
        }

        public static GalLayerInfo Deserialize (BinaryReader reader)
        {
            return new GalLayerInfo
            {
                LayerName = ReadString (reader),
                Left = reader.ReadInt32(),
                Top = reader.ReadInt32(),
                Visibility = (byte)reader.ReadInt32(),
                TransColor = reader.ReadInt32(),
                Alpha = reader.ReadInt32(),
                AlphaOn = (byte)reader.ReadInt32(),
                Lock = (byte)reader.ReadInt32()
            };
        }

        public static GalLayerInfo FromStream (IBinaryStream input, int version)
        {
            var layerInfo = new GalLayerInfo
            {
                Left       = input.ReadInt32(),
                Top        = input.ReadInt32(),
                Visibility = input.ReadUInt8(),
                TransColor = input.ReadInt32(),
                Alpha      = input.ReadInt32(),
                AlphaOn    = input.ReadUInt8()
            };

            uint name_length = input.ReadUInt32();
            if (name_length > 0 && name_length < 256)
            {
                var nameBytes = input.ReadBytes ((int)name_length);
                layerInfo.LayerName = System.Text.Encoding.ASCII.GetString (nameBytes);
            }
            else
                input.Seek (name_length, SeekOrigin.Current);

            if (version >= 107)
                layerInfo.Lock = input.ReadUInt8();

            return layerInfo;
        }
    }

    internal class GalOptions : ResourceOptions
    {
        public uint Key;
    }

    [Serializable]
    public class GalScheme : ResourceScheme
    {
        public Dictionary<string, string> KnownKeys;
    }

    [Export(typeof(ImageFormat))]
    public class GalFormat : ImageFormat
    {
        public override string         Tag { get { return "GAL"; } }
        public override string Description { get { return "LiveMaker image format"; } }
        public override uint     Signature { get { return  0x656C6147; } } // 'Gale'
        public override bool      CanWrite { get { return  true; } }

        public GalFormat()
        {
            Extensions = new[] { "gal" };
            Settings = new[] { GalVersion, GalCompression };
        }

        GalScheme DefaultScheme = new GalScheme { KnownKeys = new Dictionary<string, string>() };

        public Dictionary<string, string> KnownKeys { get { return DefaultScheme.KnownKeys; } }

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (GalScheme)value; }
        }

        FixedSetSetting GalVersion = new FixedSetSetting (Properties.Settings.Default)
        {
            Name = "GALVersion",
            Text = "GAL Output Format",
            ValuesSet = new[] { 
                "GAL v107", 
                "GAL v106", 
                "GAL v105", 
                "GAL v104", 
                "GAL v103", 
                "GAL v102 (legacy)" 
            },
        };

        FixedSetSetting GalCompression = new FixedSetSetting (Properties.Settings.Default)
        {
            Name = "GALCompression",
            Text = "Compression Method",
            ValuesSet = new[] { 
                "None", 
                "zlib", 
                "JPEG (24/32bpp)" 
            },
        };

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            using (var reader = new BinaryReader (stream.AsStream, System.Text.Encoding.UTF8, true))
            {
                var meta = GalMetaData.Deserialize (reader, fromGalStream: true);
                return meta;
            }
        }

        uint? LastKey = null;

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (GalMetaData)info;
            uint key = 0;
            if (meta.Shuffled)
            {
                if (LastKey != null)
                    key = LastKey.Value;
                else
                    key = QueryKey();
            }
            try
            {
                using (var reader = new GalReaderWithMeta (stream, meta, key))
                {
                    reader.Unpack();
                    if (meta.Shuffled)
                        LastKey = key;

                    meta.FrameMask = reader.FrameMask;
                    meta.ReservedBytes = reader.ReservedBytes;

                    var frameInfo = new GalFrameInfo {
                        FrameName = reader.FrameName,
                        Width = reader.FrameWidth,
                        Height = reader.FrameHeight,
                        BPP = reader.FrameBPP,
                        Layers = new List<GalLayerInfo>()
                    };

                    foreach (var layer in reader.AllLayers)
                        frameInfo.Layers.Add (layer);

                    meta.Frames.Clear();
                    meta.Frames.Add (frameInfo);

                    return StoreMetadataInPng (reader.CreateImageData(), meta);
                }
            }
            catch
            {
                LastKey = null;
                throw;
            }
        }

        private ImageData StoreMetadataInPng (ImageData imageData, GalMetaData meta)
        {
            var pngImage = imageData as PngImageData ?? new PngImageData (imageData.Bitmap, meta);

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter (ms))
            {
                meta.Serialize (writer);
                pngImage.CustomChunks["gALh"] = ms.ToArray();
            }

            return pngImage;
        }

        static readonly byte[] DEFAULT_UNKNOWN4 = new byte[] { 0x11, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00 };

        public override void Write (Stream file, ImageData image)
        {
            GalMetaData galMeta = null;
            if (image is PngImageData pngData && pngData.CustomChunks.TryGetValue ("gALh", out var chunk))
            {
                using (var ms = new MemoryStream (chunk))
                using (var reader = new BinaryReader (ms))
                {
                    galMeta = GalMetaData.Deserialize (reader, fromGalStream: false);
                }
            }
            if (galMeta == null)
            {
                galMeta = new GalMetaData {
                    Version = 107,
                    Compression = 0,
                    Unknown2 = 1,
                    FrameMask = 0xFFFFFFFF,
                    ReservedBytes = DEFAULT_UNKNOWN4,
                    Mask = 0xFFFFFFC0u,
                    Unknown1 = 0x00004600,
                    BlockWidth = 16,
                    BlockHeight = 16,
                    BPP = image.Bitmap.Format.BitsPerPixel,
                    Frames = new List<GalFrameInfo>()
                };

                string selectedVersion = GalVersion.Get<string>();
                string selectedCompression = GalCompression.Get<string>();

                switch (selectedVersion)
                {
                    case "GAL v102 (legacy)": galMeta.Version = 102; break;
                    case "GAL v103": galMeta.Version = 103; break;
                    case "GAL v104": galMeta.Version = 104; break;
                    case "GAL v105": galMeta.Version = 105; break;
                    case "GAL v106": galMeta.Version = 106; break;
                    case "GAL v107": galMeta.Version = 107; break;
                }

                switch (selectedCompression)
                {
                    case "None": galMeta.Compression = -1; break;
                    case "zlib": galMeta.Compression = 0; break;
                    case "JPEG (24/32bpp)": galMeta.Compression = 2; break;
                }
            }

            WriteGal (file, image, galMeta);
        }

        private void WriteGal (Stream file, ImageData image, GalMetaData meta)
        {
            var bitmap = image.Bitmap;
            int bpp = meta.BPP > 0 ? meta.BPP : bitmap.Format.BitsPerPixel;
            if (bpp == 24)
            {
                if (bitmap.Format != PixelFormats.Bgr24)
                    bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgr24, null, 0);
            }
            else if (bpp == 32)
            {
                if (bitmap.Format != PixelFormats.Bgr32 && bitmap.Format != PixelFormats.Bgra32)
                    bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgr32, null, 0);
            }
            else if (bpp == 8)
            {
                if (bitmap.Format != PixelFormats.Indexed8)
                    bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Indexed8, null, 0);
            }

            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;
            int stride = (width * bpp / 8 + 3) & ~3;
            var pixels = new byte[height * stride];
            bitmap.CopyPixels (pixels, stride, 0);

            if (meta.Compression == 2 && bpp < 24)
                meta.Compression = 0;

            using (var writer = new BinaryWriter (file, System.Text.Encoding.ASCII, true))
            {
                writer.Write (Signature);

                int v1 = meta.Version / 100;
                int v2 = (meta.Version / 10) % 10;
                int v3 = meta.Version % 10;
                writer.Write ((byte)('0' + v1));
                writer.Write ((byte)('0' + v2));
                writer.Write ((byte)('0' + v3));

                if (meta.Version <= 102)
                    WriteOldFormat (writer, bitmap, pixels, stride);
                else
                    WriteNewFormat (writer, bitmap, pixels, stride, meta, bpp);
            }
        }

        private void WriteOldFormat (BinaryWriter writer, BitmapSource bitmap, byte[] pixels, int stride)
        {
            writer.Write (new byte[9]);
            writer.Write (0u);
            writer.Write (0u);
            writer.Write (new byte[17]);

            writer.Write ((uint)bitmap.PixelWidth);
            writer.Write ((uint)bitmap.PixelHeight);
            writer.Write (bitmap.Format.BitsPerPixel);

            if (bitmap.Format == PixelFormats.Indexed8 && bitmap.Palette != null)
                WriteColorMap (writer, bitmap.Palette);

            writer.Write (pixels);
        }

        private void WriteNewFormat (BinaryWriter writer, BitmapSource bitmap, byte[] pixels,
                   int stride, GalMetaData meta, int bpp)
        {
            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;

            int headerSize = 0x28;
            if (meta.Version >= 107)
                headerSize = 0x30;

            writer.Write (headerSize);

            writer.Write (meta.Version);
            writer.Write ((uint)width);
            writer.Write ((uint)height);
            writer.Write (bpp);
            writer.Write (1); // TODO: 1 frame, animations are unsupported
            writer.Write (meta.Unknown2);
            writer.Write (meta.Shuffled ? (byte)1 : (byte)0);
            writer.Write ((byte)meta.Compression);
            writer.Write (meta.Unknown3);
            writer.Write (meta.Mask);

            if (meta.Version >= 104)
            {
                writer.Write (meta.BlockWidth);
                writer.Write (meta.BlockHeight);
            }

            if (meta.Version >= 106)
                writer.Write (meta.Unknown1);

            if (meta.Version >= 107)
                writer.Write ((ushort)0);

            string frameName = "";
            string layerName = "";
            int layerLeft = 0, layerTop = 0;
            byte layerVisibility = 1;
            int transColor = -1;
            int layerAlpha = 0xFF;
            byte alphaOn = 0;
            byte layerLock = 0;

            if (meta.Frames != null && meta.Frames.Count > 0)
            {
                var frame = meta.Frames[0];
                frameName = frame.FrameName ?? "";

                if (frame.Layers != null && frame.Layers.Count > 0)
                {
                    var layer = frame.Layers[0];
                    layerName = layer.LayerName ?? "";
                    layerLeft = layer.Left;
                    layerTop = layer.Top;
                    layerVisibility = layer.Visibility;
                    transColor = layer.TransColor;
                    layerAlpha = layer.Alpha;
                    alphaOn = layer.AlphaOn;
                    layerLock = layer.Lock;
                }
            }

            var frameNameBytes = System.Text.Encoding.ASCII.GetBytes (frameName);
            writer.Write ((uint)frameNameBytes.Length);
            if (frameNameBytes.Length > 0)
                writer.Write (frameNameBytes);

            writer.Write (meta.FrameMask != 0 ? meta.FrameMask : 0xFFFFFFFF);

            if (meta.ReservedBytes != null && meta.ReservedBytes.Length == 9)
                writer.Write (meta.ReservedBytes);
            else
                writer.Write (DEFAULT_UNKNOWN4);

            writer.Write (1); // Layer count

            writer.Write (width);
            writer.Write (height);
            writer.Write (bpp);

            if (bpp == 8)
            {
                if (bitmap.Palette != null)
                    WriteColorMap (writer, bitmap.Palette);
                else
                    WriteGrayscalePalette (writer);
            }

            writer.Write (layerLeft);
            writer.Write (layerTop);
            writer.Write (layerVisibility);
            writer.Write (transColor);
            writer.Write (layerAlpha);
            writer.Write ((byte)alphaOn);

            var layerNameBytes = System.Text.Encoding.ASCII.GetBytes (layerName);
            writer.Write ((uint)layerNameBytes.Length);
            if (layerNameBytes.Length > 0)
                writer.Write (layerNameBytes);

            if (meta.Version >= 107)
                writer.Write (layerLock);

            WriteLayerData (writer, pixels, meta, width, height, bpp);

            // Alpha channel size (0 if no alpha)
            writer.Write (0);

            writer.Write (0);
            writer.Write (1);
            writer.Write (0);
            writer.Write (0);
            writer.Write ((uint)width);
            writer.Write ((uint)height);
        }

        internal static void WriteLayerData (BinaryWriter writer, byte[] pixels, GalMetaData meta, int width, int height, int bpp)
        {
            if (meta.Compression == 0)  // zlib compression
            {
                using (var uncompressed = new MemoryStream())
                using (var tempWriter = new BinaryWriter (uncompressed))
                {
                    if (meta.BlockWidth <= 0 || meta.BlockHeight <= 0)
                        tempWriter.Write (pixels);
                    else
                    {
                        int blocks_w = (width + meta.BlockWidth - 1) / meta.BlockWidth;
                        int blocks_h = (height + meta.BlockHeight - 1) / meta.BlockHeight;
                        int blocks_count = blocks_w * blocks_h;

                        // Write block references (all -1 for new data)
                        for (int i = 0; i < blocks_count; i++)
                        {
                            tempWriter.Write (-1);  // frame_ref = -1 means raw data follows
                            tempWriter.Write (0);   // layer_ref (unused)
                        }

                        // Write pixel data block by block
                        int stride = (width * bpp / 8 + 3) & ~3;
                        for (int y = 0; y < height; y += meta.BlockHeight)
                        {
                            int h = Math.Min (meta.BlockHeight, height - y);
                            for (int x = 0; x < width; x += meta.BlockWidth)
                            {
                                int w = Math.Min (meta.BlockWidth, width - x);
                                int chunk_size = (w * bpp + 7) / 8;

                                for (int j = 0; j < h; j++)
                                {
                                    int src = (y + j) * stride + (x * bpp + 7) / 8;
                                    tempWriter.Write (pixels, src, chunk_size);
                                }
                            }
                        }
                    }

                    uncompressed.Position = 0;
                    using (var compressed = new MemoryStream())
                    {
                        using (var zlib = new ZLibStream (compressed, CompressionMode.Compress, CompressionLevel.BestSpeed))
                        {
                            uncompressed.CopyTo (zlib);
                        }
                        var compressedData = compressed.ToArray();
                        writer.Write (compressedData.Length);
                        writer.Write (compressedData);
                    }
                }
            }
            else if (meta.Compression == 2)  // JPEG
            {
                using (var ms = new MemoryStream())
                {
                    var bitmapSource = BitmapSource.Create(
                        width, height, 96, 96,
                        PixelFormats.Bgr24, null,
                        pixels, width * 3);

                    var encoder = new JpegBitmapEncoder();
                    encoder.QualityLevel = 95;
                    encoder.Frames.Add (BitmapFrame.Create (bitmapSource));
                    encoder.Save (ms);

                    var jpegData = ms.ToArray();
                    writer.Write (jpegData.Length);
                    writer.Write (jpegData);
                }
            }
            else  // No compression (-1)
            {
                // Write uncompressed block structure
                if (meta.BlockWidth <= 0 || meta.BlockHeight <= 0)
                {
                    writer.Write (pixels.Length);
                    writer.Write (pixels);
                }
                else
                {
                    int blocks_w = (width + meta.BlockWidth - 1) / meta.BlockWidth;
                    int blocks_h = (height + meta.BlockHeight - 1) / meta.BlockHeight;
                    int blocks_count = blocks_w * blocks_h;

                    using (var ms = new MemoryStream())
                    using (var msWriter = new BinaryWriter (ms))
                    {
                        // Write block references
                        for (int i = 0; i < blocks_count; i++)
                        {
                            msWriter.Write(-1);
                            msWriter.Write (0);
                        }

                        // Write pixel data block by block
                        int stride = (width * bpp / 8 + 3) & ~3;
                        for (int y = 0; y < height; y += meta.BlockHeight)
                        {
                            int h = Math.Min (meta.BlockHeight, height - y);
                            for (int x = 0; x < width; x += meta.BlockWidth)
                            {
                                int w = Math.Min (meta.BlockWidth, width - x);
                                int chunk_size = (w * bpp + 7) / 8;

                                for (int j = 0; j < h; j++)
                                {
                                    int src = (y + j) * stride + (x * bpp + 7) / 8;
                                    msWriter.Write (pixels, src, chunk_size);
                                }
                            }
                        }

                        var data = ms.ToArray();
                        writer.Write (data.Length);
                        writer.Write (data);
                    }
                }
            }
        }

        private static void WriteColorMap (BinaryWriter writer, BitmapPalette palette)
        {
            foreach (var color in palette.Colors)
            {
                writer.Write (color.B);
                writer.Write (color.G);
                writer.Write (color.R);
                writer.Write (color.A);
            }
            for (int i = palette.Colors.Count; i < 256; i++)
                writer.Write ((uint)0xFF000000);
        }

        private static void WriteGrayscalePalette (BinaryWriter writer)
        {
            for (int i = 0; i < 256; i++)
            {
                writer.Write ((byte)i);
                writer.Write ((byte)i);
                writer.Write ((byte)i);
                writer.Write ((byte)0xFF);
            }
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new GalOptions { Key = KeyFromString (Properties.Settings.Default.GALKey) };
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetGAL (KnownKeys);
        }

        internal uint QueryKey ()
        {
            if (!KnownKeys.Any())
                return 0;
            var options = Query<GalOptions> (Localization._T ("ArcImageEncrypted"));
            return options.Key;
        }

        public static uint KeyFromString (string key)
        {
            if (string.IsNullOrWhiteSpace (key) || key.Length < 4)
                return 0;
            return (uint)(key[0] | key[1] << 8 | key[2] << 16 | key[3] << 24);
        }
    }

    internal class GalReaderWithMeta : GalReader
    {
        public             string FrameName { get; private set; }
        public               int FrameWidth { get; private set; }
        public              int FrameHeight { get; private set; }
        public                 int FrameBPP { get; private set; }
        public               uint FrameMask { get; private set; }
        public         byte[] ReservedBytes { get; private set; }
        public List<GalLayerInfo> AllLayers { get; private set; }

        public GalReaderWithMeta (IBinaryStream input, GalMetaData info, uint key) 
            : base (input, info, key)
        {
            AllLayers = new List<GalLayerInfo>();
        }

        public ImageData CreateImageData()
        {
            return ImageData.Create (m_info, Format, Palette, Data, Stride);
        }

        public new void Unpack()
        {
            m_input.Position = m_info.DataOffset;
            uint name_length = m_input.ReadUInt32();

            if (name_length > 0 && name_length < 256)
            {
                var nameBytes = m_input.ReadBytes ((int)name_length);
                FrameName = System.Text.Encoding.ASCII.GetString (nameBytes);
            }
            else
                m_input.Seek (name_length, SeekOrigin.Current);

            FrameMask = m_input.ReadUInt32();
            ReservedBytes = m_input.ReadBytes (9);

            int layer_count = m_input.ReadInt32();
            if (layer_count < 1)
                throw new InvalidFormatException();

            var frame = new Frame (layer_count);
            FrameWidth = frame.Width = m_input.ReadInt32();
            FrameHeight = frame.Height = m_input.ReadInt32();
            FrameBPP = frame.BPP = m_input.ReadInt32();
            if (frame.BPP <= 0)
                throw new InvalidFormatException();
            if (frame.BPP <= 8)
                frame.Palette = ImageFormat.ReadColorMap (m_input.AsStream, 1 << frame.BPP);
            frame.SetStride();
            m_frames.Add (frame);

            for (int i = 0; i < layer_count; ++i)
            {
                var layerInfo = GalLayerInfo.FromStream (m_input, m_info.Version);

                var layer = new Layer();
                int layer_size = m_input.ReadInt32();
                layer.Pixels = layerInfo.Pixels = UnpackLayer (frame, layer_size);
                int alpha_size = m_input.ReadInt32();
                if (alpha_size != 0)
                {
                    layer.Alpha = layerInfo.AlphaChannel = UnpackLayer (frame, alpha_size, true);
                }

                frame.Layers.Add (layer);
                AllLayers.Add (layerInfo);
            }

            Flatten (0);
        }
    }

    internal class GalReader : IDisposable
    {
        protected IBinaryStream   m_input;
        protected GalMetaData     m_info;
        protected byte[]          m_output;
        protected List<Frame>     m_frames;
        protected uint            m_key;

        public byte[]           Data { get { return m_output; } }
        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }
        public int            Stride { get; private set; }

        public GalReader (IBinaryStream input, GalMetaData info, uint key)
        {
            m_info = info;
            if (m_info.Compression < 0 || m_info.Compression > 2)
                throw new InvalidFormatException();
            m_frames = new List<Frame> (m_info.FrameCount);
            m_key = key;
            m_input = input;
        }

        internal class Frame
        {
            public         int Width;
            public         int Height;
            public         int BPP;
            public         int Stride;
            public         int AlphaStride;
            public List<Layer> Layers;
            public     Color[] Palette;

            public Frame (int layer_count)
            {
                Layers = new List<Layer> (layer_count);
            }

            public void SetStride ()
            {
                Stride = (Width * BPP + 7) / 8;
                AlphaStride = (Width + 3) & ~3;
                if (BPP >= 8)
                    Stride = (Stride + 3) & ~3;
            }
        }

        internal class Layer
        {
            public byte[]   Pixels;
            public byte[]   Alpha;
        }

        public void Unpack ()
        {
            m_input.Position = m_info.DataOffset;
            uint name_length = m_input.ReadUInt32();
            m_input.Seek (name_length, SeekOrigin.Current);
            uint mask = m_input.ReadUInt32();
            m_input.Seek (9, SeekOrigin.Current);
            int layer_count = m_input.ReadInt32();
            if (layer_count < 1)
                throw new InvalidFormatException();

            // XXX only first frame is interpreted
            var frame = new Frame (layer_count);
            frame.Width  = m_input.ReadInt32();
            frame.Height = m_input.ReadInt32();
            frame.BPP    = m_input.ReadInt32();
            if (frame.BPP <= 0)
                throw new InvalidFormatException();
            if (frame.BPP <= 8)
                frame.Palette = ImageFormat.ReadColorMap (m_input.AsStream, 1 << frame.BPP);
            frame.SetStride();
            m_frames.Add (frame);
            for (int i = 0; i < layer_count; ++i)
            {
                m_input.ReadInt32();    // left
                m_input.ReadInt32();    // top
                m_input.ReadByte();     // visibility
                m_input.ReadInt32();    // (-1) TransColor
                m_input.ReadInt32();    // (0xFF) alpha
                m_input.ReadByte();     // AlphaOn
                name_length = m_input.ReadUInt32();
                m_input.Seek (name_length, SeekOrigin.Current);
                if (m_info.Version >= 107)
                    m_input.ReadByte(); // lock
                var layer = new Layer();
                int layer_size = m_input.ReadInt32();
                layer.Pixels = UnpackLayer (frame, layer_size);
                int alpha_size = m_input.ReadInt32();
                if (alpha_size != 0)
                    layer.Alpha = UnpackLayer (frame, alpha_size, true);
                frame.Layers.Add (layer);
            }
            Flatten (0);
        }

        protected byte[] UnpackLayer (Frame frame, int length, bool is_alpha = false)
        {
            if (m_info.Version < 103)
                return m_input.ReadBytes (length);
            var layer_start = m_input.Position;
            var layer_end = layer_start + length;
            var packed = new StreamRegion (m_input.AsStream, layer_start, length, true);
            try
            {
                if (0 == m_info.Compression || 2 == m_info.Compression && is_alpha)
                    return ReadZlib (frame, packed, is_alpha);
                if (2 == m_info.Compression)
                    return ReadJpeg (frame, packed);
                return ReadBlocks (frame, packed, is_alpha);
            }
            finally
            {
                packed.Dispose();
                m_input.Position = layer_end;
            }
        }

        byte[] ReadBlocks (Frame frame, Stream packed, bool is_alpha)
        {
            if (m_info.BlockWidth <= 0 || m_info.BlockHeight <= 0)
                return ReadRaw (frame, packed, is_alpha);
            int blocks_w = (frame.Width  + m_info.BlockWidth  - 1) / m_info.BlockWidth;
            int blocks_h = (frame.Height + m_info.BlockHeight - 1) / m_info.BlockHeight;
            int blocks_count = blocks_w * blocks_h;
            var data = new byte[blocks_count * 8];
            packed.Read (data, 0, data.Length);
            var refs = new int[blocks_count * 2];
            Buffer.BlockCopy (data, 0, refs, 0, data.Length);
            if (m_info.Shuffled)
                ShuffleBlocks (refs, blocks_count);

            int bpp = is_alpha ? 8 : frame.BPP;
            int stride = is_alpha ? frame.AlphaStride : frame.Stride;
            var pixels = new byte[stride * frame.Height];
            int i = 0;
            for (int y = 0; y < frame.Height; y += m_info.BlockHeight)
            {
                int height = Math.Min (m_info.BlockHeight, frame.Height - y);
                for (int x = 0; x < frame.Width; x += m_info.BlockWidth)
                {
                    int dst = y * stride + (x * bpp + 7) / 8;
                    int width = Math.Min (m_info.BlockWidth, frame.Width - x);
                    int chunk_size = (width * bpp + 7) / 8;
                    if (-1 == refs[i])
                    {
                        for (int j = 0; j < height; ++j)
                        {
                            packed.Read (pixels, dst, chunk_size);
                            dst += stride;
                        }
                    }
                    else if (-2 == refs[i])
                    {
                        int src_x = m_info.BlockWidth  * (refs[i+1] % blocks_w);
                        int src_y = m_info.BlockHeight * (refs[i+1] / blocks_w);
                        int src = src_y * stride + (src_x * bpp + 7) / 8;
                        for (int j = 0; j < height; ++j)
                        {
                            Buffer.BlockCopy (pixels, src, pixels, dst, chunk_size);
                            src += stride;
                            dst += stride;
                        }
                    }
                    else
                    {
                        int frame_ref = refs[i];
                        int layer_ref = refs[i+1];
                        if (frame_ref >= m_frames.Count || layer_ref >= m_frames[frame_ref].Layers.Count)
                            throw new InvalidFormatException();
                        var layer = m_frames[frame_ref].Layers[layer_ref];
                        byte[] src = is_alpha ? layer.Alpha : layer.Pixels;
                        for (int j = 0; j < height; ++j)
                        {
                            Buffer.BlockCopy (src, dst, pixels, dst, chunk_size);
                            dst += stride;
                        }
                    }
                    i += 2;
                }
            }
            return pixels;
        }

        byte[] ReadRaw (Frame frame, Stream packed, bool is_alpha)
        {
            int stride = is_alpha ? frame.AlphaStride : frame.Stride;
            var pixels = new byte[frame.Height * stride];
            if (m_info.Shuffled)
            {
                foreach (var dst in RandomSequence (frame.Height, m_key))
                    packed.Read (pixels, dst*stride, stride);
            }
            else
                packed.Read (pixels, 0, pixels.Length);

            return pixels;
        }

        byte[] ReadZlib (Frame frame, Stream packed, bool is_alpha)
        {
            using (var zs = new ZLibStream (packed, CompressionMode.Decompress))
                return ReadBlocks (frame, zs, is_alpha);
        }

        byte[] ReadJpeg (Frame frame, Stream packed)
        {
            var decoder = new JpegBitmapDecoder (packed, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var bitmap = decoder.Frames[0];
            frame.BPP = bitmap.Format.BitsPerPixel;
            int stride = bitmap.PixelWidth * bitmap.Format.BitsPerPixel / 8;
            var pixels = new byte[bitmap.PixelHeight * stride];
            bitmap.CopyPixels (pixels, stride, 0);
            frame.Stride = stride;
            return pixels;
        }

        protected void Flatten (int frame_num)
        {
            // XXX only first layer is considered.

            var frame = m_frames[frame_num];
            var layer = frame.Layers[0];
            if (null == layer.Alpha)
            {
                m_output = layer.Pixels;
                if (null != frame.Palette)
                    Palette = new BitmapPalette (frame.Palette);
                if (8 == frame.BPP)       Format = PixelFormats.Indexed8;
                else if (16 == frame.BPP) Format = PixelFormats.Bgr565;
                else if (24 == frame.BPP) Format = PixelFormats.Bgr24;
                else if (32 == frame.BPP) Format = PixelFormats.Bgr32;
                else if (4  == frame.BPP) Format = PixelFormats.Indexed4;
                else
                    throw new NotSupportedException();
                Stride = frame.Stride;
            }
            else
            {
                m_output = new byte[frame.Width * frame.Height * 4];
                switch (frame.BPP)
                {
                case 4:  Flatten4bpp  (frame, layer); break;
                case 8:  Flatten8bpp  (frame, layer); break;
                case 16: Flatten16bpp (frame, layer); break;
                case 24: Flatten24bpp (frame, layer); break;
                case 32: Flatten32bpp (frame, layer); break;
                default: throw new NotSupportedException ("Not supported color depth");
                }
                Format = PixelFormats.Bgra32;
                Stride = frame.Width * 4;
            }
        }

        void Flatten4bpp (Frame frame, Layer layer)
        {
            int dst = 0;
            int src = 0;
            int a   = 0;
            for (int y = 0; y < frame.Height; ++y)
            {
                for (int x = 0; x < frame.Width; ++x)
                {
                    byte pixel = layer.Pixels[src + x/2];
                    int index = 0 == (x & 1) ? (pixel & 0xF) : (pixel >> 4);
                    var color = frame.Palette[index];
                    m_output[dst++] = color.B;
                    m_output[dst++] = color.G;
                    m_output[dst++] = color.R;
                    m_output[dst++] = layer.Alpha[a+x];
                }
                src += frame.Stride;
                a += frame.AlphaStride;
            }
        }

        void Flatten8bpp (Frame frame, Layer layer)
        {
            int dst = 0;
            int src = 0;
            int a   = 0;
            for (int y = 0; y < frame.Height; ++y)
            {
                for (int x = 0; x < frame.Width; ++x)
                {
                    var color = frame.Palette[ layer.Pixels[src+x] ];
                    m_output[dst++] = color.B;
                    m_output[dst++] = color.G;
                    m_output[dst++] = color.R;
                    m_output[dst++] = layer.Alpha[a+x];
                }
                src += frame.Stride;
                a += frame.AlphaStride;
            }
        }

        void Flatten16bpp (Frame frame, Layer layer)
        {
            int src = 0;
            int dst = 0;
            int a   = 0;
            for (int y = 0; y < frame.Height; ++y)
            {
                for (int x = 0; x < frame.Width; ++x)
                {
                    int pixel = LittleEndian.ToUInt16 (layer.Pixels, src + x*2);
                    m_output[dst++] = (byte)((pixel & 0x001F) * 0xFF / 0x001F);
                    m_output[dst++] = (byte)((pixel & 0x07E0) * 0xFF / 0x07E0);
                    m_output[dst++] = (byte)((pixel & 0xF800) * 0xFF / 0xF800);
                    m_output[dst++] = layer.Alpha[a+x];
                }
                src += frame.Stride;
                a += frame.AlphaStride;
            }
        }

        void Flatten24bpp (Frame frame, Layer layer)
        {
            int src = 0;
            int dst = 0;
            int a   = 0;
            int gap = frame.Stride - frame.Width * 3;
            for (int y = 0; y < frame.Height; ++y)
            {
                for (int x = 0; x < frame.Width; ++x)
                {
                    m_output[dst++] = layer.Pixels[src++];
                    m_output[dst++] = layer.Pixels[src++];
                    m_output[dst++] = layer.Pixels[src++];
                    m_output[dst++] = layer.Alpha[a+x];
                }
                src += gap;
                a += frame.AlphaStride;
            }
        }

        void Flatten32bpp (Frame frame, Layer layer)
        {
            int src = 0;
            int dst = 0;
            int a   = 0;
            for (int y = 0; y < frame.Height; ++y)
            {
                for (int x = 0; x < frame.Width; ++x)
                {
                    m_output[dst++] = layer.Pixels[src];
                    m_output[dst++] = layer.Pixels[src+1];
                    m_output[dst++] = layer.Pixels[src+2];
                    m_output[dst++] = layer.Alpha[a+x];
                    src += 4;
                }
                a += frame.AlphaStride;
            }
        }

        void ShuffleBlocks (int[] refs, int count)
        {
            var copy = refs.Clone() as int[];
            int src = 0;
            foreach (var index in RandomSequence (count, m_key))
            {
                refs[index*2]   = copy[src++];
                refs[index*2+1] = copy[src++];
            }
        }

        static IEnumerable<int> RandomSequence (int count, uint seed)
        {
            var tp = new TpRandom (seed);
            var order = Enumerable.Range (0, count).ToList<int>();
            for (int i = 0; i < count; ++i)
            {
                int n = (int)(tp.GetRand32() % (uint)order.Count);
                yield return order[n];
                order.RemoveAt (n);
            }
        }

        #region IDisposable Members
        bool m_disposed = false;

        public void Dispose ()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (!m_disposed)
                m_disposed = true;
        }
        #endregion
    }
}
