using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using GameRes.Compression;
using GameRes.Utility;
using Newtonsoft.Json;

namespace GameRes.Formats.LiveMaker
{
    internal class GalMetaData : AnimationMetaData
    {
        public  int Version;
        public bool Shuffled;
        public  int Compression;
        public uint Mask;
        public  int BlockWidth;
        public  int BlockHeight;
        public  int DataOffset;
        public byte Unknown2;
        public byte Unknown3;
        public  int Unknown1;
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

    [Serializable]
    public class GalWriteMetadata
    {
        public             int Version { get; set; }
        public         int Compression { get; set; }
        public          int BlockWidth { get; set; }
        public         int BlockHeight { get; set; }
        public               uint Mask { get; set; }
        public           bool Shuffled { get; set; }
        public                uint Key { get; set; }
        public        string FrameName { get; set; }
        public        string LayerName { get; set; }
        public            int FrameBPP { get; set; }
        public           bool HasAlpha { get; set; }
        public       int[] FrameFooter { get; set; }
        public byte[] FrameFooterExtra { get; set; }
        public           byte Unknown2 { get; set; }
        public           byte Unknown3 { get; set; }
        public            int Unknown1 { get; set; }
        public          uint FrameMask { get; set; }
        public    byte[] ReservedBytes { get; set; }
    }

    [Export (typeof (ImageFormat))]
    public class GalFormat : ImageFormat
    {
        public override string         Tag { get { return "GAL"; } }
        public override string Description { get { return "LiveMaker multi-frame image format"; } }
        public override uint     Signature { get { return  0x656C6147; } } // 'Gale'
        public override bool      CanWrite { get { return  false; } }

        public GalFormat ()
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

        uint? LastKey = null;

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            return ReadGalMetaData (stream);
        }

        internal static GalMetaData ReadGalMetaData (IBinaryStream stream)
        {
            stream.Position = 0;

            var header = new byte[0x30];
            if (11 != stream.Read (header, 0, 11))
                return null;

            uint sig = header.ToUInt32 (0);
            if (sig != 0x656C6147) // 'Gale'
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

                var meta = new GalMetaData
                {
                    Width       = header.ToUInt32 (4),
                    Height      = header.ToUInt32 (8),
                    BPP         = header.ToInt32  (0xC),
                    Version     = version,
                    FrameCount  = header.ToInt32  (0x10),
                    Unknown2    = header[0x14],
                    Shuffled    = header[0x15] != 0,
                    Compression = header[0x16],
                    Unknown3    = header[0x17],
                    Mask        = header.ToUInt32 (0x18),
                    BlockWidth  = header.ToInt32  (0x1C),
                    BlockHeight = header.ToInt32  (0x20),
                    DataOffset  = header_size + 11
                };

                if (version >= 106)
                    meta.Unknown1 = header.ToInt32 (0x24);

                return meta;
            }
            else
            {
                stream.Position = 0;
                stream.Read (header, 0, 0x10);
                uint name_length = stream.ReadUInt32();
                stream.ReadBytes ((int)name_length + 17);
                uint width = stream.ReadUInt32();
                uint height = stream.ReadUInt32();
                int bpp = stream.ReadInt32();

                return new GalMetaData
                {
                    Width = width,
                    Height = height,
                    BPP = bpp,
                    Version = version,
                    FrameCount = 1,
                    Mask = header.ToUInt32 (0xC),
                    DataOffset = 0x10,
                    Shuffled = false,
                    Compression = -1,
                    BlockWidth = 0,
                    BlockHeight = 0,
                    Unknown2 = 0,
                    Unknown3 = 0,
                    Unknown1 = 0
                };
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (GalMetaData)info;
            uint key = 0;
            if (meta.Shuffled)
            {
                key = (LastKey != null) ? LastKey.Value : QueryKey();
            }

            try
            {
                using (var reader = new GalReader (stream, meta, key))
                {
                    reader.UnpackAllFrames();

                    if (meta.Shuffled)
                        LastKey = key;

                    if (reader.Frames.Count == 1)
                    {
                        // Single frame - store metadata for round-trip conversion
                        var imageData = ImageData.Create (info, reader.Format, reader.Palette, reader.Data, reader.Stride);
                        return StoreMetadata (imageData, meta, reader.Frames[0], key);
                    }
                    else
                    {
                        // Multiple frames - return as animation without metadata since it's handled in Arc version
                        var bitmapFrames = new List<BitmapSource>();
                        for (int i = 0; i < reader.AllFrameData.Count; i++)
                        {
                            var frameData = reader.AllFrameData[i];
                            var frame = reader.Frames[i];

                            var bitmap = BitmapSource.Create (
                                frame.Width, frame.Height,
                                96, 96,
                                frameData.Format,
                                frameData.Palette,
                                frameData.Data,
                                frameData.Stride);
                            bitmap.Freeze();
                            bitmapFrames.Add (bitmap);
                        }

                        var delays = Enumerable.Repeat (100, bitmapFrames.Count).ToList();
                        return new AnimatedImageData (bitmapFrames, delays, info);
                    }
                }
            }
            catch
            {
                LastKey = null;
                throw;
            }
        }

