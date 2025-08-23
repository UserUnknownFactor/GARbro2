using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;


namespace GameRes.Formats.Ikura
{
    public static class IsfArchiveExtension
    {
        internal static IsfAssembler Decompile(this byte[] data) => data.ToAssembler();

        internal static IsfAssembler Compile(this string code) => code.ToAssembler();

        internal static void Write(this BinaryWriter output, IsfAssembler assembler)
        {
            var position = output.BaseStream.Position;
            var data = (uint)(8 + assembler.Labels.Length * 4);
            output.Write(data);
            output.Write(assembler.Version);
            output.Write(assembler.Key);
            switch (assembler.Encoding.CodePage)
            {
                case 932: // Shift_JIS
                    output.Write((byte)0x80);
                    break;
                case 936: // GBK
                    output.Write((byte)0x86);
                    break;
                default:
                    output.Write((byte)0x00);
                    break;
            }

            var offset = data;
            for (var i = 0; i < assembler.Actions.Length; i++)
            {
                for (var j = 0; j < assembler.Labels.Length; j++)
                {
                    if (assembler.Labels[j] != i) continue;
                    output.BaseStream.Position = position + 8 + j * 4;
                    output.Write(offset - data);
                }

                output.BaseStream.Position = position + offset;
                output.Write((byte)assembler.Actions[i].Instruction);
                var size = (uint)assembler.Actions[i].Args.Sum(arg => arg.SizeForIsf());
                if (2 + size > 0x7F)
                {
                    output.Write((byte)((3 + size) >> 8 | 0x80));
                    output.Write((byte)((3 + size) & 0xFF));
                    offset += 3 + size;
                }
                else
                {
                    output.Write((byte)(2 + size));
                    offset += 2 + size;
                }

                output.Write(assembler.Actions[i].Args);
                var current = output.BaseStream.Position - position;
                if (current != offset)
                {
                    throw new FormatException($"{i} {assembler.Actions[i].Instruction} {current} != {offset}");
                }
            }

            Func<byte, byte> convert;
            switch (assembler.Version)
            {
                case 0x9795:
                    convert = b => (byte)(b << 2 | b >> 6);
                    break;
                case 0xD197:
                    convert = b => (byte)~b;
                    break;
                case 0xCE89:
                    convert = b => (byte)(b ^ assembler.Key);
                    break;
                default:
                    return;
            }

            var buffer = new byte[output.BaseStream.Position - position - 8];
            output.BaseStream.Position = position + 8;
            output.BaseStream.Read(buffer, 0, buffer.Length);
            for (var i = 0; i < buffer.Length; i++) buffer[i] = convert.Invoke(buffer[i]);
            output.BaseStream.Position = position + 8;
            output.BaseStream.Write(buffer, 0, buffer.Length);
        }

        private static void Write(this BinaryWriter output, object[] args)
        {
            foreach (var arg in args)
            {
                switch (arg)
                {
                    case byte uint8:
                        output.Write(uint8);
                        break;
                    case ushort uint16:
                        output.Write(uint16);
                        break;
                    case uint uint32:
                        output.Write(uint32);
                        break;
                    case UInt24 uint24:
                        output.Write(uint24.Bytes);
                        break;
                    case CString str:
                        output.Write(str.Bytes);
                        break;
                    case IsfString str:
                        output.Write(str.Bytes);
                        break;
                    case IsfLabel label:
                        output.Write(label.Index);
                        break;
                    case IsfValue value:
                        output.Write(value.Id);
                        break;
                    case IsfTable table:
                        output.Write(table.Value);
                        output.Write((byte)table.Labels.Length);
                        foreach (var label in table.Labels)
                        {
                            output.Write(label);
                        }

                        break;
                    case IsfMessage message:
                        foreach (var action in message.Actions)
                        {
                            output.Write(action.Key);
                            output.Write(action.Value);
                        }

                        break;
                    case IsfCondition condition:
                        var and = false;
                        foreach (var term in condition.Terms)
                        {
                            if (and) output.Write((byte)0x02);
                            output.Write(term.L);
                            output.Write(term.C);
                            output.Write(term.R);
                            and = true;
                        }

                        output.Write(condition.Action.Key);
                        output.Write(condition.Action.Value);
                        output.Write((byte)0xFF);
                        break;
                    case IsfAssignment assignment:
                        output.Write(assignment.Variable);
                        foreach (var term in assignment.Terms)
                        {
                            output.Write(term.Key);
                            output.Write(term.Value);
                        }

                        output.Write((byte)0x05);
                        break;
                    default:
                        throw new NotSupportedException($"{arg.GetType()} is unsupported.");
                }
            }
        }

        private static string ToText(this object arg, Encoding encoding)
        {
            switch (arg)
            {
                case byte uint8:
                    return $"0x{uint8:X2}";
                case ushort uint16:
                    return $"0x{uint16:X4}";
                case uint uint32:
                    return $"0x{uint32:X8}";
                case CString str:
                    return $"'{str.ToString(encoding)}'";
                case IsfString str:
                    return $"`{str.Decode().ToString(encoding)}`";
                default:
                    return arg.ToString();
            }
        }

        private static object ToArg(this string text, Encoding encoding)
        {
            switch (text[0])
            {
                case 'L':
                    return IsfLabel.Parse(text);
                case '0':
                    if (text == "0") return new IsfValue { Id = 0 };
                    // hex
                    switch (text.Length)
                    {
                        case 4: // 0x00
                            return byte.Parse(text.Substring(2), NumberStyles.HexNumber);
                        case 6: // 0x0000
                            return ushort.Parse(text.Substring(2), NumberStyles.HexNumber);
                        case 8: // 0x000000
                            return UInt24.Parse(text);
                        default: // 0x00000000
                            return uint.Parse(text.Substring(2), NumberStyles.HexNumber);
                    }
                case '\'':
                    var bytes = encoding.GetBytes(text.Trim('\''));
                    Array.Resize(ref bytes, bytes.Length + 1);
                    return new CString { Bytes = bytes };
                case '`':
                    var bytes_ = encoding.GetBytes(text.Trim('`'));
                    Array.Resize(ref bytes_, bytes_.Length + 1);
                    return IsfString.Encode(new CString { Bytes = bytes_ });
                default:
                    return IsfValue.Parse(text);
            }
        }

        private static IsfAssembler ToAssembler(this byte[] data)
        {
            var offset = data.ToInt32(0);
            var version = data.ToUInt16(4);
            var key = data.ToUInt8(6);
            var charset = data.ToUInt8(7);
            var encoding = Encoding.GetEncoding("Shift-JIS");

            switch (charset)
            {
                case 0x80:
                    encoding = Encoding.GetEncoding("Shift-JIS");
                    break;
                case 0x86:
                    encoding = Encoding.GetEncoding("GBK");
                    break;
            }

            switch (version)
            {
                case 0x9795:
                    for (var i = 8; i < data.Length; i++) data[i] = (byte)(data[i] >> 2 | data[i] << 6);
                    break;
                case 0xD197:
                    for (var i = 8; i < data.Length; i++) data[i] = (byte)~data[i];
                    break;
                case 0xCE89:
                    for (var i = 8; i < data.Length; i++) data[i] = (byte)(data[i] ^ key);
                    break;
            }

            var table = Enumerable.Range(0, (offset - 8) / 4)
                .Select(i => data.ToInt32(8 + i * 4))
                .ToArray();

            var pos = offset;
            var actions = new List<IsfAction>();
            var labels = new int[table.Length];

            while (pos < data.Length)
            {
                for (var j = 0; j < table.Length; j++)
                {
                    if (table[j] != pos - offset) continue;
                    labels[j] = actions.Count;
                }

                var instruction = (IsfInstruction)data.ToUInt8(pos);
                var size = (int)data.ToUInt8(pos + 1);
                var used = 0x02;
                if (size > 0x7F)
                {
                    size = ((size & 0x7F) << 8) | data.ToUInt8(pos + 2);
                    used++;
                }

                size = size < used ? used : size;
                pos += size;

                var action = new IsfAction
                {
                    Instruction = instruction,
                    Args = instruction.Parse(data.Slice(pos - size + used, pos))
                };
                actions.Add(action);
            }

            return new IsfAssembler
            {
                Version = version,
                Key = key,
                Actions = actions.ToArray(),
                Encoding = encoding,
                Labels = labels
            };
        }

