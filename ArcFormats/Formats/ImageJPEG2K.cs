using System;
using System.IO;
using System.Text;
using System.ComponentModel.Composition;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Runtime.InteropServices;

using GameRes.Utility;
using GameRes.Properties;

namespace GameRes
{
    [Export(typeof(ImageFormat))]
    [ExportMetadata("Priority", 30)]
    public class Jpeg2000Format : ImageFormat
    {
        public override string         Tag { get { return "JPEG2000"; } }
        public override string Description { get { return "JPEG 2000 image file format"; } }
        public override uint     Signature { get { return  0; } }
        public override bool      CanWrite { get { return  true; } }

        readonly FixedGaugeSetting Quality = new FixedGaugeSetting (Properties.Settings.Default) {
            Name = "JPEG2KQuality",
            Text = "JPEG 2000 compression quality",
            Min = 1, Max = 100,
            ValuesSet = new[] { 1, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 },
        };

        public Jpeg2000Format ()
        {
            Extensions = new string[] { "jp2", "j2k", "jpf", "jpx", "jpm", "j2c" };
            Signatures = new uint[] {
                0x0C000000,  // JP2: 00 00 00 0C (Little-Endian)
                0x51FF4FFF,  // J2K: FF 4F FF 51 (Little-Endian)
            };
            Settings = new[] { Quality };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var decoder = new OpenJpeg2Decoder())
            {
                return decoder.Decode(file, info);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            using (var encoder = new OpenJpeg2Encoder())
            {
                encoder.Encode(file, image, Quality.Get<int>());
            }
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!NativeMethods.CanBeUsed)
                return null;

            switch(file.Signature) {
            case 0x0C000000:
                file.Position = 0;
                return ReadJP2MetaData (file);
            case 0x51FF4FFF:
                file.Position = 0;
                return ReadJ2KMetaData (file);
            default:
                return null;
            }
        }

        private ImageMetaData ReadJP2MetaData(IBinaryStream file)
        {
            // JP2 Signature box (12 bytes)
            if (file.Length < 12)
                return null;

            uint sig_length = file.ReadUInt32BE();
            uint sig_type   = file.ReadUInt32();
            uint sig_data   = file.ReadUInt32();

            if (sig_length != 0x0000000C || sig_type != 0x2020506A)
                return null;

            // read the rest of the boxes
            while (file.Position < file.Length)
            {
                if (file.Length - file.Position < 8)
                    return null;

                uint box_length = file.ReadUInt32BE();
                uint box_type = file.ReadUInt32();

                if (box_type == 0x6832706A) // 'jp2h' - JP2 Header box
                {
                    // This is a superbox containing other boxes
                    long box_end = file.Position + box_length - 8;

                    while (file.Position < box_end)
                    {
                        if (box_end - file.Position < 8)
                            break;

                        uint sub_length = file.ReadUInt32BE();
                        uint sub_type = file.ReadUInt32();

                        if (sub_type == 0x72646869) // 'ihdr' - Image Header box
                        {
                            if (sub_length < 22)
                                return null;

                            uint height = file.ReadUInt32BE();
                            uint width  = file.ReadUInt32BE();
                            ushort components = file.ReadUInt16BE();
                            byte bpc = file.ReadUInt8(); // bits per component
                            byte c   = file.ReadUInt8(); // compression type (always 7 for JP2)
                            byte unk = file.ReadUInt8(); // color space unknown
                            byte ipr = file.ReadUInt8(); // intellectual property

                            return new ImageMetaData
                            {
                                Width = width,
                                Height = height,
                                BPP = (bpc + 1) * components, // bpc is (actual_bits - 1)
                            };
                        }
                        else
                        {
                            // Skip other sub-boxes
                            if (sub_length < 8)
                                break;
                            file.Seek(sub_length - 8, SeekOrigin.Current);
                        }
                    }

                    file.Position = box_end;
                }
                else
                {
                    // Skip to next box
                    if (box_length == 0)
                        break;
                    else if (box_length == 1)
                    {
                        // 64-bit box length
                        file.Seek(8, SeekOrigin.Current);
                        continue;
                    }
                    else if (box_length < 8)
                        return null;
                    else
                        file.Seek(box_length - 8, SeekOrigin.Current);
                }
            }

            return null;
        }

