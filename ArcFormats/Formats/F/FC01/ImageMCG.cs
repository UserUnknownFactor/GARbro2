using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.FC01
{
    internal class McgMetaData : ImageMetaData
    {
        public int DataOffset;
        public int PackedSize;
        public int Version;
        public int ChannelsCount;
    }

    internal class McgOptions : ResourceOptions
    {
        public byte Key;
    }

    [Serializable]
    public class McgScheme : ResourceScheme
    {
        public Dictionary<string, byte> KnownKeys;
    }

    [Export(typeof(ImageFormat))]
    public class McgFormat : ImageFormat
    {
        public override string         Tag { get { return "MCG"; } }
        public override string Description { get { return "F&C Co. image format"; } }
        public override uint     Signature { get { return  0x2047434D; } } // 'MCG'

        internal static Dictionary<string, byte> KnownKeys { get { return DefaultScheme.KnownKeys; } }

        static McgScheme DefaultScheme = new McgScheme { KnownKeys = new Dictionary<string, byte>() };

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (McgScheme)value; }
        }

        private readonly Dictionary<string, byte> _fileKeyCache = new Dictionary<string, byte>();
        private byte? _lastUserKey = null;

        public McgFormat()
        {
            Extensions = new[] { "MCG" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x40);
            if (header[5] != '.')
                return null;

            int version = header[4] * 100 + header[6] * 10 + header[7] - 0x14D0;
            if (version != 200 && version != 101 && version != 100)
                throw new NotSupportedException (string.Format("Unsupported MCG version: {0}", version));

            int header_size = header.ToInt32 (0x10);
            if (header_size < 0x40)
                return null;

            int bpp = header.ToInt32 (0x24);
            if (24 != bpp && 8 != bpp && 16 != bpp)
                throw new NotSupportedException (string.Format("Unsupported MCG bitdepth: {0}", bpp));

            int packed_size = header.ToInt32 (0x38);
            return new McgMetaData
            {
                Width   = header.ToUInt32 (0x1c),
                Height  = header.ToUInt32 (0x20),
                OffsetX =  header.ToInt32 (0x14),
                OffsetY =  header.ToInt32 (0x18),
                BPP = bpp,
                DataOffset = header_size,
                PackedSize = packed_size,
                Version = version,
                ChannelsCount = header.ToInt32 (0x34),
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (McgMetaData)info;
            byte key = DetermineKey(stream, meta);

            var reader = new McgDecoder (stream, meta, key);
            reader.Unpack();

            if (reader.Key != 0 && reader.KeyWasDetected)
            {
                var fileName = Path.GetFileName(stream.Name ?? "");
                if (!string.IsNullOrEmpty(fileName))
                    _fileKeyCache[fileName] = reader.Key;
                _lastUserKey = reader.Key;
            }

            return ImageData.Create (info, reader.Format, reader.Palette, reader.Data, reader.Stride);
        }

        private byte DetermineKey(IBinaryStream stream, McgMetaData meta)
        {
            if (meta.Version != 101 && meta.Version != 100)
                return 0;

            var fileName = Path.GetFileName(stream.Name ?? "");
            if (!string.IsNullOrEmpty(fileName) && _fileKeyCache.TryGetValue(fileName, out byte cachedKey))
                return cachedKey;

            if (_lastUserKey.HasValue)
                return _lastUserKey.Value;

            byte savedKey = Properties.Settings.Default.MCGLastKey;
            if (savedKey != 0)
                return savedKey;

            var options = Query<McgOptions> (Localization._T ("ArcImageEncrypted"));
            _lastUserKey = options.Key;
            return options.Key;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("McgFormat.Write not implemented");
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new McgOptions { Key = Properties.Settings.Default.MCGLastKey };
        }

        public override ResourceOptions GetOptions (object widget)
        {
            var w = widget as GUI.WidgetMCG;
            if (null != w)
                Properties.Settings.Default.MCGLastKey = w.GetKey ();
            return GetDefaultOptions();
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetMCG();
        }
    }

    // mcg decompression // graphic.unt @ 100047B0
    internal class McgDecoder
    {
        private readonly IBinaryStream m_file;
        private readonly McgMetaData   m_info;
        private readonly int           m_width;
        private readonly int           m_height;
        private readonly int           m_pixels;

        private byte[] m_input;
        private byte[] m_output;
        private   byte m_key;
        private   bool m_key_was_detected;

        public byte              Key { get { return m_key; } }
        public bool   KeyWasDetected { get { return m_key_was_detected; } }
        public byte[]           Data { get { return m_output; } }
        public int            Stride { get; private set; }
        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }

        private static readonly byte[] ChannelOrder = { 1, 0, 2 };

        public McgDecoder (IBinaryStream input, McgMetaData info, byte key)
        {
            m_file   = input;
            m_info   = info;
            m_width  = (int)info.Width;
            m_height = (int)info.Height;
            m_pixels = m_width * m_height;
            m_key    = key;
            m_key_was_detected = false;

            InitializeFormat();
        }

        private void InitializeFormat()
        {
            Stride = m_width * m_info.BPP / 8;
            if (m_info.Version <= 101)
                Stride = (Stride + 3) & -4;

            switch (m_info.BPP)
            {
            case 24: Format = PixelFormats.Bgr24;    break;
            case 16: Format = PixelFormats.Bgr555;   break;
            case 8:  Format = PixelFormats.Indexed8; break;
            default:
                throw new InvalidFormatException(string.Format("Invalid MCG BPP: {0}", m_info.BPP));
            }
        }

        public void Unpack ()
        {
            ReadInputData();

            if (200 == m_info.Version)
                UnpackV200();
            else
                UnpackV101();
        }

        private void ReadInputData()
        {
            m_file.Position = m_info.DataOffset;
            long input_size = m_info.PackedSize;
            if (0 == input_size)
                input_size  = m_file.Length;
            input_size     -= m_info.DataOffset;
            if (8 == m_info.BPP)
            {
                Palette = ImageFormat.ReadPalette (m_file.AsStream);
                input_size -= 0x400;
            }
            else if (m_info.ChannelsCount > 0)
            {
                var masks = new int[m_info.ChannelsCount];
                for (int i = 0; i < masks.Length; ++i)
                    masks[i] = m_file.ReadInt32();

                if (16 == m_info.BPP && 3 == m_info.ChannelsCount)
                {
                    if (0x7E0 == masks[1])
                        Format = PixelFormats.Bgr565;
                }
                input_size -= m_info.ChannelsCount * 4;
            }

            if (input_size <= 0)
                throw new InvalidFormatException ("Invalid MCG input size");

            m_input = m_file.ReadBytes ((int)input_size);
            if (m_input.Length != input_size)
                Trace.WriteLine($"Unexpected end of file {m_input.Length} != {input_size}", "[MCG]");
        }

        private void UnpackV101 ()
        {
            if (m_key != 0)
            {
                var result = TryUnpackV101WithKey(m_key);
                if (result != null)
                {
                    m_output = result;
                    return;
                }
            }

            var detected = TryDetectKeyV101();
            if (detected != null)
            {
                m_output = detected;
                m_key_was_detected = true;
                return;
            }

            m_key = 0;
            var unencrypted = TryUnpackV101WithKey(0);
            if (unencrypted != null)
                m_output = unencrypted;
            else
                throw new InvalidFormatException("Failed to decompress MCG image");
        }

        private byte[] TryUnpackV101WithKey(byte key)
        {
            try
            {
                var data = new byte[m_input.Length];
                Buffer.BlockCopy(m_input, 0, data, 0, m_input.Length);

                if (key != 0)
                    MrgOpener.Decrypt(data, 0, data.Length - 1, key);

                using (var input = new BinMemoryStream(data))
                {
                    var lzss = new MrgLzssReader(input, data.Length, Stride * m_height);
                    lzss.Unpack();

                    // check if we consumed most of the input
                    long bytesRemaining = input.Length - input.Position;
                    if (bytesRemaining <= 1) // Allow for padding byte
                    {
                        m_key = key;
                        return lzss.Data;
                    }
                }
            }
            catch { }
            return null;
        }

        private byte[] TryDetectKeyV101()
        {
            byte savedKey = Properties.Settings.Default.MCGLastKey;
            if (savedKey != 0)
            {
                var result = TryUnpackV101WithKey(savedKey);
                if (result != null)
                {
                    //Trace.WriteLine(string.Format("Found matching key {0:X2} (saved)", savedKey), "[MCG]");
                    return result;
                }
            }

            var knownKeys = McgFormat.KnownKeys;
            if (knownKeys != null && knownKeys.Count > 0)
            {
                var uniqueKeys = new HashSet<byte>(knownKeys.Values);
                foreach (byte knownKey in uniqueKeys)
                {
                    if (knownKey == savedKey || knownKey == 0)
                        continue;

                    var result = TryUnpackV101WithKey(knownKey);
                    if (result != null)
                    {
                        //Trace.WriteLine(string.Format("Found matching key {0:X2} (known)", knownKey), "[MCG]");
                        Properties.Settings.Default.MCGLastKey = knownKey;
                        return result;
                    }
                }
            }

            // Full bruteforce as last resort
            for (int key = 1; key < 256; ++key)
            {
                if (key == savedKey)
                    continue;

                if (knownKeys != null && knownKeys.ContainsValue((byte)key))
                    continue;

                var result = TryUnpackV101WithKey((byte)key);
                if (result != null)
                {
                    Trace.WriteLine(string.Format("Found matching key {0:X2} (bruteforce)", key), "[MCG]");
                    Properties.Settings.Default.MCGLastKey = (byte)key;
                    return result;
                }
            }

            return null;
        }

        private void UnpackV200 ()
        {
            m_output = new byte[m_pixels * 3];
            var reader = new MrgDecoder (m_input, 0, (uint)m_pixels);

            if (m_key != 0 && TryUnpackV200WithKey(reader, m_key))
                return;

            byte savedKey = Properties.Settings.Default.MCGLastKey;
            if (savedKey != 0 && savedKey != m_key && TryUnpackV200WithKey(reader, savedKey))
            {
                m_key_was_detected = true;
                return;
            }

            var knownKeys = McgFormat.KnownKeys;
            if (knownKeys != null && knownKeys.Count > 0)
            {
                var uniqueKeys = new HashSet<byte>(knownKeys.Values);
                foreach (byte knownKey in uniqueKeys)
                {
                    if (knownKey == m_key || knownKey == savedKey)
                        continue;

                    if (TryUnpackV200WithKey(reader, knownKey))
                    {
                        m_key_was_detected = true;
                        //Trace.WriteLine(string.Format("Found matching key {0:X2} (known)", knownKey), "[MCG]");
                        return;
                    }
                }
            }

            // Bruteforce remaining keys
            for (int key = 0; key < 256; ++key)
            {
                if (key == m_key || key == savedKey)
                    continue;

                if (knownKeys != null && knownKeys.ContainsValue((byte)key))
                    continue;

                if (TryUnpackV200WithKey(reader, (byte)key))
                {
                    m_key_was_detected = true;
                    Trace.WriteLine(string.Format("Found matching key {0:X2} (bruteforce)", key), "[MCG]");
                    return;
                }
            }

            throw new UnknownEncryptionScheme();
        }

        private bool TryUnpackV200WithKey(MrgDecoder reader, byte key)
        {
            try
            {
                var testOutput = new byte[m_output.Length];

                reader.ResetKey(key);
                for (int i = 0; i < 3; ++i)
                {
                    reader.Unpack();
                    var plane = reader.Data;
                    int src = 0;
                    for (int j = ChannelOrder[i]; j < testOutput.Length; j += 3)
                    {
                        testOutput[j] = plane[src++];
                    }
                }

                Buffer.BlockCopy(testOutput, 0, m_output, 0, m_output.Length);
                Transform();
                m_key = key;
                Properties.Settings.Default.MCGLastKey = key;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void Transform ()
        {
            // Apply predictive transform
            int dst = 0;
            for (int y = m_height - 1; y > 0; --y)   // @@1a
            {
                for (int x = Stride - 3; x > 0; --x) // @@1b
                {
                    int p0 = m_output[dst];
                    int py = m_output[dst + Stride] - p0;
                    int px = m_output[dst + 3] - p0;
                    int gradient = Math.Abs(px + py);
                    py = Math.Abs(py);
                    px = Math.Abs(px);

                    byte predictor;
                    if (gradient >= px && py >= px)
                        predictor = m_output[dst + Stride];
                    else if (gradient < py)
                        predictor = m_output[dst];
                    else
                        predictor = m_output[dst + 3];

                    m_output[dst + Stride + 3] += (byte)(predictor + 0x80);
                    ++dst;
                }
                dst += 3;
            }

            // Convert from YCbCr to RGB
            dst = 0;
            for (int i = 0; i < m_pixels; ++i)
            {
                sbyte cb = (sbyte)(m_output[dst    ] - 128);
                sbyte cr = (sbyte)(m_output[dst + 2] - 128);
                int y = m_output[dst + 1] - ((cb + cr) >> 2);

                m_output[dst++] = (byte)(cb + y);
                m_output[dst++] = (byte)y;
                m_output[dst++] = (byte)(cr + y);
            }
        }
    }
}