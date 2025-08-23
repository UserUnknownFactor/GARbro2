using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;

namespace GameRes.Formats.CatSystem
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/CSPACK"; } }
        public override string Description { get { return "Cat System resource archive"; } }
        public override uint     Signature { get { return 0x61507343; } } // 'CsPa'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "CsPack"))
                return null;

            int version = file.View.ReadByte (6) - '0';
            if (version < 1 || version > 2)
                return null;

            uint data_offset = file.View.ReadUInt32 (8);
            int entry_size = 12 * version; // 8B/20B name, 4B offset
            int count = (int)(data_offset - 12) / entry_size;
            if (!IsSaneCount (count) || count == 0) // it can contain 0 entries
                return null;

            var dir = new List<Entry> (count);
            uint next_offset = data_offset;
            var name_decoder = new CsNameDecryptor (version == 1 ? 0xC : 0x1E,
                                                    version == 1 ? 0x8 : 0x10);
            var entry_buffer = new byte[entry_size];
            int index_pos = 12;
            for (int i = 0; i < count; ++i)
            {
                file.View.Read (index_pos, entry_buffer, 0, (uint)entry_size);
                var name = name_decoder.Decrypt (entry_buffer);
                var entry = Create<Entry> (name);
                entry.Offset = next_offset;
                next_offset = entry_buffer.ToUInt32 (0)
                            ^ entry_buffer.ToUInt32 (4)
                            ^ entry_buffer.ToUInt32 (entry_size - 4);
                entry.Size = (uint)(next_offset - entry.Offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_pos += entry_size;
            }
            return new ArcFile (file, this, dir);
        }
    }

    internal class CsNameDecryptor
    {
        int             m_name_length;
        int             m_extension_pos;
        byte[]          m_buffer;
        StringBuilder   m_name;

        public CsNameDecryptor (int name_length, int extension_pos)
        {
            m_name_length = name_length;
            m_extension_pos = extension_pos;
            m_buffer = new byte[m_name_length];
            m_name = new StringBuilder (m_name_length);
        }

        public string Decrypt (byte[] buffer)
        {
            int length = m_name_length / 6 * 4;
            int dst = 0;
            for (int pos = 0; pos < length; pos += 4)
            {
                uint num = buffer.ToUInt32 (pos);
                for (int i = 5; i >= 0; --i)
                {
                    uint val = num % 40;
                    num /= 40;
                    m_buffer[dst+i] = (byte)val;
                }
                dst += 6;
            }
            m_name.Clear();
            AppendChars (0, m_name_length - 4);
            if (m_buffer[m_extension_pos] != 0)
            {
                m_name.Append ('.');
                AppendChars (m_extension_pos, 3);
            }
            return m_name.ToString();
        }

        const string Alphabet = "_0123456789abcdefghijklmnopqrstuvwxyz_";

        void AppendChars (int pos, int length)
        {
            for (int i = 0; i < length; ++i)
            {
                if (0 == m_buffer[pos+i])
                    break;
                m_name.Append (Alphabet[m_buffer[pos+i]]);
            }
        }
    }
}
