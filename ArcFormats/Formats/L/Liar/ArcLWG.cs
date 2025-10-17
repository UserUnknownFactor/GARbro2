using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using GameRes.Utility;
using Newtonsoft.Json;

namespace GameRes.Formats.Liar
{
    public class LwgImageEntry : ImageEntry
    {
        public int PosX;
        public int PosY;
        public int Flags;
    }

    [Export(typeof(ArchiveFormat))]
    public class LwgOpener : ArchiveFormat
    {
        public override string         Tag { get { return "LWG"; } }
        public override string Description { get { return  Localization._T ("LWGDescription"); } }
        public override uint     Signature { get { return  0x0001474C; } } // 'LG\x01\x00'
        public override bool  IsHierarchic { get { return  false; } }
        public override bool      CanWrite { get { return  true; } }

        static readonly string METADATA_FILE = ".lwgmetadata";

        public override ArcFile TryOpen (ArcView file)
        {
            uint height = file.View.ReadUInt32 (4);
            uint width  = file.View.ReadUInt32 (8);
            int count   = file.View.ReadInt32 (12);
            if (!IsSaneCount (count))
                return null;
            uint dir_size = file.View.ReadUInt32 (20);
            uint cur_offset = 24;
            uint data_offset = cur_offset + dir_size;
            uint data_size = file.View.ReadUInt32 (data_offset);
            data_offset += 4;

            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry    = new LwgImageEntry();
                entry.PosX   = file.View.ReadInt32 (cur_offset);
                entry.PosY   = file.View.ReadInt32 (cur_offset+4);
                entry.Flags    = file.View.ReadByte (cur_offset+8);
                entry.Offset = data_offset + file.View.ReadUInt32 (cur_offset+9);
                entry.Size   = file.View.ReadUInt32 (cur_offset+13);

                uint name_length = file.View.ReadByte (cur_offset+17);
                string name = file.View.ReadString (cur_offset+18, name_length);
                entry.Name = name + ".wcg";
                cur_offset += 18+name_length;
                if (cur_offset > dir_size+24)
                    return null;
                if (entry.Size > 0 && entry.CheckPlacement (data_offset + data_size))
                    dir.Add (entry);
            }
            if (0 == dir.Count)
                return null;

            var metadata = new LwgMetadata {
                CanvasWidth  = width,
                CanvasHeight = height,
                Entries = dir.Cast<LwgImageEntry>().Select(e => new LwgEntryMetadata {
                    Name  = Path.GetFileNameWithoutExtension(e.Name),
                    PosX  = e.PosX,
                    PosY  = e.PosY,
                    Flags = e.Flags
                }).ToList()
            };

            string json = JsonConvert.SerializeObject(metadata, Formatting.Indented);
            var jsonBytes = Encoding.UTF8.GetBytes(json);

            dir.Insert(0, new MetadataEntry {
                Name = METADATA_FILE,
                Type = "script",
                Offset = 0,
                Size = (uint)jsonBytes.Length,
                JsonContent = jsonBytes
            });

