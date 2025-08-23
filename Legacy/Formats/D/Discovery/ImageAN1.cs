using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Discovery
{
    //[Export(typeof(ImageFormat))]
    public class An1Format : Pr1Format
    {
        public override string         Tag => "AN1";
        public override string Description => "Discovery animation resource";
        public override uint     Signature => 0;

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".AN1"))
                return null;
            return base.ReadMetaData (file);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new AnReader (file, (PrMetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("An1Format.Write not implemented");
        }
    }

    internal class AnReader : PrReader
    {
        public AnReader (IBinaryStream file, PrMetaData info) : base (file, info)
        {
        }

        public new ImageData Unpack ()
        {
            UnpackPlanes();
            int frame_count = m_planes[0].ToUInt16 (2);
            int frame_width = 0x20;
            int frame_height = frame_count * 0x20;
            int output_stride = frame_width >> 1;
            var output = new byte[output_stride * frame_height];
            int src = frame_count * 0x16 + 6;
            m_plane_size = (output_stride >> 2) * frame_height;
            FlattenPlanes (src, output);
            Info.Width = (uint)frame_width;
            Info.Height = (uint)frame_height;
            return ImageData.Create (Info, PixelFormats.Indexed4, m_palette, output, output_stride);
        }
    }
}
