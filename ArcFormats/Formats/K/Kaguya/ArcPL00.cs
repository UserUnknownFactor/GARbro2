using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Windows.Media;

namespace GameRes.Formats.Kaguya
{
    class PL00Entry : PackedEntry
    {
        public int FrameIndex;
        public ImageMetaData ImageInfo;
    }

    [Export(typeof(ArchiveFormat))]
    public class PL00Opener : ArchiveFormat
    {
        public override string Tag { get { return "PL00/KAGUYA"; } }
        public override string Description { get { return "KaGuYa script engine animation resource"; } }
        public override uint Signature { get { return 0x30304C50; } } // 'PL00'
        public override bool IsHierarchic { get { return false; } }
        public override bool CanWrite { get { return false; } }

        public PL00Opener()
        {
            Extensions = new string[] { "plt" };
        }

        public override ArcFile TryOpen(ArcView file)
        {
            uint current_offset = 4;
            int frame_count = file.View.ReadUInt16(current_offset);
            if (!IsSaneCount(frame_count))
                return null;
            current_offset += 2;
            string base_name = Path.GetFileNameWithoutExtension(file.Name);
            var dir = new List<Entry>(frame_count);
            var info = new ImageMetaData
            {
                OffsetX = file.View.ReadInt32(current_offset),
                OffsetY = file.View.ReadInt32(current_offset + 4),
                Width = file.View.ReadUInt32(current_offset + 8),
                Height = file.View.ReadUInt32(current_offset + 12),
            };
            int channels = file.View.ReadInt32(38);
            info.BPP = channels * 8;
            current_offset += 16;
            for (int i = 0; i < frame_count; ++i)
            {
                int offsetx = file.View.ReadInt32(current_offset);
                int offsety = file.View.ReadInt32(current_offset + 4);
                uint width = file.View.ReadUInt32(current_offset + 8);
                uint height = file.View.ReadUInt32(current_offset + 12);
                channels = file.View.ReadInt32(current_offset + 16);
                uint size = (uint)(width * height * channels);
                current_offset += 20;
                var entry = new PL00Entry
                {
                    FrameIndex = i,
                    Name = string.Format("{0}#{1:D2}", base_name, i),
                    Type = "image",
                    Offset = current_offset,
                    Size = size,
                    IsPacked = false,
                    ImageInfo = new ImageMetaData
                    {
                        OffsetX = offsetx,
                        OffsetY = offsety,
                        Width = width,
                        Height = height,
                        BPP = channels * 8,
                    }
                };
                dir.Add(entry);
                current_offset += size;
            }
            return new PL00Archive(file, this, dir, info);
        }

        public override IImageDecoder OpenImage(ArcFile arc, Entry entry)
        {
            var anent = (PL00Entry)entry;
            var input = arc.File.CreateStream(entry.Offset, entry.Size);
            var pixels = input.ReadBytes((int)anent.Size);
            return new BitmapDecoder(pixels, anent.ImageInfo);
        }
    }

    class PL00Archive : ArcFile
    {
        public readonly ImageMetaData ImageInfo;

        public PL00Archive(ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, ImageMetaData base_info)
            : base(arc, impl, dir)
        {
            ImageInfo = base_info;
        }
    }

}
