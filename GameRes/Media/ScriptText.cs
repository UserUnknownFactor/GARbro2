using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Xml.Linq;
using System.Xml;

namespace GameRes
{
    /// <summary>
    /// Represents a single line in a script file
    /// </summary>
    public class ScriptLine
    {
        public uint Id { get; set; }
        public string Text { get; set; }
        public string Speaker { get; set; }
        public Dictionary<string, object> Metadata { get; set; }

        public ScriptLine(uint id, string text, string speaker = null)
        {
            Id = id;
            Text = text ?? string.Empty;
            Speaker = speaker;
            Metadata = new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// Types of script content
    /// </summary>
    public enum ScriptType
    {
        Unknown,
        PlainText,
        Dialogue,
        TextData,
        BinaryScript,
        JsonScript,
        XmlScript
    }

    /// <summary>
    /// Container for script data
    /// </summary>
    public class ScriptData
    {
        public string RawText { get; private set; }
        public ScriptType Type { get; set; }
        public Encoding Encoding { get; set; }
        public string NewLineFormat { get; set; }
        public IList<ScriptLine> TextLines { get { return m_text; } }
        public Dictionary<string, object> Metadata { get; private set; }

        protected List<ScriptLine> m_text;

        public ScriptData()
        {
            RawText = string.Empty;
            Type = ScriptType.Unknown;
            Encoding = Encoding.UTF8;
            m_text = new List<ScriptLine>();
            Metadata = new Dictionary<string, object>();
        }

        public ScriptData(string text, ScriptType type = ScriptType.Unknown)
        {
            RawText = text ?? string.Empty;
            Type = type;
            Encoding = Encoding.UTF8;
            m_text = new List<ScriptLine>();
            Metadata = new Dictionary<string, object>();

            DetectNewLineFormat(text);

            if (!string.IsNullOrEmpty(text) && type == ScriptType.PlainText)
                ParsePlainText(text);
        }

        public ScriptData(IEnumerable<ScriptLine> lines, ScriptType type = ScriptType.Dialogue)
        {
            m_text = new List<ScriptLine>(lines);
            Type = type;
            Encoding = Encoding.UTF8;
            NewLineFormat = Environment.NewLine; // Default to system newline
            Metadata = new Dictionary<string, object>();
            RawText = string.Join(NewLineFormat, m_text.Select(l => l.Text));
        }

        /// <summary>
        /// Detects the newline format used in the text.
        /// </summary>
        protected virtual void DetectNewLineFormat(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                NewLineFormat = Environment.NewLine;
                return;
            }

            // Count occurrences of different newline formats
            int crlfCount = 0;
            int lfCount = 0;
            int crCount = 0;

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        crlfCount++;
                        i++; // Skip the \n
                    }
                    else
                        crCount++;
                }
                else if (text[i] == '\n')
                    lfCount++;
            }

