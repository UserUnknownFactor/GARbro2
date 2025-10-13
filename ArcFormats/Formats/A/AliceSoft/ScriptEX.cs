using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using GameRes.Compression;

namespace GameRes.Formats.AliceSoft
{
    public enum ExValueType
    {
        Int = 1,
        Float = 2,
        String = 3,
        Table = 4,
        List = 5,
        Tree = 6
    }

    [Export (typeof (ScriptFormat))]
    public class ExScriptFormat : ScriptFormat
    {
        public override          string Tag { get { return "PACTEX"; } }
        public override  string Description { get { return "AliceSoft data script"; } }
        public override      uint Signature { get { return  0x44414548; } } // 'HEAD'
        public override ScriptType DataType { get { return  ScriptType.BinaryScript; } }

        private static readonly byte[] ExDecodeTable = InitDecodeTable ();

        public ExScriptFormat ()
        {
            Extensions = new[] { "pactex", "ex" };
        }

        private static byte[] InitDecodeTable ()
        {
            var table = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                int tmp = i;
                tmp = (tmp & 0x55) + ((tmp >> 1) & 0x55);
                tmp = (tmp & 0x33) + ((tmp >> 2) & 0x33);
                tmp = (tmp & 0x0F) + ((tmp >> 4) & 0x0F);

                int result;
                if ((tmp & 0x01) == 0)
                    result = ((i << (8 - tmp)) | (i >> tmp)) & 0xFF;
                else
                    result = ((i >> (8 - tmp)) | (i << tmp)) & 0xFF;

                table[i] = (byte)result;
            }
            return table;
        }

        public override bool IsScript (IBinaryStream file)
        {
            if (file.Length < 0x20)
                return false;

            var header = file.ReadHeader (0x20);

            if (header.ToUInt32 (0) != 0x44414548) // 'HEAD'
                return false;

            if (header.ToUInt32 (8) != 0x46545845) // 'EXTF'
                return false;

            return header.ToUInt32 (0x14) == 0x41544144 || header.ToUInt32 (0x18) == 0x41544144; // 'DATA'
        }

        public override Stream ConvertFrom (IBinaryStream file)
        {
            byte[] unpacked = DecodeExFile (file);
            if (unpacked == null)
                return null;

            string textData = ParseExToText (unpacked);
            return new MemoryStream (Encoding.UTF8.GetBytes (textData));
        }

        public override Stream ConvertBack (IBinaryStream file)
        {
            throw new NotSupportedException ("EX script compilation not implemented");
        }

        public override ScriptData Read (string name, Stream file)
        {
            return Read (name, file, Encoding.UTF8);
        }

        public override ScriptData Read (string name, Stream file, Encoding encoding)
        {
            byte[] data = ReadStreamData (file);
            byte[] unpacked = DecodeExData (data);

            if (unpacked == null)
                return null;

            string textData = ParseExToText (unpacked);

            return new ScriptData (textData, ScriptType.BinaryScript)
            {
                Encoding = encoding
            };
        }

        public override void Write (Stream file, ScriptData script)
        {
            throw new NotSupportedException ("EX script writing not implemented");
        }

        private byte[] ReadStreamData (Stream stream)
        {
            using (var ms = new MemoryStream ())
            {
                stream.CopyTo (ms);
                return ms.ToArray ();
            }
        }

        private byte[] DecodeExFile (IBinaryStream file)
        {
            var data = new byte[file.Length];
            file.Position = 0;
            file.Read (data, 0, data.Length);
            return DecodeExData (data);
        }

