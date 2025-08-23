using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Types
{
    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/TYPES"; } }
        public override string Description { get { return "Types resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".arc") || file.MaxOffset > uint.MaxValue)
                return null;
            var dir = new List<Entry>();
            using (var input = file.CreateStream())
            {
                var header = new byte[8];
                while (input.PeekByte() != -1)
                {
                    uint size = input.ReadUInt32();
                    if (0 == size)
                        break;
                    uint idx = input.ReadUInt32();
                    int name_length = input.ReadUInt16();
                    if (0 == name_length || name_length > 0x100)
                        return null;
                    var name = input.ReadCString (name_length);
                    if (string.IsNullOrWhiteSpace (name))
                        return null;
                    var entry = new Entry {
                        Name = name,
                        Offset = input.Position,
                        Size = size,
                    };
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    input.Read (header, 0, header.Length);
                    if (header.AsciiEqual (0, "RIFF"))
                        entry.Type = "audio";
                    else if (header.AsciiEqual (4, "TPGF"))
                        entry.Type = "image";
                    dir.Add (entry);
                    input.Position = entry.Offset + entry.Size;
                }
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
