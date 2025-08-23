using GameRes.Compression;
using GameRes.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using GameRes.Formats.Rugp;
using static ICSharpCode.SharpZipLib.Zip.ExtendedUnixData;
using static System.Net.Mime.MediaTypeNames;

namespace GameRes.Formats.Macromedia
{
    internal enum ByteOrder
    {
        LittleEndian, BigEndian
    }

    internal enum DataType
    {
        Null        = 0,
        Bitmap      = 1,
        FilmLoop    = 2,
        Text        = 3,
        Palette     = 4,
        Picture     = 5,
        Sound       = 6,
        Button      = 7,
        Shape       = 8,
        Movie       = 9,
        DigitalVideo = 10,
        Script      = 11,
        RichText    = 12,
        RTE         = 12,  // Alias
        Font        = 14,
        Transition  = 15,
    }

    internal enum ScriptType
    {
        Score = 1,
        Movie = 3,
        Parent = 7
    }

    internal class SerializationContext
    {
        public int          Version;
        public Encoding     Encoding = Encodings.cp932;
        public bool         DotSyntax;

    }

    internal class DirectorFile
    {
        List<DirectorEntry> m_dir;
        Dictionary<int, DirectorEntry> m_index = new Dictionary<int, DirectorEntry>();
        MemoryMap       m_mmap = new MemoryMap();
        KeyTable        m_keyTable = new KeyTable();
        DirectorConfig  m_config = new DirectorConfig();
        List<Cast>      m_casts = new List<Cast>();
        Dictionary<int, byte[]> m_ilsMap = new Dictionary<int, byte[]>();
        string          m_codec;
        ByteOrder       m_endianness;
        string          m_fverVersionString;

        public string Codec         => m_codec;
        public ByteOrder Endianness => m_endianness;
        public bool IsAfterBurned { get; private set; }

        public MemoryMap        MMap => m_mmap;
        public KeyTable     KeyTable => m_keyTable;
        public DirectorConfig Config => m_config;
        public List<Cast>      Casts => m_casts;
        public List<DirectorEntry>            Directory => m_dir;
        public Dictionary<int, DirectorEntry> Index     => m_index;

        public DirectorEntry Find (string four_cc) => Directory.Find (e => e.FourCC == four_cc);

        public DirectorEntry FindById (int id)
        {
            DirectorEntry entry;
            m_index.TryGetValue (id, out entry);
            return entry;
        }

        public bool Deserialize (SerializationContext context, Reader reader)
        {
            uint metaFourCC = reader.ReadU32();
            if (metaFourCC == 0x52494658) // 'XFIR'
            {
                reader.SetByteOrder (ByteOrder.LittleEndian);
                m_endianness = ByteOrder.LittleEndian;
            }
            else
            {
                m_endianness = ByteOrder.BigEndian;
            }

            reader.Skip (4); // size
            m_codec = reader.ReadFourCC();

            if (m_codec == "MV93" || m_codec == "MC95")
            {
                if (!ReadMMap (context, reader))
                    return false;
            }
            else if (m_codec == "FGDC" || m_codec == "FGDM")
            {
                IsAfterBurned = true;
                if (!ReadAfterBurner (context, reader))
                    return false;
            }
            else
            {
                Trace.WriteLine (string.Format ("Unknown codec '{0}'", m_codec), "DXR");
                return false;
            }

            if (!ReadKeyTable (context, reader))
                return false;
            if (!ReadConfig (context, reader))
                return false;
            if (!ReadCasts (context, reader))
                return false;

            return true;
        }

        internal bool ReadMMap (SerializationContext context, Reader reader)
        {
            if (reader.ReadFourCC() != "imap")
                return false;

            reader.Skip (4); // skip chunk size
            var imapData = new InitialMapChunk();
            imapData.Deserialize (context, reader);

            reader.Position = imapData.MmapOffset;
            if (reader.ReadFourCC() != "mmap")
                return false;

            reader.Position = imapData.MmapOffset + 8;
            MMap.Deserialize (context, reader);
            m_dir = MMap.Dir;
            foreach (var entry in m_dir)
                m_index[entry.Id] = entry;
            return true;
        }

        DirectorEntry ReadChunk (Reader reader, string fourCC)
        {
            long pos = reader.Position;
            string readFourCC = reader.ReadFourCC();
            uint size = reader.ReadU32();

            if (readFourCC != fourCC)
            {
                reader.Position = pos;
                return null;
            }

            return new DirectorEntry
            {
                FourCC = readFourCC,
                Size = size,
                Offset = pos + 8
            };
        }

        bool ReadAfterBurner (SerializationContext context, Reader reader)
        {
            if (reader.ReadFourCC() != "Fver")
                return false;
            int length = reader.ReadVarInt();
            long next_pos = reader.Position + length;
            int version = reader.ReadVarInt();
            if (version > 0x400)
            {
                reader.ReadVarInt(); // imap version
                reader.ReadVarInt(); // director version
            }
            if (version > 0x500)
            {
                int str_len = reader.ReadU8();
                m_fverVersionString = Encoding.ASCII.GetString (reader.ReadBytes (str_len));
            }

            reader.Position = next_pos;
            if (reader.ReadFourCC() != "Fcdr")
                return false;

            // Read compression table
            length = reader.ReadVarInt();
            var compressionTypes = new List<CompressionType>();
            using (var fcdr = new ZLibStream (reader.Source, CompressionMode.Decompress, true))
            {
                var fcdrReader = new Reader (fcdr, reader.ByteOrder);
                int compressionTypeCount = fcdrReader.ReadU16();
                for (int i = 0; i < compressionTypeCount; i++)
                {
                    var comp = new CompressionType();
                    comp.Deserialize (context, fcdrReader);
                    compressionTypes.Add (comp);
                }
            }

            reader.Position += length;
            if (reader.ReadFourCC() != "ABMP")
                return false;
            length = reader.ReadVarInt();
            next_pos = reader.Position + length;
            reader.ReadVarInt(); // compression type
            int unpacked_size = reader.ReadVarInt();
            using (var abmp = new ZLibStream (reader.Source, CompressionMode.Decompress, true))
            {
                var abmp_reader = new Reader (abmp, reader.ByteOrder);
                if (!ReadABMap (context, abmp_reader, compressionTypes))
                    return false;
            }

            reader.Position = next_pos;
            if (reader.ReadFourCC() != "FGEI")
                return false;
            reader.ReadVarInt();
            long base_offset = reader.Position;
            foreach (var entry in m_dir)
            {
                m_index[entry.Id] = entry;
                if (entry.Offset >= 0)
                    entry.Offset += base_offset;
            }

            var ils_chunk = FindById (2);
            if (null == ils_chunk)
                return false;
            using (var ils = new ZLibStream (reader.Source, CompressionMode.Decompress, true))
            {
                long pos = 0;
                var ils_reader = new Reader (ils, reader.ByteOrder);
                while (pos < ils_chunk.UnpackedSize)
                {
                    int id = ils_reader.ReadVarInt();
                    var chunk = m_index[id];
                    m_ilsMap[id] = ils_reader.ReadBytes ((int)chunk.Size);
                    pos += ils_reader.GetVarIntLength ((uint)id) + chunk.Size;
                }
            }
            return true;
        }