        private ImageMetaData ReadJ2KMetaData (IBinaryStream file)
        {
            ushort marker = file.ReadUInt16BE();
            if (marker != 0x4FFF) // SOC marker
                return null;

            while (file.Position < file.Length)
            {
                marker = file.ReadUInt16BE();
                if (marker == 0x51FF) // SIZ marker
                {
                    ushort length = file.ReadUInt16BE();
                    if (length < 41)
                        return null;

                    ushort caps = file.ReadUInt16BE();
                    uint width = file.ReadUInt32BE();
                    uint height = file.ReadUInt32BE();
                    uint x_offset = file.ReadUInt32BE();
                    uint y_offset = file.ReadUInt32BE();
                    uint tile_width = file.ReadUInt32BE();
                    uint tile_height = file.ReadUInt32BE();
                    uint tile_x_offset = file.ReadUInt32BE();
                    uint tile_y_offset = file.ReadUInt32BE();
                    ushort components = file.ReadUInt16BE();

                    // Read component info
                    byte precision = file.ReadUInt8();
                    byte bpp = (byte)((precision & 0x7F) + 1);

                    return new ImageMetaData {
                        Width = width - x_offset,
                        Height = height - y_offset,
                        BPP = bpp * components,
                    };
                }

                // Skip other markers
                if ((marker & 0x00FF) == 0x00FF && marker != 0xFFFF)
                {
                    ushort seg_length = file.ReadUInt16BE();
                    file.Seek (seg_length - 2, SeekOrigin.Current);
                }
                else
                    break;
            }

            return null;
        }
    }

internal class OpenJpeg2Decoder : IDisposable
{
    private bool disposed = false;

        public ImageData Decode(IBinaryStream input, ImageMetaData info)
        {
            var data = new byte[input.Length];
            input.Position = 0;
            input.Read(data, 0, data.Length);

            IntPtr codec = IntPtr.Zero;
            IntPtr stream = IntPtr.Zero;
            IntPtr image = IntPtr.Zero;
            GCHandle dataHandle = default(GCHandle);

            try
            {
                // Determine codec format based on data
                OPJ_CODEC_FORMAT format = OPJ_CODEC_FORMAT.OPJ_CODEC_JP2;
                if (data.Length > 2 && data[0] == 0xFF && data[1] == 0x4F)
                    format = OPJ_CODEC_FORMAT.OPJ_CODEC_J2K;

                codec = NativeMethods.opj_create_decompress(format);
                if (codec == IntPtr.Zero)
                    throw new InvalidOperationException("Failed to create JP2 decompressor");

                var parameters = new opj_dparameters_t();
                NativeMethods.opj_set_default_decoder_parameters(ref parameters);

                if (!NativeMethods.opj_setup_decoder(codec, ref parameters))
                    throw new InvalidOperationException("Failed to setup JP2 decoder");

                stream = NativeMethods.opj_stream_create(new IntPtr(0x100000), true);
                if (stream == IntPtr.Zero)
                    throw new InvalidOperationException("Failed to create JP2 stream");

                dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
                var wrapper = new SimpleStreamWrapper(dataHandle.AddrOfPinnedObject(), (uint)data.Length);

                // Set up stream callbacks - no user data cleanup callback
                NativeMethods.opj_stream_set_read_function(stream, wrapper.ReadDelegate);
                NativeMethods.opj_stream_set_skip_function(stream, wrapper.SkipDelegate);
                NativeMethods.opj_stream_set_seek_function(stream, wrapper.SeekDelegate);
                NativeMethods.opj_stream_set_user_data(stream, IntPtr.Zero, IntPtr.Zero);
                NativeMethods.opj_stream_set_user_data_length(stream, (ulong)data.Length);

                if (!NativeMethods.opj_read_header(stream, codec, ref image))
                    throw new InvalidOperationException("Failed to read JP2 header");

                if (!NativeMethods.opj_decode(codec, stream, image))
                    throw new InvalidOperationException("Failed to decode JP2 image");

                return ConvertToImageData(image);
            }
            finally
            {
                if (image != IntPtr.Zero)
                    NativeMethods.opj_image_destroy(image);
                if (stream != IntPtr.Zero)
                    NativeMethods.opj_stream_destroy(stream);
                if (codec != IntPtr.Zero)
                    NativeMethods.opj_destroy_codec(codec);
                if (dataHandle.IsAllocated)
                    dataHandle.Free();
            }
        }

