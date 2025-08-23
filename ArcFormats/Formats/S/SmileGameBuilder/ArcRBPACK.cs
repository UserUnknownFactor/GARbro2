using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GameRes.Compression;
using GameRes.Utility;
using GameRes.Formats.PkWare;

namespace GameRes.Formats.RbPack
{
    [Export(typeof(ArchiveFormat))]
    public class RbPackOpener : ArchiveFormat
    {
        public override string         Tag { get { return "RBPACK"; } }
        public override string Description { get { return "Bakin engine archive"; } }
        public override uint     Signature { get { return  0x504e4b42; } } // 'BKNP'
        public override bool  IsHierarchic { get { return  true; } }
        public override bool      CanWrite { get { return  false; } }

        readonly EncodingSetting ZipEncoding = new EncodingSetting("ZIPEncodingCP", "DefaultEncoding");

        static readonly byte[] Key1 = { 
            0x00, 0x08, 0x0E, 0x09, 0x14, 0x3C, 0x42, 0x46,
            0x48, 0x09, 0x14, 0x9A, 0x30, 0xA9, 0x54, 0xE1 };
        static readonly byte[] Key2 = { 
            0x00, 0x0E, 0x08, 0x1E, 0x18, 0x37, 0x12, 0x00,
            0x48, 0x87, 0x46, 0x0B, 0x9C, 0x68, 0xA8, 0x4B };
        static readonly byte[] ZipHeader = { 
            0x50, 0x4B, 0x03, 0x04, 0x20, 0x00, 0x00, 0x00 };

        static readonly string[] PrimaryTypes = { ".cg", ".cgh", ".dlp_d", ".exe", ".dll", ".dlp", ".webm" };

        public override ArcFile TryOpen(ArcView file)
        {
            if (!file.View.AsciiEqual(0, "BKNPAK"))
                return null;

            ushort version = Binary.BigEndian(file.View.ReadUInt16(6));
            long resourceTableOffset = file.View.ReadInt64(8) + 16;

            // Read header info
            long pos = 16;
            byte strLength = file.View.ReadByte(pos++);
            if (strLength == 1)
                strLength = file.View.ReadByte(pos++);

            string loadingFormTitle = file.View.ReadString(pos, strLength, Encoding.UTF8);
            pos += strLength;

            uint verifyCode = file.View.ReadUInt32(pos);
            pos += 4;

            byte md5Length = file.View.ReadByte(pos++);
            var md5Hash = file.View.ReadBytes(pos, md5Length);
            pos += md5Length;

            if (!VerifyArchive(verifyCode, md5Hash))
                return null;

            var dir = new List<Entry>();

            long zipLength = file.View.ReadInt64(pos);
            pos += 8;

            var zipData = new byte[zipLength - 8];
            file.View.Read(pos, zipData, 0, (uint)zipData.Length);
            DecryptData(zipData, Key1, pos);

            var mainZipEntries = new HashSet<string>();
            using (var zipStream = new MemoryStream())
            {
                zipStream.Write(ZipHeader, 0, ZipHeader.Length);
                zipStream.Write(zipData, 0, zipData.Length);
                zipStream.Position = 0;

                try
                {
                    var pkStream = new ZipPkStream(zipStream);
                    var zipDir = pkStream.ReadCentralDirectory();

                    foreach (var zipEntry in zipDir)
                    {
                        if (!zipEntry.Name.EndsWith("/"))
                        {
                            mainZipEntries.Add(zipEntry.Name);
                            var entry = new RbPackedEntry
                            {
                                Name            = zipEntry.Name ?? $"{zipEntry.Offset:X}_{zipEntry.CompressedSize:X}",
                                Type            = FormatCatalog.Instance.GetTypeFromName(zipEntry.Name),
                                Offset          = zipEntry.Offset,
                                Size            = zipEntry.CompressedSize,
                                UnpackedSize    = zipEntry.UncompressedSize,
                                IsPacked        = zipEntry.CompressionMethod != 0,
                                IsMainZip       = true,
                                IsPrimaryType   = IsPrimaryType(zipEntry.Name),
                                LocalHeaderSize = zipEntry.LocalHeaderSize,
                                ZipOffset       = pos
                            };
                            dir.Add(entry);
                        }
                    }
                }
                catch
                {
                    return null;
                }
            }

            var dirR = new List<Entry>();
            uint entryCount = file.View.ReadUInt32(resourceTableOffset);
            pos = resourceTableOffset + 4;

            for (int i = 0; i < entryCount; i++)
            {
                var entry = ReadFileEntry(file, ref pos);
                if (entry != null)
                    dirR.Add(entry);
            }

            for (int i = 0; i < entryCount; i++)
            {
                dirR[i].Offset = pos;
                if (!dirR[i].CheckPlacement(file.MaxOffset)){
                    dirR = new List<Entry>();
                    break;
                }
                pos += dirR[i].Size;
            }
            dir.AddRange(dirR);
            return new ArcFile(file, this, dir);
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            var rbEntry = entry as RbPackedEntry;
            if (rbEntry == null)
                return base.OpenEntry(arc, entry);

            byte[] data;

            if (rbEntry.IsMainZip)
            {
                // Read from main ZIP
                long zipPos = rbEntry.ZipOffset;
                var zipData = new byte[arc.File.View.ReadInt64(zipPos - 8) - 8];
                arc.File.View.Read(zipPos, zipData, 0, (uint)zipData.Length);
                DecryptData(zipData, Key1, zipPos);

                using (var zipStream = new MemoryStream())
                {
                    zipStream.Write(ZipHeader, 0, ZipHeader.Length);
                    zipStream.Write(zipData, 0, zipData.Length);
                    zipStream.Position = 0;

                    using (var zip = new ICSharpCode.SharpZipLib.Zip.ZipFile(zipStream))
                    {
                        zip.StringCodec = ICSharpCode.SharpZipLib.Zip.StringCodec.Default;
                        var zipEntry = zip.GetEntry(rbEntry.Name);
                        if (zipEntry == null)
                            throw new FileNotFoundException();

                        using (var entryStream = zip.GetInputStream(zipEntry))
                        {
                            data = new byte[zipEntry.Size];
                            entryStream.Read(data, 0, data.Length);
                        }
                    }
                }

                DecryptData(data, rbEntry.IsPrimaryType ? Key1 : Key2, 0);
            }
            else
            {
                data = new byte[rbEntry.Size];
                arc.File.View.Read(rbEntry.Offset, data, 0, (uint)data.Length);

                if (rbEntry.IsPacked)
                {
                    using (var compressed = new MemoryStream(data))
                    using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress))
                    {
                        data = new byte[rbEntry.UnpackedSize];
                        zlib.Read(data, 0, data.Length);
                    }
                }

