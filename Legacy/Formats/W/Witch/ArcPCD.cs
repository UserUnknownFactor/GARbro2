using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Compression;
using GameRes.Utility;
using ICSharpCode.SharpZipLib.BZip2;

// [030627][Witch] Tsurupeta

namespace GameRes.Formats.Witch
{
    internal class PcdEntry : PackedEntry
    {
        public ImageMetaData    Info;
    }

    [Export(typeof(ArchiveFormat))]
    public class ImageDataOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PCD/IMAGE"; } }
        public override string Description { get { return "Witch images archive"; } }
        public override uint     Signature { get { return 0x47414D49; } } // 'IMAGEDATE'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "IMAGEDATE "))
                return null;
            int count = file.View.ReadInt32 (0xA);
            if (!IsSaneCount (count))
                return null;
            long index_offset = 0xE;
            var dir = new List<Entry> (count);
            var name_buffer = new byte[0x100];
            for (int i = 0; i < count; ++i)
            {
                int left    = file.View.ReadInt32 (index_offset+8);
                int top     = file.View.ReadInt32 (index_offset+0xC);
                int right   = file.View.ReadInt32 (index_offset+0x10);
                int bottom  = file.View.ReadInt32 (index_offset+0x14);
                index_offset += 0x18;
                int name_length = file.View.ReadInt32 (index_offset);
                if (name_length <= 0)
                    return null;
                if (name_length > name_buffer.Length)
                    name_buffer = new byte[name_length];
                file.View.Read (index_offset+4, name_buffer, 0, (uint)name_length);
                DecryptName (name_buffer, 0, name_length);
                var name = Binary.GetCString (name_buffer, 0, name_length);
                index_offset += 4 + name_length;

                name_length = file.View.ReadInt32 (index_offset);
                if (name_length > name_buffer.Length)
                    name_buffer = new byte[name_length];
                file.View.Read (index_offset+4, name_buffer, 0, (uint)name_length);
                DecryptName (name_buffer, 0, name_length);
                var frame_name = Binary.GetCString (name_buffer, 0, name_length);
                if (frame_name != "NO NAME")
                {
                    frame_name = frame_name.Replace ('/', '／');
                    name = Path.Combine (name, frame_name);
                }
                index_offset += 4 + name_length;

                var entry = new PcdEntry {
                    Name = name,
                    Type = "image",
                    Offset = file.View.ReadUInt32 (index_offset),
                };
                if (entry.Offset > file.MaxOffset)
                    return null;
                entry.Info = new ImageMetaData {
                    Width  = (uint)(right - left),
                    Height = (uint)(bottom - top),
                    OffsetX = left,
                    OffsetY = top,
                    BPP = 32,
                };
                dir.Add (entry);
                index_offset += 4;
            }
            SetAdjacentEntriesSize (dir, file.MaxOffset);
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            uint format_id = arc.File.View.ReadUInt32 (entry.Offset);
            switch (format_id)
            {
            case 0: // no compression
                return arc.File.CreateStream (entry.Offset+0x20, entry.Size-0x20);
            case 1: // zlib
            case 2: // bzip2
                break;
            default:
                return arc.File.CreateStream (entry.Offset, entry.Size);
            }
            var pent = (PcdEntry)entry;
            if (!pent.IsPacked)
            {
                pent.IsPacked = true;
                pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset+4);
            }
            var input = arc.File.CreateStream (entry.Offset+0x20, entry.Size-0x20);
            if (1 == format_id)
                return new ZLibStream (input, CompressionMode.Decompress);
            else
                return new BZip2InputStream (input);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var pent = (PcdEntry)entry;
            var input = arc.OpenBinaryEntry (entry);
            return new PsdFormatDecoder (input, pent);
        }

        internal static void DecryptName (byte[] buffer, int pos, int length)
        {
            for (int i = 0; i < length; ++i)
                buffer[pos+i] ^= 0xFF;
        }

        internal static void SetAdjacentEntriesSize (List<Entry> dir, long max_offset)
        {
            int count = dir.Count;
            for (int i = 1; i < count; ++i)
                dir[i-1].Size = (uint)(dir[i].Offset - dir[i-1].Offset);
            dir[count-1].Size = (uint)(max_offset - dir[count-1].Offset);
        }
    }

    internal class PsdFormatDecoder : BinaryImageDecoder
    {
        public PsdFormatDecoder (IBinaryStream input, PcdEntry entry) : base (input, entry.Info)
        {
        }

        protected override ImageData GetImageData ()
        {
            int bpp = Info.BPP / 8;
            int stride = (int)Info.Width * bpp;
            var pixels = m_input.ReadBytes (stride * (int)Info.Height);
            PixelFormat format = 4 == bpp ? PixelFormats.Bgra32 : PixelFormats.Gray8;
            return ImageData.Create (Info, format, null, pixels, stride);
        }
    }
}
