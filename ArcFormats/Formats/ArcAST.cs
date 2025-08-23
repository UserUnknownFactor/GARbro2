using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.AST
{
    internal class AstArchive : ArcFile
    {
        public AstArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir)
            : base (arc, impl, dir)
        {
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "AST"; } }
        public override string Description { get { return "AST script engine resource archive"; } }
        public override uint     Signature { get { return 0x32435241; } } // 'ARC2'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public ArcOpener ()
        {
            Extensions = new string[] { "" };
            Signatures = new uint[] { 0x32435241, 0x31435241 }; // 'ARC2', 'ARC1'
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadByte (3) - 0x30;
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            var name_buf = new byte[32];
            var dir = new List<Entry> (count);
            long index_offset = 8;
            uint next_offset = file.View.ReadUInt32 (index_offset);
            for (int i = 0; i < count; ++i)
            {
                uint offset = next_offset;
                uint size   = file.View.ReadUInt32 (index_offset+4);
                int name_length = file.View.ReadByte (index_offset+8);
                if (name_length > name_buf.Length)
                    name_buf = new byte[name_length];
                file.View.Read (index_offset+9, name_buf, 0, (uint)name_length);
                if (i+1 == count)
                    next_offset = (uint)file.MaxOffset;
                else
                    next_offset = file.View.ReadUInt32 (index_offset+9+name_length);
                if (0 != offset && offset != file.MaxOffset)
                {
                    if (2 == version)
                        for (int j = 0; j < name_length; ++j)
                            name_buf[j] ^= 0xff;
                    uint packed_size;
                    if (0 == next_offset)
                        packed_size = size;
                    else if (next_offset >= offset)
                        packed_size = next_offset - offset;
                    else
                        return null;
                    string name = Encodings.cp932.GetString (name_buf, 0, name_length);
                    var entry = new PackedEntry
                    {
                        Name = name,
                        Type = FormatCatalog.Instance.GetTypeFromName (name),
                        Offset = offset,
                        Size = packed_size,
                        UnpackedSize = size,
                        IsPacked = packed_size != size,
                    };
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                index_offset += 9 + name_length;
            }
            if (2 == version)
                return new AstArchive (file, this, dir);
            else
                return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var pent = entry as PackedEntry;
            if (null == pent || !(arc is AstArchive))
                return input;
            if (!pent.IsPacked)
            {
                if (0xB8B1AF76 == input.Signature) // PNG signature ^ FF
                    return new XoredStream (input, 0xFF);
                return input;
            }
            var lzss = new LzssStream (input);
            lzss.Config.FrameFill = 0xFF;
            return new XoredStream (lzss, 0xFF);
        }
    }
}
