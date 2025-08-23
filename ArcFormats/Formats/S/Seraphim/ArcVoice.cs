using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text.RegularExpressions;
using GameRes.Compression;

namespace GameRes.Formats.Seraphim
{
    [Export(typeof(ArchiveFormat))]
    public class VoiceDatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SERAPH/VOICE"; } }
        public override string Description { get { return "Seraphim engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }
        public          bool   IsAmbiguous { get { return true; } }

        public VoiceDatOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        static readonly Regex   VoiceRe = new Regex (@"^Voice(?:\d|pac)\.dat$", RegexOptions.IgnoreCase);

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset > uint.MaxValue)
                return null;
            string name = Path.GetFileName (file.Name);
            if (!VoiceRe.Match (name).Success)
                return null;

            int count = file.View.ReadInt16 (0);
            if (!IsSaneCount (count))
                return null;

            uint data_offset = 2 + 4 * (uint)count;
            uint next_offset = file.View.ReadUInt32 (2);
            List<Entry> dir = null;
            if (next_offset < data_offset || next_offset >= file.MaxOffset)
                dir = ReadV2 (file, count);
            else
                dir = ReadV1 (file, count);
            if (null == dir)
                return null;
            return new ArcFile (file, this, dir);
        }

        List<Entry> ReadV1 (ArcView file, int count)
        {
            int index_offset = 2;
            uint next_offset = file.View.ReadUInt32 (index_offset);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                index_offset += 4;
                var entry = new Entry { Name = string.Format ("{0:D5}.wav", i), Type = "audio" };
                entry.Offset = next_offset;
                if (i + 1 == count)
                    next_offset = (uint)file.MaxOffset;
                else
                    next_offset = file.View.ReadUInt32 (index_offset);
                if (next_offset <= entry.Offset)
                    return null;
                entry.Size = next_offset - (uint)entry.Offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            return dir;
        }

        List<Entry> ReadV2 (ArcView file, int count)
        {
            int index_offset = 6;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = string.Format ("{0:D5}.ogg", i),
                    Type = "audio",
                    Offset = file.View.ReadUInt32 (index_offset),
                    Size   = file.View.ReadUInt32 (index_offset+4),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 12;
            }
            return dir;
        }
    }
}
