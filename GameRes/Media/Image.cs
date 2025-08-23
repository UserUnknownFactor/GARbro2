using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes
{
    public interface IImageComment
    {
        string GetComment();
    }

    public class ImageMetaData : IImageComment
    {
        /// <summary>Image width in pixels.</summary>
        public uint  Width { get; set; }

        /// <summary>Image height in pixels.</summary>
        public uint Height { get; set; }

        /// <summary>Horizontal coordinate of the image top left corner.</summary>
        public int OffsetX { get; set; }

        /// <summary>Vertical coordinate of the image top left corner.</summary>
        public int OffsetY { get; set; }

        /// <summary>Image bitdepth.</summary>
        public int     BPP { get; set; }

        /// <summary>Image source file name, if any.</summary>
        public string FileName { get; set; }

        public int iWidth  { get { return (int)Width; } }
        public int iHeight { get { return (int)Height; } }

        /// <summary>Image format specifics.<br/><br/>
        /// <example>For a parser that supports multiple extensions: <br/>
        /// $" PNG {Width} x {Height} x {BPP}bpp"</example></summary>
        public virtual string GetComment()
        { 
            return string.Format(
                " {0} x {1}{2}", Width, Height, 
                BPP !=0 ? Localization.Format("MsgBPP", BPP): ""
            );
        }
    }

    public class ImageEntry : Entry
    {
        public override string Type { get { return "image"; } }
    }

    /// <summary>
    /// Enumeration representing possible palette serialization formats.
    /// </summary>
    public enum PaletteFormat
    {
        Rgb     = 1,
        Bgr     = 2,
        RgbX    = 5,
        BgrX    = 6,
        RgbA    = 9,
        BgrA    = 10,
        RgbA7   = 55,
        BgrA7   = 66,
    }

    public class ImageData
    {
        private BitmapSource m_bitmap;

        public BitmapSource Bitmap { get { return m_bitmap; } }
        public uint          Width { get { return (uint)m_bitmap.PixelWidth; } }
        public uint         Height { get { return (uint)m_bitmap.PixelHeight; } }
        public int         OffsetX { get; set; }
        public int         OffsetY { get; set; }
        public int             BPP { get { return m_bitmap.Format.BitsPerPixel; } }

        public static double DefaultDpiX { get; set; }
        public static double DefaultDpiY { get; set; }

        static ImageData ()
        {
            SetDefaultDpi (96, 96);
        }

        public static void SetDefaultDpi (double x, double y)
        {
            DefaultDpiX = x;
            DefaultDpiY = y;
        }

        public ImageData (BitmapSource data, ImageMetaData meta)
        {
            m_bitmap = data;
            OffsetX  = meta.OffsetX;
            OffsetY  = meta.OffsetY;
        }

        public ImageData (BitmapSource data, int x = 0, int y = 0)
        {
            m_bitmap = data;
            OffsetX  = x;
            OffsetY  = y;
        }

        public static ImageData Create (ImageMetaData info, PixelFormat format, BitmapPalette palette,
                                        Array pixel_data, int stride)
        {
            var bitmap = BitmapSource.Create ((int)info.Width, (int)info.Height, DefaultDpiX, DefaultDpiY,
                                              format, palette, pixel_data, stride);
            bitmap.Freeze();
            return new ImageData (bitmap, info);
        }

        public static ImageData Create (ImageMetaData info, PixelFormat format, BitmapPalette palette,
                                        Array pixel_data)
        {
            if (format.BitsPerPixel == 4)
                return Create(info, format, palette, pixel_data,
                    (int)info.Width*format.BitsPerPixel/8);
            return Create(info, format, palette, pixel_data,
                (int)info.Width*((format.BitsPerPixel+7)/8));
        }

        public static ImageData CreateFlipped (ImageMetaData info, PixelFormat format, BitmapPalette palette,
                                               Array pixel_data, int stride)
        {
            var bitmap = BitmapSource.Create ((int)info.Width, (int)info.Height, DefaultDpiX, DefaultDpiY,
                                              format, palette, pixel_data, stride);
            var flipped = new TransformedBitmap (bitmap, new ScaleTransform { ScaleY = -1 });
            flipped.Freeze();
            return new ImageData (flipped, info);
        }
    }
    
    public class AnimatedImageData : ImageData
    {
        public List<BitmapSource> Frames { get; set; }
        public List<int>     FrameDelays { get; set; }
        public bool           IsAnimated { get; set; }

        public AnimatedImageData(BitmapSource bitmap, ImageMetaData info) 
            : base(bitmap, info)
        {
            Frames      = new List<BitmapSource> { bitmap };
            FrameDelays = new List<int> { 100 }; // Default delay 100ms
            IsAnimated  = true;
        }

        public AnimatedImageData(List<BitmapSource> frames, List<int> delays, ImageMetaData info)
            : base(frames.FirstOrDefault(), info)
        {
            Frames      = frames;
            FrameDelays = delays;
            IsAnimated  = frames.Count > 1;
        }
    }

    public abstract class ImageFormat : IResource
    {
        public override string Type { get { return "image"; } }

        public abstract ImageMetaData ReadMetaData (IBinaryStream file);

        public abstract ImageData Read (IBinaryStream file, ImageMetaData info);
        public abstract void Write (Stream file, ImageData bitmap);

        public static ImageData Read (IBinaryStream file)
        {
            var format = FindFormat (file);
            if (null == format)
                return null;

            file.Position = 0;
            return format.Item1.Read(file, format.Item2);
        }

        public static System.Tuple<ImageFormat, ImageMetaData> FindFormat (IBinaryStream file)
        {
            foreach (var impl in FormatCatalog.Instance.FindFormats<ImageFormat> (file.Name, file.Signature))
            {
                try
                {
                    file.Position = 0;
                    ImageMetaData metadata = impl.ReadMetaData (file);
                    if (null != metadata)
                    {
                        metadata.FileName = file.Name;
                        return Tuple.Create (impl, metadata);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch { }
            }
            return null;
        }

        public bool IsBuiltin
        {
            get { return this.GetType().Assembly == typeof(ImageFormat).Assembly; }
        }

        public static ImageFormat FindByTag (string tag)
        {
            return FormatCatalog.Instance.ImageFormats.FirstOrDefault (x => x.Tag == tag);
        }

        static readonly ResourceInstance<ImageFormat> s_JpegFormat = new ResourceInstance<ImageFormat> ("JPEG");
        static readonly ResourceInstance<ImageFormat> s_PngFormat  = new ResourceInstance<ImageFormat> ("PNG");
        static readonly ResourceInstance<ImageFormat> s_BmpFormat  = new ResourceInstance<ImageFormat> ("BMP");
        static readonly ResourceInstance<ImageFormat> s_TgaFormat  = new ResourceInstance<ImageFormat> ("TGA");
        static readonly ResourceInstance<ImageFormat> s_GifFormat  = new ResourceInstance<ImageFormat> ("GIF");

        public static ImageFormat Jpeg => s_JpegFormat.Value;
        public static ImageFormat  Png => s_PngFormat.Value;
        public static ImageFormat  Bmp => s_BmpFormat.Value;
        public static ImageFormat  Tga => s_TgaFormat.Value;
        public static ImageFormat  Gif => s_GifFormat.Value;

        /// <summary>
        /// Desereialize color map from <paramref name="input"/> stream, consisting of specified number of
        /// <paramref name="colors"/> stored in specified <paramref name="format"/>.
        /// Default number of colors is 256 and format is 4-byte BGRX (where X is an unsignificant byte).
        /// </summary>
        public static Color[] ReadColorMap (Stream input, int colors = 0x100, PaletteFormat format = PaletteFormat.BgrX)
        {
            int bpp = PaletteFormat.Rgb == format || PaletteFormat.Bgr == format ? 3 : 4;
            var palette_data = new byte[bpp * colors];
            if (palette_data.Length != input.Read (palette_data, 0, palette_data.Length))
                throw new EndOfStreamException();
            int src = 0;
            var color_map = new Color[colors];
            Func<int, Color> get_color;
            if (PaletteFormat.Bgr == format || PaletteFormat.BgrX == format)
                get_color = x => Color.FromRgb (palette_data[x+2], palette_data[x+1], palette_data[x]);
            else if (PaletteFormat.BgrA == format)
                get_color = x => Color.FromArgb (palette_data[x+3], palette_data[x+2], palette_data[x+1], palette_data[x]);
            else if (PaletteFormat.BgrA7 == format)
                get_color = x => Color.FromArgb (palette_data[x+3] >= byte.MaxValue / 2 ? byte.MaxValue : (byte)(palette_data[x+3] << 1), palette_data[x+2], palette_data[x+1], palette_data[x]);
            else if (PaletteFormat.RgbA == format)
                get_color = x => Color.FromArgb (palette_data[x+3], palette_data[x], palette_data[x+1], palette_data[x+2]);
            else if (PaletteFormat.RgbA7 == format)
                get_color = x => Color.FromArgb (palette_data[x+3] >= byte.MaxValue / 2 ? byte.MaxValue : (byte)(palette_data[x+3] << 1), palette_data[x], palette_data[x+1], palette_data[x+2]);
            else
                get_color = x => Color.FromRgb (palette_data[x], palette_data[x+1], palette_data[x+2]);

            for (int i = 0; i < colors; ++i)
            {
                color_map[i] = get_color (src);
                src += bpp;
            }
            return color_map;
        }

        public static BitmapPalette ReadPalette (Stream input, int colors = 0x100, PaletteFormat format = PaletteFormat.BgrX)
        {
            return new BitmapPalette (ReadColorMap (input, colors, format));
        }

        public static BitmapPalette ReadPalette (ArcView file, long offset, int colors = 0x100, PaletteFormat format = PaletteFormat.BgrX)
        {
            using (var input = file.CreateStream (offset, (uint)(4 * colors))) // largest possible size for palette
                return ReadPalette (input, colors, format);
        }
    } // ImageFormat
}
