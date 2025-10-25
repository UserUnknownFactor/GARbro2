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

        public ScriptLine (uint id, string text, string speaker = null)
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
        public IList<ScriptLine> TextLines
        {
            get { return m_text; }
            set { m_text = (List<ScriptLine>)value; }
        }
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

        public ScriptData (string text, ScriptType type = ScriptType.Unknown)
        {
            RawText = text ?? string.Empty;
            Type = type;
            Encoding = Encoding.UTF8;
            m_text = new List<ScriptLine>();
            Metadata = new Dictionary<string, object>();

            DetectNewLineFormat (text);

            if (!string.IsNullOrEmpty (text) && type == ScriptType.PlainText)
                ParsePlainText (text);
        }

        public ScriptData (IEnumerable<ScriptLine> lines, ScriptType type = ScriptType.Dialogue)
        {
            m_text = new List<ScriptLine>(lines);
            Type = type;
            Encoding = Encoding.UTF8;
            NewLineFormat = "\n";
            Metadata = new Dictionary<string, object>();
            RawText = string.Join (NewLineFormat, m_text.Select (l => l.Text));
        }

        /// <summary>
        /// Detects the newline format used in the text.
        /// </summary>
        protected virtual void DetectNewLineFormat (string text)
        {
            if (string.IsNullOrEmpty (text))
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

        protected virtual void ParsePlainText (string text)
        {
            if (string.IsNullOrEmpty (text))
                return;

            string[] lines;
            if (!string.IsNullOrEmpty (NewLineFormat) && text.Contains (NewLineFormat))
                lines = text.Split (new[] { NewLineFormat }, StringSplitOptions.None);
            else
                lines = text.Split (new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            uint id = 0;
            foreach (var line in lines)
            {
                m_text.Add (new ScriptLine (id++, line));
            }
        }

        // Helper for text formats and formats that only share TextLines
        public void Serialize (Stream output)
        {
            using (var writer = new StreamWriter (output, Encoding, 1024, true))
            {
                if (m_text.Count > 0)
                {
                    for (int i = 0; i < m_text.Count; i++)
                    {
                        writer.Write (m_text[i].Text);

                        // Don't add newline after the last line unless original had it
                        if (!string.IsNullOrEmpty (NewLineFormat)) 
                        {
                            if (i < m_text.Count - 1)
                                writer.Write (NewLineFormat);
                            else if (!string.IsNullOrEmpty (RawText) && (RawText.EndsWith (NewLineFormat) || RawText.EndsWith ("\n")))
                                writer.Write (NewLineFormat);
                        }
                        else if (i < m_text.Count - 1)
                            writer.Write ('\n');
                    }
                }
                else
                    writer.Write (RawText);
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
        public abstract bool IsScript (IBinaryStream file);

        /// <summary>
        /// Converts script from game format to readable format.
        /// </summary>
        public abstract Stream ConvertFrom (IBinaryStream file);

        /// <summary>
        /// Converts script from readable format back to game format.
        /// </summary>
        public abstract Stream ConvertBack (IBinaryStream file);

        /// <summary>
        /// Reads and parses script data.
        /// </summary>
        public abstract ScriptData Read (string name, Stream file);

        /// <inheritdoc cref="Read (string, Stream)"/>
        public abstract ScriptData Read (string name, Stream file, Encoding encoding);

        /// <summary>
        /// Writes script data to stream.
        /// </summary>
        public abstract void Write (Stream file, ScriptData script);

        /// <summary>
        /// Detects encoding of text data.
        /// </summary>
        public static Encoding DetectEncoding (Stream data, long length = -1)
        {
            var pos = data.Position;
            if (length < 0 || length > data.Length)
                length = data.Length;
            var bytedata = new byte[length];
            data.Read (bytedata, 0, (int)length);
            data.Position = pos;
            return DetectEncoding (bytedata, length);
        }

        /// <summary>
        /// Detects encoding of text data.
        /// </summary>
        public static Encoding DetectEncoding (byte[] data, long length = -1)
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
                return new UTF32Encoding (true, true); // UTF-32 BE

            // For files without BOM, we need heuristics
            int sampleSize = (int)Math.Min (length, 8192);

            // Try UTF-8 first with strict validation
            var utf8Result = EncodingValidation.ValidateUtf8 (data, sampleSize);
            if (utf8Result.IsValid && utf8Result.Confidence > 0.9)
                return Encoding.UTF8;

            // Check for UTF-16 with better heuristics
            var utf16Result = EncodingValidation.ValidateUtf16 (data, sampleSize);
            if (utf16Result.IsValid && utf16Result.Confidence > 0.8)
                return Encoding.Unicode;

            // Check for Shift-JIS with context
            var sjisResult = EncodingValidation.ValidateShiftJis (data, sampleSize);
            if (sjisResult.IsValid && sjisResult.Confidence > 0.8)
                return Encoding.GetEncoding (932);


            // Default to UTF-8 encoding, whatever goes
            return Encoding.UTF8;
        }


        /// <summary>
        /// Finds appropriate script format handler for the given file.
        /// </summary>
        public static ScriptFormat FindFormat (IBinaryStream file)
        {
            foreach (var impl in FormatCatalog.Instance.FindFormats<ScriptFormat>(file.Name, file.Signature))
            {
                try
                {
                    file.Position = 0;
                    if (impl.IsScript (file))
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
        public override bool IsScript (IBinaryStream file)
        {
            // Check extension
            var ext = Path.GetExtension (file.Name).TrimStart('.').ToLowerInvariant();
            return Extensions != null && Extensions.Contains (ext);
        }

        // Formats that override it must return a new Stream here.
        public override Stream ConvertFrom (IBinaryStream file)
        {
            return file.AsStream;
        }

        // Formats that override it must return a new Stream here.
        public override Stream ConvertBack (IBinaryStream file)
        {
            return file.AsStream;
        }

        public override ScriptData Read (string name, Stream file)
        {
            byte[] data;
            using (var ms = new MemoryStream())
            {
                file.CopyTo (ms);
                data = ms.ToArray();
            }

            var encoding = DetectEncoding (data);
            var text = encoding.GetString (data);

            // Remove BOM if present
            if (text.Length > 0 && text[0] == '\uFEFF')
                text = text.Substring (1);

            var scriptData = new ScriptData (text, GetScriptType (name)) { Encoding = encoding };
            return scriptData;
        }

        public override ScriptData Read (string name, Stream file, Encoding encoding)
        {
            byte[] data;
            using (var ms = new MemoryStream())
            {
                file.CopyTo (ms);
                data = ms.ToArray();
            }

            var text = encoding.GetString (data);

            // Remove BOM if present
            if (encoding.IsUtf16() && text.Length > 0 && text[0] == '\uFEFF')
                text = text.Substring (1);

            var scriptData = new ScriptData (text, GetScriptType (name)) { Encoding = encoding };
            return scriptData;
        }

        public override void Write (Stream file, ScriptData script)
        {
            script.Serialize (file);
        }

        protected virtual ScriptType GetScriptType (string filename)
        {
            return DataType;
        }
    }

    [Export(typeof(ScriptFormat))]
    public class TextScriptFormat : GenericScriptFormat
    {
        public override string          Tag { get { return "TXT"; } }
        public override string  Description { get { return "Plain text file"; } }
        public override uint      Signature { get { return  0; } }
        public override ScriptType DataType { get { return  ScriptType.PlainText; } }

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
        public override uint      Signature { get { return  0; } }
        public override ScriptType DataType { get { return  ScriptType.JsonScript; } }

        public JsonScriptFormat()
        {
            Extensions = new[] { "json", "jsonc" };
        }

        public override ScriptData Read (string name, Stream file)
        {
            byte[] data;
            using (var ms = new MemoryStream())
            {
                file.CopyTo (ms);
                data = ms.ToArray();
            }

            var encoding = DetectEncoding (data);
            var text = encoding.GetString (data);

            if (text.Length > 0 && text[0] == '\uFEFF')
                text = text.Substring (1);

            string formattedText = text;
            object parsedJson = null;

            try
            {
                parsedJson = JsonConvert.DeserializeObject (text);
                formattedText = JsonConvert.SerializeObject (parsedJson, Newtonsoft.Json.Formatting.Indented);
            }
            catch (JsonException ex)
            {
                formattedText = text; // Keep original
                var scriptData = new ScriptData (formattedText, ScriptType.JsonScript)
                {
                    Encoding = encoding
                };
                scriptData.Metadata["ValidJson"] = false;
                scriptData.Metadata["JsonError"] = ex.Message;
                return scriptData;
            }

            var validScriptData = new ScriptData (formattedText, ScriptType.JsonScript)
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

        public override Stream ConvertFrom (IBinaryStream file)
        {
            var scriptData = Read(file.Name, file.AsStream);
            var outputStream = new MemoryStream();
            var writer = new StreamWriter(outputStream, scriptData.Encoding, 1024, true);
            writer.Write(scriptData.RawText);
            writer.Flush();
            outputStream.Position = 0;
            return outputStream;
        }
    }

    [Export(typeof(ScriptFormat))]
    public class XmlScriptFormat : GenericScriptFormat
    {
        public override string          Tag { get { return "XML"; } }
        public override string  Description { get { return "XML file"; } }
        public override uint      Signature { get { return  0; } }
        public override ScriptType DataType { get { return  ScriptType.XmlScript; } }

        public XmlScriptFormat()
        {
            Extensions = new[] { "xml", "xaml", "xsl", "xslt", "svg" };
        }

        public override ScriptData Read (string name, Stream file)
        {
            byte[] data;
            using (var ms = new MemoryStream())
            {
                file.CopyTo (ms);
                data = ms.ToArray();
            }

            var encoding = DetectEncoding (data);
            var text = encoding.GetString (data);

            if (text.Length > 0 && text[0] == '\uFEFF')
                text = text.Substring (1);

            string formattedText = text;

            try
            {
                var doc = XDocument.Parse (text);
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
                using (var stringWriter = new StringWriter (sb))
                {
                    using (var xmlWriter = XmlWriter.Create (stringWriter, settings))
                    {
                        doc.Save (xmlWriter);
                        xmlWriter.Flush(); // Explicitly flush
                    }
                }

                formattedText = sb.ToString();
            }
            catch (XmlException)
            {
                formattedText = text;
            }

            var scriptData = new ScriptData (formattedText, ScriptType.XmlScript)
            {
                Encoding = encoding
            };

            return scriptData;
        }

        public override Stream ConvertFrom (IBinaryStream file)
        {
            var scriptData = Read(file.Name, file.AsStream);
            var outputStream = new MemoryStream();
            var writer = new StreamWriter(outputStream, scriptData.Encoding, 1024, true);
            writer.Write(scriptData.RawText);
            writer.Flush();
            outputStream.Position = 0;
            return outputStream;
        }
    }

    public class EncodingValidation
    {
        public bool      IsValid { get; set; }
        public double Confidence { get; set; }

        public static bool IsEncodingCompatible (Stream stream, Encoding encoding)
        {
            try
            {
                var buffer = new byte[Math.Min (1024, stream.Length)];
                stream.Position = 0;
                int bytesRead = stream.Read (buffer, 0, buffer.Length);
                stream.Position = 0;

                var decoder = encoding.GetDecoder();
                var charBuffer = new char[decoder.GetCharCount (buffer, 0, bytesRead)];
                decoder.GetChars (buffer, 0, bytesRead, charBuffer, 0);

                int replacementCount = 0;
                foreach (char c in charBuffer)
                {
                    if (c == '\uFFFD' || c == '\uFFFF') // Unicode Replacement or Non-character characters
                        replacementCount++;
                }
                return replacementCount == 0;
            }
            catch
            {
                return true;
            }
        }

        public static EncodingValidation ValidateUtf8 (byte[] data, int length)
        {
            int validChars = 0;
            int totalChars = 0;
            int i = 0;

            while (i < length)
            {
                byte b = data[i];

                if (b <= 0x7F)
                {
                    // ASCII - very common in UTF-8
                    validChars++;
                    totalChars++;
                    i++;
                    continue;
                }

                // Multi-byte sequence
                int sequenceLength;
                if ((b & 0xE0) == 0xC0) sequenceLength = 2;
                else if ((b & 0xF0) == 0xE0) sequenceLength = 3;
                else if ((b & 0xF8) == 0xF0) sequenceLength = 4;
                else
                {
                    return new EncodingValidation { IsValid = false, Confidence = 0 };
                }

                if (i + sequenceLength > length)
                    return new EncodingValidation { IsValid = false, Confidence = 0 };

                // Validate continuation bytes
                for (int j = 1; j < sequenceLength; j++)
                {
                    if ((data[i + j] & 0xC0) != 0x80)
                        return new EncodingValidation { IsValid = false, Confidence = 0 };
                }

                // Additional validation for overlong sequences
                if (sequenceLength == 2 && (b & 0x1E) == 0)
                    return new EncodingValidation { IsValid = false, Confidence = 0 };

                validChars++;
                totalChars++;
                i += sequenceLength;
            }

            double confidence = totalChars > 0 ? (double)validChars / totalChars : 0;
            return new EncodingValidation { IsValid = true, Confidence = confidence };
        }

        public static EncodingValidation ValidateUtf16 (byte[] data, int length)
        {
            if (length < 2)
                return new EncodingValidation { IsValid = false, Confidence = 0 };

            int validChars = 0;
            int totalChars = 0;
            int consecutiveNulls = 0;
            int maxConsecutiveNulls = 0;

            for (int i = 0; i < length - 1; i += 2)
            {
                ushort ch = BitConverter.ToUInt16 (data, i);

                // Count valid Unicode characters
                if (ch >= 0x20 && ch <= 0x7E) // Basic ASCII
                {
                    validChars++;
                    consecutiveNulls = 0;
                }
                else if (ch >= 0x80 && ch <= 0xD7FF) // Valid Unicode range
                {
                    validChars++;
                    consecutiveNulls = 0;
                }
                else if (ch >= 0xE000 && ch <= 0xFFFD) // Valid Unicode range
                {
                    validChars++;
                    consecutiveNulls = 0;
                }
                else if (ch == 0)
                {
                    consecutiveNulls++;
                    maxConsecutiveNulls = Math.Max (maxConsecutiveNulls, consecutiveNulls);
                }
                else if (ch >= 0xD800 && ch <= 0xDFFF) // Surrogate pair
                {
                    if (i + 3 < length)
                    {
                        ushort ch2 = BitConverter.ToUInt16 (data, i + 2);
                        if (ch2 >= 0xDC00 && ch2 <= 0xDFFF)
                        {
                            validChars++;
                            i += 2; // Skip the low surrogate
                        }
                    }
                }

                totalChars++;
            }

            // Too many consecutive nulls indicate this isn't text
            if (maxConsecutiveNulls > 4)
                return new EncodingValidation { IsValid = false, Confidence = 0 };

            double confidence = totalChars > 0 ? (double)validChars / totalChars : 0;
            bool isValid = confidence > 0.3 && validChars > 10; // Need reasonable amount of valid chars

            return new EncodingValidation { IsValid = isValid, Confidence = confidence };
        }

        public static EncodingValidation ValidateShiftJis (byte[] data, int length)
        {
            int validChars = 0;
            int invalidChars = 0;
            int asciiChars = 0;
            int kanjiChars = 0;
            int kanaChars = 0;

            for (int i = 0; i < length; i++)
            {
                byte b = data[i];

                if (b <= 0x7F)
                {
                    asciiChars++;
                    validChars++;
                    continue;
                }

                if (b >= 0xA1 && b <= 0xDF)
                {
                    // Half-width katakana
                    kanaChars++;
                    validChars++;
                    continue;
                }

                // Two-byte character lead byte
                if ((b >= 0x81 && b <= 0x9F) || (b >= 0xE0 && b <= 0xFC))
                {
                    if (i + 1 < length)
                    {
                        byte b2 = data[i + 1];
                        if ((b2 >= 0x40 && b2 <= 0x7E) || (b2 >= 0x80 && b2 <= 0xFC))
                        {
                            // Valid Shift-JIS sequence
                            kanjiChars++;
                            validChars++;
                            i++; // Skip the second byte
                        }
                        else
                        {
                            invalidChars++;
                            return new EncodingValidation { IsValid = false, Confidence = 0 };
                        }
                    }
                    else
                    {
                        // Incomplete sequence at end
                        invalidChars++;
                    }
                }
                else
                {
                    // Invalid byte for Shift-JIS
                    invalidChars++;
                    return new EncodingValidation { IsValid = false, Confidence = 0 };
                }
            }

            // Shift-JIS text should have a good mix of character types
            double confidence = 0;
            if (validChars > 0)
            {
                double asciiRatio = (double)asciiChars / validChars;
                double kanjiRatio = (double)kanjiChars / validChars;
                double kanaRatio  = (double)kanaChars  / validChars;

                // Shift-JIS text usually has some Japanese characters
                if (kanjiChars > 0 || kanaChars > 0)
                    confidence = 0.9;
                else if (asciiRatio == 1.0) // Pure ASCII - not really Shift-JIS
                    confidence = 0.1;
            }

            return new EncodingValidation 
            { 
                IsValid = invalidChars == 0 && validChars > 0, 
                Confidence = confidence 
            };
        }
    }
}