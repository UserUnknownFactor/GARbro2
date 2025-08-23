using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.Nekopunch
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/NEKOPUNCH"; } }
        public override string Description { get { return "Studio Nekopunch resource archive"; } }
        public override uint     Signature { get { return 0x4B434150; } } // 'PACK'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PakOpener ()
        {
            Extensions = new string[] { "pak" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            bool is_packed = 0 != file.View.ReadInt32 (8);
            uint index_offset = 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x40);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                index_offset += 0x40;
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.IsPacked = is_packed;
                entry.UnpackedSize  = file.View.ReadUInt32 (index_offset);
                entry.Size          = file.View.ReadUInt32 (index_offset+4);
                entry.Offset        = file.View.ReadUInt32 (index_offset+8);
                index_offset += 0x0C;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
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

    /// <summary>
    /// Link DOW extension to WaveAudio format.
    /// </summary>
    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "DOW")]
    [ExportMetadata("Target", "WAV")]
    public class DowAudio : ResourceAlias { }
}
