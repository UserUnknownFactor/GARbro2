using System;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;
using GameRes.Formats;
using NAudio.Wave;

namespace GameRes.Formats.CsWare
{
    [Export(typeof(AudioFormat))]
    public sealed class Af2Audio : AudioFormat
    {
        public override string         Tag { get { return "AF2"; } }
        public override string Description { get { return "CsWare audio format"; } }
        public override uint     Signature { get { return  0x32714661; } } // 'aFq2'
        public override bool      CanWrite { get { return  false; } }

        public Af2Audio ()
        {
            Extensions = new string[] { "af2", "pmd" };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20);
            if (header.ToUInt32(0) != Signature)
                return null;

            var format = new WaveFormat
            {
                FormatTag = 1, // PCM
                SamplesPerSecond = (uint)BigEndian.ToInt32 (header, 4),
                Channels = BigEndian.ToUInt16 (header, 8),
                BitsPerSample = BigEndian.ToUInt16 (header, 10),
            };

            format.BlockAlign = (ushort)(format.Channels * format.BitsPerSample / 8);
            format.SetBPS();

            int dataSize = (int)file.Length - 0x20;
            var data = new byte[dataSize];
            if (dataSize != file.Read (data, 0, dataSize))
                return null;

            return new RawPcmInput (new MemoryStream (data), format);
        }
    }
}
