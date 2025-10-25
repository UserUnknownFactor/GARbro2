using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Solfa
{
    [Export(typeof(ArchiveFormat))]
    public class SgfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SGF/SAS"; } }
        public override string Description { get { return "SAS old engine image archive"; } }
        public override uint     Signature { get { return  0x30464753; } } // 'SGF0'
        public override bool  IsHierarchic { get { return  false; } }

        public SgfOpener ()
        {
            Extensions = new string[] { "sgf" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "SGF0"))
                return null;

            uint count = file.View.ReadUInt32 (4);
            if (count == 0 || count > 1000)
                return null;

            uint headerSize = 8 + count * 4;  // Header + offset table size
            var entries = new List<Entry> ();

            for (uint i = 0; i < count; i++)
            {
                uint relativeOffset = file.View.ReadUInt32 (8 + i * 4);
                uint absoluteOffset = relativeOffset + headerSize;

                if (absoluteOffset + 48 > file.MaxOffset)
                    continue;

                uint dataSize   = file.View.ReadUInt32 (absoluteOffset + 0x08);
                uint dataOffset = file.View.ReadUInt32 (absoluteOffset + 0x0C);
                uint width      = file.View.ReadUInt32 (absoluteOffset + 0x1C);
                uint height     = file.View.ReadUInt32 (absoluteOffset + 0x20);

                if (width == 0 || width > 0x8000 || height == 0 || height > 0x8000)
                    continue;
        
                long totalSize = 48 + dataOffset + dataSize;
                entries.Add (new Entry {
                    Name   = string.Format ("image_{0:D4}", i),
                    Type   = "image",
                    Offset = absoluteOffset,
                    Size   = totalSize
                });
            }

            return entries.Count > 0 ? new ArcFile (file, this, entries) : null;
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            byte flags = arc.File.View.ReadByte(entry.Offset);
            if ((flags & 0x02) == 0)
                return base.OpenEntry(arc, entry);

            var header = arc.File.View.ReadBytes(entry.Offset, 48);
            uint dataSize   = LittleEndian.ToUInt32(header, 0x08);
            uint dataOffset = LittleEndian.ToUInt32(header, 0x0C);

            var pixels = new byte[dataSize];
            using (var input = arc.File.CreateStream(entry.Offset + 48 + dataOffset, entry.Size - 48 - dataOffset))
            using (var decoder = new IarDecompressor(input))
            {
                decoder.Unpack(pixels);
            }

            using (var mem = new MemoryStream())
            using (var writer = new BinaryWriter(mem))
            {
                header[0] &= 0xFD; // clear compression flag
                LittleEndian.Pack(0u, header, 0x0C); // clear data offset
                writer.Write(header);
                writer.Write(pixels);
                return new BinMemoryStream(mem.ToArray());
            }
        }
    }

    [Export(typeof(ImageFormat))]
    public class SgfImageFormat : ImageFormat
    {
        public override string         Tag { get { return "SGF"; } }
        public override string Description { get { return "SAS old engine image"; } }
        public override uint     Signature { get { return  0; } }

        public override ImageMetaData ReadMetaData(IBinaryStream file)
        {
            var header = file.ReadHeader(48);

            byte flags = header[0];
            ushort formatType = header.ToUInt16(0x24);
            ushort channels = header.ToUInt16(0x26);

            if ((formatType != 1 && formatType != 2 && formatType != 5 && formatType != 6) ||
                (channels != 3 && channels != 4))
                return null;

            uint width = header.ToUInt32(0x1C);
            uint height = header.ToUInt32(0x20);

            if (width == 0 || width > 0x8000 || height == 0 || height > 0x8000)
                return null;

            return new ImageMetaData {
                Width   = width,
                Height  = height,
                OffsetX = header.ToInt32(0x14),
                OffsetY = header.ToInt32(0x18),
                BPP     = channels == 4 ? 32 : 24
            };
        }

        public override ImageData Read(IBinaryStream file, ImageMetaData info)
        {
            var header = file.ReadHeader(48);

            uint dataSize     = header.ToUInt32(0x08);
            uint dataOffset   = header.ToUInt32(0x0C);
            int stride        = header.ToInt32 (0x28);
            ushort formatType = header.ToUInt16(0x24);

            file.Position = 48 + dataOffset;
            byte[] pixels = file.ReadBytes((int)dataSize);

            if (pixels.Length < info.Width * info.Height * info.BPP/8)
            {
                var padded = new byte[info.Width * info.Height * info.BPP / 8];
                Buffer.BlockCopy (pixels, 0, padded, 0, pixels.Length);
                pixels = padded;
            }

            PixelFormat format;
            if (info.BPP == 32)
                format = (formatType == 6) ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
            else
                format = (formatType == 2) ? PixelFormats.Bgr24 : PixelFormats.Rgb24;

            return ImageData.Create(info, format, null, pixels, stride);
        }

        public override void Write(Stream file, ImageData image)
        {
            throw new NotImplementedException();
        }
    }
}