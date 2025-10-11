using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GameRes.Utility;
using Newtonsoft.Json;

namespace GameRes.Formats.Qlie
{
    // Metadata structure to store original file information
    public class AbmpMetadata
    {
        public string OriginalSignature { get; set; }
        public int Version { get; set; }
        public byte[] HeaderBytes { get; set; } // Bytes 7-15 of header
        public List<AbmpSectionMetadata> Sections { get; set; } = new List<AbmpSectionMetadata>();
    }

    public class AbmpSectionMetadata
    {
        public string Type { get; set; }
        public byte[] TypeBuffer { get; set; } // 16-byte type buffer
        public string Tag { get; set; }
        public Dictionary<string, object> ExtraData { get; set; } = new Dictionary<string, object>();
        public List<AbmpEntryMetadata> Entries { get; set; } = new List<AbmpEntryMetadata>();
    }

    public class AbmpEntryMetadata
    {
        public string Name { get; set; }
        public string InternalTag { get; set; }
        public byte[] TagBuffer { get; set; } // 16-byte tag buffer
        public int? Version { get; set; }
        public byte? ImageType { get; set; }
        public byte? SoundType { get; set; }
        public byte[] SkippedBytes { get; set; }
        public ushort? ExtraFieldLength { get; set; }
        public byte[] ExtraFieldData { get; set; }
        public Dictionary<string, object> AdditionalData { get; set; } = new Dictionary<string, object>();
    }

    internal class AbmpMetadataEntry : Entry
    {
        public byte[] JsonContent { get; set; }
    }

    [Export(typeof(ArchiveFormat))]
    public class AbmpOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ABMP/QLIE"; } }
        public override string Description { get { return "QLIE engine multi-frame archive"; } }
        public override uint     Signature { get { return  0x706D6261; } } // 'abmp'
        public override bool  IsHierarchic { get { return  false; } }
        public override bool      CanWrite { get { return  true; } }

        public AbmpOpener ()
        {
            Extensions = new string[] { "b" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadByte (4) * 10 + file.View.ReadByte (5) - '0' * 11;
            if (file.View.ReadByte (6) != 0 || version < 10 || version > 15)
                return null;
            using (var reader = new AbmpReader (file, version))
            {
                var dir = reader.ReadIndex();
                if (null == dir || 0 == dir.Count)
                    return null;

                var metadata = reader.GetMetadata();
                if (metadata != null)
                {
                    var json = JsonConvert.SerializeObject (metadata, Formatting.Indented);
                    var jsonBytes = Encoding.UTF8.GetBytes (json);

                    var metadataEntry = new AbmpMetadataEntry
                    {
                        Name = "metadata.json",
                        Type = "",
                        Offset = 0,
                        Size = (uint)jsonBytes.Length,
                        JsonContent = jsonBytes
                    };
                    dir.Insert (0, metadataEntry);
                }

                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry is AbmpMetadataEntry metaEntry)
                return new MemoryStream (metaEntry.JsonContent);

            if (0xFF435031 != arc.File.View.ReadUInt32 (entry.Offset))
                return base.OpenEntry (arc, entry);
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            data = PackOpener.Decompress (data) ?? data;
            return new BinMemoryStream (data, entry.Name);
        }

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options = null, 
                                     EntryCallback callback = null)
        {
            var abmp_options = GetOptions<AbmpOptions> (options);
            int version = abmp_options.Version;

            var filteredList = list.Where (e => e.Name != "metadata.json").ToList();

            using (var writer = new BinaryWriter (output, Encoding.ASCII, true))
            {
                writer.Write (0x706D6261); // 'abmp'
                writer.Write ((byte)(version / 10 + '0'));
                writer.Write ((byte)(version % 10 + '0'));
                writer.Write ((byte)0);

                for (int i = 7; i < 0x10; i++)
                    writer.Write ((byte)0);

                var file_list = filteredList.OrderBy (e => e.Name, new NaturalStringComparer()).ToList();

                var dat_files = file_list.Where (e => Path.GetExtension (e.Name).Equals (".dat", StringComparison.OrdinalIgnoreCase)).ToList();
                var image_files = file_list.Where (e => e.Type == "image").ToList();
                var audio_files = file_list.Where (e => e.Type == "audio").ToList();

                int current = 0;
                int total = file_list.Count;

                foreach (var entry in dat_files)
                {
                    if (callback != null)
                        callback (++current, entry, Localization._T ("MsgAddingFile"));
                    WriteAbdata (writer, entry);
                }

                if (image_files.Count > 0)
                    WriteAbimageSection (writer, image_files, version, callback, ref current);

                if (audio_files.Count > 0)
                    WriteAbsoundSection (writer, audio_files, version, callback, ref current);
            }
        }

