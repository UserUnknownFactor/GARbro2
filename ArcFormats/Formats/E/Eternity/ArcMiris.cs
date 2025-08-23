using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text.RegularExpressions;
using GameRes.Compression;

namespace GameRes.Formats.Miris
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/MIRIS"; } }
        public override string Description { get { return "Studio Miris resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        static readonly Regex IndexEntryRe = new Regex (@"\G([^,]+),(\d+),(\d+)#");

        public override ArcFile TryOpen (ArcView file)
        {
            var base_dir = VFS.GetDirectoryName (file.Name);
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var list_file = VFS.CombinePath (base_dir, base_name+"l.dat");
            if (!VFS.FileExists (list_file))
                return null;
            string index;
            using (var ls = VFS.OpenStream (list_file))
            using (var zls = new ZLibStream (ls, CompressionMode.Decompress))
            using (var reader = new StreamReader (zls, Encodings.cp932))
            {
                index = reader.ReadToEnd();
            }
            if (string.IsNullOrEmpty (index))
                return null;

            var dir = new List<Entry>();
            var match = IndexEntryRe.Match (index);
            while (match.Success)
            {
                var entry = new Entry {
                    Name    = match.Groups[1].Value,
                    Offset  = UInt32.Parse (match.Groups[3].Value),
                    Size    = UInt32.Parse (match.Groups[2].Value),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                match = match.NextMatch();
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
