using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Elf
{
    internal class HizMetaData : ImageMetaData
    {
        public bool IsPacked;
        public uint DataOffset;
        public uint UnpackedSize;
        public uint Version;

        public uint Unknown1;
        public uint Unknown2;
        public uint Unknown3;
        public uint Unknown4;
    }

    [Export(typeof(ImageFormat))]
    public class HizFormat : ImageFormat
    {
        public override string         Tag { get { return "HIZ"; } }
        public override string Description { get { return "elf bitmap format"; } }
        public override uint     Signature { get { return  HIZ_SIGNATURE; } }
        public override bool      CanWrite { get { return  true; } }

        readonly static uint HIZ_SIGNATURE = 0x007A6968; // 'hiz'
        readonly static uint   HIZ_VERSION = 100;
        readonly static uint     WIDTH_XOR = 0xAA5A5A5A;
        readonly static uint    HEIGHT_XOR = 0xAC9326AF;
        readonly static uint    PACKED_XOR = 0x375A8436;
        readonly static uint  UNPACKED_XOR = 0x19739D6A;

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.Name.HasExtension (".bin"))
            {
                var pak_name = Path.ChangeExtension (file.Name, ".pak");
                if (VFS.FileExists (pak_name))
                    return null;  // this is HED format, not HIZ
            }

            file.Position = 4;
            uint first_value = file.ReadUInt32();

            uint width, height, compression_flag, unpacked_size;
            uint data_offset;
            uint version = 0;
            uint unknown1 = 0, unknown2 = 0, unknown3 = 0, unknown4 = 0;

            if (first_value == HIZ_VERSION)  // 100
            {
                version          = first_value;
                width            = file.ReadUInt32() ^ WIDTH_XOR;
                height           = file.ReadUInt32() ^ HEIGHT_XOR;
                compression_flag = file.ReadUInt32() ^ PACKED_XOR;
                unpacked_size    = file.ReadUInt32() ^ UNPACKED_XOR;

                unknown1 = file.ReadUInt32();
                unknown2 = file.ReadUInt32();
                unknown3 = file.ReadUInt32();
                unknown4 = file.ReadUInt32();

                data_offset = 0x4C;
            }
            else
            {
                width            = first_value ^ WIDTH_XOR;
                height           = file.ReadUInt32() ^ HEIGHT_XOR;
                compression_flag = file.ReadUInt32() ^ PACKED_XOR;
                unpacked_size    = file.ReadUInt32() ^ UNPACKED_XOR;
                data_offset      = 0x18;
            }

            if (width == 0 || width > 10000 || height == 0 || height > 10000)
                return null;
            if (unpacked_size != width * height * 4)
                return null;
            if (compression_flag > 1)
                return null;

            return new HizMetaData {
                Width        = width,
                Height       = height,
                BPP          = 32,
                IsPacked     = compression_flag == 1,
                DataOffset   = data_offset,
                UnpackedSize = unpacked_size,
                Version      = version,
                Unknown1     = unknown1,
                Unknown2     = unknown2,
                Unknown3     = unknown3,
                Unknown4     = unknown4,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (HizMetaData)info;
            var pixels = new byte[meta.UnpackedSize];
            stream.Position = meta.DataOffset;

            if (meta.IsPacked)
            {
                using (var lzss = new LzssStream (stream.AsStream, LzssMode.Decompress, true))
                {
                    var channel = new byte[info.Width * info.Height];
                    for (int p = 0; p < 4; ++p)
                    {
                        var n_read = lzss.Read (channel, 0, channel.Length);
                        if (channel.Length != n_read)
                            throw new InvalidFormatException ($"Unexpected end of file {channel.Length} != {n_read}");
                        int src = 0;
                        for (int i = p; i < pixels.Length; i += 4)
                            pixels[i] = channel[src++];
                    }
                }
            }
            else
            {
                var n_read = stream.Read (pixels, 0, pixels.Length);
                if (pixels.Length != n_read)
                    throw new InvalidFormatException ($"Unexpected end of file {pixels.Length} != {n_read}");
            }

            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            var bitmap = image.Bitmap;
            if (bitmap.Format != PixelFormats.Bgra32)
                bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgra32, null, 0);

            uint width  = (uint)bitmap.PixelWidth;
            uint height = (uint)bitmap.PixelHeight;
            uint unpacked_size = width * height * 4;

            var pixels = new byte[unpacked_size];
            bitmap.CopyPixels (pixels, (int)width * 4, 0);

            bool compress = unpacked_size > 256;

            byte[] compressed_data;
            using (var output_stream = new MemoryStream())
            {
                if (compress)
                {
                    using (var lzss = new LzssStream (output_stream, LzssMode.Compress, true))
                    {
                        for (int ch = 0; ch < 4; ++ch)
                        {
                            var channel = new byte[width * height];
                            for (int i = 0, src = ch; i < channel.Length; ++i, src += 4)
                                channel[i] = pixels[src];
                            lzss.Write (channel, 0, channel.Length);
                        }
                    }
                }
                else
                    output_stream.Write (pixels, 0, pixels.Length);

                compressed_data = output_stream.ToArray();
            }

            uint checksum = 0;// ? Crc32.Compute (compressed_data, 0, compressed_data.Length);
            using (var writer = new BinaryWriter (file))
            {
                writer.Write (HIZ_SIGNATURE);
                writer.Write (HIZ_VERSION);
                writer.Write (width  ^ WIDTH_XOR);
                writer.Write (height ^ HEIGHT_XOR);
                writer.Write ((compress ? 1u : 0u) ^ PACKED_XOR);
                writer.Write (unpacked_size ^ UNPACKED_XOR);

                writer.Write (checksum);
                writer.Write (width);
                writer.Write (height);
                writer.Write (0u);

                long currentPos = writer.BaseStream.Position;
                writer.Write (new byte[0x4C - currentPos]); // filler
                writer.Write (compressed_data);
            }
        }
    }
}