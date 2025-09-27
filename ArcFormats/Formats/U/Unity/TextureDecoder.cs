using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Formats.DirectDraw;
using GameRes.Utility;

namespace GameRes.Formats.Unity
{
    internal class Texture2DDecoder : IImageDecoder
    {
        AssetReader     m_reader;
        Texture2D       m_texture;
        ImageData       m_image;

        public Stream            Source { get { m_reader.Position = 0; return m_reader.Source; } }
        public ImageFormat SourceFormat { get { return null; } }
        public PixelFormat       Format { get; private set; }
        public ImageMetaData       Info { get; private set; }
        public ImageData          Image 
        { 
            get 
            { 
                if (m_image == null)
                    m_image = Unpack();
                return m_image;
            }
        }

        public Texture2DDecoder (Texture2D texture, AssetReader input)
        {
            m_reader  = input;
            m_texture = texture;
            Info = new ImageMetaData {
                Width   = (uint)m_texture.m_Width,
                Height  = (uint)m_texture.m_Height,
            };
            SetFormat (m_texture.m_TextureFormat);
        }

        void SetFormat (TextureFormat format)
        {
            switch (format)
            {
            case TextureFormat.Alpha8:
                Format = PixelFormats.Gray8;
                Info.BPP = 8;
                break;

            case TextureFormat.R16:
                Format = PixelFormats.Gray16;
                Info.BPP = 16;
                break;

            case TextureFormat.RGB24:
                Format = PixelFormats.Rgb24;
                Info.BPP = 24;
                break;

            case TextureFormat.RGB565:
                Format = PixelFormats.Bgr565;
                Info.BPP = 16;
                break;

            case TextureFormat.ARGB4444:
            case TextureFormat.RGBA4444:
                Format = PixelFormats.Bgra32;
                Info.BPP = 32;
                break;

            case TextureFormat.RGBA32:
            case TextureFormat.ARGB32:
            case TextureFormat.BGRA32:
            default:
                Format = PixelFormats.Bgra32;
                Info.BPP = 32;
                break;
            }
        }

        ImageData Unpack ()
        {
            // Load texture data if not already loaded
            if (null == m_texture.m_Data || 0 == m_texture.m_Data.Length)
                m_texture.LoadData (m_reader);

            if (null == m_texture.m_Data || 0 == m_texture.m_Data.Length || m_texture.m_Width == 0 || m_texture.m_Height == 0)
                throw new InvalidFormatException (Localization._T ("Cannot locate Texture2D data"));

            byte[] pixels;
            switch (m_texture.m_TextureFormat)
            {
            case TextureFormat.DXT1:
            case TextureFormat.DXT1Crunched:
                {
                    var decoder = new DxtDecoder (m_texture.m_Data, Info);
                    pixels = decoder.UnpackDXT1();
                    break;
                }
            case TextureFormat.DXT5:
            case TextureFormat.DXT5Crunched:
                {
                    var decoder = new DxtDecoder (m_texture.m_Data, Info);
                    pixels = decoder.UnpackDXT5();
                    break;
                }
            case TextureFormat.Alpha8:
            case TextureFormat.R16:
            case TextureFormat.RGB24:
            case TextureFormat.BGRA32:
                pixels = m_texture.m_Data;
                break;

            case TextureFormat.RGB565:
                pixels = ConvertRgb565 (m_texture.m_Data);
                break;

            case TextureFormat.ARGB32:
                pixels = ConvertArgb (m_texture.m_Data);
                break;

            case TextureFormat.RGBA32:
                pixels = ConvertRgba (m_texture.m_Data);
                break;

            case TextureFormat.ARGB4444:
                pixels = ConvertArgb16 (m_texture.m_Data);
                break;

            case TextureFormat.RGBA4444:
                pixels = ConvertRgba16 (m_texture.m_Data);
                break;

            case TextureFormat.BC7:
                {
                    var decoder = new Bc7Decoder (m_texture.m_Data, Info);
                    pixels = decoder.Unpack();
                    break;
                }
            default:
                throw new NotImplementedException (Localization.Format ("Unsupported Texture2D format: {0}", m_texture.m_TextureFormat));
            }
            
            if (pixels == null)
                throw new InvalidFormatException (Localization._T ("Cannot locate Texture2D data"));
            
            // Flip the image vertically as Unity textures are stored upside down
            return ImageData.CreateFlipped (Info, Format, null, pixels, (int)Info.Width * ((Format.BitsPerPixel + 7) / 8));
        }

        byte[] ConvertArgb (byte[] data)
        {
            // ARGB -> BGRA conversion
            var output = new byte[data.Length];
            for (int i = 0; i < data.Length; i += 4)
            {
                output[i]     = data[i + 3]; // B
                output[i + 1] = data[i + 2]; // G
                output[i + 2] = data[i + 1]; // R
                output[i + 3] = data[i];     // A
            }
            return output;
        }

        byte[] ConvertRgba (byte[] data)
        {
            // RGBA -> BGRA conversion
            var output = new byte[data.Length];
            for (int i = 0; i < data.Length; i += 4)
            {
                output[i]     = data[i + 2]; // B
                output[i + 1] = data[i + 1]; // G
                output[i + 2] = data[i];     // R
                output[i + 3] = data[i + 3]; // A
            }
            return output;
        }

        byte[] ConvertArgb16 (byte[] data)
        {
            // ARGB4444 -> BGRA32 conversion
            var output = new byte[data.Length * 2];
            int dst = 0;
            for (int i = 0; i < data.Length; i += 2)
            {
                ushort p = LittleEndian.ToUInt16 (data, i);
                output[dst++] = (byte)(((p >> 8) & 0xF) * 0x11); // B
                output[dst++] = (byte)(((p >> 4) & 0xF) * 0x11); // G
                output[dst++] = (byte)((p & 0xF) * 0x11);        // R
                output[dst++] = (byte)(((p >> 12) & 0xF) * 0x11); // A
            }
            return output;
        }

        byte[] ConvertRgba16 (byte[] data)
        {
            // RGBA4444 -> BGRA32 conversion
            var output = new byte[data.Length * 2];
            int dst = 0;
            for (int i = 0; i < data.Length; i += 2)
            {
                ushort p = LittleEndian.ToUInt16 (data, i);
                output[dst++] = (byte)(((p >> 8) & 0xF) * 0x11);  // B
                output[dst++] = (byte)(((p >> 4) & 0xF) * 0x11);  // G
                output[dst++] = (byte)(((p >> 12) & 0xF) * 0x11); // R
                output[dst++] = (byte)((p & 0xF) * 0x11);         // A
            }
            return output;
        }

        byte[] ConvertRgb565 (byte[] data)
        {
            // RGB565 -> BGRA32 conversion
            var output = new byte[data.Length * 2];
            int dst = 0;
            for (int i = 0; i < data.Length; i += 2)
            {
                ushort p = LittleEndian.ToUInt16 (data, i);
                output[dst++] = (byte)(((p & 0x1F) * 0xFF) / 0x1F);        // B
                output[dst++] = (byte)((((p >> 5) & 0x3F) * 0xFF) / 0x3F); // G
                output[dst++] = (byte)((((p >> 11) & 0x1F) * 0xFF) / 0x1F); // R
                output[dst++] = 0xFF; // A
            }
            return output;
        }

        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_reader?.Dispose();
                m_disposed = true;
            }
        }
    }
}