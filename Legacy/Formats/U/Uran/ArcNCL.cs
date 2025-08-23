using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;
using ICSharpCode.SharpZipLib.BZip2;

// [000224][Uran] P.S. 3 ~Harem Night~
// [980828][Uran] College Terra Story
// [971218][Uran] GirlDollToy

namespace GameRes.Formats.Uran
{
    internal class NclEntry : PackedEntry
    {
        public byte     Method;
    }

    [Export(typeof(ArchiveFormat))]
    public class NclOpener : ArchiveFormat
    {
        public override string         Tag { get { return "NCL"; } }
        public override string Description { get { return "Uran resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".ncl") || file.MaxOffset > uint.MaxValue)
                return null;
            long offset = 0;
            var dir = new List<Entry>();
            while (offset < file.MaxOffset)
            {
                uint size = file.View.ReadUInt32 (offset);
                if (0 == size)
                    break;
                uint unpacked_size = file.View.ReadUInt32 (offset+4);
                uint name_length = file.View.ReadUInt16 (offset+8);
                if (0 == name_length || name_length > 0x100)
                    return null;
                var name = file.View.ReadString (offset+10, name_length);
                offset += 10 + name_length;
                var entry = FormatCatalog.Instance.Create<NclEntry> (name);
                entry.Size = size;
                entry.UnpackedSize = unpacked_size;
                uint header_size = file.View.ReadUInt16 (offset+4);
                offset += 6 + header_size;
                entry.Offset = offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.Method = (byte)(file.View.ReadByte (offset) - 10);
                entry.IsPacked = entry.Method != 1;
                entry.Offset++;
                entry.Size--;
                dir.Add (entry);
                offset += size;
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            input = new NclSubStream (input, 10);
            var nclent = entry as NclEntry;
            if (nclent != null && nclent.IsPacked)
            {
                if (2 == nclent.Method)
                    input = new ZLibStream (input, CompressionMode.Decompress);
                else if (3 == nclent.Method)
                    input = new BZip2InputStream (input);
            }
            return input;
        }
    }

    public class NclSubStream : InputProxyStream
    {
        private byte        m_key;

        public NclSubStream (Stream stream, byte key, bool leave_open = false)
            : base (stream, leave_open)
        {
            m_key = key;
        }

        #region System.IO.Stream methods
        public override int Read (byte[] buffer, int offset, int count)
        {
            int read = BaseStream.Read (buffer, offset, count);
            for (int i = 0; i < read; ++i)
            {
                buffer[offset+i] -= m_key;
            }
            return read;
        }

        public override int ReadByte ()
        {
            int b = BaseStream.ReadByte();
            if (-1 != b)
            {
                b = (byte)(b - m_key);
            }
            return b;
        }
        #endregion
    }
}