        private static IsfAssembler ToAssembler(this string code)
        {
            var lines = code.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var version = ushort.Parse(
                (lines.FirstOrDefault(line => line.StartsWith("; version: ")) ?? "9597")
                .Replace("; version: ", "").Trim()
                , NumberStyles.HexNumber
            );
            var key = byte.Parse(
                (lines.FirstOrDefault(line => line.StartsWith("; key: ")) ?? "00")
                .Replace("; key: ", "").Trim()
                , NumberStyles.HexNumber
            );
            var encoding = Encoding.GetEncoding(
                (lines.FirstOrDefault(line => line.StartsWith("; encoding: ")) ?? "Shift-JIS")
                .Replace("; encoding: ", "").Trim()
            );

            var actions = new List<IsfAction>();
            var table = new List<KeyValuePair<int, int>>();

            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith(";")) continue;
                if (lines[i].StartsWith("#LABEL_"))
                {
                    var index = int.Parse(Regex.Match(lines[i], @"#LABEL_([0-9]+)").Groups[1].Value);
                    table.Add(new KeyValuePair<int, int>(index, actions.Count));
                    continue;
                }

                var name = Regex.Match(lines[i], @"    (\w+)").Groups[1].Value;
                if (!Enum.TryParse(name, out IsfInstruction instruction)) throw new FormatException(lines[i]);
                var args = new List<object>();

                // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                switch (instruction)
                {
                    case IsfInstruction.ONJP:
                    case IsfInstruction.ONJS:
                        args.Add(IsfTable.Parse(lines[i].Substring(9)));
                        break;
                    case IsfInstruction.CSET:
                    case IsfInstruction.CNS:
                    {
                        // unused '\x00'
                        var parts = lines[i].Substring(4 + instruction.ToString().Length).ToArgs(encoding);
                        var str = (CString)parts[parts.Length - 1];
                        Array.Resize(ref str.Bytes, str.Bytes.Length - 1);
                        parts[parts.Length - 1] = str;
                        args.AddRange(parts);
                    }
                        break;
                    case IsfInstruction.PM:
                    case IsfInstruction.PMP:
                    {
                        args.AddRange(lines[i].Substring(4 + instruction.ToString().Length).ToArgs(encoding));
                        var messages = new List<KeyValuePair<byte, object[]>>();
                        while (!lines[++i].StartsWith("    END"))
                        {
                            if (lines[i].StartsWith(";")) continue;
                            var parts = lines[i].Substring(8).ToArgs(encoding);
                            var type = (byte)parts[0];
                            var data = parts.Slice(1, parts.Length);
                            var message = new KeyValuePair<byte, object[]>(type, data);
                            messages.Add(message);
                        }

                        args.Add(new IsfMessage { Actions = messages.ToArray() });
                    }
                        break;
                    case IsfInstruction.CALC:
                        args.Add(IsfAssignment.Parse(lines[i].Substring(9)));
                        break;
                    case IsfInstruction.IF:
                    {
                        var terms = new List<IsfCondition.Term> { IsfCondition.Term.Parse(lines[i].Substring(7)) };
                        var op = new KeyValuePair<byte, object[]>();
                        while (!lines[++i].StartsWith("    END"))
                        {
                            if (lines[i].StartsWith(";")) continue;
                            switch (lines[i].Substring(8, 2))
                            {
                                case "AN": // AND
                                    terms.Add(IsfCondition.Term.Parse(lines[i].Substring(12)));
                                    break;
                                case "JP":
                                    op = new KeyValuePair<byte, object[]>(
                                        0x00,
                                        lines[i].Substring(10).ToArgs(encoding)
                                    );
                                    break;
                                case "HS":
                                    op = new KeyValuePair<byte, object[]>(
                                        0x01,
                                        lines[i].Substring(10).ToArgs(encoding)
                                    );
                                    break;
                                default:
                                    throw new FormatException(lines[i]);
                            }
                        }

                        args.Add(new IsfCondition { Terms = terms.ToArray(), Action = op });
                    }
                        break;
                    case IsfInstruction.MPM:
                    {
                        args.AddRange(lines[i].Substring(4 + instruction.ToString().Length).ToArgs(encoding));
                        if ((byte)args[1] == 0x00) break;
                        var messages = new List<KeyValuePair<byte, object[]>>();
                        while (!lines[++i].StartsWith("    END"))
                        {
                            if (lines[i].StartsWith(";")) continue;
                            var parts = lines[i].Substring(8).ToArgs(encoding);
                            var type = (byte)parts[0];
                            var data = parts.Slice(1, parts.Length);
                            var message = new KeyValuePair<byte, object[]>(type, data);
                            messages.Add(message);
                        }

                        args.Add(new IsfMessage { Actions = messages.ToArray() });
                    }
                        break;
                    default:
                        args.AddRange(lines[i].Substring(4 + instruction.ToString().Length).ToArgs(encoding));
                        break;
                }

                var action = new IsfAction
                {
                    Instruction = instruction,
                    Args = args.ToArray()
                };
                actions.Add(action);
            }

            var labels = new int[table.Count];
            foreach (var pair in table)
            {
                labels[pair.Key] = pair.Value;
            }

            var assembler = new IsfAssembler
            {
                Version = version,
                Key = key,
                Actions = actions.ToArray(),
                Encoding = encoding,
                Labels = labels
            };

