using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Formats.DirectDraw;
using GameRes.Utility;

namespace GameRes.Formats.MSFormats
{
    internal class XnbMetaData : ImageMetaData
    {
        public int SurfaceFormat { get; set; }
        public int      MipCount { get; set; }
        public int    DataOffset { get; set; }
        public bool IsHeaderless { get; set; }
    }

    public enum XnaSurfaceFormat
    {
        Color    = 0,
        Bgr565   = 1,
        Bgra5551 = 2,
        Bgra4444 = 3,
        Dxt1     = 4,
        Dxt3     = 5,
        Dxt5     = 6
    }

    [Export(typeof(ImageFormat))]
    public class XnbFormat : ImageFormat
    {
        public override string         Tag { get { return "XNB"; } }
        public override string Description { get { return "XNA Game Studio texture format"; } }
        public override uint     Signature { get { return  0; } } // 'XNB'
        public override bool      CanWrite { get { return  true; } }

        const byte XNBVersion = 5;
        const byte PlatformPC = 0x77; // 'w'

        public XnbFormat()
        {
            Extensions = new[] { "xnb" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var startPos = stream.Position;
            var header = stream.ReadHeader (14); // Minimum header size
            if (header.Length < 10)
                return null;

            // Check for XNB magic
            if (header[0] != 'X' || header[1] != 'N' || header[2] != 'B')
            {
                return ReadHeaderlessMetaData (stream, startPos);
            }

            byte platform = header[3];
            byte version  = header[4];
            byte flags    = header[5];

            bool isCompressed = (flags & 0x80) != 0;
            uint fileSize = header.ToUInt32 (6);

            if (isCompressed)
            {
                uint decompressedSize = header.ToUInt32(10);
                stream.Position = 14;
                var decompressed = DecompressXnbLzx(stream, (int)(fileSize - 14), (int)decompressedSize);
                if (decompressed == null)
                    return null;

                using (var decompStream = new BinaryStream (new MemoryStream (decompressed), stream.Name))
                {
                    return ReadDecompressedMetaData (decompStream);
                }
            }
            else
            {
                stream.Position = 10; // after header
                return ReadDecompressedMetaData (stream);
            }
        }

        byte[] DecompressXnbLzx (IBinaryStream input, int compressedSize, int decompressedSize)
        {
            try
            {
                using (var decompressor = new XnbLzxDecoderStream (input.AsStream, decompressedSize, compressedSize))
                {
                    var output = new byte[decompressedSize];
                    int totalRead = 0;

                    while (totalRead < decompressedSize)
                    {
                        int read = decompressor.Read (output, totalRead, decompressedSize - totalRead);
                        if (read == 0)
                            break;
                        totalRead += read;
                    }

                    return totalRead == decompressedSize ? output : null;
                }
            }
            catch
            {
                return null;
            }
        }

        ImageMetaData ReadDecompressedMetaData (IBinaryStream stream)
        {
            try
            {
                // Read type readers
                int readerCount = ReadULEB128 (stream);
                for (int i = 0; i < readerCount; i++)
                {
                    string readerName = ReadString (stream);
                    stream.ReadInt32(); // Skip version
                }

                // Skip shared resources
                int sharedCount = ReadULEB128 (stream);
                for (int i = 0; i < sharedCount; i++)
                    ReadString (stream); // Skip shared resource

                // Read primary asset
                int assetIndex = ReadULEB128 (stream);

                // Read texture metadata
                int surfaceFormat = stream.ReadInt32();
                int width = stream.ReadInt32();
                int height = stream.ReadInt32();
                int mipCount = stream.ReadInt32();

                if (width <= 0 || width > 4096 || height <= 0 || height > 4096)
                    return null;

                return new XnbMetaData
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    BPP = GetBitsPerPixel (surfaceFormat),
                    SurfaceFormat = surfaceFormat,
                    MipCount = mipCount,
                    DataOffset = (int)stream.Position,
                    IsHeaderless = false
                };
            }
            catch
            {
                return null;
            }
        }

        ImageMetaData ReadHeaderlessMetaData (IBinaryStream stream, long startPos)
        {
            try
            {
                stream.Position = startPos + 10;
                if (stream.Length - stream.Position < 16)
                    return null;

                int surfaceFormat = stream.ReadInt32();
                int width = stream.ReadInt32();
                int height = stream.ReadInt32();
                int mipCount = stream.ReadInt32();

                if (width > 0 && width <= 4096 && height > 0 && height <= 4096 && 
                    mipCount > 0 && mipCount <= 16 && surfaceFormat >= 0 && surfaceFormat <= 6)
                {
                    return new XnbMetaData
                    {
                        Width         = (uint)width,
                        Height        = (uint)height,
                        BPP           = GetBitsPerPixel (surfaceFormat),
                        SurfaceFormat = surfaceFormat,
                        MipCount      = mipCount,
                        DataOffset    = (int)stream.Position,
                        IsHeaderless  = true
                    };
                }
            }
            catch { }
            return null;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (XnbMetaData)info;

            // Handle compressed XNB
            if (!meta.IsHeaderless)
            {
                stream.Position = 0;
                var header = stream.ReadHeader (10);
                if ((header[5] & 0x80) != 0) // Compressed flag
                {
                    stream.Position = 10;
                    uint decompressedSize = stream.ReadUInt32();

                    stream.Position = 14;
                    var decompressed = DecompressXnbLzx(stream, (int)(stream.Length - 14), (int)decompressedSize);

                    if (decompressed == null)
                        throw new InvalidFormatException ("Failed to decompress XNB data");

                    using (var decompStream = new BinaryStream (new MemoryStream (decompressed), stream.Name))
                    {
                        var tempMeta = ReadDecompressedMetaData (decompStream);
                        if (tempMeta is XnbMetaData xnbTempMeta)
                            meta.DataOffset = xnbTempMeta.DataOffset;
                        return ReadTextureData (decompStream, meta);
                    }
                }
            }

            stream.Position = meta.DataOffset;
            return ReadTextureData (stream, meta);
        }