                DecryptData(data, Key2, 0);
            }

            return new BinMemoryStream(data);
        }

        private RbPackedEntry ReadFileEntry(ArcView file, ref long pos)
        {
            int strLength = file.View.ReadByte(pos++) & 0b1111111;
            var  nameBytes = file.View.ReadBytes(pos, (uint)strLength);
            DecryptData(nameBytes, Key2, pos);
            string name = Encoding.UTF8.GetString(nameBytes);

            pos += strLength;

            // Read sizes
            long compSize   = file.View.ReadInt64(pos);
            pos += 8;
            long uncompSize = file.View.ReadInt64(pos);
            pos += 8;

            return new RbPackedEntry
            {
                Name = name,
                Type = FormatCatalog.Instance.GetTypeFromName(name),
                Size = (uint)compSize,
                UnpackedSize = (uint)uncompSize,
                IsPacked = compSize != uncompSize,
                IsMainZip = false,
                IsPrimaryType = false
            };
        }

        private bool VerifyArchive(uint verifyCode, byte[] md5Hash)
        {
            using (var md5 = MD5.Create())
            {
                var bytes1 = BitConverter.GetBytes(verifyCode + 2525);
                var hash1 = md5.ComputeHash(bytes1);
                if (hash1.SequenceEqual(md5Hash))
                    return true;

                var bytes2 = BitConverter.GetBytes(verifyCode + 5252);
                var hash2 = md5.ComputeHash(bytes2);
                if (hash2.SequenceEqual(md5Hash))
                    return true;
            }

            return false;
        }

        private bool IsPrimaryType(string filename)
        {
            return PrimaryTypes.Any(ext => filename.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        }

        private static void DecryptData(byte[] data, byte[] key, long startOffset)
        {
            int keyLength = key.Length;
            int startPad = (int)(startOffset % keyLength);

            for (int i = 0; i < data.Length; i++)
            {
                int keyIndex = (i + startPad) % keyLength;
                data[i] = (byte)(data[i] - key[keyIndex]);
            }
        }
    }

    internal class RbPackedEntry : PackedEntry
    {
        public bool      IsMainZip { get; set; }
        public bool  IsPrimaryType { get; set; }
        public int LocalHeaderSize { get; set; }
        public long      ZipOffset { get; set; }
    }
}