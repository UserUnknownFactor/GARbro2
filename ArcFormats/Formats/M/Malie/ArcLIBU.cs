using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using GameRes.Cryptography;

namespace GameRes.Formats.Malie
{
    [Export(typeof(ArchiveFormat))]
    public class LibUOpener : ArchiveFormat
    {
        public override string         Tag { get { return "LIBU"; } }
        public override string Description { get { return "Malie engine resource archive"; } }
        public override uint     Signature { get { return 0x5542494C; } } // 'LIBU'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public LibUOpener ()
        {
            Extensions = new string[] { "lib" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            using (var reader = LibUReader.Create (file))
            {
                if (!reader.ReadIndex())
                    return null;
                return new ArcFile (file, this, reader.Dir);
            }
        }
    }

    internal sealed class LibUReader : ILibIndexReader
    {
        BinaryReader    m_input;
        readonly long   m_max_offset;
        List<Entry>     m_dir = new List<Entry>();

        public List<Entry> Dir { get { return m_dir; } }

        public LibUReader (Stream input)
        {
            m_input = new BinaryReader (input, Encoding.Unicode);
            m_max_offset = input.Length;
        }

        public static LibUReader Create (ArcView file)
        {
            var input = file.CreateStream();
            return new LibUReader (input);
        }

        public static LibUReader Create (ArcView file, IMalieDecryptor decryptor)
        {
            var input = new EncryptedStream (file, decryptor);
            return new LibUReader (input);
        }

        public bool ReadIndex ()
        {
            return ReadDir ("", 0) && m_dir.Count > 0;
        }

        bool ReadDir (string root, long base_offset)
        {
            m_input.BaseStream.Position = base_offset;
            if (0x5542494C != m_input.ReadUInt32()) // 'LIBU'
                return false;
            m_input.ReadInt32();
            int count = m_input.ReadInt32();
            if (!ArchiveFormat.IsSaneCount (count))
                return false;
            if (m_dir.Capacity < m_dir.Count + count)
                m_dir.Capacity = m_dir.Count + count;

            long index_pos = base_offset + 0x10;
            for (int i = 0; i < count; ++i)
            {
                m_input.BaseStream.Position = index_pos;
                var name = ReadName();
                uint entry_size = m_input.ReadUInt32();
                long entry_offset = base_offset + m_input.ReadInt64();
                index_pos = m_input.BaseStream.Position;
                bool has_extension = -1 != name.IndexOf ('.');
                name = Path.Combine (root, name);
                if (!has_extension && ReadDir (name, entry_offset))
                    continue;

                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = entry_offset;
                entry.Size   = entry_size;
                if (!entry.CheckPlacement (m_max_offset))
                    return false;

                m_dir.Add (entry);
            }
            return true;
        }

        char[] m_name_buffer = new char[0x22];

        string ReadName ()
        {
            m_input.Read (m_name_buffer, 0, 0x22);
            int length = Array.IndexOf (m_name_buffer, '\0');
            if (-1 == length)
                length = m_name_buffer.Length;
            return new string (m_name_buffer, 0, length);
        }

        #region IDisposable methods
        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_input.Dispose();
                m_disposed = true;
            }
        }
        #endregion
    }
}
