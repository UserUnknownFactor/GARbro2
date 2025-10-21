using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.LiveMaker
{
    public class GalXLayerEntry : ImageEntry
    {
        public int FrameIndex;
        public int LayerIndex;
    }

    [Export(typeof(ArchiveFormat))]
    public class GalXArchiveOpener : ArchiveFormat
    {
        public override string         Tag { get { return "GALX/ARC"; } }
        public override string Description { get { return "LiveMaker2 multi-frame image as archive"; } }
        public override uint     Signature { get { return  0x656C6147; } } // 'Gale'
        public override bool  IsHierarchic { get { return  false; } }
        public override bool      CanWrite { get { return  false; } }

        static readonly string METADATA_FILE = ".galxmetadata.xml";

        public override ArcFile TryOpen(ArcView file)
        {
            var header = file.View.ReadBytes(0, 12);
            if (!header.AsciiEqual("GaleX200"))
                return null;

            int header_size = header.ToInt32(8);
            
            GalXMetaData meta;
            XmlDocument xmlDoc;
            
            using (var input = file.CreateStream())
            {
                input.Position = 12;
                var compressed_header = input.ReadBytes(header_size);
                
                using (var zheader = new MemoryStream(compressed_header))
                using (var xheader = new ZLibStream(zheader, CompressionMode.Decompress))
                {
                    var format = new GalXFormat();
                    xmlDoc = format.ReadXml(xheader);
                    
                    // Reset stream for metadata reading
                    zheader.Position = 0;
                    using (var xheader2 = new ZLibStream(zheader, CompressionMode.Decompress))
                    {
                        meta = format.XMLToMetadata(xheader2, header_size + 12);
                    }
                }
            }

            if (meta == null)
                return null;

            var dir = new List<Entry>();
            
            // Parse frame structure from XML
            var frameNodes = xmlDoc.SelectNodes("//Frames/Frame");
            if (frameNodes == null || frameNodes.Count == 0)
                return null;

            int frameIndex = 0;
            foreach (XmlNode frameNode in frameNodes)
            {
                var layers = frameNode.SelectSingleNode("Layers");
                if (layers == null)
                    continue;

                var layer_nodes = layers.SelectNodes("Layer");
                if (layer_nodes == null)
                    continue;

                int layerIndex = 0;
                foreach (XmlNode layerNode in layer_nodes)
                {
                    string extension = meta.Compression == 2 ? "jpg" : "png";
                    dir.Add(new GalXLayerEntry
                    {
                        Name = string.Format("frame_{0:D2}_layer_{1:D2}.{2}", frameIndex, layerIndex, extension),
                        Type = "image",
                        Offset = 0,
                        Size = 0,
                        FrameIndex = frameIndex,
                        LayerIndex = layerIndex
                    });
                    layerIndex++;
                }
                frameIndex++;
            }

            // Add metadata file
            string xmlContent = xmlDoc.OuterXml;
            var xmlBytes = Encoding.UTF8.GetBytes(xmlContent);
            
            dir.Insert(0, new XmlMetadataEntry
            {
                Name = METADATA_FILE,
                Type = "script",
                Offset = 0,
                Size = (uint)xmlBytes.Length,
                XmlContent = xmlBytes,
                XmlDoc = xmlDoc
            });

            return new GalXArchive(file, this, dir, meta, xmlDoc);
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            if (entry.Name == METADATA_FILE)
            {
                var metaEntry = entry as XmlMetadataEntry;
                if (metaEntry != null && metaEntry.XmlContent != null)
                    return new MemoryStream(metaEntry.XmlContent);
            }

            var galXArc = arc as GalXArchive;
            var layerEntry = entry as GalXLayerEntry;
            if (galXArc == null || layerEntry == null)
                return base.OpenEntry(arc, entry);

            bool useJpeg = galXArc.Metadata.Compression == 2;

            using (var input = arc.File.CreateStream())
            {
                if (useJpeg)
                {
                    var reader = new GalXJpegExtractor(input, galXArc.Metadata, galXArc.XmlDoc);
                    return reader.ExtractLayerAsJpeg(layerEntry.FrameIndex, layerEntry.LayerIndex);
                }

                using (var reader = new GalXReader(input, galXArc.Metadata, 0))
                {
                    reader.UnpackAllFrames();

                    if (layerEntry.FrameIndex >= reader.Frames.Count)
                        throw new InvalidOperationException("Frame index out of range");

                    var frame = reader.Frames[layerEntry.FrameIndex];
                    if (layerEntry.LayerIndex >= frame.Layers.Count)
                        throw new InvalidOperationException("Layer index out of range");

                    var layer = frame.Layers[layerEntry.LayerIndex];

                    BitmapSource bitmap;
                    PixelFormat format;
                    BitmapPalette palette = null;

                    if (layer.Alpha != null)
                    {
                        var frameData = reader.FlattenFrame(frame);
                        bitmap = BitmapSource.Create(
                            frame.Width, frame.Height,
                            96, 96,
                            frameData.Format,
                            frameData.Palette,
                            frameData.Data,
                            frameData.Stride);
                    }
                    else
                    {
                        if (frame.BPP <= 8 && frame.Palette != null)
                        {
                            palette = new BitmapPalette(frame.Palette);
                            format = frame.BPP == 8 ? PixelFormats.Indexed8 : PixelFormats.Indexed4;
                        }
                        else if (frame.BPP == 16)
                            format = PixelFormats.Bgr565;
                        else if (frame.BPP == 24)
                            format = PixelFormats.Bgr24;
                        else
                            format = PixelFormats.Bgr32;

                        bitmap = BitmapSource.Create(
                            frame.Width, frame.Height,
                            96, 96, format, palette,
                            layer.Pixels, frame.Stride);
                    }

                    bitmap.Freeze();

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));

                    var ms = new MemoryStream();
                    encoder.Save(ms);
                    ms.Position = 0;
                    return ms;
                }
            }
        }

        public override void Create(
            Stream output, IEnumerable<Entry> list, ResourceOptions options,
            EntryCallback callback)
        {
            var entries = list.Where(e => !e.Name.EndsWith(METADATA_FILE)).ToList();

            XmlDocument metadata = null;
            var metadataFile = list.FirstOrDefault(e => e.Name.EndsWith(METADATA_FILE));
            if (metadataFile != null)
            {
                try
                {
                    metadata = new XmlDocument();
                    using (var metaStream = File.OpenRead(metadataFile.Name))
                    {
                        metadata.Load(metaStream);
                    }
                }
                catch { }
            }

            if (metadata == null)
                throw new InvalidFormatException("GAL/X metadata file (.galxmetadata.xml) required for creation");

            var frameGroups = entries
                .Select(e =>
                {
                    var name = Path.GetFileNameWithoutExtension(e.Name);
                    var parts = name.Split('_');
                    if (parts.Length >= 4 && parts[0] == "frame" && parts[2] == "layer")
                    {
                        return new FrameLayerInfo
                        {
                            Entry = e,
                            Frame = int.Parse(parts[1]),
                            Layer = int.Parse(parts[3])
                        };
                    }
                    return null;
                })
                .Where(x => x != null)
                .GroupBy(x => x.Frame)
                .OrderBy(g => g.Key)
                .ToList();

            WriteGalXArchive(output, frameGroups, metadata, callback);
        }

        private void WriteGalXArchive(Stream output,
            List<IGrouping<int, FrameLayerInfo>> frameGroups,
            XmlDocument metadata,
            EntryCallback callback)
        {
            using (var writer = new BinaryWriter(output, Encoding.ASCII, true))
            {
                // Write GaleX200 header
                writer.Write(Encoding.ASCII.GetBytes("GaleX200"));

                // Compress XML header
                byte[] compressedXml;
                using (var ms = new MemoryStream())
                {
                    using (var zlib = new ZLibStream(ms, CompressionMode.Compress))
                    {
                        var xmlBytes = Encoding.UTF8.GetBytes(metadata.OuterXml);
                        zlib.Write(xmlBytes, 0, xmlBytes.Length);
                    }
                    compressedXml = ms.ToArray();
                }

                writer.Write(compressedXml.Length);
                writer.Write(compressedXml);

                // Get frame nodes from XML
                var frameNodes = metadata.SelectNodes("//Frames/Frame");
                if (frameNodes == null || frameNodes.Count == 0)
                    throw new InvalidFormatException("No Frame elements in XML");

                int current = 0;
                int frameIndex = 0;
                
                foreach (var frameGroup in frameGroups)
                {
                    if (frameIndex >= frameNodes.Count)
                        break;

                    var frameNode = frameNodes[frameIndex];
                    var layersNode = frameNode.SelectSingleNode("Layers");
                    var layerNodes = layersNode?.SelectNodes("Layer");
                    
                    if (layerNodes == null)
                        continue;

                    var layers = frameGroup.OrderBy(x => x.Layer).ToList();

                    WriteFrameData(writer, layers, layerNodes, metadata, callback, ref current);
                    frameIndex++;
                }
            }
        }

        private void WriteFrameData(BinaryWriter writer,
            List<FrameLayerInfo> layers,
            XmlNodeList layerNodes,
            XmlDocument metadata,
            EntryCallback callback,
            ref int current)
        {
            var framesNode = metadata.SelectSingleNode("//Frames");
            int compression = int.Parse(framesNode.Attributes["CompType"]?.Value ?? "0");
            int blockWidth = int.Parse(framesNode.Attributes["BlockWidth"]?.Value ?? "0");
            int blockHeight = int.Parse(framesNode.Attributes["BlockHeight"]?.Value ?? "0");
        
            for (int l = 0; l < layers.Count && l < layerNodes.Count; l++)
            {
                if (callback != null)
                    callback(++current, layers[l].Entry, Localization._T("MsgAddingFile"));
        
                var layerNode = layerNodes[l];
                bool alphaOn = layerNode.Attributes["AlphaOn"]?.Value != "0";
        
                var layerInfo = layers[l];
        
                // Write main layer data
                if (layerInfo.Entry.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                {
                    var jpegData = File.ReadAllBytes(layerInfo.Entry.Name);
                    writer.Write(jpegData.Length);
                    writer.Write(jpegData);
                }
                else if (layerInfo.Entry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    using (var input = VFS.OpenBinaryStream(layerInfo.Entry))
                    {
                        var image = ImageFormat.Read(input);
                        if (image == null)
                            throw new InvalidFormatException($"Unable to load image {layerInfo.Entry.Name}");
        
                        var bitmap = image.Bitmap;
                        
                        // Check if image has alpha channel
                        bool hasAlpha = bitmap.Format == PixelFormats.Bgra32 || 
                                       bitmap.Format == PixelFormats.Pbgra32 ||
                                       bitmap.Format.BitsPerPixel == 32;
        
                        // Extract RGB data
                        var converted = bitmap;
                        if (converted.Format != PixelFormats.Bgr24)
                            converted = new FormatConvertedBitmap(converted, PixelFormats.Bgr24, null, 0);
        
                        int width = converted.PixelWidth;
                        int height = converted.PixelHeight;
                        int stride = (width * 3 + 3) & ~3;
                        var pixels = new byte[height * stride];
                        converted.CopyPixels(pixels, stride, 0);
        
                        // Write RGB layer
                        if (compression == 0)
                        {
                            WriteCompressedLayer(writer, pixels, width, height, stride, blockWidth, blockHeight);
                        }
                        else if (compression == 1)
                        {
                            WriteRawBlocks(writer, pixels, width, height, stride, blockWidth, blockHeight);
                        }
                        else
                        {
                            writer.Write(pixels.Length);
                            writer.Write(pixels);
                        }
        
                        // Handle alpha channel
                        if (alphaOn)
                        {
                            if (hasAlpha)
                            {
                                // Extract alpha channel from original image
                                byte[] alphaData = ExtractAlphaChannel(bitmap);
                                
                                // Write alpha data with same compression as main layer
                                if (compression == 0 || compression == 2) // zlib for alpha even when main is JPEG
                                {
                                    WriteCompressedAlpha(writer, alphaData, width, height, blockWidth, blockHeight);
                                }
                                else if (compression == 1)
                                {
                                    WriteRawAlpha(writer, alphaData, width, height, blockWidth, blockHeight);
                                }
                                else
                                {
                                    writer.Write(alphaData.Length);
                                    writer.Write(alphaData);
                                }
                            }
                            else
                            {
                                // No alpha in source image, write opaque alpha
                                byte[] opaqueAlpha = new byte[width * height];
                                for (int i = 0; i < opaqueAlpha.Length; i++)
                                    opaqueAlpha[i] = 0xFF;
                                
                                if (compression == 0 || compression == 2)
                                {
                                    WriteCompressedAlpha(writer, opaqueAlpha, width, height, blockWidth, blockHeight);
                                }
                                else
                                {
                                    writer.Write(opaqueAlpha.Length);
                                    writer.Write(opaqueAlpha);
                                }
                            }
                        }
                    }
                }
                else
                {
                    throw new InvalidFormatException($"Unknown file format: {layerInfo.Entry.Name}");
                }
            }
        }
        
        private byte[] ExtractAlphaChannel(BitmapSource bitmap)
        {
            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;
            int alphaStride = (width + 3) & ~3; // Alpha stride is aligned to 4 bytes
            byte[] alphaData = new byte[height * alphaStride];
        
            if (bitmap.Format == PixelFormats.Bgra32 || bitmap.Format == PixelFormats.Pbgra32)
            {
                int sourceStride = width * 4;
                byte[] sourceData = new byte[height * sourceStride];
                bitmap.CopyPixels(sourceData, sourceStride, 0);
        
                // Extract alpha channel (every 4th byte)
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        alphaData[y * alphaStride + x] = sourceData[y * sourceStride + x * 4 + 3];
                    }
                }
            }
            else if (bitmap.Format.BitsPerPixel == 32)
            {
                // Convert to Bgra32 first
                var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                int sourceStride = width * 4;
                byte[] sourceData = new byte[height * sourceStride];
                converted.CopyPixels(sourceData, sourceStride, 0);
        
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        alphaData[y * alphaStride + x] = sourceData[y * sourceStride + x * 4 + 3];
                    }
                }
            }
            else
            {
                // No alpha channel, return opaque
                for (int i = 0; i < alphaData.Length; i++)
                    alphaData[i] = 0xFF;
            }
        
            return alphaData;
        }
        
        private void WriteCompressedAlpha(BinaryWriter writer, byte[] alphaData, 
            int width, int height, int blockWidth, int blockHeight)
        {
            int alphaStride = (width + 3) & ~3;
            
            if (blockWidth > 0 && blockHeight > 0)
            {
                using (var uncompressed = new MemoryStream())
                using (var tempWriter = new BinaryWriter(uncompressed))
                {
                    int blocks_w = (width + blockWidth - 1) / blockWidth;
                    int blocks_h = (height + blockHeight - 1) / blockHeight;
                    int blocks_count = blocks_w * blocks_h;
        
                    // Write block references
                    for (int i = 0; i < blocks_count; i++)
                    {
                        tempWriter.Write(-1);
                        tempWriter.Write(0);
                    }
        
                    // Write alpha block data
                    for (int y = 0; y < height; y += blockHeight)
                    {
                        int h = Math.Min(blockHeight, height - y);
                        for (int x = 0; x < width; x += blockWidth)
                        {
                            int w = Math.Min(blockWidth, width - x);
        
                            for (int j = 0; j < h; j++)
                            {
                                int src = (y + j) * alphaStride + x;
                                tempWriter.Write(alphaData, src, w);
                            }
                        }
                    }
        
                    uncompressed.Position = 0;
                    using (var compressed = new MemoryStream())
                    {
                        using (var zlib = new ZLibStream(compressed, CompressionMode.Compress))
                        {
                            uncompressed.CopyTo(zlib);
                        }
                        var compressedData = compressed.ToArray();
                        writer.Write(compressedData.Length);
                        writer.Write(compressedData);
                    }
                }
            }
            else
            {
                using (var compressed = new MemoryStream())
                {
                    using (var zlib = new ZLibStream(compressed, CompressionMode.Compress))
                    {
                        zlib.Write(alphaData, 0, alphaData.Length);
                    }
                    var compressedData = compressed.ToArray();
                    writer.Write(compressedData.Length);
                    writer.Write(compressedData);
                }
            }
        }
        
        private void WriteRawAlpha(BinaryWriter writer, byte[] alphaData,
            int width, int height, int blockWidth, int blockHeight)
        {
            int alphaStride = (width + 3) & ~3;
            
            if (blockWidth > 0 && blockHeight > 0)
            {
                using (var ms = new MemoryStream())
                using (var tempWriter = new BinaryWriter(ms))
                {
                    int blocks_w = (width + blockWidth - 1) / blockWidth;
                    int blocks_h = (height + blockHeight - 1) / blockHeight;
                    int blocks_count = blocks_w * blocks_h;
        
                    // Write block references
                    for (int i = 0; i < blocks_count; i++)
                    {
                        tempWriter.Write(-1);
                        tempWriter.Write(0);
                    }
        
                    // Write alpha block data
                    for (int y = 0; y < height; y += blockHeight)
                    {
                        int h = Math.Min(blockHeight, height - y);
                        for (int x = 0; x < width; x += blockWidth)
                        {
                            int w = Math.Min(blockWidth, width - x);
        
                            for (int j = 0; j < h; j++)
                            {
                                int src = (y + j) * alphaStride + x;
                                tempWriter.Write(alphaData, src, w);
                            }
                        }
                    }
        
                    var data = ms.ToArray();
                    writer.Write(data.Length);
                    writer.Write(data);
                }
            }
            else
            {
                writer.Write(alphaData.Length);
                writer.Write(alphaData);
            }
        }

        private void WriteCompressedLayer(BinaryWriter writer, byte[] pixels,
            int width, int height, int stride, int blockWidth, int blockHeight)
        {
            if (blockWidth > 0 && blockHeight > 0)
            {
                using (var uncompressed = new MemoryStream())
                using (var tempWriter = new BinaryWriter(uncompressed))
                {
                    int blocks_w = (width + blockWidth - 1) / blockWidth;
                    int blocks_h = (height + blockHeight - 1) / blockHeight;
                    int blocks_count = blocks_w * blocks_h;

                    // Write block references
                    for (int i = 0; i < blocks_count; i++)
                    {
                        tempWriter.Write(-1);
                        tempWriter.Write(0);
                    }

                    // Write block data
                    for (int y = 0; y < height; y += blockHeight)
                    {
                        int h = Math.Min(blockHeight, height - y);
                        for (int x = 0; x < width; x += blockWidth)
                        {
                            int w = Math.Min(blockWidth, width - x);
                            int chunk_size = w * 3;

                            for (int j = 0; j < h; j++)
                            {
                                int src = (y + j) * stride + x * 3;
                                tempWriter.Write(pixels, src, chunk_size);
                            }
                        }
                    }

                    uncompressed.Position = 0;
                    using (var compressed = new MemoryStream())
                    {
                        using (var zlib = new ZLibStream(compressed, CompressionMode.Compress))
                        {
                            uncompressed.CopyTo(zlib);
                        }
                        var compressedData = compressed.ToArray();
                        writer.Write(compressedData.Length);
                        writer.Write(compressedData);
                    }
                }
            }
            else
            {
                using (var compressed = new MemoryStream())
                {
                    using (var zlib = new ZLibStream(compressed, CompressionMode.Compress))
                    {
                        zlib.Write(pixels, 0, pixels.Length);
                    }
                    var compressedData = compressed.ToArray();
                    writer.Write(compressedData.Length);
                    writer.Write(compressedData);
                }
            }
        }

        private void WriteRawBlocks(BinaryWriter writer, byte[] pixels,
            int width, int height, int stride, int blockWidth, int blockHeight)
        {
            if (blockWidth > 0 && blockHeight > 0)
            {
                using (var ms = new MemoryStream())
                using (var tempWriter = new BinaryWriter(ms))
                {
                    int blocks_w = (width + blockWidth - 1) / blockWidth;
                    int blocks_h = (height + blockHeight - 1) / blockHeight;
                    int blocks_count = blocks_w * blocks_h;

                    // Write block references
                    for (int i = 0; i < blocks_count; i++)
                    {
                        tempWriter.Write(-1);
                        tempWriter.Write(0);
                    }

                    // Write block data
                    for (int y = 0; y < height; y += blockHeight)
                    {
                        int h = Math.Min(blockHeight, height - y);
                        for (int x = 0; x < width; x += blockWidth)
                        {
                            int w = Math.Min(blockWidth, width - x);
                            int chunk_size = w * 3;

                            for (int j = 0; j < h; j++)
                            {
                                int src = (y + j) * stride + x * 3;
                                tempWriter.Write(pixels, src, chunk_size);
                            }
                        }
                    }

                    var data = ms.ToArray();
                    writer.Write(data.Length);
                    writer.Write(data);
                }
            }
            else
            {
                writer.Write(pixels.Length);
                writer.Write(pixels);
            }
        }
    }

    internal class GalXArchive : ArcFile
    {
        public GalXMetaData Metadata { get; set; }
        public XmlDocument XmlDoc { get; set; }

        public GalXArchive(ArcView arc, ArchiveFormat impl, ICollection<Entry> dir,
            GalXMetaData metadata, XmlDocument xmlDoc)
            : base(arc, impl, dir)
        {
            Metadata = metadata;
            XmlDoc = xmlDoc;
        }
    }

    internal class XmlMetadataEntry : Entry
    {
        public byte[] XmlContent { get; set; }
        public XmlDocument XmlDoc { get; set; }
    }

    internal class GalXJpegExtractor
    {
        private IBinaryStream m_input;
        private GalXMetaData m_info;
        private XmlDocument m_xml;

        public GalXJpegExtractor(IBinaryStream input, GalXMetaData info, XmlDocument xml)
        {
            m_input = input;
            m_info = info;
            m_xml = xml;
        }

        public Stream ExtractLayerAsJpeg(int frameIndex, int layerIndex)
        {
            m_input.Position = m_info.DataOffset;

            var frameNodes = m_xml.SelectNodes("//Frames/Frame");
            if (frameNodes == null || frameIndex >= frameNodes.Count)
                throw new InvalidOperationException("Frame not found");

            for (int f = 0; f <= frameIndex; f++)
            {
                var frameNode = frameNodes[f];
                var layersNode = frameNode.SelectSingleNode("Layers");
                var layerNodes = layersNode?.SelectNodes("Layer");

                if (layerNodes == null)
                    continue;

                for (int l = 0; l < layerNodes.Count; l++)
                {
                    var layerNode = layerNodes[l];
                    bool alphaOn = layerNode.Attributes["AlphaOn"]?.Value != "0";

                    int layer_size = m_input.ReadInt32();

                    if (f == frameIndex && l == layerIndex)
                    {
                        var jpegData = new byte[layer_size];
                        m_input.Read(jpegData, 0, layer_size);

                        var ms = new MemoryStream();
                        ms.Write(jpegData, 0, jpegData.Length);
                        ms.Position = 0;
                        return ms;
                    }

                    m_input.ReadBytes(layer_size);

                    if (alphaOn)
                    {
                        int alpha_size = m_input.ReadInt32();
                        if (alpha_size != 0)
                            m_input.ReadBytes(alpha_size);
                    }
                }
            }

            throw new InvalidOperationException("Layer not found");
        }
    }
}