        void WriteAbdata (BinaryWriter writer, Entry entry)
        {
            writer.Write (Encoding.ASCII.GetBytes ("abdata"));
            for (int i = 6; i < 0x10; i++)
                writer.Write ((byte)0);

            using (var input = VFS.OpenStream (entry))
            {
                writer.Write ((uint)entry.Size);
                input.CopyTo (writer.BaseStream);
            }
        }

        void WriteAbimageSection (BinaryWriter writer, List<Entry> images, int version, 
                                 EntryCallback callback, ref int current)
        {
            writer.Write (Encoding.ASCII.GetBytes ("abimage10"));
            writer.Write ((byte)0);
            for (int i = 10; i < 0x10; i++)
                writer.Write ((byte)0);

            writer.Write ((byte)Math.Min (images.Count, 255));

            foreach (var entry in images.Take (255))
            {
                if (callback != null)
                    callback (++current, entry, Localization._T ("MsgAddingFile"));

                WriteImageEntry (writer, entry, version);
            }
        }

        void WriteImageEntry (BinaryWriter writer, Entry entry, int version)
        {
            var name = Path.GetFileNameWithoutExtension (entry.Name);
            byte image_type = GetImageType (entry);

            string entry_type = version >= 15 ? "abimgdat15" :
                               version >= 14 ? "abimgdat14" :
                               version >= 13 ? "abimgdat13" : "abimgdat10";

            writer.Write (Encoding.ASCII.GetBytes (entry_type));
            for (int i = entry_type.Length; i < 0x10; i++)
                writer.Write ((byte)0);

            if (version >= 15)
            {
                // abimgdat15 format
                writer.Write ((int)1); // version
                var name_bytes = Encoding.Unicode.GetBytes (name);
                writer.Write ((ushort)(name_bytes.Length / 2));
                writer.Write (name_bytes);
                writer.Write ((ushort)0); // ASCII name length
                writer.Write (image_type);
                for (int i = 0; i < 0x11; i++)
                    writer.Write ((byte)0);
            }
            else
            {
                var name_bytes = Encoding.ASCII.GetBytes (name);
                writer.Write ((ushort)name_bytes.Length);
                writer.Write (name_bytes);

                if (version >= 13)
                {
                    writer.Write ((ushort)0); // Extra field

                    if (version == 13)
                    {
                        for (int i = 0; i < 0x0C; i++)
                            writer.Write ((byte)0);
                    }
                    else if (version == 14)
                    {
                        for (int i = 0; i < 0x4C; i++)
                            writer.Write ((byte)0);
                    }
                }

                writer.Write (image_type);
            }

            using (var input = VFS.OpenStream (entry))
            {
                writer.Write ((uint)entry.Size);
                input.CopyTo (writer.BaseStream);
            }
        }

        void WriteAbsoundSection (BinaryWriter writer, List<Entry> audio_files, int version, 
                                 EntryCallback callback, ref int current)
        {
            writer.Write (Encoding.ASCII.GetBytes ("absound10"));
            writer.Write ((byte)0);
            for (int i = 10; i < 0x10; i++)
                writer.Write ((byte)0);

            writer.Write ((byte)Math.Min (audio_files.Count, 255));

            foreach (var entry in audio_files.Take (255))
            {
                if (callback != null)
                    callback (++current, entry, Localization._T ("MsgAddingFile"));

                WriteAudioEntry (writer, entry, version);
            }
        }

