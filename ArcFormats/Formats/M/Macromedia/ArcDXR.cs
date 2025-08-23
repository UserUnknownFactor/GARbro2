using GameRes.Compression;
using GameRes.Utility;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameRes.Formats.Macromedia
{
    [Export(typeof(ArchiveFormat))]
    public class DxrOpener : ArchiveFormat
    {
        public override string         Tag => "DXR";
        public override string Description => "Macromedia Director resource archive";
        public override uint     Signature => SignatureXFIR; // 'XFIR'
        public override bool  IsHierarchic => true;
        public override bool      CanWrite => false;

        public const uint SignatureXFIR = 0x52494658u;
        public const uint SignatureRIFX = 0x58464952u;

        public DxrOpener ()
        {
            Extensions = new[] { "dxr", "cxt", "cct", "dcr", "dir", "exe" };
            Signatures = new[] { SignatureXFIR, SignatureRIFX, 0x00905A4Du };
        }

        internal static readonly HashSet<string> RawChunks = new HashSet<string> {
            "RTE0", "RTE1", "FXmp", "VWFI", "VWSC", "Lscr", "STXT", "XMED", "File"
        };

        internal static readonly HashSet<string> JsonExportChunks = new HashSet<string> {
            "KEY*", "CAS*", "CASt", "Lctx", "LctX", "Lnam", "Lscr", "VWCF", "DRCF",
            "MCsL", "VWLB", "VWFI", "VWSC"
        };

        internal bool ConvertText = true;
        internal bool ExportJson = true;

        public override ArcFile TryOpen (ArcView file)
        {
            long base_offset = 0;
            if (file.View.AsciiEqual (0, "MZ"))
                base_offset = LookForXfir (file);

            uint signature = file.View.ReadUInt32 (base_offset);
            if (signature != SignatureXFIR && signature != SignatureRIFX)
                return null;

            using (var input = file.CreateStream())
            {
                ByteOrder ord = signature == SignatureXFIR ? ByteOrder.LittleEndian : ByteOrder.BigEndian;
                var reader = new Reader (input, ord);
                reader.Position = base_offset;
                var context = new SerializationContext();
                var dir_file = new DirectorFile();
                if (!dir_file.Deserialize (context, reader))
                    return null;

                var dir = new List<Entry>();

                if (ExportJson)
                    AddJsonExports (dir_file, dir);

                if (dir_file.Codec != "APPL")
                    ImportMedia (dir_file, dir);

                foreach (DirectorEntry entry in dir_file.Directory)
                {
                    if (entry.Size != 0 && entry.Offset != -1 && RawChunks.Contains (entry.FourCC))
                    {
                        string folder = GetChunkFolder (entry.FourCC);
                        entry.Name = $"{folder}/{entry.Id:D6}.{entry.FourCC.Trim()}";

                        if ("File" == entry.FourCC)
                        {
                            entry.Offset -= 8;
                            entry.Size   += 8;
                        }

                        if (entry.FourCC == "XMED")
                        {
                            const int header_offset = 0xC;
                            if (entry.Size > header_offset + 4)
                            {
                                var checkStream = OpenChunkStream (file, entry);
                                var headerData = new byte[header_offset + 4];
                                if (checkStream.Read (headerData, 0, headerData.Length) == headerData.Length)
                                {
                                    // Check for SWF signature (FWS/CWS/ZWS)
                                    if ((headerData[header_offset] == 'F' || headerData[header_offset] == 'C' || headerData[header_offset] == 'Z') &&
                                        headerData[header_offset + 1] == 'W' &&
                                        headerData[header_offset + 2] == 'S')
                                    {
                                        var swfEntry = new DirectorEntry
                                        {
                                            Name = $"Media/{entry.Id:D6}.swf",
                                            Type = "data",
                                            Offset = entry.Offset + header_offset,
                                            Size = entry.Size - header_offset,
                                            UnpackedSize = entry.Size - header_offset,
                                            IsPacked = false,
                                            Id = entry.Id,
                                            FourCC = entry.FourCC
                                        };
                                        dir.Add (swfEntry);
                                        continue;
                                    }
                                }
                            }

                            entry.Name = $"Media/{entry.Id:D6}.xmed";
                            entry.IsPacked = false;
                            entry.UnpackedSize = entry.Size;
                        }
                        else if (entry.FourCC == "STXT" || entry.FourCC == "VWFI")
                        {
                            entry.IsPacked = false;
                            entry.UnpackedSize = entry.Size;
                        }

                        dir.Add (entry);
                    }
                }

                return new DirectorArchive (file, this, dir, dir_file);
            }
        }

        string GetChunkFolder (string fourCC)
        {
            switch (fourCC)
            {
                case "STXT":
                    return "Texts/Chunks";
                case "XMED":
                    return "Media";
                case "Lscr":
                    return "Scripts/Chunks";
                case "VWSC":
                case "VWFI":
                case "VWLB":
                    return "Metadata/Chunks";
                case "RTE0":
                case "RTE1":
                case "FXmp":
                    return "Data/chunks";
                case "File":
                    return "Files";
                default:
                    return "Chunks/Misc";
            }
        }

        void AddJsonExports (DirectorFile dir_file, List<Entry> dir)
        {
            dir.Add (new JsonEntry
            {
                Name = "Metadata/!director_summary.json",
                Data = DirectorJsonExporter.ExportToJson (dir_file, true)
            });

            dir.Add (new JsonEntry
            {
                Name = "Metadata/!chunk_list.json",
                Data = DirectorJsonExporter.ExportChunkListToJson (dir_file, true)
            });

            if (dir_file.Casts.Any (c => c.Members.Values.Any (m => m.Type == DataType.Script)))
            {
                dir.Add (new JsonEntry
                {
                    Name = "Metadata/!script_summary.json",
                    Data = DirectorJsonExporter.ExportScriptSummaryToJson (dir_file, true)
                });
            }

            dir.Add (new JsonEntry
            {
                Name = "Metadata/!media_summary.json",
                Data = DirectorJsonExporter.ExportMediaSummaryToJson (dir_file, true)
            });

            foreach (var cast in dir_file.Casts)
            {
                foreach (var member in cast.Members.Values)
                {
                    if (ShouldExportAsJson (member))
                    {
                        var castName = SanitizeName (cast.Name, cast.CastId);
                        var memberName = SanitizeName (member.Info?.Name, member.Id);
                        string folder = GetMemberJsonFolder (member.Type);
                        var fileName = $"{folder}/{castName}/{memberName}_{member.Id}.json";

                        dir.Add (new JsonEntry
                        {
                            Name = fileName,
                            Data = DirectorJsonExporter.ExportCastMemberToJson (member, true)
                        });
                    }
                }
            }

            foreach (var entry in dir_file.Directory)
            {
                if (JsonExportChunks.Contains (entry.FourCC))
                {
                    var jsonData = ExportChunkAsJson (dir_file, entry);
                    if (jsonData != null)
                    {
                        dir.Add (new JsonEntry
                        {
                            Name = $"Metadata/Chunks/{entry.Id:D6}_{entry.FourCC}.json",
                            Data = jsonData
                        });
                    }
                }
            }
        }

        string GetMemberJsonFolder (DataType type)
        {
            switch (type)
            {
                case DataType.Script:
                    return "Scripts/Metadata";
                case DataType.Shape:
                    return "Shapes/Metadata";
                case DataType.Transition:
                    return "Transitions/Metadata";
                case DataType.Palette:
                    return "Palettes/Metadata";
                case DataType.Button:
                    return "Buttons/Metadata";
                case DataType.Font:
                    return "Fonts/Metadata";
                case DataType.Movie:
                    return "Movies/Metadata";
                default:
                    return "Metadata/Misc";
            }
        }

        bool ShouldExportAsJson (CastMember member)
        {
            switch (member.Type)
            {
                case DataType.Script:
                case DataType.Shape:
                case DataType.Transition:
                case DataType.Palette:
                case DataType.Button:
                case DataType.Movie:
                case DataType.Font:
                    return true;
                default:
                    return false;
            }
        }

        string ExportChunkAsJson (DirectorFile dir_file, DirectorEntry entry)
        {
            try
            {
                var json = new JObject();
                json["id"] = entry.Id;
                json["fourCC"] = entry.FourCC;
                json["size"] = entry.Size;
                json["offset"] = entry.Offset;

                switch (entry.FourCC)
                {
                    case "KEY*":
                        json["type"] = "KeyTable";
                        json["entries"] = ExportKeyTable (dir_file.KeyTable);
                        break;

                    case "VWCF":
                    case "DRCF":
                        json["type"] = "Configuration";
                        json["config"] = dir_file.Config.ToJson();
                        break;

                    case "VWLB":
                        json["type"] = "Labels";
                        break;

                    case "VWFI":
                        json["type"] = "FileInfo";
                        break;

                    default:
                        return null;
                }

                return json.ToString (Formatting.Indented);
            }
            catch
            {
                return null;
            }
        }

        JArray ExportKeyTable (KeyTable keyTable)
        {
            var entries = new JArray();
            foreach (var entry in keyTable.Table)
            {
                if (entry.Id != 0)
                {
                    entries.Add (new JObject
                    {
                        ["id"] = entry.Id,
                        ["castId"] = entry.CastId,
                        ["fourCC"] = entry.FourCC
                    });
                }
            }
            return entries;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var jsonEntry = entry as JsonEntry;
            if (jsonEntry != null && jsonEntry._data != null)
                return new MemoryStream (jsonEntry._data);

            var snd = entry as SoundEntry;
            if (snd != null)
                return OpenSound (arc, snd);

            var pent = entry as PackedEntry;
            if (null == pent)
                return base.OpenEntry (arc, entry);

            var ment = entry as DirectorEntry;
            var input = OpenChunkStream (arc.File, pent);
            if (null == ment || !ConvertText || ment.FourCC != "STXT")
                return input.AsStream;

            using (input)
            {
                uint offset = input.ReadUInt32BE();
                uint length = input.ReadUInt32BE();
                input.Position = offset;
                var text = input.ReadBytes ((int)length);
                return new BinMemoryStream (text, entry.Name);
            }
        }

        internal Stream OpenSound (ArcFile arc, SoundEntry entry)
        {
            if (null == entry.Header)
                return base.OpenEntry (arc, entry);

            var header = new byte[entry.Header.UnpackedSize];
            using (var input = OpenChunkStream (arc.File, entry.Header))
                input.Read (header, 0, header.Length);

            var format = entry.DeserializeHeader (header);
            var riff = new MemoryStream (0x2C);
            WaveAudio.WriteRiffHeader (riff, format, entry.Size);
            if (format.BitsPerSample < 16)
            {
                using (riff)
                {
                    var input = OpenChunkStream (arc.File, entry).AsStream;
                    return new PrefixStream (riff.ToArray(), input);
                }
            }
            // samples are stored in big-endian format
            var samples = new byte[entry.UnpackedSize];
            using (var input = OpenChunkStream (arc.File, entry))
                input.Read (samples, 0, samples.Length);
            for (int i = 1; i < samples.Length; i += 2)
            {
                byte s = samples[i-1];
                samples[i-1] = samples[i];
                samples[i] = s;
            }
            riff.Write (samples, 0, samples.Length);
            riff.Position = 0;
            return riff;
        }

        void ImportMedia (DirectorFile dir_file, List<Entry> dir)
        {
            var seen_ids = new HashSet<int>();
            foreach (var cast in dir_file.Casts)
            {
                var castName = SanitizeName (cast.Name, cast.CastId);
                foreach (var piece in cast.Members.Values)
                {
                    if (seen_ids.Contains (piece.Id))
                        continue;
                    seen_ids.Add (piece.Id);
                    Entry entry = null;

                    switch (piece.Type)
                    {
                        case DataType.Bitmap:
                            entry = ImportBitmap (piece, dir_file, cast, castName);
                            break;
                        case DataType.Sound:
                            entry = ImportSound (piece, dir_file, castName);
                            break;
                        case DataType.Text:
                        case DataType.RichText:
                            entry = ImportText (piece, dir_file, castName);
                            break;
                        case DataType.Script:
                            if (piece.Script != null && ExportJson)
                                entry = ImportScript (piece, dir_file, cast);
                            break;
                    }

                    if (entry != null && entry.Size > 0)
                        dir.Add (entry);
                }
            }
        }

        Entry ImportScript (CastMember script, DirectorFile dir_file, Cast cast)
        {
            var name = SanitizeName (script.Info?.Name ?? "Script", script.Id);
            var castName = SanitizeName (cast.Name, cast.CastId);

            var scriptData = new JObject();
            scriptData["cast"] = castName;
            scriptData["member"] = script.ToJson();

            return new JsonEntry
            {
                Name = $"Scripts/{castName}/{name}.json",
                Data = scriptData.ToString (Formatting.Indented)
            };
        }

        Entry ImportText (CastMember text, DirectorFile dir_file, string castName)
        {
            var name = SanitizeName (text.Info?.Name, text.Id);
            var stxt = dir_file.KeyTable.FindByCast (text.Id, "STXT");
            if (stxt != null)
            {
                var chunk = dir_file.Index[stxt.Id];
                return new DirectorEntry
                {
                    Name = $"Texts/{castName}/{name}.txt",
                    Type = "text",
                    Offset = chunk.Offset,
                    Size = chunk.Size,
                    UnpackedSize = chunk.UnpackedSize,
                    IsPacked = chunk.IsPacked,
                    Id = chunk.Id,
                    FourCC = "STXT"
                };
            }
            return null;
        }

        Entry ImportSound (CastMember sound, DirectorFile dir_file, string castName)
        {
            var name = sound.Info.Name;
            KeyTableEntry sndHrec = null, sndSrec = null;
            foreach (var elem in dir_file.KeyTable.Table.Where (e => e.CastId == sound.Id))
            {
                if ("ediM" == elem.FourCC)
                {
                    var ediM = dir_file.Index[elem.Id];
                    name = SanitizeName (name, ediM.Id);
                    return new PackedEntry
                    {
                        Name = $"Audio/{castName}/{name}.mp3",  // ediM usually contains MP3
                        Type = "audio",
                        Offset       = ediM.Offset,
                        Size         = ediM.Size,
                        UnpackedSize = ediM.UnpackedSize,
                        IsPacked     = ediM.IsPacked
                    };
                }
                else if ("snd " == elem.FourCC)
                {
                    var snd = dir_file.Index[elem.Id];
                    if (snd.Size != 0)
                    {
                        name = SanitizeName (name, snd.Id);
                        return new PackedEntry
                        {
                            Name = $"Audio/{castName}/{name}.snd",
                            Type = "audio",
                            Offset = snd.Offset,
                            Size = snd.Size,
                            UnpackedSize = snd.Size,
                            IsPacked = false,
                        };
                    }
                }
                if (null == sndHrec && "sndH" == elem.FourCC)
                    sndHrec = elem;
                else if (null == sndSrec && "sndS" == elem.FourCC)
                    sndSrec = elem;
            }
            if (sndHrec == null || sndSrec == null)
                return null;
            var sndH = dir_file.Index[sndHrec.Id];
            var sndS = dir_file.Index[sndSrec.Id];
            name = SanitizeName (name, sndSrec.Id);
            return new SoundEntry
            {
                Name   = $"Audio/{castName}/{name}.wav",
                Type   = "audio",
                Offset = sndS.Offset,
                Size   = sndS.Size,
                UnpackedSize = sndS.UnpackedSize,
                IsPacked = sndS.IsPacked,
                Header = sndH,
            };
        }

        Entry ImportBitmap (CastMember bitmap, DirectorFile dir_file, Cast cast, string castName)
        {
            KeyTableEntry bitd = null, edim = null, alfa = null;
            foreach (var elem in dir_file.KeyTable.Table.Where (e => e.CastId == bitmap.Id))
            {
                if (null == bitd && "BITD" == elem.FourCC)
                    bitd = elem;
                else if (null == edim && "ediM" == elem.FourCC)
                    edim = elem;
                else if (null == alfa && "ALFA" == elem.FourCC)
                    alfa = elem;
            }
            if (bitd == null && edim == null)
                return null;
            var entry = new BitmapEntry();
            if (bitd != null)
            {
                entry.DeserializeHeader (bitmap.SpecificData);
                var name = SanitizeName (bitmap.Info.Name, bitd.Id);
                var chunk = dir_file.Index[bitd.Id];
                entry.Name   = $"Images/{castName}/{name}.bmp";  // Will be converted to BMP
                entry.Type   = "image";
                entry.Offset = chunk.Offset;
                entry.Size   = chunk.Size;
                entry.IsPacked = chunk.IsPacked;
                entry.UnpackedSize = chunk.UnpackedSize;
                if (entry.Palette > 0)
                {
                    var cast_id = cast.Index[entry.Palette-1];
                    var clut = dir_file.KeyTable.FindByCast (cast_id, "CLUT");
                    if (clut != null)
                        entry.PaletteRef = dir_file.Index[clut.Id];
                }
            }
            else // if (edim != null)
            {
                var name = SanitizeName (bitmap.Info.Name, edim.Id);
                var chunk = dir_file.Index[edim.Id];
                entry.Name   = $"Images/{castName}/{name}.jpg";
                entry.Type   = "image";
                entry.Offset = chunk.Offset;
                entry.Size   = chunk.Size;
                entry.IsPacked = false;
                entry.UnpackedSize = entry.Size;
            }
            if (alfa != null)
                entry.AlphaRef = dir_file.Index[alfa.Id];
            return entry;
        }

        static readonly Regex ForbiddenCharsRe = new Regex(@"[:?*<>/\\]");

        string SanitizeName (string name, int id)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty (name))
                name = id.ToString ("D6");
            else
                name = ForbiddenCharsRe.Replace (name, "_");
            return name;
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var bent = entry as BitmapEntry;
            if (null == bent)
                return base.OpenImage (arc, entry);
            if (entry.Name.HasExtension (".jpg"))
                return OpenJpeg (arc, bent);

            BitmapPalette palette = null;
            if (bent.PaletteRef != null)
            {
                using (var pal = OpenChunkStream (arc.File, bent.PaletteRef))
                {
                    var pal_bytes = pal.ReadBytes ((int)bent.PaletteRef.UnpackedSize);
                    palette = ReadPalette (pal_bytes);
                }
            }
            else if (bent.BitDepth <= 8)
            {
                switch (bent.Palette)
                {
                case  0:    palette = Palettes.SystemMac; break;
                case -1:    palette = Palettes.Rainbow; break;
                case -2:    palette = Palettes.Grayscale; break;
                case -100:  palette = Palettes.WindowsDirector4; break;
                default:
                case -101:  palette = Palettes.SystemWindows; break;
                }
            }
            var info = new BitdMetaData
            {
                Width = (uint)(bent.Right - bent.Left),
                Height = (uint)(bent.Bottom - bent.Top),
                BPP = bent.BitDepth,
                DepthType = bent.DepthType,
            };
            byte[] alpha_channel = null;
            if (bent.AlphaRef != null)
                alpha_channel = ReadAlphaChannel (arc.File, bent.AlphaRef, info);
            var input = OpenChunkStream (arc.File, bent).AsStream;
            return new BitdDecoder (input, info, palette) { AlphaChannel = alpha_channel };
        }

        IImageDecoder OpenJpeg (ArcFile arc, BitmapEntry entry)
        {
            if (null == entry.AlphaRef)
                return base.OpenImage (arc, entry);

            // jpeg with alpha-channel
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            try
            {
                var info = ImageFormat.Jpeg.ReadMetaData (input);
                if (null == info)
                    throw new InvalidFormatException ("Invalid 'ediM' chunk.");
                var alpha_channel = ReadAlphaChannel (arc.File, entry.AlphaRef, info);
                return BitdDecoder.FromJpeg (input, info, alpha_channel);
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }

        byte[] ReadAlphaChannel (ArcView file, DirectorEntry entry, ImageMetaData info)
        {
            using (var alpha = OpenChunkStream (file, entry))
            {
                var alpha_info = new BitdMetaData
                {
                    Width = info.Width,
                    Height = info.Height,
                    BPP = 8,
                    DepthType = 0x80,
                };
                var decoder = new BitdDecoder (alpha.AsStream, alpha_info, null);
                return decoder.Unpack8bpp();
            }
        }

        BitmapPalette ReadPalette (byte[] data)
        {
            int num_colors = data.Length / 6;
            var colors = new Color[num_colors];
            for (int i = 0; i < data.Length; i += 6)
            {
                colors[i / 6] = Color.FromRgb (data[i], data[i + 2], data[i + 4]);
            }
            return new BitmapPalette (colors);
        }

        IBinaryStream OpenChunkStream (ArcView file, PackedEntry entry)
        {
            var input = file.CreateStream (entry.Offset, entry.Size);
            if (!entry.IsPacked)
                return input;
            var data = new byte[entry.UnpackedSize];
            using (var zstream = new ZLibStream (input, CompressionMode.Decompress))
                zstream.Read (data, 0, data.Length);
            return new BinMemoryStream (data, entry.Name);
        }

        static readonly byte[] s_xfir = { (byte)'X', (byte)'F', (byte)'I', (byte)'R' };

        long LookForXfir (ArcView file)
        {
            var exe = new ExeFile (file);
            long pos;
            if (exe.IsWin16)
            {
                pos = exe.FindString (exe.Overlay, s_xfir);
                if (pos < 0)
                    return 0;
            }
            else
            {
                pos = exe.Overlay.Offset;
                if (pos >= file.MaxOffset)
                    return 0;
                if (file.View.AsciiEqual (pos, "10JP") || file.View.AsciiEqual (pos, "59JP"))
                {
                    pos = file.View.ReadUInt32 (pos + 4);
                }
            }
            if (pos >= file.MaxOffset || !file.View.AsciiEqual (pos, "XFIR"))
                return 0;

            // TODO threat 'LPPA' archives the normal way, like archives that contain entries.
            // the problem is, DXR archives contained within 'LPPA' have their offsets relative to executable file,
            // so have to figure out way to handle it.
            if (!file.View.AsciiEqual (pos + 8, "LPPA"))
                return pos;

            var appl = new DirectorFile();
            var context = new SerializationContext();
            using (var input = file.CreateStream())
            {
                var reader = new Reader (input, ByteOrder.LittleEndian);
                input.Position = pos + 12;
                if (!appl.ReadMMap (context, reader))
                    return 0;
                foreach (var entry in appl.Directory)
                {
                    // only the first XFIR entry is matched here, but archive may contain multiple sub-archives.
                    if (entry.FourCC == "File")
                    {
                        if (file.View.AsciiEqual (entry.Offset - 8, "XFIR")
                            && !file.View.AsciiEqual (entry.Offset, "artX"))
                            return entry.Offset - 8;
                    }
                }
                return 0;
            }
        }
    }

