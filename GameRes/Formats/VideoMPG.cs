using System.IO;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using System.Windows.Media.Imaging;
using GameRes.Utility;
using System.Windows.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition.Hosting;
using GameRes.Collections;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using GameRes.Compression;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection;

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
            Extensions = new string[] { "mpg" };
        }

        public override VideoData Read (IBinaryStream file, VideoMetaData info)
        {
            if (File.Exists (info.FileName) &&
                Extensions.Any (ext => string.Equals (ext,
                    VFS.GetExtension (info.FileName), StringComparison.OrdinalIgnoreCase)))
            {
                file.Dispose();
                return new VideoData (info);
            }

            return new VideoData (file.AsStream, info, true);
        }

        public override VideoMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.Length < 8)
                return null;
            
            if (!file.Name.EndsWith (".mpg", StringComparison.OrdinalIgnoreCase))
                return null;

            var meta = new VideoMetaData
            {
                Width = 0,
                Height = 0,
                Duration = 0,
                FrameRate = 0,
                Codec = "Unknown",
                CommonExtension = "mpg",
                HasAudio = false
            };

            return meta;
        }
    }
}