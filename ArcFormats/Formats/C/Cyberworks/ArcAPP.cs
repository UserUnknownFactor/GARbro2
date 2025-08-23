using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Cyberworks
{
    [Export(typeof(ArchiveFormat))]
    public class AppOpener : DatOpener
    {
        public override string         Tag { get { return "APP/Csystem"; } }
        public override string Description { get { return "WendyBell resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public AppOpener ()
        {
            Extensions = new string[] { "appendix" };
            Signatures = new uint[] { 0x2Fu };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            long index_offset = 4 + (long)file.View.ReadInt32 (0) * 2;
            if (index_offset <= 4 || index_offset >= file.MaxOffset)
                return null;
            using (var toc_unpacker = new TocUnpacker (file))
            {
                var toc = toc_unpacker.Unpack (index_offset, 8);
                if (null == toc)
                    return null;
                var data_offset = index_offset + 0x10 + toc_unpacker.PackedSize;
                using (var index = new AppendixReader (toc, file, data_offset))
                {
                    if (!index.Read())
                        return null;
                    return ArchiveFromDir (file, index.Dir, index.HasImages);
                }
            }
        }
    }

    internal class AppendixReader : IndexReader
    {
        long    m_data_offset;

        public AppendixReader (byte[] toc, ArcView file, long data_offset) : base (toc, file)
        {
            m_data_offset = data_offset;
        }

        char[]  m_type = new char[2];

        protected override bool ReadEntryType (Entry entry, int entry_size)
        {
            entry.Offset += m_data_offset;
            m_type[0] = (char)m_index.ReadByte();
            m_type[1] = (char)m_index.ReadByte();
            if (m_type[0] > 0x20 && m_type[0] < 0x7F)
            {
                string ext;
                if (m_type[1] > 0x20 && m_type[1] < 0x7F)
                    ext = new string (m_type);
                else
                    ext = new string (m_type[0], 1);
                if ("b0" == ext || "n0" == ext || "o0" == ext || "0b" == ext || "w0" == ext)
                {
                    entry.Type = "image";
                    HasImages = true;
                }
                else if ("j0" == ext || "k0" == ext || "u0" == ext)
                    entry.Type = "audio";
                entry.Name = Path.ChangeExtension (entry.Name, ext);
            }
            return true;
        }
    }
}
