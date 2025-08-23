using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;

namespace GameRes.Formats.Tako
{
    [Export(typeof(ArchiveFormat))]
    public class MpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MPK/HG"; } }
        public override string Description { get { return "Studio Tako resource archive"; } }
        public override uint     Signature { get { return 0x502D4748; } } // 'HG-P'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public MpkOpener ()
        {
            Signatures = new uint[] { 0x502D4748, 0x572D4748 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count) || VFS.IsPathEqualsToFileName (file.Name, "00.mpk"))
                return null;
            var list_name = VFS.ChangeFileName (file.Name, "00.mpk");
            List<string> filelist;
            if (VFS.FileExists (list_name))
            {
                using (var s = VFS.OpenStream (list_name))
                using (var xs = new XoredStream (s, 0xA))
                using (var reader = new StreamReader (xs, Encodings.cp932))
                {
                    filelist = new List<string> (count);
                    string filename;
                    while ((filename = reader.ReadLine()) != null)
                        filelist.Add (filename);
                }
            }
            else
            {
                var base_name = Path.GetFileNameWithoutExtension (file.Name);
                filelist = Enumerable.Range (0, count).Select (x => string.Format ("{0}#{1:D4}", base_name, x)).ToList();
            }
            bool has_sizes = file.View.ReadByte (3) != 'P';
            uint index_offset = 8;
            uint record_size = has_sizes ? 8u : 4u;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = FormatCatalog.Instance.Create<Entry> (filelist[i]);
                entry.Offset = file.View.ReadUInt32 (index_offset);
                if (has_sizes)
                {
                    entry.Size = file.View.ReadUInt32 (index_offset+4);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                }
                else if (entry.Offset > file.MaxOffset)
                    return null;
                dir.Add (entry);
                index_offset += record_size;
            }
            if (!has_sizes)
            {
                for (int i = 1; i < count; ++i)
                {
                    dir[i-1].Size = (uint)(dir[i].Offset - dir[i-1].Offset);
                }
                dir[dir.Count-1].Size = (uint)(file.MaxOffset - dir[dir.Count-1].Offset);
            }
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var header = new byte[2] { (byte)'B', (byte)'M' };
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            input = new PrefixStream (header, input);
            var bin = new BinaryStream (input, entry.Name);
            try
            {
                return new ImageFormatDecoder (bin);
            }
            catch
            {
                bin.Dispose();
                throw;
            }
        }
    }
}
