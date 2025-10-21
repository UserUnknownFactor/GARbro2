using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using GameRes.Utility;

namespace GameRes.Formats.SRPGStudio
{
    [Export(typeof(AudioFormat))]
    public class SrkAudioFormat : AudioFormat
    {
        public override string         Tag { get { return "SRK/AUDIO"; } }
        public override string Description { get { return "SRPG Studio encrypted audio"; } }
        public override uint     Signature { get { return  0; } }
        public override bool      CanWrite { get { return  false; } }

        public SrkAudioFormat()
        {
            Extensions = new string[] { "srk" };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".srk"))
                return null;

            if (file.Length < 8)
                return null;

            // Use shared cache from SrkOpener
            var crypto = SrkOpener.LastCrypto;

            // Test if cached key still works
            if (crypto != null)
            {
                file.Position = 0;
                var testHeader = file.ReadBytes((int)Math.Min (16, file.Length));
                var testDecrypt = crypto.Decrypt (testHeader);
                if (testDecrypt.Length >= 4)
                {
                    uint testSig = testDecrypt.ToUInt32 (0);
                    var testRes = AutoEntry.DetectFileType (testSig);
                    if (testRes.Type == null || testRes.Extensions.FirstOrDefault() == "exe")
                        crypto = null;
                }
                else
                    crypto = null;
            }
            
            if (crypto == null)
            {
                crypto = SrkOpener.DetectKeyFromStream (file.Name, file);
                if (crypto == null)
                    crypto = new SrpgCrypto ("keyset");
                SrkOpener.LastCrypto = crypto;
            }

            var headerSize = (int)Math.Min (64, file.Length);
            file.Position = 0;
            var header = file.ReadBytes (headerSize);
            var decryptedHeader = crypto.Decrypt (header);

            if (decryptedHeader.Length < 4)
                return null;

            uint signature = decryptedHeader.ToUInt32 (0);
            if (!IsAudioSignature (signature))
                return null;

            file.Position = 0;
            var fullData = file.ReadBytes((int)Math.Min (int.MaxValue, file.Length));
            var decryptedData = crypto.Decrypt (fullData);

            var decryptedStream = new BinMemoryStream (decryptedData, file.Name);
            try
            {
                var format = AudioFormat.FindFormat (decryptedStream);
                if (format == null)
                {
                    decryptedStream.Dispose();
                    return null;
                }
                
                decryptedStream.Position = 0;
                var sound = format.TryOpen (decryptedStream);
                if (sound != null)
                    return sound;
                
                decryptedStream.Dispose();
                return null;
            }
            catch
            {
                decryptedStream.Dispose();
                throw;
            }
        }

        private bool IsAudioSignature (uint signature)
        {
            if (signature == 0x46464952) return true; // 'RIFF'
            if (signature == 0x5367674F) return true; // 'OggS'
            if ((signature & 0xFFE0) == 0xFFE0 || (signature & 0xE0FF) == 0xE0FF) return true;
            if (signature == 0x43614C66) return true; // 'fLaC'
            if ((signature & 0xFFFFFF) == 0x707974 || signature == 0x70797466) return true; // M4A/MP4 audio
            var res = AutoEntry.DetectFileType (signature);
            bool is_audio = res.Type == "audio";
            if (!is_audio)
            {
                var ext = res.Extensions?.FirstOrDefault();
                if (ext != null && (ext == "wav" || ext == "ogg" || ext == "mp3" || 
                    ext == "m4a" || ext == "flac" || ext == "opus" || ext == "wma"))
                {
                    is_audio = true;
                }
            }
            
            if (!is_audio)
                throw new NotSupportedException ("Not audio - open it as archive");
            
            return is_audio;
        }
    }
}