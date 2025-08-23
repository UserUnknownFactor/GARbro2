using System.ComponentModel.Composition;
using System.IO;
using System.Security.Cryptography;

namespace GameRes.Formats.Slg
{
    [Export(typeof(ImageFormat))]
    public class TigFormat : ImageFormat
    {
        public override string         Tag { get { return "TIG"; } }
        public override string Description { get { return "SLG system encrypted PNG image"; } }
        public override uint     Signature { get { return 0x7CF3C28B; } }
        public override bool      CanWrite { get { return false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            using (var input = OpenEncrypted (stream))
                return Png.ReadMetaData (input);
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var input = OpenEncrypted (stream))
                return Png.Read (input, info);
        }

        internal IBinaryStream OpenEncrypted (IBinaryStream stream, bool seekable = false)
        {
            Stream input = new ProxyStream (stream.AsStream, true);
            input = new InputCryptoStream (input, new TigTransform());
            if (seekable)
                input = new SeekableStream (input);
            return new BinaryStream (input, stream.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("TigFormat.Write not implemented");
        }
    }

    [Export(typeof(ImageFormat))]
    public class TicFormat : TigFormat
    {
        public override string         Tag { get { return "TIC"; } }
        public override string Description { get { return "SLG system encrypted JPEG image"; } }
        public override uint     Signature { get { return 0x15A44A01; } }
        public override bool      CanWrite { get { return false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            using (var input = OpenEncrypted (stream, true))
                return Jpeg.ReadMetaData (input);
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var input = OpenEncrypted (stream))
                return Jpeg.Read (input, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("TicFormat.Write not implemented");
        }
    }

    internal sealed class TigTransform : ICryptoTransform
    {
        const int BlockSize = 1;
        const uint DefaultKey = 0x7F7F7F7F;

        RandomGenerator     m_rnd;

        public bool          CanReuseTransform { get { return false; } }
        public bool CanTransformMultipleBlocks { get { return true; } }
        public int              InputBlockSize { get { return BlockSize; } }
        public int             OutputBlockSize { get { return BlockSize; } }

        public TigTransform () : this (DefaultKey)
        {
        }

        public TigTransform (uint key)
        {
            m_rnd = new RandomGenerator (key);
        }

        public int TransformBlock (byte[] inputBuffer, int inputOffset, int inputCount,
                                   byte[] outputBuffer, int outputOffset)
        {
            for (int i = 0; i < inputCount; ++i)
            {
                outputBuffer[outputOffset+i] = (byte)(inputBuffer[inputOffset+i] - m_rnd.Next());
            }
            return inputCount;
        }

        public byte[] TransformFinalBlock (byte[] inputBuffer, int inputOffset, int inputCount)
        {
            byte[] outputBuffer = new byte[inputCount];
            TransformBlock (inputBuffer, inputOffset, inputCount, outputBuffer, 0);
            return outputBuffer;
        }

        public void Dispose ()
        {
        }
    }

    internal sealed class RandomGenerator
    {
        uint    m_seed;

        public uint Seed { get { return m_seed; } }

        public RandomGenerator (uint seed)
        {
            SRand (seed);
        }

        public void SRand (uint seed)
        {
            m_seed = seed;
        }

        public uint Next ()
        {
            m_seed *= 0x343FD;
            m_seed += 0x269EC3;
            return m_seed >> 16;
        }
    };
}
