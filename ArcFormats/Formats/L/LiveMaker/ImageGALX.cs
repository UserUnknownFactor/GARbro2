using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;

using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.LiveMaker
{
    internal class GalXMetaData : GalMetaData
    {
        public XmlNode FrameXml;
    }

    [Export(typeof(ImageFormat))]
    public class GalXFormat : ImageFormat
    {
        public override string         Tag { get { return "GALX"; } }
        public override string Description { get { return "LiveMaker2 image format"; } }
        public override uint     Signature { get { return  0x656C6147; } } // 'Gale'
        public override bool      CanWrite { get { return  true; } }

        internal GalXMetaData XMLToMetadata(Stream xml_header, int data_offset)
        {
            var xml = ReadXml (xml_header);

            XmlNode frames = xml.DocumentElement;
            if (frames.Name != "Frames")
                frames = xml.SelectSingleNode ("//Frames");

            if (data_offset != 0)
            {
                //System.Diagnostics.Debug.WriteLine ("=== ORIGINAL GAL/X XML ===");
                //System.Diagnostics.Debug.WriteLine (xml.OuterXml);
                //System.Diagnostics.Debug.WriteLine ("=== END XML ===");
                if (frames == null)
                    throw new InvalidFormatException ("No Frames element found in GAL/X XML");
            }

            var attr = frames.Attributes;
            return new GalXMetaData
            {
                Width       = UInt32.Parse (attr["Width"].Value),
                Height      = UInt32.Parse (attr["Height"].Value),
                BPP         = Int32.Parse  (attr["Bpp"].Value),
                Version     = Int32.Parse  (attr["Version"]?.Value ?? "200"),
                FrameCount  = Int32.Parse  (attr["Count"].Value),
                Shuffled    = attr["Randomized"].Value != "0",
                Compression = Int32.Parse  (attr["CompType"].Value),
                Mask  = (uint)Int32.Parse  (attr["BGColor"].Value),
                BlockWidth  = Int32.Parse  (attr["BlockWidth"].Value),
                BlockHeight = Int32.Parse  (attr["BlockHeight"].Value),
                DataOffset  = data_offset,
                FrameXml    = frames,
            };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (12);
            if (!header.AsciiEqual ("GaleX200"))
                return null;
            int header_size = LittleEndian.ToInt32 (header, 8);
            using (var zheader = new StreamRegion (file.AsStream, 12, header_size, true))
            using (var xheader = new ZLibStream (zheader, CompressionMode.Decompress))
            {
                return XMLToMetadata (xheader, header_size + 12);
            }
        }

        static readonly Regex FrameRe = new Regex (@"<Frame [^>]+>");

        internal XmlDocument ReadXml (Stream input)
        {
            using (var reader = new StreamReader (input))
            {
                var text = reader.ReadToEnd();
                var xml = new XmlDocument();
                try
                {
                    xml.LoadXml (text);
                }
                catch (XmlException)
                {
                    xml.LoadXml (FrameRe.Replace (text, "<Frame>"));
                }

                return xml;
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (GalXMetaData)info;
            if (meta.Shuffled)
                throw new NotImplementedException ("Encrypted GaleX images not implemented.");
            using (var reader = new GalXReaderWithMeta (stream, meta, 0))
            {
                reader.Unpack();
                meta.Frames = reader.GetFrameInfo();
                return StoreMetadata (reader.CreateImageData(), meta);
            }
        }

        private ImageData StoreMetadata (ImageData imageData, GalXMetaData meta)
        {
            var pngImage = imageData as PngImageData ?? new PngImageData (imageData.Bitmap, meta);
            if (meta.FrameXml != null)
            {
                var xmlString = meta.FrameXml.OuterXml;
                pngImage.CustomChunks["gALx"] = Encoding.ASCII.GetBytes (xmlString);
            }

            return pngImage;
        }

        public override void Write (Stream file, ImageData image)
        {
            GalXMetaData galMeta = null;
            if (image is PngImageData pngData)
            {
                if (pngData.CustomChunks.TryGetValue ("gALx", out var xmlChunk))
                {
                    using (var xmlStream = new MemoryStream(xmlChunk))
                    { 
                        galMeta = XMLToMetadata (xmlStream, 0);
                    }
                }
            }

            if (galMeta == null)
            {
                galMeta = new GalXMetaData
                {
                    Width = (uint)image.Bitmap.PixelWidth,
                    Height = (uint)image.Bitmap.PixelHeight,
                    BPP = image.Bitmap.Format.BitsPerPixel,
                    Version = 200,
                    FrameCount = 1,
                    Compression = 0,
                    Mask = 0xFFFFFFFF,
                    BlockWidth = 16,
                    BlockHeight = 16,
                    Shuffled = false,
                    Frames = new System.Collections.Generic.List<GalFrameInfo>()
                };
            }

            WriteGalX (file, image, galMeta);
        }

        private void WriteGalX (Stream file, ImageData image, GalXMetaData meta)
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

            using (var writer = new BinaryWriter (file, Encoding.ASCII, true))
            {
                writer.Write (Encoding.ASCII.GetBytes ("GaleX200"));

                var xml = CreateXmlHeader (meta, bitmap);
                byte[] xmlBytes = Encoding.ASCII.GetBytes (xml);

                byte[] compressedXml;
                using (var ms = new MemoryStream())
                {
                    using (var zlib = new ZLibStream (ms, CompressionMode.Compress, true))
                    {
                        zlib.Write (xmlBytes, 0, xmlBytes.Length);
                        zlib.Flush();
                    }
                    compressedXml = ms.ToArray();
                }

                writer.Write (compressedXml.Length);
                writer.Write (compressedXml);

                WriteLayerData (writer, pixels, meta, width, height, stride, bpp);
                // Write alpha channel if present (for now, no alpha)
            }
        }

        private string CreateXmlHeader (GalXMetaData meta, BitmapSource bitmap)
        {
            var sb = new StringBuilder();
            sb.AppendFormat ("<Frames Version=\"{0}\" Width=\"{1}\" Height=\"{2}\" Bpp=\"{3}\" Count=\"{4}\" ",
                200, bitmap.PixelWidth, bitmap.PixelHeight, bitmap.Format.BitsPerPixel, meta.FrameCount);
            sb.AppendFormat ("SyncPal=\"1\" Randomized=\"{0}\" CompType=\"{1}\" CompLevel=\"70\" BGColor=\"{2}\" BlockWidth=\"{3}\" BlockHeight=\"{4}\" NotFillBG=\"0\">",
                meta.Shuffled ? "1" : "0", meta.Compression, (int)meta.Mask, meta.BlockWidth, meta.BlockHeight);
            sb.AppendLine();

            sb.AppendLine ("<Frame>");
            sb.AppendFormat ("<Layers Count=\"1\" Width=\"{0}\" Height=\"{1}\" Bpp=\"{2}\">",
                bitmap.PixelWidth, bitmap.PixelHeight, bitmap.Format.BitsPerPixel);
            sb.AppendLine();

            if (meta.BPP <= 8 && bitmap.Palette != null)
            {
                sb.Append ("<RGB>");
                foreach (var color in bitmap.Palette.Colors)
                    sb.AppendFormat ("{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);

                for (int i = bitmap.Palette.Colors.Count; i < 256; i++)
                    sb.Append ("000000");

                sb.AppendLine ("</RGB>");
            }

            string layerName = "";
            int left = 0, top = 0;
            byte visible = 1;
            int transColor = -1;
            int alpha = 0xFF;
            byte alphaOn = 0;

            if (meta.Frames != null && meta.Frames.Count > 0 &&
                meta.Frames[0].Layers != null && meta.Frames[0].Layers.Count > 0)
            {
                var layer = meta.Frames[0].Layers[0];
                layerName = layer.LayerName ?? "";
                left = layer.Left;
                top = layer.Top;
                visible = layer.Visibility;
                transColor = layer.TransColor;
                alpha = layer.Alpha;
                alphaOn = layer.AlphaOn;
            }

            sb.AppendFormat ("<Layer Left=\"{0}\" Top=\"{1}\" Visible=\"{2}\" TransColor=\"{3}\" Alpha=\"{4}\" AlphaOn=\"{5}\" Name=\"{6}\" Lock=\"0\" />",
                left, top, visible, transColor, alpha, alphaOn, System.Security.SecurityElement.Escape (layerName));
            sb.AppendLine();

            sb.AppendLine ("</Layers>");
            sb.AppendLine ("</Frame>");
            sb.AppendLine ("</Frames>");

            return sb.ToString();
        }

        private void WriteLayerData (BinaryWriter writer, byte[] pixels, GalXMetaData meta,
            int width, int height, int stride, int bpp)
        {
            GalFormat.WriteLayerData (writer, pixels, meta, width, height, bpp);

            bool hasAlpha = meta.Frames?.Count > 0 &&
                           meta.Frames[0].Layers?.Count > 0 &&
                           meta.Frames[0].Layers[0].AlphaOn != 0;
            if (hasAlpha)
            {
                writer.Write ((int)0);  // TODO: For now, empty alpha
            }
        }
    }

        internal class GalXReaderWithMeta : GalXReader
    {
        public GalXReaderWithMeta (IBinaryStream input, GalXMetaData info, uint key) 
            : base (input, info, key)
        {
        }

        public ImageData CreateImageData()
        {
            return ImageData.Create (m_info, Format, Palette, Data, Stride);
        }

        public System.Collections.Generic.List<GalFrameInfo> GetFrameInfo()
        {
            var frames = new System.Collections.Generic.List<GalFrameInfo>();

            if (m_frames.Count > 0)
            {
                var frame = m_frames[0];
                var frameInfo = new GalFrameInfo
                {
                    FrameName = "",
                    Width = frame.Width,
                    Height = frame.Height,
                    BPP = frame.BPP,
                    Layers = new System.Collections.Generic.List<GalLayerInfo>()
                };

                var layers = FrameXml?.SelectNodes ("Frame/Layers/Layer");
                if (layers != null)
                {
                    int layerIndex = 0;
                    foreach (XmlNode node in layers)
                    {
                        var layerInfo = new GalLayerInfo
                        {
                            LayerName = node.Attributes["Name"]?.Value ?? "",
                            Left = int.Parse (node.Attributes["Left"]?.Value ?? "0"),
                            Top = int.Parse (node.Attributes["Top"]?.Value ?? "0"),
                            Visibility = byte.Parse (node.Attributes["Visibility"]?.Value ?? "1"),
                            TransColor = int.Parse (node.Attributes["Trans"]?.Value ?? "-1"),
                            Alpha = int.Parse (node.Attributes["Alpha"]?.Value ?? "255"),
                            AlphaOn = byte.Parse (node.Attributes["AlphaOn"]?.Value ?? "0")
                        };

                        if (layerIndex < frame.Layers.Count)
                        {
                            layerInfo.Pixels = frame.Layers[layerIndex].Pixels;
                            layerInfo.AlphaChannel = frame.Layers[layerIndex].Alpha;
                        }

                        frameInfo.Layers.Add (layerInfo);
                        layerIndex++;
                    }
                }

                frames.Add (frameInfo);
            }

            return frames;
        }
    }

    internal class GalXReader : GalReader
    {
        protected readonly XmlNode FrameXml;

        public GalXReader (IBinaryStream input, GalXMetaData info, uint key) : base (input, info, key)
        {
            FrameXml = info.FrameXml;
        }

        new public void Unpack ()
        {
            m_input.Position = m_info.DataOffset;
            var layers = FrameXml.SelectSingleNode ("Frame/Layers");
            var frame = GetFrameFromLayers (layers);
            m_frames.Add (frame);

            var layer_nodes = layers.SelectNodes ("Layer");
            foreach (XmlNode node in layer_nodes)
            {
                bool alpha_on = node.Attributes["AlphaOn"].Value != "0";
                int layer_size = m_input.ReadInt32();
                var layer = new Layer();
                layer.Pixels = UnpackLayer (frame, layer_size);
                if (alpha_on)
                {
                    int alpha_size = m_input.ReadInt32();
                    layer.Alpha = UnpackLayer (frame, alpha_size, true);
                }
                frame.Layers.Add (layer);
            }
            Flatten (0);
        }

        internal Frame GetFrameFromLayers (XmlNode layers)
        {
            var attr = layers.Attributes;
            int layer_count = Int32.Parse (attr["Count"].Value);
            var frame = new Frame (layer_count);
            frame.Width  = Int32.Parse (attr["Width"].Value);
            frame.Height = Int32.Parse (attr["Height"].Value);
            frame.BPP    = Int32.Parse (attr["Bpp"].Value);
            frame.SetStride();
            if (frame.BPP <= 8)
                frame.Palette = ReadColorMap (layers.SelectSingleNode ("RGB").InnerText);
            return frame;
        }

        internal static Color[] ReadColorMap (string rgb)
        {
            int colors = Math.Min (0x100, rgb.Length / 6);
            var color_map = new Color[colors];
            int pos = 0;
            for (int i = 0; i < colors; ++i)
            {
                byte r = HexToByte (rgb, pos);
                byte g = HexToByte (rgb, pos + 2);
                byte b = HexToByte (rgb, pos + 4);
                color_map[i] = Color.FromRgb (r, g, b);
                pos += 6;
            }
            return color_map;
        }

        internal static byte HexToByte (string hex, int pos)
        {
            int hi = "0123456789ABCDEF".IndexOf (char.ToUpper (hex[pos]));
            int lo = "0123456789ABCDEF".IndexOf (char.ToUpper (hex[pos + 1]));
            return (byte)(hi << 4 | lo);
        }
    }
}
