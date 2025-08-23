using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.Cyberworks
{
    [Serializable]
    public class TinkAudioScheme : ResourceScheme
    {
        public Dictionary<uint, byte[]> KnownKeys;
    }

    [Export(typeof(AudioFormat))]
    public class TinkAudio : AudioFormat
    {
        public override string         Tag { get { return "OGG/TINK"; } }
        public override string Description { get { return "Cyberworks encrypted OGG audio"; } }
        public override uint     Signature { get { return 0x6B6E6954; } }

        public TinkAudio ()
        {
            Signatures = new uint[] { 0x6B6E6954, 0x676E6F53, 0 }; // 'Tink', 'Song'
            Extensions = new string[] { "j0", "k0", "u0" };
        }

        static Dictionary<uint, byte[]> KnownKeys = new Dictionary<uint, byte[]>();

        public override ResourceScheme Scheme
        {
            get { return new TinkAudioScheme { KnownKeys = KnownKeys }; }
            set { KnownKeys = ((TinkAudioScheme)value).KnownKeys; }
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = new byte[Math.Min (0xE1F, file.Length)];
            if (0x10 != file.Read (header, 0, 0x10))
                return null;
            var signature = LittleEndian.ToUInt32 (header, 0);
            byte[] key;
            if (!KnownKeys.TryGetValue (signature, out key))
            {
                signature = LittleEndian.ToUInt32 (header, 0xC);
                if (!KnownKeys.TryGetValue (signature, out key))
                    return null;
                file.Read (header, 4, 0xC);
            }
            header[0] = (byte)'O';
            header[1] = (byte)'g';
            header[2] = (byte)'g';
            header[3] = (byte)'S';
            file.Read (header, 0x10, header.Length-0x10);
            int k = 0;
            for (int i = 4; i < header.Length; ++i)
            {
                header[i] ^= key[k++];
                if (k >= key.Length)
                    k = 1;
            }
            Stream input;
            if (header.Length >= file.Length)
                input = new MemoryStream (header);
            else
                input = new PrefixStream (header, new StreamRegion (file.AsStream, file.Position));
            var sound = new OggInput (input);
            if (header.Length >= file.Length)
                file.Dispose();
            return sound;
        }
    }
}
