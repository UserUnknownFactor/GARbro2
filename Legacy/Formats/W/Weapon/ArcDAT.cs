using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.IO;
using System.Windows.Media;

// [000331][WEAPON] Seido Techou

namespace GameRes.Formats.Weapon
{
    internal class CgEntry : Entry
    {
        public uint     Width;
        public uint     Height;
    }

#if DEBUG
    [Export(typeof(ArchiveFormat))]
#endif
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/WEAPON"; } }
        public override string Description { get { return "Weapon resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        const uint DefaultWidth = 800;

        public override ArcFile TryOpen (ArcView file)
        {
            var arc_name = Path.GetFileName (file.Name);
            Size[] dim_table;
            if (!KnownFileTables.TryGetValue (arc_name, out dim_table))
                return null;
            long offset = 0;
            var base_name = Path.GetFileNameWithoutExtension (arc_name);
            var dir = new List<Entry> (dim_table.Length);

            for (int i = 0; i < dim_table.Length; ++i)
            {
                var name = string.Format ("{0}#{1:D4}", base_name, i);
                uint width = (uint)dim_table[i].Width;
                uint height = (uint)dim_table[i].Height;
                var entry = new CgEntry {
                    Name = name,
                    Type = "image",
                    Offset = offset,
                    Size = height * width * 2,
                    Width = width,
                    Height = height,
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                offset += entry.Size;
            }

            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var cgent = (CgEntry)entry;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var info  = new ImageMetaData { Width = cgent.Width, Height = cgent.Height, BPP = 16 };
            return new CgDecoder (input, info);
        }

        static readonly Dictionary<string, Size[]> KnownFileTables = new Dictionary<string, Size[]>(StringComparer.OrdinalIgnoreCase) {
            ["eventcg.dat"] = CreateSizes((800, 600,  69), (800, 1200, new[] { 5, 12, 16, 58, 62 })),
            ["buy.dat"]     = CreateSizes((800, 900,  1),  (128, 160,  148)),
            ["heyacg.dat"]  = CreateSizes((236, 174,  14)),
            ["kigaecg.dat"] = CreateSizes((435, 600,  148)),
            ["chibicg.dat"] = CreateSizes((64,  64,   500)),
            ["omake.dat"]   = CreateSizes((800, 600,  1),  (800, 300,  1), (384, 96, 213)),
            ["result.dat"]  = CreateSizes((528, 600,  2),  (272, 600,  4)),
            ["title.dat"]   = CreateSizes((800, 1076, 1),  (800, 600,  1))
        };

        static Size[] CreateSizes(params (int w, int h, object count)[] specs) {
            var list = new List<Size>();
            foreach (var (w, h, count) in specs) {
                if (count is int n) {
                    for (int i = 0; i < n; i++)
                        list.Add(new Size(w, h));
                } else if (count is int[] indices) {
                    int max = indices.Max() + 1;
                    var size = new Size(w, h);
                    for (int i = 0; i < max; i++)
                        list.Add(indices.Contains(i) ? size : new Size(800, 600));
                }
            }
            return list.ToArray();
        }
    }

    internal class CgDecoder : BinaryImageDecoder
    {
        public CgDecoder (IBinaryStream input, ImageMetaData info) : base (input, info)
        {
        }

        protected override ImageData GetImageData ()
        {
            int stride = (int)Info.Width * 2;
            var pixels = m_input.ReadBytes (stride * (int)Info.Height);
            for (int i = 0; i < pixels.Length; i += 2)
            {
                int hi = pixels[i] << 2 | (pixels[i+1] & 3);
                int lo = pixels[i+1] >> 2 | (pixels[i] & ~0x1F);
                pixels[i] = (byte)lo;
                pixels[i+1] = (byte)hi;
            }
            return ImageData.Create (Info, PixelFormats.Bgr555, null, pixels, stride);
        }
    }
}