        ImageData ReadTextureData (IBinaryStream stream, XnbMetaData meta)
        {
            // Read first mip level only
            uint dataSize = stream.ReadUInt32();
            if (dataSize > stream.Length - stream.Position)
                throw new InvalidFormatException ("Invalid texture data size");

            var textureData = stream.ReadBytes((int)dataSize);

            PixelFormat format = PixelFormats.Bgra32;
            byte[] pixels;

            switch ((XnaSurfaceFormat)meta.SurfaceFormat)
            {
                case XnaSurfaceFormat.Color:
                    // XNA Color format is RGBA, swap to BGRA for display
                    pixels = new byte[textureData.Length];
                    for (int i = 0; i < textureData.Length; i += 4)
                    {
                        pixels[i    ] = textureData[i + 2]; // B
                        pixels[i + 1] = textureData[i + 1]; // G
                        pixels[i + 2] = textureData[i    ]; // R
                        pixels[i + 3] = textureData[i + 3]; // A
                    }
                    break;

                case XnaSurfaceFormat.Bgr565:
                    pixels = ConvertBgr565 (textureData, (int)meta.Width, (int)meta.Height);
                    break;

                case XnaSurfaceFormat.Bgra5551:
                    pixels = ConvertBgra5551 (textureData, (int)meta.Width, (int)meta.Height);
                    break;

                case XnaSurfaceFormat.Bgra4444:
                    pixels = ConvertBgra4444 (textureData, (int)meta.Width, (int)meta.Height);
                    break;

                case XnaSurfaceFormat.Dxt1:
                    {
                        var decoder = new DxtDecoder (textureData, meta);
                        pixels = decoder.UnpackDXT1();
                    }
                    break;

                case XnaSurfaceFormat.Dxt3:
                    {
                        var decoder = new DxtDecoder (textureData, meta);
                        pixels = decoder.UnpackDXT3();
                    }
                    break;

                case XnaSurfaceFormat.Dxt5:
                    {
                        var decoder = new DxtDecoder (textureData, meta);
                        pixels = decoder.UnpackDXT5();
                    }
                    break;

                default:
                    throw new NotSupportedException ($"Surface format {meta.SurfaceFormat} not supported");
            }

            return ImageData.Create (meta, format, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            var bitmap = image.Bitmap;
            if (bitmap.Format != PixelFormats.Bgra32)
                bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgra32, null, 0);

            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;
            int stride = width * 4;
            var pixels = new byte[stride * height];
            bitmap.CopyPixels (pixels, stride, 0);

            // Convert BGRA to RGBA and set transparent pixels to black
            var rgbaPixels = new byte[pixels.Length];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i + 3] == 0) // Alpha is 0
                {
                    rgbaPixels[i    ] = 0; // R
                    rgbaPixels[i + 1] = 0; // G
                    rgbaPixels[i + 2] = 0; // B
                    rgbaPixels[i + 3] = 0; // A
                }
                else
                {
                    rgbaPixels[i    ] = pixels[i + 2]; // R
                    rgbaPixels[i + 1] = pixels[i + 1]; // G
                    rgbaPixels[i + 2] = pixels[i    ]; // B
                    rgbaPixels[i + 3] = pixels[i + 3]; // A
                }
            }

            using (var output = new BinaryWriter (file, Encoding.UTF8, true))
            {
                // Write XNB header
                output.Write((byte)'X');
                output.Write((byte)'N');
                output.Write((byte)'B');
                output.Write (PlatformPC);
                output.Write (XNBVersion);
                output.Write((byte)0); // No compression

                // Reserve space for file size
                long fileSizePos = output.BaseStream.Position;
                output.Write (0);

                // Write type readers
                WriteULEB128 (output, 1);
                WriteString (output, "Microsoft.Xna.Framework.Content.Texture2DReader");
                output.Write (0); // Version

                // Write shared resources
                WriteULEB128 (output, 0);

                // Write primary asset
                WriteULEB128 (output, 1);

                // Write texture data
                output.Write((int)XnaSurfaceFormat.Color);
                output.Write (width);
                output.Write (height);
                output.Write (1); // Mip count

                // Write texture bytes
                output.Write (rgbaPixels.Length);
                output.Write (rgbaPixels);

                // Update file size
                long endPos = output.BaseStream.Position;
                output.BaseStream.Position = fileSizePos;
                output.Write((int)endPos);
            }
        }

        static int GetBitsPerPixel (int surfaceFormat)
        {
            switch ((XnaSurfaceFormat)surfaceFormat)
            {
                case XnaSurfaceFormat.Color:
                    return 32;
                case XnaSurfaceFormat.Bgr565:
                case XnaSurfaceFormat.Bgra5551:
                case XnaSurfaceFormat.Bgra4444:
                    return 16;
                case XnaSurfaceFormat.Dxt1:
                case XnaSurfaceFormat.Dxt3:
                case XnaSurfaceFormat.Dxt5:
                    return 4;
                default:
                    return 0;
            }
        }

        static byte[] ConvertBgr565 (byte[] input, int width, int height)
        {
            var output = new byte[width * height * 4];
            int src = 0;
            int dst = 0;

            for (int i = 0; i < width * height; i++)
            {
                ushort pixel = (ushort)(input[src] | (input[src + 1] << 8));
                src += 2;

                byte b = (byte)(((pixel >> 0) & 0x1F) * 255 / 31);
                byte g = (byte)(((pixel >> 5) & 0x3F) * 255 / 63);
                byte r = (byte)(((pixel >> 11) & 0x1F) * 255 / 31);

                output[dst++] = b;
                output[dst++] = g;
                output[dst++] = r;
                output[dst++] = 255;
            }

            return output;
        }

        static byte[] ConvertBgra5551 (byte[] input, int width, int height)
        {
            var output = new byte[width * height * 4];
            int src = 0;
            int dst = 0;

            for (int i = 0; i < width * height; i++)
            {
                ushort pixel = (ushort)(input[src] | (input[src + 1] << 8));
                src += 2;

                byte b = (byte)(((pixel >> 0) & 0x1F) * 255 / 31);
                byte g = (byte)(((pixel >> 5) & 0x1F) * 255 / 31);
                byte r = (byte)(((pixel >> 10) & 0x1F) * 255 / 31);
                byte a = (byte)(((pixel >> 15) & 0x01) * 255);

                output[dst++] = b;
                output[dst++] = g;
                output[dst++] = r;
                output[dst++] = a;
            }

            return output;
        }

        static byte[] ConvertBgra4444 (byte[] input, int width, int height)
        {
            var output = new byte[width * height * 4];
            int src = 0;
            int dst = 0;

            for (int i = 0; i < width * height; i++)
            {
                ushort pixel = (ushort)(input[src] | (input[src + 1] << 8));
                src += 2;

                byte b = (byte)(((pixel >> 0) & 0xF) * 255 / 15);
                byte g = (byte)(((pixel >> 4) & 0xF) * 255 / 15);
                byte r = (byte)(((pixel >> 8) & 0xF) * 255 / 15);
                byte a = (byte)(((pixel >> 12) & 0xF) * 255 / 15);

                output[dst++] = b;
                output[dst++] = g;
                output[dst++] = r;
                output[dst++] = a;
            }

            return output;
        }

        static int ReadULEB128 (IBinaryStream stream)
        {
            int result = 0;
            int shift = 0;

            while (true)
            {
                byte b = stream.ReadUInt8();
                result |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    break;
                shift += 7;
            }

            return result;
        }

        static string ReadString (IBinaryStream stream)
        {
            int length = ReadULEB128 (stream);
            if (length == 0)
                return string.Empty;

            var bytes = stream.ReadBytes (length);
            return Encoding.UTF8.GetString (bytes);
        }

        static void WriteULEB128 (BinaryWriter writer, int value)
        {
            if (value == 0)
            {
                writer.Write((byte)0);
                return;
            }

            while (value != 0)
            {
                byte b = (byte)(value & 0x7F);
                value >>= 7;
                if (value != 0)
                    b |= 0x80;
                writer.Write (b);
            }
        }

        static void WriteString (BinaryWriter writer, string text)
        {
            if (string.IsNullOrEmpty (text))
            {
                writer.Write((byte)0);
                return;
            }

            var encoded = Encoding.UTF8.GetBytes (text);
            WriteULEB128 (writer, encoded.Length);
            writer.Write (encoded);
        }
    }

    internal class XnbLzxDecoderStream : Stream
    {
        XnbLzxDecoder dec;
        MemoryStream decompressedStream;

        public XnbLzxDecoderStream (Stream input, int decompressedSize, int compressedSize)
        {
            dec = new XnbLzxDecoder (16);
            Decompress (input, decompressedSize, compressedSize);
        }

        private void Decompress (Stream stream, int decompressedSize, int compressedSize)
        {
            decompressedStream = new MemoryStream (decompressedSize);
            long startPos = stream.Position;
            long pos = startPos;

            while (pos - startPos < compressedSize)
            {
                int hi = stream.ReadByte();
                int lo = stream.ReadByte();
                int block_size = (hi << 8) | lo;
                int frame_size = 0x8000;

                if (hi == 0xFF)
                {
                    hi = lo;
                    lo = stream.ReadByte();
                    frame_size = (hi << 8) | lo;
                    hi = stream.ReadByte();
                    lo = stream.ReadByte();
                    block_size = (hi << 8) | lo;
                    pos += 5;
                }
                else
                    pos += 2;

                if (block_size == 0 || frame_size == 0)
                    break;

                dec.Decompress (stream, block_size, decompressedStream, frame_size);
                pos += block_size;

                stream.Seek (pos, SeekOrigin.Begin);
            }

            decompressedStream.Seek (0, SeekOrigin.Begin);
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            return decompressedStream.Read (buffer, offset, count);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek (long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength (long value) => throw new NotSupportedException();
        public override void Write (byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose (bool disposing)
        {
            base.Dispose (disposing);
            if (disposing)
            {
                decompressedStream?.Dispose();
            }
        }
    }

    internal class XnbLzxDecoder
    {
        public static uint[] position_base = null;
        public static byte[] extra_bits = null;

        private LzxState m_state;

        public XnbLzxDecoder (int window)
        {
            uint wndsize = (uint)(1 << window);
            int posn_slots;

            // setup proper exception
            if (window < 15 || window > 21) throw new UnsupportedWindowSizeRange();

            // let's initialise our state
            m_state = new LzxState();
            m_state.actual_size = 0;
            m_state.window = new byte[wndsize];
            for (int i = 0; i < wndsize; i++) m_state.window[i] = 0xDC;
            m_state.actual_size = wndsize;
            m_state.window_size = wndsize;
            m_state.window_posn = 0;

            /* initialize static tables */
            if (extra_bits == null)
            {
                extra_bits = new byte[52];
                for (int i = 0, j = 0; i <= 50; i += 2)
                {
                    extra_bits[i] = extra_bits[i+1] = (byte)j;
                    if ((i != 0) && (j < 17)) j++;
                }
            }
            if (position_base == null)
            {
                position_base = new uint[51];
                for (int i = 0, j = 0; i <= 50; i++)
                {
                    position_base[i] = (uint)j;
                    j += 1 << extra_bits[i];
                }
            }

            /* calculate required position slots */
            if (window == 20) posn_slots = 42;
            else if (window == 21) posn_slots = 50;
            else posn_slots = window << 1;

            m_state.R0 = m_state.R1 = m_state.R2 = 1;
            m_state.main_elements = (ushort)(LzxConstants.NUM_CHARS + (posn_slots << 3));
            m_state.header_read = 0;
            m_state.frames_read = 0;
            m_state.block_remaining = 0;
            m_state.block_type = LzxConstants.BLOCKTYPE.INVALID;
            m_state.intel_curpos = 0;
            m_state.intel_started = 0;

            // yo dawg i herd u liek arrays so we put arrays in ur arrays so u can array while u array
            m_state.PRETREE_table = new ushort[(1 << LzxConstants.PRETREE_TABLEBITS) + (LzxConstants.PRETREE_MAXSYMBOLS << 1)];
            m_state.PRETREE_len = new byte[LzxConstants.PRETREE_MAXSYMBOLS + LzxConstants.LENTABLE_SAFETY];
            m_state.MAINTREE_table = new ushort[(1 << LzxConstants.MAINTREE_TABLEBITS) + (LzxConstants.MAINTREE_MAXSYMBOLS << 1)];
            m_state.MAINTREE_len = new byte[LzxConstants.MAINTREE_MAXSYMBOLS + LzxConstants.LENTABLE_SAFETY];
            m_state.LENGTH_table = new ushort[(1 << LzxConstants.LENGTH_TABLEBITS) + (LzxConstants.LENGTH_MAXSYMBOLS << 1)];
            m_state.LENGTH_len = new byte[LzxConstants.LENGTH_MAXSYMBOLS + LzxConstants.LENTABLE_SAFETY];
            m_state.ALIGNED_table = new ushort[(1 << LzxConstants.ALIGNED_TABLEBITS) + (LzxConstants.ALIGNED_MAXSYMBOLS << 1)];
            m_state.ALIGNED_len = new byte[LzxConstants.ALIGNED_MAXSYMBOLS + LzxConstants.LENTABLE_SAFETY];
            /* initialise tables to 0 (because deltas will be applied to them) */
            for (int i = 0; i < LzxConstants.MAINTREE_MAXSYMBOLS; i++) m_state.MAINTREE_len[i] = 0;
            for (int i = 0; i < LzxConstants.LENGTH_MAXSYMBOLS; i++) m_state.LENGTH_len[i] = 0;
        }

        public int Decompress (Stream inData, int inLen, Stream outData, int outLen)
        {
            BitBuffer bitbuf = new BitBuffer (inData);
            long startpos = inData.Position;
            long endpos = inData.Position + inLen;

            byte[] window = m_state.window;

            uint window_posn = m_state.window_posn;
            uint window_size = m_state.window_size;
            uint R0 = m_state.R0;
            uint R1 = m_state.R1;
            uint R2 = m_state.R2;
            uint i, j;

            int togo = outLen, this_run, main_element, match_length, match_offset, length_footer, extra, verbatim_bits;
            int rundest, runsrc, copy_length, aligned_bits;

            bitbuf.InitBitStream();

            /* read header if necessary */
            if (m_state.header_read == 0)
            {
                uint intel = bitbuf.ReadBits (1);
                if (intel != 0)
                {
                    // read the filesize
                    i = bitbuf.ReadBits (16); j = bitbuf.ReadBits (16);
                    m_state.intel_filesize = (int)((i << 16) | j);
                }
                m_state.header_read = 1;
            }

            /* main decoding loop */
            while (togo > 0)
            {
                /* last block finished, new block expected */
                if (m_state.block_remaining == 0)
                {
                    // TODO may screw something up here
                    if (m_state.block_type == LzxConstants.BLOCKTYPE.UNCOMPRESSED) {
                        if((m_state.block_length & 1) == 1) inData.ReadByte(); /* realign bitstream to word */
                        bitbuf.InitBitStream();
                    }

                    m_state.block_type = (LzxConstants.BLOCKTYPE)bitbuf.ReadBits (3);;
                    i = bitbuf.ReadBits (16);
                    j = bitbuf.ReadBits (8);
                    m_state.block_remaining = m_state.block_length = (uint)((i << 8) | j);

                    switch (m_state.block_type)
                    {
                    case LzxConstants.BLOCKTYPE.ALIGNED:
                        for (i = 0, j = 0; i < 8; i++) { j = bitbuf.ReadBits (3); m_state.ALIGNED_len[i] = (byte)j; }
                        MakeDecodeTable (LzxConstants.ALIGNED_MAXSYMBOLS, LzxConstants.ALIGNED_TABLEBITS,
                                        m_state.ALIGNED_len, m_state.ALIGNED_table);
                        /* rest of aligned header is same as verbatim */
                        goto case LzxConstants.BLOCKTYPE.VERBATIM;

                    case LzxConstants.BLOCKTYPE.VERBATIM:
                        ReadLengths (m_state.MAINTREE_len, 0, 256, bitbuf);
                        ReadLengths (m_state.MAINTREE_len, 256, m_state.main_elements, bitbuf);
                        MakeDecodeTable (LzxConstants.MAINTREE_MAXSYMBOLS, LzxConstants.MAINTREE_TABLEBITS,
                                        m_state.MAINTREE_len, m_state.MAINTREE_table);
                        if (m_state.MAINTREE_len[0xE8] != 0) m_state.intel_started = 1;

                        ReadLengths (m_state.LENGTH_len, 0, LzxConstants.NUM_SECONDARY_LENGTHS, bitbuf);
                        MakeDecodeTable (LzxConstants.LENGTH_MAXSYMBOLS, LzxConstants.LENGTH_TABLEBITS,
                                        m_state.LENGTH_len, m_state.LENGTH_table);
                        break;

                    case LzxConstants.BLOCKTYPE.UNCOMPRESSED:
                        m_state.intel_started = 1; /* because we can't assume otherwise */
                        bitbuf.EnsureBits (16); /* get up to 16 pad bits into the buffer */
                        if (bitbuf.GetBitsLeft() > 16) inData.Seek(-2, SeekOrigin.Current); /* and align the bitstream! */
                        byte hi, mh, ml, lo;
                        lo = (byte)inData.ReadByte(); ml = (byte)inData.ReadByte(); mh = (byte)inData.ReadByte(); hi = (byte)inData.ReadByte();
                        R0 = (uint)(lo | ml << 8 | mh << 16 | hi << 24);
                        lo = (byte)inData.ReadByte(); ml = (byte)inData.ReadByte(); mh = (byte)inData.ReadByte(); hi = (byte)inData.ReadByte();
                        R1 = (uint)(lo | ml << 8 | mh << 16 | hi << 24);
                        lo = (byte)inData.ReadByte(); ml = (byte)inData.ReadByte(); mh = (byte)inData.ReadByte(); hi = (byte)inData.ReadByte();
                        R2 = (uint)(lo | ml << 8 | mh << 16 | hi << 24);
                        break;

                    default:
                        return -1;
                    }
                }

                /* buffer exhaustion check */
                if (inData.Position > (startpos + inLen))
                {
                    /* it's possible to have a file where the next run is less than
                     * 16 bits in size. In this case, the READ_HUFFSYM() macro used
                     * in building the tables will exhaust the buffer, so we should
                     * allow for this, but not allow those accidentally read bits to
                     * be used (so we check that there are at least 16 bits
                     * remaining - in this boundary case they aren't really part of
                     * the compressed data)
                     */
                    if (inData.Position > (startpos+inLen+2) || bitbuf.GetBitsLeft() < 16) return -1; //TODO throw proper exception
                }

                while((this_run = (int)m_state.block_remaining) > 0 && togo > 0)
                {
                    if (this_run > togo) this_run = togo;
                    togo -= this_run;
                    m_state.block_remaining -= (uint)this_run;

                    /* apply 2^x-1 mask */
                    window_posn &= window_size - 1;
                    /* runs can't straddle the window wraparound */
                    if((window_posn + this_run) > window_size)
                        return -1;

                    switch (m_state.block_type)
                    {
                    case LzxConstants.BLOCKTYPE.VERBATIM:
                        while (this_run > 0)
                        {
                            main_element = (int)ReadHuffSym (m_state.MAINTREE_table, m_state.MAINTREE_len,
                                                       LzxConstants.MAINTREE_MAXSYMBOLS, LzxConstants.MAINTREE_TABLEBITS,
                                                       bitbuf);
                            if (main_element < LzxConstants.NUM_CHARS)
                            {
                                /* literal: 0 to NUM_CHARS-1 */
                                window[window_posn++] = (byte)main_element;
                                this_run--;
                            }
                            else
                            {
                                /* match: NUM_CHARS + ((slot<<3) | length_header (3 bits)) */
                                main_element -= LzxConstants.NUM_CHARS;

                                match_length = main_element & LzxConstants.NUM_PRIMARY_LENGTHS;
                                if (match_length == LzxConstants.NUM_PRIMARY_LENGTHS)
                                {
                                    length_footer = (int)ReadHuffSym (m_state.LENGTH_table, m_state.LENGTH_len,
                                                                LzxConstants.LENGTH_MAXSYMBOLS, LzxConstants.LENGTH_TABLEBITS,
                                                                bitbuf);
                                    match_length += length_footer;
                                }
                                match_length += LzxConstants.MIN_MATCH;

                                match_offset = main_element >> 3;

                                if (match_offset > 2)
                                {
                                    /* not repeated offset */
                                    if (match_offset != 3)
                                    {
                                        extra = extra_bits[match_offset];
                                        verbatim_bits = (int)bitbuf.ReadBits((byte)extra);
                                        match_offset = (int)position_base[match_offset] - 2 + verbatim_bits;
                                    }
                                    else
                                    {
                                        match_offset = 1;
                                    }

                                    /* update repeated offset LRU queue */
                                    R2 = R1; R1 = R0; R0 = (uint)match_offset;
                                }
                                else if (match_offset == 0)
                                {
                                    match_offset = (int)R0;
                                }
                                else if (match_offset == 1)
                                {
                                    match_offset = (int)R1;
                                    R1 = R0; R0 = (uint)match_offset;
                                }
                                else /* match_offset == 2 */
                                {
                                    match_offset = (int)R2;
                                    R2 = R0; R0 = (uint)match_offset;
                                }

                                rundest = (int)window_posn;
                                this_run -= match_length;

                                /* copy any wrapped around source data */
                                if (window_posn >= match_offset)
                                {
                                    /* no wrap */
                                    runsrc = rundest - match_offset;
                                }
                                else
                                {
                                    runsrc = rundest + ((int)window_size - match_offset);
                                    copy_length = match_offset - (int)window_posn;
                                    if (copy_length < match_length)
                                    {
                                        match_length -= copy_length;
                                        window_posn += (uint)copy_length;
                                        while (copy_length-- > 0) window[rundest++] = window[runsrc++];
                                        runsrc = 0;
                                    }
                                }
                                window_posn += (uint)match_length;

                                /* copy match data - no worries about destination wraps */
                                while (match_length-- > 0) window[rundest++] = window[runsrc++];
                            }
                        }
                        break;

                    case LzxConstants.BLOCKTYPE.ALIGNED:
                        while (this_run > 0)
                        {
                            main_element = (int)ReadHuffSym (m_state.MAINTREE_table, m_state.MAINTREE_len,
                                                                         LzxConstants.MAINTREE_MAXSYMBOLS, LzxConstants.MAINTREE_TABLEBITS,
                                                                         bitbuf);

                            if (main_element < LzxConstants.NUM_CHARS)
                            {
                                /* literal 0 to NUM_CHARS-1 */
                                window[window_posn++] = (byte)main_element;
                                this_run--;
                            }
                            else
                            {
                                /* match: NUM_CHARS + ((slot<<3) | length_header (3 bits)) */
                                main_element -= LzxConstants.NUM_CHARS;

                                match_length = main_element & LzxConstants.NUM_PRIMARY_LENGTHS;
                                if (match_length == LzxConstants.NUM_PRIMARY_LENGTHS)
                                {
                                    length_footer = (int)ReadHuffSym (m_state.LENGTH_table, m_state.LENGTH_len,
                                                                     LzxConstants.LENGTH_MAXSYMBOLS, LzxConstants.LENGTH_TABLEBITS,
                                                                     bitbuf);
                                    match_length += length_footer;
                                }
                                match_length += LzxConstants.MIN_MATCH;

                                match_offset = main_element >> 3;

                                if (match_offset > 2)
                                {
                                    /* not repeated offset */
                                    extra = extra_bits[match_offset];
                                    match_offset = (int)position_base[match_offset] - 2;
                                    if (extra > 3)
                                    {
                                        /* verbatim and aligned bits */
                                        extra -= 3;
                                        verbatim_bits = (int)bitbuf.ReadBits((byte)extra);
                                        match_offset += (verbatim_bits << 3);
                                        aligned_bits = (int)ReadHuffSym (m_state.ALIGNED_table, m_state.ALIGNED_len,
                                                                   LzxConstants.ALIGNED_MAXSYMBOLS, LzxConstants.ALIGNED_TABLEBITS,
                                                                   bitbuf);
                                        match_offset += aligned_bits;
                                    }
                                    else if (extra == 3)
                                    {
                                        /* aligned bits only */
                                        aligned_bits = (int)ReadHuffSym (m_state.ALIGNED_table, m_state.ALIGNED_len,
                                                                   LzxConstants.ALIGNED_MAXSYMBOLS, LzxConstants.ALIGNED_TABLEBITS,
                                                                   bitbuf);
                                        match_offset += aligned_bits;
                                    }
                                    else if (extra > 0) /* extra==1, extra==2 */
                                    {
                                        /* verbatim bits only */
                                        verbatim_bits = (int)bitbuf.ReadBits((byte)extra);
                                        match_offset += verbatim_bits;
                                    }
                                    else /* extra == 0 */
                                    {
                                        /* ??? */
                                        match_offset = 1;
                                    }

                                    /* update repeated offset LRU queue */
                                    R2 = R1; R1 = R0; R0 = (uint)match_offset;
                                }
                                else if( match_offset == 0)
                                {
                                    match_offset = (int)R0;
                                }
                                else if (match_offset == 1)
                                {
                                    match_offset = (int)R1;
                                    R1 = R0; R0 = (uint)match_offset;
                                }
                                else /* match_offset == 2 */
                                {
                                    match_offset = (int)R2;
                                    R2 = R0; R0 = (uint)match_offset;
                                }

                                rundest = (int)window_posn;
                                this_run -= match_length;

                                /* copy any wrapped around source data */
                                if (window_posn >= match_offset)
                                {
                                    /* no wrap */
                                    runsrc = rundest - match_offset;
                                }
                                else
                                {
                                    runsrc = rundest + ((int)window_size - match_offset);
                                    copy_length = match_offset - (int)window_posn;
                                    if (copy_length < match_length)
                                    {
                                        match_length -= copy_length;
                                        window_posn += (uint)copy_length;
                                        while (copy_length-- > 0) window[rundest++] = window[runsrc++];
                                        runsrc = 0;
                                    }
                                }
                                window_posn += (uint)match_length;

                                /* copy match data - no worries about destination wraps */
                                while (match_length-- > 0) window[rundest++] = window[runsrc++];
                            }
                        }
                        break;

                    case LzxConstants.BLOCKTYPE.UNCOMPRESSED:
                        if((inData.Position + this_run) > endpos) return -1; //TODO throw proper exception
                        byte[] temp_buffer = new byte[this_run];
                        inData.Read (temp_buffer, 0, this_run);
                        temp_buffer.CopyTo (window, (int)window_posn);
                        window_posn += (uint)this_run;
                        break;

                    default:
                        return -1;
                    }
                }
            }

            if (togo != 0) return -1;
            int start_window_pos = (int)window_posn;
            if (start_window_pos == 0) start_window_pos = (int)window_size;
            start_window_pos -= outLen;
            outData.Write (window, start_window_pos, outLen);

            m_state.window_posn = window_posn;
            m_state.R0 = R0;
            m_state.R1 = R1;
            m_state.R2 = R2;

            // TODO finish intel E8 decoding
            /* intel E8 decoding */
            if((m_state.frames_read++ < 32768) && m_state.intel_filesize != 0)
            {
                if (outLen <= 6 || m_state.intel_started == 0)
                {
                    m_state.intel_curpos += outLen;
                }
                else
                {
                    int dataend = outLen - 10;
                    uint curpos = (uint)m_state.intel_curpos;

                    m_state.intel_curpos = (int)curpos + outLen;

                    while (outData.Position < dataend)
                    {
                        if (outData.ReadByte() != 0xE8) { curpos++; continue; }
                    }
                }
                return -1;
            }
            return 0;
        }

        // READ_LENGTHS (table, first, last)
        // if (lzx_read_lens (LENTABLE (table), first, last, bitsleft))
        //   return ERROR (ILLEGAL_DATA)
        // 

        private int MakeDecodeTable (uint nsyms, uint nbits, byte[] length, ushort[] table)
        {
            ushort sym;
            uint leaf;
            byte bit_num = 1;
            uint fill;
            uint pos            = 0; /* the current position in the decode table */
            uint table_mask        = (uint)(1 << (int)nbits);
            uint bit_mask        = table_mask >> 1; /* don't do 0 length codes */
            uint next_symbol    = bit_mask;    /* base of allocation for long codes */

            /* fill entries for codes short enough for a direct mapping */
            while (bit_num <= nbits )
            {
                for (sym = 0; sym < nsyms; sym++)
                {
                    if (length[sym] == bit_num)
                    {
                        leaf = pos;

                        if((pos += bit_mask) > table_mask) return 1; /* table overrun */

                        /* fill all possible lookups of this symbol with the symbol itself */
                        fill = bit_mask;
                        while (fill-- > 0) table[leaf++] = sym;
                    }
                }
                bit_mask >>= 1;
                bit_num++;
            }

            /* if there are any codes longer than nbits */
            if (pos != table_mask)
            {
                /* clear the remainder of the table */
                for (sym = (ushort)pos; sym < table_mask; sym++) table[sym] = 0;

                /* give ourselves room for codes to grow by up to 16 more bits */
                pos <<= 16;
                table_mask <<= 16;
                bit_mask = 1 << 15;

                while (bit_num <= 16)
                {
                    for (sym = 0; sym < nsyms; sym++)
                    {
                        if (length[sym] == bit_num)
                        {
                            leaf = pos >> 16;
                            for (fill = 0; fill < bit_num - nbits; fill++)
                            {
                                /* if this path hasn't been taken yet, 'allocate' two entries */
                                if (table[leaf] == 0)
                                {
                                    table[(next_symbol << 1)] = 0;
                                    table[(next_symbol << 1) + 1] = 0;
                                    table[leaf] = (ushort)(next_symbol++);
                                }
                                /* follow the path and select either left or right for next bit */
                                leaf = (uint)(table[leaf] << 1);
                                if(((pos >> (int)(15-fill)) & 1) == 1) leaf++;
                            }
                            table[leaf] = sym;

                            if((pos += bit_mask) > table_mask) return 1;
                        }
                    }
                    bit_mask >>= 1;
                    bit_num++;
                }
            }

            /* full talbe? */
            if (pos == table_mask) return 0;

            /* either erroneous table, or all elements are 0 - let's find out. */
            for (sym = 0; sym < nsyms; sym++) if (length[sym] != 0) return 1;
            return 0;
        }

        private void ReadLengths (byte[] lens, uint first, uint last, BitBuffer bitbuf)
        {
            uint x, y;
            int z;

            // hufftbl pointer here?
            for (x = 0; x < 20; x++)
            {
                y = bitbuf.ReadBits (4);
                m_state.PRETREE_len[x] = (byte)y;
            }
            MakeDecodeTable (LzxConstants.PRETREE_MAXSYMBOLS, LzxConstants.PRETREE_TABLEBITS,
                            m_state.PRETREE_len, m_state.PRETREE_table);

            for (x = first; x < last;)
            {
                z = (int)ReadHuffSym (m_state.PRETREE_table, m_state.PRETREE_len,
                                LzxConstants.PRETREE_MAXSYMBOLS, LzxConstants.PRETREE_TABLEBITS, bitbuf);
                if (z == 17)
                {
                    y = bitbuf.ReadBits (4); y += 4;
                    while (y-- != 0) lens[x++] = 0;
                }
                else if (z == 18)
                {
                    y = bitbuf.ReadBits (5); y += 20;
                    while (y-- != 0) lens[x++] = 0;
                }
                else if (z == 19)
                {
                    y = bitbuf.ReadBits (1); y += 4;
                    z = (int)ReadHuffSym (m_state.PRETREE_table, m_state.PRETREE_len,
                                LzxConstants.PRETREE_MAXSYMBOLS, LzxConstants.PRETREE_TABLEBITS, bitbuf);
                    z = lens[x] - z; if (z < 0) z += 17;
                    while (y-- != 0) lens[x++] = (byte)z;
                }
                else
                {
                    z = lens[x] - z; if (z < 0) z += 17;
                    lens[x++] = (byte)z;
                }
            }
        }

        private uint ReadHuffSym (ushort[] table, byte[] lengths, uint nsyms, uint nbits, BitBuffer bitbuf)
        {
            uint i, j;
            bitbuf.EnsureBits (16);
            if((i = table[bitbuf.PeekBits((byte)nbits)]) >= nsyms)
            {
                j = (uint)(1 << (int)((sizeof (uint)*8) - nbits));
                do
                {
                    j >>= 1; i <<= 1; i |= (bitbuf.GetBuffer() & j) != 0 ? (uint)1 : 0;
                    if (j == 0) return 0;
                } while((i = table[i]) >= nsyms);
            }
            j = lengths[i];
            bitbuf.RemoveBits((byte)j);

            return i;
        }

        #region Our BitBuffer Class
        private class BitBuffer
        {
            uint buffer;
            byte bitsleft;
            Stream byteStream;

            public BitBuffer (Stream stream)
            {
                byteStream = stream;
                InitBitStream();
            }

            public void InitBitStream()
            {
                buffer = 0;
                bitsleft = 0;
            }

            public void EnsureBits (byte bits)
            {
                while (bitsleft < bits) {
                    int lo = (byte)byteStream.ReadByte();
                    int hi = (byte)byteStream.ReadByte();
                    //int amount2shift = sizeof (uint)*8 - 16 - bitsleft;
                    buffer |= (uint)(((hi << 8) | lo) << (sizeof (uint)*8 - 16 - bitsleft));
                    bitsleft += 16;
                }
            }

            public uint PeekBits (byte bits)
            {
                return (buffer >> ((sizeof (uint)*8) - bits));
            }

            public void RemoveBits (byte bits)
            {
                buffer <<= bits;
                bitsleft -= bits;
            }

            public uint ReadBits (byte bits)
            {
                uint ret = 0;

                if (bits > 0)
                {
                    EnsureBits (bits);
                    ret = PeekBits (bits);
                    RemoveBits (bits);
                }

                return ret;
            }

            public uint GetBuffer()
            {
                return buffer;
            }

            public byte GetBitsLeft()
            {
                return bitsleft;
            }
        }
        #endregion

        struct LzxState {
            public uint                       R0, R1, R2;         /* for the LRU offset system                */
            public ushort                     main_elements;      /* number of main tree elements                */
            public int                        header_read;        /* have we started decoding at all yet?     */
            public LzxConstants.BLOCKTYPE     block_type;         /* type of this block                        */
            public uint                       block_length;       /* uncompressed length of this block         */
            public uint                       block_remaining;    /* uncompressed bytes still left to decode    */
            public uint                       frames_read;        /* the number of CFDATA blocks processed    */
            public int                        intel_filesize;     /* magic header value used for transform    */
            public int                        intel_curpos;       /* current offset in transform space        */
            public int                        intel_started;      /* have we seen any translateable data yet?    */

            public ushort[]      PRETREE_table;
            public byte[]        PRETREE_len;
            public ushort[]      MAINTREE_table;
            public byte[]        MAINTREE_len;
            public ushort[]      LENGTH_table;
            public byte[]        LENGTH_len;
            public ushort[]      ALIGNED_table;
            public byte[]        ALIGNED_len;

            public uint        actual_size;
            public byte[]      window;
            public uint        window_size;
            public uint        window_posn;
        }
    }

    /* CONSTANTS */
    struct LzxConstants {
        public const ushort MIN_MATCH = 2;
        public const ushort MAX_MATCH = 257;
        public const ushort NUM_CHARS = 256;
        public enum BLOCKTYPE {
            INVALID      = 0,
            VERBATIM     = 1,
            ALIGNED      = 2,
            UNCOMPRESSED = 3
        }
        public const ushort PRETREE_NUM_ELEMENTS  =  20;
        public const ushort ALIGNED_NUM_ELEMENTS  =  8;
        public const ushort NUM_PRIMARY_LENGTHS   =  7;
        public const ushort NUM_SECONDARY_LENGTHS =  249;

        public const ushort PRETREE_MAXSYMBOLS  =    PRETREE_NUM_ELEMENTS;
        public const ushort PRETREE_TABLEBITS   =    6;
        public const ushort MAINTREE_MAXSYMBOLS =    NUM_CHARS + 50*8;
        public const ushort MAINTREE_TABLEBITS  =    12;
        public const ushort LENGTH_MAXSYMBOLS   =    NUM_SECONDARY_LENGTHS + 1;
        public const ushort LENGTH_TABLEBITS    =    12;
        public const ushort ALIGNED_MAXSYMBOLS  =    ALIGNED_NUM_ELEMENTS;
        public const ushort ALIGNED_TABLEBITS   =    7;

        public const ushort LENTABLE_SAFETY =        64;
    }

    /* EXCEPTIONS */
    class UnsupportedWindowSizeRange : Exception { }
}