        private ImageData ConvertToImageData(IntPtr imagePtr)
    {
        var image = Marshal.PtrToStructure<opj_image_t>(imagePtr);
        
        int width = (int)(image.x1 - image.x0);
        int height = (int)(image.y1 - image.y0);
        int numComps = (int)image.numcomps;

        PixelFormat format;
        int stride;
        
        if (numComps == 1)
        {
            format = PixelFormats.Gray8;
            stride = width;
        }
        else if (numComps == 3)
        {
            format = PixelFormats.Bgr24;
            stride = width * 3;
        }
        else if (numComps == 4)
        {
            format = PixelFormats.Bgra32;
            stride = width * 4;
        }
        else
        {
            throw new NotSupportedException($"Unsupported number of components: {numComps}");
        }

        var pixels = new byte[height * stride];
        var comps = new opj_image_comp_t[numComps];
        
        // Read component structures
        for (int i = 0; i < numComps; i++)
        {
            IntPtr compPtr = IntPtr.Add(image.comps, i * Marshal.SizeOf<opj_image_comp_t>());
            comps[i] = Marshal.PtrToStructure<opj_image_comp_t>(compPtr);
        }

        // Convert component data to pixel data
        // OpenJPEG stores data as int32 (4 bytes per pixel)
        unsafe
        {
            if (numComps == 1)
            {
                // Grayscale
                int* data0 = (int*)comps[0].data;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int idx = y * width + x;
                        int value = data0[idx];
                        pixels[y * stride + x] = (byte)Math.Max(0, Math.Min(255, value));
                    }
                }
            }
            else if (numComps >= 3)
            {
                // RGB or RGBA
                int* data0 = (int*)comps[0].data;
                int* data1 = (int*)comps[1].data;
                int* data2 = (int*)comps[2].data;
                int* data3 = numComps > 3 ? (int*)comps[3].data : null;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int idx = y * width + x;
                        int pixelOffset = y * stride + x * numComps;
                        
                        // OpenJPEG gives us RGB, we need BGR
                        pixels[pixelOffset + 2] = (byte)Math.Max(0, Math.Min(255, data0[idx]));     // R
                        pixels[pixelOffset + 1] = (byte)Math.Max(0, Math.Min(255, data1[idx]));     // G
                        pixels[pixelOffset] = (byte)Math.Max(0, Math.Min(255, data2[idx]));         // B
                        if (numComps == 4 && data3 != null)
                            pixels[pixelOffset + 3] = (byte)Math.Max(0, Math.Min(255, data3[idx])); // A
                    }
                }
            }
        }

        return ImageData.Create(
            new ImageMetaData { Width = (uint)width, Height = (uint)height, 
                BPP = numComps * 8 }, format, null, pixels, stride);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposed)
            disposed = true;
    }
}

    internal class OpenJpeg2Encoder : IDisposable
    {
        private bool disposed = false;

        public void Encode(Stream output, ImageData image, int quality)
        {
            IntPtr codec = IntPtr.Zero;
            IntPtr stream = IntPtr.Zero;
            IntPtr jp2Image = IntPtr.Zero;

            try
            {
                codec = NativeMethods.opj_create_compress(OPJ_CODEC_FORMAT.OPJ_CODEC_JP2);
                if (codec == IntPtr.Zero)
                    throw new InvalidOperationException("Failed to create JP2 compressor");

                jp2Image = CreateOpenJpegImage(image);
                if (jp2Image == IntPtr.Zero)
                    throw new InvalidOperationException("Failed to create JP2 image");

                var parameters = new opj_cparameters_t();
                NativeMethods.opj_set_default_encoder_parameters(ref parameters);

                if (quality < 100)
                {
                    parameters.tcp_numlayers = 1;
                    parameters.tcp_rates[0] = 100.0f / quality;
                    parameters.cp_disto_alloc = 1;
                }

                if (!NativeMethods.opj_setup_encoder(codec, ref parameters, jp2Image))
                    throw new InvalidOperationException("Failed to setup JP2 encoder");

                var memStream = new MemoryStream();
                stream = NativeMethods.opj_stream_create(new IntPtr(0x100000), false);
                if (stream == IntPtr.Zero)
                    throw new InvalidOperationException("Failed to create JP2 output stream");

                var streamWrapper = new StreamWrapper(memStream);
                var writeDelegate = new opj_stream_write_fn(streamWrapper.WriteCallback);
                var seekDelegate = new opj_stream_seek_fn(streamWrapper.SeekCallback);
                var skipDelegate = new opj_stream_skip_fn(streamWrapper.SkipCallback);

                NativeMethods.opj_stream_set_write_function(stream, writeDelegate);
                NativeMethods.opj_stream_set_seek_function(stream, seekDelegate);
                NativeMethods.opj_stream_set_skip_function(stream, skipDelegate);

                GCHandle handle = GCHandle.Alloc(streamWrapper);
                NativeMethods.opj_stream_set_user_data(stream, GCHandle.ToIntPtr(handle), IntPtr.Zero);

                // Encode
                if (!NativeMethods.opj_start_compress(codec, jp2Image, stream))
                    throw new InvalidOperationException("Failed to start JP2 compression");

                if (!NativeMethods.opj_encode(codec, stream))
                    throw new InvalidOperationException("Failed to encode JP2 image");

                if (!NativeMethods.opj_end_compress(codec, stream))
                    throw new InvalidOperationException("Failed to end JP2 compression");

                memStream.Position = 0;
                memStream.CopyTo(output);

                handle.Free();
            }
            finally
            {
                if (stream != IntPtr.Zero)
                    NativeMethods.opj_stream_destroy(stream);
                if (jp2Image != IntPtr.Zero)
                    NativeMethods.opj_image_destroy(jp2Image);
                if (codec != IntPtr.Zero)
                    NativeMethods.opj_destroy_codec(codec);
            }
        }

        private IntPtr CreateOpenJpegImage(ImageData imageData)
        {
            var bitmap = imageData.Bitmap;
            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;
            int numComps;

            if (bitmap.Format == PixelFormats.Gray8)
                numComps = 1;
            else if (bitmap.Format == PixelFormats.Bgr24 || bitmap.Format == PixelFormats.Rgb24)
                numComps = 3;
            else if (bitmap.Format == PixelFormats.Bgra32 || bitmap.Format == PixelFormats.Bgr32)
                numComps = 4;
            else
            {
                var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0);
                bitmap = converted;
                numComps = 3;
            }

            // Create component parameters
            var cmptparms = new opj_image_cmptparm_t[numComps];
            for (int i = 0; i < numComps; i++)
            {
                cmptparms[i] = new opj_image_cmptparm_t
                {
                    dx = 1,
                    dy = 1,
                    w = (uint)width,
                    h = (uint)height,
                    x0 = 0,
                    y0 = 0,
                    prec = 8,
                    bpp = 8,
                    sgnd = 0
                };
            }

            IntPtr image = NativeMethods.opj_image_create((uint)numComps, cmptparms, OPJ_COLOR_SPACE.OPJ_CLRSPC_SRGB);
            if (image == IntPtr.Zero)
                return IntPtr.Zero;

            var img = Marshal.PtrToStructure<opj_image_t>(image);
            img.x0 = 0;
            img.y0 = 0;
            img.x1 = (uint)width;
            img.y1 = (uint)height;
            Marshal.StructureToPtr(img, image, false);

            int stride = bitmap.PixelWidth * ((bitmap.Format.BitsPerPixel + 7) / 8);
            var pixels = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels(pixels, stride, 0);

            // Get component pointers
            var comps = new opj_image_comp_t[numComps];
            for (int i = 0; i < numComps; i++)
            {
                IntPtr compPtr = IntPtr.Add(img.comps, i * Marshal.SizeOf<opj_image_comp_t>());
                comps[i] = Marshal.PtrToStructure<opj_image_comp_t>(compPtr);
            }

            // Convert pixel data to component data
            int bytesPerPixel = stride / width;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int pixelOffset = y * stride + x * bytesPerPixel;
                    int dataOffset = (y * width + x) * 4; // OpenJPEG uses int32 for each pixel

                    if (numComps == 1)
                    {
                        Marshal.WriteInt32(comps[0].data, dataOffset, pixels[pixelOffset]);
                    }
                    else if (numComps >= 3)
                    {
                        Marshal.WriteInt32(comps[0].data, dataOffset, pixels[pixelOffset + 2]);     // R
                        Marshal.WriteInt32(comps[1].data, dataOffset, pixels[pixelOffset + 1]);     // G
                        Marshal.WriteInt32(comps[2].data, dataOffset, pixels[pixelOffset]);         // B
                        if (numComps == 4)
                            Marshal.WriteInt32(comps[3].data, dataOffset, pixels[pixelOffset + 3]); // A
                    }
                }
            }

            return image;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
                disposed = true;
        }
    }

    internal class StreamWrapper
    {
        private readonly Stream stream;

        public StreamWrapper(Stream stream)
        {
            this.stream = stream;
        }

        public IntPtr WriteCallback(IntPtr buffer, IntPtr nb_bytes, IntPtr user_data)
        {
            try
            {
                int size = (int)nb_bytes;
                byte[] data = new byte[size];
                Marshal.Copy(buffer, data, 0, size);
                stream.Write(data, 0, size);
                return nb_bytes;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        public int SeekCallback(long nb_bytes, IntPtr user_data)
        {
            try
            {
                stream.Seek(nb_bytes, SeekOrigin.Begin);
                return 1;
            }
            catch
            {
                return 0;
            }
        }

        public long SkipCallback(long nb_bytes, IntPtr user_data)
        {
            try
            {
                long currentPos = stream.Position;
                stream.Seek(nb_bytes, SeekOrigin.Current);
                return stream.Position - currentPos;
            }
            catch
            {
                return -1;
            }
        }
    }

    #region Native Methods and Structures

    internal static class NativeMethods
    {
        private const string DLL_NAME = "openjp2";
        public static bool CanBeUsed { get; private set; } = false;

        static NativeMethods()
        {
            string arch    = Environment.Is64BitProcess ? "x64" : "x86";
            string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, arch, "openjp2.dll");
            
            if (!File.Exists(dllPath))
            {
                dllPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), arch, "openjp2.dll");
            }
            
            if (File.Exists(dllPath))
            {
                SetDllDirectory(Path.GetDirectoryName(dllPath));
                IntPtr handle = LoadLibrary(dllPath);
                if (handle == IntPtr.Zero)
                    throw new DllNotFoundException($"Failed to load openjp2.dll from {dllPath}");
                CanBeUsed = true;
            }
            else
            {
                throw new DllNotFoundException($"openjp2.dll not found in {arch} subfolder");
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr opj_create_decompress(OPJ_CODEC_FORMAT format);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr opj_create_compress(OPJ_CODEC_FORMAT format);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void opj_destroy_codec(IntPtr p_codec);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void opj_set_default_decoder_parameters(ref opj_dparameters_t parameters);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void opj_set_default_encoder_parameters(ref opj_cparameters_t parameters);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool opj_setup_decoder(IntPtr p_codec, ref opj_dparameters_t parameters);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool opj_setup_encoder(IntPtr p_codec, ref opj_cparameters_t parameters, IntPtr image);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool opj_read_header(IntPtr p_stream, IntPtr p_codec, ref IntPtr p_image);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool opj_decode(IntPtr p_decompressor, IntPtr p_stream, IntPtr p_image);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool opj_start_compress(IntPtr p_codec, IntPtr p_image, IntPtr p_stream);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool opj_encode(IntPtr p_codec, IntPtr p_stream);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool opj_end_compress(IntPtr p_codec, IntPtr p_stream);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr opj_image_create(uint numcmpts, [In] opj_image_cmptparm_t[] cmptparms, OPJ_COLOR_SPACE clrspc);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void opj_image_destroy(IntPtr image);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr opj_stream_create(IntPtr p_buffer_size, bool p_is_input);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void opj_stream_destroy(IntPtr p_stream);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void opj_stream_set_read_function(IntPtr p_stream, opj_stream_read_fn p_function);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void opj_stream_set_write_function(IntPtr p_stream, opj_stream_write_fn p_function);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void opj_stream_set_skip_function(IntPtr p_stream, opj_stream_skip_fn p_function);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void opj_stream_set_seek_function(IntPtr p_stream, opj_stream_seek_fn p_function);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void opj_stream_set_user_data(IntPtr p_stream, IntPtr p_data, IntPtr p_function);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void opj_stream_set_user_data_length(IntPtr p_stream, ulong data_length);

        public static IntPtr opj_stream_create_default_memory_stream(byte[] data, uint data_size, bool p_is_input)
        {
            IntPtr stream = opj_stream_create(new IntPtr(0x100000), p_is_input);
            if (stream == IntPtr.Zero)
                return IntPtr.Zero;

            if (p_is_input && data != null)
            {
                var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
                var wrapper = new MemoryStreamWrapper(handle, data_size);
                var wrapperHandle = GCHandle.Alloc(wrapper);

                var readDelegate = new opj_stream_read_fn(wrapper.ReadCallback);
                var skipDelegate = new opj_stream_skip_fn(wrapper.SkipCallback);
                var seekDelegate = new opj_stream_seek_fn(wrapper.SeekCallback);

                opj_stream_set_read_function(stream, readDelegate);
                opj_stream_set_skip_function(stream, skipDelegate);
                opj_stream_set_seek_function(stream, seekDelegate);
                opj_stream_set_user_data(stream, GCHandle.ToIntPtr(wrapperHandle), Marshal.GetFunctionPointerForDelegate(new opj_stream_free_user_data_fn(FreeUserData)));
                opj_stream_set_user_data_length(stream, data_size);

                GC.KeepAlive(readDelegate);
                GC.KeepAlive(skipDelegate);
                GC.KeepAlive(seekDelegate);
            }

            return stream;
        }

        private static void FreeUserData(IntPtr userData)
        {
            if (userData != IntPtr.Zero)
            {
                GCHandle handle = GCHandle.FromIntPtr(userData);
                if (handle.Target is MemoryStreamWrapper wrapper)
                {
                    wrapper.Dispose();
                }
                handle.Free();
            }
        }
    }

    internal class SimpleStreamWrapper
    {
        private readonly IntPtr dataPtr;
        private readonly uint dataSize;
        private uint position;

        public readonly opj_stream_read_fn ReadDelegate;
        public readonly opj_stream_skip_fn SkipDelegate;
        public readonly opj_stream_seek_fn SeekDelegate;

        public SimpleStreamWrapper(IntPtr data, uint size)
        {
            dataPtr = data;
            dataSize = size;
            position = 0;

            ReadDelegate = new opj_stream_read_fn(Read);
            SkipDelegate = new opj_stream_skip_fn(Skip);
            SeekDelegate = new opj_stream_seek_fn(Seek);
        }

        private IntPtr Read(IntPtr buffer, IntPtr nb_bytes, IntPtr user_data)
        {
            uint bytesToRead = Math.Min((uint)nb_bytes, dataSize - position);
            if (bytesToRead > 0)
            {
                unsafe
                {
                    Buffer.MemoryCopy(
                        (byte*)dataPtr + position,
                        buffer.ToPointer(),
                        (uint)nb_bytes,
                        bytesToRead);
                }
                position += bytesToRead;
                return new IntPtr((int)bytesToRead);
            }
            return new IntPtr(-1);
        }

        private long Skip(long nb_bytes, IntPtr user_data)
        {
            long newPos = Math.Min(position + nb_bytes, dataSize);
            long skipped = newPos - position;
            position = (uint)newPos;
            return skipped;
        }

        private int Seek(long nb_bytes, IntPtr user_data)
        {
            if (nb_bytes >= 0 && nb_bytes <= dataSize)
            {
                position = (uint)nb_bytes;
                return 1;
            }
            return 0;
        }
    }

    internal class MemoryStreamWrapper : IDisposable
    {
        private GCHandle dataHandle;
        private readonly uint dataSize;
        private uint position;
        private bool disposed;

        public MemoryStreamWrapper(GCHandle handle, uint size)
        {
            dataHandle = handle;
            dataSize = size;
            position = 0;
        }

        public IntPtr ReadCallback(IntPtr buffer, IntPtr nb_bytes, IntPtr user_data)
        {
            if (disposed || !dataHandle.IsAllocated)
                return new IntPtr(-1);

            uint bytesToRead = Math.Min((uint)nb_bytes, dataSize - position);
            if (bytesToRead > 0)
            {
                IntPtr src = IntPtr.Add(dataHandle.AddrOfPinnedObject(), (int)position);
                unsafe
                {
                    Buffer.MemoryCopy(src.ToPointer(), buffer.ToPointer(), (uint)nb_bytes, bytesToRead);
                }
                position += bytesToRead;
                return new IntPtr((int)bytesToRead);
            }
            return new IntPtr(-1); // EOF
        }

        public long SkipCallback(long nb_bytes, IntPtr user_data)
        {
            if (disposed || !dataHandle.IsAllocated)
                return -1;

            long newPos = Math.Min(position + nb_bytes, dataSize);
            long skipped = newPos - position;
            position = (uint)newPos;
            return skipped;
        }

        public int SeekCallback(long nb_bytes, IntPtr user_data)
        {
            if (disposed || !dataHandle.IsAllocated)
                return 0;

            if (nb_bytes >= 0 && nb_bytes <= dataSize)
            {
                position = (uint)nb_bytes;
                return 1;
            }
            return 0;
        }

        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                if (dataHandle.IsAllocated)
                    dataHandle.Free();
            }
        }
    }

    // Delegates
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr opj_stream_read_fn(IntPtr p_buffer, IntPtr p_nb_bytes, IntPtr p_user_data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr opj_stream_write_fn(IntPtr p_buffer, IntPtr p_nb_bytes, IntPtr p_user_data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate long opj_stream_skip_fn(long p_nb_bytes, IntPtr p_user_data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int opj_stream_seek_fn(long p_nb_bytes, IntPtr p_user_data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void opj_stream_free_user_data_fn(IntPtr p_user_data);

    // Enums
    internal enum OPJ_COLOR_SPACE
    {
        OPJ_CLRSPC_UNKNOWN = -1,
        OPJ_CLRSPC_UNSPECIFIED = 0,
        OPJ_CLRSPC_SRGB = 1,
        OPJ_CLRSPC_GRAY = 2,
        OPJ_CLRSPC_SYCC = 3,
        OPJ_CLRSPC_EYCC = 4,
        OPJ_CLRSPC_CMYK = 5
    }

    internal enum OPJ_CODEC_FORMAT
    {
        OPJ_CODEC_UNKNOWN = -1,
        OPJ_CODEC_J2K = 0,
        OPJ_CODEC_JPT = 1,
        OPJ_CODEC_JP2 = 2,
        OPJ_CODEC_JPP = 3,
        OPJ_CODEC_JPX = 4
    }

    // Structures
    [StructLayout(LayoutKind.Sequential)]
    internal struct opj_image_t
    {
        public uint x0;
        public uint y0;
        public uint x1;
        public uint y1;
        public uint numcomps;
        public OPJ_COLOR_SPACE color_space;
        public IntPtr comps;
        public IntPtr icc_profile_buf;
        public uint icc_profile_len;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct opj_image_comp_t
    {
        public uint dx;
        public uint dy;
        public uint w;
        public uint h;
        public uint x0;
        public uint y0;
        public uint prec;
        public uint bpp;
        public uint sgnd;
        public uint resno_decoded;
        public uint factor;
        public IntPtr data;
        public ushort alpha;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct opj_image_cmptparm_t
    {
        public uint dx;
        public uint dy;
        public uint w;
        public uint h;
        public uint x0;
        public uint y0;
        public uint prec;
        public uint bpp;
        public uint sgnd;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct opj_dparameters_t
    {
        public uint cp_reduce;
        public uint cp_layer;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4096)]
        public string infile;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4096)]
        public string outfile;
        public int decod_format;
        public int cod_format;
        public uint DA_x0;
        public uint DA_x1;
        public uint DA_y0;
        public uint DA_y1;
        public int m_verbose;
        public uint tile_index;
        public uint nb_tile_to_decode;
        public int jpwl_correct;
        public int jpwl_exp_comps;
        public int jpwl_max_tiles;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct opj_cparameters_t
    {
        public int tile_size_on;
        public int cp_tx0;
        public int cp_ty0;
        public int cp_tdx;
        public int cp_tdy;
        public int cp_disto_alloc;
        public int cp_fixed_alloc;
        public int cp_fixed_quality;
        public IntPtr cp_matrice;
        public IntPtr cp_comment;
        public int csty;
        public OPJ_PROG_ORDER prog_order;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public opj_poc_t[] POC;
        public uint numpocs;
        public int tcp_numlayers;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 100)]
        public float[] tcp_rates;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 100)]
        public float[] tcp_distoratio;
        public int numresolution;
        public int cblockw_init;
        public int cblockh_init;
        public int mode;
        public int irreversible;
        public int roi_compno;
        public int roi_shift;
        public int res_spec;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 33)]
        public int[] prcw_init;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 33)]
        public int[] prch_init;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4096)]
        public string infile;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4096)]
        public string outfile;
        public int index_on;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4096)]
        public string index;
        public int image_offset_x0;
        public int image_offset_y0;
        public int subsampling_dx;
        public int subsampling_dy;
        public int decod_format;
        public int cod_format;
        public int jpwl_epc_on;
        public int jpwl_hprot_MH;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public int[] jpwl_hprot_TPH_tileno;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public int[] jpwl_hprot_TPH;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public int[] jpwl_pprot_tileno;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public int[] jpwl_pprot_packno;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public int[] jpwl_pprot;
        public int jpwl_sens_size;
        public int jpwl_sens_addr;
        public int jpwl_sens_range;
        public int jpwl_sens_MH;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public int[] jpwl_sens_TPH_tileno;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public int[] jpwl_sens_TPH;
        public OPJ_CINEMA_MODE cp_cinema;
        public int max_comp_size;
        public OPJ_RSIZ_CAPABILITIES cp_rsiz;
        public byte tp_on;
        public byte tp_flag;
        public byte tcp_mct;
        public int jpip_on;
        public IntPtr mct_data;
        public int max_cs_size;
        public ushort rsiz;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct opj_poc_t
    {
        public uint resno0, compno0;
        public uint layno1, resno1, compno1;
        public uint layno0, precno0, precno1;
        public OPJ_PROG_ORDER prg1, prg;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 5)]
        public string progorder;
        public uint tile;
        public int tx0, tx1, ty0, ty1;
        public uint layS, resS, compS, prcS;
        public uint layE, resE, compE, prcE;
        public uint txS, txE, tyS, tyE, dx, dy;
        public uint lay_t, res_t, comp_t, prc_t, tx0_t, ty0_t;
    }

    internal enum OPJ_PROG_ORDER
    {
        OPJ_PROG_UNKNOWN = -1,
        OPJ_LRCP = 0,
        OPJ_RLCP = 1,
        OPJ_RPCL = 2,
        OPJ_PCRL = 3,
        OPJ_CPRL = 4
    }

    internal enum OPJ_CINEMA_MODE
    {
        OPJ_OFF = 0,
        OPJ_CINEMA2K_24 = 1,
        OPJ_CINEMA2K_48 = 2,
        OPJ_CINEMA4K_24 = 3
    }

    internal enum OPJ_RSIZ_CAPABILITIES
    {
        OPJ_STD_RSIZ = 0,
        OPJ_CINEMA2K = 3,
        OPJ_CINEMA4K = 4,
        OPJ_MCT = 0x8100
    }

    #endregion
}