        private ImageData StoreMetadata (ImageData imageData, GalMetaData meta, GalReader.Frame frame, uint key)
        {
            var pngImage = imageData as PngImageData ?? new PngImageData (imageData.Bitmap, meta);

            var writeMetadata = new GalWriteMetadata {
                Version = meta.Version,
                Compression = meta.Compression,
                BlockWidth = meta.BlockWidth,
                BlockHeight = meta.BlockHeight,
                Mask = meta.Mask,
                Shuffled = meta.Shuffled,
                Key = key,
                FrameName = frame.Name ?? "Frame1",
                LayerName = frame.Layers.Count > 0 ? (frame.Layers[0].Name ?? "Layer1") : "Layer1",
                FrameBPP = frame.BPP,
                HasAlpha = frame.Layers.Count > 0 && frame.Layers[0].Alpha != null,
                FrameFooter = frame.Footer,
                FrameFooterExtra = frame.FooterExtra,
                Unknown2 = meta.Unknown2,
                Unknown3 = meta.Unknown3,
                Unknown1 = meta.Unknown1,
                FrameMask = frame.FrameMask,
                ReservedBytes = frame.ReservedBytes
            };

            string json = JsonConvert.SerializeObject (writeMetadata);
            pngImage.CustomChunks["gaLm"] = Encoding.UTF8.GetBytes (json);

            return pngImage;
        }

        public override void Write (Stream file, ImageData image)
        {
            GalWriteMetadata metadata = null;
            if (image is PngImageData pngData)
            {
                if (pngData.CustomChunks.TryGetValue ("gaLm", out var metaChunk))
                {
                    try
                    {
                        string json = Encoding.UTF8.GetString (metaChunk);
                        metadata = JsonConvert.DeserializeObject<GalWriteMetadata> (json);
                    }
                    catch { }
                }
            }

            if (metadata == null)
            {


                int version = 107;
                int compression = 0;

                string galCompression = Properties.Settings.Default.GALCompression ?? "zlib";
                string galVersion = Properties.Settings.Default.GALVersion ?? "GAL v107";
                switch (galVersion)
                {
                    case "GAL v102 (legacy)": version = 102; break;
                    case "GAL v103": version = 103; break;
                    case "GAL v104": version = 104; break;
                    case "GAL v105": version = 105; break;
                    case "GAL v106": version = 106; break;
                    case "GAL v107": version = 107; break;
                }

                switch (galCompression)
                {
                    case "None":            compression = -1; break;
                    case "JPEG (24/32bpp)": compression =  2; break;
                    case "zlib":            compression =  0; break;
                }

                int blockWidth = 16;
                int blockHeight = 16;
                if (version <= 102)
                {
                    blockWidth = 0;
                    blockHeight = 0;
                }

                metadata = new GalWriteMetadata
                {
                    Version     = version,
                    Compression = compression,
                    BlockWidth  = blockWidth,
                    BlockHeight = blockHeight,
                    Mask        = 0xFFFFFFC0u,
                    Shuffled    = false,
                    Key         = 0,
                    FrameName   = "Frame1",
                    LayerName   = "Layer1",
                    FrameBPP    = 24,
                    HasAlpha    = false
                };

                var bitmap = image.Bitmap;
                if (bitmap.Format == PixelFormats.Bgra32 || bitmap.Format == PixelFormats.Pbgra32)
                {
                    metadata.FrameBPP = 32;
                    metadata.HasAlpha = true;
                }
            }

            WriteSingleFrameGal (file, image, metadata);
        }

        private void WriteSingleFrameGal (
            Stream output, ImageData image, GalWriteMetadata metadata)
        {
            using (var writer = new BinaryWriter (output, Encoding.ASCII, true))
            {
                var bitmap = image.Bitmap;
                bool hasAlpha = bitmap.Format == PixelFormats.Bgra32 ||
                               bitmap.Format == PixelFormats.Pbgra32 ||
                               (bitmap.Format.BitsPerPixel == 32 && metadata.HasAlpha);

                int targetBpp = metadata.FrameBPP > 0 ? metadata.FrameBPP : (hasAlpha ? 32 : 24);

                writer.Write (Signature);
                writer.Write ((byte)('0' + metadata.Version / 100));
                writer.Write ((byte)('0' + (metadata.Version / 10) % 10));
                writer.Write ((byte)('0' + metadata.Version % 10));

                if (metadata.Version <= 102)
                {
                    // Legacy format
                    writer.Write (new byte[9]);
                    writer.Write (metadata.Mask);
                    writer.Write (0u);
                    writer.Write (new byte[17]);

                    writer.Write ((uint)image.Width);
                    writer.Write ((uint)image.Height);
                    writer.Write (targetBpp);

                    if (targetBpp == 8 && bitmap.Palette != null)
                    {
                        foreach (var color in bitmap.Palette.Colors)
                        {
                            writer.Write (color.B);
                            writer.Write (color.G);
                            writer.Write (color.R);
                            writer.Write (color.A);
                        }
                        for (int i = bitmap.Palette.Colors.Count; i < 256; i++)
                            writer.Write ((uint)0xFF000000);
                    }

                    byte[] pixels = GetPixelsForBpp (bitmap, targetBpp);
                    writer.Write (pixels);
                }
                else
                {
                    int headerSize = metadata.Version >= 107 ? 0x30 : 0x28;
                    writer.Write (headerSize);

                    writer.Write (metadata.Version);
                    writer.Write ((uint)image.Width);
                    writer.Write ((uint)image.Height);
                    writer.Write (targetBpp);
                    writer.Write (1); // frame count
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

                    WriteFrame (writer, image, metadata, targetBpp);
                }
            }
        }