        bool ReadABMap (SerializationContext context, Reader reader, List<CompressionType> compressionTypes)
        {
            reader.ReadVarInt();
            reader.ReadVarInt();
            int count = reader.ReadVarInt();
            m_dir = new List<DirectorEntry> (count);
            for (int i = 0; i < count; ++ i)
            {
                var entry = new AfterBurnerEntry();
                entry.Deserialize (context, reader);
                if (entry.CompMethod < compressionTypes.Count)
                    entry.CompressionType = compressionTypes[entry.CompMethod];
                m_dir.Add (entry);
            }
            return true;
        }

        Reader GetChunkReader (DirectorEntry chunk, Reader reader)
        {
            if (-1 == chunk.Offset)
            {
                byte[] chunk_data;
                if (!m_ilsMap.TryGetValue (chunk.Id, out chunk_data))
                    throw new InvalidFormatException (string.Format ("Can't find chunk {0} in ILS", chunk.FourCC));
                var input = new BinMemoryStream (chunk_data, null);
                reader = new Reader (input, reader.ByteOrder);
            }
            else
            {
                reader.Position = chunk.Offset;
            }
            return reader;
        }

        bool ReadKeyTable (SerializationContext context, Reader reader)
        {
            var key_chunk = Find ("KEY*");
            if (null == key_chunk)
                return false;
            reader = GetChunkReader (key_chunk, reader);
            KeyTable.Deserialize (context, reader);
            return true;
        }

        bool ReadConfig (SerializationContext context, Reader reader)
        {
            var config_chunk = Find ("VWCF") ?? Find ("DRCF");
            if (null == config_chunk)
                return false;
            reader = GetChunkReader (config_chunk, reader);
            Config.Deserialize (context, reader);
            context.Version = Config.Version;
            context.DotSyntax = context.Version >= 700;
            return true;
        }

        bool ReadCasts (SerializationContext context, Reader reader)
        {
            Reader cas_reader;
            if (context.Version >= 500)
            {
                var mcsl = Find ("MCsL");
                if (mcsl != null)
                {
                    var mcsl_reader = GetChunkReader (mcsl, reader);
                    var cast_list = new CastList();
                    cast_list.Deserialize (context, mcsl_reader);
                    foreach (var entry in cast_list.Entries)
                    {
                        var key_entry = KeyTable.FindByCast (entry.Id, "CAS*");
                        if (key_entry != null)
                        {
                            if (Index.TryGetValue (key_entry.Id, out DirectorEntry cas_entry))
                            {
                                cas_reader = GetChunkReader (cas_entry, reader);
                                var cast = new Cast (context, cas_reader, cas_entry);
                                cast.CastId = entry.Id;
                                if (!PopulateCast (cast, context, reader, entry))
                                    return false;
                                Casts.Add (cast);
                            }
                            else
                            {
                                System.Diagnostics.Trace.WriteLine($"Warning: Section {key_entry.Id} not found in memory map for cast {entry.Id}");
                            }
                        }
                    }
                    return true;
                }
            }

            var cas_chunk = Find ("CAS*");
            if (null == cas_chunk)
                return false;
            var new_entry = new CastListEntry { Name = "Internal", Id = 1024, MinMember = Config.MinMember };
            cas_reader = GetChunkReader (cas_chunk, reader);
            var new_cast = new Cast (context, cas_reader, cas_chunk);
            new_cast.CastId = new_entry.Id;
            if (!PopulateCast (new_cast, context, reader, new_entry))
                return false;
            Casts.Add (new_cast);
            return true;
        }

        public bool PopulateCast (Cast cast, SerializationContext context, Reader reader, CastListEntry entry)
        {
            cast.Name = entry.Name;
            cast.MinMember = entry.MinMember;

            // Find script context for this cast
            var lctxEntry = KeyTable.FindByCast (entry.Id, "Lctx") ?? KeyTable.FindByCast (entry.Id, "LctX");
            if (lctxEntry != null && Index.ContainsKey (lctxEntry.Id))
            {
                var lctxChunk = Index[lctxEntry.Id];
                var lctxReader = GetChunkReader (lctxChunk, reader);
                cast.ScriptContext = new ScriptContext();
                cast.ScriptContext.Deserialize (context, lctxReader);
            }

            for (int i = 0; i < cast.Index.Length; ++i)
            {
                int chunk_id = cast.Index[i];
                if (chunk_id > 0)
                {
                    var chunk = this.Index[chunk_id];
                    var member = new CastMember();
                    member.Id = i + entry.MinMember;
                    member.ChunkId = chunk_id;
                    var cast_reader = GetChunkReader (chunk, reader);
                    member.Deserialize (context, cast_reader);

                    // Link script if available
                    if (cast.ScriptContext != null && member.Info.ScriptId > 0)
                    {
                        // Find script chunk
                        var scriptEntry = cast.ScriptContext.Scripts.FirstOrDefault (s => s.Id == member.Info.ScriptId);
                        if (scriptEntry != null && Index.ContainsKey (scriptEntry.ChunkId))
                        {
                            var scriptChunk = Index[scriptEntry.ChunkId];
                            var scriptReader = GetChunkReader (scriptChunk, reader);
                            member.Script = new Script();
                            member.Script.Deserialize (context, scriptReader);
                        }
                    }

                    cast.Members[member.Id] = member;
                }
            }
            return true;
        }

