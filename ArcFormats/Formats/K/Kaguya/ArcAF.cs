using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Kaguya
{
    [Export(typeof(ArchiveFormat))]
    public class AfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/AF01"; } }
        public override string Description { get { return "Atelier Kaguya resource archive"; } }
        public override uint     Signature { get { return 0x31304641; } } // 'AF01'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        const int MaxFileNameLength = 0x100;

        public override ArcFile TryOpen (ArcView file)
        {
            uint index_offset = file.View.ReadUInt32 (8);
            if (index_offset >= file.MaxOffset - 8)
                return null;
            using (var index = file.CreateStream (index_offset + 8))
            {
                long data_offset = 12;
                var name_buffer = new byte[MaxFileNameLength];
                var dir = new List<Entry>();
                while (index.PeekByte() != -1)
                {
                    int name_length = index.ReadInt32();
                    if (name_length <= 0 || name_length > name_buffer.Length)
                        return null;
                    if (name_length != index.Read (name_buffer, 0, name_length))
                        return null;
                    var name = DecryptString (name_buffer, name_length);
                    name = name.TrimStart ('\\', '/');
                    var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                    int flags = index.ReadInt16();
                    entry.IsPacked = 1 == flags;
                    data_offset += 4 + name_length + 6;
                    if (entry.IsPacked)
                        data_offset += 4;
                    entry.Offset = data_offset;
                    entry.Size = index.ReadUInt32();
                    entry.UnpackedSize = index.ReadUInt32();
                    if (!entry.IsPacked)
                        entry.Size = entry.UnpackedSize;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    data_offset += entry.Size;
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            using (var input = arc.File.CreateStream (entry.Offset, entry.Size))
            {
                var output = new byte[pent.UnpackedSize];
                LzUnpack (input, output);
                return new BinMemoryStream (output);
            }
        }

        string DecryptString (byte[] name, int length)
        {
            for (int i = 0; i < length; ++i)
                name[i] ^= 0xFF;
            return Encodings.cp932.GetString (name, 0, length);
        }

        void LzUnpack (Stream input, byte[] output)
        {
            var frame = new byte[0x1000];
            int frame_pos = 1;
            int dst = 0;
            using (var bits = new MsbBitStream (input))
            {
                while (dst < output.Length)
                {
                    if (0 != bits.GetNextBit ())
                    {
                        byte b = (byte)bits.GetBits (8);
                        output[dst++] = b;
                        frame[frame_pos++ & 0xFFF] = b;
                    }
                    else
                    {
                        int offset = bits.GetBits (12);
                        int count = bits.GetBits (4) + 2;
                        for (int i = 0; i < count; ++i)
                        {
                            byte b = frame[(offset + i) & 0xFFF];
                            output[dst++] = b;
                            frame[frame_pos++ & 0xFFF] = b;
                        }
                    }
                }
            }
        }
    }
}
