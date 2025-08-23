using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;

namespace GameRes.Formats.Nexton
{
    internal class NextonEntry : Entry
    {
        public byte Key;
    }

    [Export(typeof(ArchiveFormat))]
    public class LstOpener : ArchiveFormat
    {
        public override string         Tag { get { return "LST"; } }
        public override string Description { get { return "Nexton LikeC engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public LstOpener ()
        {
            Extensions = new string[] { "" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            string lstname = file.Name + ".lst";
            if (!VFS.FileExists (lstname))
                return null;
            using (var lst = VFS.OpenView (lstname))
            {
                List<Entry> dir = null;
                try
                {
                    dir = OpenMoon (lst, file.MaxOffset);
                }
                catch { /* ignore parse errors */ }
                if (null == dir)
                    dir = OpenNexton (lst, file.MaxOffset);
                if (null == dir)
                    return null;
                return new ArcFile (file, this, dir);
            }
        }

        private List<Entry> OpenMoon (ArcView lst, long max_offset)
        {
            int count = (int)(lst.View.ReadUInt32 (0) ^ 0xcccccccc);
            if (count <= 0 || (4 + count*0x2c) > lst.MaxOffset)
                return null;
            var cp932 = Encodings.cp932.WithFatalFallback();
            var dir = new List<Entry> (count);
            uint index_offset = 4;
            for (int i = 0; i < count; ++i)
            {
                string name = ReadName (lst, index_offset+8, 0x24, 0xcc, cp932);
                if (0 == name.Length)
                    return null;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = lst.View.ReadUInt32 (index_offset) ^ 0xcccccccc;
                entry.Size   = lst.View.ReadUInt32 (index_offset+4) ^ 0xcccccccc;
                if (!entry.CheckPlacement (max_offset))
                    return null;
                dir.Add (entry);
                index_offset += 0x2c;
            }
            return dir;
        }

        static string[] TypeExt = new string[] { "LST", "SNX", "BMP", "PNG", "WAV", "OGG" };

        private List<Entry> OpenNexton (ArcView lst, long max_offset)
        {
            uint key = lst.View.ReadByte (3); // guess xor key
            if (0 == key)
                return null;
            key |= key << 8;
            key |= key << 16;
            int count = (int)(lst.View.ReadUInt32 (0) ^ key);
            if (count <= 0 || (4 + count*0x4c) > lst.MaxOffset)
                return null;
            var cp932 = Encodings.cp932.WithFatalFallback();
            var dir = new List<Entry> (count);
            uint index_offset = 4;
            for (int i = 0; i < count; ++i)
            {
                string name = ReadName (lst, index_offset+8, 0x40, (byte)key, cp932);
                if (0 == name.Length)
                    return null;
                var entry = new NextonEntry {
                    Name = name,
                    Offset = lst.View.ReadUInt32 (index_offset) ^ key,
                    Size   = lst.View.ReadUInt32 (index_offset+4) ^ key,
                };
                if (!entry.CheckPlacement (max_offset))
                    return null;
                int type = lst.View.ReadInt32 (index_offset+0x48);
                if (type >= 0 && type < TypeExt.Length)
                {
                    entry.Name = Path.ChangeExtension (name, TypeExt[type]);
                    if (2 == type || 3 == type)
                        entry.Type = "image";
                    else if (4 == type || 5 == type)
                        entry.Type = "audio";
                    else if (1 == type)
                    {
                        entry.Type = "script";
                        entry.Key = (byte)(key + 1);
                    }
                }
                dir.Add (entry);
                index_offset += 0x4c;
            }
            return dir;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var nxent = entry as NextonEntry;
            if (null == nxent || 0 == nxent.Key)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            for (int i = 0; i != data.Length; ++i)
                data[i] ^= nxent.Key;
            return new BinMemoryStream (data, entry.Name);
        }

        private static string ReadName (ArcView view, long offset, uint size, byte key, Encoding enc)
        {
            byte[] buffer = new byte[size];
            uint n;
            for (n = 0; n < size; ++n)
            {
                byte b = view.View.ReadByte (offset+n);
                if (0 == b)
                    break;
                if (b != key)
                    b ^= key;
                buffer[n] = b;
            }
            return enc.GetString (buffer, 0, (int)n);
        }
    }
}
