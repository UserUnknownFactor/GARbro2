using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;
using ICSharpCode.SharpZipLib.BZip2;

namespace GameRes.Formats.GameMaker
{
    internal class WinEntry : PackedEntry
    {
        public string   ChunkType;
        public uint     ChunkOffset;
        public int      FrameIndex;
        public bool     IsAnimated;
        public uint     SpriteOffset;
    }

    internal class WinArchive : ArcFile
    {
        public readonly Dictionary<string, ChunkInfo> Chunks;
        public readonly List<TextureSheet> Textures;

        public WinArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir,
                          Dictionary<string, ChunkInfo> chunks, List<TextureSheet> textures)
            : base (arc, impl, dir)
        {
            Chunks = chunks;
            Textures = textures;
        }
    }

    internal class ChunkInfo
    {
        public string Name;
        public long   Offset;
        public uint   Size;
    }

    internal class TextureSheet
    {
        public long      Offset;
        public uint      Size;
    }

    internal class TexturePage
    {
        public int SourceX, SourceY, SourceWidth, SourceHeight;
        public int RenderX, RenderY;
        public int BoundingWidth, BoundingHeight;
        public int SheetId;
    }

    internal class WinImageMetaData : ImageMetaData
    {
        public bool IsAnimated;
        public int  FrameCount;
    }

    [Export(typeof(ArchiveFormat))]
    public class WinOpener : ArchiveFormat
    {
        public override string         Tag { get { return "WIN/GM"; } }
        public override string Description { get { return "GameMaker Studio data archive"; } }
        public override uint     Signature { get { return 0x4D524F46; } } // 'FORM'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public WinOpener ()
        {
            Extensions = new string[] { "win" };
            Signatures = new uint[] { 0x4D524F46 }; // 'FORM'
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "FORM"))
                return null;

            uint form_size = file.View.ReadUInt32 (4);
            if (form_size + 8 > file.MaxOffset)
                return null;

            // Read all chunks
            var chunks = new Dictionary<string, ChunkInfo>();
            long pos = 8;

            while (pos < file.MaxOffset)
            {
                if (pos + 8 > file.MaxOffset)
                    break;

                string chunk_name = file.View.ReadString (pos, 4, Encoding.ASCII);
                uint chunk_size = file.View.ReadUInt32 (pos + 4);

                chunks[chunk_name] = new ChunkInfo
                {
                    Name = chunk_name,
                    Offset = pos,
                    Size = chunk_size
                };

                pos += 8 + chunk_size;

                // Align to 4-byte boundary if needed
                if ((chunk_size & 3) != 0)
                    pos = (pos + 3) & ~3;
            }

            // We need at least GEN8 chunk to identify this as a valid WIN file
            if (!chunks.ContainsKey ("GEN8"))
                return null;

            var dir = new List<Entry>();
            var textures = new List<TextureSheet>();

            // Load texture sheets first (TXTR chunk)
            if (chunks.ContainsKey ("TXTR"))
            {
                LoadTextureSheets (file, chunks["TXTR"], textures, dir);
            }

            // Load sprites (SPRT chunk)
            if (chunks.ContainsKey ("SPRT"))
            {
                LoadSprites (file, chunks["SPRT"], textures, dir);
            }

            // Load backgrounds (BGND chunk)
            if (chunks.ContainsKey ("BGND"))
            {
                LoadBackgrounds (file, chunks["BGND"], textures, dir);
            }

            // Add raw chunks for debugging/extraction
            foreach (var chunk in chunks)
            {
                if (chunk.Key != "FORM") // Skip container chunk
                {
                    var entry = new Entry
                    {
                        Name = string.Format (@"chunks\{0}", chunk.Key),
                        Type = "data",
                        Offset = chunk.Value.Offset + 8, // Skip chunk header
                        Size = chunk.Value.Size
                    };
                    dir.Add (entry);
                }
            }

            if (dir.Count == 0)
                return null;

            return new WinArchive (file, this, dir, chunks, textures);
        }
        
        void LoadTextureSheets (ArcView file, ChunkInfo chunk, List<TextureSheet> textures, List<Entry> dir)
        {
            long base_offset = chunk.Offset + 8;
            uint count = file.View.ReadUInt32 (base_offset);
        
            for (uint i = 0; i < count; i++)
            {
                uint address = file.View.ReadUInt32 (base_offset + 4 + i * 4);
                if (address == 0)
                    continue;
        
                // Check if this is the PNG format
                uint first_value = file.View.ReadUInt32 (address);
                if (first_value == 0) // GMS_Explorer format has 0 here
                {
                    uint png_offset = file.View.ReadUInt32 (address + 4);
                    if (png_offset > 0 && png_offset + 8 < file.MaxOffset)
                    {
                        // Check for PNG signature
                        if (file.View.ReadUInt64 (png_offset) == 0x0A1A0A0D474E5089)
                        {
                            long png_size = GetPngSize (file, png_offset);
                            
                            var entry = new WinEntry
                            {
                                Name = string.Format (@"textures\sheet_{0:D4}.png", i),
                                Type = "image",
                                Offset = png_offset,
                                Size = (uint)png_size,
                                ChunkType = "TXTR_PNG",
                                IsPacked = false
                            };
                            dir.Add (entry);
        
                            textures.Add (new TextureSheet
                            {
                                Offset = png_offset,
                                Size = (uint)png_size
                            });
                            continue;
                        }
                    }
                }
        
                // Otherwise try the BZip2 format (your original format)
                uint data_offset = file.View.ReadUInt32 (address + 0x18);
                
                if (data_offset > 0 && data_offset + 16 < file.MaxOffset)
                {
                    // Search for BZip2 signature
                    bool found_bzip = false;
                    uint bzip_offset = 0;
                    
                    for (uint search = 0; search < 32 && data_offset + search + 3 < file.MaxOffset; search++)
                    {
                        if (file.View.ReadByte(data_offset + search) == 0x42 &&     // 'B'
                            file.View.ReadByte(data_offset + search + 1) == 0x5A && // 'Z'
                            file.View.ReadByte(data_offset + search + 2) == 0x68)   // 'h'
                        {
                            found_bzip = true;
                            bzip_offset = data_offset + search;
                            break;
                        }
                    }
                    
                    if (found_bzip)
                    {
                        // Calculate size
                        uint compressed_size;
                        if (i + 1 < count)
                        {
                            uint next_address = file.View.ReadUInt32 (base_offset + 4 + (i + 1) * 4);
                            uint next_data_offset = file.View.ReadUInt32 (next_address + 0x18);
                            compressed_size = next_data_offset - bzip_offset;
                        }
                        else
                        {
                            compressed_size = (uint)(chunk.Offset + chunk.Size - bzip_offset);
                        }
                        
                        var entry = new WinEntry
                        {
                            Name = string.Format (@"textures\sheet_{0:D4}.qoi", i),
                            Type = "image",
                            Offset = bzip_offset,
                            Size = compressed_size,
                            ChunkType = "TXTR_BZ2",
                            IsPacked = true
                        };
                        dir.Add (entry);
        
                        textures.Add (new TextureSheet
                        {
                            Offset = bzip_offset,
                            Size = compressed_size
                        });
                    }
                }
            }
        }

        byte[] ExtractFromTexturePage (ArcView file, TextureSheet sheet, TexturePage tp)
        {
            try
            {
                // Read the texture sheet data
                var texture_data = file.View.ReadBytes (sheet.Offset, sheet.Size);
                if (texture_data == null || texture_data.Length == 0)
                    return null;
                
                BitmapSource bitmap = null;
                
                // Check if it's PNG format
                if (texture_data.Length > 8 && 
                    texture_data[0] == 0x89 && texture_data[1] == 0x50 && 
                    texture_data[2] == 0x4E && texture_data[3] == 0x47)
                {
                    using (var png_stream = new MemoryStream (texture_data))
                    {
                        var decoder = new PngBitmapDecoder (png_stream, 
                            BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                        bitmap = decoder.Frames[0];
                        bitmap.Freeze();
                    }
                }
                // Check if it's BZip2 format
                else if (texture_data.Length > 3 &&
                         texture_data[0] == 0x42 && texture_data[1] == 0x5A && texture_data[2] == 0x68)
                {
                    byte[] decompressed_data;
                    
                    using (var compressed_stream = new MemoryStream (texture_data))
                    using (var bzip_stream = new BZip2InputStream (compressed_stream))
                    using (var decompressed = new MemoryStream())
                    {
                        bzip_stream.CopyTo (decompressed);
                        decompressed_data = decompressed.ToArray();
                    }
                    
                    // Check decompressed format
                    if (decompressed_data.Length > 8 && decompressed_data[0] == 0x89 && decompressed_data[1] == 0x50)
                    {
                        // PNG
                        using (var png_stream = new MemoryStream (decompressed_data))
                        {
                            var decoder = new PngBitmapDecoder (png_stream, 
                                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                            bitmap = decoder.Frames[0];
                            bitmap.Freeze();
                        }
                    }
                    else if (decompressed_data.Length > 4)
                    {
                        // Check for GameMaker QOI variants
                        uint magic = BitConverter.ToUInt32(decompressed_data, 0);
                        if (magic == 0x71696F66 || magic == 0x716F7A32) // "fioq" or "qoz2"
                        {
                            using (var qoi_stream = new BinMemoryStream (decompressed_data, "texture.qoi"))
                            using (var decoder = new QoiImageDecoder (qoi_stream))
                            {
                                var image_data = decoder.Image;
                                if (image_data == null)
                                    return null;
                                bitmap = image_data.Bitmap;
                            }
                        }
                        else
                        {
                            Trace.WriteLine($"Unknown decompressed format. Magic: {magic:X8}");
                            return null;
                        }
                    }
                }
                // Check if it's QOI directly
                else if (texture_data.Length > 4)
                {
                    uint magic = BitConverter.ToUInt32(texture_data, 0);
                    if (magic == 0x71696F66 || magic == 0x716F7A32) // "fioq" or "qoz2"
                    {
                        using (var qoi_stream = new BinMemoryStream (texture_data, "texture.qoi"))
                        using (var decoder = new QoiImageDecoder (qoi_stream))
                        {
                            var image_data = decoder.Image;
                            if (image_data == null)
                                return null;
                            bitmap = image_data.Bitmap;
                        }
                    }
                    else
                    {
                        Trace.WriteLine($"Unknown texture format. Magic: {magic:X8}");
                        return null;
                    }
                }
                else
                {
                    //Trace.WriteLine("Texture data too small");
                    return null;
                }
                
                if (bitmap == null)
                {
                    Trace.WriteLine("Failed to decode texture");
                    return null;
                }
                
                //Trace.WriteLine($"Texture decoded: {bitmap.PixelWidth}x{bitmap.PixelHeight}");
                //Trace.WriteLine($"Extracting region: {tp.SourceX},{tp.SourceY} {tp.SourceWidth}x{tp.SourceHeight}");
                //Trace.WriteLine($"Render to: {tp.RenderX},{tp.RenderY} in {tp.BoundingWidth}x{tp.BoundingHeight}");

                if (tp.SourceX + tp.SourceWidth > bitmap.PixelWidth ||
                    tp.SourceY + tp.SourceHeight > bitmap.PixelHeight)
                {
                    Trace.WriteLine($"Extraction region out of bounds");
                    return null;
                }
                
                var crop = new CroppedBitmap (bitmap, 
                    new System.Windows.Int32Rect (tp.SourceX, tp.SourceY, 
                        tp.SourceWidth, tp.SourceHeight));
        
                var final = new RenderTargetBitmap (
                    tp.BoundingWidth, tp.BoundingHeight, 96, 96, PixelFormats.Pbgra32);
        
                var visual = new DrawingVisual();
                using (var context = visual.RenderOpen())
                {
                    context.DrawImage (crop, 
                        new System.Windows.Rect (tp.RenderX, tp.RenderY, 
                            tp.SourceWidth, tp.SourceHeight));
                }
                final.Render (visual);
        
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add (BitmapFrame.Create (final));
                
                using (var output = new MemoryStream())
                {
                    encoder.Save (output);
                    return output.ToArray();
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"ExtractFromTexturePage failed: {ex.Message}");
                Trace.WriteLine($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        void LoadSprites (ArcView file, ChunkInfo chunk, List<TextureSheet> textures, List<Entry> dir)
        {
            long base_offset = chunk.Offset + 8;
            uint count = file.View.ReadUInt32 (base_offset);

            for (uint i = 0; i < count; i++)
            {
                uint address = file.View.ReadUInt32 (base_offset + 4 + i * 4);
                if (address == 0)
                    continue;

                uint name_offset = file.View.ReadUInt32 (address);
                string name = ReadString (file, name_offset);

                if (string.IsNullOrEmpty (name))
                    name = string.Format ("sprite_{0:D4}", i);

                // Read sprite data
                uint width = file.View.ReadUInt32 (address + 4);
                uint height = file.View.ReadUInt32 (address + 8);
                
                uint texture_count = file.View.ReadUInt32 (address + 68);
                
                if (texture_count > 1)
                {
                    // Multi-frame sprite
                    var entry = new WinEntry
                    {
                        Name = string.Format (@"sprites\{0}.png", name),
                        Type = "image",
                        Offset = address,
                        Size = 72 + texture_count * 4,
                        ChunkType = "SPRT",
                        ChunkOffset = address,
                        IsAnimated = true,
                        IsPacked = true,
                        SpriteOffset = address
                    };
                    dir.Add (entry);
                }
                else if (texture_count == 1)
                {
                    // Single frame sprite - read the texture page address
                    uint tp_address = file.View.ReadUInt32 (address + 72);
                    
                    var entry = new WinEntry
                    {
                        Name = string.Format (@"sprites\{0}.png", name),
                        Type = "image",
                        Offset = tp_address,
                        Size = 22,
                        ChunkType = "SPRT",
                        ChunkOffset = address,
                        FrameIndex = 0,
                        IsAnimated = false,
                        IsPacked = true,
                        SpriteOffset = address
                    };
                    dir.Add (entry);
                }
            }
        }

        void LoadBackgrounds (ArcView file, ChunkInfo chunk, List<TextureSheet> textures, List<Entry> dir)
        {
            long base_offset = chunk.Offset + 8;
            uint count = file.View.ReadUInt32 (base_offset);

            for (uint i = 0; i < count; i++)
            {
                uint address = file.View.ReadUInt32 (base_offset + 4 + i * 4);
                if (address == 0)
                    continue;

                uint name_offset = file.View.ReadUInt32 (address);
                string name = ReadString (file, name_offset);

                if (string.IsNullOrEmpty (name))
                    name = string.Format ("background_{0:D4}", i);

                // Skip 3 unknown uint32s
                uint tp_address = file.View.ReadUInt32 (address + 16);

                var entry = new WinEntry
                {
                    Name = string.Format (@"backgrounds\{0}.png", name),
                    Type = "image",
                    Offset = tp_address,
                    Size = 22,
                    ChunkType = "BGND",
                    ChunkOffset = address,
                    IsPacked = true
                };
                dir.Add (entry);
            }
        }

        string ReadString (ArcView file, uint offset)
        {
            if (offset < 4 || offset >= file.MaxOffset)
                return "";

            uint str_offset = offset - 4;
            uint length = file.View.ReadUInt32 (str_offset);
            
            if (length == 0 || length > 1024 || str_offset + 4 + length > file.MaxOffset)
                return "";

            return file.View.ReadString (str_offset + 4, length, Encoding.UTF8);
        }

        long GetPngSize (ArcView file, long offset)
        {
            long pos = offset + 8; // Skip PNG header
            
            while (pos + 8 < file.MaxOffset)
            {
                uint chunk_size = Binary.BigEndian (file.View.ReadUInt32 (pos));
                string chunk_type = file.View.ReadString (pos + 4, 4, Encoding.ASCII);
                
                pos += 12 + chunk_size; // 4 (size) + 4 (type) + 4 (CRC) + data
                
                if (chunk_type == "IEND")
                    return pos - offset;
            }
            
            return file.MaxOffset - offset;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var win_arc = arc as WinArchive;
            var win_entry = entry as WinEntry;
            
            if (null == win_arc || null == win_entry)
                return base.OpenEntry (arc, entry);
        
            if (!win_entry.IsPacked)
                return base.OpenEntry (arc, entry);
        
            // Handle PNG textures (no decompression needed)
            if (win_entry.ChunkType == "TXTR_PNG")
                return base.OpenEntry (arc, entry);
        
            // Handle BZip2 compressed textures
            if (win_entry.ChunkType == "TXTR_BZ2")
            {
                var compressed_data = arc.File.View.ReadBytes (win_entry.Offset, win_entry.Size);
                
                try
                {
                    using (var compressed_stream = new MemoryStream (compressed_data))
                    using (var bzip_stream = new BZip2InputStream (compressed_stream))
                    using (var decompressed = new MemoryStream())
                    {
                        bzip_stream.CopyTo (decompressed);
                        return new BinMemoryStream (decompressed.ToArray(), entry.Name);
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidFormatException ($"BZip2 decompression failed: {ex.Message}");
                }
            }
        
            // Handle single frame extraction
            if ((win_entry.ChunkType == "SPRT" || win_entry.ChunkType == "BGND") && !win_entry.IsAnimated)
            {
                var tp = ReadTexturePage (arc.File, win_entry.Offset);
                if (tp == null || tp.SheetId < 0 || tp.SheetId >= win_arc.Textures.Count)
                    return Stream.Null;
                    
                var image = ExtractFromTexturePage (arc.File, win_arc.Textures[tp.SheetId], tp);
                if (image == null)
                    return Stream.Null;
                    
                return new BinMemoryStream (image, entry.Name);
            }
        
            // Handle animated sprites
            if (win_entry.IsAnimated && win_entry.ChunkType == "SPRT")
            {
                var frames = LoadSpriteFrames (arc.File, win_arc, win_entry);
                if (frames != null && frames.Count > 0)
                    return new BinMemoryStream (frames[0], entry.Name);
                return Stream.Null;
            }
        
            return Stream.Null;
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var win_arc = arc as WinArchive;
            var win_entry = entry as WinEntry;
            
            if (null == win_arc || null == win_entry)
                return base.OpenImage (arc, entry);
        
            // For non-packed entries, use default handling
            if (!win_entry.IsPacked)
                return base.OpenImage (arc, entry);
        
            // Handle texture sheets directly
            if (win_entry.ChunkType == "TXTR_PNG")
            {
                // Direct PNG texture
                var stream = arc.File.CreateStream (win_entry.Offset, win_entry.Size);
                return ImageFormatDecoder.Create (stream);
            }
            
            if (win_entry.ChunkType == "TXTR_BZ2")
            {
                // Compressed texture - decompress first
                var compressed_data = arc.File.View.ReadBytes (win_entry.Offset, win_entry.Size);
                
                try
                {
                    using (var compressed_stream = new MemoryStream (compressed_data))
                    using (var bzip_stream = new BZip2InputStream (compressed_stream))
                    using (var decompressed = new MemoryStream())
                    {
                        bzip_stream.CopyTo (decompressed);
                        var decompressed_data = decompressed.ToArray();
                        
                        // Check if it's PNG or QOI
                        if (decompressed_data.Length > 8 && decompressed_data[0] == 0x89 && decompressed_data[1] == 0x50)
                        {
                            // PNG format
                            var png_stream = new BinMemoryStream (decompressed_data, entry.Name);
                            return ImageFormatDecoder.Create (png_stream);
                        }
                        else if (decompressed_data.Length > 4 && 
                                 decompressed_data[0] == 0x71 && decompressed_data[1] == 0x6F &&
                                 decompressed_data[2] == 0x69 && decompressed_data[3] == 0x66)
                        {
                            // QOI format
                            var qoi_stream = new BinMemoryStream (decompressed_data, entry.Name);
                            return new QoiImageDecoder (qoi_stream);
                        }
                        else
                        {
                            // Try as raw image data
                            var stream = new BinMemoryStream (decompressed_data, entry.Name);
                            return ImageFormatDecoder.Create (stream);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Failed to decompress texture: {ex.Message}");
                    return null;
                }
            }
        
            // Handle sprites and backgrounds
            if ((win_entry.ChunkType == "SPRT" || win_entry.ChunkType == "BGND") && !win_entry.IsAnimated)
            {
                try
                {
                    var tp = ReadTexturePage (arc.File, win_entry.Offset);
                    if (tp == null || tp.SheetId < 0 || tp.SheetId >= win_arc.Textures.Count)
                    {
                        Trace.WriteLine($"Invalid texture page: SheetId={tp?.SheetId}");
                        return null;
                    }
                    
                    var image_data = ExtractFromTexturePage (arc.File, win_arc.Textures[tp.SheetId], tp);
                    if (image_data == null || image_data.Length == 0)
                    {
                        Trace.WriteLine("Failed to extract texture page");
                        return null;
                    }
                    
                    // The extracted data should be PNG
                    var stream = new BinMemoryStream (image_data, entry.Name);
                    return ImageFormatDecoder.Create (stream);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Failed to extract sprite/background: {ex.Message}");
                    return null;
                }
            }
        
            // Handle animated sprites
            if (win_entry.IsAnimated && win_entry.ChunkType == "SPRT")
            {
                var frames = LoadSpriteFrames (arc.File, win_arc, win_entry);
                if (frames != null && frames.Count > 0)
                {
                    var info = new WinImageMetaData
                    {
                        Width = 0,
                        Height = 0,
                        BPP = 32,
                        IsAnimated = true,
                        FrameCount = frames.Count
                    };
        
                    // Get dimensions from first frame
                    using (var stream = new BinMemoryStream (frames[0], entry.Name))
                    {
                        try
                        {
                            var decoder = new PngBitmapDecoder (stream, 
                                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                            if (decoder.Frames.Count > 0)
                            {
                                info.Width = (uint)decoder.Frames[0].PixelWidth;
                                info.Height = (uint)decoder.Frames[0].PixelHeight;
                            }
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"Failed to decode frame: {ex.Message}");
                        }
                    }
        
                    var win_stream = new WinImageStream (frames, info);
                    return new WinImageDecoder (win_stream, info, frames);
                }
            }
        
            return base.OpenImage (arc, entry);
        }

        List<byte[]> LoadSpriteFrames (ArcView file, WinArchive arc, WinEntry entry)
        {
            var frames = new List<byte[]>();
            
            // Read frame count
            uint frame_count = file.View.ReadUInt32 (entry.SpriteOffset + 68);
            
            for (uint i = 0; i < frame_count; i++)
            {
                uint tp_offset = file.View.ReadUInt32 (entry.SpriteOffset + 72 + i * 4);
                var tp = ReadTexturePage (file, tp_offset);
                
                if (tp != null && tp.SheetId >= 0 && tp.SheetId < arc.Textures.Count)
                {
                    var frame_data = ExtractFromTexturePage (file, arc.Textures[tp.SheetId], tp);
                    if (frame_data != null)
                        frames.Add (frame_data);
                }
            }
            
            return frames;
        }

        TexturePage ReadTexturePage (ArcView file, long offset)
        {
            if (offset + 22 > file.MaxOffset)
                return null;
                
            var tp = new TexturePage();
            tp.SourceX = file.View.ReadUInt16 (offset);
            tp.SourceY = file.View.ReadUInt16 (offset + 2);
            tp.SourceWidth = file.View.ReadUInt16 (offset + 4);
            tp.SourceHeight = file.View.ReadUInt16 (offset + 6);
            tp.RenderX = file.View.ReadUInt16 (offset + 8);
            tp.RenderY = file.View.ReadUInt16 (offset + 10);
            
            // Skip bounding box
            tp.BoundingWidth = file.View.ReadUInt16 (offset + 16);
            tp.BoundingHeight = file.View.ReadUInt16 (offset + 18);
            tp.SheetId = file.View.ReadUInt16 (offset + 20);
            
            return tp;
        }

    }

    internal class WinImageStream : Stream
    {
        internal readonly List<byte[]> m_frames;
        private readonly MemoryStream m_current;
        
        public WinImageStream (List<byte[]> frames, ImageMetaData info)
        {
            m_frames = frames;
            m_current = new MemoryStream (frames[0]);
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => m_current.Length;
        public override long Position 
        { 
            get => m_current.Position; 
            set => m_current.Position = value; 
        }

        public override void Flush() => m_current.Flush();
        public override int Read (byte[] buffer, int offset, int count) => m_current.Read (buffer, offset, count);
        public override long Seek (long offset, SeekOrigin origin) => m_current.Seek (offset, origin);
        public override void SetLength (long value) => throw new NotSupportedException();
        public override void Write (byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose (bool disposing)
        {
            if (disposing)
                m_current?.Dispose();
            base.Dispose (disposing);
        }
    }

    internal class WinImageDecoder : IImageDecoder
    {
        private Stream m_input;
        private WinImageMetaData m_info;
        private ImageData m_image;
        private List<byte[]> m_frames;

        public Stream Source => m_input;
        public ImageFormat SourceFormat => null;
        public ImageMetaData Info => m_info;
        public ImageData Image 
        { 
            get 
            {
                if (m_image == null)
                    m_image = CreateImageData();
                return m_image;
            }
        }

        public WinImageDecoder (WinImageStream input, WinImageMetaData info, List<byte[]> frames)
        {
            m_input = input;
            m_info = info;
            m_frames = frames;
        }

        private ImageData CreateImageData()
        {
            if (m_frames == null || m_frames.Count == 0)
                return null;

            var bitmaps = new List<BitmapSource>();
            var delays = new List<int>();

            foreach (var frame_data in m_frames)
            {
                using (var stream = new BinMemoryStream (frame_data))
                {
                    var decoder = new PngBitmapDecoder (stream, 
                        BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count > 0)
                    {
                        var frame = decoder.Frames[0];
                        frame.Freeze();
                        bitmaps.Add (frame);
                        delays.Add (100); // default 100ms delay
                    }
                }
            }

            if (bitmaps.Count > 1)
            {
                return new AnimatedImageData (bitmaps, delays, m_info);
            }
            else if (bitmaps.Count == 1)
            {
                return new ImageData (bitmaps[0], m_info);
            }

            return null;
        }

        public void Dispose()
        {
            m_input?.Dispose();
        }
    }
}