        void WriteAudioEntry (BinaryWriter writer, Entry entry, int version)
        {
            var name = Path.GetFileNameWithoutExtension (entry.Name);

            string entry_type = version >= 12 ? "absnddat12" : "absnddat10";

            writer.Write (Encoding.ASCII.GetBytes (entry_type));
            for (int i = entry_type.Length; i < 0x10; i++)
                writer.Write ((byte)0);

            if (version >= 12)
            {
                writer.Write ((int)1); // version field
                var name_bytes = Encoding.Unicode.GetBytes (name);
                writer.Write ((ushort)(name_bytes.Length / 2));
                writer.Write (name_bytes);
                for (int i = 0; i < 7; i++)
                    writer.Write ((byte)0);
            }
            else
            {
                var name_bytes = Encoding.ASCII.GetBytes (name);
                writer.Write ((ushort)name_bytes.Length);
                writer.Write (name_bytes);
                writer.Write ((byte)0); // type
            }

            using (var input = VFS.OpenStream (entry))
            {
                writer.Write ((uint)entry.Size);
                input.CopyTo (writer.BaseStream);
            }
        }

        byte GetImageType (Entry entry)
        {
            var ext = Path.GetExtension (entry.Name).ToLowerInvariant();
            switch (ext)
            {
                case ".bmp": return 0;
                case ".jpg":
                case ".jpeg": return 1;
                case ".png": return 3;
                case ".m": return 4;
                case ".argb": return 5;
                case ".b": return 6;
                case ".ogv": return 7;
                case ".mdl": return 8;
                default:
                    // Try to detect by signature
                    using (var input = VFS.OpenStream (entry))
                    {
                        uint sig = FormatCatalog.ReadSignature (input);
                        if (sig == 0x474E5089) return 3; // PNG
                        if ((sig & 0xFFFF) == 0xD8FF) return 1; // JPEG
                        if ((sig & 0xFFFF) == 0x4D42) return 0; // BMP
                        return 3; // Default to PNG
                    }
            }
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new AbmpOptions { Version = 11 };
        }

        public override ResourceOptions GetOptions (object widget)
        {
            var w = widget as GUI.WidgetABMP;
            if (w != null)
                return new AbmpOptions { Version = w.Version };
            return GetDefaultOptions();
        }

