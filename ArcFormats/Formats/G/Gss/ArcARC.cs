using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;

namespace GameRes.Formats.Gss
{
    [Export(typeof(ArchiveFormat))]
    public class LsdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/LSD"; } }
        public override string Description { get { return "GSS engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".arc"))
                return null;

            var bin_name = Path.ChangeExtension (file.Name, "BIN");
            if (!VFS.FileExists (bin_name))
                return null;

            using (var bin = VFS.OpenView (bin_name))
            {
                if (!bin.View.AsciiEqual (0, "LSDARC V.100"))
                    return null;
                int count = bin.View.ReadInt32 (0xC);
                if (!IsSaneCount (count))
                    return null;
                using (var index = bin.CreateStream())
                {
                    index.Position = 0x10;
                    var dir = new List<Entry> (count);
                    for (int i = 0; i < count; ++i)
                    {
                        var entry = new PackedEntry();
                        entry.IsPacked     = index.ReadInt32() != 0;
                        entry.Offset       = index.ReadUInt32();
                        entry.UnpackedSize = index.ReadUInt32();
                        entry.Size         = index.ReadUInt32();
                        entry.Name         = index.ReadCString();
                        if (!entry.CheckPlacement (file.MaxOffset))
                            return null;
                        dir.Add (entry);
                    }
                    return new ArcFile (file, this, dir);
                }
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var p_entry = entry as PackedEntry;
            if (null == p_entry || !p_entry.IsPacked || 
                    !arc.File.View.AsciiEqual (entry.Offset, "LSD\x1A"))
                return base.OpenEntry (arc, entry);

            char enc_method    = (char)arc.File.View.ReadByte (entry.Offset + 4);
            byte pack_method   = arc.File.View.ReadByte (entry.Offset + 5);
            uint unpacked_size = arc.File.View.ReadUInt32 (entry.Offset + 6);

            int len;
            using (var input = arc.File.CreateStream (entry.Offset + 12, entry.Size - 12))
            {
                var buf_packed = new byte[unpacked_size > entry.Size ? unpacked_size : entry.Size];
                input.Read (buf_packed, 0, (int)entry.Size - 12);
                input.Seek (0, SeekOrigin.Begin);
                var output = new byte[unpacked_size];
                switch ((char)pack_method)
                {
                case 'D': len = UnpackD (buf_packed, output, unpacked_size); break;
                case 'R': len = UnpackR (input, output); break;
                case 'H': len = UnpackH (buf_packed, output, unpacked_size); break;
                case 'W': 
                    var decompressedSize = UnpackW (buf_packed, output, unpacked_size);
                    var headerByte = buf_packed[0];
                    len = decrypt (output, output, decompressedSize - headerByte, (char)enc_method, headerByte, headerByte);
                    break;
                default: len = input.Read (output, 0, output.Length); break;
                }
                len =  decrypt (output, output, len < output.Length ? len:output.Length, (char)enc_method);
                return new BinMemoryStream (output, entry.Name);
            }
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            throw new NotImplementedException();
        }

        int UnpackD (byte[] buf_packed, byte[] output, uint unpacked_size)
        {
            int result = 0, wordIndex=0, readValue, maskedValue;
            int bitPosition = 0, totalBitsRead = 0;
            var halfSize = unpacked_size >> 1;
            byte bitOffset, adjustedBitOffset;
            if ((unpacked_size >> 1) != 0)
            {
                int outputPosition = 0;
                int currentPosition = 0;
                do
                {
                    bitOffset = (byte)(bitPosition & 0x1F);
                    int byteOffset = (int)((bitPosition >> 3) & 0xFFFFFFFC);
                    if ((int)(bitPosition & 0x1F) < 28)
                    {
                        readValue = BitConverter.ToInt32 (buf_packed, byteOffset);     
                    }
                    else
                    {
                        readValue = BitConverter.ToInt32 (buf_packed, byteOffset + 1);
                        bitOffset -= (byte)8;
                    }
                    var shiftedValue = readValue >> bitOffset;
                    totalBitsRead  +=  5;
                    byte nextBitOffset = (byte)(totalBitsRead & 0x1F);
                    int tableIndex = shiftedValue & 0xF ;
                    int nextByteOffset = (int)(((totalBitsRead) >> 3) & 0xFFFFFFFC);
                    if (nextBitOffset >= 24)
                    {
                        maskedValue = BitConverter.ToInt32 (buf_packed, nextByteOffset + 3);
                        adjustedBitOffset = (byte)(nextBitOffset - 24);
                    }
                    else if (nextBitOffset >= 16)
                    {
                            maskedValue = BitConverter.ToInt32 (buf_packed, nextByteOffset + 2);
                            adjustedBitOffset = (byte)(nextBitOffset - 16);
                    }
                    else
                    {
                        if (nextBitOffset < 8)
                        {
                            maskedValue = BitConverter.ToInt32 (buf_packed, nextByteOffset);
                        }
                        else
                        {
                            maskedValue = BitConverter.ToInt32 (buf_packed, nextByteOffset + 1);
                            nextBitOffset -= 8;
                        }
                        adjustedBitOffset = nextBitOffset;
                    }
                    var decodedValue = (DecompressionMaskTable[tableIndex] & (maskedValue >> adjustedBitOffset)) + DecompressionOffsetTable[0x10 + tableIndex];
                    if ((shiftedValue & 0x10) != 0)
                        decodedValue = decodedValue & 0xffff0000 |  (~decodedValue & 0xffff);
                    output[outputPosition++] = (byte)(decodedValue); //word
                    output[outputPosition++] = (byte)(decodedValue >> 8);
                    totalBitsRead += (int)DecompressionOffsetTable[tableIndex];
                    ++wordIndex;
                    bitPosition = totalBitsRead;
                    currentPosition += 2;
                }
                while (wordIndex != halfSize);
                result = currentPosition + 2;
            }
            return result;
        }

        int UnpackR (IBinaryStream input, byte[] output)
        {
            int dst = 0;
            while (dst < output.Length)
            {
                int ctl = input.ReadByte();
                if (-1 == ctl)
                    break;
                int count;
                if ((ctl & 0xC0) == 0xC0)
                {
                    count = ctl & 0xF;
                    ctl &= 0xF0;
                }
                else
                {
                    count = ctl & 0x3F;
                    ctl &= 0xC0;
                }
                switch (ctl)
                {
                case 0xF0: return 0;

                case 0x40:
                    input.Read (output, dst, count);
                    dst += count;
                    break;

                case 0xD0:
                    count = count << 8 | input.ReadUInt8();
                    input.Read (output, dst, count);
                    dst += count;
                    break;

                case 0x80:
                    {
                        byte v = input.ReadUInt8();
                        while (count --> 0)
                            output[dst++] = v;
                        break;
                    }

                case 0xE0:
                    {
                        count = count << 8 | input.ReadUInt8();
                        byte v = input.ReadUInt8();
                        while (count --> 0)
                            output[dst++] = v;
                        break;
                    }

                case 0x00:
                    dst += count;
                    break;

                case 0xC0:
                    count = count << 8 | input.ReadUInt8();
                    dst += count;
                    break;
                }
            }
            return dst;
        }

        int UnpackH (byte[] buf_packed, byte[] output, uint unpacked_size)
        {
            var lookupTable = new Byte[0x10000];
            Array.Clear (lookupTable, 0, lookupTable.Length);

            const uint huffmanTreeOffset = 0x2004;
            const uint huffmanDataOffset = 0x2804;
            const uint huffmanIndexOffset = 0x3004;

            uint currentPackedPos = 2, previousPos, nextPackedPos = 2, outputPosition = 0;
            byte outputByte, huffmanEntryCount = 0, huffmanSearchIndex, huffmanNodeIndex, firstByte = buf_packed[0];
            int symbolIndex = 0, huffmanMatchIndex = 0;

            do
            {
                previousPos = nextPackedPos;
                var currentByte = buf_packed[currentPackedPos];
                var nextAddress = currentPackedPos + 1;
                nextPackedPos++;
                if (currentByte != 0)
                {
                    byte lowByte = buf_packed[currentPackedPos + 1];
                    byte highByte = buf_packed[currentPackedPos + 2];
                    uint wordValue = (ushort)(lowByte + (highByte << 8));
                    if (currentByte >= 8)
                    {
                        if (currentByte >= 0xD)
                        {
                            uint huffmanCode;
                            if (currentByte < 0x10) // 2 byte
                            {
                                huffmanCode = wordValue;
                                nextAddress = currentPackedPos + 3;
                                nextPackedPos = previousPos + 3;
                            }
                            else // 3 byte
                            {
                                huffmanCode = (uint)(wordValue + (buf_packed[currentPackedPos + 3] << 16));
                                nextAddress = currentPackedPos + 4;
                                nextPackedPos = previousPos + 4;
                            }
                            lookupTable[huffmanDataOffset + 4 + 8 * huffmanEntryCount] = currentByte;
                            BitConverter.GetBytes((uint)(huffmanCode)).CopyTo (lookupTable, huffmanDataOffset + 8 * huffmanEntryCount);
                            lookupTable[huffmanDataOffset + 5 + 8 * huffmanEntryCount] = (byte)symbolIndex;
                            huffmanEntryCount++;
                        }
                        else // 2 byte
                        {
                            lookupTable[2 * wordValue] = (byte)symbolIndex;
                            lookupTable[2 * wordValue + 1] = currentByte;
                            nextAddress = currentPackedPos + 3;
                            nextPackedPos = previousPos + 3;
                        }
                    }
                    else // 1 byte
                    {
                        lookupTable[2 * lowByte] = (byte)symbolIndex;
                        lookupTable[2 * lowByte + 1] = currentByte;
                        nextAddress = currentPackedPos + 2;
                        nextPackedPos = previousPos + 2;
                    }
                }
                currentPackedPos = nextAddress;
                ++symbolIndex;
            } while (symbolIndex != 0x100);

            byte huffmanTableIndex = 0;
            byte bitLength = 0xD;
            do
            {
                huffmanSearchIndex = 0;
                lookupTable[huffmanIndexOffset + bitLength] = (byte)huffmanTableIndex;
                if (huffmanEntryCount != 0)
                {
                    int huffmanDataPos = 0x2804;
                    do
                    {
                        if (lookupTable[huffmanDataPos + 4] == bitLength)
                        {
                            Array.Copy (lookupTable, huffmanDataPos, lookupTable, huffmanTreeOffset + 8 * huffmanTableIndex, 4);
                            Array.Copy (lookupTable, huffmanDataPos + 4, lookupTable, huffmanTreeOffset + 4 + 8 * huffmanTableIndex, 4); 
                            huffmanTableIndex++;
                        }
                        ++huffmanSearchIndex;
                        huffmanDataPos += 8;
                    }
                    while (huffmanSearchIndex != huffmanEntryCount);
                }
                bitLength++;
            } while (bitLength != 0x18);

            lookupTable[0x301c] = (byte)huffmanTableIndex;
            Array.Clear (lookupTable, 0x3028, 8);

            int remainingBytes = (int)unpacked_size, bitStreamPosition = 0, bytesDecoded = 0;

            while (true)
            {
                int packedDataOffset = (int)(nextPackedPos + ((uint)(bitStreamPosition & 0x1F) >> 3) + ((bitStreamPosition >> 3) & 0xFFFFFFFC));
                ulong packedBits = BitConverter.ToUInt32 (buf_packed, packedDataOffset);
                byte currentBitLength = firstByte;
                uint extractedBits = ExtractBits((uint)packedBits, (uint)(packedBits >> 32), (uint)((bitStreamPosition & 0x1F) - (bitStreamPosition & 0x18)));
                if (firstByte != 0xD)
                {
                    while (true)
                    { 
                        huffmanMatchIndex = (int)(extractedBits & HuffmanBitMaskTable[2 * currentBitLength]);
                        // NOTE: must pay attention to the type conversion and length
                        BitConverter.GetBytes((int)(2 * huffmanMatchIndex + 0x811C6780)).CopyTo (lookupTable, huffmanTreeOffset - 4);
                        if (currentBitLength == lookupTable[2 * huffmanMatchIndex + 1]) // buf1 + 1
                            break;
                        currentBitLength++;
                        if (currentBitLength == 0xD)
                            goto CHECK_EXTENDED_HUFFMAN;
                    }
                    outputByte = lookupTable[2 * huffmanMatchIndex];
                    goto WRITE_OUTPUT;
                }
            CHECK_EXTENDED_HUFFMAN:
                if (currentBitLength == 0x18)
                    break;
            SEARCH_HUFFMAN_TABLE:
                while (true)
                {
                    huffmanNodeIndex = lookupTable[huffmanIndexOffset + currentBitLength];
                    if (huffmanNodeIndex != lookupTable[huffmanIndexOffset + 1 + currentBitLength])
                        break;
                    //CONTINUE_SEARCH:
                    if (++currentBitLength == 0x18)
                    {
                        return 0;
                        //outchar = buf[off2 + 5 + 8 * v29]; // seems a hack
                        //goto CONTINUE_SEARCH;
                    }
                }
                while (BitConverter.ToUInt32 (lookupTable, (int)huffmanTreeOffset + 8 * huffmanNodeIndex) !=
                       (extractedBits & HuffmanBitMaskTable[2 * currentBitLength]))
                {
                    huffmanNodeIndex++;
                    if (huffmanNodeIndex == lookupTable[huffmanIndexOffset + 1 + currentBitLength ])
                    {
                        if (++currentBitLength == 0x18)
                        {
                            return 0;
                        }
                        goto SEARCH_HUFFMAN_TABLE;
                    }
                }
                outputByte = lookupTable[huffmanTreeOffset + 5 + 8 * huffmanNodeIndex];
            WRITE_OUTPUT:
                output[outputPosition] = (byte)outputByte;
                BitConverter.GetBytes((Int64)bitStreamPosition + (ushort)currentBitLength).CopyTo (lookupTable, 0x3028);
                remainingBytes = bytesDecoded + 1;
                ++outputPosition;
                bitStreamPosition += currentBitLength ;
                bytesDecoded = remainingBytes;
                if (remainingBytes >= unpacked_size)
                    return bytesDecoded;
            }
            return 0;
        }
        
        uint ExtractBits (uint result, uint highBits, uint shiftAmount)
        {
            if (shiftAmount > 63) result = 0;
            else if (shiftAmount != 0)
            {
                if (shiftAmount > 31)
                    result = highBits >> (int)(shiftAmount - 32);
                else
                    result = (highBits << (int)(32 - shiftAmount)) | (result >> (int)shiftAmount);
                // inserts the low shiftAmount bit of the result into highBits, then assigns to the result
            }
            return result;
        }

        int UnpackW (byte[] buf_packed, byte[] output, uint unpacked_size)
        {
            return 0;
            /*
            int header_length = input.ReadUInt8();
            if (!IsSaneCount(header_length) || header_length == 0)
                return 0;

            int shift = input.ReadUInt8();
            input.Read (output, 0, header_length);
            int dst = header_length & ~1;

            int bitPosition = 0;
            while (dst < output.Length)
            {
                int bit = bitPosition & 0x1F;
                int byteOffset = (bitPosition >> 3) & 0x1FFFFFFC;
                int packedValue;
                if (bit < 0x1C)
                    packedValue = input.ReadInt32();
                else
                {
                    packedValue = input.ReadInt32();
                    bit -= 8;
                }
                bitPosition += 5;
                int compressionInfo = (packedValue >> bit) & 0x1F;
                bit = bitPosition & 0x1F;
                int nextByteOffset = (bitPosition >> 3) & 0x1FFFFFFC;
                if (bit < 8)
                {
                    packedValue = MemInt32(&src[nextByteOffset]);
                }
                else if (bit < 0x10)
                {
                    packedValue = MemInt32(&src[nextByteOffset + 1]);
                    bit -= 8;
                }
                else if (bit < 0x18)
                {
                    packedValue = MemInt32(&src[nextByteOffset + 2]);
                    bit -= 16;
                }
                else
                {
                    packedValue = MemInt32(&src[nextByteOffset + 3]);
                    bit -= 24;
                }
                int sampleIndex = compressionInfo & 0xF;
                int sample = AudioSampleOffsets[sampleIndex] + (((packedValue >> bit) & (AudioSampleMasks[sampleIndex] >> shift)) << shift);
                if ((compressionInfo & 0x10) != 0)
                    sample = -sample;
                LittleEndian.Pack ((short)sample, output, dst);
                dst += 2;
                int bitAdvance = AudioBitLengths[sampleIndex];
                if (bitAdvance > shift)
                {
                    bitPosition += bitAdvance - shift;
                }
            }
            return dst;
            */
        }

        int decrypt (byte[] input, byte[] output, int len, char enc_method, int start_input=0, int start_output=0) 
        {
            int len_decrypt = len, i;
            int cur_output_addr = start_output;
            int cur_addr = start_input;
            switch (enc_method)
            {
            case 'N':
                input.CopyTo (output, 0);
                break;
            case 'B': //byte
                i = 0;
                if (len_decrypt!=0)
                {
                    do
                    {
                        var d = input[cur_addr];
                        var tmp = -d;
                        if (i != 0)
                            tmp = input[cur_addr - 1] - d;
                        i++;
                        output[cur_output_addr++] = (byte)tmp;
                        ++cur_addr;
                    }
                    while (i != len_decrypt);
                }
                break;
            case 'W': //word
                len_decrypt = (int)((len + 1) & 0xFFFFFFFE);
                i = 0;
                if (len_decrypt!=0)
                {
                    do
                    {
                        var d = input[cur_addr] | (input[cur_addr + 1] << 8);
                        var tmp = -d;
                        if (i!=0)
                            tmp = (input[cur_addr-2] | input[cur_addr - 1] << 8) - d;
                        output[cur_output_addr++] = (byte)(tmp & 0xff);
                        output[cur_output_addr++] = (byte)(tmp >> 8);
                        i += 2;
                        cur_addr += 2;
                    }
                    while (i != len_decrypt);
                }
                break;
            case 'S': // big endian word
                len_decrypt = (int)((len + 1) & 0xFFFFFFFE);
                i = 0;
                if (len_decrypt!=0)
                {
                    do
                    {
                        var d = input[cur_addr + 1] | (input[cur_addr] << 8);
                        var tmp = -d;
                        if (i!=0)
                            tmp = (input[cur_addr - 1] | input[cur_addr - 2] << 8) - d;
                        output[cur_output_addr++] = (byte)(tmp >> 8);
                        output[cur_output_addr++] = (byte)(tmp & 0xff);
                        i += 2;
                        cur_addr += 2;
                    }
                    while (i != len_decrypt);
                }
                break;
            }
            return len_decrypt;
        }

        static readonly int[] AudioBitLengths = {
            0x0, 0x0, 0x0, 0x0, 0x3, 0x4, 0x5, 0x6,
            0x7, 0x8, 0x9, 0xA, 0xB, 0xC, 0xD, 0xE
        };
        static readonly int[] AudioSampleOffsets = {
            0x00, 0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40, 0x80,
            0x0100, 0x0200, 0x0400, 0x0800, 0x1000, 0x2000, 0x4000
        };
        static readonly int[] AudioSampleMasks = {
            0x00, 0x00, 0x00, 0x00, 0x07, 0x0F, 0x1F, 0x3F,
            0x7F, 0xFF, 0x01FF, 0x03FF, 0x07FF, 0x0FFF, 0x1FFF, 0x3FFF
        };
        static readonly uint[] HuffmanBitMaskTable = { 
            0x00000000, 0x00000000, 0x00000001, 0x00000000, 0x00000003, 0x00000000, 0x00000007, 0x00000000,
            0x0000000F, 0x00000000, 0x0000001F, 0x00000000, 0x0000003F, 0x00000000, 0x0000007F, 0x00000000,
            0x000000FF, 0x00000000, 0x000001FF, 0x00000000, 0x000003FF, 0x00000000, 0x000007FF, 0x00000000,
            0x00000FFF, 0x00000000, 0x00001FFF, 0x00000000, 0x00003FFF, 0x00000000, 0x00007FFF, 0x00000000,
            0x0000FFFF, 0x00000000, 0x0001FFFF, 0x00000000, 0x0003FFFF, 0x00000000, 0x0007FFFF, 0x00000000,
            0x000FFFFF, 0x00000000, 0x001FFFFF, 0x00000000, 0x003FFFFF, 0x00000000, 0x007FFFFF, 0x00000000,
            0x00FFFFFF, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000007, 0x0000000F,
            0x0000001F, 0x0000003F, 0x0000007F, 0x000000FF, 0x000001FF, 0x000003FF, 0x000007FF, 0x00000FFF,
            0x00001FFF, 0x00003FFF, 0x00007FFF, 0x0000FFFF, 0x0001FFFF, 0x0003FFFF, 0x000FFFFF, 0x00000000,
            0x06050403, 0x0A090807, 0x0E0D0C0B, 0x00000000, 0x00000001, 0x00000002, 0x00000004, 0x00000008,
            0x00000010, 0x00000020, 0x00000040, 0x00000080, 0x00000100, 0x00000200, 0x00000400, 0x00000800,
            0x00001000, 0x00002000, 0x00004000, 0x00000000, 0xFFFF0001, 0x00000000, 0x00000000, 0x00000001,
            0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0x00000001, 0x00000001,
        };
        static readonly uint[] DecompressionMaskTable ={
            0x00000000, 0x00000000, 0x00000001, 0x00000003, 0x00000007, 0x0000000F, 0x0000001F, 0x0000003F,
            0x0000007F, 0x000000FF, 0x000001FF, 0x000003FF, 0x000007FF, 0x00000FFF, 0x00001FFF, 0x00003FFF,
            0x00007FFF, 0x0000FFFF, 0x0001FFFF, 0x0003FFFF, 0x000FFFFF, 0x00000000, 0x00000000, 0x00000001,
        };
        static readonly uint[] DecompressionOffsetTable = {
            0x00000000, 0x00000000, 0x00000001, 0x00000002, 0x00000003, 0x00000004, 0x00000005, 0x00000006,
            0x00000007, 0x00000008, 0x00000009, 0x0000000A, 0x0000000B, 0x0000000C, 0x0000000D, 0x0000000E,
            0x00000000, 0x00000001, 0x00000002, 0x00000004, 0x00000008, 0x00000010, 0x00000020, 0x00000040,
            0x00000080, 0x00000100, 0x00000200, 0x00000400, 0x00000800, 0x00001000, 0x00002000, 0x00004000,
            0x00000000, 0x00000000, 0x00000000, 0x00000001, 0x00000000, 0x00000003, 0x00000000, 0x00000007,
            0x00000000, 0x0000000F, 0x00000000, 0x0000001F, 0x00000000, 0x0000003F, 0x00000000, 0x0000007F,
            0x00000000, 0x000000FF, 0x00000000, 0x000001FF, 0x00000000, 0x000003FF, 0x00000000, 0x000007FF,
            0x00000000, 0x00000FFF, 0x00000000, 0x00001FFF, 0x00000000, 0x00003FFF, 0x00000000, 0x00007FFF,
            0x00000000, 0x0000FFFF, 0x00000000, 0x0001FFFF, 0x00000000, 0x0003FFFF, 0x00000000, 0x0007FFFF,
            0x00000000, 0x000FFFFF, 0x00000000, 0x001FFFFF, 0x00000000, 0x003FFFFF, 0x00000000, 0x007FFFFF,
            0x00000000, 0x00FFFFFF, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000007,
            0x0000000F, 0x0000001F, 0x0000003F, 0x0000007F, 0x000000FF, 0x000001FF, 0x000003FF, 0x000007FF,
            0x00000FFF, 0x00001FFF, 0x00003FFF, 0x00007FFF, 0x0000FFFF, 0x0001FFFF, 0x0003FFFF, 0x000FFFFF,
        };
    }
}


