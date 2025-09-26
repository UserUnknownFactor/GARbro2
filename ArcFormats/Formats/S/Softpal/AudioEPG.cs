using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Softpal
{
    [Export(typeof(AudioFormat))]
    public class EpegAudio : AudioFormat
    {
        public override string         Tag { get { return "EPG/AUDIO"; } }
        public override string Description { get { return "Softpal EPEG embedded audio"; } }
        public override uint     Signature { get { return  0x47455045; } } // 'EPEG'
        public override bool      CanWrite { get { return  false; } }

        public EpegAudio()
        {
            Extensions = new string[] { "epg" };
        }

        public override SoundInput TryOpen(IBinaryStream file)
        {
            var header = file.ReadHeader(0x24);
            if (!header.AsciiEqual("EPEG"))
                return null;

            // From sub_10008FB0
            uint flags = header.ToUInt32(0x2C);
            if ((flags & 2) == 0)
                return null; // No audio

            file.Position = 0;
            var audioHeader = file.ReadBytes(0x24); // 36 bytes
            uint v15 = LittleEndian.ToUInt32(audioHeader, 0x2C);
            uint audioOffset = 8 * v15 + 18;
            file.Position = audioOffset;

            // From PalSoundLoadVerEpeg:
            uint audioDataSize = file.ReadUInt32();
            audioDataSize /= v15; // This gives us some unit size

            int v16 = header.ToInt32(0x40);
            int v17 = header.ToInt32(0x44);

            file.Position += 40;
            long audioStreamSize = audioDataSize * Math.Max(v16, v17);
            var audioData = new byte[audioStreamSize];
            file.Read(audioData, 0, (int)audioStreamSize);
            var format = new WaveFormat
            {
                FormatTag = 1, // PCM
                Channels = 2,   // Stereo (could be determined by v16/v17)
                SamplesPerSecond = 44100, // Standard, might be in header
                BitsPerSample = 16,
                BlockAlign = 4
            };
            format.SetBPS();
            var pcmStream = new MemoryStream(audioData);
            return new RawPcmInput(pcmStream, format);
        }
    }
}