        private byte[] DecodeExData (byte[] data)
        {
            using (var stream = new MemoryStream (data))
            using (var view = new BinaryReader (stream))
            {
                if (!CheckSignature (view))
                    return null;

                uint compressed_size, uncompressed_size;
                uint compressed_data_offset;

                stream.Position = 0x14;
                if (view.ReadUInt32 () == 0x41544144) // 'DATA'
                    compressed_data_offset = 0x20;
                else
                {
                    stream.Position = 0x18;
                    if (view.ReadUInt32 () == 0x41544144) // 'DATA'
                        compressed_data_offset = 0x24;
                    else
                        return null;
                }
                compressed_size = view.ReadUInt32 ();
                uncompressed_size = view.ReadUInt32 ();

                if (compressed_size > data.Length - compressed_data_offset)
                    return null;

                var compressed_data = new byte[compressed_size];
                stream.Position = compressed_data_offset;
                stream.Read (compressed_data, 0, (int)compressed_size);

                for (int i = 0; i < compressed_data.Length; i++)
                    compressed_data[i] = ExDecodeTable[compressed_data[i]];

                byte[] unpacked_data;
                try
                {
                    using (var input = new MemoryStream (compressed_data))
                    using (var zstream = new ZLibStream (input, CompressionMode.Decompress))
                    {
                        unpacked_data = new byte[uncompressed_size];
                        if (zstream.Read (unpacked_data, 0, unpacked_data.Length) != uncompressed_size)
                            return null;
                    }
                }
                catch
                {
                    return null;
                }

                return unpacked_data;
            }
        }

        private bool CheckSignature (BinaryReader view)
        {
            view.BaseStream.Position = 0;
            if (view.ReadUInt32 () != 0x44414548) // 'HEAD'
                return false;

            view.BaseStream.Position = 8;
            if (view.ReadUInt32 () != 0x46545845) // 'EXTF'
                return false;

            return true;
        }

        private string ParseExToText (byte[] data)
        {
            try
            {
                var parser = new ExTextParser (data);
                return parser.Parse ();
            }
            catch (Exception e)
            {
                return $"// Error parsing EX data: {e.Message}\n// Data size: {data.Length} bytes\n";
            }
        }

        internal class ExTextParser
        {
            private readonly  BinaryReader _reader;
            private readonly  MemoryStream _stream;
            private readonly StringBuilder _output;
            private int _indentLevel = 0;

            public ExTextParser (byte[] data)
            {
                _stream = new MemoryStream (data);
                _reader = new BinaryReader (_stream);
                _output = new StringBuilder ();
            }

            public string Parse ()
            {
                bool first = true;
                while (_stream.Position < _stream.Length - 8)
                {
                    if (!first)
                        _output.AppendLine ();

                    if (!ParseBlock ())
                        break;

                    first = false;
                }

                if (_output.Length > 0 && _output[_output.Length - 1] != '\n')
                    _output.AppendLine ();

                return _output.ToString ();
            }

            private bool ParseBlock ()
            {
                try
                {
                    var type = (ExValueType)_reader.ReadInt32 ();
                    if ((int)type < 1 || (int)type > 6)
                        return false;

                    int size = _reader.ReadInt32 ();
                    if (size <= 0 || size > _stream.Length - _stream.Position)
                        return false;

                    long dataStart = _stream.Position;
                    string name = ReadPascalString ();
                    if (name == null)
                        name = "unnamed";

                    _output.Append (GetTypeName (type));
                    _output.Append (" ");
                    _output.Append (FormatIdentifier (name));
                    _output.Append (" = ");

                    WriteValue (type, false);

                    _output.AppendLine (";");

                    _stream.Position = dataStart + size;

                    return true;
                }
                catch
                {
                    return false;
                }
            }

            private void WriteValue (ExValueType type, bool inLine)
            {
                switch (type)
                {
                    case ExValueType.Int:
                        _output.Append (_reader.ReadInt32 ());
                        break;

                    case ExValueType.Float:
                        float f = _reader.ReadSingle ();
                        if (f == (int)f && f != 0)
                            _output.Append (((int)f).ToString (System.Globalization.CultureInfo.InvariantCulture) + ".000000");
                        else
                            _output.Append (f.ToString ("F6", System.Globalization.CultureInfo.InvariantCulture));
                        break;

                    case ExValueType.String:
                        _output.Append (FormatString (ReadPascalString ()));
                        break;

                    case ExValueType.Table:
                        WriteTable (inLine);
                        break;

                    case ExValueType.List:
                        WriteList (inLine);
                        break;

                    case ExValueType.Tree:
                        WriteTree ();
                        break;
                }
            }