        public string GetVersionString()
        {
            return DirectorVersion.GetVersionString (Config.Version, m_fverVersionString);
        }

        public JObject ToJson()
        {
            var json = new JObject();
            json["codec"] = m_codec;
            json["version"] = GetVersionString();
            json["afterburned"] = IsAfterBurned;
            json["endianness"] = m_endianness.ToString();

            if (Config != null)
                json["config"] = Config.ToJson();

            if (Casts.Count > 0)
            {
                var castsArray = new JArray();
                foreach (var cast in Casts)
                {
                    castsArray.Add (cast.ToJson());
                }
                json["casts"] = castsArray;
            }

            return json;
        }
    }

    internal class CastMember
    {
        public DataType     Type;
        public CastInfo     Info = new CastInfo();
        public byte[]       SpecificData;
        public byte         Flags;
        public int          Id;
        public int          ChunkId;
        public Script       Script;

        public void Deserialize (SerializationContext context, Reader reader)
        {
            reader = reader.CloneUnless (ByteOrder.BigEndian);
            if (context.Version >= 500)
            {
                Type = (DataType)reader.ReadI32();
                int info_length = reader.ReadI32();
                int data_length = reader.ReadI32();
                if (info_length > 0)
                {
                    Info.Deserialize (context, reader);
                }
                SpecificData = reader.ReadBytes (data_length);
            }
            else
            {
                int data_length = reader.ReadU16();
                int info_length = reader.ReadI32();
                Type = (DataType)reader.ReadU8();
                --data_length;
                if (data_length > 0)
                {
                    Flags = reader.ReadU8();
                    --data_length;
                }
                SpecificData = reader.ReadBytes (data_length);
                if (info_length > 0)
                {
                    Info.Deserialize (context, reader);
                }
            }
        }

        public JObject ToJson()
        {
            var json = new JObject();
            json["id"] = Id;
            json["type"] = Type.ToString();

            if (Info != null)
                json["info"] = Info.ToJson();

            if (Script != null)
                json["script"] = Script.ToJson();

            // Add type-specific data
            if (SpecificData != null && SpecificData.Length > 0)
            {
                switch (Type)
                {
                    case DataType.Script:
                        json["scriptType"] = DecodeScriptType();
                        break;
                    case DataType.Shape:
                        json["shapeData"] = DecodeShapeData();
                        break;
                    case DataType.Transition:
                        json["transitionData"] = DecodeTransitionData();
                        break;
                    case DataType.Palette:
                        json["paletteData"] = DecodePaletteData();
                        break;
                    default:
                        json["dataSize"] = SpecificData.Length;
                        break;
                }
            }

            return json;
        }

        private string DecodeScriptType()
        {
            if (SpecificData.Length >= 2)
            {
                ushort scriptType = BigEndian.ToUInt16 (SpecificData, 0);
                switch ((ScriptType)scriptType)
                {
                    case ScriptType.Score: return "Score/Behavior";
                    case ScriptType.Movie: return "Movie";
                    case ScriptType.Parent: return "Parent";
                    default: return $"Unknown ({scriptType})";
                }
            }
            return "Unknown";
        }

        private JObject DecodeShapeData()
        {
            var shape = new JObject();
            if (SpecificData.Length >= 2)
            {
                shape["shapeType"] = BigEndian.ToUInt16 (SpecificData, 0);
            }
            return shape;
        }

        private JObject DecodeTransitionData()
        {
            var trans = new JObject();
            if (SpecificData.Length >= 4)
            {
                trans["duration"] = BigEndian.ToUInt16 (SpecificData, 0);
                trans["chunkSize"] = BigEndian.ToUInt16 (SpecificData, 2);
            }
            return trans;
        }

        private JObject DecodePaletteData()
        {
            var palette = new JObject();
            if (SpecificData.Length >= 2)
            {
                int colorCount = BigEndian.ToUInt16 (SpecificData, 0);
                palette["colorCount"] = colorCount;

                var colors = new JArray();
                int offset = 2;
                for (int i = 0; i < colorCount && offset + 6 <= SpecificData.Length; i++)
                {
                    var color = new JObject();
                    color["r"] = BigEndian.ToUInt16 (SpecificData, offset) >> 8;
                    color["g"] = BigEndian.ToUInt16 (SpecificData, offset + 2) >> 8;
                    color["b"] = BigEndian.ToUInt16 (SpecificData, offset + 4) >> 8;
                    colors.Add (color);
                    offset += 6;
                }
                palette["colors"] = colors;
            }
            return palette;
        }
    }

    internal class CastInfo
    {
        public uint     DataOffset;
        public uint     ScriptKey;
        public uint     Flags;
        public int      ScriptId;
        public string   Name;
        public string   SourceText;
        public string   CreatedBy;
        public string   ModifiedBy;
        public DateTime Created;
        public DateTime Modified;
        public List<byte[]> Items = new List<byte[]>();