        private void WriteFrame (BinaryWriter writer, ImageData image, GalWriteMetadata metadata, int targetBpp)
        {
            // Frame header
            var frameNameBytes = Encoding.ASCII.GetBytes (metadata.FrameName ?? "Frame1");
            writer.Write ((uint)frameNameBytes.Length);
            if (frameNameBytes.Length > 0)
                writer.Write (frameNameBytes);

            writer.Write (metadata.FrameMask != 0 ? metadata.FrameMask : 0xFFFFFFFF);

            if (metadata.ReservedBytes != null && metadata.ReservedBytes.Length == 9)
                writer.Write (metadata.ReservedBytes);
            else
                writer.Write (new byte[] { 0x11, 0, 0, 0, 0x02, 0, 0, 0, 0 });

            writer.Write (1); // layer count
            writer.Write ((int)image.Width);
            writer.Write ((int)image.Height);
            writer.Write (targetBpp);

            // Palette for 8-bit images
            if (targetBpp <= 8)
            {
                for (int i = 0; i < 256; i++)
                    writer.Write ((uint)0xFF000000);
            }

            // Layer header
            writer.Write (0);       // left
            writer.Write (0);       // top
            writer.Write ((byte)1); // visibility
            writer.Write (-1);      // TransColor
            writer.Write (metadata.HasAlpha ? 0xFFFFFFFF : 0x000000FF); // alpha
            writer.Write ((byte)(metadata.HasAlpha ? 1 : 0)); // AlphaOn

            var layerNameBytes = Encoding.ASCII.GetBytes (metadata.LayerName ?? "Layer1");
            writer.Write ((uint)layerNameBytes.Length);
            if (layerNameBytes.Length > 0)
                writer.Write (layerNameBytes);

            if (metadata.Version >= 107)
                writer.Write ((byte)0); // lock

            var bitmap = image.Bitmap;
            bool sourceHasAlpha = bitmap.Format == PixelFormats.Bgra32 ||
                                  bitmap.Format == PixelFormats.Pbgra32;

            // Handle JPEG compression
            if (metadata.Compression == 2)
            {
                WriteJpegLayer (writer, bitmap);

                if (metadata.HasAlpha)
                {
                    if (sourceHasAlpha)
                    {
                        var alphaData = ExtractAlphaChannel (bitmap, bitmap.PixelWidth, bitmap.PixelHeight);
                        WriteCompressedAlpha (
                            writer, alphaData, (int)image.Width, (int)image.Height,
                            metadata.BlockWidth, metadata.BlockHeight
                        );
                    }
                    else
                    {
                        WriteOpaqueAlpha (
                            writer, (int)image.Width, (int)image.Height,
                            metadata.BlockWidth, metadata.BlockHeight
                        );
                    }
                }
                else
                    writer.Write (0); // No alpha channel
            }
            else
            {
                // Non-JPEG compression
                byte[] pixels = GetPixelsForBpp (bitmap, targetBpp);
                int stride = ((int)image.Width * (targetBpp / 8) + 3) & ~3;

                var tempMeta = new GalMetaData
                {
                    Compression = metadata.Compression,
                    BlockWidth = metadata.BlockWidth,
                    BlockHeight = metadata.BlockHeight
                };
                WriteLayerData (
                    writer, pixels, tempMeta, (int)image.Width, (int)image.Height,
                    targetBpp, metadata.Shuffled, metadata.Key
                );

                // Write alpha if needed and not embedded in 32-bit data
                if (metadata.HasAlpha && targetBpp != 32)
                {
                    if (sourceHasAlpha)
                    {
                        var alphaData = ExtractAlphaChannel (bitmap, bitmap.PixelWidth, bitmap.PixelHeight);
                        WriteAlphaData (
                            writer, alphaData, (int)image.Width, (int)image.Height,
                            metadata.Compression, metadata.BlockWidth, metadata.BlockHeight,
                            metadata.Shuffled, metadata.Key
                        );
                    }
                    else
                    {
                        WriteOpaqueAlpha (writer, (int)image.Width, (int)image.Height,
                                        metadata.BlockWidth, metadata.BlockHeight);
                    }
                }
                else
                {
                    writer.Write (0); // No separate alpha
                }
            }

            // Write frame footer based on version
            if (metadata.Version == 105)
            {
                if (metadata.FrameFooter != null && metadata.FrameFooter.Length >= 2)
                {
                    writer.Write (metadata.FrameFooter[0]);
                    writer.Write (metadata.FrameFooter[1]);
                }
                else
                {
                    writer.Write (0);
                    writer.Write (0);
                }
            }
            else if (metadata.Version >= 106)
            {
                if (metadata.FrameFooter != null && metadata.FrameFooter.Length >= 6)
                {
                    for (int i = 0; i < 6; i++)
                        writer.Write (metadata.FrameFooter[i]);

                    if (metadata.FrameFooter[1] == 3 &&
                        metadata.FrameFooterExtra != null &&
                        metadata.FrameFooterExtra.Length == 32)
                    {
                        writer.Write (metadata.FrameFooterExtra);
                    }
                }
                else
                {
                    writer.Write (0);
                    writer.Write (1); // this seem important
                    writer.Write (0);
                    writer.Write (0);
                    writer.Write ((int)image.Width);
                    writer.Write ((int)image.Height);
                }
            }
            // No footer for versions < 105
        }

        private void WriteJpegLayer (BinaryWriter writer, BitmapSource bitmap)
        {
            var rgbBitmap = bitmap;
            if (rgbBitmap.Format != PixelFormats.Bgr24)
                rgbBitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgr24, null, 0);

            var jpegEncoder = new JpegBitmapEncoder();
            jpegEncoder.QualityLevel = 95;
            jpegEncoder.Frames.Add (BitmapFrame.Create (rgbBitmap));

