using GameRes.Utility;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;

namespace GameRes.Formats.Kid
{
    [Export(typeof(ImageFormat))]
    public class KlzFormat: DigitalWorks.Tim2Format
    {
        public override string Tag { get { return "KLZ/KID_COMP"; } }
        public override string Description { get { return "KID PlayStation 2 LZH compressed TIM2 image format"; } }
        public override uint Signature { get { return 0; } } //KLZ have no header
        public KlzFormat()
        {
            Extensions = new string[] { "klz" };
            Settings = null;
        }

        public override ImageMetaData ReadMetaData(IBinaryStream stream)
        {
            uint unpacked_size = Binary.BigEndian(stream.Signature);
            if (unpacked_size <= 0x20 || unpacked_size > 0x5000000) // ~83MB
                return null;
            stream.Position = 0;
            //Stream streamdec = LzsStreamDecode(stream);
            //using (var lzss = new LzssStream(stream.AsStream, LzssMode.Decompress, true))
            using (var input = new SeekableStream(LzhStreamDecode(stream)))
            using (var tm2 = new BinaryStream(input, stream.Name))
                return base.ReadMetaData(tm2);
        }
        public override ImageData Read(IBinaryStream stream, ImageMetaData info)
        {
            //stream.Position = 4;
            //using (var lzss = new LzssStream(stream.AsStream, LzssMode.Decompress, true))
            using (var input = new SeekableStream(LzhStreamDecode(stream)))
            using (var tm2 = new BinaryStream(input, stream.Name))
                return base.Read(tm2, info);
        }
        public override void Write(Stream file, ImageData image)
        {
            throw new System.NotImplementedException("KlzFormat.Write not implemented");
        }

