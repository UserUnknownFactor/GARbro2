using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;

namespace GameRes
{
    [Export(typeof(VideoFormat))]
    public class MpgFormat : VideoFormat
    {
        public override string         Tag { get { return "MPG"; } }
        public override string Description { get { return "MPEG Video"; } }
        public override uint     Signature { get { return  0; } }
        public override bool      CanWrite { get { return  false; } }

        public MpgFormat ()
        {
            Extensions = new string[] { "mpg", "mpeg", "m2v", "vob" };
        }

        public override VideoData Read (IBinaryStream file, VideoMetaData info)
        {
            if (File.Exists (info.FileName) &&
                Extensions.Any (ext => string.Equals (ext,
                    VFS.GetExtension (info.FileName, true), StringComparison.OrdinalIgnoreCase)))
            {
                file.Dispose();
                return new VideoData (info);
            }

            return new VideoData (file.AsStream, info, true);
        }

        public override VideoMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.Length < 16)
                return null;

            // Check for MPEG Program Stream (PS) pack header: 0x000001BA
            // or MPEG Elementary Stream: 0x000001B3 (sequence header)
            // or MPEG Transport Stream: 0x47 (sync byte)

            var header = file.ReadBytes (16);

            bool isMpeg = false;
            string codec = Localization._T("Unknown");

            // Check for MPEG PS pack header (0x000001BA) - your example file
            if (header.Length >= 4 &&
                header[0] == 0x00 && header[1] == 0x00 &&
                header[2] == 0x01 && header[3] == 0xBA)
            {
                isMpeg = true;
                codec = "MPEG-PS";
            }
            // Check for MPEG sequence header (0x000001B3)
            else if (header.Length >= 4 &&
                     header[0] == 0x00 && header[1] == 0x00 &&
                     header[2] == 0x01 && header[3] == 0xB3)
            {
                isMpeg = true;
                codec = "MPEG-1/2";
            }
            // Check for MPEG Transport Stream (0x47 sync byte pattern)
            else if (header.Length >= 1 && header[0] == 0x47)
            {
                // Verify TS pattern (sync byte every 188 or 204 bytes)
                if (file.Length >= 376)
                {
                    file.Position = 188;
                    if (file.ReadUInt8 () == 0x47)
                    {
                        isMpeg = true;
                        codec = "MPEG-TS";
                    }
                }
            }

            if (!isMpeg)
            {
                // Also check if there's a start code within the first 2048 bytes
                file.Position = 0;
                var buffer = file.ReadBytes ((int)Math.Min (2048, file.Length));
                isMpeg = FindMpegStartCode (buffer);
                if (isMpeg)
                    codec = "MPEG";
            }

            if (!isMpeg)
                return null;

            var meta = new VideoMetaData
            {
                Width = 0,
                Height = 0,
                Duration = 0,
                FrameRate = 0,
                Codec = codec,
                CommonExtension = "mpg",
                HasAudio = false
            };

            file.Position = 0;
            TryReadSequenceHeader (file, meta);

            return meta;
        }

        private bool FindMpegStartCode (byte[] buffer)
        {
            // Look for MPEG start codes (0x000001xx)
            for (int i = 0; i < buffer.Length - 3; i++)
            {
                if (buffer[i] == 0x00 && buffer[i + 1] == 0x00 && buffer[i + 2] == 0x01)
                {
                    byte code = buffer[i + 3];
                    // Valid MPEG start codes
                    if (code == 0xBA || // Pack header
                        code == 0xBB || // System header
                        code == 0xB3 || // Sequence header
                        code == 0xB8 || // GOP header
                        code == 0x00 || // Picture start
                        (code >= 0xC0 && code <= 0xDF) || // Audio streams
                        (code >= 0xE0 && code <= 0xEF))   // Video streams
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void TryReadSequenceHeader (IBinaryStream file, VideoMetaData meta)
        {
            // Search for sequence header (0x000001B3) in the first 8KB
            var buffer = file.ReadBytes ((int)Math.Min (8192, file.Length));

            for (int i = 0; i < buffer.Length - 7; i++)
            {
                if (buffer[i] == 0x00 && buffer[i + 1] == 0x00 &&
                    buffer[i + 2] == 0x01 && buffer[i + 3] == 0xB3)
                {
                    // Parse sequence header
                    // Width: 12 bits at offset 4
                    // Height: 12 bits at offset 5.4
                    uint width = ((uint)buffer[i + 4] << 4) | ((uint)buffer[i + 5] >> 4);
                    uint height = (((uint)buffer[i + 5] & 0x0F) << 8) | (uint)buffer[i + 6];

                    if (width > 0 && width < 8192 && height > 0 && height < 8192)
                    {
                        meta.Width = width;
                        meta.Height = height;

                        // Frame rate code (4 bits)
                        byte frameRateCode = (byte)(buffer[i + 7] & 0x0F);
                        meta.FrameRate = GetFrameRate (frameRateCode);
                    }
                    break;
                }
            }
        }

        private double GetFrameRate (byte code)
        {
            switch (code)
            {
            case 1:  return 23.976;
            case 2:  return 24.0;
            case 3:  return 25.0;
            case 4:  return 29.97;
            case 5:  return 30.0;
            case 6:  return 50.0;
            case 7:  return 59.94;
            case 8:  return 60.0;
            default: return 0;
            }
        }
    }
}