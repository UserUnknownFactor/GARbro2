using System;
using System.IO;
using System.Security.Cryptography;

namespace GameRes.Formats
{
    /// <summary>
    /// Abstract base class for byte-level cryptographic transformations.
    /// Provides a foundation for simple byte-by-byte encryption/decryption operations.
    /// </summary>
    public abstract class ByteTransform : ICryptoTransform
    {
        private const int BlockSize = 1;

        public bool CanReuseTransform => true;
        public bool CanTransformMultipleBlocks => true;
        public int InputBlockSize => BlockSize;
        public int OutputBlockSize => BlockSize;

        public abstract int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount,
                                         byte[] outputBuffer, int outputOffset);

        public byte[] TransformFinalBlock(byte[] inputBuffer, int inputOffset, int inputCount)
        {
            byte[] outputBuffer = new byte[inputCount];
            TransformBlock(inputBuffer, inputOffset, inputCount, outputBuffer, 0);
            return outputBuffer;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
        }
    }

    #region Byte Transforms

    /// <summary>
    /// Performs bitwise NOT operation on each byte.
    /// </summary>
    public sealed class NotTransform : ByteTransform
    {
        public override int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount,
                                         byte[] outputBuffer, int outputOffset)
        {
            for (int i = 0; i < inputCount; ++i)
                outputBuffer[outputOffset++] = (byte)~inputBuffer[inputOffset + i];
            return inputCount;
        }
    }

    /// <summary>
    /// Performs XOR operation with a single byte key.
    /// </summary>
    public sealed class XorTransform : ByteTransform
    {
        private readonly byte m_key;

        public XorTransform(byte key)
        {
            m_key = key;
        }

        public override int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount,
                                         byte[] outputBuffer, int outputOffset)
        {
            for (int i = 0; i < inputCount; ++i)
                outputBuffer[outputOffset++] = (byte)(m_key ^ inputBuffer[inputOffset + i]);
            return inputCount;
        }
    }

    /// <summary>
    /// Performs addition with a single byte key (modulo 256).
    /// </summary>
    public sealed class AddTransform : ByteTransform
    {
        private readonly byte m_key;

        public AddTransform(byte key)
        {
            m_key = key;
        }

        public override int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount,
                                         byte[] outputBuffer, int outputOffset)
        {
            for (int i = 0; i < inputCount; ++i)
            {
                outputBuffer[outputOffset++] = (byte)(inputBuffer[inputOffset + i] + m_key);
            }
            return inputCount;
        }
    }

    /// <summary>
    /// Performs subtraction with a single byte key (modulo 256).
    /// </summary>
    public sealed class SubtractTransform : ByteTransform
    {
        private readonly byte m_key;

        public SubtractTransform(byte key)
        {
            m_key = key;
        }

        public override int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount,
                                         byte[] outputBuffer, int outputOffset)
        {
            for (int i = 0; i < inputCount; ++i)
                outputBuffer[outputOffset++] = (byte)(inputBuffer[inputOffset + i] - m_key);
            return inputCount;
        }
    }

    /// <summary>
    /// Performs byte rotation to the left by specified number of bits.
    /// </summary>
    public sealed class RotateLeftTransform : ByteTransform
    {
        private readonly int m_shift;

        public RotateLeftTransform(int shift)
        {
            m_shift = shift % 8;
        }

        public override int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount,
                                         byte[] outputBuffer, int outputOffset)
        {
            for (int i = 0; i < inputCount; ++i)
            {
                byte value = inputBuffer[inputOffset + i];
                outputBuffer[outputOffset++] = (byte)((value << m_shift) | (value >> (8 - m_shift)));
            }
            return inputCount;
        }
    }

    /// <summary>
    /// Performs byte rotation to the right by specified number of bits.
    /// </summary>
    public sealed class RotateRightTransform : ByteTransform
    {
        private readonly int m_shift;

        public RotateRightTransform(int shift)
        {
            m_shift = shift % 8;
        }

        public override int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount,
                                         byte[] outputBuffer, int outputOffset)
        {
            for (int i = 0; i < inputCount; ++i)
            {
                byte value = inputBuffer[inputOffset + i];
                outputBuffer[outputOffset++] = (byte)((value >> m_shift) | (value << (8 - m_shift)));
            }
            return inputCount;
        }
    }

    #endregion

    #region Standard Cryptographic Streams

    /// <summary>
    /// Stream wrapper for AES encryption/decryption.
    /// </summary>
    public class AesStream : CryptoStream
    {
        private readonly Aes m_aes;

        public AesStream(Stream stream, byte[] key, byte[] iv, CryptoStreamMode mode)
            : base(stream, CreateTransform(key, iv, mode, out var aes), mode)
        {
            m_aes = aes;
        }

        private static ICryptoTransform CreateTransform(byte[] key, byte[] iv, CryptoStreamMode mode, out Aes aes)
        {
            aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            
            return mode == CryptoStreamMode.Write 
                ? aes.CreateEncryptor() 
                : aes.CreateDecryptor();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                m_aes?.Dispose();
        }

        /// <summary>
        /// Creates an AES stream with a password-derived key.
        /// </summary>
        public static AesStream CreateWithPassword(Stream stream, string password, byte[] salt, CryptoStreamMode mode)
        {
            using (var deriveBytes = new Rfc2898DeriveBytes(password, salt, 10000))
            {
                byte[] key = deriveBytes.GetBytes(32); // 256-bit key
                byte[] iv = deriveBytes.GetBytes(16);  // 128-bit IV
                return new AesStream(stream, key, iv, mode);
            }
        }
    }

    /// <summary>
    /// Stream wrapper for Triple DES encryption/decryption.
    /// </summary>
    public class TripleDesStream : CryptoStream
    {
        private readonly TripleDES m_tripleDes;

        public TripleDesStream(Stream stream, byte[] key, byte[] iv, CryptoStreamMode mode)
            : base(stream, CreateTransform(key, iv, mode, out var tripleDes), mode)
        {
            m_tripleDes = tripleDes;
        }

        private static ICryptoTransform CreateTransform(byte[] key, byte[] iv, CryptoStreamMode mode, out TripleDES tripleDes)
        {
            tripleDes = TripleDES.Create();
            tripleDes.Key = key;
            tripleDes.IV = iv;
            tripleDes.Mode = CipherMode.CBC;
            tripleDes.Padding = PaddingMode.PKCS7;
            
            return mode == CryptoStreamMode.Write 
                ? tripleDes.CreateEncryptor() 
                : tripleDes.CreateDecryptor();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                m_tripleDes?.Dispose();
        }
    }

    /// <summary>
    /// Stream wrapper for DES encryption/decryption (legacy, not recommended for new applications).
    /// </summary>
    public class DesStream : CryptoStream
    {
        private readonly DES m_des;

        public DesStream(Stream stream, byte[] key, byte[] iv, CryptoStreamMode mode)
            : base(stream, CreateTransform(key, iv, mode, out var des), mode)
        {
            m_des = des;
        }

        private static ICryptoTransform CreateTransform(byte[] key, byte[] iv, CryptoStreamMode mode, out DES des)
        {
            des = DES.Create();
            des.Key = key;
            des.IV = iv;
            des.Mode = CipherMode.CBC;
            des.Padding = PaddingMode.PKCS7;
            
            return mode == CryptoStreamMode.Write 
                ? des.CreateEncryptor() 
                : des.CreateDecryptor();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                m_des?.Dispose();
        }
    }

    /// <summary>
    /// Stream wrapper for RC2 encryption/decryption.
    /// </summary>
    public class Rc2Stream : CryptoStream
    {
        private readonly RC2 m_rc2;

        public Rc2Stream(Stream stream, byte[] key, byte[] iv, CryptoStreamMode mode)
            : base(stream, CreateTransform(key, iv, mode, out var rc2), mode)
        {
            m_rc2 = rc2;
        }

        private static ICryptoTransform CreateTransform(byte[] key, byte[] iv, CryptoStreamMode mode, out RC2 rc2)
        {
            rc2 = RC2.Create();
            rc2.Key = key;
            rc2.IV = iv;
            rc2.Mode = CipherMode.CBC;
            rc2.Padding = PaddingMode.PKCS7;
            
            return mode == CryptoStreamMode.Write 
                ? rc2.CreateEncryptor() 
                : rc2.CreateDecryptor();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                m_rc2?.Dispose();
        }
    }

    /// <summary>
    /// Helper class for creating encryption streams with proper key and IV generation.
    /// </summary>
    public static class CryptoStreamHelper
    {
        /// <summary>
        /// Creates an AES encryption stream with generated key and IV.
        /// </summary>
        public static (AesStream stream, byte[] key, byte[] iv) CreateAesEncryptionStream(Stream outputStream)
        {
            using (var aes = Aes.Create())
            {
                aes.GenerateKey();
                aes.GenerateIV();
                var stream = new AesStream(outputStream, aes.Key, aes.IV, CryptoStreamMode.Write);
                return (stream, aes.Key, aes.IV);
            }
        }

        /// <summary>
        /// Creates an AES decryption stream.
        /// </summary>
        public static AesStream CreateAesDecryptionStream(Stream inputStream, byte[] key, byte[] iv)
        {
            return new AesStream(inputStream, key, iv, CryptoStreamMode.Read);
        }

        /// <summary>
        /// Generates a random key for the specified algorithm.
        /// </summary>
        public static byte[] GenerateKey(SymmetricAlgorithm algorithm)
        {
            algorithm.GenerateKey();
            return algorithm.Key;
        }

        /// <summary>
        /// Generates a random IV for the specified algorithm.
        /// </summary>
        public static byte[] GenerateIV(SymmetricAlgorithm algorithm)
        {
            algorithm.GenerateIV();
            return algorithm.IV;
        }
    }

    #endregion

    #region Encryption Streams

    /// <summary>
    /// Stream that applies XOR encryption/decryption using a byte array key.
    /// The key is applied cyclically based on stream position.
    /// </summary>
    public class ByteStringEncryptedStream : InputProxyStream
    {
        private readonly byte[] m_key;
        private readonly int m_basePos;

        public ByteStringEncryptedStream(Stream main, byte[] key, bool leaveOpen = false)
            : this(main, key, 0, leaveOpen)
        {
        }

        public ByteStringEncryptedStream(Stream main, byte[] key, long startPos, bool leaveOpen = false)
            : base(main, leaveOpen)
        {
            m_key = key ?? throw new ArgumentNullException(nameof(key));
            if (m_key.Length == 0)
                throw new ArgumentException("Key cannot be empty", nameof(key));
            m_basePos = (int)(startPos % m_key.Length);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int startPos = (int)((m_basePos + BaseStream.Position) % m_key.Length);
            int read = BaseStream.Read(buffer, offset, count);
            for (int i = 0; i < read; ++i)
                buffer[offset + i] ^= m_key[(startPos + i) % m_key.Length];
            return read;
        }

        public override int ReadByte()
        {
            long pos = BaseStream.Position;
            int b = BaseStream.ReadByte();
            if (b != -1)
                b ^= m_key[(m_basePos + pos) % m_key.Length];
            return b;
        }
    }

    /// <summary>
    /// CryptoStream that disposes transformation object upon close.
    /// </summary>
    public class InputCryptoStream : CryptoStream
    {
        private ICryptoTransform m_transform;

        public InputCryptoStream(Stream input, ICryptoTransform transform)
            : base(input, transform, CryptoStreamMode.Read)
        {
            m_transform = transform;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && m_transform != null)
            {
                m_transform.Dispose();
                m_transform = null;
            }
        }
    }

    /// <summary>
    /// Stream that decrypts data by subtracting key bytes from the input stream.
    /// </summary>
    public class SubtractedStream : InputProxyStream
    {
        private readonly byte[] m_key;
        private readonly long m_offset;

        public SubtractedStream(Stream input, byte[] key, long offset = 0) : base(input)
        {
            m_key = key ?? throw new ArgumentNullException(nameof(key));
            if (m_key.Length == 0)
                throw new ArgumentException("Key cannot be empty", nameof(key));
            m_offset = offset;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var position = BaseStream.Position + m_offset;
            int read = BaseStream.Read(buffer, offset, count);

            for (int i = 0; i < read; i++)
            {
                int keyIndex = (int)((position + i) % m_key.Length);
                buffer[offset + i] = (byte)(buffer[offset + i] - m_key[keyIndex]);
            }
            return read;
        }
    }

    /// <summary>
    /// Stream that applies XOR encryption/decryption using a single byte key.
    /// Supports both reading and writing operations.
    /// </summary>
    public class XoredStream : ProxyStream
    {
        private readonly byte m_key;
        private byte[] m_writeBuffer;

        public XoredStream(Stream stream, byte key, bool leaveOpen = false)
            : base(stream, leaveOpen)
        {
            m_key = key;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = BaseStream.Read(buffer, offset, count);
            for (int i = 0; i < read; ++i)
                buffer[offset + i] ^= m_key;
            return read;
        }

        public override int ReadByte()
        {
            int b = BaseStream.ReadByte();
            if (b != -1)
                b ^= m_key;
            return b;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (m_writeBuffer == null)
                m_writeBuffer = new byte[81920];

            while (count > 0)
            {
                int chunk = Math.Min(m_writeBuffer.Length, count);
                for (int i = 0; i < chunk; ++i)
                    m_writeBuffer[i] = (byte)(buffer[offset + i] ^ m_key);

                BaseStream.Write(m_writeBuffer, 0, chunk);
                offset += chunk;
                count -= chunk;
            }
        }

        public override void WriteByte(byte value)
        {
            BaseStream.WriteByte((byte)(value ^ m_key));
        }
    }

    /// <summary>
    /// Stream that applies Caesar cipher encryption/decryption.
    /// </summary>
    public class CaesarStream : InputProxyStream
    {
        private readonly int m_shift;

        public CaesarStream(Stream input, int shift, bool leaveOpen = false)
            : base(input, leaveOpen)
        {
            m_shift = shift % 256;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = BaseStream.Read(buffer, offset, count);
            for (int i = 0; i < read; ++i)
                buffer[offset + i] = (byte)((buffer[offset + i] + m_shift) % 256);
            return read;
        }

        public override int ReadByte()
        {
            int b = BaseStream.ReadByte();
            if (b != -1)
                b = (b + m_shift) % 256;
            return b;
        }
    }

    /// <summary>
    /// Stream that applies a rolling XOR key based on stream position.
    /// </summary>
    public class RollingXorStream : InputProxyStream
    {
        private readonly byte m_initialKey;
        private readonly byte m_increment;

        public RollingXorStream(Stream input, byte initialKey, byte increment = 1, bool leaveOpen = false)
            : base(input, leaveOpen)
        {
            m_initialKey = initialKey;
            m_increment = increment;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            long position = BaseStream.Position;
            int read = BaseStream.Read(buffer, offset, count);
            
            for (int i = 0; i < read; ++i)
            {
                byte key = (byte)(m_initialKey + (position + i) * m_increment);
                buffer[offset + i] ^= key;
            }
            return read;
        }

        public override int ReadByte()
        {
            long position = BaseStream.Position;
            int b = BaseStream.ReadByte();
            if (b != -1)
            {
                byte key = (byte)(m_initialKey + position * m_increment);
                b ^= key;
            }
            return b;
        }
    }

    /// <summary>
    /// Simple RC4 implementation for stream encryption/decryption.
    /// Note: RC4 is considered weak and should not be used for sensitive data.
    /// </summary>
    public class Rc4Stream : ProxyStream
    {
        private readonly byte[] m_state;
        private int m_x;
        private int m_y;

        public Rc4Stream(Stream stream, byte[] key, bool leaveOpen = false)
            : base(stream, leaveOpen)
        {
            if (key == null || key.Length == 0)
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            m_state = new byte[256];
            InitializeState(key);
        }

        private void InitializeState(byte[] key)
        {
            // Initialize state array
            for (int i = 0; i < 256; i++)
                m_state[i] = (byte)i;

            // Key scheduling algorithm
            int j = 0;
            for (int i = 0; i < 256; i++)
            {
                j = (j + m_state[i] + key[i % key.Length]) & 0xFF;
                Swap(i, j);
            }

            m_x = 0;
            m_y = 0;
        }

        private void Swap(int i, int j)
        {
            byte temp = m_state[i];
            m_state[i] = m_state[j];
            m_state[j] = temp;
        }

        private byte GetNextKeyByte()
        {
            m_x = (m_x + 1) & 0xFF;
            m_y = (m_y + m_state[m_x]) & 0xFF;
            Swap(m_x, m_y);
            return m_state[(m_state[m_x] + m_state[m_y]) & 0xFF];
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = BaseStream.Read(buffer, offset, count);
            for (int i = 0; i < read; i++)
            {
                buffer[offset + i] ^= GetNextKeyByte();
            }
            return read;
        }

        public override int ReadByte()
        {
            int b = BaseStream.ReadByte();
            if (b != -1)
                b ^= GetNextKeyByte();
            return b;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            byte[] encrypted = new byte[count];
            for (int i = 0; i < count; i++)
                encrypted[i] = (byte)(buffer[offset + i] ^ GetNextKeyByte());

            BaseStream.Write(encrypted, 0, count);
        }

        public override void WriteByte(byte value)
        {
            BaseStream.WriteByte((byte)(value ^ GetNextKeyByte()));
        }
    }

    #endregion
}