using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Unity.PMaster
{
    internal class PMasterEntry : Entry
    {
        public uint DecryptionKey;
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/PMASTER"; } }
        public override string Description { get { return "Unity PMaster engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            try // this format is way too generic
            {
                if (file.MaxOffset < 0x400)
                    return null;

                int entryCount = 0;
                for (int headerOffset = 0; headerOffset < 0x400; headerOffset += 4)
                    entryCount += file.View.ReadInt32 (headerOffset);

                if (!IsSaneCount (entryCount))
                    return null;

                uint indexSize = (uint)entryCount * 0x10;
                if (indexSize >= file.MaxOffset || 0x400 + indexSize > file.MaxOffset)
                    return null;

                if (0xD4 + 4 > file.MaxOffset || 0x5C + 4 > file.MaxOffset)
                    return null;

                var encryptedIndex = file.View.ReadBytes (0x400, indexSize);
                if (encryptedIndex == null || encryptedIndex.Length < indexSize)
                    return null;

                uint indexDecryptionKey = file.View.ReadUInt32 (0xD4);
                DecryptData (encryptedIndex, indexDecryptionKey);

                if (encryptedIndex.Length < 8)
                    return null;

                uint firstEntryOffset = encryptedIndex.ToUInt32 (4);
                if (firstEntryOffset >= file.MaxOffset || firstEntryOffset <= (0x400 + indexSize))
                    return null;

                uint nameTableSize = firstEntryOffset - (0x400 + indexSize);
                if (nameTableSize == 0 || 0x400 + indexSize + nameTableSize > file.MaxOffset)
                    return null;

                var encryptedNameTable = file.View.ReadBytes (0x400 + indexSize, nameTableSize);
                if (encryptedNameTable == null || encryptedNameTable.Length < nameTableSize)
                    return null;

                uint nameTableDecryptionKey = file.View.ReadUInt32 (0x5C);
                DecryptData(encryptedNameTable, nameTableDecryptionKey);

                int currentIndexPosition = 0;
                var directoryEntries = new List<Entry>(entryCount);
                for (int entryIndex = 0; entryIndex < entryCount; ++entryIndex)
                {
                    if (currentIndexPosition + 16 > encryptedIndex.Length)
                        return null;

                    int nameOffset = encryptedIndex.ToInt32 (currentIndexPosition);
                    if (nameOffset < 0 || nameOffset >= encryptedNameTable.Length)
                        return null;

                    var entryName = Binary.GetCString (encryptedNameTable, nameOffset);
                    var entry = Create<PMasterEntry>(entryName);
                    var entryOffset = encryptedIndex.ToUInt32 (currentIndexPosition + 4);
                    entry.Offset = entryOffset;
                    entry.Size = encryptedIndex.ToUInt32 (currentIndexPosition + 8);
                    entry.DecryptionKey = encryptedIndex.ToUInt32 (currentIndexPosition + 12);

                    currentIndexPosition += 16;
                    if (string.IsNullOrEmpty(entryName))
                        continue;
                     if (!entry.CheckPlacement (file.MaxOffset))
                        return null;

                    directoryEntries.Add(entry);
                }
                return new ArcFile (file, this, directoryEntries);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
            catch (IndexOutOfRangeException)
            {
                return null;
            }
            catch (OverflowException)
            {
                return null;
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pmasterEntry = (PMasterEntry)entry;
            var encryptedData = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            DecryptData (encryptedData, pmasterEntry.DecryptionKey);
            return new BinMemoryStream (encryptedData, entry.Name);
        }

        void DecryptData (byte[] encryptedData, uint encryptionSeed)
        {
            var decryptionKey = GenerateDecryptionKey (encryptionSeed);
            for (int byteIndex = 0; byteIndex < encryptedData.Length; byteIndex++)
            {
                byte currentByte = encryptedData[byteIndex];
                currentByte ^= decryptionKey[byteIndex & 0xFF];
                currentByte += 0x4D;
                currentByte += decryptionKey[byteIndex % 0x2B];
                currentByte -= decryptionKey[byteIndex & 0xFF];
                currentByte ^= 0x23;
                encryptedData[byteIndex] = currentByte;
            }
        }

        byte[] GenerateDecryptionKey (uint encryptionSeed)
        {
            var keyBytes = new byte[256];
            uint randomValue = encryptionSeed * 2281 + 59455;
            uint randomValue2 = (randomValue << 17) ^ randomValue;
            for (int keyIndex = 0; keyIndex < 256; keyIndex++)
            {
                randomValue >>= 5;
                randomValue ^= randomValue2;
                randomValue *= 471;
                randomValue -= encryptionSeed;
                randomValue += randomValue2;
                randomValue2 = randomValue + 87;
                randomValue ^= randomValue2 & 91;
                keyBytes[keyIndex] = (byte)randomValue;
                randomValue >>= 1;
            }
            return keyBytes;
        }
    }
}