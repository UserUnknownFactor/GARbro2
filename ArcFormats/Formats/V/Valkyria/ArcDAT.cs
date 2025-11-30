using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;

namespace GameRes.Formats.Valkyria
{
    [Export(typeof(ArchiveFormat))]
    public class VDatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/VALKYRIA"; } }
        public override string Description { get { return "Valkyria DAT resource archive"; } }
        public override uint     Signature { get { return  0; } }
        public override bool  IsHierarchic { get { return  false; } }
        public override bool      CanWrite { get { return  false; } }

        private static readonly Encoding AnsiEncoding = Encoding.GetEncoding (932);

        internal const uint V2_Const1 = 0xE1DA85E3;
        internal const int    V2_Rot1 = 3;
        internal const uint V2_Const2 = 0x627E907B;
        internal const int    V2_Rot2 = 7;

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension ("DAT"))
                return null;

            uint first_dword = file.View.ReadUInt32 (0);

            if (first_dword == 0)
            {
                uint second_dword = file.View.ReadUInt32 (4);
                if (second_dword == 1)
                    return TryOpenFormat2 (file, 12); // [0][1][size]
                else
                    return TryOpenFormat1 (file);     // [0][size]
            }

            if (first_dword == 1)
                return TryOpenFormat2 (file, 8);      // [1][size]

            // Unencrypted format
            uint index_size = first_dword;
            if (index_size >= file.MaxOffset)
                return null;

            return ReadUnencryptedIndex (file, index_size);
        }

        private ArcFile ReadUnencryptedIndex (ArcView file, uint index_size)
        {
            int count = (int)index_size / 0x10C;
            if (index_size != (uint)count * 0x10Cu || !IsSaneCount (count))
                return null;

            uint index_offset = 4;
            long base_offset = index_offset + index_size;
            var dir = new List<Entry> (count);

            for (int i = 0; i < count; ++i)
            {
                var name = ReadCString (file.View.ReadBytes (index_offset, 0x104));
                index_offset += 0x104;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = base_offset + file.View.ReadUInt32 (index_offset);
                entry.Size = file.View.ReadUInt32 (index_offset + 4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                index_offset += 8;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        private ArcFile TryOpenFormat1 (ArcView file)
        {
            uint index_size = file.View.ReadUInt32 (4);
            if (0 == index_size || index_size >= file.MaxOffset)
                return null;

            const int EntrySize = 0x10C;
            const int NameSize = 0x104;

            int count = (int)index_size / EntrySize;
            if (index_size != (uint)count * EntrySize || !IsSaneCount (count))
                return null;

            var dir_path = Path.GetDirectoryName (file.Name);
            if (dir_path == null) return null;

            var arc_key = ReadArcKey (dir_path);
            if (arc_key == null) return null;

            int index_offset = 8;
            long base_offset = index_offset + index_size;
            var dir = new List<Entry> (count);

            for (int i = 0; i < count; ++i)
            {
                var name_bytes = file.View.ReadBytes (index_offset, NameSize);
                var name = ReadCString (name_bytes);
                var key = GetEntryKey (arc_key, name_bytes);
                index_offset += NameSize;

                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = base_offset + (file.View.ReadUInt32 (index_offset) ^ key);
                entry.Size = file.View.ReadUInt32 (index_offset + 4) ^ key;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                index_offset += 8;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        private ArcFile TryOpenFormat2 (ArcView file, int header_size)
        {
            uint index_size = file.View.ReadUInt32 (header_size - 4);
            if (0 == index_size || index_size >= file.MaxOffset)
                return null;

            const int EntrySize = 0x10C;
            const int NameSize = 0x104;

            int count = (int)index_size / EntrySize;
            if (index_size != (uint)count * EntrySize || !IsSaneCount (count))
                return null;

            byte[] index_buffer = file.View.ReadBytes (header_size, index_size);
            DecryptRolRor (index_buffer, index_size);

            var dir = new List<Entry> (count);

            for (int i = 0; i < count; ++i)
            {
                int entry_offset = i * EntrySize;
                var name_bytes = new byte[NameSize];
                Array.Copy (index_buffer, entry_offset, name_bytes, 0, NameSize);
                var name = ReadCString (name_bytes);

                uint raw_offset = BitConverter.ToUInt32 (index_buffer, entry_offset + NameSize);
                uint raw_size = BitConverter.ToUInt32 (index_buffer, entry_offset + NameSize + 4);

                var entry = FormatCatalog.Instance.Create<Entry> (name);

                entry.Offset = header_size + index_size + raw_offset;
                entry.Size = raw_size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;

                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        internal static void DecryptRolRor (byte[] buffer, uint bufferSize)
        {
            bool toggle = false;

            for (int i = 0; i <= (int)bufferSize - 4; i++)
            {
                uint value = BitConverter.ToUInt32 (buffer, i);
                uint result;

                if (toggle)
                {
                    result = RolRor (1, value, V2_Const2, V2_Rot2, bufferSize);
                    toggle = false;
                }
                else
                {
                    result = RolRor (1, value, V2_Const1, V2_Rot1, bufferSize);
                    toggle = true;
                }

                buffer[i] = (byte)result;
                buffer[i + 1] = (byte)(result >> 8);
                buffer[i + 2] = (byte)(result >> 16);
                buffer[i + 3] = (byte)(result >> 24);
            }
        }

        internal static uint RolRor (int mode, uint value, uint constant, int rotation, uint key)
        {
            if (mode != 0)
                return key ^ RotateRight (value - constant, rotation);
            else
                return constant + RotateLeft (key ^ value, rotation);
        }

        private static uint RotateRight (uint value, int count)
        {
            count &= 31;
            return (value >> count) | (value << (32 - count));
        }

        private static uint RotateLeft (uint value, int count)
        {
            count &= 31;
            return (value << count) | (value >> (32 - count));
        }

        private static byte[] ReadArcKey (string dir_path)
        {
            var file_path = Path.Combine (dir_path, "system.dat");
            if (!File.Exists (file_path))
                return null;

            var key = new byte[4];
            using (var fs = File.OpenRead (file_path))
            {
                fs.Position = 0x10E;
                var name_bytes = new byte[260];
                fs.Read (name_bytes, 0, 260);
                var len = CStringLength (name_bytes);

                for (int i = len, j = 0; i > 0; i--)
                {
                    key[j] += name_bytes[i];
                    if (++j == 4)
                        j = 0;
                }
            }
            return key;
        }

        private static uint GetEntryKey (byte[] arc_key, byte[] name_bytes)
        {
            var key = new byte[4];
            var len = CStringLength (name_bytes);

            for (int i = len, j = 0; i > 0; i--)
            {
                key[j] += name_bytes[i];
                if (++j == 4)
                    j = 0;
            }

            key[0] += arc_key[3];
            key[1] += arc_key[2];
            key[2] += arc_key[1];
            key[3] += arc_key[0];

            return BitConverter.ToUInt32 (key, 0);
        }

        private static int CStringLength (byte[] bytes)
        {
            var len = Array.IndexOf<byte> (bytes, 0);
            if (len == -1) len = bytes.Length;
            return len;
        }

        private static string ReadCString (byte[] bytes)
        {
            int len = Array.IndexOf<byte> (bytes, 0);
            if (len == -1) len = bytes.Length;
            return AnsiEncoding.GetString (bytes, 0, len);
        }
    }
}