            return assembler;
        }

        private static object[] Parse(this IsfInstruction instruction, byte[] data)
        {
            Func<byte[], int, object> uint8 = (bytes, pos) => bytes.ToUInt8(pos);
            Func<byte[], int, object> uint16 = (bytes, pos) => bytes.ToUInt16(pos);
            Func<byte[], int, object> uint24 = (bytes, pos) => bytes.ToUInt24(pos);
            Func<byte[], int, object> uint32 = (bytes, pos) => bytes.ToUInt32(pos);
            Func<byte[], int, object> cstring = (bytes, pos) => bytes.ToCString(pos);
            Func<byte[], int, object> label = (bytes, pos) => bytes.ToIsfLabel(pos);
            Func<byte[], int, object> value = (bytes, pos) => bytes.ToIsfValue(pos);
            Func<byte[], int, object> table = (bytes, pos) => bytes.ToIsfTable(pos);
            Func<byte[], int, object> message = (bytes, pos) => bytes.ToIsfMessage(pos);
            Func<byte[], int, object> condition = (bytes, pos) => bytes.ToIsfCondition(pos);
            Func<byte[], int, object> assignment = (bytes, pos) => bytes.ToIsfAssignment(pos);
            Func<byte[], int, object>[] readers;

            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (instruction)
            {
                case IsfInstruction.ED:
                    return data.ToArgs();
                case IsfInstruction.LS:
                case IsfInstruction.LSBS:
                    return data.ToArgs(cstring);
                case IsfInstruction.SRET:
                    return data.ToArgs();
                case IsfInstruction.JP:
                case IsfInstruction.JS:
                    return data.ToArgs(label);
                case IsfInstruction.RT:
                    return data.ToArgs();
                case IsfInstruction.ONJP:
                case IsfInstruction.ONJS:
                    return data.ToArgs(table);
                case IsfInstruction.CHILD:
                    return data.ToArgs(cstring);
                case IsfInstruction.CW:
                    return data.ToArgs(uint8, value, value, value, value, uint8);
                case IsfInstruction.CP:
                case IsfInstruction.CIR:
                    return data.ToArgs(uint8, uint8, cstring);
                case IsfInstruction.CPS:
                    return data.ToArgs(uint8, uint24, uint24, uint24, uint24, uint24, uint24);
                case IsfInstruction.CIP:
                    return data.ToArgs(uint8, uint8, value, value, value);
                case IsfInstruction.CSET:
                    return data.ToArgs(uint8, uint8, value, value, value, value, cstring);
                case IsfInstruction.CWO:
                    return data.ToArgs(uint8, value, uint8);
                case IsfInstruction.CWC:
                    return data.ToArgs(uint8);
                case IsfInstruction.CC:
                    return data.ToArgs(uint8, uint16, uint8);
                case IsfInstruction.CRESET:
                case IsfInstruction.CRND:
                    return data.ToArgs();
                case IsfInstruction.CTEXT:
                    return data.ToArgs(uint8, value, value, value, value, cstring,
                        value, value, value, value, value, value);
                case IsfInstruction.WS:
                    return data.ToArgs(uint8, value, value, value, value, uint8);
                case IsfInstruction.WP:
                    return data.ToArgs(uint8, uint8, cstring);
                case IsfInstruction.WL:
                    return data.ToArgs(uint8, cstring);
                case IsfInstruction.WW:
                    return data.ToArgs(uint8, value, value, uint8);
                case IsfInstruction.CN:
                    return data.ToArgs(uint8, uint8);
                case IsfInstruction.CNS:
                    return data.ToArgs(uint8, uint8, cstring);
                case IsfInstruction.PF:
                case IsfInstruction.PB:
                    return data.ToArgs(uint8, value);
                case IsfInstruction.PJ:
                    return data.ToArgs(uint8, uint8);
                case IsfInstruction.WO:
                case IsfInstruction.WC:
                    return data.ToArgs(uint8);
                case IsfInstruction.PM:
                case IsfInstruction.PMP:
                    return data.ToArgs(uint8, message);
                case IsfInstruction.WSH:
                case IsfInstruction.WSS:
                    return data.ToArgs(value);
                case IsfInstruction.FLN:
                    return data.ToArgs(uint16);
                case IsfInstruction.SK:
                    return data.ToArgs(uint16, uint8);
                case IsfInstruction.SKS:
                    return data.ToArgs(uint16, uint16, uint8);
                case IsfInstruction.HF:
                    return data.ToArgs(uint16, uint16);
                case IsfInstruction.FT:
                    return data.ToArgs(uint16, uint16, uint16);
                case IsfInstruction.SP:
                    readers = Enumerable.Repeat(uint16, 1 + data.Length / 2).ToArray();
                    readers[0] = uint8;
                    readers[readers.Length - 1] = uint8;
                    return data.ToArgs(readers: readers);
                case IsfInstruction.STS:
                    return data.ToArgs(uint8, uint8);
                case IsfInstruction.HP:
                    return data.ToArgs(uint8, uint16);
                case IsfInstruction.ES:
                case IsfInstruction.EC:
                    return data.ToArgs(uint16, uint16);
                case IsfInstruction.STC:
                    return data.ToArgs(uint8, uint8, uint16);
                case IsfInstruction.HN:
                    return data.ToArgs(uint16, uint16);
                case IsfInstruction.HXP:
                    return data.ToArgs(uint8, uint8, uint16);
                case IsfInstruction.HLN:
                    return data.ToArgs(uint16);
                case IsfInstruction.HS:
                    return data.ToArgs(uint16, value);
                case IsfInstruction.HINC:
                case IsfInstruction.HDEC:
                    return data.ToArgs(uint16);
                case IsfInstruction.CALC:
                    return data.ToArgs(assignment);
                case IsfInstruction.HSG:
                    return data.ToArgs(uint16, uint16, value);
                case IsfInstruction.HT:
                    return data.ToArgs(uint16, uint16, uint16);
                case IsfInstruction.IF:
                    return data.ToArgs(condition);
                case IsfInstruction.EXA:
                    return data.ToArgs(uint16, uint16);
                case IsfInstruction.EXS:
                case IsfInstruction.EXC:
                    return data.ToArgs(value, value, value, uint8);
                case IsfInstruction.SCP:
                case IsfInstruction.SSP:
                    return data.ToArgs(uint16, uint8);
                case IsfInstruction.VSET:
                case IsfInstruction.GN:
                    return data.ToArgs(value, value, value);
                case IsfInstruction.GF:
                    return data.ToArgs();
                case IsfInstruction.GC:
                    return data.ToArgs(value, uint8, uint8, uint8);
                case IsfInstruction.GI:
                    return data.ToArgs(value, value, uint8, uint8, uint8);
                case IsfInstruction.GO:
                    return data.ToArgs(value, uint8, uint8, uint8, uint8);
                case IsfInstruction.GL:
                    return data.ToArgs(value, cstring);
                case IsfInstruction.GP:
                    readers = Enumerable.Repeat(value, 1 + (data.Length - 1) / 4).ToArray();
                    readers[0] = uint8;
                    return data.ToArgs(readers: readers);
                case IsfInstruction.GB:
                    return data.ToArgs(value, uint8, uint8, uint8, uint8, value, value, value, value);
                case IsfInstruction.GPB:
                    return data.ToArgs(value);
                case IsfInstruction.GPJ:
                    return data.ToArgs(uint8);
                case IsfInstruction.PR:
                    return data.ToArgs(value, value, value);
                case IsfInstruction.GASTAR:
                    return data.ToArgs(uint8, uint8, uint8, uint8, uint8, value);
                case IsfInstruction.GASTOP:
                    return data.ToArgs();
                case IsfInstruction.GPI:
                case IsfInstruction.GPO:
                    readers = Enumerable.Repeat(value, 1 + (data.Length - 1) / 4).ToArray();
                    readers[0] = uint8;
                    return data.ToArgs(readers: readers);
                case IsfInstruction.GGE:
                    return data.ToArgs(value, value, value, value, value, cstring);
                case IsfInstruction.GPE:
                    return data.ToArgs(uint8,
                        value,
                        value, value, value, value, // rect
                        value,
                        value, value, value, value // rect
                    );
                case IsfInstruction.GSCRL:
                    return data.ToArgs(uint8,
                        value,
                        value, value, value, value, // array
                        value, value, value, value, // array
                        value, value, // array
                        value, value, // array
                        value
                    );
                case IsfInstruction.GV:
                    return data.ToArgs(uint16, uint8, uint8, value);
                case IsfInstruction.GAL:
                    return data.ToArgs();
                case IsfInstruction.GAOPEN:
                    return data.ToArgs(uint8, uint8, uint8, uint8, cstring);
                case IsfInstruction.GASET:
                case IsfInstruction.GAPOS:
                case IsfInstruction.GACLOSE:
                case IsfInstruction.GADELETE:
                    return data.ToArgs();
                case IsfInstruction.SGL:
                    return data.ToArgs(value, value, value, value);
                case IsfInstruction.ML:
                    return data.ToArgs(cstring, uint8);
                case IsfInstruction.MP:
                    return data.ToArgs(uint8, value);
                case IsfInstruction.MF:
                    return data.ToArgs(value);
                case IsfInstruction.MS:
                    return data.ToArgs();
                case IsfInstruction.SER:
                    return data.ToArgs(cstring, value);
                case IsfInstruction.SEP:
                    return data.ToArgs(value, value);
                case IsfInstruction.SED:
                    return data.ToArgs(value);
                case IsfInstruction.PCMON:
                    return data.ToArgs(uint8);
                case IsfInstruction.PCML:
                    return data.ToArgs(cstring);
                case IsfInstruction.PCMS:
                    return data.ToArgs(value);
                case IsfInstruction.PCMEND:
                    return data.ToArgs();
                case IsfInstruction.SES:
                    return data.ToArgs(value, value);
                case IsfInstruction.BGMGETPOS:
                    return data.ToArgs(uint8, uint16);
                case IsfInstruction.SEGETPOS:
                    return data.ToArgs(value, uint8, uint16);
                case IsfInstruction.PCMGETPOS:
                    return data.ToArgs(uint8, uint16);
                case IsfInstruction.PCMCN:
                    return data.ToArgs();
                case IsfInstruction.IM:
                    return data.ToArgs(uint8, cstring);
                case IsfInstruction.IC:
                    return data.ToArgs(value);
                case IsfInstruction.IMS:
                    return data.ToArgs(value, value, value, value);
                case IsfInstruction.IXY:
                    return data.ToArgs(value, value);
                case IsfInstruction.IH:
                    return data.ToArgs(uint8,
                        value, value, value, value,
                        uint8, uint16, uint8, uint8, uint8
                    );
                case IsfInstruction.IG:
                    return data.ToArgs(uint16, uint16, uint8, uint8);
                case IsfInstruction.IGINIT:
                case IsfInstruction.IGRELEASE:
                    return data.ToArgs();
                case IsfInstruction.IHK:
                    return data.ToArgs(uint8,
                        value, value, value, value,
                        value, value, value, value
                    );
                case IsfInstruction.IHKDEF:
                    return data.ToArgs(value);
                case IsfInstruction.IHGL:
                    return data.ToArgs(cstring, value, value);
                case IsfInstruction.IHGC:
                    return data.ToArgs();
                case IsfInstruction.IHGP:
                    return data.ToArgs(value,
                        value, value, value, value,
                        value, value, value, value
                    );
                case IsfInstruction.CLK:
                    return data.ToArgs(uint8, value);
                case IsfInstruction.IGN:
                    return data.ToArgs(value);
                case IsfInstruction.DAE:
                    return data.ToArgs(value, value);
                case IsfInstruction.DAP:
                    return data.ToArgs(value, uint8, value);
                case IsfInstruction.DAS:
                    return data.ToArgs(value);
                case IsfInstruction.SETINSIDEVOL:
                    return data.ToArgs(uint8, value);
                case IsfInstruction.KIDCLR:
                    return data.ToArgs();
                case IsfInstruction.KIDMOJI:
                    return data.ToArgs(uint24, uint24);
                case IsfInstruction.KIDPAGE:
                    return data.ToArgs(uint24, uint24, uint16, uint8, uint16, uint16);
                case IsfInstruction.KIDSET:
                    return data.ToArgs(uint32);
                case IsfInstruction.KIDEND:
                    return data.ToArgs();
                case IsfInstruction.KIDFN:
                    return data.ToArgs(uint32);
                case IsfInstruction.KIDHABA:
                    return data.ToArgs(uint8, uint16, uint16);
                case IsfInstruction.KIDSCAN:
                    return data.ToArgs(uint16, value);
                case IsfInstruction.SETKIDWNDPUTPOS:
                case IsfInstruction.SETMESWNDPUTPOS:
                    return data.ToArgs(uint8, value, value, value, value);
                case IsfInstruction.MSGBOX:
                    return data.ToArgs(uint8, uint8, uint8, uint8, cstring, uint8);
                case IsfInstruction.SETSMPRATE:
                    return data.ToArgs(value);
                case IsfInstruction.CLKEXMCSET:
                    return data.ToArgs(value, value, value, value);
                case IsfInstruction.IRCLK:
                case IsfInstruction.IROPN:
                    return data.ToArgs();
                case IsfInstruction.MPM:
                    return data.ToUInt8(1) == 0
                        ? data.ToArgs(uint8, uint8)
                        : data.ToArgs(uint8, uint8, message);
                case IsfInstruction.MPC:
                    return data.ToArgs(uint8, uint8);
                case IsfInstruction.TAGSET:
                    return data.ToArgs(uint8, cstring);
                case IsfInstruction.FRAMESET:
                    return data.ToArgs(uint8, uint8, cstring);
                case IsfInstruction.RBSET:
                case IsfInstruction.CBSET:
                    return data.ToArgs(uint8, uint8, uint8, uint16, cstring);
                case IsfInstruction.SLDRSET:
                    return data.ToArgs(uint8, uint8, uint8, uint8,
                        cstring, cstring, cstring,
                        uint8, value, value, value, uint8, uint16, uint16
                    );
                case IsfInstruction.OPSL:
                    return data.ToArgs(uint8);
                case IsfInstruction.OPPROP:
                    return data.ToArgs();
                case IsfInstruction.DISABLE:
                case IsfInstruction.ENABLE:
                    return data.ToArgs(uint8, uint8, uint8);
                case IsfInstruction.EXT:
                    return data.ToArgs(uint8);
                case IsfInstruction.CNF:
                    return data.ToArgs(uint8, cstring);
                case IsfInstruction.ATIMES:
                    return data.ToArgs(value);
                case IsfInstruction.AWAIT:
                    return data.ToArgs();
                case IsfInstruction.AVIP:
                    return data.ToArgs(value, value, value, value, cstring);
                case IsfInstruction.PPF:
                case IsfInstruction.SVF:
                    return data.ToArgs(uint8);
                case IsfInstruction.SETGAMEINFO:
                    return data.ToArgs(cstring);
                case IsfInstruction.SETFONTSTYLE:
                    return data.ToArgs(uint8, uint8);
                case IsfInstruction.SETFONTCOLOR:
                    return data.ToArgs(uint8, uint8, uint24);
                case IsfInstruction.TIMERSET:
                    return data.ToArgs(value);
                case IsfInstruction.TIMEREND:
                    return data.ToArgs();
                case IsfInstruction.TIMERGET:
                    return data.ToArgs(uint16);
                case IsfInstruction.GRPOUT:
                    return data.ToArgs(value, cstring, uint8, uint8, cstring, cstring);
                case IsfInstruction.EXT_:
                    return data.ToArgs();
                default:
                    return data.ToArgs();
            }
        }

        private static object[] ToArgs(this byte[] data, params Func<byte[], int, object>[] readers)
        {
            var args = new List<object>();
            var pos = 0;
            foreach (var reader in readers)
            {
                if (pos >= data.Length) throw new NotSupportedException("offset exceeds length");
                var arg = reader.Invoke(data, pos);
                args.Add(arg);
                pos += arg.SizeForIsf();
            }

            if (pos != data.Length)
            {
                args.AddRange(data.Skip(pos).Cast<object>());
            }

            return args.ToArray();
        }

        private static object[] ToArgs(this string text, Encoding encoding)
        {
            if (text.Trim() == "") return Array.Empty<object>();
            var parts = text.Split(',');
            var args = new object[parts.Length];
            for (var i = 0; i < args.Length; i++)
            {
                args[i] = parts[i].Trim().ToArg(encoding);
            }

            return args;
        }

        private static int SizeForIsf(this object arg)
        {
            switch (arg)
            {
                case byte _:
                    return 1;
                case ushort _:
                    return 2;
                case uint _:
                    return 4;
                case IIsfData block:
                    return block.Size;
                default:
                    throw new NotSupportedException($"{arg.GetType()} size is unknown.");
            }
        }

        private static T[] Slice<T>(this T[] sourceArray, int start, int end)
        {
            var size = end - start;
            var destinationArray = new T[size];
            Array.Copy(sourceArray, start, destinationArray, 0, size);
            return destinationArray;
        }

        #region ISF Data

        private static byte ToUInt8<Bs>(this Bs bytes, int index) where Bs : IList<byte>
        {
            return bytes[index];
        }

        private static UInt24 ToUInt24<Bs>(this Bs bytes, int index) where Bs : IList<byte>
        {
            return new UInt24 { Bytes = bytes.Skip(index).Take(3).ToArray() };
        }

        private static CString ToCString<Bs>(this Bs bytes, int index) where Bs : IList<byte>
        {
            var offset = index;
            while (offset < bytes.Count)
            {
                if (bytes[offset++] == 0x00) break;
            }

            switch (bytes)
            {
                case byte[] array:
                    return new CString { Bytes = array.Slice(index, offset) };
                default:
                    return new CString { Bytes = bytes.Take(offset).Skip(index).ToArray() };
            }
        }

        private static IsfString ToIsfString<Bs>(this Bs bytes, int index) where Bs : IList<byte>
        {
            var offset = index;
            while (offset < bytes.Count)
            {
                if (bytes[offset] == 0x00)
                {
                    offset++;
                    break;
                }

                if (bytes[offset] == 0x5C)
                {
                    offset++;
                    if (bytes[offset] != 0) offset++;
                    offset++;
                    continue;
                }

                offset += bytes[offset] >= 0x7F ? 2 : 1;
            }

            switch (bytes)
            {
                case byte[] array:
                    return new IsfString { Bytes = array.Slice(index, offset) };
                default:
                    return new IsfString { Bytes = bytes.Take(offset).Skip(index).ToArray() };
            }
        }

        private static IsfLabel ToIsfLabel<Bs>(this Bs bytes, int index) where Bs : IList<byte>
        {
            return new IsfLabel { Index = bytes.ToUInt16(index) };
        }

        private static IsfValue ToIsfValue<Bs>(this Bs bytes, int index) where Bs : IList<byte>
        {
            return new IsfValue { Id = bytes.ToUInt32(index) };
        }

        private static IsfTable ToIsfTable<Bs>(this Bs bytes, int index) where Bs : IList<byte>
        {
            var pos = index;
            var id = bytes.ToUInt32(pos);
            pos += 4;
            var size = bytes.ToUInt8(pos);
            pos += 1;
            var arr = new ushort[size];
            for (var i = 0; i < size; i++)
            {
                arr[i] = bytes.ToUInt16(pos);
                pos += 2;
            }

            return new IsfTable { Value = id, Labels = arr };
        }

        private static IsfMessage ToIsfMessage<Bs>(this Bs bytes, int index) where Bs : IList<byte>
        {
            var pos = index;
            var actions = new List<KeyValuePair<byte, object[]>>();
            while (pos < bytes.Count)
            {
                var type = bytes.ToUInt8(pos);
                pos += 1;
                var args = new List<object>();
                switch (type)
                {
                    case 0x00:
                        break;
                    case 0x01:
                        args.Add(bytes.ToUInt8(pos));
                        pos += 1;
                        args.Add(bytes.ToUInt8(pos));
                        pos += 1;
                        args.Add(bytes.ToUInt8(pos));
                        pos += 1;
                        args.Add(bytes.ToUInt8(pos));
                        pos += 1;
                        break;
                    case 0x02:
                    case 0x03:
                        break;
                    case 0x04:
                        args.Add(bytes.ToUInt8(pos));
                        pos += 1;
                        break;
                    case 0x05:
                    case 0x06:
                        break;
                    case 0x07:
                        args.Add(bytes.ToUInt8(pos));
                        pos += 1;
                        break;
                    case 0x08:
                        args.Add(bytes.ToIsfValue(pos));
                        pos += 4;
                        break;
                    case 0x09:
                        args.Add(bytes.ToUInt8(pos));
                        pos += 1;
                        break;
                    case 0x0A:
                        args.Add(bytes.ToUInt16(pos));
                        pos += 2;
                        args.Add(bytes.ToUInt8(pos));
                        pos += 1;
                        args.Add(bytes.ToUInt8(pos));
                        pos += 1;
                        break;
                    case 0x0B:
                    case 0x0C:
                    case 0x10:
                        args.Add(bytes.ToUInt8(pos));
                        pos += 1;
                        args.Add(bytes.ToUInt8(pos));
                        pos += 1;
                        break;
                    case 0x11:
                        args.Add(bytes.ToIsfValue(pos));
                        pos += 4;
                        break;
                    case 0xFF:
                        var str = bytes.ToIsfString(pos);
                        args.Add(str);
                        pos += str.Size;
                        break;
                }

                actions.Add(new KeyValuePair<byte, object[]>(type, args.ToArray()));
            }

            return new IsfMessage { Actions = actions.ToArray() };
        }

        private static IsfCondition ToIsfCondition<Bs>(this Bs bytes, int index) where Bs : IList<byte>
        {
            var pos = index;
            var terms = new List<IsfCondition.Term>();
            var action = new KeyValuePair<byte, object[]>(0xFF, null);
            while (pos < bytes.Count)
            {
                var l = bytes.ToUInt32(pos);
                pos += 4;
                var c = bytes.ToUInt8(pos);
                pos += 1;
                var r = bytes.ToUInt32(pos);
                pos += 4;
                terms.Add(new IsfCondition.Term { L = l, C = c, R = r });

                var op = bytes.ToUInt8(pos);
                pos += 1;
                switch (op)
                {
                    case 0x00:
                        // JP
                        var jp = IsfInstruction.JP.Parse(bytes.Skip(pos).Take(2).ToArray()).ToArray();
                        action = new KeyValuePair<byte, object[]>(op, jp);
                        pos += 2;
                        break;
                    case 0x01:
                        // HS
                        var hs = IsfInstruction.HS.Parse(bytes.Skip(pos).Take(6).ToArray()).ToArray();
                        action = new KeyValuePair<byte, object[]>(op, hs);
                        pos += 6;
                        break;
                    case 0xFF:
                        // END
                        pos -= 1;
                        break;
                    default:
                        // AND
                        continue;
                }

                var end = bytes.ToUInt8(pos);
                if (end == 0xFF) break;
            }

            return new IsfCondition { Terms = terms.ToArray(), Action = action };
        }

        private static IsfAssignment ToIsfAssignment<Bs>(this Bs bytes, int index) where Bs : IList<byte>
        {
            var pos = index;
            var id = bytes.ToUInt16(index);
            pos += 2;
            var terms = new List<KeyValuePair<byte, uint>>();
            while (pos < bytes.Count)
            {
                var op = bytes.ToUInt8(pos);
                pos += 1;
                if (op >= 5) break;
                var value = bytes.ToUInt32(pos);
                pos += 4;
                terms.Add(new KeyValuePair<byte, uint>(op, value));
            }

            return new IsfAssignment { Variable = id, Terms = terms.ToArray() };
        }

        internal interface IIsfData
        {
            int Size { get; }
        }

        internal struct UInt24 : IIsfData
        {
            public byte[] Bytes;

            public int Size => 3;

            public uint Value => (uint)Bytes.ToInt24(0);

            public override string ToString()
            {
                return $"0x{Value:X6}";
            }

            public static UInt24 Parse(string s)
            {
                return new UInt24
                {
                    Bytes = new[]
                    {
                        byte.Parse(s.Substring(6, 2), NumberStyles.HexNumber),
                        byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber),
                        byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber),
                    }
                };
            }
        }

        internal struct CString : IIsfData
        {
            public byte[] Bytes;

            public int Size => Bytes.Length;

            public string ToString(Encoding encoding)
            {
                var count = Bytes.Length;
                while (count > 0 && Bytes[count - 1] == 0x00) count--;
                return encoding.GetString(Bytes, 0, count);
            }

            public override string ToString()
            {
                return ToString(Encoding.UTF8);
            }
        }

        internal struct IsfString : IIsfData
        {
            public byte[] Bytes;

            public int Size => Bytes.Length;

            public CString Decode()
            {
                var buffer = new List<byte>();
                var offset = 0;
                while (offset < Bytes.Length)
                {
                    switch (Bytes[offset])
                    {
                        case 0x00:
                            buffer.Add(0);
                            offset += 1;
                            continue;
                        case 0x5C:
                            buffer.Add(IsfKana[0xB8]);
                            offset += 1;
                            if (Bytes[offset] != 0x00) offset += 1;
                            buffer.Add(IsfKana[Bytes[offset - 1] * 2 + 1]);
                            offset += 1;
                            continue;
                        case 0x7F:
                            buffer.Add(Bytes[offset + 1]);
                            offset += 2;
                            continue;
                        default:
                            if (Bytes[offset] > 0x7F)
                            {
                                buffer.Add(Bytes[offset]);
                                buffer.Add(Bytes[offset + 1]);
                                offset += 2;
                            }
                            else
                            {
                                buffer.Add(IsfKana[Bytes[offset] * 2]);
                                buffer.Add(IsfKana[Bytes[offset] * 2 + 1]);
                                offset += 1;
                            }

                            continue;
                    }
                }

                return new CString { Bytes = buffer.ToArray() };
            }

            public static IsfString Encode(CString s)
            {
                var buffer = new List<byte>(s.Bytes.Length);
                for (var i = 0; i < s.Bytes.Length; i++)
                {
                    if (s.Bytes[i] == 0x00)
                    {
                        buffer.Add(0x00);
                        continue;
                    }

                    if (s.Bytes[i] <= 0x7F)
                    {
                        buffer.Add(0x7F);
                        buffer.Add(s.Bytes[i]);
                        continue;
                    }

                    var index = (byte)0;
                    for (var j = 2; j < 0x7F * 2; j += 2)
                    {
                        if (s.Bytes[i] != IsfKana[j] || s.Bytes[i + 1] != IsfKana[j + 1]) continue;
                        index = (byte)(j / 2);
                        break;
                    }

                    if (index != 0)
                    {
                        buffer.Add(index);
                        if (index == 0x5C) buffer.Add(0x00);
                    }
                    else
                    {
                        buffer.Add(s.Bytes[i]);
                        buffer.Add(s.Bytes[i + 1]);
                    }

                    i++;
                }

                return new IsfString { Bytes = buffer.ToArray() };
            }

            public override string ToString()
            {
                return Decode().ToString();
            }
        }

        internal struct IsfLabel : IIsfData
        {
            public ushort Index;

            public int Size => 2;

            public override string ToString()
            {
                return $"LABEL_{Index}";
            }

            public static IsfLabel Parse(string s)
            {
                return new IsfLabel { Index = ushort.Parse(s.Substring(6)) };
            }
        }

        internal struct IsfValue : IIsfData
        {
            public uint Id;

            public int Size => 4;

            public override string ToString()
            {
                var value = (int)(Id << 2) >> 2;
                var type = Id >> 30;
                switch (type)
                {
                    case 0:
                        return $"{value}";
                    case 1:
                        return $"RAND({value})";
                    default:
                        return $"&{value:X4}";
                }
            }

            public static IsfValue Parse(string s)
            {
                int value;
                uint type;
                switch (s[0])
                {
                    case 'R':
                        value = int.Parse(s.Substring(5, s.Length - 6));
                        type = 1;
                        break;
                    case '&':
                        value = int.Parse(s.Substring(1), NumberStyles.HexNumber);
                        type = 2;
                        break;
                    default:
                        value = int.Parse(s);
                        type = 0;
                        break;
                }

                var id = (type << 30) | (uint)value >> 31 << 29 | (uint)value << 3 >> 3;
                return new IsfValue { Id = id };
            }
        }

        internal struct IsfTable : IIsfData
        {
            public uint Value;
            public ushort[] Labels;

            public int Size => 4 + 1 + Labels.Length * 2;

            public override string ToString()
            {
                var value = new IsfValue { Id = Value };
                var builder = new StringBuilder();
                builder.Append(value);
                builder.Append(", [");
                builder.Append(string.Join(", ", Labels.Select(i => new IsfLabel { Index = i })));
                builder.Append("]");
                return builder.ToString();
            }

            public static IsfTable Parse(string s)
            {
                var part = s.Split('[', ']');
                var value = IsfValue.Parse(part[0].Trim(' ', ','));
                var labels = part[1].ToArgs(Encoding.ASCII).Cast<IsfLabel>().ToArray();
                var ids = new ushort[labels.Length];
                for (var i = 0; i < labels.Length; i++)
                {
                    ids[i] = labels[i].Index;
                }

                return new IsfTable { Value = value.Id, Labels = ids };
            }
        }

        internal struct IsfMessage : IIsfData
        {
            public KeyValuePair<byte, object[]>[] Actions;

            public int Size => Actions.Sum(action =>
            {
                switch (action.Key)
                {
                    case 0x01:
                        return 1 + 4;
                    case 0x04:
                        return 1 + 1;
                    case 0x07:
                        return 1 + 1;
                    case 0x08:
                        return 1 + 4;
                    case 0x09:
                        return 1 + 1;
                    case 0x0A:
                        return 1 + 4;
                    case 0x0B:
                    case 0x0C:
                    case 0x10:
                        return 1 + 2;
                    case 0x11:
                        return 1 + 4;
                    case 0xFF:
                        return 1 + ((IsfString)action.Value[0]).Size;
                    default:
                        return 1;
                }
            });
        }

        internal struct IsfCondition : IIsfData
        {
            public struct Term
            {
                public uint L;
                public byte C;
                public uint R;

                public override string ToString()
                {
                    var a = new IsfValue { Id = L };
                    var b = new IsfValue { Id = R };
                    switch (C)
                    {
                        case 0x00:
                            return $"{a} == {b}";
                        case 0x01:
                            return $"{a} < {b}";
                        case 0x02:
                            return $"{a} <= {b}";
                        case 0x03:
                            return $"{a} > {b}";
                        case 0x04:
                            return $"{a} >= {b}";
                        case 0x05:
                            return $"{a} != {b}";
                        default:
                            return "FALSE";
                    }
                }

                public static Term Parse(string s)
                {
                    var match = Regex.Match(s, @"\s*(\S+)\s*(\S+)\s*(\S+)\s*");
                    var l = IsfValue.Parse(match.Groups[1].Value);
                    var r = IsfValue.Parse(match.Groups[3].Value);
                    byte c;
                    switch (match.Groups[2].Value)
                    {
                        case "==":
                            c = 0x00;
                            break;
                        case "<":
                            c = 0x01;
                            break;
                        case "<=":
                            c = 0x02;
                            break;
                        case ">":
                            c = 0x03;
                            break;
                        case ">=":
                            c = 0x04;
                            break;
                        case "!=":
                            c = 0x05;
                            break;
                        default:
                            c = 0xFF;
                            break;
                    }

                    return new Term { L = l.Id, C = c, R = r.Id };
                }
            }

            public Term[] Terms;
            public KeyValuePair<byte, object[]> Action;

            private int ActionSize()
            {
                switch (Action.Key)
                {
                    case 0x00:
                        return 1 + 2;
                    case 0x01:
                        return 1 + 6;
                    default:
                        return 1;
                }
            }

            public int Size => Terms.Length * 10 + ActionSize();
        }

        internal struct IsfAssignment : IIsfData
        {
            public ushort Variable;
            public KeyValuePair<byte, uint>[] Terms;

            public int Size => 2 + Terms.Length * 5 + 1;

            public override string ToString()
            {
                var builder = new StringBuilder();
                builder.Append($"&{Variable:X4} =");
                foreach (var term in Terms)
                {
                    builder.Append(" ");
                    switch (term.Key)
                    {
                        case 0x00:
                            builder.Append("+");
                            break;
                        case 0x01:
                            builder.Append("-");
                            break;
                        case 0x02:
                            builder.Append("*");
                            break;
                        case 0x03:
                            builder.Append("/");
                            break;
                        case 0x04:
                            builder.Append("%");
                            break;
                        default:
                            throw new FormatException($"0x{term.Key:X2}");
                    }

                    builder.Append(" ");
                    builder.Append(new IsfValue { Id = term.Value });
                }

                return builder.ToString();
            }

            public static IsfAssignment Parse(string s)
            {
                var variable = ushort.Parse(s.Substring(1, 4), NumberStyles.HexNumber);
                var terms = new List<KeyValuePair<byte, uint>>();
                var match = Regex.Match(s, @"\s+([^=])\s+(\S+)");
                while (match.Success)
                {
                    byte op;
                    switch (match.Groups[1].Value)
                    {
                        case "+":
                            op = 0x00;
                            break;
                        case "-":
                            op = 0x01;
                            break;
                        case "*":
                            op = 0x02;
                            break;
                        case "/":
                            op = 0x03;
                            break;
                        case "%":
                            op = 0x04;
                            break;
                        default:
                            op = 0xFF;
                            break;
                    }

                    var value = IsfValue.Parse(match.Groups[2].Value);
                    terms.Add(new KeyValuePair<byte, uint>(op, value.Id));

                    match = match.NextMatch();
                }

                return new IsfAssignment { Variable = variable, Terms = terms.ToArray() };
            }
        }


        [SuppressMessage("ReSharper", "UnusedMember.Local")]
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        [SuppressMessage("ReSharper", "IdentifierTypo")]
        internal enum IsfInstruction
        {
            ED = 0x00, // End
            LS = 0x01, // Execute scenario load
            LSBS = 0x02, // Execute sub-scenario load
            SRET = 0x03, // Return from sub-scenario
            JP = 0x04, // Jump
            JS = 0x05, // Subroutine jump
            RT = 0x06, // Return from subroutine
            ONJP = 0x07, // Conditional jump
            ONJS = 0x08, // Conditional subroutine call
            CHILD = 0x09, // Execute child process
            URL = 0x0A,
            UNK_0B = 0x0B,
            UNK_0C = 0x0C,
            UNK_0D = 0x0D,
            UNK_0E = 0x0E,
            UNK_0F = 0x0F,
            CW = 0x10, // Set command window position and width
            CP = 0x11, // Load command window frame
            CIR = 0x12, // Load icon
            CPS = 0x13, // Set character palette
            CIP = 0x14, // Set icon to command
            CSET = 0x15, // Set command name
            CWO = 0x16, // Open command window
            CWC = 0x17, // Close command window
            CC = 0x18, // Execute command selection
            CCLR = 0x19,
            CRESET = 0x1A, // Prepare for setting command name
            CRND = 0x1B, // Randomly arrange commands
            CTEXT = 0x1C, // Display text
            UNK_1D = 0x1D,
            UNK_1E = 0x1E,
            UNK_1F = 0x1F,
            WS = 0x20, // Set window display position
            WP = 0x21, // Load window parts
            WL = 0x22, // Load click-wait parts
            WW = 0x23, // Set click-wait
            CN = 0x24, // Set character name length
            CNS = 0x25, // Set character name
            PF = 0x26, // Set message display speed
            PB = 0x27, // Specify character size
            PJ = 0x28, // Set character style
            WO = 0x29, // Open window
            WC = 0x2A, // Close window
            PM = 0x2B, // Display character
            PMP = 0x2C, // Display character with voice flag check
            WSH = 0x2D, // Hide message window
            WSS = 0x2E, // Show message window
            UNK_2F = 0x2F,
            FLN = 0x30, // Set number of flags
            SK = 0x31, // Set, clear, or invert flag
            SKS = 0x32, // Set, clear, or invert flags in batch
            HF = 0x33, // Flag judgment jump
            FT = 0x34, // Flag transfer
            SP = 0x35, // Set pattern flag
            HP = 0x36, // Pattern flag judgment jump
            STS = 0x37, // Set system flag
            ES = 0x38, // Save specified flag
            EC = 0x39, // Load specified flag
            STC = 0x3A, // System flag judgment jump
            HN = 0x3B, // Flag judgment jump
            HXP = 0x3C, // Pattern flag judgment jump 2
            UNK_3D = 0x3D,
            UNK_3E = 0x3E,
            UNK_3F = 0x3F,
            HLN = 0x40, // Set number of variables
            HS = 0x41, // Assign value to variable
            HINC = 0x42, // Increment variable
            HDEC = 0x43, // Decrement variable
            CALC = 0x44, // Calculate
            HSG = 0x45, // Assign values to variables in batch
            HT = 0x46, // Transfer variable
            IF = 0x47, // Execute IF-THEN
            EXA = 0x48, // Reserve area to separately store flags and variables
            EXS = 0x49, // Write specified flag/variable to area reserved by EXA command
            EXC = 0x4A, // Read specified flag/variable from area reserved by EXA command
            SCP = 0x4B, // Copy system variable
            SSP = 0x4C, // Copy parameter to system variable
            UNK_4D = 0x4D,
            UNK_4E = 0x4E,
            UNK_4F = 0x4F,
            VSET = 0x50, // Set virtual VRAM
            GN = 0x51, // Turn on graphic display
            GF = 0x52, // Turn off graphic display
            GC = 0x53, // Clear graphics
            GI = 0x54, // Graphic fade-in
            GO = 0x55, // Graphic fade-out
            GL = 0x56, // Load and display graphic
            GP = 0x57, // Copy graphic
            GB = 0x58, // Draw rectangle
            GPB = 0x59, // Set character size
            GPJ = 0x5A, // Set character style
            PR = 0x5B, // Display character
            GASTAR = 0x5C, // Start animation
            GASTOP = 0x5D, // Stop animation
            GPI = 0x5E, // Fade-in graphic effect and BGM
            GPO = 0x5F, // Fade-out graphic effect and BGM
            GGE = 0x60, // Effect using grayscale
            GPE = 0x61, // Zoom in/out processing
            GSCRL = 0x62, // Scroll processing
            GV = 0x63, // Screen shake processing
            GAL = 0x64, // Set animation loop
            GAOPEN = 0x65, // Open animation file
            GASET = 0x66, // Set animation data
            GAPOS = 0x67, // Set animation display position
            GACLOSE = 0x68, // Close animation file
            GADELETE = 0x69, // Delete animation
            UNK_6A = 0x6A,
            UNK_6B = 0x6B,
            UNK_6C = 0x6C,
            UNK_6D = 0x6D,
            UNK_6E = 0x6E,
            SGL = 0x6F, // Load save image
            ML = 0x70, // Load/play music data
            MP = 0x71, // Play music
            MF = 0x72, // Music fade-out
            MS = 0x73, // Stop music
            SER = 0x74, // Load sound effect
            SEP = 0x75, // Play sound effect
            SED = 0x76, // Delete sound effect
            PCMON = 0x77, // Play PCM audio
            PCML = 0x78, // Load PCM
            PCMS = 0x79, // Stop PCM
            PCMEND = 0x7A, // Wait for PCM audio to stop
            SES = 0x7B, // Stop SES sound effect
            BGMGETPOS = 0x7C, // Get music playback position
            SEGETPOS = 0x7D, // Get sound effect playback position
            PCMGETPOS = 0x7E, // Get PCM playback position
            PCMCN = 0x7F, // Backup audio file name
            IM = 0x80, // Load mouse cursor data
            IC = 0x81, // Change mouse cursor
            IMS = 0x82, // Set mouse movement range
            IXY = 0x83, // Change mouse position
            IH = 0x84, // Set IG command selection range
            IG = 0x85, // Mouse input within screen
            IGINIT = 0x86, // Mouse input within screen - initialize
            IGRELEASE = 0x87, // Mouse input within screen - release
            IHK = 0x88, // Keyboard extension - set destination data
            IHKDEF = 0x89, // Keyboard extension - set default number
            IHGL = 0x8A, // Load selected layout image
            IHGC = 0x8B, // Zero clear selected layout
            IHGP = 0x8C, // Transfer specified image
            CLK = 0x8D, // Wait for click
            IGN = 0x8E, // Get cursor NO
            UNK_8F = 0x8F,
            DAE = 0x90, // Set CDDA
            DAP = 0x91, // Play CDDA
            DAS = 0x92, // Stop CDDA
            UNK_93 = 0x93,
            UNK_94 = 0x94,
            UNK_95 = 0x95,
            UNK_96 = 0x96,
            UNK_97 = 0x97,
            UNK_98 = 0x98,
            UNK_99 = 0x99,
            UNK_9A = 0x9A,
            UNK_9B = 0x9B,
            UNK_9C = 0x9C,
            UNK_9D = 0x9D,
            UNK_9E = 0x9E,
            SETINSIDEVOL = 0x9F, // Set internal volume
            KIDCLR = 0xA0, // Initialize read text
            KIDMOJI = 0xA1, // Set color of read text characters
            KIDPAGE = 0xA2, // Page information of read text
            KIDSET = 0xA3, // Read flag judgment for read text
            KIDEND = 0xA4, // Set read flag for read text
            KIDFN = 0xA5, // Set number of read flags
            KIDHABA = 0xA6, // Number of characters per line in read text
            KIDSCAN = 0xA7, // Read function and read flag judgment
            UNK_A8 = 0xA8,
            UNK_A9 = 0xA9,
            UNK_AA = 0xAA,
            UNK_AB = 0xAB,
            UNK_AC = 0xAC,
            UNK_AD = 0xAD,
            SETKIDWNDPUTPOS = 0xAE, // Specify put position of read window
            SETMESWNDPUTPOS = 0xAF, // Specify put position of message window
            INNAME = 0xB0,
            NAMECOPY = 0xB1,
            CHANGEWALL = 0xB2,
            MSGBOX = 0xB3, // Display message box
            SETSMPRATE = 0xB4, // Set sampling rate
            UNK_B5 = 0xB5,
            UNK_B6 = 0xB6,
            UNK_B7 = 0xB7,
            UNK_B8 = 0xB8,
            UNK_B9 = 0xB9,
            UNK_BA = 0xBA,
            UNK_BB = 0xBB,
            UNK_BC = 0xBC,
            CLKEXMCSET = 0xBD, // Initialize mouse cursor ID for click-wait extended function
            IRCLK = 0xBE, //
            IROPN = 0xBF, //
            UNK_C0 = 0xC0,
            UNK_C1 = 0xC1,
            UNK_C2 = 0xC2,
            UNK_C3 = 0xC3,
            UNK_C4 = 0xC4,
            UNK_C5 = 0xC5,
            UNK_C6 = 0xC6,
            UNK_C7 = 0xC7,
            UNK_C8 = 0xC8,
            UNK_C9 = 0xC9,
            UNK_CA = 0xCA,
            UNK_CB = 0xCB,
            UNK_CC = 0xCC,
            UNK_CD = 0xCD,
            UNK_CE = 0xCE,
            UNK_CF = 0xCF,
            PPTL = 0xD0,
            PPABL = 0xD1,
            PPTYPE = 0xD2,
            PPORT = 0xD3,
            PPCRT = 0xD4,
            SABL = 0xD5,
            MPM = 0xD6, // Execute simultaneous multi-line display
            MPC = 0xD7, // Discard registered line
            PM2 = 0xD8,
            MPM2 = 0xD9,
            UNK_DA = 0xDA,
            UNK_DB = 0xDB,
            UNK_DC = 0xDC,
            UNK_DD = 0xDD,
            UNK_DE = 0xDE,
            UNK_DF = 0xDF,
            TAGSET = 0xE0, // Set dialog tag
            FRAMESET = 0xE1, // Set dialog frame
            RBSET = 0xE2, // Set dialog radio button
            CBSET = 0xE3, // Set dialog checkbox
            SLDRSET = 0xE4, // Set dialog slider
            OPSL = 0xE5, // Open SAVE/LOAD dialog
            OPPROP = 0xE6, // Open settings dialog
            DISABLE = 0xE7, // Disable dialog control
            ENABLE = 0xE8, // Enable dialog control
            TITLE = 0xE9,
            UNK_EA = 0xEA,
            UNK_EB = 0xEB,
            UNK_EC = 0xEC,
            UNK_ED = 0xED,
            UNK_EE = 0xEE,
            EXT = 0xEF, // Extended processing
            CNF = 0xF0, // Set concatenated file name
            ATIMES = 0xF1, // Start wait
            AWAIT = 0xF2, // Wait
            AVIP = 0xF3, // Play AVI file
            PPF = 0xF4, // Set popup menu display
            SVF = 0xF5, // Set save enable/disable
            PPE = 0xF6, // Set popup menu prohibit/allow display
            SETGAMEINFO = 0xF7, // Set in-game information
            SETFONTSTYLE = 0xF8, // Specify display font style
            SETFONTCOLOR = 0xF9, // Specify display font color
            TIMERSET = 0xFA, // Set time counter
            TIMEREND = 0xFB, // End time counter
            TIMERGET = 0xFC, // Get time counter
            GRPOUT = 0xFD, // Output image
            BREAK = 0xFE, // Break
            EXT_ = 0xFF, // Extended processing

        }

        internal static byte[] IsfKana =
        {
            0x81, 0x40, 0x81, 0x40, 0x81, 0x41, 0x81, 0x42,
            0x81, 0x45, 0x81, 0x48, 0x81, 0x49, 0x81, 0x69,
            0x81, 0x6a, 0x81, 0x75, 0x81, 0x76, 0x82, 0x4f,
            0x82, 0x50, 0x82, 0x51, 0x82, 0x52, 0x82, 0x53,
            0x82, 0x54, 0x82, 0x55, 0x82, 0x56, 0x82, 0x57,
            0x82, 0x58, 0x82, 0xa0, 0x82, 0xa2, 0x82, 0xa4,
            0x82, 0xa6, 0x82, 0xa8, 0x82, 0xa9, 0x82, 0xaa,
            0x82, 0xab, 0x82, 0xac, 0x82, 0xad, 0x82, 0xae,
            0x81, 0x40, 0x82, 0xb0, 0x82, 0xb1, 0x82, 0xb2,
            0x82, 0xb3, 0x82, 0xb4, 0x82, 0xb5, 0x82, 0xb6,
            0x82, 0xb7, 0x82, 0xb8, 0x82, 0xb9, 0x82, 0xba,
            0x82, 0xbb, 0x82, 0xbc, 0x82, 0xbd, 0x82, 0xbe,
            0x82, 0xbf, 0x82, 0xc0, 0x82, 0xc1, 0x82, 0xc2,
            0x82, 0xc3, 0x82, 0xc4, 0x82, 0xc5, 0x82, 0xc6,
            0x82, 0xc7, 0x82, 0xc8, 0x82, 0xc9, 0x82, 0xca,
            0x82, 0xcb, 0x82, 0xcc, 0x82, 0xcd, 0x82, 0xce,
            0x82, 0xd0, 0x82, 0xd1, 0x82, 0xd3, 0x82, 0xd4,
            0x82, 0xd6, 0x82, 0xd7, 0x82, 0xd9, 0x82, 0xda,
            0x82, 0xdc, 0x82, 0xdd, 0x82, 0xde, 0x82, 0xdf,
            0x82, 0xe0, 0x82, 0xe1, 0x82, 0xe2, 0x82, 0xe3,
            0x82, 0xe4, 0x82, 0xe5, 0x82, 0xe6, 0x82, 0xe7,
            0x82, 0xe8, 0x82, 0xe9, 0x82, 0xea, 0x82, 0xeb,
            0x82, 0xed, 0x82, 0xf0, 0x82, 0xf1, 0x83, 0x41,
            0x83, 0x43, 0x83, 0x45, 0x83, 0x47, 0x83, 0x49,
            0x83, 0x4a, 0x83, 0x4c, 0x83, 0x4e, 0x83, 0x50,
            0x83, 0x52, 0x83, 0x54, 0x83, 0x56, 0x83, 0x58,
            0x83, 0x5a, 0x83, 0x5c, 0x83, 0x5e, 0x83, 0x60,
            0x83, 0x62, 0x83, 0x63, 0x83, 0x65, 0x83, 0x67,
            0x83, 0x69, 0x83, 0x6a, 0x82, 0xaf, 0x83, 0x6c,
            0x83, 0x6d, 0x83, 0x6e, 0x83, 0x71, 0x83, 0x74,
            0x83, 0x77, 0x83, 0x7a, 0x83, 0x7d, 0x83, 0x7e,
            0x83, 0x80, 0x83, 0x81, 0x83, 0x82, 0x83, 0x84
        };

        #endregion

        internal struct IsfAction
        {
            public IsfInstruction Instruction;
            public object[] Args;
        }

        internal class IsfAssembler
        {
            internal ushort Version;
            internal byte Key;
            internal Encoding Encoding;
            internal IsfAction[] Actions;
            internal int[] Labels;

            public override string ToString()
            {
                var builder = new StringBuilder();
                builder.AppendLine($"; version: {Version:X4}");
                builder.AppendLine($"; key: {Key:X2}");
                builder.AppendLine($"; encoding: {Encoding.WebName}");

                for (var i = 0; i < Actions.Length; i++)
                {
                    for (var j = 0; j < Labels.Length; j++)
                    {
                        if (Labels[j] != i) continue;
                        builder.AppendLine($"#LABEL_{j}: ");
                    }

                    var args = Actions[i].Args
                        .Select(arg => arg.ToText(Encoding))
                        .GetEnumerator();

                    // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                    switch (Actions[i].Instruction)
                    {
                        case IsfInstruction.PM:
                        case IsfInstruction.PMP:
                            builder.AppendLine($"    {Actions[i].Instruction} {Actions[i].Args[0].ToText(Encoding)}");
                            foreach (var action in ((IsfMessage)Actions[i].Args[1]).Actions)
                            {
                                builder.Append($"        {action.Key.ToText(Encoding)}");
                                foreach (var arg in action.Value)
                                {
                                    builder.Append(", ");
                                    builder.Append(arg.ToText(Encoding));
                                }

                                builder.AppendLine();
                            }

                            builder.AppendLine($"    END {Actions[i].Instruction}");
                            break;
                        case IsfInstruction.IF:
                            var condition = (IsfCondition)Actions[i].Args[0];
                            builder.AppendLine($"    IF {condition.Terms[0]}");
                            var terms = condition.Terms
                                .Skip(1)
                                .GetEnumerator();

                            while (terms.MoveNext())
                            {
                                builder.AppendLine($"        AND {terms.Current}");
                            }

                            switch (condition.Action.Key)
                            {
                                case 0:
                                    builder.Append("        JP");
                                    break;
                                case 1:
                                    builder.Append("        HS");
                                    break;
                            }

                            args = condition.Action.Value
                                .Select(arg => arg.ToText(Encoding))
                                .GetEnumerator();

                            if (args.MoveNext())
                            {
                                builder.Append(" ");
                                builder.Append(args.Current);
                            }

                            while (args.MoveNext())
                            {
                                builder.Append(", ");
                                builder.Append(args.Current);
                            }

                            builder.AppendLine();

                            builder.AppendLine("    END IF");
                            break;
                        case IsfInstruction.MPM:
                            builder.Append("    MPM ");
                            if ((byte)Actions[i].Args[1] == 0)
                            {
                                if (args.MoveNext())
                                {
                                    builder.Append(" ");
                                    builder.Append(args.Current);
                                }

                                while (args.MoveNext())
                                {
                                    builder.Append(", ");
                                    builder.Append(args.Current);
                                }

                                builder.AppendLine();
                                break;
                            }

                            builder.AppendLine($"0x{Actions[i].Args[0]:X2}, 0x{Actions[i].Args[1]:X2}");

                            foreach (var action in ((IsfMessage)Actions[i].Args[2]).Actions)
                            {
                                builder.Append($"        {action.Key.ToText(Encoding)}");
                                foreach (var arg in action.Value)
                                {
                                    builder.Append(", ");
                                    builder.Append(arg.ToText(Encoding));
                                }

                                builder.AppendLine();
                            }

                            builder.AppendLine("    END MPM");
                            break;
                        default:
                            builder.Append($"    {Actions[i].Instruction}");

                            if (args.MoveNext())
                            {
                                builder.Append(" ");
                                builder.Append(args.Current);
                            }

                            while (args.MoveNext())
                            {
                                builder.Append(", ");
                                builder.Append(args.Current);
                            }

                            builder.AppendLine();
                            break;
                    }
                }

                return builder.ToString();
            }
        }
    }
}