        public void Deserialize (SerializationContext context, Reader reader)
        {
            long base_offset = reader.Position;
            DataOffset = reader.ReadU32();
            ScriptKey = reader.ReadU32();
            reader.Skip (4);
            Flags = reader.ReadU32();
            ScriptId = reader.ReadI32();
            reader.Position = base_offset + DataOffset;
            int table_len = reader.ReadU16();
            var offsets = new int[table_len];
            for (int i = 0; i < table_len; ++i)
                offsets[i] = reader.ReadI32();

            int data_length = reader.ReadI32();
            long list_offset = reader.Position;
            Items.Clear();
            Items.Capacity = offsets.Length;
            for (int i = 0; i < offsets.Length; ++i)
            {
                int offset = offsets[i];
                int next_offset = (i + 1 < offsets.Length) ? offsets[i+1] : data_length;
                reader.Position = list_offset + offset;
                Items.Add (reader.ReadBytes (next_offset - offset));
            }

            SourceText = Items.Count > 0 ? Binary.GetCString (Items[0], 0) : string.Empty;
            Name = GetString (1, context.Encoding);
            CreatedBy = GetString (2, context.Encoding);
            ModifiedBy = GetString (3, context.Encoding);

            // Parse dates if available
            if (Items.Count > 4 && Items[4].Length >= 4)
                Created = ParseMacDate (BigEndian.ToUInt32 (Items[4], 0));
            if (Items.Count > 5 && Items[5].Length >= 4)
                Modified = ParseMacDate (BigEndian.ToUInt32 (Items[5], 0));
        }

        string GetString (int item_idx, Encoding enc)
        {
            if (item_idx >= Items.Count)
                return string.Empty;
            var src = Items[item_idx];
            if (src.Length <= 1 || 0 == src[0])
                return string.Empty;
            int len = src[0];
            return enc.GetString (src, 1, Math.Min (len, src.Length - 1));
        }

        DateTime ParseMacDate (uint macTime)
        {
            // Mac epoch is January 1, 1904
            var epoch = new DateTime (1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epoch.AddSeconds (macTime);
        }

        public JObject ToJson()
        {
            var json = new JObject();
            json["name"] = Name;
            json["scriptId"] = ScriptId;
            json["flags"] = Flags;

            if (!string.IsNullOrEmpty (SourceText))
                json["sourceText"] = SourceText;
            if (!string.IsNullOrEmpty (CreatedBy))
                json["createdBy"] = CreatedBy;
            if (!string.IsNullOrEmpty (ModifiedBy))
                json["modifiedBy"] = ModifiedBy;
            if (Created != default (DateTime))
                json["created"] = Created.ToString("yyyy-MM-dd HH:mm:ss");
            if (Modified != default (DateTime))
                json["modified"] = Modified.ToString("yyyy-MM-dd HH:mm:ss");

            return json;
        }
    }

    internal class Cast
    {
        public int[]    Index;
        public string   Name;
        public int      CastId { get; set; }
        public int      MinMember { get; set; }
        public ScriptContext ScriptContext { get; set; }
        public Dictionary<int, CastMember> Members = new Dictionary<int, CastMember>();

        public Cast (SerializationContext context, Reader reader, DirectorEntry entry)
        {
            int count = (int)(entry.Size / 4);
            Index = new int[count];
            Deserialize (context, reader);
        }

        public void Deserialize (SerializationContext context, Reader reader)
        {
            reader = reader.CloneUnless (ByteOrder.BigEndian);
            for (int i = 0; i < Index.Length; ++i)
                Index[i] = reader.ReadI32();
        }

        public JObject ToJson()
        {
            var json = new JObject();
            json["name"] = Name;
            json["id"] = CastId;
            json["minMember"] = MinMember;
            json["memberCount"] = Members.Count;

            if (Members.Count > 0)
            {
                var membersArray = new JArray();
                foreach (var member in Members.Values.OrderBy (m => m.Id))
                {
                    membersArray.Add (member.ToJson());
                }
                json["members"] = membersArray;
            }

            return json;
        }
    }

    internal class Script
    {
        public int TotalLength;
        public int ScriptNumber;
        public int ParentNumber;
        public List<Handler> Handlers = new List<Handler>();
        public List<string> PropertyNames = new List<string>();
        public List<string> GlobalNames = new List<string>();
        public List<Literal> Literals = new List<Literal>();

        public void Deserialize (SerializationContext context, Reader reader)
        {
            reader = reader.CloneUnless (ByteOrder.BigEndian);
            long basePos = reader.Position;

            TotalLength = reader.ReadI32();
            reader.Skip (4); // totalLength2
            int headerLength = reader.ReadI32();
            ScriptNumber = reader.ReadI16();
            reader.Skip (2); // unk20
            ParentNumber = reader.ReadI16();

            reader.Position = basePos + headerLength;

            // Read handler vectors, properties, globals, handlers, literals...
            // This is simplified - full implementation would parse all script data
        }

        public JObject ToJson()
        {
            var json = new JObject();
            json["scriptNumber"] = ScriptNumber;
            json["parentNumber"] = ParentNumber;
            json["handlerCount"] = Handlers.Count;
            json["propertyCount"] = PropertyNames.Count;
            json["globalCount"] = GlobalNames.Count;
            json["literalCount"] = Literals.Count;

            if (PropertyNames.Count > 0)
                json["properties"] = new JArray (PropertyNames);
            if (GlobalNames.Count > 0)
                json["globals"] = new JArray (GlobalNames);

            return json;
        }
    }

    internal class Handler
    {
        public int NameId = 0;
        public int ArgumentCount = 0;
        public int LocalsCount = 0;
        public int LineCount = 0;
        public byte[] ByteCode = null;
    }

    internal class Literal
    {
        public int Type = 0;
        public object Value = null;
    }

    internal class ScriptContext
    {
        public List<ScriptEntry> Scripts = new List<ScriptEntry>();
        public int LnamSectionId = 0;
        public int ValidCount = 0;
        public int Flags = 0;

