using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.NekoNyan
{
    public class SpriteArcEntry : PackedEntry
    {
        public SpriteGameDatabase.Item Game { get; set; }
        public uint Key { get; set; }
    }

    [Export(typeof(ArchiveFormat))]
    public class SpriteArcDAT : ArchiveFormat
    {        
        public override string         Tag { get { return "NEKO/SPR"; } }
        public override string Description { get { return "NEKONYAN/SPRITE resource archive"; } }
        public override uint     Signature { get { return  0; } }
        public override bool  IsHierarchic { get { return  true; } }

        public override ArcFile TryOpen (ArcView view)
        {
            const int headerSize = 1024;
            if (view.MaxOffset < headerSize)
                return null;

            if (!TryIdentifyGame (view, out var game))
                return null;

            var fileCount = 0;
            for (var i = game.DecryParam.fileCountBeginByte; i < headerSize - 4; i += 4)
                fileCount += view.View.ReadInt32 (i);
            if (fileCount == 0)
                return new ArcFile(view, this, Array.Empty<Entry>()); // why?

            var entries = new List<Entry>();
            var seed1   = view.View.ReadUInt32 (0xD4);
            var seed2   = view.View.ReadUInt32 (0x5C);

            // table of contents is encrypted, need to decrypt it first
            var tocSize = 16 * fileCount;
            if (tocSize > view.MaxOffset - headerSize)
                return null;

            using (var tocBuffer = ArrayPool<byte>.Shared.RentSafe (tocSize))
            {
                if (view.View.Read (headerSize, tocBuffer, 0, (uint)tocSize) != tocSize)
                    return null;

                SpriteDecryptionUtils.Decrypt (new Span<byte>(tocBuffer, 0, tocSize), seed1, game.DecryParam);

                var contentOffset = BitConverter.ToInt32 (tocBuffer, 12);
                var constSize = contentOffset - (headerSize + tocSize);
                if (contentOffset > view.MaxOffset)
                    return null;

                using (var constBuffer = ArrayPool<byte>.Shared.RentSafe (constSize))
                {
                    if (view.View.Read (headerSize + tocSize, constBuffer, 0, (uint)constSize) != constSize)
                        return null;

                    SpriteDecryptionUtils.Decrypt (new Span<byte>(constBuffer, 0, constSize), seed2, game.DecryParam);

                    for (var i = 0; i < fileCount; i++)
                    {
                        var entryOffset = 16 * i;
                        var size = BitConverter.ToUInt32 (tocBuffer, entryOffset);
                        var constAddr = BitConverter.ToInt32 (tocBuffer, entryOffset + 4);
                        var key = BitConverter.ToUInt32 (tocBuffer, entryOffset + 8);
                        var dataAddr = BitConverter.ToUInt32 (tocBuffer, entryOffset + 12);

                        var cnt = 0;
                        for (; constAddr + cnt < constSize && constBuffer[constAddr + cnt] != 0; cnt++) { }

                        var name = Encoding.ASCII.GetString (constBuffer, constAddr, cnt);
                        entries.Add (new SpriteArcEntry
                        {
                            Game   = game,
                            Name   = name,
                            Offset = dataAddr,
                            Size   = size,
                            Key    = key,
                            Type = FormatCatalog.Instance.GetTypeFromName (name)
                        });
                    }
                }
            }

            return new ArcFile (view, this, entries);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (!(entry is SpriteArcEntry spriteEntry))
                return arc.File.CreateStream (entry.Offset, entry.Size);

            return new SpriteDecryptionStream (arc.File.CreateStream (entry.Offset, entry.Size), spriteEntry.Key, spriteEntry.Game.DecryParam);
        }

        private static bool TryIdentifyGame (ArcView view, out SpriteGameDatabase.Item game)
        {
            game = null;
            var info_name = VFS.CombinePath (VFS.GetDirectoryName (view.Name), "app.info");
            if (!File.Exists (info_name))
                return false;

            using (var sr = new StreamReader (info_name, Encoding.UTF8))
            {
                var company = sr.ReadLine();
                var product = sr.ReadLine();

                if (string.IsNullOrEmpty (company) || string.IsNullOrEmpty (product))
                    return false;

                game = SpriteGameDatabase.Games.FirstOrDefault (item => item.AppInfoCompany == company && item.AppInfoProduct == product);
            }

            return game != null;
        }
    }

    public static class SpriteDecryptionUtils
    {
        public const int KeyTableSize = 256;

        public static void Decrypt (Span<byte> data, uint key, in SpriteGameDatabase.DecryParams decryParams, long baseIndex = 0)
        {
            Span<byte> keyTable = stackalloc byte[KeyTableSize];
            GenerateKeyTable (keyTable, key, decryParams);
            Decrypt (data, keyTable, decryParams, baseIndex);
        }

        public static void Decrypt (Span<byte> data, Span<byte> keyTable, in SpriteGameDatabase.DecryParams decryParams, long baseIndex = 0)
        {
            if (keyTable.Length != KeyTableSize)
                ThrowInvalidKeyTable();

            for (var i = 0; i < data.Length; i++)
            {
                var keyIndex = baseIndex + i;
                var currentByte = data[i];
                currentByte ^= keyTable[(int)(keyIndex % decryParams.decryMod1)];
                currentByte += decryParams.decryAdd;
                currentByte += keyTable[(int)(keyIndex % decryParams.decryMod2)];
                currentByte ^= decryParams.decryXor;
                data[i] = currentByte;
            }
        }

        public static void GenerateKeyTable (Span<byte> keyTable, uint seed, in SpriteGameDatabase.DecryParams decryParams)
        {
            if (keyTable.Length != KeyTableSize)
                ThrowInvalidKeyTable();

            var state1 = seed * decryParams.genKeyInitMul + decryParams.genKeyInitAdd;
            var state2 = (state1 << decryParams.genKeyInitShift) ^ state1;
            for (var i = 0; i < KeyTableSize; i++)
            {
                state1 -= seed;
                state1 += state2;
                state2 = state1 + decryParams.genKeyRoundAdd;
                state1 *= state2 & decryParams.genKeyRoundAnd;
                keyTable[i] = (byte)state1;
                state1 >>= decryParams.genKeyRoundShift;
            }
        }

        #if NETSTANDARD2_1_OR_GREATER
        [System.Diagnostics.CodeAnalysis.DoesNotReturn]
        #endif
        private static void ThrowInvalidKeyTable()
        {
            throw new ArgumentException ($"Key table must be exactly {KeyTableSize} bytes long.");
        }
    }

    public class SpriteDecryptionStream : Stream
    {
        private Stream m_baseStream;
        private byte[] m_keyTable;
        private SpriteGameDatabase.DecryParams m_decryptParams;

        public SpriteDecryptionStream (Stream encryptedSource, uint decryptionKey, in SpriteGameDatabase.DecryParams decryParams)
        {
            m_baseStream = encryptedSource;
            m_decryptParams = decryParams;

            m_keyTable = new byte[SpriteDecryptionUtils.KeyTableSize];
            SpriteDecryptionUtils.GenerateKeyTable (m_keyTable, decryptionKey, decryParams);
        }

        public override void Flush()
        {
            m_baseStream.Flush();
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            return m_baseStream.Seek (offset, origin);
        }

        public override void SetLength (long value)
        {
            m_baseStream.SetLength (value);
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            var initPosition = m_baseStream.Position;
            var bytesRead = m_baseStream.Read (buffer, offset, count);

            if (bytesRead == 0)
                return 0;

            var span = new Span<byte>(buffer, offset, bytesRead);
            SpriteDecryptionUtils.Decrypt (span, m_keyTable, m_decryptParams, (int)initPosition);

            return bytesRead;
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException ("SpriteDecryptionStream does not support writing yet.");
        }

        public override bool  CanRead => m_baseStream.CanRead;
        public override bool  CanSeek => m_baseStream.CanSeek;
        public override bool CanWrite => false;

        public override long Length => m_baseStream.Length;

        public override long Position
        {
            get => m_baseStream.Position;
            set => m_baseStream.Position = value;
        }
    }

    public static class SpriteGameDatabase
    {
        public struct DecryParams
        {
            public long fileCountBeginByte;
            public readonly uint genKeyInitMul;
            public readonly uint genKeyInitAdd;
            public readonly  int genKeyInitShift;
            public readonly uint genKeyRoundAdd;
            public readonly uint genKeyRoundAnd;
            public readonly  int genKeyRoundShift;

            public readonly long decryMod1;
            public readonly byte decryAdd;
            public readonly long decryMod2;
            public readonly byte decryXor;

            public DecryParams (long fileCountBeginByte,
                uint genKeyInitMul,
                uint genKeyInitAdd,
                int genKeyInitShift,
                uint genKeyRoundAdd,
                uint genKeyRoundAnd,
                int genKeyRoundShift,
                long decryMod1,
                byte decryAdd,
                long decryMod2,
                byte decryXor)
            {
                this.fileCountBeginByte = fileCountBeginByte;
                this.genKeyInitMul    = genKeyInitMul;
                this.genKeyInitAdd    = genKeyInitAdd;
                this.genKeyInitShift  = genKeyInitShift;
                this.genKeyRoundAdd   = genKeyRoundAdd;
                this.genKeyRoundAnd   = genKeyRoundAnd;
                this.genKeyRoundShift = genKeyRoundShift;
                this.decryMod1        = decryMod1;
                this.decryAdd         = decryAdd;
                this.decryMod2        = decryMod2;
                this.decryXor         = decryXor;
            }
        }

        public class Item
        {
            public string  AppInfoCompany { get; } = "NekoNyanSoft";
            public string  AppInfoProduct { get; set; }
            public DecryParams DecryParam { get; } 

            public Item (string appInfoProduct, DecryParams param)
            {
                AppInfoProduct = appInfoProduct;
                DecryParam = param;
            }
        }

        public static readonly Item[] Games =
        {
            new Item ("Aokana",       new DecryParams (16, 0x1CDFU, 0xA74CU, 17, 56U, 239U, 1, 0xFD, 3, 0x59, 0x99)),
            new Item ("AokanaEXTRA2", new DecryParams (16, 0x1CDFU, 0xA74CU, 17, 56U, 239U, 1, 0xFD, 3, 0x59, 0x99)),
            new Item ("AokanaEXTRA2", new DecryParams (12, 0x131CU, 0xA740U, 7,  0x9CU, 0xCEU, 3, 0xB3, 3, 0x59, 0x77)),
            new Item ("KoiChoco",     new DecryParams (12, 0x1704U, 0xA140U, 7,  0x155U, 0xDCU, 2, 0xEB, 31, 0x57, 0xA5))
        };
    }
}