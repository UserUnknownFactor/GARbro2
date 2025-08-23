using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.IO;
using System.Text;
using GameRes.Compression;

namespace GameRes.Formats.RenPy
{
    internal class RpaEntry : PackedEntry
    {
        public byte[] Header = null;
    }

    public class RpaOptions : ResourceOptions
    {
        public uint Key;
    }

    [Export(typeof(ArchiveFormat))]
    public class RpaOpener : ArchiveFormat
    {
        public override string         Tag { get { return "RPA"; } }
        public override string Description { get { return Localization._T ("RPADescription"); } }
        public override uint     Signature { get { return 0x2d415052; } } // "RPA-"
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return true; } }

        public override ArcFile TryOpen (ArcView file)
        {
            var magic = file.View.ReadString (0, 8, Encoding.ASCII);
            if (!magic.StartsWith("RPA-"))
                return null;

            string version_str = magic.Substring (4, 3);
            float version;
            if (!float.TryParse (version_str, NumberStyles.Float, CultureInfo.InvariantCulture, out version))
                return null;

            if (version == 3.0f || version == 3.2f)
                return TryOpenV3 (file, version);
            else if (version == 2.0f)
                return TryOpenV2 (file);
            else
                return null;
        }

        private ArcFile TryOpenV2 (ArcView file)
        {
            // RPA-2.0 format: "RPA-2.0 XXXXXXXXXXXXXXXX\n"
            string header_line = file.View.ReadString (0, 25, Encoding.ASCII);
            if (!header_line.EndsWith ("\n"))
                return null;

            string index_offset_str = header_line.Substring (8, 16).Trim();
            long index_offset;
            if (!long.TryParse (index_offset_str, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out index_offset))
                return null;

            if (index_offset >= file.MaxOffset)
                return null;

            return LoadIndex (file, index_offset, 0);
        }

        private ArcFile TryOpenV3 (ArcView file, float version)
        {
            var header_bytes = new byte[256];
            file.View.Read (0, header_bytes, 0, (uint)header_bytes.Length);

            int newline_pos = Array.IndexOf (header_bytes, (byte)'\n');
            if (newline_pos == -1)
                return null;

            string header_line = Encoding.ASCII.GetString (header_bytes, 0, newline_pos);
            var parts = header_line.Split (' ');

            if (parts.Length < 3)
                return null;

            long index_offset;
            if (!long.TryParse (parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out index_offset))
                return null;

            if (index_offset >= file.MaxOffset)
                return null;

            uint key = 0;
            int key_start = (version == 3.2f) ? 3 : 2;
            for (int i = key_start; i < parts.Length; i++)
            {
                uint part;
                if (uint.TryParse (parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out part))
                    key ^= part;
            }

            return LoadIndex (file, index_offset, key);
        }

        private ArcFile LoadIndex (ArcView file, long index_offset, uint key)
        {
            IDictionary indexes_dict = null;
            using (var index = new ZLibStream (file.CreateStream (index_offset), CompressionMode.Decompress))
            {
                var pickle = new Pickle (index);
                indexes_dict = pickle.Load() as IDictionary;
            }
            if (null == indexes_dict)
                return null;

            var dir = new List<Entry> (indexes_dict.Count);
            foreach (DictionaryEntry cur_index in indexes_dict)
            {
                var name_raw = cur_index.Key as byte[];
                var values = cur_index.Value as IList;
                if (null == name_raw || null == values || values.Count < 1)
                {
                    Trace.WriteLine ("invalid index entry", "RpaOpener.TryOpen");
                    continue;
                }
                string name = Encoding.UTF8.GetString (name_raw);
                if (string.IsNullOrEmpty (name))
                    continue;

                IList entries_list = values[0] is IList ? values : new ArrayList { values };
                foreach (var entry_data in entries_list)
                {
                    var tuple = entry_data as IList;
                    if (null == tuple || tuple.Count < 2)
                    {
                        Trace.WriteLine ("invalid index tuple", "RpaOpener.TryOpen");
                        continue;
                    }

                    var entry = FormatCatalog.Instance.Create<RpaEntry> (name);

                    if (key != 0)
                    {
                        entry.Offset       = (long)(Convert.ToInt64 (tuple[0]) ^ key);
                        entry.UnpackedSize = (uint)(Convert.ToInt64 (tuple[1]) ^ key);
                    }
                    else
                    {
                        entry.Offset       = Convert.ToInt64 (tuple[0]);
                        entry.UnpackedSize = (uint)Convert.ToInt64 (tuple[1]);
                    }

                    entry.Size = entry.UnpackedSize;

                    if (tuple.Count > 2)
                    {
                        entry.Header = tuple[2] as byte[];
                        if (null != entry.Header && entry.Header.Length > 0)
                        {
                            entry.Size -= (uint)entry.Header.Length;
                            entry.IsPacked = true;
                        }
                    }
                    dir.Add (entry);
                }
            }

            if (dir.Count == 0)
                return null;

            Trace.TraceInformation ("[{0}] [{1:X8}] [{2}]", dir[0].Name, dir[0].Offset, dir[0].Size);
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            Stream input;
            if (0 != entry.Size)
                input = arc.File.CreateStream (entry.Offset, entry.Size);
            else
                input = Stream.Null;
            var rpa_entry = entry as RpaEntry;
            if (null == rpa_entry || null == rpa_entry.Header || 0 == rpa_entry.Header.Length)
                return input;
            return new PrefixStream (rpa_entry.Header, input);
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new RpaOptions { Key = Properties.Settings.Default.RPAKey };
        }

        public override object GetCreationWidget ()
        {
            return new GUI.CreateRPAWidget();
        }

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                     EntryCallback callback)
        {
            var rpa_options    = GetOptions<RpaOptions> (options);
            int callback_count = 0;
            var file_table     = new Dictionary<PyString, ArrayList>();
            long data_offset   = 0x22;
            output.Position    = data_offset;

            foreach (var entry in list)
            {
                if (null != callback)
                    callback (callback_count++, entry, Localization._T ("MsgAddingFile"));

                string name = entry.Name.Replace (@"\", "/");
                var rpa_entry = new RpaEntry { Name = name };
                using (var file = File.OpenRead (entry.Name))
                {
                    var size = file.Length;
                    if (size > uint.MaxValue)
                        throw new FileSizeException();
                    int header_size         = (int)Math.Min (size, 0x10);
                    rpa_entry.Offset        = output.Position ^ rpa_options.Key;
                    rpa_entry.Header        = new byte[header_size];
                    rpa_entry.UnpackedSize  = (uint)size ^ rpa_options.Key;
                    rpa_entry.Size          = (uint)(size - header_size);

                    file.Read (rpa_entry.Header, 0, header_size);
                    file.CopyTo (output);
                }
                var py_name = new PyString (name);
                if (file_table.ContainsKey (py_name))
                    file_table[py_name].Add (rpa_entry);
                else
                    file_table[py_name] = new ArrayList { rpa_entry };
            }
            long index_pos = output.Position;
            string signature = string.Format (CultureInfo.InvariantCulture, "RPA-3.0 {0:x16} {1:x8}\n",
                                              index_pos, rpa_options.Key);
            var header = Encoding.ASCII.GetBytes (signature);
            if (header.Length > data_offset)
                throw new ApplicationException ("Signature serialization failed.");

            if (null != callback)
                callback (callback_count++, null, Localization._T ("MsgWritingIndex"));

            using (var index = new ZLibStream (output, CompressionMode.Compress, CompressionLevel.Level9, true))
            {
                var pickle = new Pickle (index);
                if (!pickle.Dump (file_table))
                    throw new ApplicationException ("Archive index serialization failed.");
            }
            output.Position = 0;
            output.Write (header, 0, header.Length);
        }
    }
    public class Pickle
    {
        Stream                  m_stream;

        ArrayList               m_stack = new ArrayList();
        Stack<int>              m_marks = new Stack<int>();
        Dictionary<int, object> m_memo  = new Dictionary<int, object>();

        const int HIGHEST_PROTOCOL = 5;
        const int BATCHSIZE         = 1000;
        const byte PROTO            = 0x80; /* identify pickle protocol */
        const byte TUPLE2           = 0x86; /* build 2-tuple from two topmost stack items */
        const byte TUPLE3           = 0x87; /* build 3-tuple from three topmost stack items */
        const byte LONG1            = 0x8A; /* push long from < 256 bytes */
        const byte LONG4            = 0x8B; /* push really big long */
        const byte SHORT_BINUNICODE = 0x8C; /* protocol 4 */
        const byte BINUNICODE8      = 0x8D; /* protocol 4 */
        const byte BINBYTES8        = 0x8E; /* protocol 4 */
        const byte EMPTY_SET        = 0x8F; /* protocol 4 */
        const byte ADDITEMS         = 0x90; /* protocol 4 */
        const byte FROZENSET        = 0x91; /* protocol 4 */
        const byte NEWOBJ_EX        = 0x92; /* protocol 4 */
        const byte STACK_GLOBAL     = 0x93; /* protocol 4 */
        const byte MEMOIZE          = 0x94; /* protocol 4 */
        const byte FRAME            = 0x95; /* protocol 4 */
        const byte BYTEARRAY8       = 0x96; /* protocol 5 */
        const byte NEXT_BUFFER      = 0x97; /* protocol 5 */
        const byte READONLY_BUFFER  = 0x98; /* protocol 5 */
        const byte MARK             = (byte)'(';
        const byte STOP             = (byte)'.';
        const byte BINBYTES         = (byte)'B'; /* protocol 3 */
        const byte SHORT_BINBYTES   = (byte)'C'; /* protocol 3 */
        const byte INT              = (byte)'I';
        const byte BININT           = (byte)'J';
        const byte BININT1          = (byte)'K';
        const byte BININT2          = (byte)'M';
        const byte BINSTRING        = (byte)'T';
        const byte SHORT_BINSTRING  = (byte)'U';
        const byte BINUNICODE       = (byte)'X';
        const byte EMPTY_LIST       = (byte)']';
        const byte APPEND           = (byte)'a';
        const byte APPENDS          = (byte)'e';
        const byte BINGET           = (byte)'h';
        const byte BINPUT           = (byte)'q';
        const byte LONG_BINPUT      = (byte)'r';
        const byte SETITEM          = (byte)'s';
        const byte TUPLE            = (byte)'t';
        const byte SETITEMS         = (byte)'u';
        const byte EMPTY_DICT       = (byte)'}';

        public Pickle (Stream stream)
        {
            m_stream = stream;
        }

        public bool Dump (object obj)
        {
            m_stream.WriteByte (PROTO);
            m_stream.WriteByte ((byte)HIGHEST_PROTOCOL);
            if (!Save (obj))
                return false;
            m_stream.WriteByte (STOP);
            return true;
        }

        bool Save (object obj)
        {
            if (null == obj)
            {
                Trace.WriteLine ("Null reference not serialized", "Pickle.Save");
                return false;
            }
            switch (Type.GetTypeCode (obj.GetType()))
            {
            case TypeCode.Byte:     return SaveInt ((uint)(byte)obj);
            case TypeCode.SByte:    return SaveInt ((uint)(sbyte)obj);
            case TypeCode.UInt16:   return SaveInt ((uint)(ushort)obj);
            case TypeCode.Int16:    return SaveInt ((uint)(short)obj);
            case TypeCode.Int32:    return SaveInt ((uint)(int)obj);
            case TypeCode.UInt32:   return SaveInt ((uint)obj);
            case TypeCode.Int64:    return SaveLong ((long)obj);
            case TypeCode.UInt64:   return SaveLong ((long)(ulong)obj);
            case TypeCode.Object:   break;
            default:
                Trace.WriteLine (obj, "Object could not be serialized");
                return false;
            }
            if (obj is RpaEntry)
                return SaveEntry (obj as RpaEntry);
            if (obj is PyString)
                return SaveString (obj as PyString);
            if (obj is byte[])
                return SaveString (obj as byte[]);
            if (obj is IDictionary)
                return SaveDict (obj as IDictionary);
            if (obj is IList)
                return SaveList (obj as IList);

            Trace.WriteLine (obj, "Object could not be serialized");
            return false;
        }

        bool SaveString (byte[] str)
        {
            int size = str.Length;
            if (size < 256)
            {
                m_stream.WriteByte (SHORT_BINSTRING);
                m_stream.WriteByte ((byte)size);
            }
            else
            {
                m_stream.WriteByte (BINSTRING);
                PutInt (size);
            }
            m_stream.Write (str, 0, size);
            return true;
        }

        bool SaveString (PyString str)
        {
            if (str.IsAscii)
                return SaveString (str.Bytes);
            m_stream.WriteByte (BINUNICODE);
            PutInt (str.Length);
            m_stream.Write (str.Bytes, 0, str.Length);
            return true;
        }

        bool SaveEntry (RpaEntry entry)
        {
            byte opcode = null == entry.Header ? TUPLE2 : TUPLE3;
            SaveLong (entry.Offset);
            SaveInt ((uint)entry.UnpackedSize);
            if (null != entry.Header)
                SaveString (entry.Header);
            m_stream.WriteByte (opcode);
            return true;
        }

        bool SaveList (IList list)
        {
            m_stream.WriteByte (EMPTY_LIST);
            if (0 == list.Count)
                return true;
            return BatchList (list.GetEnumerator());
        }

        bool BatchList (IEnumerator iterator)
        {
            int n = 0;
            do
            {
                if (!iterator.MoveNext())
                    break;
                var first_item = iterator.Current;
                if (!iterator.MoveNext())
                {
                    if (!Save (first_item))
                        return false;
                    m_stream.WriteByte (APPEND);
                    break;
                }
                m_stream.WriteByte (MARK);
                if (!Save (first_item))
                    return false;
                n = 1;
                do
                {
                    if (!Save (iterator.Current))
                        return false;
                    if (++n == BATCHSIZE)
                        break;
                }
                while (iterator.MoveNext());
                m_stream.WriteByte (APPENDS);
            }
            while (n == BATCHSIZE);
            return true;
        }

        bool SaveInt (uint i)
        {
            byte[] buf = new byte[5];
            buf[1] = (byte)( i        & 0xff);
            buf[2] = (byte)((i >> 8)  & 0xff);
            buf[3] = (byte)((i >> 16) & 0xff);
            buf[4] = (byte)((i >> 24) & 0xff);
            int length;
            if (0 == buf[4] && 0 == buf[3])
            {
                if (0 == buf[2])
                {
                    buf[0] = BININT1;
                    length = 2;
                }
                else
                {
                    buf[0] = BININT2;
                    length = 3;
                }
            }
            else
            {
                buf[0] = BININT;
                length = 5;
            }
            m_stream.Write (buf, 0, length);
            return true;
        }

        bool SaveLong (long l)
        {
            if (0 == ((l >> 32) & 0xffffffff))
                return SaveInt ((uint)l);
            m_stream.WriteByte (INT);
            string num = l.ToString (CultureInfo.InvariantCulture);
            var num_data = Encoding.ASCII.GetBytes (num);
            m_stream.Write (num_data, 0, num_data.Length);
            m_stream.WriteByte (0x0a);
            return true;
        }

        bool SaveDict (IDictionary dict)
        {
            m_stream.WriteByte (EMPTY_DICT);
            if (0 == dict.Count)
                return true;
            return BatchDict (dict);
        }

        bool BatchDict (IDictionary dict)
        {
            int dict_size = dict.Count;
            var iterator = dict.GetEnumerator();
            if (1 == dict_size)
            {
                if (!iterator.MoveNext())
                    return false;
                if (!Save (iterator.Key))
                    return false;
                if (!Save (iterator.Value))
                    return false;
                m_stream.WriteByte (SETITEM);
                return true;
            }
            int i;
            do
            {
                i = 0;
                m_stream.WriteByte (MARK);
                while (iterator.MoveNext())
                {
                    if (!Save (iterator.Key))
                        return false;
                    if (!Save (iterator.Value))
                        return false;
                    if (++i == BATCHSIZE)
                        break;
                }
                m_stream.WriteByte (SETITEMS);
            }
            while (i == BATCHSIZE);
            return true;
        }

        bool PutInt (int i)
        {
            m_stream.WriteByte ((byte)(i & 0xff));
            m_stream.WriteByte ((byte)((i >> 8) & 0xff));
            m_stream.WriteByte ((byte)((i >> 16) & 0xff));
            m_stream.WriteByte ((byte)((i >> 24) & 0xff));
            return true;
        }

        public object Load ()
        {
            m_memo.Clear();
            for (;;)
            {
                int sym = m_stream.ReadByte();
                switch (sym)
                {
                case PROTO:
                    if (!LoadProto())
                        break;
                    continue;

                case FRAME:
                    if (!LoadFrame())
                        break;
                    continue;

                case EMPTY_DICT:
                    if (!LoadEmptyDict())
                        break;
                    continue;

                case BINPUT:
                    if (!LoadBinPut())
                        break;
                    continue;

                case LONG_BINPUT:
                    if (!LoadLongBinPut())
                        break;
                    continue;

                case BINGET:
                    if (!LoadBinGet())
                        break;
                    continue;

                case MARK:
                    if (!LoadMark())
                        break;
                    continue;

                case SHORT_BINSTRING:
                    if (!LoadShortBinstring())
                        break;
                    continue;

                case BINSTRING:
                case BINUNICODE:
                    if (!LoadBinUnicode())
                        break;
                    continue;

                case SHORT_BINUNICODE:
                    if (!LoadShortBinUnicode())
                        break;
                    continue;

                case BINUNICODE8:
                    if (!LoadBinUnicode8())
                        break;
                    continue;

                case BINBYTES:
                    if (!LoadBinBytes())
                        break;
                    continue;

                case SHORT_BINBYTES:
                    if (!LoadShortBinBytes())
                        break;
                    continue;

                case BINBYTES8:
                    if (!LoadBinBytes8())
                        break;
                    continue;

                case EMPTY_LIST:
                    if (!LoadEmptyList())
                        break;
                    continue;

                case EMPTY_SET:
                    if (!LoadEmptySet())
                        break;
                    continue;

                case BININT:
                    if (!LoadBinInt (4))
                        break;
                    continue;

                case BININT1:
                    if (!LoadBinInt (1))
                        break;
                    continue;

                case BININT2:
                    if (!LoadBinInt (2))
                        break;
                    continue;

                case INT:
                    if (!LoadInt())
                        break;
                    continue;

                case TUPLE:
                    if (!LoadTuple())
                        break;
                    continue;

                case TUPLE2:
                    if (!LoadCountedTuple (2))
                        break;
                    continue;

                case TUPLE3:
                    if (!LoadCountedTuple (3))
                        break;
                    continue;

                case LONG1:
                    if (!LoadLong())
                        break;
                    continue;

                case LONG4:
                    if (!LoadLong4())
                        break;
                    continue;

                case APPEND:
                    if (!LoadAppend())
                        break;
                    continue;

                case APPENDS:
                    if (!LoadAppends())
                        break;
                    continue;

                case ADDITEMS:
                    if (!LoadAddItems())
                        break;
                    continue;

                case FROZENSET:
                    if (!LoadFrozenSet())
                        break;
                    continue;

                case SETITEM:
                    if (!LoadSetItem())
                        break;
                    continue;

                case SETITEMS:
                    if (!LoadSetItems())
                        break;
                    continue;

                case MEMOIZE:
                    if (!LoadMemoize())
                        break;
                    continue;

                case BYTEARRAY8:
                    if (!LoadByteArray8())
                        break;
                    continue;

                case NEXT_BUFFER:
                    if (!LoadNextBuffer())
                        break;
                    continue;

                case READONLY_BUFFER:
                    if (!LoadReadOnlyBuffer())
                        break;
                    continue;

                case STOP:
                    break;

                case -1: // EOF
                case 0:
                    Trace.WriteLine ("Unexpected end of file", "Pickle.Load");
                    return null;

                default:
                    Trace.TraceError ("Unknown Pickle serialization opcode 0x{0:X2}", sym);
                    return null;
                }
                break;
            }
            if (0 == m_stack.Count)
            {
                Trace.WriteLine ("Invalid pickle data", "Pickle.Load");
                return null;
            }
            return m_stack.Pop();
        }

        bool LoadProto ()
        {
            int i = m_stream.ReadByte();
            if (-1 == i)
                return false;
            if (i > HIGHEST_PROTOCOL)
                return false;
            return true;
        }

        bool LoadFrame ()
        {
            long frame_size;
            if (!ReadLong (8, out frame_size) || frame_size < 0)
                return false;
            return true;
        }

        bool LoadEmptyDict ()
        {
            m_stack.Push (new Hashtable());
            return true;
        }

        bool LoadBinPut ()
        {
            int key = m_stream.ReadByte();
            if (-1 == key || 0 == m_stack.Count)
                return false;
            m_memo[key] = m_stack.Peek();
            return true;
        }

        bool LoadLongBinPut ()
        {
            int key;
            if (!ReadInt (4, out key) || 0 == m_stack.Count || key < 0)
                return false;
            m_memo[key] = m_stack.Peek();
            return true;
        }

        bool LoadBinGet ()
        {
            int key = m_stream.ReadByte();
            if (-1 == key)
                return false;
            if (m_memo.ContainsKey(key))
                m_stack.Push(m_memo[key]);
            else
                return false;
            return true;
        }

        bool LoadMark ()
        {
            m_marks.Push (m_stack.Count);
            return true;
        }

        int GetMarker ()
        {
            if (0 == m_marks.Count)
            {
                Trace.TraceError("RPA Pickle MARK list is empty");
                return -1;
            }
            return m_marks.Pop();
        }

        bool LoadShortBinstring ()
        {
            int length = m_stream.ReadByte();
            if (-1 == length)
                return false;
            return LoadBinString (length);
        }

        bool LoadBinUnicode ()
        {
            int length;
            if (!ReadInt (4, out length))
                return false;
            return LoadBinString (length);
        }

        bool LoadShortBinUnicode ()
        {
            int length = m_stream.ReadByte();
            if (-1 == length)
                return false;

            return LoadBinString (length);
        }

        bool LoadBinUnicode8 ()
        {
            long length;
            if (!ReadLong(8, out length) || length < 0 || length > int.MaxValue)
                return false;

            return LoadBinString ((int)length);
        }

        bool LoadBinString (int length)
        {
            var bytes = new byte[length];
            if (length != m_stream.Read (bytes, 0, length))
                return false;
            m_stack.Push (bytes);
            return true;
        }

        bool LoadBinBytes ()
        {
            int length;
            if (!ReadInt (4, out length) || length < 0)
                return false;

            var bytes = new byte[length];
            if (length != m_stream.Read (bytes, 0, length))
                return false;

            m_stack.Push (bytes);
            return true;
        }

        bool LoadShortBinBytes ()
        {
            int length = m_stream.ReadByte();
            if (-1 == length)
                return false;

            var bytes = new byte[length];
            if (length != m_stream.Read (bytes, 0, length))
                return false;

            m_stack.Push (bytes);
            return true;
        }

        bool LoadBinBytes8 ()
        {
            long length;
            if (!ReadLong (8, out length) || length < 0 || length > int.MaxValue)
                return false;

            var bytes = new byte[length];
            if ((int)length != m_stream.Read (bytes, 0, (int)length))
                return false;

            m_stack.Push (bytes);
            return true;
        }

        bool LoadEmptyList ()
        {
            m_stack.Push (new ArrayList());
            return true;
        }

        bool LoadEmptySet ()
        {
            m_stack.Push(new HashSet<object>());
            return true;
        }

        bool ReadInt (int size, out int value)
        {
            value = 0;
            for (int i = 0; i < size; ++i)
            {
                int b = m_stream.ReadByte();
                if (-1 == b)
                    return false;
                value |= b << (i * 8);
            }
            return true;
        }

        bool ReadLong (int size, out long value)
        {
            value = 0;
            for (int i = 0; i < size; ++i)
            {
                int b = m_stream.ReadByte();
                if (-1 == b)
                    return false;
                value |= (long)b << (i * 8);
            }
            return true;
        }

        bool LoadBinInt (int size)
        {
            int x = 0;
            if (!ReadInt (size, out x))
                return false;
            m_stack.Push (x);
            return true;
        }

        bool LoadInt ()
        {
            var num = m_stream.ReadStringUntil (0x0a, Encoding.ASCII);
            long n;
            if (!long.TryParse (num, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                return false;
            m_stack.Push (n);
            return true;
        }

        bool LoadLong ()
        {
            int count = m_stream.ReadByte();
            if (-1 == count)
                return false;
            m_stack.Push (DecodeLong (count));
            return true;
        }

        bool LoadLong4 ()
        {
            int count = 0;
            if (!ReadInt (4, out count) || count < 0)
                return false;
            m_stack.Push (DecodeLong (count));
            return true;
        }

        object DecodeLong (int count)
        {
            if (count <= 0)
                return 0L;
            else if (count > 8)
            {
                var bytes = new byte[count];
                m_stream.Read (bytes, 0, count);
                return new BigInteger (bytes);
            }
            else
            {
                var bytes = new byte[8];
                m_stream.Read (bytes, 0, count);
                if (0 != (bytes[count-1] & 0x80)) // sign bit is set
                {
                    for (int i = count; i < bytes.Length; ++i)
                        bytes[i] = 0xFF;
                }
                return bytes.ToInt64 (0);
            }
        }

        bool LoadTuple ()
        {
            int mark = GetMarker();
            if (mark < 0)
                return false;
            return LoadCountedTuple (m_stack.Count - mark);
        }

        bool LoadCountedTuple (int count)
        {
            if (m_stack.Count < count)
                return false;
            var tuple = new ArrayList (count);
            int start = m_stack.Count - count;
            for (int i = 0; i < count; ++i)
            {
                tuple.Add (m_stack[start + i]);
            }
            m_stack.RemoveRange (start, count);
            m_stack.Push (tuple);
            return true;
        }

        bool LoadAppend ()
        {
            int x = m_stack.Count - 1;
            if (x <= 0)
            {
                Trace.WriteLine ("Stack underflow", "LoadAppend");
                return false;
            }
            var list = m_stack[x-1] as ArrayList;
            if (null == list)
            {
                Trace.WriteLine ("Object is not a list", "LoadAppend");
                return false;
            }
            list.Add (m_stack[x]);
            m_stack.RemoveAt (x);
            return true;
        }

        bool LoadAppends ()
        {
            int mark = GetMarker();
            if (mark < 0)
                return false;

            var list = m_stack[mark - 1] as ArrayList;
            if (null == list)
            {
                Trace.WriteLine ("Marked object is not a list", "LoadAppends");
                return false;
            }

            var slice = PdataPopList(mark);
            if (null == slice)
                return false;
            list.AddRange(slice);
            return true;
        }

        bool LoadAddItems ()
        {
            int mark = GetMarker();
            if (mark < 0)
                return false;

            var set = m_stack[mark - 1] as HashSet<object>;
            if (null == set)
            {
                Trace.WriteLine ("Marked object is not a set", "LoadAddItems");
                return false;
            }

            for (int i = mark; i < m_stack.Count; ++i)
            {
                set.Add (m_stack[i]);
            }

            return PdataClear (mark);
        }

        bool LoadFrozenSet ()
        {
            int mark = GetMarker();
            if (mark < 0)
                return false;

            var items = new HashSet<object>();
            for (int i = mark; i < m_stack.Count; ++i)
            {
                items.Add (m_stack[i]);
            }

            PdataClear (mark);

            m_stack.Push (items);
            return true;
        }

        bool LoadMemoize ()
        {
            if (0 == m_stack.Count)
                return false;

            // Find the next available memo key
            int key = m_memo.Count;
            m_memo[key] = m_stack.Peek();
            return true;
        }

        bool LoadByteArray8 ()
        {
            if (m_stack.Count == 0)
                return false;

            var obj = m_stack.Pop();
            if (!(obj is long || obj is ulong || obj is BigInteger))
                return false;

            ulong length;
            if (obj is BigInteger bigInt)
            {
                if (bigInt < 0 || bigInt > ulong.MaxValue)
                    return false;
                length = (ulong)bigInt;
            }
            else
            {
                length = Convert.ToUInt64(obj);
            }

            if (length > int.MaxValue)
                return false;

            var bytes = new byte[length];
            if (m_stream.Read (bytes, 0, (int)length) != (int)length)
                return false;

            m_stack.Push (bytes);
            return true;
        }

        bool LoadNextBuffer ()
        {
            Trace.TraceError ("NEXT_BUFFER opcode requires out-of-band buffer support");
            return false;
        }

        bool LoadReadOnlyBuffer ()
        {
            if (m_stack.Count == 0)
                return false;
            return true;
        }

        ArrayList PdataPopList (int start)
        {
            int count = m_stack.Count - start;
            var list = new ArrayList (count);
            for (int i = start; i < m_stack.Count; ++i)
                list.Add (m_stack[i]);
            m_stack.RemoveRange (start, count);
            return list;
        }

        bool LoadSetItem ()
        {
            return DoSetItems (m_stack.Count-2);
        }

        bool LoadSetItems ()
        {
            return DoSetItems (GetMarker());
        }

        bool DoSetItems (int mark)
        {
            if (!(m_stack.Count >= mark && mark > 0))
            {
                Trace.WriteLine ("Stack underflow", "LoadSetItems");
                return false;
            }
            var dict = m_stack[mark-1] as Hashtable;
            if (null == dict)
            {
                Trace.WriteLine ("Marked object is not a dictionary", "LoadSetItems");
                return false;
            }
            for (int i = mark+1; i < m_stack.Count; i += 2)
            {
                var key   = m_stack[i-1];
                var value = m_stack[i];
                dict[key] = value;
            }
            return PdataClear (mark);
        }

        bool PdataClear (int clearto)
        {
            if (clearto < 0)
                return false;
            if (clearto < m_stack.Count)
                m_stack.RemoveRange (clearto, m_stack.Count-clearto);
            return true;
        }
    }

    static public class ArrayListEx
    {
        static public object Peek (this ArrayList array)
        {
            return array[array.Count-1];
        }

        static public void Push (this ArrayList array, object item)
        {
            array.Add (item);
        }

        static public object Pop (this ArrayList array)
        {
            var item = array[array.Count-1];
            array.RemoveAt (array.Count-1);
            return item;
        }
    }

    internal class PyString : IEquatable<PyString>
    {
        int         m_hash;
        byte[]      m_bytes;
        Lazy<bool>  m_is_ascii;

        public PyString (string s)
        {
            m_hash = s.GetHashCode();
            m_bytes = Encoding.UTF8.GetBytes (s);
            m_is_ascii = new Lazy<bool> (() => -1 == Array.FindIndex (m_bytes, x => x > 0x7f));
        }

        public PyString () : this ("")
        {
        }

        public bool IsAscii { get { return m_is_ascii.Value; } }

        public byte[] Bytes { get { return m_bytes; } }

        public int   Length { get { return m_bytes.Length; } }

        public bool Equals (PyString other)
        {
            if (null == other)
                return false;
            if (this.m_hash != other.m_hash)
                return false;
            if (this.Length != other.Length)
                return false;
            for (var i = 0; i < m_bytes.Length; ++i)
                if (m_bytes[i] != other.m_bytes[i])
                    return false;
            return true;
        }

        public override bool Equals (object other)
        {
            return this.Equals (other as PyString);
        }

        public override int GetHashCode ()
        {
            return m_hash;
        }

        public override string ToString ()
        {
            return Encoding.UTF8.GetString (m_bytes);
        }
    }
}
