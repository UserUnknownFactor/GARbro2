using System;
using System.ComponentModel.Composition;
using System.Text;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Valkyria
{
    internal class Mg2MetaData : ImageMetaData
    {
        public int          ImageLength;
        public int          AlphaLength;
        public IMg2Scheme   Scheme;
        public ImageFormat  Format;
    }

    internal interface IMg2Scheme
    {
        Mg2EncryptedStream CreateStream (Stream main, int offset, int length, int key);
        ImageData CreateImage (BitmapSource bitmap, ImageMetaData info);
    }

    [Export(typeof(ImageFormat))]
    public class Mg2Format : ImageFormat
    {
        public override string         Tag { get { return "MG2"; } }
        public override string Description { get { return "Valkyria image format"; } }
        public override uint     Signature { get { return  0; } } // 'MICO'

        static readonly IMg2Scheme[] KnownSchemes = {
            new Mg2SchemeV1(),
            new Mg2SchemeV2(),
            new Mg2SchemeV3(),
            new Mg2SchemeV4(),
            new Mg2SchemeV5()
        };

        private const uint MG2_Header_Const1 = 0x6269B97C;
        private const int    MG2_Header_Rot1 = 3;
        private const uint MG2_Header_Const2 = 0xD17ACA4B;
        private const int    MG2_Header_Rot2 = 2;

        internal const uint MG2_Body_Const1 = 0xBD3ACCDD;
        internal const int    MG2_Body_Rot1 = 3;
        internal const uint MG2_Body_Const2 = 0x915C1A4C;
        internal const int    MG2_Body_Rot2 = 5;

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension ("MG2"))
                return null;

            var header   = file.ReadHeader (0x10);
            var fileSize = (uint)file.Length;

            byte[] workingHeader = null;
            int    length;
            int    alphaLength;
            string format;

            if (!header.AsciiEqual (0, "MICO"))
            {
                var headerBytes = header.ToArray ();
                byte[] decrypted = null;

                decrypted = DecryptHeaderRolRor (headerBytes, fileSize);
                if (CheckMicoHeader (decrypted))
                    workingHeader = decrypted;
                else
                {
                    decrypted = DecryptHeaderXor (headerBytes, fileSize);
                    if (CheckMicoHeader (decrypted))
                        workingHeader = decrypted;
                    else
                        return null;
                }

                if (AsciiEqual (workingHeader, 4, "CG01"))      format = "CG01";
                else if (AsciiEqual (workingHeader, 4, "CG02")) format = "CG02";
                else if (AsciiEqual (workingHeader, 4, "CG03")) format = "CG03";
                else
                    return null;

                length = BitConverter.ToInt32 (workingHeader, 8);
                alphaLength = BitConverter.ToInt32 (workingHeader, 12);
            }
            else
            {
                if (header.AsciiEqual (4, "CG01"))      format = "CG01";
                else if (header.AsciiEqual (4, "CG02")) format = "CG02";
                else if (header.AsciiEqual (4, "CG03")) format = "CG03";
                else
                    return null;

                length = header.ToInt32 (8);
                alphaLength = header.ToInt32 (12);
            }

            return TrySchemes (file, length, alphaLength, format);
        }

        private static bool CheckMicoHeader (byte[] header)
        {
            return header != null && header.Length >= 4 &&
                   header[0] == 'M' && header[1] == 'I' && header[2] == 'C' && header[3] == 'O';
        }

        private static bool AsciiEqual (byte[] data, int offset, string text)
        {
            if (offset + text.Length > data.Length)
                return false;
            for (int i = 0; i < text.Length; i++)
            {
                if (data[offset + i] != (byte)text[i])
                    return false;
            }
            return true;
        }

        private ImageMetaData TrySchemes (IBinaryStream file, int length, int alphaLength, string format)
        {
            IMg2Scheme[] schemes;

            switch (format)
            {
            case "CG01": schemes = new IMg2Scheme[] { new Mg2SchemeV2 (), new Mg2SchemeV1 () }; break;
            case "CG02": schemes = new IMg2Scheme[] { new Mg2SchemeV3 (), new Mg2SchemeV4 () }; break;
            case "CG03": schemes = new IMg2Scheme[] { new Mg2SchemeV5 () }; break;
            default:
                return null;
            }

            foreach (var scheme in schemes)
            {
                using (var input = scheme.CreateStream (file.AsStream, 0x10, length, length))
                using (var img = new BinaryStream (input, file.Name))
                {
                    ImageFormat imgFormat;
                    if (Png.Signature == img.Signature)   imgFormat = Png;
                    else if (0xE0FFD8FF == img.Signature) imgFormat = Jpeg;
                    else
                        continue;
                    var info = imgFormat.ReadMetaData (img);
                    if (null == info)
                        continue;
                    return new Mg2MetaData
                    {
                        Width       = info.Width,
                        Height      = info.Height,
                        OffsetX     = info.OffsetX,
                        OffsetY     = info.OffsetY,
                        BPP         = info.BPP,
                        ImageLength = length,
                        AlphaLength = alphaLength,
                        Scheme      = scheme,
                        Format      = imgFormat,
                    };
                }
            }
            return null;
        }

        private static byte[] DecryptHeaderRolRor (byte[] header, uint fileSize)
        {
            var result = (byte[])header.Clone ();
            bool toggle = false;

            for (int i = 0; i <= 12; i++)
            {
                if (i + 3 >= result.Length)
                    break;

                uint value = BitConverter.ToUInt32 (result, i);
                uint decrypted;

                if (toggle)
                {
                    decrypted = RolRor (1, value, MG2_Header_Const2, MG2_Header_Rot2, fileSize);
                    toggle = false;
                }
                else
                {
                    decrypted = RolRor (1, value, MG2_Header_Const1, MG2_Header_Rot1, fileSize);
                    toggle = true;
                }

                result[i] = (byte)decrypted;
                result[i + 1] = (byte)(decrypted >> 8);
                result[i + 2] = (byte)(decrypted >> 16);
                result[i + 3] = (byte)(decrypted >> 24);
            }

            return result;
        }

        private static byte[] DecryptHeaderXor (byte[] header, uint fileSize)
        {
            var result = (byte[])header.Clone ();
            var v0 = BitConverter.GetBytes (fileSize);
            var v1 = (byte)(v0[1] + v0[3]);
            var v2 = (byte)(v0[0] + v0[2]);

            for (int i = 0; i < result.Length; i++)
            {
                result[i] ^= v1;
                v1 += v2;
            }

            return result;
        }

        internal static uint RolRor (int mode, uint value, uint constant, int rotation, uint key)
        {
            if (mode != 0)
                return key ^ RotateRight (value - constant, rotation);
            else
                return constant + RotateLeft (key ^ value, rotation);
        }

        private static uint RotateRight (uint value, int count)
        {
            count &= 31;
            return (value >> count) | (value << (32 - count));
        }

        private static uint RotateLeft (uint value, int count)
        {
            count &= 31;
            return (value << count) | (value >> (32 - count));
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (Mg2MetaData)info;
            var frame = ReadBitmapSource (file.AsStream, meta);
            return meta.Scheme.CreateImage (frame, meta);
        }

        BitmapSource ReadBitmapSource (Stream file, Mg2MetaData meta)
        {
            BitmapSource frame;
            using (var input = meta.Scheme.CreateStream (file, 0x10, meta.ImageLength, meta.ImageLength))
            using (var img = new BinaryStream (input, meta.FileName))
            {
                var image = meta.Format.Read (img, meta);
                frame = image.Bitmap;
                if (0 == meta.AlphaLength)
                    return frame;
            }
            if (frame.Format.BitsPerPixel != 32)
                frame = new FormatConvertedBitmap (frame, PixelFormats.Bgr32, null, 0);
            int stride = frame.PixelWidth * 4;
            var pixels = new byte[stride * (int)meta.Height];
            frame.CopyPixels (pixels, stride, 0);

            using (var input = meta.Scheme.CreateStream (file, 0x10 + meta.ImageLength, meta.AlphaLength, meta.ImageLength))
            {
                var decoder = BitmapDecoder.Create (input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                BitmapSource alpha_frame = decoder.Frames[0];
                if (alpha_frame.PixelWidth != frame.PixelWidth || alpha_frame.PixelHeight != frame.PixelHeight)
                    return BitmapSource.Create ((int)meta.Width, (int)meta.Height,
                                                ImageData.DefaultDpiX, ImageData.DefaultDpiY,
                                                PixelFormats.Bgr32, null, pixels, stride);

                alpha_frame = new FormatConvertedBitmap (alpha_frame, PixelFormats.Gray8, null, 0);
                var alpha = new byte[alpha_frame.PixelWidth * alpha_frame.PixelHeight];
                alpha_frame.CopyPixels (alpha, alpha_frame.PixelWidth, 0);

                int src = 0;
                for (int dst = 3; dst < pixels.Length; dst += 4)
                    pixels[dst] = alpha[src++];
                return BitmapSource.Create ((int)meta.Width, (int)meta.Height,
                                            ImageData.DefaultDpiX, ImageData.DefaultDpiY,
                                            PixelFormats.Bgra32, null, pixels, stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Mg2Format.Write not implemented");
        }
    }

    internal class Mg2EncryptedStream : StreamRegion
    {
        readonly byte   m_version;
        readonly int    m_threshold;
        readonly byte   m_key0;
        readonly byte   m_key1;
        readonly byte[] m_key2;
        readonly byte[] m_decrypted_buffer;

        protected Mg2EncryptedStream (Stream main, int offset, int length, byte version, int threshold, byte key0, byte key1, int key2)
            : base (main, offset, length, true)
        {
            m_version = version;
            m_threshold = threshold;
            m_key0 = key0;
            m_key1 = key1;

            if (m_version == 4)
            {
                m_key2 = new byte[length];
                var v0 = BitConverter.GetBytes (key2);
                var v1 = (byte)(v0[1] + v0[3]);
                var v2 = (byte)(v0[0] + v0[2]);

                for (var i = 0; i < length; i++)
                {
                    m_key2[i] = v1;
                    v1 += v2;
                }
            }
            else if (m_version == 5)
            {
                main.Position = offset;
                m_decrypted_buffer = new byte[length];
                main.Read (m_decrypted_buffer, 0, length);

                DecryptV5Buffer (m_decrypted_buffer, (uint)key2);
                Position = 0;
            }
        }

        private static void DecryptV5Buffer (byte[] buffer, uint imageLength)
        {
            bool toggle = false;

            for (int i = 0; i <= buffer.Length - 4; i++)
            {
                uint value = BitConverter.ToUInt32 (buffer, i);
                uint result;

                if (toggle)
                {
                    result = Mg2Format.RolRor (1, value, Mg2Format.MG2_Body_Const2, Mg2Format.MG2_Body_Rot2, imageLength);
                    toggle = false;
                }
                else
                {
                    result = Mg2Format.RolRor (1, value, Mg2Format.MG2_Body_Const1, Mg2Format.MG2_Body_Rot1, imageLength);
                    toggle = true;
                }

                buffer[i    ] = (byte)result;
                buffer[i + 1] = (byte)(result >> 8);
                buffer[i + 2] = (byte)(result >> 16);
                buffer[i + 3] = (byte)(result >> 24);
            }
        }

        public static Mg2EncryptedStream CreateV1 (Stream main, int offset, int length)
        {
            return new Mg2EncryptedStream (main, offset, length, 1, length / 5, 0, 0, 0);
        }

        public static Mg2EncryptedStream CreateV2 (Stream main, int offset, int length)
        {
            return new Mg2EncryptedStream (main, offset, length, 2, Math.Min (25, length), (byte)length, 0, 0);
        }

        public static Mg2EncryptedStream CreateV3 (Stream main, int offset, int length, int key)
        {
            return new Mg2EncryptedStream (main, offset, length, 3, 0, (byte)(key >> 1), (byte)((key & 1) + (key >> 3)), 0);
        }

        public static Mg2EncryptedStream CreateV4 (Stream main, int offset, int length, int key)
        {
            return new Mg2EncryptedStream (main, offset, length, 4, 0, 0, 0, key);
        }

        public static Mg2EncryptedStream CreateV5 (Stream main, int offset, int length, int key)
        {
            return new Mg2EncryptedStream (main, offset, length, 5, 0, 0, 0, key);
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            long pos = Position;

            if (m_version == 5)
            {
                // V5: Read from pre-decrypted buffer
                int available = (int)Math.Min (count, m_decrypted_buffer.Length - pos);
                if (available <= 0)
                    return 0;

                Array.Copy (m_decrypted_buffer, pos, buffer, offset, available);
                Position = pos + available;
                return available;
            }

            int read = base.Read (buffer, offset, count);

            if (m_version == 3)
            {
                for (int i = 0; i < read; ++i)
                {
                    buffer[offset + i] ^= (byte)((pos >> 4) ^ (pos + m_key0) ^ m_key1);
                    pos++;
                }
            }
            else if (m_version == 4)
            {
                for (int i = 0; i < read; ++i)
                    buffer[offset + i] ^= m_key2[pos + i];
            }
            else
            {
                for (int i = 0; i < read && pos < m_threshold; ++i)
                    buffer[offset + i] ^= (byte)(m_key0 + pos++);
            }
            return read;
        }

        public override int ReadByte ()
        {
            long pos = Position;

            if (m_version == 5)
            {
                if (pos >= m_decrypted_buffer.Length)
                    return -1;
                Position = pos + 1;
                return m_decrypted_buffer[pos];
            }

            int b = base.ReadByte ();

            if (m_version == 3)
            {
                b ^= (byte)((pos >> 4) ^ (pos + m_key0) ^ m_key1);
            }
            else if (m_version == 4)
            {
                b ^= m_key2[pos];
            }
            else
            {
                if (b != -1 && pos < m_threshold)
                    b ^= (byte)(m_key0 + pos);
            }

            return b;
        }
    }

    internal class Mg2SchemeV1 : IMg2Scheme
    {
        public Mg2EncryptedStream CreateStream (Stream main, int offset, int length, int key)
        {
            return Mg2EncryptedStream.CreateV1 (main, offset, length);
        }

        public ImageData CreateImage (BitmapSource frame, ImageMetaData info)
        {
            frame.Freeze();
            return new ImageData (frame, info);
        }
    }

    internal class Mg2SchemeV2 : IMg2Scheme
    {
        public Mg2EncryptedStream CreateStream (Stream main, int offset, int length, int key)
        {
            return Mg2EncryptedStream.CreateV2 (main, offset, length);
        }

        public ImageData CreateImage (BitmapSource frame, ImageMetaData info)
        {
            frame = new TransformedBitmap (frame, new ScaleTransform { ScaleY = -1 });
            frame.Freeze();
            return new ImageData (frame, info);
        }
    }

    internal class Mg2SchemeV3 : IMg2Scheme
    {
        public Mg2EncryptedStream CreateStream (Stream main, int offset, int length, int key)
        {
            return Mg2EncryptedStream.CreateV3 (main, offset, length, key);
        }

        public ImageData CreateImage (BitmapSource frame, ImageMetaData info)
        {
            frame = new TransformedBitmap (frame, new ScaleTransform { ScaleY = -1 });
            frame.Freeze();
            return new ImageData (frame, info);
        }
    }

    internal class Mg2SchemeV4 : IMg2Scheme
    {
        public Mg2EncryptedStream CreateStream (Stream main, int offset, int length, int key)
        {
            return Mg2EncryptedStream.CreateV4 (main, offset, length, key);
        }

        public ImageData CreateImage (BitmapSource frame, ImageMetaData info)
        {
            frame = new TransformedBitmap (frame, new ScaleTransform { ScaleY = -1 });
            frame.Freeze();
            return new ImageData (frame, info);
        }
    }

    internal class Mg2SchemeV5 : IMg2Scheme
    {
        public Mg2EncryptedStream CreateStream (Stream main, int offset, int length, int key)
        {
            return Mg2EncryptedStream.CreateV5 (main, offset, length, key);
        }

        public ImageData CreateImage (BitmapSource frame, ImageMetaData info)
        {
            frame = new TransformedBitmap (frame, new ScaleTransform { ScaleY = -1 });
            frame.Freeze();
            return new ImageData (frame, info);
        }
    }
}