            // Determine the predominant newline format
            if (crlfCount >= lfCount && crlfCount >= crCount)
                NewLineFormat = "\r\n";
            else if (lfCount >= crCount)
                NewLineFormat = "\n";
            else if (crCount > 0)
                NewLineFormat = "\r";
            else
                NewLineFormat = Environment.NewLine; // Default if no newlines found
        }

        protected virtual void ParsePlainText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            string[] lines;
            if (!string.IsNullOrEmpty(NewLineFormat) && text.Contains(NewLineFormat))
                lines = text.Split(new[] { NewLineFormat }, StringSplitOptions.None);
            else
                lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            uint id = 0;
            foreach (var line in lines)
            {
                m_text.Add(new ScriptLine(id++, line));
            }
        }

        public virtual void Serialize(Stream output)
        {
            using (var writer = new StreamWriter(output, Encoding, 1024, true))
            {
                if (m_text.Count > 0)
                {
                    for (int i = 0; i < m_text.Count; i++)
                    {
                        writer.Write(m_text[i].Text);

                        // Don't add newline after the last line unless original had it
                        if (i < m_text.Count - 1)
                            writer.Write(NewLineFormat);
                        else if (RawText.EndsWith(NewLineFormat) || RawText.EndsWith("\n"))
                            writer.Write(NewLineFormat);
                    }
                }
                else
                    writer.Write(RawText);
            }
        }

        public virtual void Deserialize(Stream input)
        {
            using (var reader = new StreamReader(input, Encoding, true, 1024, true))
            {
                RawText = reader.ReadToEnd();
                DetectNewLineFormat(RawText);
                ParsePlainText(RawText);
            }
        }

        /// <summary>
        /// Gets information about the newline format.
        /// </summary>
        public string GetNewLineInfo()
        {
            switch (NewLineFormat)
            {
                case "\r\n":  return "CRLF (Windows)";
                case "\n":    return "LF (Unix/Linux)";
                case "\r":    return "CR (Classic Mac)";
                default:      return "Unknown";
            }
        }
    }

    /// <summary>
    /// Base class for script format handlers
    /// </summary>
    public abstract class ScriptFormat : IResource
    {
        public override string        Type { get { return "script"; } }

        public virtual ScriptType DataType { get { return ScriptType.Unknown; } }

        /// <summary>
        /// Determines if the file is a valid script of this format.
        /// </summary>
        public abstract bool IsScript(IBinaryStream file);

        /// <summary>
        /// Converts script from game format to readable format.
        /// </summary>
        public abstract Stream ConvertFrom(IBinaryStream file);

        /// <summary>
        /// Converts script from readable format back to game format.
        /// </summary>
        public abstract Stream ConvertBack(IBinaryStream file);

        /// <summary>
        /// Reads and parses script data.
        /// </summary>
        public abstract ScriptData Read(string name, Stream file);

        /// <inheritdoc cref="Read(string, Stream)"/>
        public abstract ScriptData Read(string name, Stream file, Encoding encoding);

        /// <summary>
        /// Writes script data to stream.
        /// </summary>
        public abstract void Write(Stream file, ScriptData script);

        /// <summary>
        /// Detects encoding of text data.
        /// </summary>
        public static Encoding DetectEncoding(Stream data, long length = -1)
        {
            var pos = data.Position;
            if (length < 0 || length > data.Length)
                length = data.Length;
            var bytedata = new byte[length];
            data.Read(bytedata, 0, (int)length);
            data.Position = pos;
            return DetectEncoding(bytedata, length);
        }

        /// <summary>
        /// Detects encoding of text data.
        /// </summary>
        public static Encoding DetectEncoding(byte[] data, long length = -1)
        {
            if (data == null || data.Length == 0)
                return Encoding.Default;

            if (length < 0 || length > data.Length)
                length = data.Length;

            // Check for BOM (Byte Order Mark) first
            if (length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                return Encoding.UTF8;
            if (length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            {
                if (length >= 4 && data[2] == 0 && data[3] == 0)
                    return Encoding.UTF32; // UTF-32 LE
                return Encoding.Unicode; // UTF-16 LE
            }
            if (length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
                return Encoding.BigEndianUnicode; // UTF-16 BE
            if (length >= 4 && data[0] == 0 && data[1] == 0 && data[2] == 0xFE && data[3] == 0xFF)
                return new UTF32Encoding(true, true); // UTF-32 BE

            // For files without BOM, we need heuristics
            // Sample size for analysis (limit to avoid performance issues)
            int sampleSize = (int)Math.Min(length, 8192);

            // Check for UTF-16 without BOM by looking for null bytes pattern
            if (IsUtf16(data, sampleSize))
                return Encoding.Unicode;

            // Check for Shift-JIS before UTF-8 as some Shift-JIS can be misdetected as UTF-8
            if (IsShiftJis(data, sampleSize))
                return Encoding.GetEncoding(932);

            // Check for valid UTF-8
            if (IsValidUtf8(data, sampleSize))
                return Encoding.UTF8;

            // Default to ANSI (system default code page)
            return Encoding.Default;
        }

        private static bool IsValidUtf8(byte[] data, int length)
        {
            int i = 0;
            while (i < length)
            {
                byte b = data[i];

                // ASCII
                if (b <= 0x7F)
                {
                    i++;
                    continue;
                }

                // Multi-byte sequence
                int sequenceLength;
                if ((b & 0xE0) == 0xC0) sequenceLength = 2;
                else if ((b & 0xF0) == 0xE0) sequenceLength = 3;
                else if ((b & 0xF8) == 0xF0) sequenceLength = 4;
                else return false; // Invalid UTF-8 start byte

                if (i + sequenceLength > length)
                    return false;

                // Validate continuation bytes
                for (int j = 1; j < sequenceLength; j++)
                {
                    if ((data[i + j] & 0xC0) != 0x80)
                        return false;
                }

                i += sequenceLength;
            }

            return true;
        }

        private static bool IsUtf16(byte[] data, int length)
        {
            if (length < 2)
                return false;

            // Count null bytes in even and odd positions
            int evenNulls = 0;
            int oddNulls = 0;
            int nonAsciiChars = 0;

            for (int i = 0; i < length - 1; i += 2)
            {
                if (data[i] == 0) evenNulls++;
                if (data[i + 1] == 0) oddNulls++;

                // Check for non-ASCII characters
                if (data[i] > 0x7F || data[i + 1] > 0x7F)
                    nonAsciiChars++;
            }

            // If we have many nulls in one position but not the other, likely UTF-16
            int threshold = length / 8; // At least 25% of bytes should be null

            // Little Endian (more common on Windows)
            if (oddNulls > threshold && evenNulls < threshold / 4)
                return true;

            // Big Endian
            if (evenNulls > threshold && oddNulls < threshold / 4)
                return true;

            return false;
        }

        private static bool IsShiftJis(byte[] data, int length)
        {
            int validSequences = 0;
            int invalidSequences = 0;

            for (int i = 0; i < length; i++)
            {
                byte b = data[i];

                // Single-byte characters
                if (b <= 0x7F || (b >= 0xA1 && b <= 0xDF))
                    continue;

                // Two-byte character lead byte
                if ((b >= 0x81 && b <= 0x9F) || (b >= 0xE0 && b <= 0xFC))
                {
                    if (i + 1 < length)
                    {
                        byte b2 = data[i + 1];
                        if ((b2 >= 0x40 && b2 <= 0x7E) || (b2 >= 0x80 && b2 <= 0xFC))
                        {
                            validSequences++;
                            i++; // Skip the second byte
                        }
                        else
                            invalidSequences++;
                    }
                }
                else if (b >= 0x80)
                {
                    // High byte that's not valid in Shift-JIS
                    invalidSequences++;
                }
            }

            return validSequences > 0 && invalidSequences < validSequences / 4;
        }

        /// <summary>
        /// Finds appropriate script format handler for the given file.
        /// </summary>
        public static ScriptFormat FindFormat(IBinaryStream file)
        {
            foreach (var impl in FormatCatalog.Instance.FindFormats<ScriptFormat>(file.Name, file.Signature))
            {
                try
                {
                    file.Position = 0;
                    if (impl.IsScript(file))
                        return impl;
                }
                catch (System.OperationCanceledException)
                {
                    throw;
                }
                catch { }
            }
            return null;
        }
    }

    /// <summary>
    /// Base implementation for generic script formats.
    /// </summary>
    public abstract class GenericScriptFormat : ScriptFormat
    {
        public override bool IsScript(IBinaryStream file)
        {
            // Check extension
            var ext = Path.GetExtension(file.Name).TrimStart('.').ToLowerInvariant();
            return Extensions != null && Extensions.Contains(ext);
        }

        public override Stream ConvertFrom(IBinaryStream file)
        {
            return file.AsStream;
        }

        public override Stream ConvertBack(IBinaryStream file)
        {
            return file.AsStream;
        }

        public override ScriptData Read(string name, Stream file)
        {
            byte[] data;
            using (var ms = new MemoryStream())
            {
                file.CopyTo(ms);
                data = ms.ToArray();
            }

            var encoding = DetectEncoding(data);
            var text = encoding.GetString(data);

            // Remove BOM if present
            if (text.Length > 0 && text[0] == '\uFEFF')
                text = text.Substring(1);

            var scriptData = new ScriptData(text, GetScriptType(name)) { Encoding = encoding };
            return scriptData;
        }

        public override ScriptData Read(string name, Stream file, Encoding encoding)
        {
            byte[] data;
            using (var ms = new MemoryStream())
            {
                file.CopyTo(ms);
                data = ms.ToArray();
            }

            var text = encoding.GetString(data);

            // Remove BOM if present
            if (encoding.IsUtf16() && text.Length > 0 && text[0] == '\uFEFF')
                text = text.Substring(1);

            var scriptData = new ScriptData(text, GetScriptType(name)) { Encoding = encoding };
            return scriptData;
        }

        public override void Write(Stream file, ScriptData script)
        {
            script.Serialize(file);
        }

        protected virtual ScriptType GetScriptType(string filename)
        {
            return DataType;
        }
    }

    [Export(typeof(ScriptFormat))]
    public class TextScriptFormat : GenericScriptFormat
    {
        public override string          Tag { get { return "TXT"; } }
        public override string  Description { get { return "Plain text file"; } }
        public override uint      Signature { get { return 0; } }
        public override ScriptType DataType { get { return ScriptType.PlainText; } }

        public TextScriptFormat()
        {
            Extensions = new[] { "txt", "text", "log", "md", "rs", "js", "css", "lua", "cpp", "cs" };
        }
    }


    [Export(typeof(ScriptFormat))]
    public class JsonScriptFormat : GenericScriptFormat
    {
        public override string          Tag { get { return "JSON"; } }
        public override string  Description { get { return "JSON file"; } }
        public override uint      Signature { get { return 0; } }
        public override ScriptType DataType { get { return ScriptType.JsonScript; } }

        public JsonScriptFormat()
        {
            Extensions = new[] { "json", "jsonc" };
        }

        public override ScriptData Read(string name, Stream file)
        {
            byte[] data;
            using (var ms = new MemoryStream())
            {
                file.CopyTo(ms);
                data = ms.ToArray();
            }

            var encoding = DetectEncoding(data);
            var text = encoding.GetString(data);

            if (text.Length > 0 && text[0] == '\uFEFF')
                text = text.Substring(1);

            string formattedText = text;
            object parsedJson = null;

            try
            {
                parsedJson = JsonConvert.DeserializeObject(text);
                formattedText = JsonConvert.SerializeObject(parsedJson, Newtonsoft.Json.Formatting.Indented);
            }
            catch (JsonException ex)
            {
                formattedText = text; // Keep original
                var scriptData = new ScriptData(formattedText, ScriptType.JsonScript)
                {
                    Encoding = encoding
                };
                scriptData.Metadata["ValidJson"] = false;
                scriptData.Metadata["JsonError"] = ex.Message;
                return scriptData;
            }

            var validScriptData = new ScriptData(formattedText, ScriptType.JsonScript)
            {
                Encoding = encoding
            };
            validScriptData.Metadata["ValidJson"] = true;
            validScriptData.Metadata["OriginalLength"] = text.Length;
            validScriptData.Metadata["FormattedLength"] = formattedText.Length;

            if (parsedJson != null)
            {
                validScriptData.Metadata["RootType"] = parsedJson.GetType().Name;

                if (parsedJson is JObject jObj)
                    validScriptData.Metadata["PropertyCount"] = jObj.Properties().Count();
                else if (parsedJson is JArray jArr)
                    validScriptData.Metadata["ArrayLength"] = jArr.Count;
            }

            return validScriptData;
        }
    }

    [Export(typeof(ScriptFormat))]
    public class XmlScriptFormat : GenericScriptFormat
    {
        public override string          Tag { get { return "XML"; } }
        public override string  Description { get { return "XML file"; } }
        public override uint      Signature { get { return 0; } }
        public override ScriptType DataType { get { return ScriptType.XmlScript; } }

        public XmlScriptFormat()
        {
            Extensions = new[] { "xml", "xaml", "xsl", "xslt", "svg" };
        }

        public override ScriptData Read(string name, Stream file)
        {
            byte[] data;
            using (var ms = new MemoryStream())
            {
                file.CopyTo(ms);
                data = ms.ToArray();
            }

            var encoding = DetectEncoding(data);
            var text = encoding.GetString(data);

            if (text.Length > 0 && text[0] == '\uFEFF')
                text = text.Substring(1);

            string formattedText = text;

            try
            {
                var doc = XDocument.Parse(text);
                var settings = new XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = "  ",
                    NewLineChars = "\r\n",
                    NewLineHandling = NewLineHandling.Replace,
                    OmitXmlDeclaration = false,
                    Encoding = encoding,
                    CloseOutput = false
                };

                var sb = new StringBuilder();
                using (var stringWriter = new StringWriter(sb))
                {
                    using (var xmlWriter = XmlWriter.Create(stringWriter, settings))
                    {
                        doc.Save(xmlWriter);
                        xmlWriter.Flush(); // Explicitly flush
                    }
                }

                formattedText = sb.ToString();
            }
            catch (XmlException)
            {
                formattedText = text;
            }

            var scriptData = new ScriptData(formattedText, ScriptType.XmlScript)
            {
                Encoding = encoding
            };

            return scriptData;
        }
    }

    /*
    [Export(typeof(ScriptFormat))]
    public class BinScriptFormat : ScriptFormat
    {
        public override string         Tag { get { return "SCR"; } }
        public override string Description { get { return "Binary script format"; } }
        public override uint     Signature { get { return 0; } }
        public override ScriptType DataType { get { return ScriptType.BinaryScript; } }

        public BinScriptFormat()
        {
            Extensions = new[] { "scr", "bin", "dat" };
        }

        public override bool IsScript(IBinaryStream file)
        {
            var ext = Path.GetExtension(file.Name).TrimStart('.').ToLowerInvariant();
            return Extensions != null && Extensions.Contains(ext);
        }

        public override Stream ConvertFrom(IBinaryStream file)
        {
            throw new NotSupportedException("Binary script conversion not implemented");
        }

        public override Stream ConvertBack(IBinaryStream file)
        {
            throw new NotSupportedException("Binary script conversion not implemented");
        }

        public override ScriptData Read(string name, Stream file)
        {
            throw new NotSupportedException("Binary script reading not implemented");
        }

        public override ScriptData Read(string name, Stream file, Encoding e)
        {
            throw new NotSupportedException("Binary script reading not implemented");
        }

        public override void Write(Stream file, ScriptData script)
        {
            throw new NotSupportedException("Binary script writing not implemented");
        }
    }
    */
}