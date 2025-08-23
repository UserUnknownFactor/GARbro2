using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.WestGate
{
    [Export(typeof(ArchiveFormat))]
    public class UwfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "UWF"; } }
        public override string Description { get { return "West Gate audio archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public UwfOpener ()
        {
            Extensions = new[] { "uwf", "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset <= 0x1500)
                return null;
            uint first_offset = file.View.ReadUInt32 (0x14FC);
            if (first_offset >= file.MaxOffset || first_offset < 0x1500)
                return null;
            int count = (int)((first_offset - 0x14F0) / 0x10);
            if (!IsSaneCount (count))
                return null;
            var dir = UcaTool.ReadIndex (file, 0x14F0, count, "audio");
            if (null == dir)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            uint fmt_size = arc.File.View.ReadUInt16 (entry.Offset);
            if (fmt_size >= entry.Size)
                return base.OpenEntry (arc, entry);
            uint pcm_size = arc.File.View.ReadUInt32 (entry.Offset+2+fmt_size);
            if (pcm_size >= entry.Size)
                return base.OpenEntry (arc, entry);
            using (var mem = new MemoryStream())
            using (var riff = new BinaryWriter (mem))
            {
                uint total_size = (uint)(0x1C + fmt_size + pcm_size);
                riff.Write (AudioFormat.Wav.Signature);
                riff.Write (total_size);
                riff.Write (0x45564157); // 'WAVE'
                riff.Write (0x20746d66); // 'fmt '
                riff.Write (fmt_size);
                var fmt = arc.File.View.ReadBytes (entry.Offset+2, fmt_size);
                riff.Write (fmt);
                riff.Write (0x61746164); // 'data'
                riff.Flush();
                var wav_header = mem.ToArray();
                var pcm = arc.File.CreateStream (entry.Offset+fmt_size+2, pcm_size+4);
                return new PrefixStream (wav_header, pcm);
            }
        }
    }
}