internal class DirectorArchive : ArcFile
    {
        public DirectorFile DirectorFile { get; }

        public DirectorArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, DirectorFile dirFile)
            : base (arc, impl, dir)
        {
            DirectorFile = dirFile;
        }
    }

    internal class JsonEntry : Entry
    {
        public byte[] _data;

        public string Data
        {
            get => _data != null ? Encoding.UTF8.GetString (_data) : null;
            set => _data = value != null ? Encoding.UTF8.GetBytes (value) : null;
        }

        public JsonEntry()
        {
            Type = "script";
            Offset = 0;
            base.Size = 0;
        }

        public void SetData (string data)
        {
            Data = data;
            if (_data != null)
                base.Size = (uint)_data.Length;
        }
    }

    internal class BitmapEntry : PackedEntry
    {
        public byte Flags;
        public byte DepthType;
        public int  Top;
        public int  Left;
        public int  Bottom;
        public int  Right;
        public int  BitDepth;
        public int  Palette;
        public DirectorEntry PaletteRef;
        public DirectorEntry AlphaRef;

        public void DeserializeHeader (byte[] data)
        {
            using (var input = new MemoryStream (data, false))
            {
                var reader = new Reader (input, ByteOrder.BigEndian);

                DepthType = reader.ReadU8();
                Flags     = reader.ReadU8();
                Top       = reader.ReadI16();
                Left      = reader.ReadI16();
                Bottom    = reader.ReadI16();
                Right     = reader.ReadI16();

                if (data.Length > 0x16)
                {
                    reader.Skip (0x0C);
                    BitDepth = reader.ReadU16() & 0xFF; // ???
                    if (data.Length >= 0x1C)
                    {
                        reader.Skip (2);
                        Palette = reader.ReadI16();
                    }
                }
            }
        }
    }

    internal class SoundEntry : PackedEntry
    {
        public DirectorEntry    Header;

        public WaveFormat DeserializeHeader (byte[] header)
        {
            // pure guesswork
            return new WaveFormat {
                FormatTag             = 1,
                Channels              = (ushort)BigEndian.ToUInt32 (header, 0x4C),
                SamplesPerSecond      = BigEndian.ToUInt32 (header, 0x2C),
                AverageBytesPerSecond = BigEndian.ToUInt32 (header, 0x30),
                BlockAlign            = (ushort)BigEndian.ToUInt32 (header, 0x50),
                BitsPerSample         = (ushort)BigEndian.ToUInt32 (header, 0x44),
            };
        }
    }
}