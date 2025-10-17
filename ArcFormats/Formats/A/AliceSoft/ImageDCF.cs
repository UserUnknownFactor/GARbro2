using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.AliceSoft
{
    internal class DcfMetaData : ImageMetaData
    {
        public string   BaseName;
        public long     DataOffset;
        public bool     IsPcf;

        public override string GetComment ()
        {
            var comment = base.GetComment();
            if (!string.IsNullOrEmpty (BaseName))
                comment += Localization.Format ("BaseImage", Path.GetFileName(BaseName));
            if (IsPcf) comment += " (PCF)";
            return comment;
        }
    }

    internal interface IBaseImageReader
    {
        int     BPP { get; }
        byte[] Data { get; }

        void Unpack ();
    }

    [Export(typeof(ImageFormat))]
    public class DcfFormat : ImageFormat
    {
        public override string         Tag { get { return "DCF"; } }
        public override string Description { get { return "AliceSoft System incremental image"; } }
        public override uint     Signature { get { return  0x20666364; } } // 'dcf '
        public override bool      CanWrite { get { return  false; } }

        public DcfFormat ()
        {
            Extensions = new[] { "dcf", "pcf" };
            Signatures = new[] { 0x20666364u, 0x20666370u };
            Settings   = new IResourceSetting[] { MergeWithBase };
        }

        static readonly ResourceInstance<AfaOpener> Afa = new ResourceInstance<AfaOpener> ("AFA");

        public static readonly LocalResourceSetting MergeWithBase = new LocalResourceSetting ("DCFMergeWithBase");

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x1C);
            uint header_size = header.ToUInt32 (4);
            long data_pos = 8 + header_size;
            if (header.ToInt32 (8) != 1)
                return null;
            uint width  = header.ToUInt32 (0x0C);
            uint height = header.ToUInt32 (0x10);
            int bpp = header.ToInt32 (0x14);
            int name_length = header.ToInt32 (0x18);
            if (name_length <= 0)
                return null;
            int shift = (name_length % 7) + 1;
            var name_bits = stream.ReadBytes (name_length);
            for (int i = 0; i < name_length; ++i)
                name_bits[i] = Binary.RotByteL (name_bits[i], shift);

            return new DcfMetaData {
                Width = width,
                Height = height,
                BPP = bpp,
                BaseName = Afa.Value.NameEncoding.GetString (name_bits),
                DataOffset = data_pos,
                IsPcf = stream.Signature == 0x20666370u,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (DcfMetaData)info;
            var reader = new DcfReader (stream, meta, MergeWithBase.Get<bool>());
            reader.Unpack();
            var imageData = ImageData.Create (reader.Info, reader.Format, null, reader.Data, reader.Stride);
            return StoreMetadata (imageData, (DcfMetaData)info);
        }

        private ImageData StoreMetadata (ImageData imageData, DcfMetaData meta)
        {
            var pngImage = imageData as PngImageData ?? new PngImageData (imageData.Bitmap, meta);

            if (!string.IsNullOrEmpty (meta.BaseName))
                pngImage.CustomChunks["dCFb"] = Encoding.UTF8.GetBytes (meta.BaseName);
            if (meta.IsPcf)
                pngImage.CustomChunks["dCFp"] = new byte[] {  (byte)1 };

            return pngImage;
        }

        public override void Write (Stream file, ImageData image)
        {
            string baseName = null;
            if (image is PngImageData pngData)
            {
                if (pngData.CustomChunks.TryGetValue ("dCFb", out var baseNameChunk))
                    baseName = Encoding.UTF8.GetString (baseNameChunk);
            }

            if (baseName == null || !VFS.FileExists (baseName))
            {
                WriteStandalone (file, image);
                return;
            }

            var (baseBPP, baseData) = DcfBaseImageHelper.ReadBaseImage (baseName, baseName, (uint)image.Width, (uint)image.Height);

            if (baseData == null)
                throw new InvalidOperationException ("Failed to read base image: " + baseName);

            // Convert base image to BGRA32 if needed
            byte[] basePixels = baseData;
            if (baseBPP == 24)
            {
                basePixels = new byte[image.Width * image.Height * 4];
                int srcStride = (int)image.Width * 3;
                int dstStride = (int)image.Width * 4;
                for (int y = 0; y < image.Height; y++)
                {
                    for (int x = 0; x < image.Width; x++)
                    {
                        int srcIdx = y * srcStride + x * 3;
                        int dstIdx = y * dstStride + x * 4;
                        basePixels[dstIdx    ] = baseData[srcIdx    ];
                        basePixels[dstIdx + 1] = baseData[srcIdx + 1];
                        basePixels[dstIdx + 2] = baseData[srcIdx + 2];
                        basePixels[dstIdx + 3] = 0xFF;
                    }
                }
            }

            var targetPixels = GetPixels (image);

            // Create chunk map (16x16 blocks)
            int chunksW = ((int)image.Width + 15) / 16;
            int chunksH = ((int)image.Height + 15) / 16;
            var chunkMap = new byte[chunksW * chunksH + 4];

            // Write chunk map size at beginning
            LittleEndian.Pack ((uint)(chunkMap.Length - 4), chunkMap, 0);

            var diffPixels = new byte[targetPixels.Length];
            Array.Copy (targetPixels, diffPixels, targetPixels.Length);

            // Mark unchanged chunks and copy base data to diff
            for (int cy = 0; cy < chunksH; cy++)
            {
                for (int cx = 0; cx < chunksW; cx++)
                {
                    int chunkIndex = cy * chunksW + cx + 4;
                    bool isDifferent = false;

                    // Check if chunk is different
                    for (int by = 0; by < 16 && !isDifferent; by++)
                    {
                        int y = cy * 16 + by;
                        if (y >= image.Height) break;

                        for (int bx = 0; bx < 16; bx++)
                        {
                            int x = cx * 16 + bx;
                            if (x >= image.Width) break;

                            int pixelOffset = (y * (int)image.Width + x) * 4;
                            if (targetPixels[pixelOffset] != basePixels[pixelOffset] ||
                                targetPixels[pixelOffset + 1] != basePixels[pixelOffset + 1] ||
                                targetPixels[pixelOffset + 2] != basePixels[pixelOffset + 2] ||
                                targetPixels[pixelOffset + 3] != basePixels[pixelOffset + 3])
                            {
                                isDifferent = true;
                                break;
                            }
                        }
                    }

                    if (!isDifferent)
                    {
                        // Mark chunk as unchanged (1 = use base)
                        chunkMap[chunkIndex] = 1;

                        // Copy base pixels to diff image for this chunk
                        for (int by = 0; by < 16; by++)
                        {
                            int y = cy * 16 + by;
                            if (y >= image.Height) break;

                            for (int bx = 0; bx < 16; bx++)
                            {
                                int x = cx * 16 + bx;
                                if (x >= image.Width) break;

                                int pixelOffset = (y * (int)image.Width + x) * 4;
                                diffPixels[pixelOffset    ] = basePixels[pixelOffset    ];
                                diffPixels[pixelOffset + 1] = basePixels[pixelOffset + 1];
                                diffPixels[pixelOffset + 2] = basePixels[pixelOffset + 2];
                                diffPixels[pixelOffset + 3] = basePixels[pixelOffset + 3];
                            }
                        }
                    }
                }
            }

            byte[] compressedChunkMap;
            using (var ms = new MemoryStream())
            {
                using (var zlib = new ZLibStream (ms, CompressionMode.Compress, CompressionLevel.Level9))
                {
                    zlib.Write (chunkMap, 0, chunkMap.Length);
                }
                compressedChunkMap = ms.ToArray();
            }

            // Create diff QNT
            byte[] qntData;
            using (var ms = new MemoryStream())
            {
                var diffImage = ImageData.Create (new ImageMetaData { 
                    Width = image.Width, 
                    Height = image.Height, 
                    BPP = 32 
                }, PixelFormats.Bgra32, null, diffPixels);

                var qntFormat = new QntFormat();
                qntFormat.Write (ms, diffImage);
                qntData = ms.ToArray();
            }

            if (!baseName.EndsWith (".qnt", StringComparison.OrdinalIgnoreCase))
                baseName = Path.GetFileNameWithoutExtension (baseName) + ".qnt";

            WriteDcf (file, baseName, compressedChunkMap, qntData, (uint)image.Width, (uint)image.Height);
        }

        void WriteStandalone (Stream file, ImageData image)
        {
            // Create QNT data
            byte[] qntData;
            using (var ms = new MemoryStream())
            {
                var qntFormat = new QntFormat();
                qntFormat.Write (ms, image);
                qntData = ms.ToArray();
            }

            // Empty chunk map (all chunks are new)
            var chunkMap = new byte[4];
            byte[] compressedChunkMap;
            using (var ms = new MemoryStream())
            {
                using (var zlib = new ZLibStream (ms, CompressionMode.Compress))
                {
                    zlib.Write (chunkMap, 0, 4);
                }
                compressedChunkMap = ms.ToArray();
            }

            WriteDcf (file, "", compressedChunkMap, qntData, (uint)image.Width, (uint)image.Height);
        }

        void WriteDcf (Stream file, string baseName, byte[] chunkMap, byte[] qntData, uint width, uint height)
        {
            var encoding = Afa.Value.NameEncoding ?? Encodings.cp932;
            var nameBytes = encoding.GetBytes (baseName);
            int shift = (nameBytes.Length % 7) + 1;
            for (int i = 0; i < nameBytes.Length; i++)
                nameBytes[i] = Binary.RotByteR (nameBytes[i], shift);

            using (var writer = new BinaryWriter (file))
            {
                // DCF header
                writer.Write (0x20666364); // 'dcf '
                writer.Write ((uint)(0x14 + nameBytes.Length)); // header size
                writer.Write ((int)1);     // version
                writer.Write (width);
                writer.Write (height);
                writer.Write ((int)32);    // bpp
                writer.Write ((int)nameBytes.Length);
                writer.Write (nameBytes);

                // DFDL section (chunk map)
                writer.Write (0x6C646664); // 'dfdl'
                writer.Write ((uint)(4 + chunkMap.Length));
                writer.Write ((int)(chunkMap.Length + 4)); // uncompressed size
                writer.Write (chunkMap);

                // DCGD section (QNT data)
                writer.Write (0x64676364); // 'dcgd'
                writer.Write ((uint)qntData.Length);
                writer.Write (qntData);
            }
        }

        byte[] GetPixels (ImageData image)
        {
            var bitmap = image.Bitmap;
            if (bitmap.Format != PixelFormats.Bgra32)
                bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgra32, null, 0);

            int stride = (int)image.Width * 4;
            var pixels = new byte[stride * (int)image.Height];
            bitmap.CopyPixels (pixels, stride, 0);
            return pixels;
        }
    }

    internal sealed class DcfReader : IBaseImageReader
    {
        IBinaryStream       m_input;
        DcfMetaData         m_info;
        byte[]              m_output;
        byte[]              m_mask = null;
        byte[]              m_base = null;
        int                 m_overlay_bpp;
        int                 m_base_bpp;
        bool                m_merge_with_base;

        static readonly ResourceInstance<ImageFormat> s_QntFormat = new ResourceInstance<ImageFormat> ("QNT");
        static readonly ResourceInstance<ImageFormat> s_DcfFormat = new ResourceInstance<ImageFormat> ("DCF");

        internal ImageFormat  Qnt { get { return s_QntFormat.Value; } }
        internal ImageFormat  Dcf { get { return s_DcfFormat.Value; } }

        public int            BPP { get { return m_base_bpp; } }
        public ImageMetaData Info { get; private set; }
        public byte[]        Data { get { return m_output; } }
        public PixelFormat Format { get; private set; }
        public int         Stride { get; private set; }

        public DcfReader (IBinaryStream input, DcfMetaData info, bool mergeWithBase = true)
        {
            m_input = input;
            m_info = info;
            Info = info;
            m_merge_with_base = mergeWithBase;
        }

        public void Unpack ()
        {
            int pt_x = 0;
            int pt_y = 0;
            long next_pos = m_info.DataOffset;
            for (;;)
            {
                m_input.Position = next_pos;
                uint id = m_input.ReadUInt32();
                next_pos += 8 + m_input.ReadUInt32();
                if (0x6C646664 == id) // 'dfdl'
                {
                    int unpacked_size = m_input.ReadInt32();
                    if (unpacked_size <= 0)
                        continue;
                    m_mask = new byte[unpacked_size];
                    using (var input = new ZLibStream (m_input.AsStream, CompressionMode.Decompress, true))
                        input.Read (m_mask, 0, unpacked_size);
                }
                else if (0x6C647470 == id) // 'ptdl'
                {
                    pt_x = m_input.ReadInt32();
                    pt_y = m_input.ReadInt32();
                }
                else if (0x64676364 == id || 0x64676370 == id) // 'dcgd' || 'pcgd'
                    break;
            }

            long qnt_pos = m_input.Position;
            if (m_input.ReadUInt32() != Qnt.Signature)
                throw new InvalidFormatException();

            using (var reg = new StreamRegion (m_input.AsStream, qnt_pos, true))
            using (var qnt = new BinaryStream (reg, m_input.Name))
            {
                var qnt_info = Qnt.ReadMetaData (qnt) as QntMetaData;
                if (null == qnt_info)
                    throw new InvalidFormatException();

                var overlay = new QntFormat.Reader (reg, qnt_info);
                overlay.Unpack();
                m_overlay_bpp = overlay.BPP;

                if (m_merge_with_base && (m_mask != null || m_info.IsPcf))
                    ReadBaseImage();

                if (m_info.IsPcf)
                {
                    if (m_merge_with_base)
                    {
                        if (null == m_base)
                            SetEmptyBase();
                        qnt_info.OffsetX = pt_x;
                        qnt_info.OffsetY = pt_y;
                        BlendOverlay (qnt_info, overlay.Data);
                        m_output = m_base;
                        SetFormat (m_info.iWidth, m_base_bpp);
                    }
                    else
                    {
                        m_output = overlay.Data;
                        SetFormat (qnt_info.iWidth, m_overlay_bpp);
                        Info = qnt_info;
                    }
                }
                else if (m_base != null && m_merge_with_base)
                {
                    m_output = MaskOverlay (overlay.Data);
                    SetFormat (m_info.iWidth, m_overlay_bpp);
                }
                else
                {
                    m_output = overlay.Data;
                    SetFormat (qnt_info.iWidth, m_overlay_bpp);
                    if (!m_merge_with_base && m_mask == null)
                        Info = qnt_info;
                }
            }
        }

        void SetFormat (int width, int bpp)
        {
            Format = 24 == bpp ? PixelFormats.Bgr24 : PixelFormats.Bgra32;
            Stride = width * (bpp / 8);
        }

        void SetEmptyBase ()
        {
            m_base_bpp = 32;
            m_base = new byte[m_info.Width * m_info.Height * 4];
        }

        byte[] MaskOverlay (byte[] overlay)
        {
            int blocks_x = m_info.iWidth / 0x10;
            int blocks_y = m_info.iHeight / 0x10;
            int base_step = m_base_bpp / 8;
            int overlay_step = m_overlay_bpp / 8;
            int base_stride = m_info.iWidth * base_step;
            int overlay_stride = m_info.iWidth * overlay_step;
            int mask_pos = 4;
            for (int y = 0; y < blocks_y; ++y)
            {
                int base_pos = y * 0x10 * base_stride;
                int dst_pos  = y * 0x10 * overlay_stride;
                for (int x = 0; x < blocks_x; ++x)
                {
                    if (0 == m_mask[mask_pos++])
                        continue;
                    for (int by = 0; by < 0x10; ++by)
                    {
                        int src = base_pos + by * base_stride    + x * 0x10 * base_step;
                        int dst = dst_pos  + by * overlay_stride + x * 0x10 * overlay_step;
                        for (int bx = 0; bx < 0x10; ++bx)
                        {
                            overlay[dst  ] = m_base[src  ];
                            overlay[dst+1] = m_base[src+1];
                            overlay[dst+2] = m_base[src+2];
                            if (4 == overlay_step)
                            {
                                overlay[dst+3] = 4 == base_step ? m_base[src+3] : (byte)0xFF;
                            }
                            src += base_step;
                            dst += overlay_step;
                        }
                    }
                }
            }
            return overlay;
        }

        void BlendOverlay (ImageMetaData overlay_info, byte[] overlay)
        {
            int ovl_x = overlay_info.OffsetX;
            int ovl_y = overlay_info.OffsetY;
            int ovl_width = overlay_info.iWidth;
            int ovl_height = overlay_info.iHeight;
            int base_width = m_info.iWidth;
            int base_height = m_info.iHeight;
            if (checked(ovl_x + ovl_width) > base_width)
                ovl_width = base_width - ovl_x;
            if (checked(ovl_y + ovl_height) > base_height)
                ovl_height = base_height - ovl_y;
            if (ovl_height <= 0 || ovl_width <= 0)
                return;

            int dst_stride = m_info.iWidth * 4;
            int src_stride = overlay_info.iWidth * 4;
            int dst = ovl_y * dst_stride + ovl_x * 4;
            int src = 0;
            int gap = dst_stride - src_stride;
            for (int y = 0; y < overlay_info.iHeight; ++y)
            {
                for (int x = 0; x < overlay_info.iWidth; ++x)
                {
                    byte src_alpha = overlay[src+3];
                    if (src_alpha != 0)
                    {
                        if (0xFF == src_alpha || 0 == m_base[dst+3])
                        {
                            m_base[ dst ] = overlay[ src ];
                            m_base[dst+1] = overlay[src+1];
                            m_base[dst+2] = overlay[src+2];
                            m_base[dst+3] = src_alpha;
                        }
                        else
                        {
                            m_base[dst  ] = (byte)((overlay[src  ] * src_alpha
                                                    + m_base[dst  ] * (0xFF - src_alpha)) / 0xFF);
                            m_base[dst+1] = (byte)((overlay[src+1] * src_alpha
                                                    + m_base[dst+1] * (0xFF - src_alpha)) / 0xFF);
                            m_base[dst+2] = (byte)((overlay[src+2] * src_alpha
                                                    + m_base[dst+2] * (0xFF - src_alpha)) / 0xFF);
                            m_base[dst+3] = (byte)Math.Max (src_alpha, m_base[dst+3]);
                        }
                    }
                    dst += 4;
                    src += 4;
                }
                dst += gap;
            }
        }

        void ReadBaseImage()
        {
            var result = DcfBaseImageHelper.ReadBaseImage (m_info.FileName, m_info.BaseName, m_info.Width, m_info.Height);
            m_base_bpp = result.bpp;
            m_base = result.data;
        }
    }

    internal static class DcfBaseImageHelper
    {
        public static (int bpp, byte[] data) ReadBaseImage (string currentFileName, string baseFileName, uint width, uint height)
        {
            try
            {
                string dir_name = VFS.GetDirectoryName (currentFileName);
                string base_name = Path.ChangeExtension (baseFileName, "qnt");
                base_name = VFS.CombinePath (dir_name, base_name);

                ImageFormat base_format = null;
                Func<IBinaryStream, ImageMetaData, IBaseImageReader> create_reader;

                if (VFS.FileExists (base_name))
                {
                    base_format = new QntFormat();
                    create_reader = (s, m) => new QntFormat.Reader (s.AsStream, (QntMetaData)m);
                }
                else
                {
                    base_name = Path.ChangeExtension (baseFileName, "pcf");
                    if (VFS.IsPathEqualsToFileName (currentFileName, base_name))
                        return (0, null);

                    base_name = VFS.CombinePath (dir_name, base_name);
                    if (!VFS.FileExists (base_name))
                    {
                        base_name = Path.ChangeExtension (baseFileName, "dcf");
                        base_name = VFS.CombinePath (dir_name, base_name);
                    }

                    base_format = new DcfFormat();
                    create_reader = (s, m) => new DcfReader (s, (DcfMetaData)m);
                }

                using (var base_file = VFS.OpenBinaryStream (base_name))
                {
                    var base_info = base_format.ReadMetaData (base_file);
                    if (base_info != null && width == base_info.Width && height == base_info.Height)
                    {
                        base_info.FileName = base_name;
                        var reader = create_reader (base_file, base_info);
                        reader.Unpack();
                        return (reader.BPP, reader.Data);
                    }
                }
            }
            catch (Exception X)
            {
                Trace.WriteLine (X.Message, "[DCF]");
            }

            return (0, null);
        }
    }
}
