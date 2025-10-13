using System;
using System.IO;
using System.Text;
using System.ComponentModel.Composition;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using GameRes.Utility;

namespace GameRes
{
    [Export(typeof(ImageFormat))]
    [ExportMetadata("Priority", 10)]
    public class JpegFormat : ImageFormat
    {
        public override string         Tag { get { return "JPEG"; } }
        public override string Description { get { return "JPEG image file format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return true; } }

        readonly FixedGaugeSetting Quality = new FixedGaugeSetting (Properties.Settings.Default) {
            Name = "JPEGQuality",
            Text = Localization._T("JPEGQualityText"),
            Min = 1, Max = 100,
            ValuesSet = new[] { 1, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 },
        };

        public JpegFormat ()
        {
            Extensions = new string[] { "jpg", "jpeg" };
            Signatures = new uint[] { 0xe0ffd8ffu, 0 };
            Settings = new[] { Quality };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            try
            {
                file.Position = 0;

                var decoder = new JpegBitmapDecoder(file.AsStream,
                    BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

                if (decoder.Frames.Count == 0)
                    return null;

                var frame = decoder.Frames[0];
                frame.Freeze();
                return new ImageData(frame, info);
            }
            catch
            {
                Trace.WriteLine($"Filed to load {file.Name} with JpegBitmapDecoder, using ImageMagick fallback...");
                return FormatCatalog.Instance.GetImageFormatByTag("IMGMAGICK")?.Read(file, info);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            var encoder = new JpegBitmapEncoder();
            encoder.QualityLevel = Quality.Get<int>();
            encoder.Frames.Add (BitmapFrame.Create (image.Bitmap, null, null, null));
            encoder.Save (file);
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (0xFF != file.ReadByte() || 0xD8 != file.ReadByte())
                return null;

            while (-1 != file.PeekByte())
            {
                ushort marker = Binary.BigEndian (file.ReadUInt16());
                if ((marker & 0xff00) != 0xff00)
                    break;

                int length = Binary.BigEndian (file.ReadUInt16());

                // Check for Start of Frame markers
                // SOF0-3, SOF5-7, SOF9-11, SOF13-15
                bool isSOF = (marker >= 0xFFC0 && marker <= 0xFFC3) ||
                             (marker >= 0xFFC5 && marker <= 0xFFC7) ||
                             (marker >= 0xFFC9 && marker <= 0xFFCB) ||
                             (marker >= 0xFFCD && marker <= 0xFFCF);

                if (isSOF)
                {
                    if (length < 8)
                        break;
                    int bits = file.ReadByte();
                    uint height = Binary.BigEndian (file.ReadUInt16());
                    uint width  = Binary.BigEndian (file.ReadUInt16());
                    int components = file.ReadByte();
                    return new ImageMetaData {
                        Width = width,
                        Height = height,
                        BPP = bits * components,
                    };
                }

                // Skip other markers
                if (length >= 2)
                {
                    long skipBytes = length - 2;
                    long available = file.Length - file.Position;
                    if (skipBytes > available)
                        break; // Corrupted JPEG
                    file.Seek (skipBytes, SeekOrigin.Current);
                }
                else
                    break; // Invalid length
            }
            return null;
        }
    }
}