        public void Deserialize (SerializationContext context, Reader reader)
        {
            reader = reader.CloneUnless (ByteOrder.BigEndian);
            reader.Skip (8); // unknown0, unknown1
            int entryCount = reader.ReadI32();
            reader.Skip (4); // entryCount2
            int entriesOffset = reader.ReadI32();
            reader.Skip (16); // unknowns
            LnamSectionId = reader.ReadI32();
            ValidCount = reader.ReadI32();
            Flags = reader.ReadI16();
            reader.Skip (2); // freePointer

            reader.Position += entriesOffset - 48; // Adjust to entries

            Scripts.Clear();
            for (int i = 0; i < entryCount; i++)
            {
                var entry = new ScriptEntry();
                entry.Unknown0 = reader.ReadI16();
                entry.ChunkId = reader.ReadI32();
                entry.Id = i;
                reader.Skip (6); // unknown1, unknown2
                Scripts.Add (entry);
            }
        }
    }

    internal class ScriptEntry
    {
        public int Id;
        public int ChunkId;
        public short Unknown0;
    }

    internal class CastList
    {
        public uint DataOffset;
        public int OffsetCount;
        public int[] OffsetTable;
        public int ItemsLength;
        public int CastCount;
        public int ItemsPerCast;
        public List<byte[]> Items = new List<byte[]>();

        public readonly List<CastListEntry> Entries = new List<CastListEntry>();

        public void Deserialize (SerializationContext context, Reader reader)
        {
            long base_offset = reader.Position;
            reader = reader.CloneUnless (ByteOrder.BigEndian);
            DataOffset = reader.ReadU32();
            reader.Skip (2);
            CastCount = reader.ReadU16();
            ItemsPerCast = reader.ReadU16();
            reader.Skip (2);
            reader.Position = base_offset + DataOffset;
            OffsetCount = reader.ReadU16();
            OffsetTable = new int[OffsetCount];
            for (int i = 0; i < OffsetCount; ++i)
            {
                OffsetTable[i] = reader.ReadI32();
            }
            ItemsLength = reader.ReadI32();
            long items_offset = reader.Position;
            Items.Clear();
            Items.Capacity = OffsetCount;
            for (int i = 0; i < OffsetCount; ++i)
            {
                int offset = OffsetTable[i];
                int next_offset = (i + 1 < OffsetCount) ? OffsetTable[i + 1] : ItemsLength;
                int item_size = next_offset - offset;
                reader.Position = items_offset + offset;
                Items.Add (reader.ReadBytes (item_size));
            }

            Entries.Clear();
            Entries.Capacity = CastCount;
            int item_idx = 0;
            for (int i = 0; i < CastCount; ++i)
            {
                var entry = new CastListEntry();
                if (ItemsPerCast >= 1)
                    entry.Name = GetString (item_idx + 1, context.Encoding);
                if (ItemsPerCast >= 2)
                    entry.Path = GetString (item_idx + 2, context.Encoding);
                if (ItemsPerCast >= 3 && item_idx + 3 < Items.Count && Items[item_idx + 3].Length >= 2)
                    entry.Flags = BigEndian.ToUInt16 (Items[item_idx + 3], 0);
                if (ItemsPerCast >= 4 && item_idx + 4 < Items.Count && Items[item_idx + 4].Length >= 8)
                {
                    entry.MinMember = BigEndian.ToUInt16 (Items[item_idx + 4], 0);
                    entry.MaxMember = BigEndian.ToUInt16 (Items[item_idx + 4], 2);
                    entry.Id        = BigEndian.ToInt32 (Items[item_idx + 4], 4);
                }
                Entries.Add (entry);
                item_idx += ItemsPerCast;
            }
        }

        string GetString (int item_idx, Encoding enc)
        {
            if (item_idx >= Items.Count)
                return string.Empty;
            var src = Items[item_idx];
            if (src.Length <= 1 || 0 == src[0])
                return string.Empty;
            int len = src[0];
            return enc.GetString (src, 1, Math.Min (len, src.Length - 1));
        }
    }

    internal class CastListEntry
    {
        public string   Name;
        public string   Path;
        public ushort   Flags;
        public int      MinMember;
        public int      MaxMember;
        public int      Id;
    }

    internal class DirectorConfig
    {
        public short Length;
        public short FileVersion;
        public short StageTop;
        public short StageLeft;
        public short StageBottom;
        public short StageRight;
        public short MinMember;
        public short MaxMember;
        public ushort StageColor;
        public ushort BitDepth;
        public int Version;
        public int FrameRate;
        public int Platform;
        public int Protection;
        public uint CheckSum;
        public int DefaultPalette;
        public byte Field9;
        public byte Field10;
        public short CommentFont;
        public short CommentSize;
        public ushort CommentStyle;
        public byte Field17;
        public byte Field18;
        public int Field19;
        public short Field21;
        public int Field22;
        public int Field23;
        public int Field24;
        public byte Field25;
        public byte Field26;
        public int Field29;

        // D7+ specific fields
        public byte D7StageColorR;
        public byte D7StageColorG;
        public byte D7StageColorB;
        public byte D7StageColorIsRGB;

        public void Deserialize (SerializationContext context, Reader reader)
        {
            long base_offset = reader.Position;
            reader = reader.CloneUnless (ByteOrder.BigEndian);

            reader.Position = base_offset + 0x24;
            Version = reader.ReadU16();
            int humanVer = DirectorVersion.GetHumanVersion (Version);

            reader.Position = base_offset;
            Length = reader.ReadI16();
            FileVersion = reader.ReadI16();
            StageTop = reader.ReadI16();
            StageLeft = reader.ReadI16();
            StageBottom = reader.ReadI16();
            StageRight = reader.ReadI16();
            MinMember = reader.ReadI16();
            MaxMember = reader.ReadI16();
            Field9 = reader.ReadU8();
            Field10 = reader.ReadU8();

            if (humanVer < 700)
            {
                reader.Skip (2); // preD7field11
            }
            else
            {
                D7StageColorG = reader.ReadU8();
                D7StageColorB = reader.ReadU8();
            }

            CommentFont = reader.ReadI16();
            CommentSize = reader.ReadI16();
            CommentStyle = reader.ReadU16();

            if (humanVer < 700)
            {
                StageColor = reader.ReadU16();
            }
            else
            {
                D7StageColorIsRGB = reader.ReadU8();
                D7StageColorR = reader.ReadU8();
            }

            BitDepth = reader.ReadU16();
            Field17 = reader.ReadU8();
            Field18 = reader.ReadU8();
            Field19 = reader.ReadI32();
            reader.Skip (2); // directorVersion (already read)
            Field21 = reader.ReadI16();
            Field22 = reader.ReadI32();
            Field23 = reader.ReadI32();
            Field24 = reader.ReadI32();
            Field25 = reader.ReadU8();
            Field26 = reader.ReadU8();
            FrameRate = reader.ReadU16();
            Platform = reader.ReadI16();
            Protection = reader.ReadI16();
            Field29 = reader.ReadI32();
            CheckSum = reader.ReadU32();

            if (humanVer > 1200)
            {
                reader.Position = base_offset + 0x4E;
            }
            else
            {
                reader.Position = base_offset + 0x46;
            }
            DefaultPalette = reader.ReadU16();
        }

