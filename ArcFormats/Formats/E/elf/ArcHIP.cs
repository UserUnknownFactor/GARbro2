using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Elf
{
    [Export(typeof(ArchiveFormat))]
    [ExportMetadata("Priority", -1)]
    public class HipArchiveOpener : ArchiveFormat
    {
        public override string         Tag { get { return "HIP/ARC"; } }
        public override string Description { get { return "HIP images bundle"; } }
        public override uint     Signature { get { return 0x00706968; } } // 'hip'
        public override bool  IsHierarchic { get { return false; } }
        public override bool CanWrite { get { return true; } }

        readonly static uint HIZ_SIGNATURE = 0x007A6968; // 'hiz'
        readonly static uint ANM_SIGNATURE = 0x006D6E61; // 'anm'

        public override ArcFile TryOpen (ArcView file)
        {           
            uint first_offset = file.View.ReadUInt32(0x0C);
            uint second_offset = file.View.ReadUInt32(0x10);

            if (first_offset == 0 && second_offset == 0)
                return null;
            
            var dir = new List<Entry>();
            int index = 0;
            
            // Handle first entry
            if (first_offset > 0 && first_offset < file.MaxOffset)
            {
                uint size;
                if (second_offset > first_offset)
                    size = second_offset - first_offset;
                else
                    size = (uint)(file.MaxOffset - first_offset);
                
                uint sig = file.View.ReadUInt32(first_offset);
                var (ext, type) = GetExtension(sig);
                dir.Add(new Entry {
                    Name = string.Format("{0}_{1:D2}.{2}", 
                        Path.GetFileNameWithoutExtension(file.Name), 
                        index++,
                        ext),
                    Type = type,
                    Offset = first_offset,
                    Size = size
                });
            }
            
            // Handle second entry
            if (second_offset > 0 && second_offset < file.MaxOffset)
            {
                uint size = (uint)(file.MaxOffset - second_offset);
                uint sig = file.View.ReadUInt32(second_offset);
                var (ext, type) = GetExtension(sig);
                dir.Add(new Entry {
                    Name = string.Format("{0}_{1:D2}.{2}", 
                        Path.GetFileNameWithoutExtension(file.Name), 
                        index++,
                        ext),
                    Type = type,
                    Offset = second_offset,
                    Size = size
                });
            }
            
            if (dir.Count == 0)
                return null;
           
            return new ArcFile(file, this, dir);
        }

        public override void Create(
            Stream output, IEnumerable<Entry> list, ResourceOptions options, 
            EntryCallback callback)
        {
            var entries = new List<Entry>(list);
           
            if (entries.Count > 2)
                throw new InvalidFormatException("HIP archives support maximum 2 entries.");
            
            entries.Sort((a, b) => string.Compare(a.Name, b.Name));
    
            using (var writer = new BinaryWriter(output, System.Text.Encoding.ASCII, true))
            {
                writer.Write(0x00706968u); // 'hip'
                writer.Write(new byte[0x14]); // Padding to 0x18

                var file_data = new List<byte[]>();
                int current = 0;
                foreach (var entry in entries)
                {
                    if (callback != null)
                        callback(++current, entry, Localization._T("MsgAddingFile"));
                    
                    using (var input = VFS.OpenBinaryStream(entry))
                    {
                        var data = new byte[input.Length];
                        input.Read(data, 0, data.Length);
                        file_data.Add(data);
                    }
                }

                output.Position = 0x0C;
                uint offset = 0x18;
                
                if (entries.Count == 1 && entries[0].Name.Contains("_01"))
                {
                    writer.Write(0u);
                    writer.Write(offset);
                }
                else
                {
                    writer.Write(entries.Count > 0 ? offset : 0u);
                    offset += entries.Count > 0 ? (uint)file_data[0].Length : 0u;
                    writer.Write(entries.Count > 1 ? offset : 0u);
                }

                output.Position = 0x18;
                foreach (var data in file_data)
                    writer.Write(data);
            }
        }

        private bool IsValidSignature(uint signature)
        {
            return signature == HIZ_SIGNATURE || 
                   signature == ANM_SIGNATURE || 
                   signature == 0x00706968;
        }

        private (string, string) GetExtension(uint signature)
        {
            switch (signature)
            {
            case 0x007A6968: return ("hiz", "image");
            case 0x006D6E61: return ("anm", "");
            case 0x00706968: return ("hip", "archive");
            default: return ("dat", "");
            }
        }
    }
}