            using (var jpegStream = new MemoryStream())
            {
                jpegEncoder.Save (jpegStream);
                var jpegData = jpegStream.ToArray();
                writer.Write (jpegData.Length);
                writer.Write (jpegData);
            }
        }

        internal static byte[] GetPixelsForBpp (BitmapSource bitmap, int targetBpp)
        {
            PixelFormat targetFormat;
            switch (targetBpp)
            {
                case 8:  targetFormat = PixelFormats.Indexed8; break;
                case 16: targetFormat = PixelFormats.Bgr565;   break;
                case 24: targetFormat = PixelFormats.Bgr24;    break;
                case 32: targetFormat = PixelFormats.Bgra32;   break;
                default: targetFormat = PixelFormats.Bgr24;    break;
            }

            var converted = bitmap;
            if (converted.Format != targetFormat)
                converted = new FormatConvertedBitmap (bitmap, targetFormat, null, 0);

            int bytesPerPixel = (targetBpp + 7) / 8;
            int stride = ((int)converted.PixelWidth * bytesPerPixel + 3) & ~3;
            var pixels = new byte[(int)converted.PixelHeight * stride];
            converted.CopyPixels (pixels, stride, 0);

            return pixels;
        }

        internal static PixelFormat GetTargetPixelFormat (int bpp)
        {
            switch (bpp)
            {
                case 8:  return PixelFormats.Indexed8;
                case 16: return PixelFormats.Bgr565;
                case 24: return PixelFormats.Bgr24;
                case 32: return PixelFormats.Bgra32;
                default: return PixelFormats.Bgr24;
            }
        }

        internal static byte[] ExtractAlphaChannel (BitmapSource bitmap, int width, int height)
        {
            int alphaStride = (width + 3) & ~3;
            byte[] alphaData = new byte[height * alphaStride];

            if (bitmap.Format == PixelFormats.Bgra32 || bitmap.Format == PixelFormats.Pbgra32)
            {
                int sourceStride = width * 4;
                byte[] sourceData = new byte[height * sourceStride];
                bitmap.CopyPixels (sourceData, sourceStride, 0);

                for (int y = 0; y < height; y++)
                for (int x = 0; x < width;  x++)
                    alphaData[y * alphaStride + x] = sourceData[y * sourceStride + x * 4 + 3];
            }
            else
            {
                for (int i = 0; i < alphaData.Length; i++)
                    alphaData[i] = 0xFF;
            }

            return alphaData;
        }

        internal static byte[] LoadAlphaFromImage (string filename)
        {
            using (var input = VFS.OpenBinaryStream (filename))
            {
                var image = ImageFormat.Read (input);
                if (image == null)
                    throw new InvalidFormatException ($"Unable to load alpha image {filename}");

                var bitmap = image.Bitmap;
                int width = bitmap.PixelWidth;
                int height = bitmap.PixelHeight;
                int alphaStride = (width + 3) & ~3;
                byte[] alphaData = new byte[height * alphaStride];

                if (bitmap.Format == PixelFormats.Gray8)
                {
                    // Direct grayscale alpha
                    bitmap.CopyPixels (alphaData, alphaStride, 0);
                }
                else
                {
                    // Convert to grayscale
                    var gray = new FormatConvertedBitmap (bitmap, PixelFormats.Gray8, null, 0);
                    gray.CopyPixels (alphaData, alphaStride, 0);
                }

                return alphaData;
            }
        }

