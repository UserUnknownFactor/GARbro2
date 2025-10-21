using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Text.RegularExpressions;

namespace GameRes.Formats.LiveMaker
{
    [Export (typeof (ScriptFormat))]
    public class ScriptLPM : ScriptFormat
    {
        public override string         Tag { get { return "LPM"; } }
        public override string Description { get { return "LiveMaker Preview Menu"; } }
        public override uint     Signature { get { return  0x6576694C; } } // 'Live'
        public override bool      CanWrite { get { return  true; } }

        const int DefaultLpmVersion = 106;

        public ScriptLPM()
        {
            Extensions = new[] { "lpm" };
        }

        public override bool IsScript (IBinaryStream file)
        {
            var signature = file.ReadBytes (12);
            return signature.AsciiEqual ("LivePrevMenu");
        }

        public override Stream ConvertFrom (IBinaryStream file)
        {
            file.Position = 0;
            var lpm = ParseLPM (file);
            var output = new MemoryStream();
            var writer = new StreamWriter (output, Encoding.UTF8, 4096, true);
            writer.WriteLine ($"# LivePrevMenu Version {lpm.Version}");
            writer.WriteLine ($"# Buttons: {lpm.Buttons.Count}");
            writer.WriteLine();

            for (int i = 0; i < lpm.Buttons.Count; i++)
            {
                var button = lpm.Buttons[i];
                writer.WriteLine ($"[Button {i}]");
                writer.WriteLine ($"Width={button.Width}");
                writer.WriteLine ($"Height={button.Height}");
                writer.WriteLine ($"Source={button.Source}");
                writer.WriteLine ($"Unk2={button.Unk2}");
                writer.WriteLine ($"Name={button.Name}");
                writer.WriteLine ($"SourceSelected={button.SourceSelected}");
                writer.WriteLine ($"Unk3={button.Unk3}");
                writer.WriteLine ($"Unk4={button.Unk4}");
                if (lpm.Version > 100)
                    writer.WriteLine ($"Unk5={button.Unk5}");
                if (lpm.Version > 102)
                {
                    writer.WriteLine ($"Unk6_1={button.Unk6_1}");
                    writer.WriteLine ($"Unk6_2={button.Unk6_2}");
                }
                writer.WriteLine ($"Unk7={button.Unk7}");
                writer.WriteLine ($"Unk8={button.Unk8}");
                writer.WriteLine ($"Unk9={button.Unk9}");
                if (lpm.Version > 101)
                {
                    writer.WriteLine ($"Unk10_1={button.Unk10_1}");
                    writer.WriteLine ($"Unk10_2={button.Unk10_2}");
                }
                writer.WriteLine ($"Unk15={button.Unk15}");
                writer.WriteLine ($"Unk16={button.Unk16}");
                writer.WriteLine ($"Unk17={button.Unk17}");
                if (lpm.Version > 103)
                {
                    writer.WriteLine ($"Unk18_1={button.Unk18_1}");
                    writer.WriteLine ($"Unk18_2={button.Unk18_2}");
                    writer.WriteLine ($"Unk18_3={button.Unk18_3}");
                    writer.WriteLine ($"Unk18_4={button.Unk18_4}");
                    writer.WriteLine ($"Unk18_5={button.Unk18_5}");
                    writer.WriteLine ($"Unk18_6={button.Unk18_6}");
                }
                if (lpm.Version > 104)
                    writer.WriteLine ($"Unk19={button.Unk19}");
                if (lpm.Version > 105)
                    writer.WriteLine ($"Unk20={button.Unk20}");
                writer.WriteLine();
            }

            writer.Flush();
            output.Position = 0;
            return output;
        }

        public override Stream ConvertBack (IBinaryStream file)
        {
            var text = Encoding.UTF8.GetString (file.ReadBytes((int)file.Length));
            var lpm = ParseTextFormat (text);
            return SerializeLPM (lpm);
        }

