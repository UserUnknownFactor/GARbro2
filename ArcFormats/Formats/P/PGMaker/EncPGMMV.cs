using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using GameRes.Cryptography;
using GameRes.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameRes.Formats.PGMM
{
    public static class PgmmDecryptor
    {
        private static readonly byte[] IV = {
            0xA0, 0x47, 0xE9, 0x3D, 0x23, 0x0A, 0x4C, 0x62,
            0xA7, 0x44, 0xB1, 0xA4, 0xEE, 0x85, 0x7F, 0xBA
        };

        public class DecryptionInfo
        {
            public bool UseWeakMode { get; set; }
            public byte[] DecryptedKey { get; set; }
        }

        public static DecryptionInfo GetDecryptionInfo(string filePath)
        {
            var info = new DecryptionInfo { UseWeakMode = true };

            var infoPath = FindInfoJson(filePath);
            if (!string.IsNullOrEmpty(infoPath) && VFS.FileExists(infoPath))
            {
                try
                {
                    using (var infoStream = VFS.OpenStream(infoPath))
                    using (var reader = new StreamReader(infoStream))
                    {
                        var jsonText = reader.ReadToEnd();
                        var keyBase64 = ParseJsonForKey(jsonText);
                        if (!string.IsNullOrEmpty(keyBase64))
                        {
                            var encryptedKey = Convert.FromBase64String(keyBase64);
                            info.DecryptedKey = DecryptPgmmKey(encryptedKey);
                            info.UseWeakMode = false;
                        }
                    }
                }
                catch { }
            }

            return info;
        }

        public static byte[] DecryptData(IBinaryStream file, DecryptionInfo info)
        {
            file.Position = 0;
            var encrypted = file.ReadBytes((int)file.Length);
            return DecryptPgmmResource(encrypted, info.DecryptedKey, info.UseWeakMode);
        }

        private static string FindInfoJson(string filePath)
        {
            var currentDir = VFS.GetDirectoryName(filePath);

            while (!string.IsNullOrEmpty(currentDir))
            {
                var infoPath = VFS.CombinePath(currentDir, "data", "info.json");
                if (VFS.FileExists(infoPath))
                    return infoPath;

                var parentDir = VFS.GetDirectoryName(currentDir);

                // Stop if we've reached the root or parent is the same as current
                if (parentDir == currentDir)
                    break;

                currentDir = parentDir;
            }

            return null;
        }

        private static string ParseJsonForKey(string jsonText)
        {
            try
            {
                var jsonObject = JObject.Parse(jsonText);
                return jsonObject["key"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static byte[] DecryptPgmmKey(byte[] encryptedKey)
        {
            return CbcDecrypt(encryptedKey, IV, WeakTwofishBlockDecrypt);
        }

        private static byte[] DecryptPgmmResource(byte[] data, byte[] key, bool weak)
        {
            if (data.Length < 4 || (data.Length - 4) % 16 != 0)
                throw new InvalidDataException("Invalid encrypted data length");

            var ciphertext = new byte[data.Length - 4];
            Array.Copy(data, 4, ciphertext, 0, ciphertext.Length);

            if (weak)
            {
                return CbcDecrypt(ciphertext, IV, WeakTwofishBlockDecrypt);
            }
            else
            {
                var derivedKey = DeriveKey(data, key);

                using (var twofish = new Twofish())
                {
                    twofish.KeySize = derivedKey.Length * 8;
                    twofish.Mode = CipherMode.CBC;
                    twofish.Padding = PaddingMode.None;

                    using (var decryptor = twofish.CreateDecryptor(derivedKey, IV))
                    {
                        return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                    }
                }
            }
        }

        private static byte[] DeriveKey(byte[] data, byte[] key)
        {
            var derivedKey = new byte[key.Length];
            Array.Copy(key, derivedKey, key.Length);

            int i = 0;
            int h = data.Length - data[3] - 4;
            while (h > 0)
            {
                int t = (h ^ derivedKey[i]) & 0xff;
                derivedKey[i] = (byte)(t < 1 ? 1 : t);
                h /= 256;
                i++;
                if (i >= derivedKey.Length) break;
            }

            return derivedKey;
        }

        private static byte[] CbcDecrypt(byte[] data, byte[] iv, Action<byte[]> blockDecFunc)
        {
            if (data.Length % 16 != 0)
                throw new InvalidDataException("Data length must be multiple of 16");

            var result = new byte[data.Length];
            var lastBlock = new byte[16];
            Array.Copy(iv, lastBlock, 16);

            for (int i = 0; i < data.Length; i += 16)
            {
                var ct = new byte[16];
                Array.Copy(data, i, ct, 0, 16);

                var pt = new byte[16];
                Array.Copy(ct, pt, 16);
                blockDecFunc(pt);
                XorBlock(pt, 0, lastBlock, 0, 16);

                Array.Copy(pt, 0, result, i, 16);
                Array.Copy(ct, lastBlock, 16);
            }

            return result;
        }

        private static void WeakTwofishBlockDecrypt(byte[] block)
        {
            if (block.Length != 16)
                throw new ArgumentException("Block must be 16 bytes");

            uint[] words = new uint[4];
            for (int i = 0; i < 4; i++)
                words[i] = LittleEndian.ToUInt32(block, i * 4);

            for (int i = 0; i < 8; i++)
            {
                words[2] = RotateLeft(words[2], 1);
                words[3] = RotateRight(words[3], 1);
                words[0] = RotateLeft(words[0], 1);
                words[1] = RotateRight(words[1], 1);
            }

            uint temp = words[0];
            words[0] = words[2];
            words[2] = temp;

            temp = words[1];
            words[1] = words[3];
            words[3] = temp;

            for (int i = 0; i < 4; i++)
                LittleEndian.Pack(words[i], block, i * 4);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint RotateLeft(uint x, int n)
        {
            return (x << n) | (x >> (32 - n));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint RotateRight(uint x, int n)
        {
            return (x >> n) | (x << (32 - n));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void XorBlock(byte[] a, int aOffset, byte[] b, int bOffset, int length)
        {
            for (int i = 0; i < length; i++)
                a[aOffset + i] ^= b[bOffset + i];
        }
    }

    [Export(typeof(ImageFormat))]
    public class PgmmImageFormat : ImageFormat
    {
        public override string         Tag { get { return "PGMM/ENC/IMAGE"; } }
        public override string Description { get { return "PGMM encrypted image"; } }
        public override uint     Signature { get { return  0; } }

        public PgmmImageFormat()
        {
            Extensions = new string[] { "png", "gif", "jpg", "jpeg", "bmp", "tga", "webp" };
        }

        public override ImageMetaData ReadMetaData(IBinaryStream file)
        {
            var header = file.ReadHeader(4);
            if (!header.AsciiEqual(0, "enc"))
                return null;

            var ext = Path.GetExtension(file.Name).TrimStart('.').ToLowerInvariant();
            if (!Extensions.Contains(ext))
                return null;

            var info = PgmmDecryptor.GetDecryptionInfo(file.Name);

            return new PgmmImageMetaData
            {
                Width = 0,  // Will be determined by actual format
                Height = 0,
                BPP = 0,
                UseWeakMode = info.UseWeakMode,
                DecryptedKey = info.DecryptedKey,
                ActualFormat = ext
            };
        }

        public override ImageData Read(IBinaryStream file, ImageMetaData info)
        {
            var meta = (PgmmImageMetaData)info;
            var decryptInfo = new PgmmDecryptor.DecryptionInfo
            {
                UseWeakMode = meta.UseWeakMode,
                DecryptedKey = meta.DecryptedKey
            };

            var decrypted = PgmmDecryptor.DecryptData(file, decryptInfo);

            using (var stream = new BinMemoryStream(decrypted))
            {
                var format = ImageFormat.FindFormat(stream);
                if (format != null)
                {
                    stream.Position = 0;
                    var reader = format.Item1.Read(stream, format.Item2);
                    info.Width = reader.Width;
                    info.Height = reader.Height;
                    info.BPP = reader.BPP;
                    return reader;
                }
                else
                {
                    throw new NotSupportedException($"No decoder worked for this {meta.ActualFormat} format");
                }
            }
        }

        public override void Write(Stream file, ImageData image)
        {
            throw new NotImplementedException("PGMM encryption not implemented");
        }

        private class PgmmImageMetaData : ImageMetaData
        {
            public bool UseWeakMode { get; set; }
            public byte[] DecryptedKey { get; set; }
            public string ActualFormat { get; set; }
        }
    }

    [Export(typeof(AudioFormat))]
    public class PgmmAudioFormat : AudioFormat
    {
        public override string         Tag { get { return "PGMM/ENC/AUDIO"; } }
        public override string Description { get { return "PGMM encrypted audio"; } }
        public override uint     Signature { get { return  0; } }

        public PgmmAudioFormat()
        {
            Extensions = new string[] { "ogg", "wav", "mp3" };
        }

        public override SoundInput TryOpen(IBinaryStream file)
        {
            var header = file.ReadHeader(4);
            if (!header.AsciiEqual(0, "enc"))
                return null;

            var ext = Path.GetExtension(file.Name).TrimStart('.').ToLowerInvariant();
            if (!Extensions.Contains(ext))
                return null;

            var info = PgmmDecryptor.GetDecryptionInfo(file.Name);
            var decrypted = PgmmDecryptor.DecryptData(file, info);

            using (var stream = new BinMemoryStream(decrypted))
            {
                var sound = AudioFormat.Read(stream);
                if (sound != null)
                    return sound;

            }
            return null;
        }
    }

    [Export(typeof(ScriptFormat))]
    public class PgmmScriptFormat : GenericScriptFormat
    {
        public override string         Tag { get { return "PGMM/ENC/TEXT"; } }
        public override string Description { get { return "PGMM encrypted text file"; } }
        public override uint     Signature { get { return  0; } }

        public PgmmScriptFormat()
        {
            Extensions = new string[] { "txt", "json", "xml", "lua", "js", "csv" };
        }

        public override ScriptData Read(string name, Stream file)
        {
            var binaryStream = file as IBinaryStream ?? new BinaryStream(file, name);

            var header = binaryStream.ReadHeader(4);
            if (!header.AsciiEqual(0, "enc"))
                return null;

            var ext = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
            if (!Extensions.Contains(ext))
                return null;

            var info = PgmmDecryptor.GetDecryptionInfo(name);
            var decrypted = PgmmDecryptor.DecryptData(binaryStream, info);

            var encoding = DetectEncoding(decrypted);
            var text = encoding.GetString(decrypted);

            return new ScriptData(text);
        }

        private Encoding GuessEncoding(byte[] data)
        {
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                return Encoding.UTF8;
            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
                return Encoding.Unicode;
            if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
                return Encoding.BigEndianUnicode;

            return Encoding.UTF8;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PgmmBinaryFormat : ArchiveFormat
    {
        public override string         Tag { get { return "PGMM/ENC/BIN"; } }
        public override string Description { get { return "PGMM encrypted binary file"; } }
        public override uint     Signature { get { return  0; } }
        public override bool  IsHierarchic { get { return  false; } }
        public override bool      CanWrite { get { return  false; } }

        public PgmmBinaryFormat()
        {
            Extensions = new string[] { "dat", "bin", "exe", "dll", "ttf", "otf", "woff" };
        }

        public override ArcFile TryOpen(ArcView file)
        {
            if (!file.View.AsciiEqual(0, "enc"))
                return null;

            var ext = Path.GetExtension(file.Name).TrimStart('.').ToLowerInvariant();
            if (!Extensions.Contains(ext))
                return null;

            var info = PgmmDecryptor.GetDecryptionInfo(file.Name);
            var baseName = Path.GetFileNameWithoutExtension(file.Name);
            var entry = new PgmmBinaryEntry
            {
                Name = baseName + "_decrypted." + ext,
                Type = FormatCatalog.Instance.GetTypeFromName(file.Name),
                Offset = 0,
                Size = (uint)file.MaxOffset,
                UseWeakMode = info.UseWeakMode,
                DecryptedKey = info.DecryptedKey
            };

            var dir = new List<Entry> { entry };
            return new ArcFile(file, this, dir);
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            var pgmmEntry = entry as PgmmBinaryEntry;
            if (pgmmEntry == null)
                throw new InvalidOperationException("Invalid entry type");

            var input = arc.File.CreateStream();
            var decryptInfo = new PgmmDecryptor.DecryptionInfo
            {
                UseWeakMode = pgmmEntry.UseWeakMode,
                DecryptedKey = pgmmEntry.DecryptedKey
            };

            var decrypted = PgmmDecryptor.DecryptData(input, decryptInfo);
            return new BinMemoryStream(decrypted);
        }

        private class PgmmBinaryEntry : Entry
        {
            public bool UseWeakMode { get; set; }
            public byte[] DecryptedKey { get; set; }
        }
    }
}