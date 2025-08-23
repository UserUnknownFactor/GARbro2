using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;

namespace GameRes.Formats.BaseUnit
{
    [Export(typeof(AudioFormat))]
    public class WstAudio : AudioFormat
    {
        public override string         Tag { get { return "WST"; } }
        public override string Description { get { return "IPAC ADPCM audio format"; } }
        public override uint     Signature { get { return 0x32545357; } } // 'WST2'

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var adpcm_data = new byte[0x20];
            file.Position = 12;
            if (0x1C != file.Read (adpcm_data, 4, 0x1C))
                return null;
            adpcm_data[0] = 0xF4;
            adpcm_data[1] = 7;
            adpcm_data[2] = 7;
            int data_length = (int)(file.Length - file.Position);
            int total_size = 0x4E - 8 + data_length;
            var wav_file = new MemoryStream (total_size+4);
            try
            {
                using (var wav = new BinaryWriter (wav_file, Encoding.Default, true))
                {
                    wav.Write (Wav.Signature);
                    wav.Write (total_size);
                    wav.Write (0x45564157); // 'WAVE'
                    wav.Write (0x20746d66); // 'fmt '
                    wav.Write (0x32);
                    wav.Write ((ushort)2); // ADPCM
                    wav.Write ((ushort)2); // channels
                    wav.Write ((uint)0xAC44);
                    wav.Write ((uint)0xAC44);
                    wav.Write ((ushort)0x800);
                    wav.Write ((ushort)4);
                    wav.Write ((ushort)0x20);
                    wav.Write (adpcm_data, 0, adpcm_data.Length);
                    wav.Write (0x61746164); // 'data'
                    wav.Write (data_length);
                    file.AsStream.CopyTo (wav_file);
                }
                wav_file.Position = 0;
                var sound = new WaveInput (wav_file);
                file.Dispose();
                return sound;
            }
            catch
            {
                wav_file.Dispose();
                throw;
            }
        }
    }
}
