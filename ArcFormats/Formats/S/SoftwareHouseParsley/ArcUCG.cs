using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Compression;

namespace GameRes.Formats.Parsley
{
    [Export(typeof(ArchiveFormat))]
    public class UcgOpener : ArchiveFormat
    {
        public override string         Tag { get { return "UCG"; } }
        public override string Description { get { return "Software House Parsley CG archive"; } }
        public override uint     Signature { get { return 0x64; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public UcgOpener ()
        {
            Extensions = new string[] { "" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            var arc_name = Path.GetFileName (file.Name);
            bool is_cg = arc_name.Contains ("CG");
            uint index_offset = 8;
            uint first_offset = 8 + (uint)count * 0x18;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x14);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                var entry = Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x14);
                if (entry.Offset < first_offset || entry.Offset > file.MaxOffset)
                    return null;
                if (is_cg)
                    entry.Type = "image";
                dir.Add (entry);
                index_offset += 0x18;
            }
            for (int i = 1; i < count; ++i)
            {
                var entry = dir[i-1];
                entry.Size = (uint)(dir[i].Offset - entry.Offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
            }
            dir[count - 1].Size = (uint)(file.MaxOffset - dir[count - 1].Offset);
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            uint packed_size = arc.File.View.ReadUInt32 (entry.Offset);
            if (packed_size + 0x1C != entry.Size)
                return base.OpenImage (arc, entry);
            var info = new ImageMetaData {
                Width = arc.File.View.ReadUInt32 (entry.Offset+8),
                Height = arc.File.View.ReadUInt32 (entry.Offset+12),
                BPP = 32
            };
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new UCg32Decoder (input, info);
        }
    }

    internal class UCg32Decoder : BinaryImageDecoder
    {
        public UCg32Decoder (IBinaryStream input, ImageMetaData info) : base (input, info)
        {
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = 0x1C;
            using (var lz = new LzssStream (m_input.AsStream, LzssMode.Decompress, true))
            {
                int stride = Info.iWidth * 4;
                var pixels = new byte[stride * Info.iHeight];
                lz.Read (pixels, 0, pixels.Length);
                return ImageData.CreateFlipped (Info, PixelFormats.Bgra32, null, pixels, stride);
            }
        }
    }
}