            return new LwgArchive(file, this, dir, metadata);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Name == METADATA_FILE)
            {
                var metaEntry = entry as MetadataEntry;
                if (metaEntry != null && metaEntry.JsonContent != null)
                    return new MemoryStream (metaEntry.JsonContent);
            }
            return base.OpenEntry(arc, entry);
        }

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                     EntryCallback callback)
        {
            var entries = list.Where (e => !e.Name.EndsWith (METADATA_FILE)).ToList ();

            LwgMetadata metadata = null;
            var metadataFile = list.FirstOrDefault (e => e.Name.EndsWith (METADATA_FILE));
            if (metadataFile != null)
            {
                try
                {
                    using (var metaStream = File.OpenRead (metadataFile.Name))
                    using (var reader = new StreamReader (metaStream))
                    {
                        string json = reader.ReadToEnd ();
                        metadata = JsonConvert.DeserializeObject<LwgMetadata> (json);
                    }
                }
                catch { }
            }

            if (metadata != null && metadata.Entries != null && metadata.Entries.Count > 0)
            {
                var orderDict = new Dictionary<string, int> (StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < metadata.Entries.Count; i++)
                    orderDict[metadata.Entries[i].Name] = i;

                entries.Sort ((a, b) =>
                {
                    var nameA = Path.GetFileNameWithoutExtension (a.Name);
                    var nameB = Path.GetFileNameWithoutExtension (b.Name);

                    int orderA = orderDict.TryGetValue (nameA, out int posA) ? posA : int.MaxValue;
                    int orderB = orderDict.TryGetValue (nameB, out int posB) ? posB : int.MaxValue;

                    return orderA.CompareTo (orderB);
                });
            }

            uint canvasWidth, canvasHeight;
            if (metadata != null)
            {
                canvasWidth = metadata.CanvasWidth;
                canvasHeight = metadata.CanvasHeight;
            }
            else
            {
                var lwg_options = GetOptions<LwgOptions> (options);
                canvasWidth = lwg_options.CanvasWidth;
                canvasHeight = lwg_options.CanvasHeight;
            }

            var encoding = Encodings.cp932.WithFatalFallback ();

            uint dir_size = 0;
            var name_data = new List<byte[]> ();
            var entry_metadata = new List<LwgEntryMetadata> ();

            foreach (var entry in entries)
            {
                var nameWithoutExt = Path.GetFileNameWithoutExtension (entry.Name);
                var nameBytes = encoding.GetBytes (nameWithoutExt);
                name_data.Add (nameBytes);
                dir_size += 18 + (uint)nameBytes.Length;

                var entryMeta = metadata?.Entries?.FirstOrDefault (e =>
                    e.Name.Equals (nameWithoutExt, System.StringComparison.OrdinalIgnoreCase));

                if (entryMeta == null)
                {
                    entryMeta = new LwgEntryMetadata {
                        Name = nameWithoutExt,
                        PosX = 0,
                        PosY = 0,
                        Flags = 40
                    };

                    // Try to parse position from filename
                    var parts = nameWithoutExt.Split ('_');
                    if (parts.Length >= 3)
                    {
                        int x, y;
                        if (int.TryParse (parts[parts.Length - 2], out x) &&
                            int.TryParse (parts[parts.Length - 1], out y))
                        {
                            entryMeta.PosX = x;
                            entryMeta.PosY = y;
                        }
                    }
                }

                entry_metadata.Add (entryMeta);
            }

            var file_data = new List<byte[]> ();
            long total_data_size = 0;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (null != callback)
                    callback (entries.Count, entry, Localization._T ("MsgAddingFile"));

                using (var input = File.OpenRead (entry.Name))
                {
                    var data = new byte[input.Length];
                    input.Read (data, 0, data.Length);
                    file_data.Add (data);

                    entry.Offset = total_data_size;
                    entry.Size = (uint)data.Length;
                    total_data_size += entry.Size;
                }
            }

            var header = new byte[24];
            LittleEndian.Pack (Signature, header, 0);
            LittleEndian.Pack (canvasHeight, header, 4);
            LittleEndian.Pack (canvasWidth, header, 8);
            LittleEndian.Pack (entries.Count, header, 12);
            LittleEndian.Pack ((uint)0, header, 16); // Unknown
            LittleEndian.Pack (dir_size, header, 20);
            output.Write (header, 0, 24);

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var meta = entry_metadata[i];

                var entryData = new byte[17];
                LittleEndian.Pack (meta.PosX, entryData, 0);
                LittleEndian.Pack (meta.PosY, entryData, 4);
                entryData[8] = (byte)meta.Flags;
                LittleEndian.Pack ((uint)entry.Offset, entryData, 9 );
                LittleEndian.Pack ((uint)entry.Size,   entryData, 13);
                output.Write (entryData, 0, 17);

                output.WriteByte ((byte)name_data[i].Length);
                output.Write (name_data[i], 0, name_data[i].Length);
            }

            var dataSizeBytes = new byte[4];
            LittleEndian.Pack ((uint)total_data_size, dataSizeBytes, 0);
            output.Write (dataSizeBytes, 0, 4);

            for (int i = 0; i < file_data.Count; i++)
            {
                output.Write (file_data[i], 0, file_data[i].Length);
                if (null != callback)
                    callback (i + 1, entries[i], Localization._T ("MsgAddingFile"));
            }
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new LwgOptions {
                CanvasWidth = 800,
                CanvasHeight = 600,
            };
        }

        public override object GetCreationWidget ()
        {
            return new GUI.CreateLWGWidget();
        }
    }

    internal class LwgArchive : ArcFile
    {
        public LwgMetadata Metadata { get; set; }

        public LwgArchive(ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, LwgMetadata metadata)
            : base(arc, impl, dir)
        {
            Metadata = metadata;
        }
    }

    internal class MetadataEntry : Entry
    {
        public byte[] JsonContent { get; set; }
    }

    public class LwgMetadata
    {
        public  uint CanvasWidth { get; set; }
        public uint CanvasHeight { get; set; }
        public List<LwgEntryMetadata> Entries { get; set; }
    }

    public class LwgEntryMetadata
    {
        public  string Name { get; set; }
        public     int PosX { get; set; }
        public     int PosY { get; set; }
        public    int Flags { get; set; }
    }

    public class LwgOptions : ResourceOptions
    {
        public  uint CanvasWidth { get; set; } = 800;
        public uint CanvasHeight { get; set; } = 600;
    }
}
