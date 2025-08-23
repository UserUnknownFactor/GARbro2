using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Softpal
{
    [Export(typeof(ArchiveFormat))]
    public class PacOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAC/SOFTPAL"; } }
        public override string Description { get { return "Archive format used by Softpal subsidiaries"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PacOpener ()
        {
            Extensions = new string[] { "pac" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 0x3FE;
            uint name_length = 0x20;
            uint first_offset = file.View.ReadUInt32 (index_offset+name_length+4);
            if (first_offset != index_offset + (uint)count*(name_length+8))
            {
                name_length = 0x10;
                first_offset = file.View.ReadUInt32 (index_offset+name_length+4);
                if (first_offset != index_offset + (uint)count*(name_length+8))
                    return null;
            }
            if (first_offset >= file.MaxOffset)
                return null;
            return ReadIndex (file, count, index_offset, name_length);
        }

        protected ArcFile ReadIndex (ArcView file, int count, uint index_offset, uint name_length)
        {
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, name_length);
                index_offset += name_length;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Size   = file.View.ReadUInt32 (index_offset);
                entry.Offset = file.View.ReadUInt32 (index_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if ("image" == entry.Type || "audio" == entry.Type || entry.Size <= 16
                || '$' != arc.File.View.ReadByte (entry.Offset))
                return base.OpenEntry (arc, entry);
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            int count = (data.Length - 16) / 4;
            if (count > 0)
            {
                unsafe
                {
                    fixed (byte* data8 = &data[16])
                    {
                        uint* data32 = (uint*)data8;
                        int shift = 4;
                        for (uint* data_end = data32 + count; data32 != data_end; ++data32)
                        {
                            byte* byte_ptr = (byte*)data32;
                            *byte_ptr = Binary.RotByteL (*byte_ptr, shift++);
                            *data32 ^= 0x084DF873u ^ 0xFF987DEEu;
                        }
                    }
                }
            }
            return new BinMemoryStream (data, entry.Name);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class Pac2Opener : PacOpener
    {
        public override string         Tag { get { return "PAC/AMUSE"; } }
        public override string Description { get { return "Archive format used by Amuse Craft subsidiaries"; } }
        public override uint     Signature { get { return 0x20434150; } } // 'PAC '
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 0x804;
            uint name_length = 0x20;
            uint first_offset = file.View.ReadUInt32 (index_offset+name_length+4);
            if (first_offset != index_offset + (uint)count*(name_length+8))
                return null;

            return ReadIndex (file, count, index_offset, name_length);
        }
    }
}