        internal static void WriteLayerData (BinaryWriter writer, byte[] pixels, 
            GalMetaData meta, int width, int height, int bpp, bool shuffled = false, uint key = 0)
        {
            int stride = (width * bpp / 8 + 3) & ~3;
            int bytesPerPixel = (bpp + 7) / 8;

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
                        var refs = new int[blocks_count * 2];
                        for (int i = 0; i < blocks_count; i++)
                        {
                            refs[i*2  ] = -1;  // frame_ref = -1 means raw data follows
                            refs[i*2+1] =  0;  // layer_ref (unused)
                        }

                        if (shuffled)
                            ShuffleBlocks (refs, blocks_count, key);

                        for (int i = 0; i < refs.Length; i++)
                            tempWriter.Write (refs[i]);

                        // Write pixel data block by block
                        for (int y = 0; y < height; y += meta.BlockHeight)
                        {
                            int h = Math.Min (meta.BlockHeight, height - y);
                            for (int x = 0; x < width; x += meta.BlockWidth)
                            {
                                int w = Math.Min (meta.BlockWidth, width - x);
                                int chunk_size = w * bytesPerPixel;

                                for (int j = 0; j < h; j++)
                                {
                                    int src = (y+j) * stride + x * bytesPerPixel;
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
                        pixels, stride);

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
                        var refs = new int[blocks_count * 2];
                        for (int i = 0; i < blocks_count; i++)
                        {
                            refs[i*2  ] = -1;
                            refs[i*2+1] =  0;
                        }

                        if (shuffled)
                            ShuffleBlocks (refs, blocks_count, key);

                        for (int i = 0; i < refs.Length; i++)
                            msWriter.Write (refs[i]);

                        // Write pixel data block by block
                        for (int y = 0; y < height; y += meta.BlockHeight)
                        {
                            int h = Math.Min (meta.BlockHeight, height - y);
                            for (int x = 0; x < width; x += meta.BlockWidth)
                            {
                                int w = Math.Min (meta.BlockWidth, width - x);
                                int chunk_size = w * bytesPerPixel;

                                for (int j = 0; j < h; j++)
                                {
                                    int src = (y + j) * stride + x * bytesPerPixel;
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

        internal static void WriteAlphaData (BinaryWriter writer, byte[] alphaData,
            int width, int height, int compression, int blockWidth, int blockHeight,
            bool shuffled = false, uint key = 0)
        {
            int alphaStride = (width + 3) & ~3;

            if (compression == 0 || compression == 2)
            {
                if (blockWidth > 0 && blockHeight > 0)
                {
                    WriteCompressedBlocks (
                        writer, alphaData, width, height, alphaStride, 
                        blockWidth, blockHeight, 1, shuffled, key
                    );
                }
                else
                    WriteCompressedRaw (
                        writer, alphaData, height, alphaStride, shuffled, key
                    );
            }
            else if (compression == 1 || compression == -1)
            {
                if (blockWidth > 0 && blockHeight > 0)
                {
                    WriteRawBlocks (
                        writer, alphaData, width, height, alphaStride, 
                        blockWidth, blockHeight, 1, shuffled, key
                    );
                }
                else
                {
                    WriteRawData (
                        writer, alphaData, height, alphaStride, shuffled, key
                    );
                }
            }
            else
            {
                writer.Write (alphaData.Length);
                writer.Write (alphaData);
            }
        }

        internal static void WriteCompressedAlpha (BinaryWriter writer, byte[] alphaData,
            int width, int height, int blockWidth, int blockHeight)
        {
            int alphaStride = (width + 3) & ~3;

            if (blockWidth > 0 && blockHeight > 0)
            {
                WriteCompressedBlocks (
                    writer, alphaData, width, height, alphaStride, 
                    blockWidth, blockHeight, 1, false, 0
                );
            }
            else
            {
                using (var compressed = new MemoryStream())
                {
                    using (var zlib = new ZLibStream (compressed, CompressionMode.Compress))
                    {
                        zlib.Write (alphaData, 0, alphaData.Length);
                    }
                    var compressedData = compressed.ToArray();
                    writer.Write (compressedData.Length);
                    writer.Write (compressedData);
                }
            }
        }

        internal static void WriteOpaqueAlpha (BinaryWriter writer, int width, int height,
            int blockWidth, int blockHeight)
        {
            int alphaStride = (width + 3) & ~3;
            var alphaData = new byte[height * alphaStride];
            for (int i = 0; i < alphaData.Length; i++)
                alphaData[i] = 0xFF;

            WriteCompressedAlpha (writer, alphaData, width, height, blockWidth, blockHeight);
        }

        private static void WriteCompressedBlocks (
            BinaryWriter writer, byte[] data,
            int width, int height, int stride, int blockWidth, int blockHeight,
            int bytesPerPixel, bool shuffled, uint key)
        {
            using (var uncompressed = new MemoryStream())
            using (var tempWriter = new BinaryWriter (uncompressed))
            {
                int blocks_w = (width + blockWidth - 1) / blockWidth;
                int blocks_h = (height + blockHeight - 1) / blockHeight;
                int blocks_count = blocks_w * blocks_h;

                var refs = new int[blocks_count * 2];
                for (int i = 0; i < blocks_count; i++)
                {
                    refs[i*2  ] = -1;
                    refs[i*2+1] =  0;
                }

                if (shuffled)
                    ShuffleBlocks (refs, blocks_count, key);

                for (int i = 0; i < refs.Length; i++)
                    tempWriter.Write (refs[i]);

                for (int y = 0; y < height; y += blockHeight)
                {
                    int h = Math.Min (blockHeight, height - y);
                    for (int x = 0; x < width; x += blockWidth)
                    {
                        int w = Math.Min (blockWidth, width - x);
                        int chunk_size = w * bytesPerPixel;

                        for (int j = 0; j < h; j++)
                        {
                            int src = (y+j) * stride + x * bytesPerPixel;
                            tempWriter.Write (data, src, chunk_size);
                        }
                    }
                }

                uncompressed.Position = 0;
                using (var compressed = new MemoryStream())
                {
                    using (var zlib = new ZLibStream (compressed, CompressionMode.Compress))
                    {
                        uncompressed.CopyTo (zlib);
                    }
                    var compressedData = compressed.ToArray();
                    writer.Write (compressedData.Length);
                    writer.Write (compressedData);
                }
            }
        }

        private static void WriteRawBlocks (
            BinaryWriter writer, byte[] data,
            int width, int height, int stride, int blockWidth, int blockHeight, 
            int bytesPerPixel, bool shuffled, uint key)
        {
            using (var ms = new MemoryStream())
            using (var tempWriter = new BinaryWriter (ms))
            {
                int blocks_w = (width + blockWidth - 1) / blockWidth;
                int blocks_h = (height + blockHeight - 1) / blockHeight;
                int blocks_count = blocks_w * blocks_h;

                var refs = new int[blocks_count * 2];
                for (int i = 0; i < blocks_count; i++)
                {
                    refs[i*2  ] = -1;
                    refs[i*2+1] =  0;
                }

                if (shuffled)
                    ShuffleBlocks (refs, blocks_count, key);

                for (int i = 0; i < refs.Length; i++)
                    tempWriter.Write (refs[i]);

                for (int y = 0; y < height; y += blockHeight)
                {
                    int h = Math.Min (blockHeight, height - y);
                    for (int x = 0; x < width; x += blockWidth)
                    {
                        int w = Math.Min (blockWidth, width - x);
                        int chunk_size = w * bytesPerPixel;

                        for (int j = 0; j < h; j++)
                        {
                            int src = (y + j) * stride + x * bytesPerPixel;
                            tempWriter.Write (data, src, chunk_size);
                        }
                    }
                }

                var result = ms.ToArray();
                writer.Write (result.Length);
                writer.Write (result);
            }
        }

        private static void WriteCompressedRaw (BinaryWriter writer, byte[] data,
            int height, int stride, bool shuffled, uint key)
        {
            byte[] reordered = data;
            if (shuffled)
            {
                reordered = new byte[data.Length];
                var sequence = RandomSequence (height, key).ToArray();
                for (int i = 0; i < height; i++)
                {
                    int srcRow = sequence[i];
                    Buffer.BlockCopy (data, srcRow * stride, reordered, i * stride, stride);
                }
            }

            using (var compressed = new MemoryStream())
            {
                using (var zlib = new ZLibStream (compressed, CompressionMode.Compress))
                {
                    zlib.Write (reordered, 0, reordered.Length);
                }
                var compressedData = compressed.ToArray();
                writer.Write (compressedData.Length);
                writer.Write (compressedData);
            }
        }

        private static void WriteRawData (BinaryWriter writer, byte[] data,
            int height, int stride, bool shuffled, uint key)
        {
            writer.Write (data.Length);

            if (shuffled)
            {
                var sequence = RandomSequence (height, key).ToArray();
                for (int i = 0; i < height; i++)
                {
                    int srcRow = sequence[i];
                    writer.Write (data, srcRow * stride, stride);
                }
            }
            else
                writer.Write (data);
        }

        static void ShuffleBlocks (int[] refs, int count, uint key)
        {
            var copy = refs.Clone() as int[];
            int src = 0;
            foreach (var index in RandomSequence (count, key))
            {
                refs[index*2  ] = copy[src++];
                refs[index*2+1] = copy[src++];
            }
        }

        internal static IEnumerable<int> RandomSequence (int count, uint seed)
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

        internal static void ApplyGalSettings (GalArchiveMetadata metadata)
        {
            string galCompression = Properties.Settings.Default.GALCompression ?? "zlib";
            string galVersion = Properties.Settings.Default.GALVersion ?? "GAL v107";
            switch (galVersion)
            {
                case "GAL v102 (legacy)": 
                    metadata.Version     = 102;
                    metadata.BlockWidth  = 0;
                    metadata.BlockHeight = 0;
                    break;
                case "GAL v103":  metadata.Version = 103; break;
                case "GAL v104":  metadata.Version = 104; break;
                case "GAL v105":  metadata.Version = 105; break;
                case "GAL v106":  metadata.Version = 106; break;
                case "GAL v107": 
                default:          metadata.Version = 107; break;
            }

            switch (galCompression)
            {
                case "None":            metadata.Compression = -1; break;
                case "JPEG (24/32bpp)": metadata.Compression = 2;  break;
                case "zlib":
                default:                metadata.Compression = 0;  break;
            }
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new GalOptions { 
                Key = KeyFromString (Properties.Settings.Default.GALKey) 
            };
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

    internal class GalReader : IDisposable
    {
        protected IBinaryStream m_input;
        protected   GalMetaData m_info;
        protected   List<Frame> m_frames;
        protected          uint m_key;

        public           byte[] Data { get; protected set; }
        public    PixelFormat Format { get; protected set; }
        public BitmapPalette Palette { get; protected set; }
        public            int Stride { get; protected set; }

        public           List<Frame> Frames { get { return m_frames; } }
        public List<FrameData> AllFrameData { get; protected set; }

        public class FrameData
        {
            public        byte[] Data;
            public   PixelFormat Format;
            public BitmapPalette Palette;
            public           int Stride;
        }

        public GalReader (IBinaryStream input, GalMetaData info, uint key)
        {
            m_info = info;
            if (m_info.Compression < 0 || m_info.Compression > 2)
                throw new InvalidFormatException();
            m_frames = new List<Frame> (m_info.FrameCount);
            AllFrameData = new List<FrameData>();
            m_key = key;
            m_input = input;
        }

        internal class Frame
        {
            public     int Width;
            public     int Height;
            public     int BPP;
            public     int Stride;
            public     int AlphaStride;
            public Color[] Palette;
            public  string Name;
            public   int[] Footer;
            public  byte[] FooterExtra;
            public    uint FrameMask;
            public  byte[] ReservedBytes;

            public List<Layer> Layers;

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
            public byte[] Pixels;
            public byte[] Alpha;
            public string Name;
        }

        public void UnpackAllFrames ()
        {
            m_input.Position = m_info.DataOffset;

            if (m_info.FrameCount < 1 || m_info.FrameCount > 1000)
                throw new InvalidFormatException ("Invalid frame count: " + m_info.FrameCount);

            for (int frameIndex = 0; frameIndex < m_info.FrameCount; frameIndex++)
            {
                try
                {
                    long frameStartPos = m_input.Position;

                    uint name_length = m_input.ReadUInt32();
                    if (name_length > 0x1000)
                        throw new InvalidFormatException ($"Invalid name length: {name_length}");
                    var nameBytes = m_input.ReadBytes ((int)name_length);
                    string frameName = name_length > 0 ? Encoding.ASCII.GetString (nameBytes) : "";

                    uint frameMask = m_input.ReadUInt32();
                    byte[] reservedBytes = m_input.ReadBytes (9);

                    int layer_count = m_input.ReadInt32();
                    if (layer_count < 1 || layer_count > 0x1000)
                        throw new InvalidFormatException ($"Invalid layer count: {layer_count}");

                    var frame = new Frame (layer_count);
                    frame.Name          = frameName;
                    frame.FrameMask     = frameMask;
                    frame.ReservedBytes = reservedBytes;
                    frame.Width         = m_input.ReadInt32();
                    frame.Height        = m_input.ReadInt32();
                    frame.BPP           = m_input.ReadInt32();

                    if (frame.Width <= 0 || frame.Width > 20000 ||
                        frame.Height <= 0 || frame.Height > 20000)
                        throw new InvalidFormatException ($"Invalid frame dimensions: {frame.Width}x{frame.Height}");

                    if (frame.BPP <= 0 || frame.BPP > 32)
                        throw new InvalidFormatException ($"Invalid BPP: {frame.BPP}");

                    long pixelCount = (long)frame.Width * frame.Height;
                    if (pixelCount > 100_000_000)
                        throw new InvalidFormatException ($"Frame too large {frame.Width}x{frame.Height}");

                    if (frame.BPP <= 8)
                        frame.Palette = ImageFormat.ReadColorMap (m_input.AsStream, 1 << frame.BPP);

                    frame.SetStride();
                    m_frames.Add (frame);

                    for (int i = 0; i < layer_count; ++i)
                    {
                        m_input.ReadInt32();    // left
                        m_input.ReadInt32();    // top
                        m_input.ReadByte ();    // visibility
                        m_input.ReadInt32();    // TransColor
                        m_input.ReadInt32();    // alpha
                        m_input.ReadByte ();    // AlphaOn

                        name_length = m_input.ReadUInt32();
                        if (name_length > 0x1000)
                            throw new InvalidFormatException ($"Invalid layer name length: {name_length}");
                        var layerNameBytes = m_input.ReadBytes ((int)name_length);
                        string layerName = name_length > 0 ? Encoding.ASCII.GetString (layerNameBytes) : "";

                        if (m_info.Version >= 107)
                            m_input.ReadByte(); // lock

                        var layer = new Layer();
                        layer.Name = layerName;

                        int layer_size = m_input.ReadInt32();
                        if (layer_size < 0 || layer_size > 200_000_000)
                            throw new InvalidFormatException ($"Invalid layer size: {layer_size}");

                        layer.Pixels = UnpackLayer (frame, layer_size);

                        int alpha_size = m_input.ReadInt32();
                        if (alpha_size < 0 || alpha_size > 200_000_000)
                            throw new InvalidFormatException ($"Invalid alpha size: {alpha_size}");

                        if (alpha_size != 0)
                            layer.Alpha = UnpackLayer (frame, alpha_size, true);
                        frame.Layers.Add (layer);
                    }

                    if (m_info.Version == 105 && frameIndex < m_info.FrameCount - 1)
                    {
                        frame.Footer = new int[2];
                        frame.Footer[0] = m_input.ReadInt32();
                        frame.Footer[1] = m_input.ReadInt32();
                    }
                    else if (m_info.Version >= 106 && m_input.Position + 24 <= m_input.Length)
                    {
                        frame.Footer = new int[6];
                        for (int i = 0; i < 6; i++)
                            frame.Footer[i] = m_input.ReadInt32();

                        if (frame.Footer[1] == 3 && m_input.Position + 32 <= m_input.Length)
                            frame.FooterExtra = m_input.ReadBytes (32);
                    }

                    var frameData = FlattenFrame (frame);
                    AllFrameData.Add (frameData);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine ($"[GAL] Error reading frame {frameIndex}: {ex.Message}");
                    m_input.PrintHexDump ($"Frame {frameIndex} error", "GAL");

                    if (m_frames.Count > 0)
                        break;
                    else
                        throw;
                }
            }

            if (AllFrameData.Count > 0)
            {
                Data    = AllFrameData[0].Data;
                Format  = AllFrameData[0].Format;
                Palette = AllFrameData[0].Palette;
                Stride  = AllFrameData[0].Stride;
            }
        }

        internal byte[] UnpackLayer (Frame frame, int length, bool is_alpha = false)
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

            int blocks_w = (frame.Width + m_info.BlockWidth - 1) / m_info.BlockWidth;
            int blocks_h = (frame.Height + m_info.BlockHeight - 1) / m_info.BlockHeight;
            int blocks_count = blocks_w * blocks_h;
            byte[] data = new byte[blocks_count * 8];
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
                        int src_x = m_info.BlockWidth * (refs[i + 1] % blocks_w);
                        int src_y = m_info.BlockHeight * (refs[i + 1] / blocks_w);
                        int src   = src_y * stride + (src_x * bpp + 7) / 8;
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
                        int layer_ref = refs[i + 1];
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
            byte[] pixels = new byte[frame.Height * stride];

            if (m_info.Shuffled)
            {
                foreach (var dst in GalFormat.RandomSequence (frame.Height, m_key))
                    packed.Read (pixels, dst * stride, stride);
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
            var bitmap  = decoder.Frames[0];
            frame.BPP   = bitmap.Format.BitsPerPixel;
            int stride  = bitmap.PixelWidth * bitmap.Format.BitsPerPixel / 8;
            byte[] pixels  = new byte[bitmap.PixelHeight * stride];
            bitmap.CopyPixels (pixels, stride, 0);
            frame.Stride = stride;
            return pixels;
        }

        internal FrameData FlattenFrame (Frame frame)
        {
            var frameData = new FrameData();
            var layer = frame.Layers[0];

            if (layer.Alpha != null)
            {
                frameData.Data = new byte[frame.Width * frame.Height * 4];

                switch (frame.BPP)
                {
                    case 4:  Flatten4bpp  (frame, layer, frameData.Data); break;
                    case 8:  Flatten8bpp  (frame, layer, frameData.Data); break;
                    case 16: Flatten16bpp (frame, layer, frameData.Data); break;
                    case 24: Flatten24bpp (frame, layer, frameData.Data); break;
                    case 32: Flatten32bpp (frame, layer, frameData.Data); break;
                    default:
                        throw new NotSupportedException ("Not supported color depth");
                }

                frameData.Format = PixelFormats.Bgra32;
                frameData.Stride = frame.Width * 4;
            }
            else
            {
                frameData.Data = layer.Pixels;
                if (null != frame.Palette)
                    frameData.Palette = new BitmapPalette (frame.Palette);

                if (8 == frame.BPP)       frameData.Format = PixelFormats.Indexed8;
                else if (16 == frame.BPP) frameData.Format = PixelFormats.Bgr565;
                else if (24 == frame.BPP) frameData.Format = PixelFormats.Bgr24;
                else if (32 == frame.BPP) frameData.Format = PixelFormats.Bgr32;
                else if (4  == frame.BPP) frameData.Format = PixelFormats.Indexed4;
                else
                    throw new NotSupportedException();

                frameData.Stride = frame.Stride;
            }

            return frameData;
        }

        internal void Flatten4bpp (Frame frame, Layer layer, byte[] output)
        {
            int dst = 0;
            int src = 0;
            int   a = 0;
            for (int y = 0; y < frame.Height; ++y)
            {
                for (int x = 0; x < frame.Width; ++x)
                {
                    byte pixel = layer.Pixels[src + x/2];
                    int index = 0 == (x & 1) ? (pixel & 0xF) : (pixel >> 4);
                    var color = frame.Palette[index];
                    output[dst++] = color.B;
                    output[dst++] = color.G;
                    output[dst++] = color.R;
                    output[dst++] = layer.Alpha[a + x];
                }
                src += frame.Stride;
                a += frame.AlphaStride;
            }
        }

        internal void Flatten8bpp (Frame frame, Layer layer, byte[] output)
        {
            int dst = 0;
            int src = 0;
            int   a = 0;
            for (int y = 0; y < frame.Height; ++y)
            {
                for (int x = 0; x < frame.Width; ++x)
                {
                    var color = frame.Palette[layer.Pixels[src + x]];
                    output[dst++] = color.B;
                    output[dst++] = color.G;
                    output[dst++] = color.R;
                    output[dst++] = layer.Alpha[a + x];
                }
                src += frame.Stride;
                a += frame.AlphaStride;
            }
        }

        internal void Flatten16bpp (Frame frame, Layer layer, byte[] output)
        {
            int src = 0;
            int dst = 0;
            int   a = 0;
            for (int y = 0; y < frame.Height; ++y)
            {
                for (int x = 0; x < frame.Width; ++x)
                {
                    int pixel = LittleEndian.ToUInt16 (layer.Pixels, src + x * 2);
                    output[dst++] = (byte)((pixel & 0x001F) * 0xFF / 0x001F);
                    output[dst++] = (byte)((pixel & 0x07E0) * 0xFF / 0x07E0);
                    output[dst++] = (byte)((pixel & 0xF800) * 0xFF / 0xF800);
                    output[dst++] = layer.Alpha[a + x];
                }
                src += frame.Stride;
                a += frame.AlphaStride;
            }
        }

        internal void Flatten24bpp (Frame frame, Layer layer, byte[] output)
        {
            int src = 0;
            int dst = 0;
            int   a = 0;
            int gap = frame.Stride - frame.Width * 3;
            for (int y = 0; y < frame.Height; ++y)
            {
                for (int x = 0; x < frame.Width; ++x)
                {
                    output[dst++] = layer.Pixels[src++];
                    output[dst++] = layer.Pixels[src++];
                    output[dst++] = layer.Pixels[src++];
                    output[dst++] = layer.Alpha [a + x];
                }
                src += gap;
                a += frame.AlphaStride;
            }
        }

        internal void Flatten32bpp (Frame frame, Layer layer, byte[] output)
        {
            int src = 0;
            int dst = 0;

            if (layer.Alpha != null)
            {
                // If we have separate alpha channel, use it
                int a = 0;
                for (int y = 0; y < frame.Height; ++y)
                {
                    for (int x = 0; x < frame.Width; ++x)
                    {
                        output[dst++] = layer.Pixels[src];
                        output[dst++] = layer.Pixels[src + 1];
                        output[dst++] = layer.Pixels[src + 2];
                        output[dst++] = layer.Alpha[a + x];
                        src += 4;
                    }
                    a += frame.AlphaStride;
                }
            }
            else
            {
                for (int y = 0; y < frame.Height; ++y)
                {
                    for (int x = 0; x < frame.Width; ++x)
                    {
                        output[dst++] = layer.Pixels[src];
                        output[dst++] = layer.Pixels[src + 1];
                        output[dst++] = layer.Pixels[src + 2];
                        output[dst++] = layer.Pixels[src + 3];
                        src += 4;
                    }
                }
            }
        }

        void ShuffleBlocks (int[] refs, int count)
        {
            var copy = refs.Clone() as int[];
            int src = 0;
            foreach (var index in GalFormat.RandomSequence (count, m_key))
            {
                refs[index * 2] = copy[src++];
                refs[index * 2 + 1] = copy[src++];
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