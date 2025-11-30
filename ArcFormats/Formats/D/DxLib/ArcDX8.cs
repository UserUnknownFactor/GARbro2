using System;
using System.CodeDom;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Windows.Navigation;

using static GameRes.Formats.DxLib.Dx8Opener;

namespace GameRes.Formats.DxLib
{

    internal class DXA8PackedEntry : PackedEntry {
        public bool HuffmanCompressed { get; set; }
        public uint HuffmanSize { get; set; }

        public uint LZSize { get; set; }
    }

    internal class DxArchive8 : DxArchive
    {
        public byte huffmanMaxKB;

        public DxArchive8 (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, 
                            IDxKey enc, int version,byte huffmanKB) : base (arc, impl, dir, enc, version)
        {
            huffmanMaxKB = huffmanKB;
        }
    }

    internal class DxHeaderV8 : DxHeader
    {
        public DXA8Flags Flags;
        public byte HuffmanKB; // oddly used only in Compression process not in decompression.
                               //15 bytes of padding.
    }

    [Export(typeof(ArchiveFormat))]
    [ExportMetadata("Priority", -1)]
    public class Dx8Opener : DxOpener
    {
        public override string         Tag { get { return "DXLIB"; } }
        public override string Description { get { return "DxLib archive"; } }
        public override uint     Signature { get { return  0x00085844; } }
        public override bool  IsHierarchic { get { return  true; } }
        public override bool      CanWrite { get { return  false; } }

        public Dx8Opener ()
        {
            Extensions = new string[] { "dxa", "hud", "usi", "med", "dat", "bin", "bcx", "wolf" };
            Signatures = new[] { 0x00085844u };
        }

        internal enum DXA8Flags : UInt32
        {
            DXA_FLAG_NO_KEY        = 1,      // file is not encrypted
            DXA_FLAG_NO_HEAD_PRESS = 1 << 1, // don't compress the header after compressing individual entries
        }

        [Serializable]
        public class DXAOpts : ResourceOptions
        {
            public string Keyword;
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new DXAOpts {
                Keyword = Properties.Settings.Default.DXA_Password
            };
        }

        public override ResourceOptions GetOptions (object widget)
        {
            if (widget is WidgetPassword)
            {
                return new DXAOpts {
                    Keyword = ((WidgetPassword)widget).Password
                };
            }
            return GetDefaultOptions();
        }

        public override object GetAccessWidget ()
        {
            var parentFormat = FormatCatalog.Instance.Formats.FirstOrDefault(f => f.GetType() == typeof(DxOpener));
            return new WidgetPassword
            {
                FormatTag = parentFormat?.Tag,
                Scheme = parentFormat?.Scheme
            };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            var dx = new DxHeaderV8 {
                IndexSize   = file.View.ReadUInt32 (4),
                BaseOffset  = file.View.ReadInt64  (8),
                IndexOffset = file.View.ReadInt64  (0x10),
                FileTable   = file.View.ReadInt64  (0x18),
                DirTable    = file.View.ReadInt64  (0x20),
                CodePage    = file.View.ReadInt32  (0x28),
                Flags       = (DXA8Flags)file.View.ReadUInt32 (0x2C),
                HuffmanKB   = file.View.ReadByte   (0x30)
            };
            if (dx.DirTable >= dx.IndexSize || dx.FileTable >= dx.IndexSize)
                return null;
            DxKey8 key = null;

            var headerBuffer = file.View.ReadBytes (dx.IndexOffset, file.MaxOffset - dx.IndexOffset);
            bool isencrypted = (dx.Flags & DXA8Flags.DXA_FLAG_NO_KEY) == 0;
            string keyStr = null;
            if (isencrypted)
            {
                keyStr = Query<DXAOpts>(Localization._T ("ArcEncryptedNotice")).Keyword;
                key = new DxKey8 (keyStr,dx.CodePage);
            }

            Decrypt (headerBuffer, 0, headerBuffer.Length, 0, key.Key);
            //Decrypted but might be compressed
            if ((dx.Flags & DXA8Flags.DXA_FLAG_NO_HEAD_PRESS) == 0)
            {
                byte[] huffmanBuffer = new byte[headerBuffer.Length]; 
                byte[] lzBuffer;
                headerBuffer.CopyTo (huffmanBuffer, 0);
                //huffmanBuffer = headerBuffer;
                HuffmanDecoder decoder = new HuffmanDecoder (huffmanBuffer, (ulong)huffmanBuffer.LongLength);
                lzBuffer = decoder.Unpack();
                MemoryStream lzStream = new MemoryStream (lzBuffer);
                headerBuffer = Unpack (lzStream);

            }

            List<Entry> entries;
            using (var reader = IndexReader.Create (dx, 8, new MemoryStream (headerBuffer)))
            {
                entries = reader.Read();
            }
            if (entries == null)
                return null;

            if (isencrypted)
                WidgetPassword.MarkPasswordAsSuccessful("DXA", keyStr);

            Comment = $"Version 8";
            return new DxArchive8 (file, this,entries ,key, 8,dx.HuffmanKB);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            var dx_arc = arc as DxArchive8;
            if (null == dx_arc)
                return input;
            var dx_ent = (DXA8PackedEntry)entry;
            long dec_offset =  dx_ent.UnpackedSize; 
            var key = dx_arc.Encryption.GetEntryKey (dx_ent.Name);
            input = new EncryptedStream (input, dec_offset, key);

            byte[] tmpBuffer = new byte[dx_ent.Size]; 
            input.Read (tmpBuffer, 0, tmpBuffer.Length);
            if (dx_ent.HuffmanCompressed)
            {
                byte[] buffer = new byte[dx_ent.HuffmanSize];
                byte[] outBuffer = new byte[dx_ent.IsPacked ? dx_ent.LZSize : dx_ent.UnpackedSize];
                Array.Copy (tmpBuffer, buffer, dx_ent.HuffmanSize);
                HuffmanDecoder decoder = new HuffmanDecoder (buffer,dx_ent.HuffmanSize);
                byte[] partTmpBuffer = decoder.Unpack();
                //returned buffer might be partial. Check if this is the case.
                var outBufSize = dx_ent.IsPacked ? dx_ent.LZSize : dx_ent.UnpackedSize;
                if (dx_arc.huffmanMaxKB != 0xff && outBufSize > dx_arc.huffmanMaxKB * 1024 * 2)
                {
                    //What we have here is two huffmanMaxKB KB buffers, that constitute the beginning and end of file respectively.
                    Array.Copy (partTmpBuffer,0, outBuffer, 0,dx_arc.huffmanMaxKB*1024);
                    Array.Copy (partTmpBuffer,dx_arc.huffmanMaxKB*1024,outBuffer,outBuffer.Length-dx_arc.huffmanMaxKB*1024,dx_arc.huffmanMaxKB*1024);
                    //uncompressed part goes into middle.
                    Array.Copy (tmpBuffer, dx_ent.HuffmanSize, outBuffer, dx_arc.huffmanMaxKB * 1024, outBufSize - dx_arc.huffmanMaxKB * 1024 * 2);
                    tmpBuffer = outBuffer;
                } else
                {
                    //that is all that needs to be done.
                    tmpBuffer = partTmpBuffer;
                }
            }
            if (dx_ent.IsPacked)
            {
                byte[] buffer = new byte[dx_ent.LZSize];
                tmpBuffer.CopyTo (buffer, 0);
                var tmpMemStream = new MemoryStream (buffer);
                tmpBuffer = Unpack (tmpMemStream);

            }
            return new BinMemoryStream (tmpBuffer, entry.Name);

            /*
            if (!dx_ent.IsPacked)
                return input;
            using (input)
            {
                var data = Unpack (input);
                return new BinMemoryStream (data, entry.Name);
            }
            */
            //return null;
        }
    }

