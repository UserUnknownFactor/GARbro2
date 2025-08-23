using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;

namespace GameRes.Formats.Tad
{
    [Export(typeof(ArchiveFormat))]
    public class TadOpener : ArchiveFormat
    {
        public override string         Tag { get { return "TAD"; } }
        public override string Description { get { return "TAD image archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        static readonly byte[] Separator  = { 0x20 }; // space
        static readonly byte[] Terminator = { 0x71 }; // 'q'

        public TadOpener()
        {
            Extensions = new string[] { "tad" };
        }

        public override ArcFile TryOpen(ArcView file)
        {
            long pos = 0;
            var numFilesBytes = new List<byte>();
            while (pos < file.MaxOffset)
            {
                byte b = file.View.ReadByte(pos++);
                if (b == Separator[0])
                    break;
                if (b < 0x30 || b > 0x39) // Not a digit
                    return null;
                numFilesBytes.Add(b);
            }

            if (numFilesBytes.Count == 0 || pos >= file.MaxOffset)
                return null;

            if (file.View.ReadByte(pos++) != Terminator[0])
                return null;

            int numFiles;
            try
            {
                numFiles = int.Parse(Encoding.ASCII.GetString(numFilesBytes.ToArray()));
                if (!IsSaneCount(numFiles, 9999))
                    return null;
            }
            catch
            {
                return null;
            }

            var fileSizes = new List<uint>();
            for (int i = 0; i < numFiles; i++)
            {
                var sizeBytes = new List<byte>();
                while (pos < file.MaxOffset)
                {
                    byte b = file.View.ReadByte(pos++);
                    if (b == Separator[0])
                        break;
                    if (b < 0x30 || b > 0x39) // Not a digit
                        return null;
                    sizeBytes.Add(b);
                }

                if (sizeBytes.Count == 0 || pos >= file.MaxOffset)
                    return null;

                if (file.View.ReadByte(pos++) != Terminator[0])
                    return null;

                try
                {
                    uint size = uint.Parse(Encoding.ASCII.GetString(sizeBytes.ToArray()));
                    fileSizes.Add(size);
                }
                catch
                {
                    return null;
                }
            }

            long dataOffset = pos;
            long totalDataSize = fileSizes.Sum(s => (long)s);
            if (dataOffset + totalDataSize != file.MaxOffset)
                return null;

            var dir = new List<Entry>();
            long currentOffset = dataOffset;

            for (int i = 0; i < numFiles; i++)
            {
                uint signature = 0;
                if (currentOffset + 4 <= file.MaxOffset)
                    signature = file.View.ReadUInt32(currentOffset);

                string ext = ".dat";
                if (signature == 0x474E5089) // PNG signature
                    ext = ".png";
                else if (signature == 0xE0FFD8FF || signature == 0xE1FFD8FF) // JPEG
                    ext = ".jpg";
                else if ((signature & 0xFFFF) == 0x4D42) // BMP
                    ext = ".bmp";

                var entry = new Entry
                {
                    Name = string.Format("{0:D4}{1}", i, ext),
                    Type = "image",
                    Offset = currentOffset,
                    Size = fileSizes[i]
                };
                dir.Add(entry);
                currentOffset += fileSizes[i];
            }

            return new ArcFile(file, this, dir);
        }

        public override void Create(Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                   EntryCallback callback)
        {
            var entries = list.ToList();
            entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

            var header = new MemoryStream();
            var writer = new BinaryWriter(header, Encoding.ASCII);

            writer.Write(Encoding.ASCII.GetBytes(entries.Count.ToString()));
            writer.Write(Separator);
            writer.Write(Terminator);

            foreach (var entry in entries)
            {
                writer.Write(Encoding.ASCII.GetBytes(entry.Size.ToString()));
                writer.Write(Separator);
                writer.Write(Terminator);
            }


            header.Position = 0;
            header.CopyTo(output);

            foreach (var entry in entries)
            {
                if (null != callback)
                    callback(entries.Count, entry, Localization._T ("MsgAddingFile"));

                using (var input = File.OpenRead(entry.Name))
                {
                    input.CopyTo(output);
                }
            }
        }
    }
}