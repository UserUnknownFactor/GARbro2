using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows.Media;

namespace GameRes.Formats.EbgSystem
{
#if DEBUG
    [Export(typeof(ArchiveFormat))]
#endif
    public class BinOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BIN/EBG_SYSTEM"; } }
        public override string Description { get { return "EBG_SYSTEM bitmap archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        const uint BitmapSize = 0x96000;

        public override ArcFile TryOpen (ArcView file)
        {
            if (!VFS.IsPathEqualsToFileName (file.Name, "0000.bin"))
                return null;
            if ((file.MaxOffset % BitmapSize) != 0)
                return null;
            int count = (int)(file.MaxOffset / BitmapSize);
            if (!IsSaneCount (count))
                return null;

            uint offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = string.Format ("{0:D4}.bmp", i),
                    Type = "image",
                    Offset = offset,
                    Size = BitmapSize,
                };
                dir.Add (entry);
                offset += BitmapSize;
            }
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new BitmapDecoder (input);
        }

        internal class BitmapDecoder : BinaryImageDecoder
        {
            public BitmapDecoder (IBinaryStream input)
                : base (input, new ImageMetaData { Width = 640, Height = 480, BPP = 15 })
            {
            }

            protected override ImageData GetImageData ()
            {
                var pixels = m_input.ReadBytes ((int)BitmapSize);
                return ImageData.CreateFlipped (Info, PixelFormats.Bgr555, null, pixels, (int)Info.Width * 2);
            }
        }
    }
}