    internal sealed class IndexReaderV8 : IndexReader
    {
        readonly int m_entry_size;
        public IndexReaderV8 (DxHeader header, int version, Stream input) : base (header, version, input)
        {
            m_entry_size = 0x48;
        }
        private class DxDirectory
        {
            public long DirOffset;
            public long ParentDirOffset;
            public  int FileCount;
            public long FileTable;
        }

        DxDirectory ReadDirEntry()
        {
            var dir = new DxDirectory
            {
                DirOffset       = m_input.ReadInt64(),
                ParentDirOffset = m_input.ReadInt64(),
                FileCount       = (int)m_input.ReadInt64(),
                FileTable       = m_input.ReadInt64()
            };
            return dir;
        }

        protected override void ReadFileTable (string root, long table_offset)
        {
            m_input.Position = m_header.DirTable + table_offset;
            var dir = ReadDirEntry();
            if (dir.DirOffset != -1 && dir.ParentDirOffset != -1)
            {
                m_input.Position = m_header.FileTable + dir.DirOffset;
                root = Path.Combine (root, ExtractFileName (m_input.ReadInt64()));
            }
            long current_pos = m_header.FileTable + dir.FileTable;
            for (int i = 0; i < dir.FileCount; ++i)
            {
                m_input.Position = current_pos;
                var name_offset = m_input.ReadInt64();
                uint attr = (uint)m_input.ReadInt64();
                m_input.Seek (0x18, SeekOrigin.Current);
                var offset = m_input.ReadInt64();
                if (0 != (attr & 0x10)) // FILE_ATTRIBUTE_DIRECTORY
                {
                    if (0 == offset || table_offset == offset)
                        throw new InvalidFormatException ("Infinite recursion in DXA directory index");
                    ReadFileTable (root, offset);
                }
                else
                {
                    var size = m_input.ReadInt64();
                    var packed_size = m_input.ReadInt64();
                    var huffman_packed_size = m_input.ReadInt64();
                    var entry = FormatCatalog.Instance.Create<DXA8PackedEntry>(Path.Combine (root, ExtractFileName (name_offset)));
                    entry.Offset = m_header.BaseOffset + offset;
                    entry.UnpackedSize = (uint)size;
                    entry.IsPacked = packed_size != -1;
                    entry.HuffmanCompressed = huffman_packed_size != -1;

                    entry.HuffmanSize = entry.HuffmanCompressed ? (uint)huffman_packed_size : 0;
                    entry.LZSize = entry.IsPacked ? (uint)packed_size : 0;

                    if (entry.HuffmanCompressed)
                    {
                        var outBufSize = entry.IsPacked ? packed_size : size;
                        var dx8_hdr = (DxHeaderV8)m_header;

                        // Check if we're using split Huffman encoding
                        if (dx8_hdr.HuffmanKB != 0xFF && outBufSize > dx8_hdr.HuffmanKB * 1024 * 2)
                        {
                            // Huffman encodes first and last HuffmanKB*1024 bytes
                            // The middle part is stored uncompressed
                            var uncompressedMiddle = outBufSize - dx8_hdr.HuffmanKB * 1024 * 2;
                            entry.Size = (uint)(huffman_packed_size + uncompressedMiddle);
                        }
                        else
                        {
                            // Entire data is Huffman encoded
                            entry.Size = (uint)huffman_packed_size;
                        }
                    }
                    else if (entry.IsPacked)
                        entry.Size = (uint)packed_size;
                    else
                        entry.Size = (uint)size;

                    m_dir.Add (entry);
                }
                current_pos += m_entry_size;
            }
        }
    }
}
