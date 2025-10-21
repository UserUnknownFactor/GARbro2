using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
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
        public XmlDocument XmlDoc;
        public XmlNode FrameXml { get { return XmlDoc?.DocumentElement; } }
    }

    [Export (typeof (ImageFormat))]
    public class GalXFormat : ImageFormat
    {
        public override string Tag { get { return "GALX"; } }
        public override string Description { get { return "LiveMaker2 image format"; } }
        public override uint Signature { get { return 0x656C6147; } } // 'Gale'
        public override bool CanWrite { get { return false; } }

        internal GalXMetaData XMLToMetadata (Stream xml_header, int data_offset)
        {
            var xml = ReadXml (xml_header);

            XmlNode frames = xml.DocumentElement;
            if (frames.Name != "Frames")
                frames = xml.SelectSingleNode ("//Frames");

            if (frames == null)
                throw new InvalidFormatException ("No Frames element found in GAL/X XML");

            var attr = frames.Attributes;
            return new GalXMetaData
            {
                Width = UInt32.Parse (attr["Width"].Value),
                Height = UInt32.Parse (attr["Height"].Value),
                BPP = Int32.Parse (attr["Bpp"].Value),
                Version = Int32.Parse (attr["Version"]?.Value ?? "200"),
                FrameCount = Int32.Parse (attr["Count"].Value),
                Shuffled = attr["Randomized"].Value != "0",
                Compression = Int32.Parse (attr["CompType"].Value),
                Mask = (uint)Int32.Parse (attr["BGColor"].Value),
                BlockWidth = Int32.Parse (attr["BlockWidth"].Value),
                BlockHeight = Int32.Parse (attr["BlockHeight"].Value),
                DataOffset = data_offset,
                XmlDoc = xml
            };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (12);
            if (!header.AsciiEqual ("GaleX200"))
                return null;
            int header_size = header.ToInt32 (8);
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
                var text = reader.ReadToEnd ();
                var xml = new XmlDocument ();
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

            using (var reader = new GalXReader (stream, meta, 0))
            {
                reader.UnpackAllFrames ();

                if (reader.AllFrameData.Count == 1)
                    return ImageData.Create (info, reader.Format, reader.Palette, reader.Data, reader.Stride);

                var bitmapFrames = new List<BitmapSource> ();
                foreach (var frameData in reader.AllFrameData)
                {
                    var bitmap = BitmapSource.Create (
                        (int)info.Width, (int)info.Height,
                        96, 96,
                        frameData.Format,
                        frameData.Palette,
                        frameData.Data,
                        frameData.Stride);
                    bitmap.Freeze ();
                    bitmapFrames.Add (bitmap);
                }

                var delays = Enumerable.Repeat (100, bitmapFrames.Count).ToList ();

                return new AnimatedImageData (bitmapFrames, delays, info);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GalXFormat cannot write");
        }
    }

    internal class GalXReader : GalReader
    {
        protected readonly XmlDocument XmlDoc;

        public GalXReader (IBinaryStream input, GalXMetaData info, uint key) : base (input, info, key)
        {
            XmlDoc = info.XmlDoc;
        }

        public new void UnpackAllFrames ()
        {
            m_input.Position = m_info.DataOffset;

            // Get all Frame nodes
            var frameNodes = XmlDoc.SelectNodes ("//Frames/Frame");
            if (frameNodes == null || frameNodes.Count == 0)
                throw new InvalidFormatException ("No Frame elements found in GAL/X XML");

            // Process each frame
            foreach (XmlNode frameNode in frameNodes)
            {
                var layers = frameNode.SelectSingleNode ("Layers");
                if (layers == null)
                    continue;

                var frame = GetFrameFromLayers (layers);
                m_frames.Add (frame);

                var layer_nodes = layers.SelectNodes ("Layer");
                foreach (XmlNode node in layer_nodes)
                {
                    bool alpha_on = node.Attributes["AlphaOn"]?.Value != "0";
                    int layer_size = m_input.ReadInt32 ();
                    var layer = new Layer ();
                    layer.Pixels = UnpackLayer (frame, layer_size);
                    if (alpha_on)
                    {
                        int alpha_size = m_input.ReadInt32 ();
                        layer.Alpha = UnpackLayer (frame, alpha_size, true);
                    }
                    frame.Layers.Add (layer);
                }

                var frameData = FlattenFrame (frame);
                AllFrameData.Add (frameData);
            }

            // Set first frame as default output
            if (AllFrameData.Count > 0)
            {
                Data = AllFrameData[0].Data;
                Format = AllFrameData[0].Format;
                Palette = AllFrameData[0].Palette;
                Stride = AllFrameData[0].Stride;
            }
        }

        internal Frame GetFrameFromLayers (XmlNode layers)
        {
            var attr = layers.Attributes;
            int layer_count = Int32.Parse (attr["Count"].Value);
            var frame = new Frame (layer_count);
            frame.Width = Int32.Parse (attr["Width"].Value);
            frame.Height = Int32.Parse (attr["Height"].Value);
            frame.BPP = Int32.Parse (attr["Bpp"].Value);
            frame.SetStride ();

            if (frame.BPP <= 8)
            {
                var rgbNode = layers.SelectSingleNode ("RGB");
                if (rgbNode != null)
                    frame.Palette = ReadColorMap (rgbNode.InnerText);
            }
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