            private void WriteTable (bool inLine)
            {
                _output.Append ("{");

                // Read fields
                int nr_fields = _reader.ReadInt32 ();
                var fields = new List<Field> ();
                for (int i = 0; i < nr_fields; i++)
                {
                    fields.Add (ReadField ());
                }

                // Read dimensions
                int nr_columns = _reader.ReadInt32 ();
                int nr_rows = _reader.ReadInt32 ();

                // Check if dimensions need swapping
                if (fields.Count > 0 && nr_columns != fields.Count)
                {
                    if (nr_rows == fields.Count)
                    {
                        int tmp = nr_columns;
                        nr_columns = nr_rows;
                        nr_rows = tmp;
                    }
                }

                if (fields.Count > 0)
                {
                    _output.AppendLine ();
                    _indentLevel++;
                    Indent ();
                    _output.Append ("{ ");

                    // Write fields
                    for (int i = 0; i < fields.Count; i++)
                    {
                        if (i > 0)
                            _output.Append (", ");
                        WriteFieldDecl (fields[i]);
                    }

                    _output.AppendLine (" },");
                }

                // Write rows
                for (int i = 0; i < nr_rows; i++)
                {
                    if (fields.Count > 0)
                        Indent ();
                    else if (i > 0)
                        _output.Append (" ");

                    _output.Append ("{ ");

                    for (int j = 0; j < nr_columns; j++)
                    {
                        if (j > 0)
                            _output.Append (", ");

                        var cellType = (ExValueType)_reader.ReadInt32 ();
                        WriteValue (cellType, true);
                    }

                    _output.Append (" }");

                    if (i < nr_rows - 1)
                        _output.Append (",");

                    if (fields.Count > 0)
                        _output.AppendLine ();
                }

                if (fields.Count > 0)
                {
                    _indentLevel--;
                    Indent ();
                }
                else
                {
                    _output.Append (" ");
                }

                _output.Append ("}");
            }

            private void WriteFieldDecl (Field field)
            {
                if (field.IsIndex)
                    _output.Append ("indexed ");

                _output.Append (GetTypeName (field.Type));
                _output.Append (" ");
                _output.Append (FormatIdentifier (field.Name));

                if (field.HasValue)
                {
                    _output.Append (" = ");
                    WriteFieldValue (field);
                }

                if (field.Subfields.Count > 0)
                {
                    _output.Append (" { ");
                    for (int i = 0; i < field.Subfields.Count; i++)
                    {
                        if (i > 0)
                            _output.Append (", ");
                        WriteFieldDecl (field.Subfields[i]);
                    }
                    _output.Append (" }");
                }
            }