        private LPMData ParseTextFormat (string text)
        {
            var lpm = new LPMData { Version = DefaultLpmVersion };
            var lines = text.Split (new[] { "\r\n", "\n" }, StringSplitOptions.None);

            LPMButton currentButton = null;
            var versionRegex = new Regex(@"# LivePrevMenu Version (\d+)");

            foreach (var line in lines)
            {
                if (line.StartsWith ("# LivePrevMenu Version"))
                {
                    var match = versionRegex.Match (line);
                    if (match.Success)
                        lpm.Version = int.Parse (match.Groups[1].Value);
                }
                else if (line.StartsWith ("[Button"))
                {
                    if (currentButton != null)
                        lpm.Buttons.Add (currentButton);
                    currentButton = new LPMButton();
                }
                else if (line.Contains ("=") && currentButton != null)
                {
                    var parts = line.Split('=');
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim();
                        var value = parts[1].Trim();

                        switch (key)
                        {
                        case "Width": currentButton.Width = uint.Parse (value); break;
                        case "Height": currentButton.Height = uint.Parse (value); break;
                        case "Source": currentButton.Source = value; break;
                        case "Unk2": currentButton.Unk2 = byte.Parse (value); break;
                        case "Name": currentButton.Name = value; break;
                        case "SourceSelected": currentButton.SourceSelected = value; break;
                        case "Unk3": currentButton.Unk3 = value; break;
                        case "Unk4": currentButton.Unk4 = value; break;
                        case "Unk5": currentButton.Unk5 = value; break;
                        case "Unk6_1": currentButton.Unk6_1 = value; break;
                        case "Unk6_2": currentButton.Unk6_2 = value; break;
                        case "Unk7": currentButton.Unk7 = value; break;
                        case "Unk8": currentButton.Unk8 = value; break;
                        case "Unk9": currentButton.Unk9 = value; break;
                        case "Unk10_1": currentButton.Unk10_1 = value; break;
                        case "Unk10_2": currentButton.Unk10_2 = value; break;
                        case "Unk15": currentButton.Unk15 = uint.Parse (value); break;
                        case "Unk16": currentButton.Unk16 = uint.Parse (value); break;
                        case "Unk17": currentButton.Unk17 = value; break;
                        case "Unk18_1": currentButton.Unk18_1 = value; break;
                        case "Unk18_2": currentButton.Unk18_2 = value; break;
                        case "Unk18_3": currentButton.Unk18_3 = value; break;
                        case "Unk18_4": currentButton.Unk18_4 = value; break;
                        case "Unk18_5": currentButton.Unk18_5 = value; break;
                        case "Unk18_6": currentButton.Unk18_6 = uint.Parse (value); break;
                        case "Unk19": currentButton.Unk19 = value; break;
                        case "Unk20": currentButton.Unk20 = value; break;
                        }
                    }
                }
            }

            if (currentButton != null)
                lpm.Buttons.Add (currentButton);

            return lpm;
        }

        private Stream SerializeLPM (LPMData lpm)
        {
            var output = new MemoryStream();
            using (var writer = new BinaryWriter (output, Encoding.GetEncoding (932), true))
            {
                // Write signature
                writer.Write (Encoding.ASCII.GetBytes ("LivePrevMenu"));

                // Write version (3 bytes ASCII)
                writer.Write (Encoding.ASCII.GetBytes ($"{lpm.Version:D3}"));

                // Write unknown 8 bytes
                writer.Write (lpm.Unk1);

                // Write button count
                writer.Write (lpm.Buttons.Count);

                // Write buttons
                foreach (var button in lpm.Buttons)
                {
                    writer.Write (button.Width);
                    writer.Write (button.Height);
                    WritePascalString (writer, button.Source);
                    writer.Write (button.Unk2);
                    WritePascalString (writer, button.Name);
                    WritePascalString (writer, button.SourceSelected);
                    WritePascalString (writer, button.Unk3);
                    WritePascalString (writer, button.Unk4);

                    if (lpm.Version > 100)
                        WritePascalString (writer, button.Unk5);

                    if (lpm.Version > 102)
                    {
                        WritePascalString (writer, button.Unk6_1);
                        WritePascalString (writer, button.Unk6_2);
                    }

                    WritePascalString (writer, button.Unk7);
                    WritePascalString (writer, button.Unk8);
                    WritePascalString (writer, button.Unk9);

                    if (lpm.Version > 101)
                    {
                        WritePascalString (writer, button.Unk10_1);
                        WritePascalString (writer, button.Unk10_2);
                    }

                    writer.Write (button.Unk15);
                    writer.Write (button.Unk16);
                    WritePascalString (writer, button.Unk17);

                    if (lpm.Version > 103)
                    {
                        WritePascalString (writer, button.Unk18_1);
                        WritePascalString (writer, button.Unk18_2);
                        WritePascalString (writer, button.Unk18_3);
                        WritePascalString (writer, button.Unk18_4);
                        WritePascalString (writer, button.Unk18_5);
                        writer.Write (button.Unk18_6);
                    }

                    if (lpm.Version > 104)
                        WritePascalString (writer, button.Unk19);

                    if (lpm.Version > 105)
                        WritePascalString (writer, button.Unk20);
                }
            }

            output.Position = 0;
            return output;
        }

        public override ScriptData Read (string name, Stream file)
        {
            return Read (name, file, Encoding.GetEncoding (932));
        }

        public override ScriptData Read (string name, Stream file, Encoding encoding)
        {
            using (var input = BinaryStream.FromStream (file, name))
            {
                var lpm = ParseLPM (input);
                var script = new ScriptData();
                script.Type = ScriptType.TextData;

                uint lineId = 0;
                foreach (var button in lpm.Buttons)
                {
                    if (!string.IsNullOrEmpty (button.Name))
                    {
                        script.TextLines.Add (new ScriptLine (lineId++, button.Name));
                    }
                }

                return script;
            }
        }

