using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.ScenePlayer
{
    [Export(typeof(AudioFormat))]
    [ExportMetadata("Priority", 1)]
    public class PmwAudio : WaveAudio
    {
        public override string         Tag { get { return "PMW"; } }
        public override string Description { get { return "ScenePlayer compressed WAV audio"; } }
        public override uint     Signature { get { return  0; } }
        public override bool      CanWrite { get { return  true; } }
        
        public override SoundInput TryOpen (IBinaryStream file)
        {
            int first = file.ReadByte();
            if ((first ^ 0x21) != 0x78) // doesn't look like zlib stream
                return null;

            file.Position = 0;
            using (var input = new XoredStream (file.AsStream, 0x21, true))
            using (var zstream = new ZLibStream (input, CompressionMode.Decompress))
            {
                SoundInput sound = null;
                var wav = new MemoryStream();
                try
                {
                    zstream.CopyTo (wav);
                    wav.Position = 0;
                    sound = new WaveInput (wav);
                }
                finally
                {
                    if (null == sound)
                        wav.Dispose();
                    else
                        file.Dispose();
                }
                return sound;
            }
        }

        public override void Write (SoundInput source, Stream output)
        {
            using (var wav = new XoredStream (output, 0x21, true))
            using (var zstream = new ZLibStream (wav, CompressionMode.Compress, CompressionLevel.Level9))
                base.Write (source, zstream);
        }
    }
}