        public override object GetCreationWidget ()
        {
            return new GUI.WidgetABMP();
        }
    }


    internal sealed class AbmpReader : IDisposable
    {
        ArcView             m_file;
        IBinaryStream       m_input;
        string              m_base_name;
        int                 m_version;
        List<Entry>         m_dir;
        AbmpMetadata        m_metadata;

        public AbmpReader (ArcView file, int version)
        {
            m_file = file;
            m_input = file.CreateStream();
            m_base_name = Path.GetFileNameWithoutExtension (file.Name);
            m_version = version;
            m_dir = new List<Entry>();
            m_metadata = new AbmpMetadata 
            { 
                Version = version,
                OriginalSignature = "abmp"
            };
        }

        public AbmpMetadata GetMetadata()
        {
            return m_metadata;
        }

        public List<Entry> ReadIndex ()
        {
            m_input.Position = 7;
            m_metadata.HeaderBytes = m_input.ReadBytes (9);

            m_input.Position = 0x10;
            int n = 0;
            var type_buf = new byte[0x10];
            while (0x10 == m_input.Read (type_buf, 0, 0x10))
            {
                var section = new AbmpSectionMetadata
                {
                    TypeBuffer = (byte[])type_buf.Clone(),
                    ExtraData = new Dictionary<string, object>()
                };

                if (Binary.AsciiEqual (type_buf, "abdata"))
                {
                    section.Type = "abdata";
                    uint size = m_input.ReadUInt32(); 
                    var entry = new Entry {
                        Name = string.Format ("{0}#{1}.dat", m_base_name, n++),
                        Offset = m_input.Position,
                        Size = size,
                    };
                    m_dir.Add (entry);

                    var entryMeta = new AbmpEntryMetadata
                    {
                        Name = entry.Name,
                        AdditionalData = new Dictionary<string, object>()
                    };
                    section.Entries.Add (entryMeta);
                    Skip (size);
                }
                else if (Binary.AsciiEqual (type_buf, "abimage10\0") ||
                         Binary.AsciiEqual (type_buf, "absound10\0"))
                {
                    section.Type = Binary.AsciiEqual (type_buf, "abimage10\0") ? "abimage" : "absound";
                    section.Tag = Binary.GetCString (type_buf, 0, 0x10, Encoding.ASCII);

                    int count = m_input.ReadByte();
                    section.ExtraData["Count"] = count;

                    for (int i = 0; i < count; ++i)
                    {
                        if (0x10 != m_input.Read (type_buf, 0, 0x10))
                            break;

                        var entryMeta = new AbmpEntryMetadata
                        {
                            TagBuffer = (byte[])type_buf.Clone(),
                            AdditionalData = new Dictionary<string, object>()
                        };

                        var tag = Binary.GetCString (type_buf, 0, 0x10, Encoding.ASCII);
                        entryMeta.InternalTag = tag;
                        string name = null;

                        if ("abimgdat15" == tag)
                        {
                            int img_version = m_input.ReadInt32();
                            entryMeta.Version = img_version;
                            int name_length = m_input.ReadUInt16();
                            if (name_length > 0)
                            {
                                var name_bytes = m_input.ReadBytes (name_length*2);
                                name = Encoding.Unicode.GetString (name_bytes);
                                entryMeta.AdditionalData["UnicodeName"] = name;
                            }
                            name_length = m_input.ReadUInt16();
                            if (name_length > 0)
                            {
                                if (string.IsNullOrEmpty (name))
                                {
                                    var ascii_bytes = m_input.ReadBytes (name_length);
                                    name = Encoding.ASCII.GetString (ascii_bytes);
                                }
                                else
                                {
                                    var ascii_bytes = m_input.ReadBytes (name_length);
                                    entryMeta.AdditionalData["AsciiName"] = Encoding.ASCII.GetString (ascii_bytes);
                                }
                            }
                            byte type = m_input.ReadUInt8();
                            entryMeta.ImageType = type;
                            /*
                            case 0:   ".bmp"
                            case 1:   ".jpg"
                            case 2:
                            case 3:   ".png"
                            case 4:   ".m"
                            case 5:   ".argb"
                            case 6:   ".b"
                            case 7:   ".ogv"
                            case 8:   ".mdl"
                            */
                            if (2 == img_version)
                                entryMeta.SkippedBytes = m_input.ReadBytes (0x1D);
                            else
                                entryMeta.SkippedBytes = m_input.ReadBytes (0x11);
                        }
                        else if ("absnddat12" == tag)
                        {
                            int snd_version = m_input.ReadInt32();
                            entryMeta.Version = snd_version;
                            int name_length = m_input.ReadUInt16();
                            if (name_length > 0)
                            {
                                var name_bytes = m_input.ReadBytes (name_length*2);
                                name = Encoding.Unicode.GetString (name_bytes);
                                entryMeta.AdditionalData["UnicodeName"] = name;
                            }
                            if (m_input.Length - m_input.Position > 7)
                            {
                                entryMeta.SkippedBytes = m_input.ReadBytes (7);
                            }
                        }
                        else
                        {
                            int name_length = m_input.ReadUInt16();
                            if (name_length > 0)
                            {
                                var name_bytes = m_input.ReadBytes (name_length);
                                name = Encoding.ASCII.GetString (name_bytes);
                            }

                            if (tag != "abimgdat10" && tag != "absnddat10")
                            {
                                ushort extraLen = m_input.ReadUInt16();
                                entryMeta.ExtraFieldLength = extraLen;
                                if (extraLen > 0)
                                    entryMeta.ExtraFieldData = m_input.ReadBytes (extraLen);

                                if ("abimgdat13" == tag)
                                    entryMeta.SkippedBytes = m_input.ReadBytes (0x0C);
                                else if ("abimgdat14" == tag)
                                    entryMeta.SkippedBytes = m_input.ReadBytes (0x4C);
                            }
                            byte typeOrSound = (byte)m_input.ReadByte();
                            if (section.Type == "abimage")
                                entryMeta.ImageType = typeOrSound;
                            else
                                entryMeta.SoundType = typeOrSound;
                        }

                        var size = m_input.ReadUInt32();
                        if (0 != size)
                        {
                            if (string.IsNullOrEmpty (name))
                                name = string.Format ("{0}#{1}", m_base_name, n++);
                            else
                                name = s_InvalidChars.Replace (name, "_");

                            entryMeta.Name = name;

                            var entry = new Entry {
                                Name = name,
                                Type = tag.StartsWith ("abimg") ? "image" : tag.StartsWith ("absnd") ? "audio" : "",
                                Offset = m_input.Position,
                                Size = size,
                            };
                            if (entry.CheckPlacement (m_file.MaxOffset))
                            {
                                DetectFileType (m_file, entry);
                                m_dir.Add (entry);
                            }
                            section.Entries.Add (entryMeta);
                        }
                        Skip (size);
                    }
                }
                else
                {
                    section.Type = "unknown";
                    section.Tag = GetTypeName (type_buf);

                    var entry = new Entry {
                        Name = string.Format ("{0}#{1}#{2}", m_base_name, n++, section.Tag),
                        Offset = m_input.Position,
                        Size = (uint)(m_file.MaxOffset - m_input.Position),
                    };
                    m_dir.Add (entry);

                    var entryMeta = new AbmpEntryMetadata
                    {
                        Name = entry.Name,
                        AdditionalData = new Dictionary<string, object>()
                    };
                    section.Entries.Add (entryMeta);
                    Skip (entry.Size);
                }

                m_metadata.Sections.Add (section);
            }
            return m_dir;
        }

        void Skip (long amount)
        {
            m_input.Seek (amount, SeekOrigin.Current);
        }

        static internal void DetectFileType (ArcView file, Entry entry)
        {
            uint signature = file.View.ReadUInt32 (entry.Offset);
            var res = AutoEntry.DetectFileType (signature);
            if (null != res)
                entry.ChangeType (res);
        }

        static string GetTypeName (byte[] type_buf)
        {
            int n = 0;
            while (n < type_buf.Length)
            {
                if (0 == type_buf[n])
                    break;
                if (type_buf[n] < 0x20 || type_buf[n] > 0x7E)
                    return "unknown";
                n++;
            }
            if (0 == n)
                return "";
            return Encoding.ASCII.GetString (type_buf, 0, n).Trim();
        }

        static readonly Regex s_InvalidChars = new Regex (@"[:/\\*?]");

        #region IDisposable Members
        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_input.Dispose();
                _disposed = true;
            }
        }
        #endregion
    }

    [Export(typeof(ArchiveFormat))]
    public class Abmp7Opener : AbmpOpener
    {
        public override string         Tag { get { return "ABMP7"; } }
        public override string Description { get { return "QLIE engine multi-frame image archive"; } }
        public override uint     Signature { get { return  0x504D4241; } } // 'ABMP'
        public override bool  IsHierarchic { get { return  false; } }
        public override bool      CanWrite { get { return  true; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.ReadUInt16 (4) != '7')
                return null;
            string base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint offset = 0xC;
            var dir = new List<Entry>();

            // Create metadata for ABMP7
            var metadata = new AbmpMetadata
            {
                OriginalSignature = "ABMP7",
                Version = 7,
                HeaderBytes = file.View.ReadBytes (6, 6) // bytes 6-11
            };

            uint size = file.View.ReadUInt32 (offset);
            offset += 4;

            var section = new AbmpSectionMetadata
            {
                Type = "data",
                ExtraData = new Dictionary<string, object>()
            };

            if (size > 0)
            {
                var entry = new Entry {
                    Name = string.Format ("{0}#0.dat", base_name),
                    Offset = offset,
                    Size = size,
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);

                section.Entries.Add (new AbmpEntryMetadata { 
                    Name = entry.Name, 
                    AdditionalData = new Dictionary<string, object>() 
                });
                offset += size;
            }

            int n = 1;
            while (offset < file.MaxOffset)
            {
                size = file.View.ReadUInt32 (offset);
                if (0 == size)
                    break;
                offset += 4;
                var entry = new Entry {
                    Name = string.Format ("{0}#{1}", base_name, n++),
                    Type = "image",
                    Offset = offset,
                    Size = size,
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    break;
                AbmpReader.DetectFileType (file, entry);
                dir.Add (entry);

                section.Entries.Add (new AbmpEntryMetadata { 
                    Name = entry.Name, 
                    AdditionalData = new Dictionary<string, object>() 
                });
                offset += size;
            }

            metadata.Sections.Add (section);

            // Add metadata.json as a virtual entry
            var json = JsonConvert.SerializeObject (metadata, Formatting.Indented);
            var jsonBytes = Encoding.UTF8.GetBytes (json);

            var metadataEntry = new AbmpMetadataEntry
            {
                Name = "metadata.json",
                Type = "",
                Offset = 0,
                Size = (uint)jsonBytes.Length,
                JsonContent = jsonBytes
            };
            dir.Insert (0, metadataEntry);

            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry is AbmpMetadataEntry metaEntry)
                return new MemoryStream (metaEntry.JsonContent);

            return base.OpenEntry (arc, entry);
        }

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options = null,
                                     EntryCallback callback = null)
        {
            // Filter out metadata.json
            var filteredList = list.Where (e => e.Name != "metadata.json").ToList();

            using (var writer = new BinaryWriter (output, Encoding.ASCII, true))
            {
                // Write header
                writer.Write (0x504D4241); // 'ABMP'
                writer.Write ((ushort)'7');
                writer.Write ((ushort)0);
                writer.Write (0); // padding

                var file_list = filteredList.OrderBy (e => e.Name, new NaturalStringComparer()).ToList();
                int current = 0;

                // First file should be .dat if it exists
                var dat_file = file_list.FirstOrDefault (e => 
                    Path.GetExtension (e.Name).Equals (".dat", StringComparison.OrdinalIgnoreCase));

                if (dat_file != null)
                {
                    if (callback != null)
                        callback (++current, dat_file, Localization._T ("MsgAddingFile"));

                    using (var input = VFS.OpenStream (dat_file))
                    {
                        writer.Write ((uint)dat_file.Size);
                        input.CopyTo (writer.BaseStream);
                    }
                    file_list.Remove (dat_file);
                }
                else
                    writer.Write ((uint)0); // No dat file

                foreach (var entry in file_list.Where (e => e.Type == "image"))
                {
                    if (callback != null)
                        callback (++current, entry, Localization._T ("MsgAddingFile"));

                    using (var input = VFS.OpenStream (entry))
                    {
                        writer.Write ((uint)entry.Size);
                        input.CopyTo (writer.BaseStream);
                    }
                }

                // End marker
                writer.Write ((uint)0);
            }
        }
    }

    public class AbmpOptions : ResourceOptions
    {
        public int Version { get; set; }
    }

    internal class NaturalStringComparer : IComparer<string>
    {
        public int Compare (string x, string y)
        {
            return NaturalCompare (x, y);
        }

        static int NaturalCompare (string x, string y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            int ix = 0, iy = 0;
            while (ix < x.Length && iy < y.Length)
            {
                if (char.IsDigit (x[ix]) && char.IsDigit (y[iy]))
                {
                    // Compare numbers
                    int nx = ix, ny = iy;
                    while (nx < x.Length && char.IsDigit (x[nx])) nx++;
                    while (ny < y.Length && char.IsDigit (y[ny])) ny++;

                    string sx = x.Substring (ix, nx - ix);
                    string sy = y.Substring (iy, ny - iy);

                    int result = sx.Length.CompareTo (sy.Length);
                    if (result == 0)
                        result = sx.CompareTo (sy);
                    if (result != 0)
                        return result;

                    ix = nx;
                    iy = ny;
                }
                else
                {
                    int result = x[ix].CompareTo (y[iy]);
                    if (result != 0)
                        return result;
                    ix++;
                    iy++;
                }
            }
            return x.Length.CompareTo (y.Length);
        }
    }
}
