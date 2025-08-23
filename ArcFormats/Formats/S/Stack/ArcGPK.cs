using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using GameRes.Compression;

namespace GameRes.Formats.Stack
{
    internal class GpkEntry : PackedEntry
    {
        public byte[]   Header;
    }

    [Export(typeof(ArchiveFormat))]
    public class GpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "GPK/STACK"; } }
        public override string Description { get { return "Stack script engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            long idx_offset = file.MaxOffset - 32;
            if (idx_offset <= 0)
                return null;
            if (!file.View.AsciiEqual (idx_offset, "STKFile0PIDX") ||
                !file.View.AsciiEqual (idx_offset+16, "STKFile0PACKFILE"))
                return null;
            uint idx_size = file.View.ReadUInt32 (idx_offset+12);
            if (idx_size > idx_offset)
                return null;
            idx_offset -= idx_size;
            var key = QueryKey (file.Name);
            if (null == key)
                return null;
            Stream input = file.CreateStream (idx_offset, idx_size);
            input = new ByteStringEncryptedStream (input, key);
            input.Position = 4;
            using (input = new ZLibStream (input, CompressionMode.Decompress))
            using (var index = new BinaryStream (input, file.Name))
            {
                var name_buffer = new byte[0x100];
                var dir = new List<Entry>();
                while (index.PeekByte() != -1)
                {
                    int name_length = index.ReadUInt16() * 2;
                    if (0 == name_length)
                        break;
                    if (name_length > name_buffer.Length)
                        name_buffer = new byte[name_length];
                    index.Read (name_buffer, 0, name_length);
                    var name = Encoding.Unicode.GetString (name_buffer, 0, name_length);
                    var entry = Create<GpkEntry> (name);

                    index.ReadInt32();
                    index.ReadInt16();
                    entry.Offset = index.ReadUInt32();
                    entry.Size   = index.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    index.ReadInt32();
                    entry.UnpackedSize = index.ReadUInt32();
                    entry.IsPacked = entry.UnpackedSize != 0;
                    int header_length = index.ReadUInt8();
                    if (header_length > 0)
                        entry.Header = index.ReadBytes (header_length);
                    dir.Add (entry);
                }
                if (0 == dir.Count)
                    return null;
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var gent = (GpkEntry)entry;
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (gent.Header != null)
                input = new PrefixStream (gent.Header, input);
            if (gent.IsPacked)
                input = new ZLibStream (input, CompressionMode.Decompress);
            return input;
        }

        byte[] QueryKey (string arc_name)
        {
            if (VFS.IsVirtual)
                return null;
            var dir = VFS.GetDirectoryName (arc_name);
            var parent_dir = Directory.GetParent (dir).FullName;
            var exe_files = VFS.GetFiles (VFS.CombinePath (parent_dir, "*.exe")).Concat (VFS.GetFiles (VFS.CombinePath (dir, "*.exe")));
            foreach (var exe_entry in exe_files)
            {
                try
                {
                    using (var exe = new ExeFile.ResourceAccessor (exe_entry.Name))
                    {
                        var code = exe.GetResource ("CIPHERCODE", "CODE");
                        if (null == code)
                            continue;
                        if (20 == code.Length)
                            code = new CowArray<byte> (code, 4, 16).ToArray();
                        return code;
                    }
                }
                catch { /* ignore errors */ }
            }
            return null;
        }
    }
}