        /// <summary>
        /// Original lzh_decode_mips
        /// </summary>
        /// The following code is from punk7890/PS2-Visual-Novel-Tool under MIT license.
        /// Source code: https://github.com/punk7890/PS2-Visual-Novel-Tool/blob/ac5602fbf13d15ce1bfaa27dc2263373cfebc0e5/src/scenes/kid.gd#L104
        /// <param name="input">input stream, include header</param>
        /// <returns></returns>
        public static Stream LzhStreamDecode(IBinaryStream input) {
            byte[] decompressBuffer = new byte[0x4000];
            List<byte> outputData = new List<byte>();
            uint totalOutputSize = Binary.BigEndian(input.ReadUInt32());
            ushort currentBlockSize = Binary.BigEndian(input.ReadUInt16());
            bool condition;
            int tempValue, currentOutputPosition = 0, copySourcePosition, copyLength;
            byte currentByte; //byte a0
            ushort compressionWord;
            int bitMaskIndex = 0, bytesProcessedInBlock = 0, 
                controlBytePosition, dataBytePosition, bufferStartOffset = 0, 
                streamPosition = 0, remainingBlockSize = currentBlockSize;
            int nextBlockPosition = 0;
            int totalBytesWritten = 0;
            int blocksPassed = 0;
            byte[] bitMaskTable = new byte[] { 
                0x01, 0x02, 0x04, 0x08,
                0x10, 0x20, 0x40, 0x80,
                // Only first 8 are used
                0x81, 0x75, 0x81, 0x69,
                0x00, 0x00, 0x00, 0x00,
                0x01, 0x02, 0x00, 0x00
            };
            // out.resize(0x4000)
            /*int temp = 0x4000;
            while (temp > 0)
            {
                out_bytes.Add(0);
                temp--;
            }*/
            //out_bytes = Enumerable.Repeat((byte)0, 0x4000);
            // out.resize(0x4000) end
            if (currentBlockSize > 0x4000)
            {
                nextBlockPosition = 4;
                while (remainingBlockSize > 0x4000)
                {
                    int bytesToCopy = 0;
                    int readPosition = nextBlockPosition + 2;
                    while (bytesToCopy < 0x4000)
                    {
                        input.Position = readPosition;
                        outputData.Add(input.ReadUInt8());
                        bytesToCopy++;
                        readPosition++;
                    }
                    totalBytesWritten += 0x4000;
                    nextBlockPosition += bytesToCopy + 2;
                    blocksPassed++;
                    input.Position = nextBlockPosition;
                    remainingBlockSize = Binary.BigEndian(input.ReadUInt16());
                    if (totalBytesWritten >= totalOutputSize || nextBlockPosition >= input.Length)
                    {
                        Stream stream = new MemoryStream(outputData.ToArray());
                        return stream;
                    }
                    streamPosition = nextBlockPosition + 2;
                }
            }
            else
            {
                streamPosition = 6;
            }

            controlBytePosition = streamPosition;
            /*v0 = OO60_sp + 1;
            OO48_sp = v0;*/
            dataBytePosition = streamPosition + 1;
            while (true){
                input.Position = controlBytePosition;
                //input.Seek(OO44_sp, SeekOrigin.Begin);
                /*v0 = input.ReadUInt8();
                a0 = v0 & 0xFF;*/
                //a0 = input.ReadUInt8();
                /*v0 = OO40_sp;
                v1 = v0 & 0xFF;*/
                tempValue = bitMaskTable[bitMaskIndex & 0xFF] & input.ReadUInt8();
                //v0 &= a0;
                // #001BA8AC
                if (tempValue == 0)
                {
                    input.Position = dataBytePosition;
                    //input.Seek(OO48_sp, SeekOrigin.Begin);
                    currentByte = input.ReadUInt8();
                    tempValue = bufferStartOffset + currentOutputPosition;
                    /*out_bytes.RemoveAt(v0);
                    out_bytes.Insert(v0, v1);*/
                    decompressBuffer[tempValue] = currentByte;
                    dataBytePosition++;
                    bytesProcessedInBlock++;
                    currentOutputPosition++;
                }
                else if (tempValue != 0) {
                    // # 001BA8F0
                    bytesProcessedInBlock += 2;
                    input.Position = dataBytePosition;
                    // input.Seek(OO48_sp, SeekOrigin.Begin);
                    /*v0 = input.ReadUInt8() & 0xFF;
                    v1 = v0 << 8;
                    v0 = input.ReadUInt8() & 0xFF;
                    v0 = v1 | v0;
                    v0 &= 0xFFFF;*/
                    compressionWord = Binary.BigEndian(input.ReadUInt16());
                    /*s2 = v0 & 0xFFFF;
                    v0 = s2 & 0xFFFF;*/
                    //v0 = s2;
                    //v0 = (s2 & 0x1F);
                    //v0 += 2;
                    //v0 &= 0xFFFF;
                    /*s3 = v0 & 0xFFFF;
                    v0 = s2 & 0xFFFF;*/
                    copyLength = (compressionWord & 0x1F) + 2;
                    //v0 = s2;
                    //v0 >>= 5;
                    /*v0 &= 0xFFFF;
                    s1 = v0 & 0xFFFF;
                    v0 = s1 & 0xFFFF;*/
                    tempValue = currentOutputPosition - (compressionWord >> 5) - 1;
                    //v0 -= 1;
                    //v0 &= 0xFFFF;
                    copySourcePosition = tempValue & 0xFFFF;
                    dataBytePosition += 1;
                    tempValue = 1;
                    while (tempValue != 0)
                    {
                        condition = currentOutputPosition < 0x0800;
                        // # 001BA96C
                        if (condition)
                        {
                            tempValue = copySourcePosition & 0xFFFF;
                            condition = currentOutputPosition < tempValue;
                            if (condition)
                            {
                                currentByte = decompressBuffer[bufferStartOffset];
                                tempValue = bufferStartOffset + currentOutputPosition;
                                /*out_bytes.RemoveAt(v0);
                                out_bytes.Insert(v0, v1);*/
                                decompressBuffer[tempValue] = currentByte;
                                currentOutputPosition += 1;
                                /*v0 = s1 + 1;
                                s1 = v0 & 0xFFFF;*/
                                copySourcePosition = (copySourcePosition + 1) & 0xFFFF;
                                // # 001BA9D8
                                /*v1 = s3;
                                v0 = v1 - 1;
                                s3 = v0 & 0xFFFF;
                                v0 = v1 & 0xFFFF;*/
                                tempValue = copyLength & 0xFFFF;
                                copyLength = (copyLength - 1) & 0xFFFF;
                                continue;
                            }
                        }
                        // # 001BA9B0
                        /*v1 = s1 & 0xFFFF;
                        v0 = OO50_sp;
                        v0 += v1;*/
                        //v0 = OO50_sp + s1 & 0xFFFF;
                        //v1 = out_bytes[v0];
                        currentByte = decompressBuffer[bufferStartOffset + copySourcePosition & 0xFFFF];
                        tempValue = bufferStartOffset;
                        tempValue += currentOutputPosition;
                        /*out_bytes.RemoveAt(v0);
                        out_bytes.Insert(v0, v1);*/
                        decompressBuffer[tempValue] = currentByte;
                        currentOutputPosition += 1;
                        //v0 = s1 + 1;
                        copySourcePosition = (copySourcePosition + 1) & 0xFFFF;
                        // # 001BA9D8
                        /*v1 = s3;
                        v0 = v1 - 1;
                        s3 = v0 & 0xFFFF;
                        v0 = v1 & 0xFFFF;*/
                        tempValue = copyLength & 0xFFFF;
                        copyLength = copyLength - 1 & 0xFFFF;
                    }
                    dataBytePosition += 1;
                }
                // # 001BAA00
                bitMaskIndex += 1;
                //v1 = Convert.ToByte(OO40_sp & 0xFF);
                //v0 = 8;
                if ((bitMaskIndex & 0xFF) == 8)
                {
                    bitMaskIndex = 0;
                    controlBytePosition = dataBytePosition;
                    dataBytePosition += 1;
                    bytesProcessedInBlock += 1;
                }
                /*v0 = OO42_sp;
                v1 = v0 & 0xFFFF;
                v0 = OO70_sp;
                v0 -= 1;*/
                //v0 = v1 < v0 ? 1 : 0;
                tempValue = bytesProcessedInBlock < remainingBlockSize - 1 ? 1 : 0;
                if (tempValue == 0)
                {
                    totalBytesWritten += currentOutputPosition;
                    if (totalBytesWritten >= totalOutputSize || nextBlockPosition >= input.Length)
                    {
                        outputData.AddRange(decompressBuffer);
                        Stream stream = new MemoryStream(outputData.ToArray());
                        return stream;
                    }
                    blocksPassed += 1;
                    if (blocksPassed == 1)
                    {
                        nextBlockPosition += remainingBlockSize + 6;
                    }
                    else
                    {
                        nextBlockPosition += remainingBlockSize + 2;
                    }

                    outputData.AddRange(decompressBuffer);
                    // # out.fill(0);
                    input.Position = nextBlockPosition;
                    remainingBlockSize = Binary.BigEndian(input.ReadUInt16());
                    if (remainingBlockSize > 0x4000)
                    {
                        while (remainingBlockSize > 0x4000)
                        {
                            int bytesToCopy = 0;
                            int readPosition = nextBlockPosition + 2;
                            while (bytesToCopy < 0x4000)
                            {
                                input.Position = readPosition;
                                outputData.Add(input.ReadUInt8());
                                bytesToCopy += 1;
                                readPosition += 1;
                            }
                            totalBytesWritten += 0x4000;
                            nextBlockPosition += bytesToCopy + 2;
                            input.Position = nextBlockPosition;
                            remainingBlockSize = Binary.BigEndian(input.ReadUInt16());
                            if (totalBytesWritten >= totalOutputSize || nextBlockPosition >= input.Length)
                            {
                                Stream stream = new MemoryStream(outputData.ToArray());
                                return stream;
                            }
                        }
                    }
                    currentOutputPosition = 0;
                    //s1 = 0;
                    bufferStartOffset = 0;
                    bytesProcessedInBlock = 0;
                    dataBytePosition = nextBlockPosition + 2;
                    if (dataBytePosition > input.Length) {
                        Stream stream = new MemoryStream(outputData.ToArray());
                        return stream;
                    }
                    bitMaskIndex = 0;
                    streamPosition = dataBytePosition;
                    controlBytePosition = streamPosition;
                    tempValue = streamPosition + 1;
                    dataBytePosition = tempValue;
                }
            }
            //Stream stream_out = new MemoryStream(f_out_bytes.ToArray());
            //return stream_out;
        }
    }
}