        public override void Write (Stream file, ScriptData script)
        {
            throw new NotImplementedException ("Direct LPM writing not supported, use ConvertBack");
        }

        private LPMData ParseLPM (IBinaryStream input)
        {
            var data = new LPMData();

            // Read signature
            var signature = input.ReadBytes (12);
            if (!signature.AsciiEqual ("LivePrevMenu"))
                throw new InvalidFormatException ("Invalid LPM signature");

            // Read version (3 bytes ASCII)
            var versionBytes = input.ReadBytes (3);
            data.Version = int.Parse (Encoding.ASCII.GetString (versionBytes));

            // Unknown 8 bytes
            data.Unk1 = input.ReadBytes (8);

            // Read button count
            int buttonCount = input.ReadInt32();

            // Read buttons
            for (int i = 0; i < buttonCount; i++)
            {
                var button = new LPMButton();
                button.Width = input.ReadUInt32();
                button.Height = input.ReadUInt32();
                button.Source = ReadPascalString (input);
                button.Unk2 = (byte)input.ReadByte();
                button.Name = ReadPascalString (input);
                button.SourceSelected = ReadPascalString (input);
                button.Unk3 = ReadPascalString (input);
                button.Unk4 = ReadPascalString (input);

                if (data.Version > 100)
                    button.Unk5 = ReadPascalString (input);

                if (data.Version > 102)
                {
                    button.Unk6_1 = ReadPascalString (input);
                    button.Unk6_2 = ReadPascalString (input);
                }

                button.Unk7 = ReadPascalString (input);
                button.Unk8 = ReadPascalString (input);
                button.Unk9 = ReadPascalString (input);

                if (data.Version > 101)
                {
                    button.Unk10_1 = ReadPascalString (input);
                    button.Unk10_2 = ReadPascalString (input);
                }

                button.Unk15 = input.ReadUInt32();
                button.Unk16 = input.ReadUInt32();
                button.Unk17 = ReadPascalString (input);

                if (data.Version > 103)
                {
                    button.Unk18_1 = ReadPascalString (input);
                    button.Unk18_2 = ReadPascalString (input);
                    button.Unk18_3 = ReadPascalString (input);
                    button.Unk18_4 = ReadPascalString (input);
                    button.Unk18_5 = ReadPascalString (input);
                    button.Unk18_6 = input.ReadUInt32();
                }

                if (data.Version > 104)
                    button.Unk19 = ReadPascalString (input);

                if (data.Version > 105)
                    button.Unk20 = ReadPascalString (input);

                data.Buttons.Add (button);
            }

            return data;
        }

        private string ReadPascalString (IBinaryStream input)
        {
            uint length = input.ReadUInt32();
            if (length == 0)
                return string.Empty;
            var bytes = input.ReadBytes((int)length);
            return Encoding.GetEncoding (932).GetString (bytes);
        }

        private void WritePascalString (BinaryWriter writer, string str)
        {
            if (string.IsNullOrEmpty (str))
            {
                writer.Write((uint)0);
            }
            else
            {
                var bytes = Encoding.GetEncoding (932).GetBytes (str);
                writer.Write((uint)bytes.Length);
                writer.Write (bytes);
            }
        }

        private class LPMData
        {
            public int Version { get; set; }
            public byte[] Unk1 { get; set; } = new byte[8];
            public List<LPMButton> Buttons { get; } = new List<LPMButton>();
        }

        private class LPMButton
        {
            public uint Width { get; set; }
            public uint Height { get; set; }
            public string Source { get; set; } = "";
            public byte Unk2 { get; set; }
            public string Name { get; set; } = "";
            public string SourceSelected { get; set; } = "";
            public string Unk3 { get; set; } = "";
            public string Unk4 { get; set; } = "";
            public string Unk5 { get; set; } = "";
            public string Unk6_1 { get; set; } = "";
            public string Unk6_2 { get; set; } = "";
            public string Unk7 { get; set; } = "";
            public string Unk8 { get; set; } = "";
            public string Unk9 { get; set; } = "";
            public string Unk10_1 { get; set; } = "";
            public string Unk10_2 { get; set; } = "";
            public uint Unk15 { get; set; }
            public uint Unk16 { get; set; }
            public string Unk17 { get; set; } = "";
            public string Unk18_1 { get; set; } = "";
            public string Unk18_2 { get; set; } = "";
            public string Unk18_3 { get; set; } = "";
            public string Unk18_4 { get; set; } = "";
            public string Unk18_5 { get; set; } = "";
            public uint Unk18_6 { get; set; }
            public string Unk19 { get; set; } = "";
            public string Unk20 { get; set; } = "";
        }
    }
}