        public JObject ToJson()
        {
            var json = new JObject();
            json["version"] = DirectorVersion.GetHumanVersion (Version);
            json["directorVersion"] = Version;
            json["fileVersion"] = FileVersion;
            json["frameRate"] = FrameRate;
            json["platform"] = GetPlatformName (Platform);
            json["protection"] = Protection;
            json["stage"] = new JObject
            {
                ["top"] = StageTop,
                ["left"] = StageLeft,
                ["bottom"] = StageBottom,
                ["right"] = StageRight,
                ["width"] = StageRight - StageLeft,
                ["height"] = StageBottom - StageTop
            };
            json["memberRange"] = new JObject
            {
                ["min"] = MinMember,
                ["max"] = MaxMember
            };
            json["bitDepth"] = BitDepth;
            json["defaultPalette"] = DefaultPalette;

            if (DirectorVersion.GetHumanVersion (Version) >= 700)
            {
                json["stageColor"] = new JObject
                {
                    ["r"] = D7StageColorR,
                    ["g"] = D7StageColorG,
                    ["b"] = D7StageColorB,
                    ["isRGB"] = D7StageColorIsRGB != 0
                };
            }
            else
            {
                json["stageColor"] = StageColor;
            }

            return json;
        }

        private string GetPlatformName (int platform)
        {
            switch (platform)
            {
                case 1: return "Macintosh";
                case 2: return "Windows";
                default: return $"Unknown ({platform})";
            }
        }
    }

    internal class InitialMapChunk
    {
        public uint Version;
        public uint MmapOffset;
        public uint DirectorVersion;

        public void Deserialize (SerializationContext context, Reader reader)
        {
            Version = reader.ReadU32();
            MmapOffset = reader.ReadU32();
            DirectorVersion = reader.ReadU32();
            reader.Skip (12); // unused fields
        }
    }

    internal class KeyTable
    {
        public int  EntrySize;
        public int  TotalCount;
        public int  UsedCount;
        public readonly List<KeyTableEntry> Table = new List<KeyTableEntry>();

        public KeyTableEntry this[int index] => Table[index];

        public KeyTableEntry FindByCast (int cast_id, string four_cc)
        {
            return Table.Find (e => e.CastId == cast_id && e.FourCC == four_cc);
        }

        public void Deserialize (SerializationContext context, Reader reader)
        {
            EntrySize = reader.ReadU16();
            reader.Skip (2);
            TotalCount = reader.ReadI32();
            UsedCount = reader.ReadI32();

            Table.Clear();
            Table.Capacity = TotalCount;
            for (int i = 0; i < TotalCount; ++i)
            {
                var entry = new KeyTableEntry();
                entry.Deserialize (context, reader);
                Table.Add (entry);
            }
        }
    }

    internal class KeyTableEntry
    {
        public int      Id;
        public int      CastId;
        public string   FourCC;

        public void Deserialize (SerializationContext context, Reader input)
        {
            Id     = input.ReadI32();
            CastId = input.ReadI32();
            FourCC = input.ReadFourCC();
        }
    }

    internal class MemoryMap
    {
        public ushort   HeaderLength;
        public ushort   EntryLength;
        public int      ChunkCountMax;
        public int      ChunkCountUsed;
        public int      JunkHead;
        public int      JunkHead2;
        public int      FreeHead;
        public readonly List<DirectorEntry> Dir = new List<DirectorEntry>();

        public DirectorEntry this[int index] => Dir[index];

        public void Deserialize (SerializationContext context, Reader reader)
        {
            long header_pos = reader.Position;
            HeaderLength = reader.ReadU16();
            if (HeaderLength < 0x18)
                throw new InvalidFormatException ("Invalid <mmap> header length.");
            EntryLength = reader.ReadU16();
            if (EntryLength < 0x14)
                throw new InvalidFormatException ("Invalid <mmap> entry length.");
            ChunkCountMax = reader.ReadI32();
            ChunkCountUsed = reader.ReadI32();
            JunkHead = reader.ReadI32();
            JunkHead2 = reader.ReadI32();
            FreeHead = reader.ReadI32();

            Dir.Clear();
            Dir.Capacity = ChunkCountUsed;
            long entry_pos = header_pos + HeaderLength;
            for (int i = 0; i < ChunkCountUsed; ++i)
            {
                reader.Position = entry_pos;
                var entry = new MemoryMapEntry (i);
                entry.Deserialize (context, reader);
                if (entry.FourCC != "free" && entry.FourCC != "junk")
                    Dir.Add (entry);
                entry_pos += EntryLength;
            }
        }
    }

    internal class DirectorEntry : PackedEntry
    {
        public int    Id;
        public string FourCC;

        public DirectorEntry()
        {
            Type = "data";
            Offset = -1;
        }
    }

    internal class MemoryMapEntry : DirectorEntry
    {
        public ushort   Flags;
        public short    Unknown0;
        public int      Next;

        public MemoryMapEntry (int id = 0)
        {
            Id = id;
        }

