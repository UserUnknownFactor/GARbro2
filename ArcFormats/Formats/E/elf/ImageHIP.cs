using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Elf
{
    internal class HipMetaData : HizMetaData
    {
        public uint SecondOffset;
        public uint SecondLength;

        public HipMetaData() { }

        public HipMetaData(HizMetaData hiz)
        {
            Width = hiz.Width;
            Height = hiz.Height;
            BPP = hiz.BPP;
            IsPacked = hiz.IsPacked;
            DataOffset = hiz.DataOffset;
            UnpackedSize = hiz.UnpackedSize;
            Version = hiz.Version;
            Unknown1 = hiz.Unknown1;
            Unknown2 = hiz.Unknown2;
            Unknown3 = hiz.Unknown3;
            Unknown4 = hiz.Unknown4;
        }

        public override string GetComment()
        {
            string comment = base.GetComment();
            if (SecondOffset > 0 && SecondLength >= 4)
                comment += " ["+ Localization._T("Type_archive") + "]";
            return comment;
        }
    }

    [Export(typeof(ImageFormat))]
    public class HipFormat : HizFormat
    {
        public override string         Tag { get { return "HIP"; } }
        public override string Description { get { return "elf simple archive image format"; } }
        public override uint     Signature { get { return  0x00706968; } } // 'hip'
        public override bool      CanWrite { get { return  true; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x18);
            int index_offset = 0xC;
            uint first_offset = header.ToUInt32 (index_offset);
            if (0 == first_offset)
            {
                index_offset += 4;
                first_offset = header.ToUInt32 (index_offset);
                if (0 == first_offset)
                    return null;
            }
            index_offset += 4;

            long first_length;
            uint second_offset = header.ToUInt32 (index_offset);
            uint second_length = 0;

            if (second_offset == 0)
                first_length = stream.Length - first_offset;
            else if (second_offset < first_offset)
                return null;
            else
            {
                first_length = second_offset - first_offset;
                second_length = (uint)(stream.Length - second_offset);
            }

            using (var reg = new StreamRegion (stream.AsStream, first_offset, first_length, true))
            using (var hiz = new BinaryStream (reg, stream.Name))
            {
                var info = base.ReadMetaData (hiz) as HizMetaData;
                if (info == null)
                    return null;

                var hipMeta = new HipMetaData(info)
                {
                    DataOffset = info.DataOffset + first_offset,
                    SecondOffset = second_offset,
                    SecondLength = second_length
                };

                return hipMeta;
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            return base.Read (stream, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            using (var writer = new BinaryWriter (file))
            {
                writer.Write (0x00706968);   // 'hip'
                writer.Write (new byte[8]);  // padding
                writer.Write ((uint)0x18);   // first image offset
                writer.Write ((uint)0);      // no second data
                
                base.Write (file, image);
            }
        }
    }
}