            private void WriteFieldValue (Field field)
            {
                switch (field.Type)
                {
                    case ExValueType.Int:
                        _output.Append (field.IntValue);
                        break;
                    case ExValueType.Float:
                        if (field.FloatValue == (int)field.FloatValue && field.FloatValue != 0)
                            _output.Append (((int)field.FloatValue).ToString (System.Globalization.CultureInfo.InvariantCulture) + ".000000");
                        else
                            _output.Append (field.FloatValue.ToString ("F6", System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    case ExValueType.String:
                        _output.Append (FormatString (field.StringValue));
                        break;
                    default:
                        WriteValue (field.Type, true);
                        break;
                }
            }

            private void WriteList (bool inLine)
            {
                int nr_items = _reader.ReadInt32 ();

                _output.Append ("{");

                if (nr_items > 0)
                {
                    _output.AppendLine ();
                    _indentLevel++;
                }

                for (int i = 0; i < nr_items; i++)
                {
                    Indent ();

                    var type = (ExValueType)_reader.ReadInt32 ();
                    int size = _reader.ReadInt32 ();
                    long pos = _stream.Position;

                    WriteValue (type, true);

                    // Skip to next item
                    _stream.Position = pos + size;

                    if (i < nr_items - 1)
                        _output.Append (",");

                    _output.AppendLine ();
                }

                if (nr_items > 0)
                {
                    _indentLevel--;
                    Indent ();
                }

                _output.Append ("}");
            }

            private void WriteTree ()
            {
                string name = ReadString ();
                int is_leaf = _reader.ReadInt32 ();

                _output.AppendLine ("{");
                _indentLevel++;

                if (is_leaf != 0)
                {
                    // This shouldn't happen at root level
                    var type = (ExValueType)_reader.ReadInt32 ();
                    int size = _reader.ReadInt32 ();
                    long pos = _stream.Position;
                    string leaf_name = ReadString ();
                    WriteValue (type, true);
                    _stream.Position = pos + size;
                    _reader.ReadInt32 ();
                }
                else
                {
                    int nr_children = _reader.ReadInt32 ();
                    WriteTreeChildren (nr_children);
                }

                _indentLevel--;
                Indent ();
                _output.Append ("}");
            }

            private void WriteTreeChildren (int count)
            {
                for (int i = 0; i < count; i++)
                {
                    var childName = ReadString ();
                    var childIsLeaf = _reader.ReadInt32 ();

                    Indent ();
                    _output.Append (FormatIdentifier (childName));
                    _output.Append (" = ");

                    if (childIsLeaf != 0)
                    {
                        var type = (ExValueType)_reader.ReadInt32 ();
                        int size = _reader.ReadInt32 ();
                        long pos = _stream.Position;

                        string leaf_name = ReadString ();

                        if (type == ExValueType.Table || type == ExValueType.List)
                        {
                            _output.Append ("(");
                            _output.Append (GetTypeName (type).ToLower ());
                            _output.Append (") ");
                        }

                        WriteValue (type, true);

                        _stream.Position = pos + size;
                        _reader.ReadInt32 ();
                    }
                    else
                    {
                        int childCount = _reader.ReadInt32 ();
                        _output.AppendLine ("{");
                        _indentLevel++;
                        WriteTreeChildren (childCount);
                        _indentLevel--;
                        Indent ();
                        _output.Append ("}");
                    }

                    _output.AppendLine (",");
                }
            }

            private Field ReadField ()
            {
                var field = new Field ();

                field.Type = (ExValueType)_reader.ReadInt32 ();
                field.Name = ReadString () ?? "";
                field.HasValue = _reader.ReadInt32 () != 0;
                field.IsIndex = _reader.ReadInt32 () != 0;

                if (field.HasValue)
                {
                    switch (field.Type)
                    {
                        case ExValueType.Int:
                            field.IntValue = _reader.ReadInt32 ();
                            break;
                        case ExValueType.Float:
                            field.FloatValue = _reader.ReadSingle ();
                            break;
                        case ExValueType.String:
                            field.StringValue = ReadPascalString ();
                            break;
                        default:
                            long pos = _stream.Position;
                            SkipValue (field.Type);
                            break;
                    }
                }

                if (field.Type == ExValueType.Table)
                {
                    int nr_subfields = _reader.ReadInt32 ();
                    for (int i = 0; i < nr_subfields; i++)
                    {
                        field.Subfields.Add (ReadField ());
                    }
                }

                return field;
            }

            private void SkipValue (ExValueType type)
            {
                switch (type)
                {
                    case ExValueType.Int:
                        _reader.ReadInt32 ();
                        break;

                    case ExValueType.Float:
                        _reader.ReadSingle ();
                        break;

                    case ExValueType.String:
                        ReadPascalString ();
                        break;

                    case ExValueType.Table:
                        SkipTable ();
                        break;

                    case ExValueType.List:
                        SkipList ();
                        break;

                    case ExValueType.Tree:
                        SkipTree ();
                        break;
                }
            }

            private void SkipTable ()
            {
                // Read fields
                int nr_fields = _reader.ReadInt32 ();
                for (int i = 0; i < nr_fields; i++)
                {
                    SkipField ();
                }

                // Read dimensions
                int nr_columns = _reader.ReadInt32 ();
                int nr_rows = _reader.ReadInt32 ();

                // Skip all cells
                for (int i = 0; i < nr_rows; i++)
                {
                    for (int j = 0; j < nr_columns; j++)
                    {
                        var cellType = (ExValueType)_reader.ReadInt32 ();
                        SkipValue (cellType);
                    }
                }
            }

            private void SkipField ()
            {
                var type = (ExValueType)_reader.ReadInt32 ();
                ReadString (); // name
                bool hasValue = _reader.ReadInt32 () != 0;
                _reader.ReadInt32 (); // is_index

                if (hasValue)
                {
                    SkipValue (type);
                }

                if (type == ExValueType.Table)
                {
                    int nr_subfields = _reader.ReadInt32 ();
                    for (int i = 0; i < nr_subfields; i++)
                    {
                        SkipField ();
                    }
                }
            }

            private void SkipList ()
            {
                int nr_items = _reader.ReadInt32 ();

                for (int i = 0; i < nr_items; i++)
                {
                    var type = (ExValueType)_reader.ReadInt32 ();
                    int size = _reader.ReadInt32 ();
                    // For lists, we can use the size field to skip efficiently
                    _stream.Position += size;
                }
            }

            private void SkipTree ()
            {
                ReadString (); // name
                int is_leaf = _reader.ReadInt32 ();

                if (is_leaf != 0)
                {
                    // Leaf node
                    var type = (ExValueType)_reader.ReadInt32 ();
                    int size = _reader.ReadInt32 ();
                    // For leaf nodes, we can use size to skip
                    _stream.Position += size;
                    _reader.ReadInt32 (); // trailing zero
                }
                else
                {
                    // Branch node
                    int nr_children = _reader.ReadInt32 ();
                    for (int i = 0; i < nr_children; i++)
                    {
                        SkipTree ();
                    }
                }
            }

            private void Indent ()
            {
                for (int i = 0; i < _indentLevel; i++)
                    _output.Append ("\t");
            }

            private string GetTypeName (ExValueType type)
            {
                switch (type)
                {
                    case ExValueType.Int: return "int";
                    case ExValueType.Float: return "float";
                    case ExValueType.String: return "string";
                    case ExValueType.Table: return "table";
                    case ExValueType.List: return "list";
                    case ExValueType.Tree: return "tree";
                    default: return "unknown";
                }
            }

            private string FormatIdentifier (string name)
            {
                if (string.IsNullOrEmpty (name))
                    return "\"\"";

                // Names starting with @ are special
                if (name.StartsWith ("@"))
                    return name;

                return "\"" + EscapeString (name) + "\"";
            }

            private string FormatString (string str)
            {
                if (str == null)
                    return "\"\"";
                return "\"" + EscapeString (str) + "\"";
            }

            private string EscapeString (string str)
            {
                if (str == null)
                    return "";

                var sb = new StringBuilder ();
                foreach (char c in str)
                {
                    switch (c)
                    {
                        case '\\': sb.Append ("\\\\"); break;
                        case '"': sb.Append ("\\\""); break;
                        case '\n': sb.Append ("\\n"); break;
                        case '\r': sb.Append ("\\r"); break;
                        case '\t': sb.Append ("\\t"); break;
                        default: sb.Append (c); break;
                    }
                }
                return sb.ToString ();
            }

            private string ReadPascalString ()
            {
                try
                {
                    int length = _reader.ReadInt32 ();
                    if (length <= 0 || length > 10000)
                        return null;

                    var bytes = _reader.ReadBytes (length);

                    // Find actual string end (before padding)
                    int actualLength = length;
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        if (bytes[i] == 0)
                        {
                            actualLength = i;
                            break;
                        }
                    }

                    if (actualLength == 0)
                        return "";

                    // Preserve original encoding
                    try
                    {
                        return Encodings.cp932.GetString (bytes, 0, actualLength);
                    }
                    catch
                    {
                        return Encoding.UTF8.GetString (bytes, 0, actualLength);
                    }
                }
                catch
                {
                    return null;
                }
            }

            private string ReadString ()
            {
                string str = ReadPascalString ();

                // Align to 4 bytes
                long pos = _stream.Position;
                if (pos % 4 != 0)
                    _stream.Position = (pos + 3) & ~3;

                return str;
            }

            private class Field
            {
                public ExValueType Type { get; set; }
                public string Name { get; set; }
                public bool HasValue { get; set; }
                public bool IsIndex { get; set; }
                public int IntValue { get; set; }
                public float FloatValue { get; set; }
                public string StringValue { get; set; }
                public List<Field> Subfields { get; set; } = new List<Field> ();
            }
        }
    }

    [Export (typeof (ResourceAlias))]
    [ExportMetadata ("Extension", "EX")]
    [ExportMetadata ("Target", "PACTEX")]
    public class ExFormat : ResourceAlias { }
}