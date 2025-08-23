using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;

namespace GameRes.Formats.Minori
{
    [Export(typeof(ArchiveFormat))]
    public class FarOpener : ArchiveFormat
    {
        public override string         Tag { get { return "FAR/DC"; } }
        public override string Description { get { return "Minori Dreamcast resource archive"; } }
        public override uint     Signature { get { return 0x32566146; } } // 'FaV2'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen(ArcView file)
        {
            string archivename = Path.GetFileNameWithoutExtension(file.Name);
            int count = file.View.ReadInt32(4) - 2;
            if (!IsSaneCount(count) || count == 0)
                return null;

            var dir = new List<Entry>(count);
            for (int i = 0; i < count; i++)
            {
                uint offset = file.View.ReadUInt32(16 + i * 8);
                uint size = file.View.ReadUInt32(16 + i * 8 + 4);
                offset *= 2048;
                size *= 2048;

                if (offset > file.MaxOffset || offset + size > file.MaxOffset)
                    throw new InvalidFormatException();

                if (size == 0) continue;

                if (file.View.ReadString (offset, 4, Encoding.ASCII) == "LZSS")
                {
                    offset += 12;
                    size = file.View.ReadUInt32 (offset - 4);
                }

                var entry = Create<Entry>(archivename + i.ToString("D5") + ".bip");
                entry.Offset = offset;
                entry.Size = size;
                dir.Add(entry);
            }

            if (dir.Count == 0)
                return null;

            return new ArcFile (file, this, dir);
        }
    }
}
