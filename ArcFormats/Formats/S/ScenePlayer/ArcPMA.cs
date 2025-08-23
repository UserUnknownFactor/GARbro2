using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.ScenePlayer
{
    [Export(typeof(ArchiveFormat))]
    public class PmaOpener : PmxOpener
    {
        public override string         Tag { get { return "PMA"; } }
        public override string Description { get { return "ScenePlayer animation resource"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".pma")
                || file.View.ReadByte (0) != (0x78^0x21))
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var input = CreatePmxStream (file);
            bool index_complete = false;
            try
            {
                using (var index = new BinaryStream (input, file.Name, true))
                {
                    int count = index.ReadInt32();
                    if (!IsSaneCount (count))
                        return null;
                    var dir = new List<Entry> (count);
                    for (int i = 0; i < count; ++i)
                    {
                        index.ReadByte();
                        var offset = index.Position;
                        if (index.ReadUInt16() != 0x4D42) // 'BM'
                            return null;
                        uint size = index.ReadUInt32();
                        var entry = new Entry {
                            Name = string.Format ("{0}#{1}.bmp", base_name, i),
                            Type = "image",
                            Offset = offset,
                            Size =  size,
                        };
                        dir.Add (entry);
                        index.Position = offset + size;
                    }
                    index_complete = true;
                    return new PmxArchive (file, this, dir, input);
                }
            }
            finally
            {
                if (!index_complete)
                    input.Dispose();
            }
        }
    }
}
