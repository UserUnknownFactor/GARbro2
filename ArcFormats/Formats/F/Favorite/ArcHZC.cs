using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;
using GameRes.Compression;

namespace GameRes.Formats.FVP
{
    internal class HzcArchive : ArcFile
    {
        public readonly HzcMetaData ImageInfo;

        public HzcArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, HzcMetaData info)
            : base (arc, impl, dir)
        {
            ImageInfo = info;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class HzcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "HZC/MULTI"; } }
        public override string Description { get { return "Favorite View Point multi-frame image"; } }
        public override uint     Signature { get { return  0x31637A68; } } // 'hzc1'
        public override bool  IsHierarchic { get { return  false; } }
        public override bool      CanWrite { get { return  false; } }

        public HzcOpener ()
        {
            Extensions = new string[] { "hzc" };
        }

        static readonly Lazy<ImageFormat> Hzc = new Lazy<ImageFormat> (() => ImageFormat.FindByTag ("HZC"));

        public override ArcFile TryOpen (ArcView file)
        {
            uint header_size = file.View.ReadUInt32 (8);
            HzcMetaData image_info;
            using (var header = file.CreateStream (0, 0xC+header_size))
            {
                image_info = Hzc.Value.ReadMetaData (header) as HzcMetaData;
                if (null == image_info)
                    return null;
            }
            int count = file.View.ReadInt32 (0x20);
            if (0 == count)
                count = 1;
            string base_name = Path.GetFileNameWithoutExtension (file.Name);
            int frame_size = image_info.UnpackedSize / count;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D3}", base_name, i),
                    Type = "image",
                    Offset = frame_size * i,
                    Size = (uint)frame_size,
                };
                dir.Add (entry);
            }
            return new HzcArchive (file, this, dir, image_info);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var hzc = (HzcArchive)arc;
            using (var input = arc.File.CreateStream (0xC+hzc.ImageInfo.HeaderSize))
            using (var z = new ZLibStream (input, CompressionMode.Decompress))
            {
                uint frame_size = (uint)entry.Size;
                var pixels = new byte[frame_size];
                uint offset = 0;
                for (;;)
                {
                    if (pixels.Length != z.Read (pixels, 0, pixels.Length))
                        throw new EndOfStreamException();
                    if (offset >= entry.Offset)
                        break;
                    offset += frame_size;
                }
                return new BinMemoryStream (pixels, entry.Name);
            }
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var hzc = (HzcArchive)arc;
            var input = arc.File.CreateStream (0xC+hzc.ImageInfo.HeaderSize);
            try
            {
                return new HzcDecoder (input, hzc.ImageInfo, entry);
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }
    }
}
