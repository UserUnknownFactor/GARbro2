using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace GameRes.Formats.LiveMaker
{
    [Export (typeof (ScriptFormat))]
    public class ScriptLSB : ScriptFormat
    {
        public override string Tag { get { return "LSB"; } }
        public override string Description { get { return "LiveMaker binary script"; } }
        public override uint Signature { get { return 0; } }
        public override ScriptType DataType { get { return ScriptType.BinaryScript; } }
        public override bool CanWrite { get { return false; } }

        const int MinLsbVersion = 103;
        const int DefaultLsbVersion = 117;
        const int MaxLsbVersion = 117;

        public ScriptLSB ()
        {
            Extensions = new[] { "lsb", "lsc" };
        }

        public override bool IsScript (IBinaryStream file)
        {
            if (file.Length < 16)
                return false;

            var header = file.ReadBytes (9);
            file.Position = 0;

            if (Encoding.ASCII.GetString (header) == "LiveMaker")
                return true;

            if (header.Take (5).SequenceEqual (new byte[] { 0x3C, 0x3F, 0x78, 0x6D, 0x6C })) // "<?xml"
                return true;

            file.Position = 0;
            int version = file.ReadInt32 ();
            return version >= MinLsbVersion && version <= MaxLsbVersion;
        }

        public override Stream ConvertFrom (IBinaryStream file)
        {
            file.Position = 0;

            var header = file.ReadBytes (9);
            file.Position = 0;

            if (Encoding.ASCII.GetString (header).StartsWith ("LiveMaker"))
                return file.AsStream; // Already in text format

            if (header.Take (5).SequenceEqual (new byte[] { 0x3C, 0x3F, 0x78, 0x6D, 0x6C })) // "<?xml"
                return file.AsStream; // xml

            try 
            { 
                var lsb = ParseLSB (file);
                return ConvertToText (lsb);
            }
            catch (Exception ex)
            {
                Trace.WriteLine ($"[LSB] Error reading lsb: {ex.Message}");
                file.PrintHexDump ($"ParseLSB error", "LSB");
            }
            return Stream.Null;
        }

        public override Stream ConvertBack (IBinaryStream file)
        {
            var text = Encoding.GetEncoding (932).GetString (file.ReadBytes ((int)file.Length));
            var lsb = ParseLSC (text);
            return SerializeLSB (lsb);
        }

        public override ScriptData Read (string name, Stream file)
        {
            return Read (name, file, Encoding.GetEncoding (932));
        }

        public override ScriptData Read (string name, Stream file, Encoding encoding)
        {
            using (var input = BinaryStream.FromStream (file, name))
            {
                var header = input.ReadBytes (9);
                input.Position = 0;

                var script = new ScriptData ();
                script.Type = ScriptType.BinaryScript;
                script.Encoding = encoding;

                if (Encoding.ASCII.GetString (header).StartsWith ("LiveMaker"))
                {
                    var text = encoding.GetString (input.ReadBytes ((int)input.Length));
                    return ParseLSCToScript (text, encoding);
                }
                else
                {
                    var lsb = ParseLSB (input);
                    return ExtractText (lsb, encoding);
                }
            }
        }

        public override void Write (Stream file, ScriptData script)
        {
            // This would require reverse engineering the text insertion
            throw new NotImplementedException ("Direct LSB script writing not supported - use ConvertBack");
        }

        private LSBData ParseLSB (IBinaryStream input)
        {
            var data = new LSBData ();

            // Read header
            data.Version = input.ReadInt32 ();
            Debug.WriteLine($"LSB Version: {data.Version}");
    
            if (data.Version < MinLsbVersion || data.Version > MaxLsbVersion)
                throw new InvalidFormatException ($"Unsupported LSB version: {data.Version}");

            data.Flags = (byte)input.ReadByte ();
            int commandCount = input.ReadInt32 ();
            int paramStreamSize = input.ReadInt32 ();
    
            Debug.WriteLine($"Command type count: {commandCount}, Param stream size: {paramStreamSize}");

            // Read command parameters
            data.CommandParams = new List<bool[]>();
    
            for (int i = 0; i < commandCount; i++)
        {
            var paramBytes = input.ReadBytes(paramStreamSize);
            if (paramBytes.Length != paramStreamSize)
                throw new InvalidFormatException(
                    $"Unexpected end of param stream at {input.Position:X} (wanted {paramStreamSize} bytes, got {paramBytes.Length})");

            // decode 8 bits per byte, but clamp to the real number of properties (<= 0xB0)
            var bits = new bool[paramStreamSize * 8];
            for (int j = 0; j < paramStreamSize; j++)
                for (int bit = 0; bit < 8; bit++)
                    bits[j * 8 + bit] = (paramBytes[j] & (1 << bit)) != 0;

            data.CommandParams.Add(bits);
        }

        Debug.WriteLine($"After command params, position=0x{input.Position:X}");
        Debug.WriteLine($"Next 8 bytes: {BitConverter.ToString(input.ReadBytes(8))}");
        input.Position -= 8;

            // Read commands
            int numCommands = input.ReadInt32 ();
            Debug.WriteLine($"Number of commands to read: {numCommands}");
    
            for (int i = 0; i < numCommands; i++)
            {
                Debug.WriteLine($"Reading command {i} at position 0x{input.Position:X}");
                try
                {
                    var cmd = ParseCommand (input, data);
                    data.Commands.Add (cmd);
                    Debug.WriteLine($"Successfully read command {i}: {cmd.Type}");
                }
                catch (Exception ex)
                {
                    throw new InvalidFormatException($"Error parsing command {i}: {ex.Message}");
                }
            }

            return data;
        }

                private Command ParseCommand (IBinaryStream input, LSBData lsbData)
        {
            long cmdStart = input.Position;
            var cmd = new Command ();
            cmd.Type = (CommandType)input.ReadByte ();
            cmd.Indent = input.ReadUInt32 ();

            // Both flags are packed into a single byte
            byte flags = (byte)input.ReadByte ();
            cmd.Mute = (flags & 0x01) != 0;
            cmd.NotUpdate = (flags & 0x02) != 0;

            cmd.LineNo = input.ReadUInt32 ();
    
            input.ReadByte ();
    
            Debug.WriteLine($"Command {cmd.Type} at 0x{cmdStart:X}, LineNo={cmd.LineNo}, header ends at 0x{input.Position:X}");

            // Parse command-specific data based on type
            switch (cmd.Type)
        {
            case CommandType.If:
            case CommandType.Elseif:
                cmd.Calc = ParseLiveParser (input);
                break;

            case CommandType.Else:
            case CommandType.ClrHist:
            case CommandType.Terminate:
            case CommandType.DoEvent:
            case CommandType.ClrRead:
            case CommandType.IFDEF:
            case CommandType.IFNDEF:
            case CommandType.ENDIF:
                // No additional data
                break;

            case CommandType.Label:
            case CommandType.Comment:
                cmd.Name = ReadPascalString (input);
                break;

            case CommandType.Jump:
                cmd.Page = ParseLabelReference (input);
                cmd.Calc = ParseLiveParser (input);
                break;

            case CommandType.Call:
                cmd.Page = ParseLabelReference (input);
                cmd.Result = ReadPascalString (input);
                cmd.Calc = ParseLiveParser (input);
                cmd.Params = ParseLiveParserArray (input);
                break;

            case CommandType.Exit:
                cmd.Calc = ParseLiveParser (input);
                break;

            case CommandType.Break:
                cmd.Calc = ParseLiveParser (input);
                cmd.End = input.ReadUInt32 ();
                break;

            case CommandType.Continue:
                cmd.Calc = ParseLiveParser (input);
                cmd.Start = input.ReadUInt32 ();
                break;

            case CommandType.Wait:
                cmd.Calc = ParseLiveParser (input);
                cmd.Time = ParseLiveParser (input);
                if (lsbData.Version > 0x6A)
                    cmd.StopEvent = ParseLiveParser (input);
                break;

            case CommandType.BoxNew:
            case CommandType.ImgNew:
            case CommandType.MesNew:
            case CommandType.Timer:
            case CommandType.Movie:
            case CommandType.Cinema:
            case CommandType.Caption:
            case CommandType.Menu:
            case CommandType.Button:
            case CommandType.ParticleNew:
            case CommandType.FireNew:
            case CommandType.Sound:
            case CommandType.EditNew:
            case CommandType.MemoNew:
            case CommandType.MapImgNew:
            case CommandType.WaveNew:
            case CommandType.TileNew:
            case CommandType.SliderNew:
            case CommandType.ScrollbarNew:
            case CommandType.GaugeNew:
            case CommandType.CGCaption:
            case CommandType.PrevMenuNew:
                ParseComponentCommand (input, cmd, lsbData);
                break;

            case CommandType.Flip:
                cmd.Wipe = ParseLiveParser (input);
                cmd.Time = ParseLiveParser (input);
                cmd.Reverse = ParseLiveParser (input);
                cmd.Act = ParseLiveParser (input);
                cmd.Targets = ParseLiveParserArray (input);
                cmd.Delete = ParseLiveParser (input);
                // The Python shows this as a fixed array of 2 parsers, NOT prefixed
                cmd.ParamArray = new List<LiveParser>();
                cmd.ParamArray.Add(ParseLiveParser(input));
                cmd.ParamArray.Add(ParseLiveParser(input));
                if (lsbData.Version > 0x64)
                    cmd.Source = ParseLiveParser (input);
                if (lsbData.Version > 0x6A)
                    cmd.StopEvent = ParseLiveParser (input);
                if (lsbData.Version > 0x74)
                    cmd.DifferenceOnly = ParseLiveParser (input);
                break;

            case CommandType.Calc:
            case CommandType.WhileInit:
                cmd.Calc = ParseLiveParser (input);
                break;

            case CommandType.VarNew:
                cmd.Name = ReadPascalString (input);
                cmd.VarType = (ParamType)input.ReadByte ();
                cmd.InitVal = ParseLiveParser (input);
                cmd.Scope = (byte)input.ReadByte ();
                break;

            case CommandType.VarDel:
                cmd.Name = ReadPascalString (input);
                break;

            case CommandType.GetProp:
                cmd.ObjName = ParseLiveParser (input);
                cmd.ObjProp = ParseLiveParser (input);
                cmd.VarName = ReadPascalString (input);
                break;

            case CommandType.SetProp:
                cmd.ObjName = ParseLiveParser (input);
                cmd.ObjProp = ParseLiveParser (input);
                cmd.Value = ParseLiveParser (input);
                break;

            case CommandType.ObjDel:
                cmd.ObjName = ParseLiveParser (input);
                break;

            case CommandType.TextIns:
                ParseTextInsCommand (input, cmd, lsbData.Version);
                break;

            case CommandType.MovieStop:
                cmd.Target = ParseLiveParser (input);
                cmd.Time = ParseLiveParser (input);
                cmd.WaitFlag = ParseLiveParser (input);
                if (lsbData.Version > 0x6A)
                    cmd.StopEvent = ParseLiveParser (input);
                break;

            case CommandType.MenuClose:
            case CommandType.TextClr:
                cmd.Target = ParseLiveParser (input);
                break;

            case CommandType.MediaPlay:
                cmd.Target = ParseLiveParser (input);
                break;

            case CommandType.CallHist:
                cmd.Target = ParseLiveParser (input);
                cmd.Index = ParseLiveParser (input);
                cmd.Count = ParseLiveParser (input);
                cmd.CutBreak = ParseLiveParser (input);
                if (lsbData.Version > 0x6E)
                    cmd.FormatName = ParseLiveParser (input);
                break;

            case CommandType.While:
                cmd.Calc = ParseLiveParser (input);
                cmd.End = input.ReadUInt32 ();
                break;

            case CommandType.WhileLoop:
                cmd.Calc = ParseLiveParser (input);
                cmd.Start = input.ReadUInt32 ();
                break;

            case CommandType.GameSave:
                cmd.No = ParseLiveParser (input);
                cmd.Page = new LabelReference { Page = ReadPascalString (input) };
                if (lsbData.Version > 0x68)
                    cmd.Page.Label = input.ReadUInt32 ();
                cmd.Caption = ParseLiveParser (input);
                break;

            case CommandType.GameLoad:
                cmd.No = ParseLiveParser (input);
                break;

            case CommandType.PCReset:
            case CommandType.Reset:
                cmd.Page = ParseLabelReference (input);
                cmd.AllClear = (byte)input.ReadByte ();
                break;

            case CommandType.PropMotion:
                cmd.Name = ParseLiveParser (input);
                cmd.ObjName = ParseLiveParser (input);
                cmd.ObjProp = ParseLiveParser (input);
                cmd.Value = ParseLiveParser (input);
                cmd.Time = ParseLiveParser (input);
                cmd.MoveType = ParseLiveParser (input);
                if (lsbData.Version > 0x6B)
                    cmd.Paused = ParseLiveParser (input);
                break;

            case CommandType.FormatHist:
                cmd.Name = ParseLiveParser (input);
                if (lsbData.Version > 0x6E)
                    cmd.Target = ParseLiveParser (input);
                break;

            case CommandType.SaveCabinet:
            case CommandType.LoadCabinet:
                ParseComponentCommand (input, cmd, lsbData);
                cmd.Act = ParseLiveParser (input);
                cmd.Targets = ParseLiveParserArray (input);
                break;

            default:
                throw new InvalidFormatException($"Unhandled command type: {cmd.Type} at position 0x{cmdStart:X}");
            }

            return cmd;
        }

        private void ParseComponentCommand(IBinaryStream input, Command cmd, LSBData lsbData)
        {
            var commandParams = lsbData.CommandParams[(int)cmd.Type];
            cmd.Components = new List<LiveParser>();

            int componentCount = 0;
            for (int i = 0; i < commandParams.Length; i++)
            {
                if (commandParams[i])
                    componentCount++;
            }

            for (int i = 0; i < componentCount; i++)
            {
                var component = ParseLiveParser(input);
                cmd.Components.Add(component);
            }
        }

        private void ParseTextInsCommand (IBinaryStream input, Command cmd, int version)
        {
            // Read TpWord structure
            int textLength = input.ReadInt32 ();
            long textEnd   = input.Position + textLength;

            cmd.TpWord   = ParseTpWord (input, textEnd);
            cmd.Target   = ParseLiveParser (input);
            cmd.Hist     = ParseLiveParser (input);
            cmd.WaitFlag = ParseLiveParser (input);

            if (version > 0x6A)
                cmd.StopEvent = ParseLiveParser (input);
        }

        private TpWord ParseTpWord (IBinaryStream input, long endPosition)
        {
            var tpword = new TpWord ();

            var signature = input.ReadBytes (6);
            if (!signature.AsciiEqual ("TpWord"))
            {
                input.Position = endPosition;
                return tpword;
            }

            // Read version (3 bytes)
            var versionBytes = input.ReadBytes (3);
            tpword.Version = int.Parse (Encoding.ASCII.GetString (versionBytes));

            // Read decorators
            int decoratorCount = input.ReadInt32 ();
            for (int i = 0; i < decoratorCount; i++)
            {
                var decorator = new TDecorate ();
                decorator.Count = input.ReadUInt32 ();
                decorator.Unk2 = input.ReadUInt32 ();
                decorator.Unk3 = input.ReadUInt32 ();
                decorator.Unk4 = input.ReadUInt32 ();
                decorator.Unk5 = (byte)input.ReadByte ();
                decorator.Unk6 = (byte)input.ReadByte ();

                if (tpword.Version < 100)
                    decorator.Unk7 = (byte)input.ReadByte ();
                else
                    decorator.Unk7 = input.ReadUInt32 ();

                decorator.Unk8 = ReadPascalString (input);
                decorator.Ruby = ReadPascalString (input);

                if (tpword.Version >= 100)
                {
                    decorator.Unk10 = input.ReadUInt32 ();
                    decorator.Unk11 = input.ReadUInt32 ();
                }

                tpword.Decorators.Add (decorator);
            }

            // Read conditions
            if (tpword.Version >= 104)
            {
                int conditionCount = input.ReadInt32 ();
                for (int i = 0; i < conditionCount; i++)
                {
                    var condition = new TWdCondition ();
                    condition.Count = input.ReadUInt32 ();
                    condition.Target = ReadPascalString (input);
                    tpword.Conditions.Add (condition);
                }
            }

            // Read links
            if (tpword.Version >= 105)
            {
                int linkCount = input.ReadInt32 ();
                for (int i = 0; i < linkCount; i++)
                {
                    var link = new TWdLink ();
                    link.Count = input.ReadUInt32 ();
                    link.Event = ReadPascalString (input);
                    link.Unk3 = ReadPascalString (input);
                    tpword.Links.Add (link);
                }
            }

            // Read body
            int bodyCount = input.ReadInt32 ();
            for (int i = 0; i < bodyCount; i++)
            {
                byte wdTypeByte = (byte)input.ReadByte ();
                if (wdTypeByte == 0 || wdTypeByte > 10 || wdTypeByte == 8)
                {
                    // Unknown type - treat as raw byte in body
                    tpword.Body.Add (new TWdGlyph { Type = (TWdType)wdTypeByte });
                    continue;
                }
                var wd = ParseTWdGlyph (input, (TWdType)wdTypeByte, tpword.Version);
                tpword.Body.Add (wd);
            }

            input.Position = endPosition;
            return tpword;
        }

        private TWdGlyph ParseTWdGlyph (IBinaryStream input, TWdType type, int version)
        {
            TWdGlyph glyph = null;

            // Read common condition field for version >= 104
            int? condition = null;
            if (version >= 104)
                condition = input.ReadInt32 ();

            switch (type)
            {
                case TWdType.TWdChar:
                    {
                        var ch = new TWdChar ();
                        ch.Condition = condition;

                        // Read link fields
                        if (version < 105)
                            ch.LinkName = ReadPascalString (input);
                        else
                            ch.Link = input.ReadInt32 ();

                        ch.TextSpeed = input.ReadUInt32 ();

                        // Read character
                        byte[] chBytes = input.ReadBytes (2);
                        var character = Encoding.GetEncoding (932).GetString (chBytes);
                        if (character.StartsWith ("\0"))
                            character = character.Substring (1);
                        ch.Ch = character;

                        ch.Decorator = input.ReadInt32 ();
                        glyph = ch;
                        break;
                    }

                case TWdType.TWdOpeDiv:
                    {
                        var div = new TWdOpeDiv ();
                        div.Condition = condition;
                        div.Align = (byte)input.ReadByte ();

                        if (version >= 105)
                        {
                            div.PadLeft = input.ReadInt32 ();
                            div.PadRight = input.ReadInt32 ();
                            div.NoHeight = (byte)input.ReadByte ();
                        }

                        glyph = div;
                        break;
                    }

                case TWdType.TWdOpeReturn:
                    {
                        var ret = new TWdOpeReturn ();
                        ret.Condition = condition;
                        ret.BreakType = (BreakType)input.ReadByte ();
                        glyph = ret;
                        break;
                    }

                case TWdType.TWdOpeIndent:
                    {
                        glyph = new TWdOpeIndent { Condition = condition };
                        break;
                    }

                case TWdType.TWdOpeUndent:
                    {
                        glyph = new TWdOpeUndent { Condition = condition };
                        break;
                    }

                case TWdType.TWdOpeEvent:
                    {
                        var evt = new TWdOpeEvent ();
                        evt.Condition = condition;
                        evt.Event = ReadPascalString (input);
                        glyph = evt;
                        break;
                    }

                case TWdType.TWdOpeVar:
                case TWdType.TWdOpeHistChar:
                    {
                        var var = type == TWdType.TWdOpeVar ? new TWdOpeVar () : new TWdOpeHistChar ();
                        var.Condition = condition;
                        var.Decorator = input.ReadInt32 ();

                        if (version > 100)
                            var.Unk3 = input.ReadUInt32 ();

                        if (version > 100 && version < 105)
                            var.LinkName = ReadPascalString (input);
                        else if (version >= 105)
                            var.Link = input.ReadInt32 ();

                        if (version < 102)
                            var.VarNameParams = ParseLiveParser (input);
                        else
                            var.VarName = ReadPascalString (input);

                        glyph = var;
                        break;
                    }

                case TWdType.TWdImg:
                    {
                        var img = new TWdImg ();
                        img.Condition = condition;

                        // Read link fields
                        if (version < 105)
                            img.LinkName = ReadPascalString (input);
                        else
                            img.Link = input.ReadInt32 ();

                        img.TextSpeed = input.ReadUInt32 ();
                        img.Src = ReadPascalString (input);
                        img.Align = (byte)input.ReadByte ();

                        if (version >= 103)
                            img.HoverSrc = ReadPascalString (input);

                        if (version >= 105)
                        {
                            img.MgnLeft = input.ReadInt32 ();
                            img.MgnRight = input.ReadInt32 ();
                            img.MgnTop = input.ReadInt32 ();
                            img.MgnBottom = input.ReadInt32 ();
                            img.DownSrc = ReadPascalString (input);
                        }

                        glyph = img;
                        break;
                    }
            }

            return glyph ?? new TWdGlyph { Type = type, Condition = condition };
        }

        private LabelReference ParseLabelReference (IBinaryStream input)
        {
            var reference = new LabelReference ();
            reference.Page = ReadPascalString (input);
            reference.Label = input.ReadUInt32 ();
            return reference;
        }

        private LiveParser ParseLiveParser (IBinaryStream input)
        {
            var parser = new LiveParser ();
            int entryCount = input.ReadInt32 ();

            for (int i = 0; i < entryCount; i++)
            {
                var entry = new OpeData ();
                entry.Type = (OpeDataType)input.ReadByte ();
                entry.Name = ReadPascalString (input);
                int operandCount = input.ReadInt32 ();

                if (entry.Type == OpeDataType.Func)
                    entry.Func = (OpeFuncType)input.ReadByte ();

                for (int j = 0; j < operandCount; j++)
                {
                    var param = new Param ();
                    param.Type = (ParamType)input.ReadByte ();

                    switch (param.Type)
                    {
                        case ParamType.Int:
                            param.Value = input.ReadInt32 ();
                            break;
                        case ParamType.Float:
                            var floatBytes = input.ReadBytes (10);
                            // For now, just try to interpret as double (won't be exact)
                            var paddedBytes = new byte[16];
                            Array.Copy (floatBytes, 0, paddedBytes, 0, 8); // Use first 8 bytes
                            param.Value = BitConverter.ToDouble (paddedBytes, 0);
                            break;
                        case ParamType.Flag:
                            param.Value = (byte)input.ReadByte () != 0;
                            break;
                        case ParamType.Str:
                        case ParamType.Var:
                            param.Value = ReadPascalString (input);
                            break;
                         default:
                            param.Value = ReadPascalString(input);
                            break;
                    }

                    entry.Operands.Add (param);
                }

                parser.Entries.Add (entry);
            }

            return parser;
        }

        private List<LiveParser> ParseLiveParserArray (IBinaryStream input, int count = -1, bool prefixed = true)
        {
            var array = new List<LiveParser> ();

            if (count == -1)
            {
                if (prefixed)
                    count = input.ReadInt32 ();
                else
                    return array;
            }

            for (int i = 0; i < count; i++)
            {
                array.Add (ParseLiveParser (input));
            }

            return array;
        }

        private LSBData ParseLSC (string text)
        {
            var lsb = new LSBData ();
            var lines = text.Split (new[] { "\r\n", "\n" }, StringSplitOptions.None);
            int lineIndex = 0;

            if (lineIndex >= lines.Length || !lines[lineIndex].StartsWith ("LiveMaker"))
                throw new InvalidFormatException ("Invalid LSC format");

            // Parse version
            string versionStr = lines[lineIndex].Substring (9);
            if (versionStr.StartsWith ("B"))
                versionStr = versionStr.Substring (1);
            lsb.Version = int.Parse (versionStr);
            lineIndex++;

            // Parse param_type
            if (lineIndex < lines.Length)
            {
                // param_type is always 1
                lineIndex++;
            }

            // Skip empty line for version >= 104
            if (lsb.Version >= 104 && lineIndex < lines.Length && string.IsNullOrEmpty (lines[lineIndex]))
                lineIndex++;

            // Parse command count
            if (lineIndex >= lines.Length)
                return lsb;

            int commandCount = int.Parse (lines[lineIndex++]);

            // Parse command parameters
            for (int i = 0; i < commandCount && lineIndex < lines.Length; i++)
            {
                var paramLine = lines[lineIndex++];
                var bools = new bool[256];

                if (!string.IsNullOrWhiteSpace (paramLine))
                {
                    var indices = paramLine.Split ('\t')
                        .Where (s => !string.IsNullOrEmpty (s))
                        .Select (s => int.Parse (s.Trim ()));
                    foreach (var idx in indices)
                    {
                        if (idx < bools.Length)
                            bools[idx] = true;
                    }
                }

                lsb.CommandParams.Add (bools);
            }

            // Skip to command section
            while (lineIndex < lines.Length)
            {
                var line = lines[lineIndex];
                if (line.StartsWith (";") || string.IsNullOrWhiteSpace (line))
                {
                    lineIndex++;
                    continue;
                }

                // Start parsing commands
                break;
            }

            // Parse commands
            while (lineIndex < lines.Length)
            {
                var line = lines[lineIndex++];
                if (string.IsNullOrWhiteSpace (line) || line.StartsWith (";"))
                    continue;

                var cmd = ParseLSCCommand (line, lines, ref lineIndex, lsb);
                if (cmd != null)
                    lsb.Commands.Add (cmd);
            }

            return lsb;
        }

        private Command ParseLSCCommand (string line, string[] allLines, ref int lineIndex, LSBData lsb)
        {
            var parts = line.Split ('\t');
            if (parts.Length < 5)
                return null;

            var cmd = new Command ();

            // Parse basic command fields
            if (!Enum.TryParse<CommandType> (parts[0], out var cmdType))
                return null;

            cmd.Type = cmdType;
            cmd.Indent = uint.Parse (parts[1]);
            cmd.Mute = parts[2] == "1";
            cmd.NotUpdate = parts[3] == "1";

            if (lsb.Version >= 106)
            {
                cmd.Color = uint.Parse (parts[4]);
                cmd.LineNo = parts.Length > 5 ? uint.Parse (parts[5]) : 0;
            }
            else
            {
                cmd.LineNo = uint.Parse (parts[4]);
            }

            // Parse command-specific data from remaining tabs
            int paramIndex = lsb.Version >= 106 ? 6 : 5;
            var cmdParams = parts.Skip (paramIndex).ToArray();

            ParseLSCCommandParams (cmd, cmdParams, lsb);

            return cmd;
        }

        private void ParseLSCCommandParams (Command cmd, string[] params_, LSBData lsb)
        {
            switch (cmd.Type)
            {
                case CommandType.Label:
                case CommandType.Comment:
                case CommandType.VarDel:
                    if (params_.Length > 0)
                        cmd.Name = params_[0];
                    break;

                case CommandType.VarNew:
                    if (params_.Length >= 4)
                    {
                        cmd.Name = params_[0];
                        if (Enum.TryParse<ParamType> (params_[1], out var varType))
                            cmd.VarType = varType;
                        cmd.InitVal = ParseLiveParserFromString (params_[2]);
                        cmd.Scope = byte.Parse (params_[3]);
                    }
                    break;

                case CommandType.If:
                case CommandType.Elseif:
                case CommandType.Calc:
                case CommandType.WhileInit:
                case CommandType.Exit:
                    if (params_.Length > 0)
                        cmd.Calc = ParseLiveParserFromString (params_[0]);
                    break;

                case CommandType.Jump:
                    if (params_.Length >= 2)
                    {
                        cmd.Page = ParseLabelReferenceFromString (params_[0]);
                        cmd.Calc = ParseLiveParserFromString (params_[1]);
                    }
                    break;

                case CommandType.Call:
                    if (params_.Length >= 4)
                    {
                        cmd.Page = ParseLabelReferenceFromString (params_[0]);
                        cmd.Result = params_[1];
                        cmd.Calc = ParseLiveParserFromString (params_[2]);
                        // params_[3] would be the params array
                    }
                    break;

                case CommandType.Break:
                    if (params_.Length >= 2)
                    {
                        cmd.Calc = ParseLiveParserFromString (params_[0]);
                        cmd.End = uint.Parse (params_[1]);
                    }
                    break;

                case CommandType.Continue:
                    if (params_.Length >= 2)
                    {
                        cmd.Calc = ParseLiveParserFromString (params_[0]);
                        cmd.Start = uint.Parse (params_[1]);
                    }
                    break;

                case CommandType.Wait:
                    if (params_.Length >= 2)
                    {
                        cmd.Calc = ParseLiveParserFromString (params_[0]);
                        cmd.Time = ParseLiveParserFromString (params_[1]);
                        if (params_.Length > 2 && lsb.Version > 0x6A)
                            cmd.StopEvent = ParseLiveParserFromString (params_[2]);
                    }
                    break;

                case CommandType.While:
                    if (params_.Length >= 2)
                    {
                        cmd.Calc = ParseLiveParserFromString (params_[0]);
                        cmd.End = uint.Parse (params_[1]);
                    }
                    break;

                case CommandType.WhileLoop:
                    if (params_.Length >= 2)
                    {
                        cmd.Calc = ParseLiveParserFromString (params_[0]);
                        cmd.Start = uint.Parse (params_[1]);
                    }
                    break;

                case CommandType.GameSave:
                    if (params_.Length >= 3)
                    {
                        cmd.No = ParseLiveParserFromString (params_[0]);
                        cmd.Page = new LabelReference { Page = params_[1] };
                        if (lsb.Version > 0x68 && params_.Length > 3)
                            cmd.Page.Label = uint.Parse (params_[2]);
                        cmd.Caption = ParseLiveParserFromString (params_[params_.Length - 1]);
                    }
                    break;

                case CommandType.GameLoad:
                    if (params_.Length > 0)
                        cmd.No = ParseLiveParserFromString (params_[0]);
                    break;

                case CommandType.PCReset:
                case CommandType.Reset:
                    if (params_.Length >= 2)
                    {
                        cmd.Page = ParseLabelReferenceFromString (params_[0]);
                        cmd.AllClear = byte.Parse (params_[1]);
                    }
                    break;

                case CommandType.TextIns:
                    // Text commands have complex TpWord data that needs special handling
                    cmd.TpWord = new TpWord ();
                    if (params_.Length >= 3)
                    {
                        cmd.Target = ParseLiveParserFromString (params_[0]);
                        cmd.Hist = ParseLiveParserFromString (params_[1]);
                        cmd.WaitFlag = ParseLiveParserFromString (params_[2]);
                        if (params_.Length > 3 && lsb.Version > 0x6A)
                            cmd.StopEvent = ParseLiveParserFromString (params_[3]);
                    }
                    break;

                case CommandType.GetProp:
                    if (params_.Length >= 3)
                    {
                        cmd.ObjName = ParseLiveParserFromString (params_[0]);
                        cmd.ObjProp = ParseLiveParserFromString (params_[1]);
                        cmd.VarName = params_[2];
                    }
                    break;

                case CommandType.SetProp:
                    if (params_.Length >= 3)
                    {
                        cmd.ObjName = ParseLiveParserFromString (params_[0]);
                        cmd.ObjProp = ParseLiveParserFromString (params_[1]);
                        cmd.Value = ParseLiveParserFromString (params_[2]);
                    }
                    break;

                case CommandType.ObjDel:
                case CommandType.MenuClose:
                case CommandType.TextClr:
                case CommandType.MediaPlay:
                    if (params_.Length > 0)
                        cmd.ObjName = ParseLiveParserFromString (params_[0]);
                    break;

                case CommandType.MovieStop:
                    if (params_.Length >= 3)
                    {
                        cmd.Target = ParseLiveParserFromString (params_[0]);
                        cmd.Time = ParseLiveParserFromString (params_[1]);
                        cmd.WaitFlag = ParseLiveParserFromString (params_[2]);
                        if (params_.Length > 3 && lsb.Version > 0x6A)
                            cmd.StopEvent = ParseLiveParserFromString (params_[3]);
                    }
                    break;

                case CommandType.CallHist:
                    if (params_.Length >= 4)
                    {
                        cmd.Target = ParseLiveParserFromString (params_[0]);
                        cmd.Index = ParseLiveParserFromString (params_[1]);
                        cmd.Count = ParseLiveParserFromString (params_[2]);
                        cmd.CutBreak = ParseLiveParserFromString (params_[3]);
                        if (params_.Length > 4 && lsb.Version > 0x6E)
                            cmd.FormatName = ParseLiveParserFromString (params_[4]);
                    }
                    break;

                case CommandType.Flip:
                    if (params_.Length >= 6)
                    {
                        cmd.Wipe = ParseLiveParserFromString (params_[0]);
                        cmd.Time = ParseLiveParserFromString (params_[1]);
                        cmd.Reverse = ParseLiveParserFromString (params_[2]);
                        cmd.Act = ParseLiveParserFromString (params_[3]);
                        // params_[4] would be targets array
                        cmd.Delete = ParseLiveParserFromString (params_[5]);
                        // Additional version-specific params
                    }
                    break;

                case CommandType.PropMotion:
                    if (params_.Length >= 6)
                    {
                        cmd.Name = ParseLiveParserFromString (params_[0]);  // Store as LiveParser
                        cmd.ObjName = ParseLiveParserFromString (params_[1]);
                        cmd.ObjProp = ParseLiveParserFromString (params_[2]);
                        cmd.Value = ParseLiveParserFromString (params_[3]);
                        cmd.Time = ParseLiveParserFromString (params_[4]);
                        cmd.MoveType = ParseLiveParserFromString (params_[5]);
                        if (params_.Length > 6 && lsb.Version > 0x6B)
                            cmd.Paused = ParseLiveParserFromString (params_[6]);
                    }
                    break;

                case CommandType.FormatHist:
                    if (params_.Length >= 1)
                    {
                        cmd.Name = ParseLiveParserFromString (params_[0]);  // Store as LiveParser
                        if (params_.Length > 1 && lsb.Version > 0x6E)
                            cmd.Target = ParseLiveParserFromString (params_[1]);
                    }
                    break;

                // Component commands need special handling
                case CommandType.BoxNew:
                case CommandType.ImgNew:
                case CommandType.MesNew:
                case CommandType.Timer:
                case CommandType.Movie:
                case CommandType.Cinema:
                case CommandType.Caption:
                case CommandType.Menu:
                case CommandType.Button:
                case CommandType.ParticleNew:
                case CommandType.FireNew:
                case CommandType.Sound:
                case CommandType.EditNew:
                case CommandType.MemoNew:
                case CommandType.MapImgNew:
                case CommandType.WaveNew:
                case CommandType.TileNew:
                case CommandType.SliderNew:
                case CommandType.ScrollbarNew:
                case CommandType.GaugeNew:
                case CommandType.CGCaption:
                case CommandType.PrevMenuNew:
                    ParseComponentCommandParams (cmd, params_, lsb);
                    break;

                case CommandType.SaveCabinet:
                case CommandType.LoadCabinet:
                    ParseComponentCommandParams (cmd, params_, lsb);
                    if (params_.Length > cmd.Components.Count + 1)
                    {
                        cmd.Act = ParseLiveParserFromString (params_[cmd.Components.Count]);
                        // params_[cmd.Components.Count + 1] would be targets array
                    }
                    break;
            }
        }

        private LiveParser ParseLiveParserFromString (string str)
        {
            // This is a simplified parser - actual implementation would parse the expression syntax
            var parser = new LiveParser ();
            if (!string.IsNullOrEmpty (str))
            {
                var entry = new OpeData
                {
                    Type = OpeDataType.To,
                    Name = "____arg"
                };
                entry.Operands.Add (new Param
                {
                    Type = ParamType.Str,
                    Value = str
                });
                parser.Entries.Add (entry);
            }
            return parser;
        }

        private LabelReference ParseLabelReferenceFromString (string str)
        {
            var parts = str.Split (':');
            var reference = new LabelReference ();
            if (parts.Length >= 1)
                reference.Page = parts[0];
            if (parts.Length >= 2 && uint.TryParse (parts[1], out var label))
                reference.Label = label;
            return reference;
        }

        private void ParseComponentCommandParams (Command cmd, string[] params_, LSBData lsb)
        {
            cmd.Components = new List<LiveParser> ();
            var commandParams = lsb.CommandParams[(int)cmd.Type];

            int paramIndex = 0;
            for (int i = 1; i < commandParams.Length && paramIndex < params_.Length; i++)
            {
                if (commandParams[i])
                {
                    cmd.Components.Add (ParseLiveParserFromString (params_[paramIndex]));
                    paramIndex++;
                }
            }
        }

        private Stream SerializeLSB (LSBData lsb)
        {
            var output = new MemoryStream ();
            using (var writer = new BinaryWriter (output, Encoding.GetEncoding (932), true))
            {
                // Write header
                writer.Write (lsb.Version);
                writer.Write (lsb.Flags);
                writer.Write (lsb.CommandParams.Count);

                // Calculate param stream size
                int maxParams = lsb.CommandParams.Max (p => p.Length);
                int paramStreamSize = (maxParams + 7) / 8;
                writer.Write (paramStreamSize);

                // Write command parameters
                foreach (var params_ in lsb.CommandParams)
                {
                    var bytes = new byte[paramStreamSize];
                    for (int i = 0; i < params_.Length && i < paramStreamSize * 8; i++)
                    {
                        if (params_[i])
                            bytes[i / 8] |= (byte)(1 << (i % 8));
                    }
                    writer.Write (bytes);
                }

                // Write commands
                using (var cmdStream = new MemoryStream ())
                using (var cmdWriter = new BinaryWriter (cmdStream, Encoding.GetEncoding (932)))
                {
                    foreach (var cmd in lsb.Commands)
                    {
                        SerializeCommand (cmdWriter, cmd, lsb);
                    }

                    writer.Write ((int)cmdStream.Length);
                    writer.Write (cmdStream.ToArray());
                }
            }

            output.Position = 0;
            return output;
        }

        private void SerializeCommand (BinaryWriter writer, Command cmd, LSBData lsb)
        {
            writer.Write ((byte)cmd.Type);
            writer.Write (cmd.Indent);

            // Pack both flags into a single byte
            byte flags = 0;
            if (cmd.Mute) flags |= 0x01;
            if (cmd.NotUpdate) flags |= 0x02;
            writer.Write (flags);

            writer.Write (cmd.LineNo);

            // Serialize command-specific data
            switch (cmd.Type)
            {
                case CommandType.If:
                case CommandType.Elseif:
                    SerializeLiveParser (writer, cmd.Calc);
                    break;

                case CommandType.Else:
                case CommandType.ClrHist:
                case CommandType.Terminate:
                case CommandType.DoEvent:
                case CommandType.ClrRead:
                case CommandType.IFDEF:
                case CommandType.IFNDEF:
                case CommandType.ENDIF:
                    // No additional data
                    break;

                case CommandType.Label:
                case CommandType.Comment:
                    WritePascalString (writer, cmd.Name as string ?? "");
                    break;

                case CommandType.Jump:
                    SerializeLabelReference (writer, cmd.Page);
                    SerializeLiveParser (writer, cmd.Calc);
                    break;

                case CommandType.Call:
                    SerializeLabelReference (writer, cmd.Page);
                    WritePascalString (writer, cmd.Result ?? "");
                    SerializeLiveParser (writer, cmd.Calc);
                    SerializeLiveParserArray (writer, cmd.Params);
                    break;

                case CommandType.Exit:
                    SerializeLiveParser (writer, cmd.Calc);
                    break;

                case CommandType.Break:
                    SerializeLiveParser (writer, cmd.Calc);
                    writer.Write (cmd.End);
                    break;

                case CommandType.Continue:
                    SerializeLiveParser (writer, cmd.Calc);
                    writer.Write (cmd.Start);
                    break;

                case CommandType.Wait:
                    SerializeLiveParser (writer, cmd.Calc);
                    SerializeLiveParser (writer, cmd.Time);
                    if (lsb.Version > 0x6A)
                        SerializeLiveParser (writer, cmd.StopEvent);
                    break;

                case CommandType.BoxNew:
                case CommandType.ImgNew:
                case CommandType.MesNew:
                case CommandType.Timer:
                case CommandType.Movie:
                case CommandType.Cinema:
                case CommandType.Caption:
                case CommandType.Menu:
                case CommandType.Button:
                case CommandType.ParticleNew:
                case CommandType.FireNew:
                case CommandType.Sound:
                case CommandType.EditNew:
                case CommandType.MemoNew:
                case CommandType.MapImgNew:
                case CommandType.WaveNew:
                case CommandType.TileNew:
                case CommandType.SliderNew:
                case CommandType.ScrollbarNew:
                case CommandType.GaugeNew:
                case CommandType.CGCaption:
                case CommandType.PrevMenuNew:
                    SerializeComponentCommand (writer, cmd, lsb);
                    break;

                case CommandType.Flip:
                    SerializeLiveParser (writer, cmd.Wipe);
                    SerializeLiveParser (writer, cmd.Time);
                    SerializeLiveParser (writer, cmd.Reverse);
                    SerializeLiveParser (writer, cmd.Act);
                    SerializeLiveParserArray (writer, cmd.Targets);
                    SerializeLiveParser (writer, cmd.Delete);
                    SerializeLiveParserArray (writer, cmd.ParamArray, false);
                    if (lsb.Version > 0x64)
                        SerializeLiveParser (writer, cmd.Source);
                    if (lsb.Version > 0x6A)
                        SerializeLiveParser (writer, cmd.StopEvent);
                    if (lsb.Version > 0x74)
                        SerializeLiveParser (writer, cmd.DifferenceOnly);
                    break;

                case CommandType.Calc:
                case CommandType.WhileInit:
                    SerializeLiveParser (writer, cmd.Calc);
                    break;

                case CommandType.VarNew:
                    WritePascalString (writer, cmd.Name as string ?? "");
                    writer.Write ((byte)cmd.VarType);
                    SerializeLiveParser (writer, cmd.InitVal);
                    writer.Write (cmd.Scope);
                    break;

                case CommandType.VarDel:
                    WritePascalString (writer, cmd.Name as string ?? "");
                    break;

                case CommandType.GetProp:
                    SerializeLiveParser (writer, cmd.ObjName);
                    SerializeLiveParser (writer, cmd.ObjProp);
                    WritePascalString (writer, cmd.VarName ?? "");
                    break;

                case CommandType.SetProp:
                    SerializeLiveParser (writer, cmd.ObjName);
                    SerializeLiveParser (writer, cmd.ObjProp);
                    SerializeLiveParser (writer, cmd.Value);
                    break;

                case CommandType.ObjDel:
                case CommandType.MenuClose:
                case CommandType.TextClr:
                case CommandType.MediaPlay:
                    SerializeLiveParser (writer, cmd.ObjName ?? cmd.Target);
                    break;

                case CommandType.TextIns:
                    SerializeTpWord (writer, cmd.TpWord);
                    SerializeLiveParser (writer, cmd.Target);
                    SerializeLiveParser (writer, cmd.Hist);
                    SerializeLiveParser (writer, cmd.WaitFlag);
                    if (lsb.Version > 0x6A)
                        SerializeLiveParser (writer, cmd.StopEvent);
                    break;

                case CommandType.MovieStop:
                    SerializeLiveParser (writer, cmd.Target);
                    SerializeLiveParser (writer, cmd.Time);
                    SerializeLiveParser (writer, cmd.WaitFlag);
                    if (lsb.Version > 0x6A)
                        SerializeLiveParser (writer, cmd.StopEvent);
                    break;

                case CommandType.CallHist:
                    SerializeLiveParser (writer, cmd.Target);
                    SerializeLiveParser (writer, cmd.Index);
                    SerializeLiveParser (writer, cmd.Count);
                    SerializeLiveParser (writer, cmd.CutBreak);
                    if (lsb.Version > 0x6E)
                        SerializeLiveParser (writer, cmd.FormatName);
                    break;

                case CommandType.While:
                    SerializeLiveParser (writer, cmd.Calc);
                    writer.Write (cmd.End);
                    break;

                case CommandType.WhileLoop:
                    SerializeLiveParser (writer, cmd.Calc);
                    writer.Write (cmd.Start);
                    break;

                case CommandType.GameSave:
                    SerializeLiveParser (writer, cmd.No);
                    WritePascalString (writer, cmd.Page?.Page ?? "");
                    if (lsb.Version > 0x68)
                        writer.Write (cmd.Page?.Label ?? 0);
                    SerializeLiveParser (writer, cmd.Caption);
                    break;

                case CommandType.GameLoad:
                    SerializeLiveParser (writer, cmd.No);
                    break;

                case CommandType.PCReset:
                case CommandType.Reset:
                    SerializeLabelReference (writer, cmd.Page);
                    writer.Write (cmd.AllClear);
                    break;

                case CommandType.PropMotion:
                    SerializeLiveParser (writer, cmd.Name as LiveParser ?? new LiveParser ());
                    SerializeLiveParser (writer, cmd.ObjName);
                    SerializeLiveParser (writer, cmd.ObjProp);
                    SerializeLiveParser (writer, cmd.Value);
                    SerializeLiveParser (writer, cmd.Time);
                    SerializeLiveParser (writer, cmd.MoveType);
                    if (lsb.Version > 0x6B)
                        SerializeLiveParser (writer, cmd.Paused);
                    break;

                case CommandType.FormatHist:
                    SerializeLiveParser (writer, cmd.Name as LiveParser ?? new LiveParser ());
                    if (lsb.Version > 0x6E)
                        SerializeLiveParser (writer, cmd.Target);
                    break;

                case CommandType.SaveCabinet:
                case CommandType.LoadCabinet:
                    SerializeComponentCommand (writer, cmd, lsb);
                    SerializeLiveParser (writer, cmd.Act);
                    SerializeLiveParserArray (writer, cmd.Targets);
                    break;
            }
        }

        private void SerializeComponentCommand (BinaryWriter writer, Command cmd, LSBData lsb)
        {
            if (cmd.Components == null)
                cmd.Components = new List<LiveParser> ();

            var commandParams = lsb.CommandParams[(int)cmd.Type];
            int componentIndex = 0;

            for (int i = 1; i < commandParams.Length && i < 256; i++) // PR_NONE is 0, so start from 1
            {
                if (commandParams[i])
                {
                    if (componentIndex < cmd.Components.Count)
                        SerializeLiveParser (writer, cmd.Components[componentIndex]);
                    else
                        SerializeLiveParser (writer, new LiveParser ());
                    componentIndex++;
                }
            }
        }

        private void SerializeLabelReference (BinaryWriter writer, LabelReference reference)
        {
            WritePascalString (writer, reference?.Page ?? "");
            writer.Write (reference?.Label ?? 0);
        }

        private void SerializeLiveParser (BinaryWriter writer, LiveParser parser)
        {
            if (parser == null)
                parser = new LiveParser ();

            writer.Write (parser.Entries.Count);
            foreach (var entry in parser.Entries)
            {
                writer.Write ((byte)entry.Type);
                WritePascalString (writer, entry.Name);
                writer.Write (entry.Operands.Count);

                if (entry.Type == OpeDataType.Func)
                    writer.Write ((byte)entry.Func);

                foreach (var operand in entry.Operands)
                {
                    writer.Write ((byte)operand.Type);

                    switch (operand.Type)
                    {
                        case ParamType.Int:
                            writer.Write ((int)operand.Value);
                            break;
                        case ParamType.Float:
                            var doubleBytes = BitConverter.GetBytes ((double)operand.Value);
                            // Take last 10 bytes for long double format
                            writer.Write (doubleBytes, 0, Math.Min (10, doubleBytes.Length));
                            if (doubleBytes.Length < 10)
                                writer.Write (new byte[10 - doubleBytes.Length]);
                            break;
                        case ParamType.Flag:
                            writer.Write ((byte)((bool)operand.Value ? 1 : 0));
                            break;
                        case ParamType.Str:
                        case ParamType.Var:
                            WritePascalString (writer, operand.Value.ToString ());
                            break;
                    }
                }
            }
        }

        private void SerializeLiveParserArray (BinaryWriter writer, List<LiveParser> array, bool prefixed = true)
        {
            if (array == null)
                array = new List<LiveParser> ();

            if (prefixed)
                writer.Write (array.Count);

            foreach (var parser in array)
            {
                SerializeLiveParser (writer, parser);
            }
        }

        private void SerializeTpWord (BinaryWriter writer, TpWord tpword)
        {
            if (tpword == null)
                tpword = new TpWord ();

            using (var tpStream = new MemoryStream ())
            using (var tpWriter = new BinaryWriter (tpStream, Encoding.GetEncoding (932)))
            {
                tpWriter.Write (Encoding.ASCII.GetBytes ("TpWord"));

                tpWriter.Write (Encoding.ASCII.GetBytes ($"{tpword.Version:D3}"));

                tpWriter.Write (tpword.Decorators.Count);
                foreach (var decorator in tpword.Decorators)
                {
                    tpWriter.Write (decorator.Count);
                    tpWriter.Write (decorator.Unk2);
                    tpWriter.Write (decorator.Unk3);
                    tpWriter.Write (decorator.Unk4);
                    tpWriter.Write (decorator.Unk5);
                    tpWriter.Write (decorator.Unk6);

                    if (tpword.Version < 100)
                        tpWriter.Write ((byte)decorator.Unk7);
                    else
                        tpWriter.Write (decorator.Unk7);

                    WritePascalString (tpWriter, decorator.Unk8);
                    WritePascalString (tpWriter, decorator.Ruby);

                    if (tpword.Version >= 100)
                    {
                        tpWriter.Write (decorator.Unk10);
                        tpWriter.Write (decorator.Unk11);
                    }
                }

                if (tpword.Version >= 104)
                {
                    tpWriter.Write (tpword.Conditions.Count);
                    foreach (var condition in tpword.Conditions)
                    {
                        tpWriter.Write (condition.Count);
                        WritePascalString (tpWriter, condition.Target);
                    }
                }

                if (tpword.Version >= 105)
                {
                    tpWriter.Write (tpword.Links.Count);
                    foreach (var link in tpword.Links)
                    {
                        tpWriter.Write (link.Count);
                        WritePascalString (tpWriter, link.Event);
                        WritePascalString (tpWriter, link.Unk3);
                    }
                }

                tpWriter.Write (tpword.Body.Count);
                foreach (var glyph in tpword.Body)
                {
                    SerializeTWdGlyph (tpWriter, glyph, tpword.Version);
                }

                writer.Write ((int)tpStream.Length);
                writer.Write (tpStream.ToArray());
            }
        }

        private void SerializeTWdGlyph (BinaryWriter writer, TWdGlyph glyph, int version)
        {
            writer.Write ((byte)glyph.Type);

            if (version >= 104)
                writer.Write (glyph.Condition ?? -1);

            switch (glyph.Type)
            {
            case TWdType.TWdChar:
                {
                    var ch = glyph as TWdChar;

                    if (version < 105)
                        WritePascalString (writer, ch.LinkName ?? "");
                    else
                        writer.Write (ch.Link ?? -1);

                    writer.Write (ch.TextSpeed);

                    // Write character
                    var bytes = Encoding.GetEncoding (932).GetBytes (ch.Ch);
                    if (bytes.Length == 1)
                    {
                        writer.Write ((byte)0);
                        writer.Write (bytes[0]);
                    }
                    else
                    {
                        writer.Write (bytes[1]);
                        writer.Write (bytes[0]);
                    }

                    writer.Write (ch.Decorator);
                    break;
                }

            case TWdType.TWdOpeDiv:
                {
                    var div = glyph as TWdOpeDiv;
                    writer.Write (div.Align);

                    if (version >= 105)
                    {
                        writer.Write (div.PadLeft);
                        writer.Write (div.PadRight);
                        writer.Write (div.NoHeight);
                    }
                    break;
                }

            case TWdType.TWdOpeReturn:
                {
                    var ret = glyph as TWdOpeReturn;
                    writer.Write ((byte)ret.BreakType);
                    break;
                }

            case TWdType.TWdOpeEvent:
                {
                    var evt = glyph as TWdOpeEvent;
                    WritePascalString (writer, evt.Event);
                    break;
                }

            case TWdType.TWdOpeVar:
            case TWdType.TWdOpeHistChar:
                {
                    var var = glyph as TWdOpeVar;
                    writer.Write (var.Decorator);

                    if (version > 100)
                        writer.Write (var.Unk3);

                    if (version > 100 && version < 105)
                        WritePascalString (writer, var.LinkName ?? "");
                    else if (version >= 105)
                        writer.Write (var.Link ?? -1);

                    if (version < 102)
                        SerializeLiveParser (writer, var.VarNameParams);
                    else
                        WritePascalString (writer, var.VarName ?? "");
                    break;
                }

            case TWdType.TWdImg:
                {
                    var img = glyph as TWdImg;

                    if (version < 105)
                        WritePascalString (writer, img.LinkName ?? "");
                    else
                        writer.Write (img.Link ?? -1);

                    writer.Write (img.TextSpeed);
                    WritePascalString (writer, img.Src);
                    writer.Write (img.Align);

                    if (version >= 103)
                        WritePascalString (writer, img.HoverSrc ?? "");

                    if (version >= 105)
                    {
                        writer.Write (img.MgnLeft);
                        writer.Write (img.MgnRight);
                        writer.Write (img.MgnTop);
                        writer.Write (img.MgnBottom);
                        WritePascalString (writer, img.DownSrc ?? "");
                    }
                    break;
                }

            case TWdType.TWdOpeIndent:
            case TWdType.TWdOpeUndent:
                // No additional data
                break;
            }
        }

        private Stream ConvertToText (LSBData lsb)
        {
            var output = new MemoryStream ();
            using (var writer = new StreamWriter (output, Encoding.UTF8, 4096, true))
            {
                writer.WriteLine ($"LiveMaker{lsb.Version:D3}");
                writer.WriteLine ("1"); // param_type

                if (lsb.Version >= 104)
                    writer.WriteLine (); // empty line

                writer.WriteLine (lsb.CommandParams.Count);

                // Write command parameters
                foreach (var params_ in lsb.CommandParams)
                {
                    var indices = new List<int> ();
                    for (int i = 0; i < params_.Length; i++)
                    {
                        if (params_[i])
                            indices.Add (i);
                    }
                    writer.WriteLine (string.Join ("\t", indices));
                }

                writer.WriteLine (); // Separator before commands
                writer.WriteLine ("; Commands");

                // Write commands with proper formatting
                foreach (var cmd in lsb.Commands)
                {
                    var parts = new List<string>
            {
                cmd.Type.ToString(),
                cmd.Indent.ToString(),
                cmd.Mute ? "1" : "0",
                cmd.NotUpdate ? "1" : "0"
            };

                    if (lsb.Version >= 106)
                    {
                        parts.Add (cmd.Color.ToString ());
                        parts.Add (cmd.LineNo.ToString ());
                    }
                    else
                    {
                        parts.Add (cmd.LineNo.ToString ());
                    }

                    // Add command-specific parameters
                    switch (cmd.Type)
                    {
                    case CommandType.If:
                    case CommandType.Elseif:
                        parts.Add (LiveParserToString (cmd.Calc));
                        break;

                    case CommandType.Else:
                    case CommandType.ClrHist:
                    case CommandType.Terminate:
                    case CommandType.DoEvent:
                    case CommandType.ClrRead:
                    case CommandType.IFDEF:
                    case CommandType.IFNDEF:
                    case CommandType.ENDIF:
                        // No additional parameters
                        break;

                    case CommandType.Label:
                    case CommandType.Comment:
                        parts.Add (cmd.Name as string ?? "");
                        break;

                    case CommandType.Jump:
                        parts.Add ($"{cmd.Page?.Page ?? ""}:{cmd.Page?.Label ?? 0}");
                        parts.Add (LiveParserToString (cmd.Calc));
                        break;

                    case CommandType.Call:
                        parts.Add ($"{cmd.Page?.Page ?? ""}:{cmd.Page?.Label ?? 0}");
                        parts.Add (cmd.Result ?? "");
                        parts.Add (LiveParserToString (cmd.Calc));
                        parts.Add (LiveParserArrayToString (cmd.Params));
                        break;

                    case CommandType.Exit:
                        parts.Add (LiveParserToString (cmd.Calc));
                        break;

                    case CommandType.Break:
                        parts.Add (LiveParserToString (cmd.Calc));
                        parts.Add (cmd.End.ToString ());
                        break;

                    case CommandType.Continue:
                        parts.Add (LiveParserToString (cmd.Calc));
                        parts.Add (cmd.Start.ToString ());
                        break;

                    case CommandType.Wait:
                        parts.Add (LiveParserToString (cmd.Calc));
                        parts.Add (LiveParserToString (cmd.Time));
                        if (lsb.Version > 0x6A && cmd.StopEvent != null)
                            parts.Add (LiveParserToString (cmd.StopEvent));
                        break;

                    case CommandType.BoxNew:
                    case CommandType.ImgNew:
                    case CommandType.MesNew:
                    case CommandType.Timer:
                    case CommandType.Movie:
                    case CommandType.Cinema:
                    case CommandType.Caption:
                    case CommandType.Menu:
                    case CommandType.Button:
                    case CommandType.ParticleNew:
                    case CommandType.FireNew:
                    case CommandType.Sound:
                    case CommandType.EditNew:
                    case CommandType.MemoNew:
                    case CommandType.MapImgNew:
                    case CommandType.WaveNew:
                    case CommandType.TileNew:
                    case CommandType.SliderNew:
                    case CommandType.ScrollbarNew:
                    case CommandType.GaugeNew:
                    case CommandType.CGCaption:
                    case CommandType.PrevMenuNew:
                        if (cmd.Components == null) break;
                        foreach (var component in cmd.Components)
                            parts.Add (LiveParserToString (component));
                        break;

                    case CommandType.Flip:
                        parts.Add (LiveParserToString (cmd.Wipe));
                        parts.Add (LiveParserToString (cmd.Time));
                        parts.Add (LiveParserToString (cmd.Reverse));
                        parts.Add (LiveParserToString (cmd.Act));
                        parts.Add (LiveParserArrayToString (cmd.Targets));
                        parts.Add (LiveParserToString (cmd.Delete));
                        if (cmd.ParamArray != null)
                            parts.Add (LiveParserArrayToString (cmd.ParamArray));
                        if (lsb.Version > 0x64 && cmd.Source != null)
                            parts.Add (LiveParserToString (cmd.Source));
                        if (lsb.Version > 0x6A && cmd.StopEvent != null)
                            parts.Add (LiveParserToString (cmd.StopEvent));
                        if (lsb.Version > 0x74 && cmd.DifferenceOnly != null)
                            parts.Add (LiveParserToString (cmd.DifferenceOnly));
                        break;

                    case CommandType.Calc:
                    case CommandType.WhileInit:
                        parts.Add (LiveParserToString (cmd.Calc));
                        break;

                    case CommandType.VarNew:
                        parts.Add (cmd.Name as string ?? "");
                        parts.Add (cmd.VarType.ToString ());
                        parts.Add (LiveParserToString (cmd.InitVal));
                        parts.Add (cmd.Scope.ToString ());
                        break;

                    case CommandType.VarDel:
                        parts.Add (cmd.Name as string ?? "");
                        break;

                    case CommandType.GetProp:
                        parts.Add (LiveParserToString (cmd.ObjName));
                        parts.Add (LiveParserToString (cmd.ObjProp));
                        parts.Add (cmd.VarName ?? "");
                        break;

                    case CommandType.SetProp:
                        parts.Add (LiveParserToString (cmd.ObjName));
                        parts.Add (LiveParserToString (cmd.ObjProp));
                        parts.Add (LiveParserToString (cmd.Value));
                        break;

                    case CommandType.ObjDel:
                        parts.Add (LiveParserToString (cmd.ObjName));
                        break;

                    case CommandType.TextIns:
                        parts.Add (TpWordToString (cmd.TpWord));
                        parts.Add (LiveParserToString (cmd.Target));
                        parts.Add (LiveParserToString (cmd.Hist));
                        parts.Add (LiveParserToString (cmd.WaitFlag));
                        if (lsb.Version > 0x6A && cmd.StopEvent != null)
                            parts.Add (LiveParserToString (cmd.StopEvent));

                        // Write the command
                        writer.WriteLine (string.Join ("\t", parts));

                        // Add a comment with the extracted text
                        if (cmd.TpWord != null)
                        {
                            var text = ExtractTextFromTpWord (cmd.TpWord);
                            if (!string.IsNullOrEmpty (text))
                                writer.WriteLine ($"; Text: {text.Replace ("\n", "\\n").Replace ("\r", "\\r")}");
                        }
                        continue; // Skip the normal write at the end

                    case CommandType.MovieStop:
                        parts.Add (LiveParserToString (cmd.Target));
                        parts.Add (LiveParserToString (cmd.Time));
                        parts.Add (LiveParserToString (cmd.WaitFlag));
                        if (lsb.Version > 0x6A && cmd.StopEvent != null)
                            parts.Add (LiveParserToString (cmd.StopEvent));
                        break;

                    case CommandType.MenuClose:
                    case CommandType.TextClr:
                    case CommandType.MediaPlay:
                        parts.Add (LiveParserToString (cmd.ObjName ?? cmd.Target));
                        break;

                    case CommandType.CallHist:
                        parts.Add (LiveParserToString (cmd.Target));
                        parts.Add (LiveParserToString (cmd.Index));
                        parts.Add (LiveParserToString (cmd.Count));
                        parts.Add (LiveParserToString (cmd.CutBreak));
                        if (lsb.Version > 0x6E && cmd.FormatName != null)
                            parts.Add (LiveParserToString (cmd.FormatName));
                        break;

                    case CommandType.While:
                        parts.Add (LiveParserToString (cmd.Calc));
                        parts.Add (cmd.End.ToString ());
                        break;

                    case CommandType.WhileLoop:
                        parts.Add (LiveParserToString (cmd.Calc));
                        parts.Add (cmd.Start.ToString ());
                        break;

                    case CommandType.GameSave:
                        parts.Add (LiveParserToString (cmd.No));
                        parts.Add (cmd.Page?.Page ?? "");
                        if (lsb.Version > 0x68)
                            parts.Add ((cmd.Page?.Label ?? 0).ToString ());
                        parts.Add (LiveParserToString (cmd.Caption));
                        break;

                    case CommandType.GameLoad:
                        parts.Add (LiveParserToString (cmd.No));
                        break;

                    case CommandType.PCReset:
                    case CommandType.Reset:
                        parts.Add ($"{cmd.Page?.Page ?? ""}:{cmd.Page?.Label ?? 0}");
                        parts.Add (cmd.AllClear.ToString ());
                        break;

                    case CommandType.PropMotion:
                        parts.Add (LiveParserToString (cmd.Name as LiveParser));
                        parts.Add (LiveParserToString (cmd.ObjName));
                        parts.Add (LiveParserToString (cmd.ObjProp));
                        parts.Add (LiveParserToString (cmd.Value));
                        parts.Add (LiveParserToString (cmd.Time));
                        parts.Add (LiveParserToString (cmd.MoveType));
                        if (lsb.Version > 0x6B && cmd.Paused != null)
                            parts.Add (LiveParserToString (cmd.Paused));
                        break;

                    case CommandType.FormatHist:
                        parts.Add (LiveParserToString (cmd.Name as LiveParser));
                        if (lsb.Version > 0x6E && cmd.Target != null)
                            parts.Add (LiveParserToString (cmd.Target));
                        break;

                    case CommandType.SaveCabinet:
                    case CommandType.LoadCabinet:
                        if (cmd.Components != null)
                        {
                            foreach (var component in cmd.Components)
                            {
                                parts.Add (LiveParserToString (component));
                            }
                        }
                        parts.Add (LiveParserToString (cmd.Act));
                        parts.Add (LiveParserArrayToString (cmd.Targets));
                        break;
                    }

                    writer.WriteLine (string.Join ("\t", parts));
                }

                writer.Flush ();
            }

            output.Position = 0;
            return output;
        }

        private string TpWordToString (TpWord tpword)
        {
            if (tpword == null)
                return "[TpWord:null]";
            return $"[TpWord:v{tpword.Version}:b{tpword.Body.Count}]";
        }

        private string LiveParserToString (LiveParser parser)
        {
            if (parser == null || parser.Entries.Count == 0)
                return "";

            // Build expression string from entries
            var result = new StringBuilder ();

            foreach (var entry in parser.Entries)
            {
                if (entry.Type == OpeDataType.To)
                {
                    if (entry.Name != "____arg")
                        result.Append ($"{entry.Name} = ");

                    if (entry.Operands.Count > 0)
                    {
                        var operand = entry.Operands[0];
                        result.Append (FormatOperand (operand));
                    }
                }
                else if (entry.Type == OpeDataType.Func)
                {
                    result.Append ($"{entry.Func}(");
                    for (int i = 0; i < entry.Operands.Count; i++)
                    {
                        if (i > 0)
                            result.Append (", ");
                        result.Append (FormatOperand (entry.Operands[i]));
                    }
                    result.Append (")");
                }
                else
                {
                    // Binary operators
                    if (entry.Operands.Count >= 2)
                    {
                        result.Append (FormatOperand (entry.Operands[0]));
                        result.Append (GetOperatorString (entry.Type));
                        result.Append (FormatOperand (entry.Operands[1]));
                    }
                }
            }

            return result.ToString ();
        }

        private string FormatOperand (Param operand)
        {
            switch (operand.Type)
            {
                case ParamType.Str:
                    return $"\"{operand.Value}\"";
                case ParamType.Var:
                    return operand.Value?.ToString () ?? "";
                case ParamType.Flag:
                    return (bool)operand.Value ? "TRUE" : "FALSE";
                default:
                    return operand.Value?.ToString () ?? "";
            }
        }

        private string GetOperatorString (OpeDataType type)
        {
            switch (type)
            {
            case OpeDataType.Plus:     return " + ";
            case OpeDataType.Minus:    return " - ";
            case OpeDataType.Mul:      return " * ";
            case OpeDataType.Div:      return " / ";
            case OpeDataType.Mod:      return " % ";
            case OpeDataType.Or:       return " | ";
            case OpeDataType.And:      return " & ";
            case OpeDataType.Xor:      return " ^ ";
            case OpeDataType.Equal:    return " == ";
            case OpeDataType.NEqual:   return " != ";
            case OpeDataType.Big:      return " > ";
            case OpeDataType.Small:    return " < ";
            case OpeDataType.EBig:     return " >= ";
            case OpeDataType.ESmall:   return " <= ";
            case OpeDataType.ShiftL:   return " << ";
            case OpeDataType.ShiftR:   return " >> ";
            case OpeDataType.ComboStr: return " ++ ";
            default: return $" {type} ";
            }
        }

        private string LiveParserArrayToString (List<LiveParser> array)
        {
            if (array == null || array.Count == 0)
                return "[]";

            var items = array.Select (p => LiveParserToString (p));
            return "[" + string.Join (", ", items) + "]";
        }

        private ScriptData ParseLSCToScript (string text, Encoding encoding)
        {
            var script = new ScriptData (text, ScriptType.PlainText);
            script.Encoding = encoding;

            // Parse text format
            var lines = text.Split (new[] { "\r\n", "\n" }, StringSplitOptions.None);
            uint lineId = 0;

            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace (line) && !line.StartsWith (";"))
                    script.TextLines.Add (new ScriptLine (lineId++, line));
            }

            return script;
        }

        private ScriptData ExtractText (LSBData lsb, Encoding encoding)
        {
            var script = new ScriptData ();
            script.Type = ScriptType.BinaryScript;
            script.Encoding = encoding;

            foreach (var cmd in lsb.Commands)
            {
                if (cmd.Type == CommandType.TextIns && cmd.TpWord != null)
                {
                    var text = ExtractTextFromTpWord (cmd.TpWord);
                    if (!string.IsNullOrEmpty (text))
                    {
                        // Find speaker from previous label
                        string speaker = null;
                        int cmdIndex = lsb.Commands.IndexOf (cmd);
                        if (cmdIndex > 0)
                        {
                            for (int i = cmdIndex - 1; i >= 0; i--)
                            {
                                if (lsb.Commands[i].Type == CommandType.Label)
                                {
                                    speaker = lsb.Commands[i].Name as string;
                                    break;
                                }
                            }
                        }

                        script.TextLines.Add (new ScriptLine (cmd.LineNo, text, speaker));
                    }
                }
            }

            return script;
        }

        private string ExtractTextFromTpWord (TpWord tpword)
        {
            var text = new StringBuilder ();

            foreach (var glyph in tpword.Body)
            {
                if (glyph.Type == TWdType.TWdChar)
                {
                    var ch = glyph as TWdChar;
                    text.Append (ch.Ch);
                }
                else if (glyph.Type == TWdType.TWdOpeReturn)
                {
                    var ret = glyph as TWdOpeReturn;
                    if (ret.BreakType == BreakType.Line)
                        text.AppendLine ();
                }
            }

            return text.ToString ();
        }

        private string ReadPascalString (IBinaryStream input)
        {
            uint length = input.ReadUInt32 ();

            if (length > 100000)
                throw new InvalidFormatException ($"Invalid Pascal string length: {length} at position 0x{input.Position - 4:X}");

            if (length == 0)
                return string.Empty;

            if (input.Position + length > input.Length)
                throw new InvalidFormatException ($"Pascal string extends past end of stream: length={length}, remaining={(input.Length - input.Position)}");

            var bytes = input.ReadBytes ((int)length);
            return Encoding.GetEncoding (932).GetString (bytes);
        }

        private void WritePascalString (BinaryWriter writer, string str)
        {
            if (string.IsNullOrEmpty (str))
                writer.Write ((uint)0);
            else
            {
                var bytes = Encoding.GetEncoding (932).GetBytes (str);
                writer.Write ((uint)bytes.Length);
                writer.Write (bytes);
            }
        }

        #region Data Classes
        private class LSBData
        {
            public int Version { get; set; }
            public byte Flags { get; set; }
            public List<bool[]> CommandParams { get; set; } = new List<bool[]> ();
            public List<Command> Commands { get; } = new List<Command> ();
        }

        private class Command
        {
            public CommandType Type { get; set; }
            public uint Indent { get; set; }
            public bool Mute { get; set; }
            public bool NotUpdate { get; set; }
            public uint LineNo { get; set; }
            public uint Color { get; set; }

            // Common fields
            public object Name { get; set; }
            public string Text { get; set; }
            public LiveParser Calc { get; set; }
            public LiveParser Time { get; set; }
            public LiveParser StopEvent { get; set; }
            public LabelReference Page { get; set; }
            public string Result { get; set; }
            public List<LiveParser> Params { get; set; }
            public List<LiveParser> Components { get; set; }
            public uint End { get; set; }
            public uint Start { get; set; }
            public ParamType VarType { get; set; }
            public LiveParser InitVal { get; set; }
            public byte Scope { get; set; }
            public string VarName { get; set; }
            public LiveParser ObjName { get; set; }
            public LiveParser ObjProp { get; set; }
            public LiveParser Value { get; set; }
            public LiveParser Target { get; set; }
            public LiveParser WaitFlag { get; set; }
            public LiveParser Hist { get; set; }
            public LiveParser Index { get; set; }
            public LiveParser Count { get; set; }
            public LiveParser CutBreak { get; set; }
            public LiveParser FormatName { get; set; }
            public LiveParser No { get; set; }
            public LiveParser Caption { get; set; }
            public byte AllClear { get; set; }
            public LiveParser MoveType { get; set; }
            public LiveParser Paused { get; set; }
            public LiveParser Act { get; set; }
            public List<LiveParser> Targets { get; set; }
            public LiveParser Wipe { get; set; }
            public LiveParser Reverse { get; set; }
            public LiveParser Delete { get; set; }
            public List<LiveParser> ParamArray { get; set; }
            public LiveParser Source { get; set; }
            public LiveParser DifferenceOnly { get; set; }
            public TpWord TpWord { get; set; }
        }

        private class LabelReference
        {
            public string Page { get; set; }
            public uint Label { get; set; }
        }

        private class LiveParser
        {
            public List<OpeData> Entries { get; } = new List<OpeData> ();
        }

        private class OpeData
        {
            public OpeDataType Type { get; set; }
            public string Name { get; set; }
            public OpeFuncType Func { get; set; }
            public List<Param> Operands { get; } = new List<Param> ();
        }

        private class Param
        {
            public ParamType Type { get; set; }
            public object Value { get; set; }
        }

        private class TpWord
        {
            public int Version { get; set; }
            public List<TDecorate> Decorators { get; } = new List<TDecorate> ();
            public List<TWdCondition> Conditions { get; } = new List<TWdCondition> ();
            public List<TWdLink> Links { get; } = new List<TWdLink> ();
            public List<TWdGlyph> Body { get; } = new List<TWdGlyph> ();
        }

        private class TDecorate
        {
            public uint Count { get; set; }
            public uint Unk2 { get; set; }
            public uint Unk3 { get; set; }
            public uint Unk4 { get; set; }
            public byte Unk5 { get; set; }
            public byte Unk6 { get; set; }
            public uint Unk7 { get; set; }
            public string Unk8 { get; set; }
            public string Ruby { get; set; }
            public uint Unk10 { get; set; }
            public uint Unk11 { get; set; }
        }

        private class TWdCondition
        {
            public uint Count { get; set; }
            public string Target { get; set; }
        }

        private class TWdLink
        {
            public uint Count { get; set; }
            public string Event { get; set; }
            public string Unk3 { get; set; }
        }

        private class TWdGlyph
        {
            public TWdType Type { get; set; }
            public int? Condition { get; set; }
        }

        private class TWdChar : TWdGlyph
        {
            public string LinkName { get; set; }
            public int? Link { get; set; }
            public uint TextSpeed { get; set; }
            public string Ch { get; set; }
            public int Decorator { get; set; }
        }

        private class TWdOpeDiv : TWdGlyph
        {
            public byte Align { get; set; }
            public int PadLeft { get; set; }
            public int PadRight { get; set; }
            public byte NoHeight { get; set; }
        }

        private class TWdOpeReturn : TWdGlyph
        {
            public BreakType BreakType { get; set; }
        }

        private class TWdOpeIndent : TWdGlyph { }
        private class TWdOpeUndent : TWdGlyph { }

        private class TWdOpeEvent : TWdGlyph
        {
            public string Event { get; set; }
        }

        private class TWdOpeVar : TWdGlyph
        {
            public int Decorator { get; set; }
            public uint Unk3 { get; set; }
            public string LinkName { get; set; }
            public int? Link { get; set; }
            public LiveParser VarNameParams { get; set; }
            public string VarName { get; set; }
        }

        private class TWdOpeHistChar : TWdOpeVar { }

        private class TWdImg : TWdGlyph
        {
            public string LinkName { get; set; }
            public int? Link { get; set; }
            public uint TextSpeed { get; set; }
            public string Src { get; set; }
            public byte Align { get; set; }
            public string HoverSrc { get; set; }
            public int MgnLeft { get; set; }
            public int MgnRight { get; set; }
            public int MgnTop { get; set; }
            public int MgnBottom { get; set; }
            public string DownSrc { get; set; }
        }
        #endregion

        #region Enums
        private enum CommandType : byte
        {
            If = 0x00,
            Elseif = 0x01,
            Else = 0x02,
            Label = 0x03,
            Jump = 0x04,
            Call = 0x05,
            Exit = 0x06,
            Wait = 0x07,
            BoxNew = 0x08,
            ImgNew = 0x09,
            MesNew = 0x0A,
            Timer = 0x0B,
            Movie = 0x0C,
            Flip = 0x0D,
            Calc = 0x0E,
            VarNew = 0x0F,
            VarDel = 0x10,
            GetProp = 0x11,
            SetProp = 0x12,
            ObjDel = 0x13,
            TextIns = 0x14,
            MovieStop = 0x15,
            ClrHist = 0x16,
            Cinema = 0x17,
            Caption = 0x18,
            Menu = 0x19,
            MenuClose = 0x1A,
            Comment = 0x1B,
            TextClr = 0x1C,
            CallHist = 0x1D,
            Button = 0x1E,
            While = 0x1F,
            WhileInit = 0x20,
            WhileLoop = 0x21,
            Break = 0x22,
            Continue = 0x23,
            ParticleNew = 0x24,
            FireNew = 0x25,
            GameSave = 0x26,
            GameLoad = 0x27,
            PCReset = 0x28,
            Reset = 0x29,
            Sound = 0x2A,
            EditNew = 0x2B,
            MemoNew = 0x2C,
            Terminate = 0x2D,
            DoEvent = 0x2E,
            ClrRead = 0x2F,
            MapImgNew = 0x30,
            WaveNew = 0x31,
            TileNew = 0x32,
            SliderNew = 0x33,
            ScrollbarNew = 0x34,
            GaugeNew = 0x35,
            CGCaption = 0x36,
            MediaPlay = 0x37,
            PrevMenuNew = 0x38,
            PropMotion = 0x39,
            FormatHist = 0x3A,
            SaveCabinet = 0x3B,
            LoadCabinet = 0x3C,
            IFDEF = 0x3D,
            IFNDEF = 0x3E,
            ENDIF = 0x3F
        }

        private enum TWdType : byte
        {
            TWdChar        = 1,
            TWdOpeDiv      = 2,
            TWdOpeReturn   = 3,
            TWdOpeIndent   = 4,
            TWdOpeUndent   = 5,
            TWdOpeEvent    = 6,
            TWdOpeVar      = 7,
            // 8 is skipped
            TWdImg         = 9,
            TWdOpeHistChar = 10
        }

        private enum ParamType : byte
        {
            Var   = 0x00,
            Int   = 0x01,
            Float = 0x02,
            Flag  = 0x03,
            Str   = 0x04
        }

        private enum OpeDataType : byte
        {
            None = 0x00,
            To = 0x01,
            Plus = 0x02,
            Minus = 0x03,
            Mul = 0x04,
            Div = 0x05,
            Mod = 0x06,
            Or = 0x07,
            And = 0x08,
            Xor = 0x09,
            DimTo = 0x0A,
            Func = 0x0B,
            Equal = 0x0C,
            Big = 0x0D,
            Small = 0x0E,
            EBig = 0x0F,
            ESmall = 0x10,
            ShiftL = 0x11,
            ShiftR = 0x12,
            ComboStr = 0x13,
            NEqual = 0x14
        }

        private enum OpeFuncType : byte
        {
            IntToStr = 0x00,
            IntToHex = 0x01,
            GetProp = 0x02,
            SetProp = 0x03,
            GetArraySize = 0x04,
            Length = 0x05,
            JLength = 0x06,
            Copy = 0x07,
            JCopy = 0x08,
            Delete = 0x09,
            JDelete = 0x0A,
            Insert = 0x0B,
            JInsert = 0x0C,
            CompareStr = 0x0D,
            CompareText = 0x0E,
            Pos = 0x0F,
            JPos = 0x10,
            Trim = 0x11,
            JTrim = 0x12,
            Exists = 0x13,
            Not = 0x14,
            SetArray = 0x15,
            FillMem = 0x16,
            CopyMem = 0x17,
            GetCheck = 0x18,
            SetCheck = 0x19,
            Random = 0x1A,
            GetSaveCaption = 0x1B,
            ArrayToString = 0x1C,
            StringToArray = 0x1D,
            IndexOfStr = 0x1E,
            SortStr = 0x1F,
            ListCompo = 0x20,
            ToClientX = 0x21,
            ToClientY = 0x22,
            ToScreenX = 0x23,
            ToScreenY = 0x24,
            Int = 0x25,
            Float = 0x26,
            Sin = 0x27,
            Cos = 0x28,
            Tan = 0x29,
            ArcSin = 0x2A,
            ArcCos = 0x2B,
            ArcTan = 0x2C,
            ArcTan2 = 0x2D,
            Hypot = 0x2E,
            IndexOfMenu = 0x2F,
            Abs = 0x30,
            Fabs = 0x31,
            VarExists = 0x32,
            EncodeDate = 0x33,
            EncodeTime = 0x34,
            DecodeDate = 0x35,
            DecodeTime = 0x36,
            GetYear = 0x37,
            GetMonth = 0x38,
            GetDay = 0x39,
            GetHour = 0x3A,
            GetMin = 0x3B,
            GetSec = 0x3C,
            GetWeek = 0x3D,
            GetWeekStr = 0x3E,
            GetWeekJStr = 0x3F,
            FixStr = 0x40,
            GetDisplayMode = 0x41,
            AddArray = 0x42,
            InsertArray = 0x43,
            DeleteArray = 0x44,
            InPrimary = 0x45,
            CopyArray = 0x46,
            FileExists = 0x47,
            LoadTextFile = 0x48,
            LowerCase = 0x49,
            UpperCase = 0x4A,
            ExtractFilePath = 0x4B,
            ExtractFileName = 0x4C,
            ExtractFileExt = 0x4D,
            IsPathDelimiter = 0x4E,
            AddBackSlash = 0x4F,
            ChangeFileExt = 0x50,
            IsDelimiter = 0x51,
            StringOfChar = 0x52,
            StringReplace = 0x53,
            AssignTemp = 0x54,
            HanToZen = 0x55,
            ZenToHan = 0x56,
            DBCreateTable = 0x57,
            DBSetActive = 0x58,
            DBAddField = 0x59,
            DBSetRecNo = 0x5A,
            DBInsert = 0x5B,
            DBDelete = 0x5C,
            DBGetInt = 0x5D,
            DBSetInt = 0x5E,
            DBGetFloat = 0x5F,
            DBSetFloat = 0x60,
            DBGetBool = 0x61,
            DBSetBool = 0x62,
            DBGetStr = 0x63,
            DBSetStr = 0x64,
            DBRecordCount = 0x65,
            DBFindFirst = 0x66,
            DBFindLast = 0x67,
            DBFindNext = 0x68,
            DBFindPrior = 0x69,
            DBLocate = 0x6A,
            DBLoadTsvFile = 0x6B,
            DBDirectGetInt = 0x6C,
            DBDirectSetInt = 0x6D,
            DBDirectGetFloat = 0x6E,
            DBDirectSetFloat = 0x6F,
            DBDirectGetBool = 0x70,
            DBDirectSetBool = 0x71,
            DBDirectGetStr = 0x72,
            DBDirectSetStr = 0x73,
            DBCopyTable = 0x74,
            DBDeleteTable = 0x75,
            DBInsertTable = 0x76,
            DBCopy = 0x77,
            DBClearTable = 0x78,
            DBSort = 0x79,
            DBGetActive = 0x7A,
            DBGetRecNo = 0x7B,
            DBClearRecord = 0x7C,
            SetWallPaper = 0x7D,
            Min = 0x7E,
            Max = 0x7F,
            Fmin = 0x80,
            Fmax = 0x81,
            GetVarType = 0x82,
            GetEnabled = 0x83,
            SetEnabled = 0x84,
            AddDelimiter = 0x85,
            ListSaveCaption = 0x86,
            OpenUrl = 0x87,
            Calc = 0x88,
            SaveScreen = 0x89,
            StrToIntDef = 0x8A,
            StrToFloatDef = 0x8B,
            GetVisible = 0x8C,
            SetVisible = 0x8D,
            GetHistoryCount = 0x8E,
            GetHistoryMaxCount = 0x8F,
            SetHistoryMaxCount = 0x90,
            GetGroupIndex = 0x91,
            GetSelected = 0x92,
            SetSelected = 0x93,
            SelectOpenFile = 0x94,
            SelectSaveFile = 0x95,
            SelectDirectory = 0x96,
            ExtractFile = 0x97,
            Chr = 0x98,
            Ord = 0x99,
            InCabinet = 0x9A,
            PushVar = 0x9B,
            PopVar = 0x9C,
            DeleteStack = 0x9D,
            CopyFile = 0x9E,
            DBGetTableCount = 0x9F,
            DBGetTable = 0xA0,
            CreateObject = 0xA1,
            DeleteObject = 0xA2,
            GetItem = 0xA3,
            UniqueArray = 0xA4,
            TrimArray = 0xA5,
            GetImeOpened = 0xA6,
            SetImeOpened = 0xA7,
            Alert = 0xA8,
            GetCinemaProp = 0xA9,
            SetCinemaProp = 0xAA
        }

        private enum BreakType : byte
        {
            Line = 0,
            Page = 1,
            Reserved = 2,
            Pause = 3,
            Clear = 4
        }

        private enum PropertyType : byte
        {
            PR_NONE = 0x00,
            PR_NAME = 0x01,
            PR_PARENT = 0x02,
            PR_SOURCE = 0x03,
            PR_LEFT = 0x04,
            PR_TOP = 0x05,
            PR_WIDTH = 0x06,
            PR_HEIGHT = 0x07,
            PR_ZOOMX = 0x08,
            PR_COLOR = 0x09,
            PR_BORDERWIDTH = 0x0A,
            PR_BORDERCOLOR = 0x0B,
            PR_ALPHA = 0x0C,
            PR_PRIORITY = 0x0D,
            PR_OFFSETX = 0x0E,
            PR_OFFSETY = 0x0F,
            PR_FONTNAME = 0x10,
            PR_FONTHEIGHT = 0x11,
            PR_FONTSTYLE = 0x12,
            PR_LINESPACE = 0x13,
            PR_FONTCOLOR = 0x14,
            PR_FONTLINKCOLOR = 0x15,
            PR_FONTBORDERCOLOR = 0x16,
            PR_FONTHOVERCOLOR = 0x17,
            PR_FONTHOVERSTYLE = 0x18,
            PR_HOVERCOLOR = 0x19,
            PR_ANTIALIAS = 0x1A,
            PR_DELAY = 0x1B,
            PR_PAUSED = 0x1C,
            PR_VOLUME = 0x1D,
            PR_REPEAT = 0x1E,
            PR_BALANCE = 0x1F,
            PR_ANGLE = 0x20,
            PR_ONPLAYING = 0x21,
            PR_ONNOTIFY = 0x22,
            PR_ONMOUSEMOVE = 0x23,
            PR_ONMOUSEOUT = 0x24,
            PR_ONLBTNDOWN = 0x25,
            PR_ONLBTNUP = 0x26,
            PR_ONRBTNDOWN = 0x27,
            PR_ONRBTNUP = 0x28,
            PR_ONWHEELDOWN = 0x29,
            PR_ONWHEELUP = 0x2A,
            PR_BRIGHTNESS = 0x2B,
            PR_ONPLAYEND = 0x2C,
            PR_INDEX = 0x2D,
            PR_COUNT = 0x2E,
            PR_ONLINK = 0x2F,
            PR_VISIBLE = 0x30,
            PR_COLCOUNT = 0x31,
            PR_ROWCOUNT = 0x32,
            PR_TEXT = 0x33,
            PR_MARGINX = 0x34,
            PR_MARGINY = 0x35,
            PR_HALIGN = 0x36,
            PR_BORDERSOURCETL = 0x37,
            PR_BORDERSOURCETC = 0x38,
            PR_BORDERSOURCETR = 0x39,
            PR_BORDERSOURCECL = 0x3A,
            PR_BORDERSOURCECC = 0x3B,
            PR_BORDERSOURCECR = 0x3C,
            PR_BORDERSOURCEBL = 0x3D,
            PR_BORDERSOURCEBC = 0x3E,
            PR_BORDERSOURCEBR = 0x3F,
            PR_BORDERHALIGNT = 0x40,
            PR_BORDERHALIGNC = 0x41,
            PR_BORDERHALIGNB = 0x42,
            PR_BORDERVALIGNL = 0x43,
            PR_BORDERVALIGNC = 0x44,
            PR_BORDERVALIGNR = 0x45,
            PR_SCROLLSOURCE = 0x46,
            PR_CHECKSOURCE = 0x47,
            PR_AUTOSCRAP = 0x48,
            PR_ONSELECT = 0x49,
            PR_RCLICKSCRAP = 0x4A,
            PR_ONOPENING = 0x4B,
            PR_ONOPENED = 0x4C,
            PR_ONCLOSING = 0x4D,
            PR_ONCLOSED = 0x4E,
            PR_CARETX = 0x4F,
            PR_CARETY = 0x50,
            PR_IGNOREMOUSE = 0x51,
            PR_TEXTPAUSED = 0x52,
            PR_TEXTDELAY = 0x53,
            PR_HOVERSOURCE = 0x54,
            PR_PRESSEDSOURCE = 0x55,
            PR_GROUPINDEX = 0x56,
            PR_ALLOWALLUP = 0x57,
            PR_SELECTED = 0x58,
            PR_CAPTUREMASK = 0x59,
            PR_POWER = 0x5A,
            PR_ORIGWIDTH = 0x5B,
            PR_ORIGHEIGHT = 0x5C,
            PR_APPEARX = 0x5D,
            PR_APPEARY = 0x5E,
            PR_PARTMOTION = 0x5F,
            PR_PARAM = 0x60,
            PR_PARAM2 = 0x61,
            PR_TOPINDEX = 0x62,
            PR_READONLY = 0x63,
            PR_CURSOR = 0x64,
            PR_POSZOOMED = 0x65,
            PR_ONPLAYSTART = 0x66,
            PR_PARAM3 = 0x67,
            PR_ONMOUSEIN = 0x68,
            PR_ONMAPIN = 0x69,
            PR_ONMAPOUT = 0x6A,
            PR_MAPSOURCE = 0x6B,
            PR_AMP = 0x6C,
            PR_WAVELEN = 0x6D,
            PR_SCROLLX = 0x6E,
            PR_SCROLLY = 0x6F,
            PR_FLIPH = 0x70,
            PR_FLIPV = 0x71,
            PR_ONIDLE = 0x72,
            PR_DISTANCEX = 0x73,
            PR_DISTANCEY = 0x74,
            PR_CLIPLEFT = 0x75,
            PR_CLIPTOP = 0x76,
            PR_CLIPWIDTH = 0x77,
            PR_CLIPHEIGHT = 0x78,
            PR_DURATION = 0x79,
            PR_THUMBSOURCE = 0x7A,
            PR_BUTTONSOURCE = 0x7B,
            PR_MIN = 0x7C,
            PR_MAX = 0x7D,
            PR_VALUE = 0x7E,
            PR_ORIENTATION = 0x7F,
            PR_SMALLCHANGE = 0x80,
            PR_LARGECHANGE = 0x81,
            PR_MAPTEXT = 0x82,
            PR_GLYPHWIDTH = 0x83,
            PR_GLYPHHEIGHT = 0x84,
            PR_ZOOMY = 0x85,
            PR_CLICKEDSOURCE = 0x86,
            PR_ANIPAUSED = 0x87,
            PR_ONHOLD = 0x88,
            PR_ONRELEASE = 0x89,
            PR_REVERSE = 0x8A,
            PR_PLAYING = 0x8B,
            PR_REWINDONLOAD = 0x8C,
            PR_COMPOTYPE = 0x8D,
            PR_FONTSHADOWCOLOR = 0x8E,
            PR_FONTBORDER = 0x8F,
            PR_FONTSHADOW = 0x90,
            PR_ONKEYDOWN = 0x91,
            PR_ONKEYUP = 0x92,
            PR_ONKEYREPEAT = 0x93,
            PR_HANDLEKEY = 0x94,
            PR_ONFOCUSIN = 0x95,
            PR_ONFOCUSOUT = 0x96,
            PR_OVERLAY = 0x97,
            PR_TAG = 0x98,
            PR_CAPTURELINK = 0x99,
            PR_FONTHOVERBORDER = 0x9A,
            PR_FONTHOVERBORDERCOLOR = 0x9B,
            PR_FONTHOVERSHADOW = 0x9C,
            PR_FONTHOVERSHADOWCOLOR = 0x9D,
            PR_BARSIZE = 0x9E,
            PR_MUTEONLOAD = 0x9F,
            PR_PLUSX = 0xA0,
            PR_PLUSY = 0xA1,
            PR_CARETHEIGHT = 0xA2,
            PR_REPEATPOS = 0xA3,
            PR_BLURSPAN = 0xA4,
            PR_BLURDELAY = 0xA5,
            PR_FONTCHANGEABLED = 0xA6,
            PR_IMEMODE = 0xA7,
            PR_FLOATANGLE = 0xA8,
            PR_FLOATZOOMX = 0xA9,
            PR_FLOATZOOMY = 0xAA,
            PR_CAPMASKLEVEL = 0xAB,
            PR_PADDINGLEFT = 0xAC,
            PR_PADDINGRIGHT = 0xAD
        }
        #endregion
    }
}