        public void Deserialize (SerializationContext context, Reader reader)
        {
            FourCC   = reader.ReadFourCC();
            Size     = reader.ReadU32();
            Offset   = reader.ReadU32() + 8;
            Flags    = reader.ReadU16();
            Unknown0 = reader.ReadI16();
            Next     = reader.ReadI32();
            UnpackedSize = Size;
            IsPacked = false;
        }
    }

    internal class AfterBurnerEntry : DirectorEntry
    {
        public int      CompMethod;
        public CompressionType CompressionType;

        public void Deserialize (SerializationContext context, Reader reader)
        {
            Id           = reader.ReadVarInt();
            Offset       = reader.ReadVarInt();
            Size         = (uint)reader.ReadVarInt();
            UnpackedSize = (uint)reader.ReadVarInt();
            CompMethod   = reader.ReadVarInt(); // assume zlib
            FourCC       = reader.ReadFourCC();
            IsPacked     = Size != UnpackedSize;
        }
    }

    internal class CompressionType
    {
        public Guid Guid;
        public string Description;

        public void Deserialize (SerializationContext context, Reader reader)
        {
            // Read GUID (16 bytes)
            byte[] guidBytes = reader.ReadBytes (16);
            Guid = new Guid (guidBytes);

            // Read null-terminated string
            var bytes = new List<byte>();
            byte b;
            while ((b = reader.ReadU8()) != 0)
                bytes.Add (b);
            Description = Encoding.ASCII.GetString (bytes.ToArray());
        }
    }

    internal static class DirectorVersion
    {
        public static int GetHumanVersion (int version)
        {
            if (version >= 1951) return 1200;
            if (version >= 1922) return 1150;
            if (version >= 1921) return 1100;
            if (version >= 1851) return 1000;
            if (version >= 1700) return 850;
            if (version >= 1410) return 800;
            if (version >= 1224) return 700;
            if (version >= 1218) return 600;
            if (version >= 1201) return 500;
            if (version >= 1117) return 404;
            if (version >= 1115) return 400;
            if (version >= 1029) return 310;
            if (version >= 1028) return 300;
            return 200;
        }

        public static string GetVersionString (int version, string fverVersionString = null)
        {
            int humanVer = GetHumanVersion (version);
            int major = humanVer / 100;
            int minor = (humanVer / 10) % 10;
            int patch = humanVer % 10;

            string versionNumber;
            if (string.IsNullOrEmpty (fverVersionString))
            {
                versionNumber = $"{major}.{minor}";
                if (patch > 0)
                    versionNumber += $".{patch}";
            }
            else
            {
                versionNumber = fverVersionString;
            }

            if (major >= 11)
                return $"Adobe Director {versionNumber}";
            if (major == 10)
                return $"Macromedia Director MX 2004 ({versionNumber})";
            if (major == 9)
                return $"Macromedia Director MX ({versionNumber})";

            return $"Macromedia Director {versionNumber}";
        }
    }

    internal class Reader
    {
        Stream      m_input;
        byte[]      m_buffer = new byte[4];

        public Reader (Stream input, ByteOrder e = ByteOrder.LittleEndian) : this (input, Encodings.cp932, e)
        {
        }

        public Reader (Stream input, Encoding enc, ByteOrder e = ByteOrder.LittleEndian)
        {
            m_input = input;
            Encoding = enc;
            SetByteOrder (e);
        }

        public Stream Source => m_input;

        public ByteOrder ByteOrder { get; private set; }

        public Encoding Encoding { get; set; }

        public long Position
        {
            get => m_input.Position;
            set => m_input.Position = value;
        }

        private Func<ushort> ToU16;
        private Func<uint>   ToU32;

        public void SetByteOrder (ByteOrder e)
        {
            this.ByteOrder = e;
            if (ByteOrder.LittleEndian == e)
            {
                ToU16 = () => LittleEndian.ToUInt16 (m_buffer, 0);
                ToU32 = () => LittleEndian.ToUInt32 (m_buffer, 0);
            }
            else
            {
                ToU16 = () => BigEndian.ToUInt16 (m_buffer, 0);
                ToU32 = () => BigEndian.ToUInt32 (m_buffer, 0);
            }
        }

        static Dictionary<uint, string> KnownFourCC = new Dictionary<uint, string>();

        public string ReadFourCC ()
        {
            uint signature = ReadU32();
            string four_cc;
            if (KnownFourCC.TryGetValue (signature, out four_cc))
                return four_cc;
            BigEndian.Pack (signature, m_buffer, 0);
            return KnownFourCC[signature] = Encoding.ASCII.GetString (m_buffer, 0, 4);
        }

        public void Skip (int amount) => m_input.Seek (amount, SeekOrigin.Current);

        public byte ReadU8 ()
        {
            int b = m_input.ReadByte();
            if (-1 == b)
                throw new EndOfStreamException();
            return (byte)b;
        }

        public sbyte ReadI8 () => (sbyte)ReadU8();

        public ushort ReadU16 ()
        {
            if (m_input.Read (m_buffer, 0, 2) < 2)
                throw new EndOfStreamException();
            return ToU16();
        }

        public short ReadI16 () => (short)ReadU16();

        public uint ReadU32 ()
        {
            if (m_input.Read (m_buffer, 0, 4) < 4)
                throw new EndOfStreamException();
            return ToU32();
        }

        public int ReadI32 () => (int)ReadU32();

        public byte[] ReadBytes (int length)
        {
            if (0 == length)
                return Array.Empty<byte>();
            var buffer = new byte[length];
            if (m_input.Read (buffer, 0, length) < length)
                throw new EndOfStreamException();
            return buffer;
        }

        public int ReadVarInt ()
        {
            int n = 0;
            for (int i = 0; i < 5; ++i)
            {
                int bits = m_input.ReadByte();
                if (-1 == bits)
                    throw new EndOfStreamException();
                n = n << 7 | bits & 0x7F;
                if (0 == (bits & 0x80))
                    return n;
            }
            throw new InvalidFormatException();
        }

        public uint GetVarIntLength (uint i)
        {
            uint n = 1;
            while (i > 0x7F)
            {
                i >>= 7;
                ++n;
            }
            return n;
        }

