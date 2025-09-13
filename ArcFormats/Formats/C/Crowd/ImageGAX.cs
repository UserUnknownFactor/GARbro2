using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Crowd
{
    internal class GaxMetaData : ImageMetaData
    {
        public byte[] Key;
    }

    [Export(typeof(ImageFormat))]
    public class GaxFormat : ImageFormat
    {
        public override string         Tag { get { return "GAX/PNG"; } }
        public override string Description { get { return "ANIM encrypted image"; } }
        public override uint     Signature { get { return  0x01000000; } }
        public override bool      CanWrite { get { return  true; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var key = new byte[0x10];
            stream.Position = 4;
            if (key.Length != stream.Read (key, 0, key.Length))
                return null;

            using (var enc    = new InputProxyStream (stream.AsStream, true))
            using (var crypto = new InputCryptoStream (enc, new GaxTransform (key, false)))
            using (var input  = new BinaryStream (crypto, stream.Name))
            {
                var info = Png.ReadMetaData (input);
                if (null == info)
                    return null;

                return new GaxMetaData {
                    OffsetX = info.OffsetX,
                    OffsetY = info.OffsetY,
                    Width   = info.Width,
                    Height  = info.Height,
                    BPP     = info.BPP,
                    Key     = key,
                };
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (GaxMetaData)info;
            using (var enc    = new StreamRegion (stream.AsStream, 0x14, true))
            using (var crypto = new InputCryptoStream (enc, new GaxTransform (meta.Key, false)))
            using (var input  = new BinaryStream (crypto, stream.Name))
            {
                var imageData = Png.Read (input, info);
                var pngImageData = new PngImageData (imageData.Bitmap, info);
                pngImageData.CustomChunks["gAXk"] = meta.Key;
                return pngImageData;
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            byte[] key = null;

            if (image is PngImageData pngImage && pngImage.CustomChunks.TryGetValue ("gAXk", out key))
            {
                if (key.Length != 0x10)
                    key = null;
            }

            if (key == null)
                key = GenerateRandomKey();

            var header = new byte[0x14];
            header[0] = 0x00;
            header[1] = 0x00;
            header[2] = 0x00;
            header[3] = 0x01;
            Array.Copy (key, 0, header, 4, 0x10);
            file.Write (header, 0, header.Length);

            using (var crypto = new CryptoStream (file, new GaxTransform (key, true), CryptoStreamMode.Write))
            {
                var pngFormat = new PngFormat();
                pngFormat.Write (crypto, image);
                crypto.FlushFinalBlock();
            }
        }

        private byte[] GenerateRandomKey()
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                var key = new byte[0x10];
                //rng.GetBytes (key);
                return key;
            }
        }
    }

    internal sealed class GaxTransform : ICryptoTransform
    {
        private const int BLOCK_SIZE = 16;

        private byte[] m_key;
        private bool   m_encrypting;

        public bool          CanReuseTransform { get { return false; } }
        public bool CanTransformMultipleBlocks { get { return true; } }
        public int              InputBlockSize { get { return BLOCK_SIZE; } }
        public int             OutputBlockSize { get { return BLOCK_SIZE; } }

        public GaxTransform (byte[] key, bool encrypting = false)
        {
            m_key = key.Clone() as byte[];
            m_encrypting = encrypting;
        }

        public int TransformBlock (byte[] inputBuffer, int inputOffset, int inputCount,
                                   byte[] outputBuffer, int outputOffset)
        {
            int inputEnd = inputOffset + inputCount;
            while (inputOffset < inputEnd)
            {
                int blockStart = inputOffset;
                int k;
                for (k = 0; k < BLOCK_SIZE && inputOffset < inputEnd; ++k)
                    outputBuffer[outputOffset++] = (byte)(inputBuffer[inputOffset++] ^ m_key[k]);
                if (k < BLOCK_SIZE)
                    break;

                byte m = m_encrypting
                    ? inputBuffer[blockStart + 14]     // Encryption: plaintext is input
                    : outputBuffer[outputOffset - 2];  // Decryption: plaintext is output

                switch (m & 7)
                {
                case 0:
                    m_key[ 0] += m;
                    m_key[ 3] += (byte)(m + 2);
                    m_key[ 4]  = (byte)(m_key[2] + m + 11);
                    m_key[ 8]  = (byte)(m_key[6] + 7);
                    break;
                case 1:
                    m_key[ 2]  = (byte)(m_key[9] + m_key[10]);
                    m_key[ 6]  = (byte)(m_key[7] + m_key[15]);
                    m_key[ 8] += m_key[1];
                    m_key[15]  = (byte)(m_key[3] + m_key[5]);
                    break;
                case 2:
                    m_key[ 1] += m_key[2];
                    m_key[ 5] += m_key[6];
                    m_key[ 7] += m_key[8];
                    m_key[10] += m_key[11];
                    break;
                case 3:
                    m_key[ 9] = (byte)(m_key[ 1] + m_key[2]);
                    m_key[11] = (byte)(m_key[ 5] + m_key[6]);
                    m_key[12] = (byte)(m_key[ 7] + m_key[8]);
                    m_key[13] = (byte)(m_key[10] + m_key[11]);
                    break;
                case 4:
                    m_key[ 0] = (byte)(m_key[ 1] + 0x6F);
                    m_key[ 3] = (byte)(m_key[ 4] + 0x47);
                    m_key[ 4] = (byte)(m_key[ 5] + 0x11);
                    m_key[14] = (byte)(m_key[15] + 0x40);
                    break;
                case 5:
                    m_key[2] += m_key[10];
                    m_key[4]  = (byte)(m_key[5] + m_key[12]);
                    m_key[6]  = (byte)(m_key[8] + m_key[14]);
                    m_key[8]  = (byte)(m_key[0] + m_key[11]);
                    break;
                case 6:
                    m_key[ 9] = (byte)(m_key[1] + m_key[11]);
                    m_key[11] = (byte)(m_key[3] + m_key[13]);
                    m_key[13] = (byte)(m_key[5] + m_key[15]);
                    m_key[15] = (byte)(m_key[7] + m_key[ 9]);
                    goto case 7;
                case 7:
                    m_key[1]  = (byte)(m_key[5] + m_key[ 9]);
                    m_key[2]  = (byte)(m_key[6] + m_key[10]);
                    m_key[3]  = (byte)(m_key[7] + m_key[11]);
                    m_key[4]  = (byte)(m_key[8] + m_key[12]);
                    break;
                }
            }
            return inputCount;
        }

        public byte[] TransformFinalBlock (byte[] inputBuffer, int inputOffset, int inputCount)
        {
            byte[] outputBuffer = new byte[inputCount];
            for (int i = 0; i < inputCount; ++i)
                outputBuffer[i] = (byte)(inputBuffer[inputOffset + i] ^ m_key[i]);
            return outputBuffer;
        }

        #region IDisposable methods
        public void Dispose()
        {
            System.GC.SuppressFinalize (this);
        }
        #endregion
    }
}
