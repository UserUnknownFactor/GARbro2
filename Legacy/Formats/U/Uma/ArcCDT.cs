using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

// [030530][Uma] Pretty Candead!

namespace GameRes.Formats.Uma
{
    [Export(typeof(ArchiveFormat))]
    public class CdtOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CDT/UMA"; } }
        public override string Description { get { return "Uma resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public CdtOpener ()
        {
            Extensions = new string[] { "cdt", "spt" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasAnyOfExtensions ("cdt", "spt"))
                return null;
            using (var input = file.CreateStream())
            {
                var dir = new List<Entry>();
                while (input.PeekByte() != -1)
                {
                    var name = input.ReadCString (0x10);
                    if (string.IsNullOrWhiteSpace (name))
                        return null;
                    var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                    entry.UnpackedSize = input.ReadUInt32();
                    entry.Size         = input.ReadUInt32();
                    entry.IsPacked     = input.ReadInt32() != 0;
                    entry.Offset       = input.Position;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    input.Seek (entry.Size, SeekOrigin.Current);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return input;
            return new LzssStream (input);
        }
    }
}