        public Reader CloneUnless (ByteOrder order)
        {
            if (this.ByteOrder != order)
                return new Reader (this.Source, this.Encoding, order);
            else
                return this;
        }
    }

    // Extension class for JSON export functionality
    internal static class DirectorJsonExporter
    {
        public static string ExportToJson (DirectorFile file, bool prettyPrint = true)
        {
            var json = file.ToJson();
            return json.ToString (prettyPrint ? Formatting.Indented : Formatting.None);
        }

        public static string ExportCastMemberToJson (CastMember member, bool prettyPrint = true)
        {
            var json = member.ToJson();
            return json.ToString (prettyPrint ? Formatting.Indented : Formatting.None);
        }

        public static string ExportChunkListToJson (DirectorFile file, bool prettyPrint = true)
        {
            var json = new JObject();
            var chunks = new JArray();

            foreach (var entry in file.Directory.OrderBy (e => e.Id))
            {
                var chunk = new JObject();
                chunk["id"] = entry.Id;
                chunk["fourCC"] = entry.FourCC;
                chunk["size"] = entry.Size;
                chunk["offset"] = entry.Offset;

                if (entry.IsPacked)
                {
                    chunk["packed"] = true;
                    chunk["unpackedSize"] = entry.UnpackedSize;
                }

                // Add type-specific information
                switch (entry.FourCC)
                {
                    case "CAS*":
                        chunk["type"] = "Cast";
                        break;
                    case "CASt":
                        chunk["type"] = "Cast Member";
                        break;
                    case "KEY*":
                        chunk["type"] = "Key Table";
                        break;
                    case "Lscr":
                        chunk["type"] = "Script";
                        break;
                    case "Lnam":
                        chunk["type"] = "Script Names";
                        break;
                    case "Lctx":
                    case "LctX":
                        chunk["type"] = "Script Context";
                        break;
                    case "VWCF":
                    case "DRCF":
                        chunk["type"] = "Configuration";
                        break;
                    case "MCsL":
                        chunk["type"] = "Cast List";
                        break;
                    case "STXT":
                        chunk["type"] = "Styled Text";
                        break;
                    case "BITD":
                        chunk["type"] = "Bitmap Data";
                        break;
                    case "snd ":
                        chunk["type"] = "Sound";
                        break;
                    case "VWSC":
                        chunk["type"] = "Score";
                        break;
                    case "VWLB":
                        chunk["type"] = "Labels";
                        break;
                    case "VWFI":
                        chunk["type"] = "File Info";
                        break;
                    case "XMED":
                        chunk["type"] = "External Media";
                        break;
                    case "THUM":
                        chunk["type"] = "Thumbnail";
                        break;
                }

                chunks.Add (chunk);
            }

            json["chunkCount"] = chunks.Count;
            json["chunks"] = chunks;

            return json.ToString (prettyPrint ? Formatting.Indented : Formatting.None);
        }

        public static string ExportScriptSummaryToJson (DirectorFile file, bool prettyPrint = true)
        {
            var json = new JObject();
            var scripts = new JArray();

            foreach (var cast in file.Casts)
            {
                foreach (var member in cast.Members.Values.Where (m => m.Type == DataType.Script))
                {
                    var script = new JObject();
                    script["castName"] = cast.Name;
                    script["memberId"] = member.Id;
                    script["memberName"] = member.Info?.Name ?? "";

                    if (member.SpecificData != null && member.SpecificData.Length >= 2)
                    {
                        ushort scriptType = BigEndian.ToUInt16 (member.SpecificData, 0);
                        script["scriptType"] = GetScriptTypeName (scriptType);
                    }

                    if (!string.IsNullOrEmpty (member.Info?.SourceText))
                    {
                        var lines = member.Info.SourceText.Split (new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        script["lineCount"] = lines.Length;
                        script["preview"] = lines.FirstOrDefault() ?? "";
                    }

                    scripts.Add (script);
                }
            }

            json["scriptCount"] = scripts.Count;
            json["scripts"] = scripts;

            return json.ToString (prettyPrint ? Formatting.Indented : Formatting.None);
        }

        private static string GetScriptTypeName (ushort type)
        {
            switch ((ScriptType)type)
            {
                case ScriptType.Score: return "Score/Behavior Script";
                case ScriptType.Movie: return "Movie Script";
                case ScriptType.Parent: return "Parent Script";
                default: return $"Unknown ({type})";
            }
        }

        public static string ExportMediaSummaryToJson (DirectorFile file, bool prettyPrint = true)
        {
            var json = new JObject();
            var summary = new JObject();

            var typeCounts = new Dictionary<DataType, int>();
            foreach (var cast in file.Casts)
            {
                foreach (var member in cast.Members.Values)
                {
                    if (!typeCounts.ContainsKey (member.Type))
                        typeCounts[member.Type] = 0;
                    typeCounts[member.Type]++;
                }
            }

            foreach (var kvp in typeCounts.OrderBy (k => k.Key))
            {
                summary[kvp.Key.ToString()] = kvp.Value;
            }

            json["totalMembers"] = typeCounts.Values.Sum();
            json["memberTypes"] = summary;

            // Add cast summary
            var casts = new JArray();
            foreach (var cast in file.Casts)
            {
                var castInfo = new JObject();
                castInfo["name"] = cast.Name;
                castInfo["id"] = cast.CastId;
                castInfo["memberCount"] = cast.Members.Count;

                var castTypes = new JObject();
                foreach (var member in cast.Members.Values)
                {
                    if (!castTypes.ContainsKey (member.Type.ToString()))
                        castTypes[member.Type.ToString()] = 0;
                    castTypes[member.Type.ToString()] = (int)castTypes[member.Type.ToString()] + 1;
                }
                castInfo["types"] = castTypes;

                casts.Add (castInfo);
            }
            json["casts"] = casts;

            return json.ToString (prettyPrint ? Formatting.Indented : Formatting.None);
        }
    }
}