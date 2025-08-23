using System;
using System.Collections.Generic;
using System.IO;

namespace GameRes.Formats
{
    public class MultiFileArchive : ArcFile
    {
        IEnumerable<ArcView>  m_parts;

        public IEnumerable<ArcView> Parts
        {
            get
            {
                yield return File;
                if (m_parts != null)
                    foreach (var part in m_parts)
                        yield return part;
            }
        }

        public MultiFileArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, IEnumerable<ArcView> parts = null)
            : base (arc, impl, dir)
        {
            m_parts = parts;
        }

        public Stream OpenStream (Entry entry)
        {
            Stream input = null;
            try
            {
                long part_offset = 0;
                long entry_start = entry.Offset;
                long entry_end   = entry.Offset + GetEntrySize (entry);
                foreach (var part in Parts)
                {
                    long part_end_offset = part_offset + part.MaxOffset;
                    if (entry_start < part_end_offset)
                    {
                        uint part_size = (uint)Math.Min (entry_end - entry_start, part_end_offset - entry_start);
                        var entry_part = part.CreateStream (entry_start - part_offset, part_size);
                        if (input != null)
                            input = new ConcatStream (input, entry_part);
                        else
                            input = entry_part;
                        entry_start += part_size;
                        if (entry_start >= entry_end)
                            break;
                    }
                    part_offset = part_end_offset;
                }
                return input ?? Stream.Null;
            }
            catch
            {
                if (input != null)
                    input.Dispose();
                throw;
            }
        }

        protected virtual long GetEntrySize (Entry entry)
        {
            return entry.Size;
        }
        
        bool m_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (m_disposed)
                return;

            if (disposing && m_parts != null)
            {
                foreach (var arc in m_parts)
                    arc.Dispose();
            }
            m_disposed = true;
            base.Dispose (disposing);
        }
    }
}
