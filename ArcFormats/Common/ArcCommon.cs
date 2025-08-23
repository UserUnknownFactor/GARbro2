using GameRes.Utility;
using System;
using System.IO;
using System.Linq;
using System.ComponentModel.Composition;

namespace GameRes.Formats
{
    /// <summary>
    /// An entry that automatically detects its file type based on content signature.
    /// </summary>
    /// <remarks>
    /// This class uses lazy evaluation to determine the actual file type and extension
    /// only when needed, based on the file's signature bytes.
    /// </remarks>
    public class AutoEntry : Entry
    {
        private Lazy<IResource> m_res;
        private Lazy<string> m_name;
        private Lazy<string> m_type;

        /// <inheritdoc/>
        public override string Name
        {
            get { return m_name.Value; }
            set { m_name = new Lazy<string> (() => value); }
        }

        /// <inheritdoc/>
        public override string Type
        {
            get { return m_type.Value; }
            set { m_type = new Lazy<string> (() => value); }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AutoEntry"/> class.
        /// </summary>
        /// <param name="name">The base name of the entry.</param>
        /// <param name="type_checker">A function that determines the resource type.</param>
        public AutoEntry (string name, Func<IResource> type_checker)
        {
            m_res  = new Lazy<IResource> (type_checker);
            m_name = new Lazy<string> (() => GetName (name));
            m_type = new Lazy<string> (GetEntryType);
        }

        /// <summary>
        /// Creates an AutoEntry by reading the file signature at the specified offset.
        /// </summary>
        /// <param name="file">The archive view containing the file.</param>
        /// <param name="offset">The offset where the file starts.</param>
        /// <param name="base_name">The base name for the entry.</param>
        /// <returns>A new AutoEntry instance.</returns>
        public static AutoEntry Create (ArcView file, long offset, string base_name)
        {
            return new AutoEntry (base_name, () => DetectFileType (file.View.ReadUInt32 (offset))) { Offset = offset };
        }

        /// <summary>
        /// Detects the file type based on a 4-byte signature.
        /// </summary>
        /// <param name="signature">The 4-byte signature read from the file.</param>
        /// <returns>The detected resource type, or null if the type cannot be determined.</returns>
        public static IResource DetectFileType (uint signature)
        {
            return FormatCatalog.DetectFileType (signature);
        }

        /// <summary>
        /// Gets the entry name with the appropriate extension based on the detected file type.
        /// </summary>
        /// <param name="name">The base name.</param>
        /// <returns>The name with the correct extension.</returns>
        private string GetName (string name)
        {
            if (null == m_res.Value)
                return name;
            var ext = m_res.Value.Extensions.FirstOrDefault();
            if (string.IsNullOrEmpty (ext))
                return name;
            return Path.ChangeExtension (name, ext);
        }

        /// <summary>
        /// Gets the entry type string based on the detected resource.
        /// </summary>
        /// <returns>The resource type string, or empty string if type is unknown.</returns>
        private string GetEntryType ()
        {
            return null == m_res.Value ? "" : m_res.Value.Type;
        }
    }

    /// <summary>
    /// Extension methods for string manipulation, particularly for file path operations.
    /// </summary>
    public static class StringExtensions
    {
        /// <summary>
        /// Checks if the filename has the specified extension.
        /// </summary>
        /// <param name="filename">The filename to check.</param>
        /// <param name="ext">The extension to look for (with or without leading dot).</param>
        /// <returns>true if the filename has the specified extension; otherwise, false.</returns>
        public static bool HasExtension (this string filename, string ext)
        {
            bool ext_is_empty = string.IsNullOrEmpty (ext);
            if (!ext_is_empty && '.' == ext[0])
                return filename.EndsWith (ext, StringComparison.OrdinalIgnoreCase);
            int ext_start = GetExtensionIndex (filename);
            // filename extension length
            int l_ext_length = filename.Length - ext_start;
            if (ext_is_empty)
                return 0 == l_ext_length;
            return (l_ext_length == ext.Length
                    && filename.EndsWith (ext, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Checks if the filename ends with any of the extensions from the provided list.
        /// </summary>
        /// <param name="filename">The filename to check.</param>
        /// <param name="ext_list">Array of extensions to check against.</param>
        /// <returns>true if the filename has any of the specified extensions; otherwise, false.</returns>
        public static bool HasAnyOfExtensions (this string filename, params string[] ext_list)
        {
            int ext_start = GetExtensionIndex (filename);
            int l_ext_length = filename.Length - ext_start;
            foreach (string ext in ext_list)
            {
                if (string.IsNullOrEmpty (ext) || "." == ext)
                {
                    if (0 == l_ext_length)
                        return true;
                }
                else if ('.' == ext[0] || l_ext_length == ext.Length)
                {
                    if (filename.EndsWith (ext, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Gets the index where the file extension starts in the filename.
        /// </summary>
        /// <param name="filename">The filename to analyze.</param>
        /// <returns>The index of the character after the last dot in the filename, or the length of the string if no extension.</returns>
        internal static int GetExtensionIndex (string filename)
        {
            int name_start = filename.LastIndexOfAny (VFS.PathSeparatorChars);
            if (-1 == name_start)
                name_start = 0;
            else
                name_start++;
            if (filename.Length == name_start) // path ends with '\'
                return name_start;

            int ext_start = filename.LastIndexOf ('.', filename.Length-1, filename.Length - name_start);
            if (-1 == ext_start)
                return filename.Length;
            else
                return ext_start + 1;
        }

        /// <summary>
        /// Returns a copy of this string with ASCII characters converted to lowercase.
        /// </summary>
        /// <param name="str">The string to convert.</param>
        /// <returns>A new string with ASCII uppercase letters converted to lowercase.</returns>
        public static string ToLowerAscii (this string str)
        {
            int i;
            for (i = 0; i < str.Length; ++i)
            {
                if (str[i] >= 'A' && str[i] <= 'Z')
                    break;
            }
            if (i == str.Length)
                return str;
            var builder = new System.Text.StringBuilder (str, 0, i, str.Length);
            builder.Append ((char)(str[i++] | 0x20));
            while (i < str.Length)
            {
                char c = str[i];
                if (c >= 'A' && c <= 'Z')
                    c = (char)(c | 0x20);
                builder.Append (c);
                ++i;
            }
            return builder.ToString();
        }

        /// <summary>
        /// Converts a string to Shift-JIS encoding with ASCII characters converted to lowercase.
        /// </summary>
        /// <param name="text">The text to convert.</param>
        /// <returns>Byte array in Shift-JIS encoding with ASCII letters in lowercase.</returns>
        public static byte[] ToLowerShiftJis (this string text)
        {
            var text_bytes = Encodings.cp932.GetBytes (text);
            text_bytes.ToLowerShiftJis();
            return text_bytes;
        }

        /// <summary>
        /// Converts ASCII uppercase letters to lowercase in a Shift-JIS encoded byte array.
        /// </summary>
        /// <param name="text_bytes">The Shift-JIS encoded byte array to modify in place.</param>
        public static void ToLowerShiftJis (this byte[] text_bytes)
        {
            for (int i = 0; i < text_bytes.Length; ++i)
            {
                byte c = text_bytes[i];
                if (c >= 'A' && c <= 'Z')
                    text_bytes[i] += 0x20;
                else if (c > 0x7F && c < 0xA1 || c > 0xDF)
                    ++i;
            }
        }
    }

    /// <summary>
    /// Creates streams in TGA (Truevision Graphics Adapter) format from raw image pixels.
    /// </summary>
    [Obsolete("Use a more modern image format or dedicated TGA library")]
    public static class TgaStream
    {
        /// <summary>
        /// Creates a TGA format stream from the given image pixels.
        /// </summary>
        /// <param name="info">Image metadata including dimensions and color depth.</param>
        /// <param name="pixels">Raw pixel data.</param>
        /// <param name="flipped">If true, the image is stored bottom-to-top; otherwise top-to-bottom.</param>
        /// <returns>A stream containing the TGA formatted image.</returns>
        public static Stream Create (ImageMetaData info, byte[] pixels, bool flipped = false)
        {
            var header = new byte[0x12];
            header[2] = (byte)(info.BPP > 8 ? 2 : 3);
            LittleEndian.Pack ((short)info.OffsetX, header, 8);
            LittleEndian.Pack ((short)info.OffsetY, header, 0xa);
            LittleEndian.Pack ((ushort)info.Width,  header, 0xc);
            LittleEndian.Pack ((ushort)info.Height, header, 0xe);
            header[0x10] = (byte)info.BPP;
            if (!flipped)
                header[0x11] = 0x20;
            return new PrefixStream (header, new MemoryStream (pixels));
        }

        /// <summary>
        /// Creates a TGA format stream from the given image pixels with stride adjustment.
        /// </summary>
        /// <param name="info">Image metadata including dimensions and color depth.</param>
        /// <param name="stride">The number of bytes per row in the source pixel data.</param>
        /// <param name="pixels">Raw pixel data.</param>
        /// <param name="flipped">If true, the image is stored bottom-to-top; otherwise top-to-bottom.</param>
        /// <returns>A stream containing the TGA formatted image.</returns>
        public static Stream Create (ImageMetaData info, int stride, byte[] pixels, bool flipped = false)
        {
            int tga_stride = (int)info.Width * info.BPP / 8;
            if (stride != tga_stride)
            {
                var adjusted = new byte[tga_stride * (int)info.Height];
                int src = 0;
                int dst = 0;
                for (uint y = 0; y < info.Height; ++y)
                {
                    Buffer.BlockCopy (pixels, src, adjusted, dst, tga_stride);
                    src += stride;
                    dst += tga_stride;
                }
                pixels = adjusted;
            }
            return Create (info, pixels, flipped);
        }
    }

    /// <summary>
    /// Provides software implementations of MMX (MultiMedia eXtensions) SIMD operations.
    /// </summary>
    /// <remarks>
    /// These methods emulate MMX instructions for parallel arithmetic operations on packed data.
    /// They operate on multiple data elements simultaneously within a single register-sized value.
    /// </remarks>
    public static class MMX
    {
        /// <summary>
        /// Performs parallel addition of packed bytes (8-bit values).
        /// </summary>
        /// <param name="x">First operand containing packed bytes.</param>
        /// <param name="y">Second operand containing packed bytes.</param>
        /// <returns>Result of parallel byte addition.</returns>
        public static ulong PAddB (ulong x, ulong y)
        {
            ulong r = 0;
            for (ulong mask = 0xFF; mask != 0; mask <<= 8)
            {
                r |= ((x & mask) + (y & mask)) & mask;
            }
            return r;
        }

        /// <summary>
        /// Performs parallel addition of packed bytes (8-bit values) on 32-bit operands.
        /// </summary>
        /// <param name="x">First operand containing packed bytes.</param>
        /// <param name="y">Second operand containing packed bytes.</param>
        /// <returns>Result of parallel byte addition.</returns>
        public static uint PAddB (uint x, uint y)
        {
            uint r13 = (x & 0xFF00FF00u) + (y & 0xFF00FF00u);
            uint r02 = (x & 0x00FF00FFu) + (y & 0x00FF00FFu);
            return (r13 & 0xFF00FF00u) | (r02 & 0x00FF00FFu);
        }

        /// <summary>
        /// Performs parallel addition of packed words (16-bit values).
        /// </summary>
        /// <param name="x">First operand containing packed words.</param>
        /// <param name="y">Second operand containing packed words.</param>
        /// <returns>Result of parallel word addition.</returns>
        public static ulong PAddW (ulong x, ulong y)
        {
            ulong mask = 0xffff;
            ulong r = ((x & mask) + (y & mask)) & mask;
            mask <<= 16;
            r |= ((x & mask) + (y & mask)) & mask;
            mask <<= 16;
            r |= ((x & mask) + (y & mask)) & mask;
            mask <<= 16;
            r |= ((x & mask) + (y & mask)) & mask;
            return r;
        }

        /// <summary>
        /// Performs parallel addition of packed doublewords (32-bit values).
        /// </summary>
        /// <param name="x">First operand containing packed doublewords.</param>
        /// <param name="y">Second operand containing packed doublewords.</param>
        /// <returns>Result of parallel doubleword addition.</returns>
        public static ulong PAddD (ulong x, ulong y)
        {
            ulong mask = 0xffffffff;
            ulong r = ((x & mask) + (y & mask)) & mask;
            mask <<= 32;
            return r | ((x & mask) + (y & mask)) & mask;
        }

        /// <summary>
        /// Performs parallel subtraction of packed bytes (8-bit values).
        /// </summary>
        /// <param name="x">First operand containing packed bytes.</param>
        /// <param name="y">Second operand containing packed bytes to subtract.</param>
        /// <returns>Result of parallel byte subtraction.</returns>
        public static ulong PSubB (ulong x, ulong y)
        {
            ulong r = 0;
            for (ulong mask = 0xFF; mask != 0; mask <<= 8)
            {
                r |= ((x & mask) - (y & mask)) & mask;
            }
            return r;
        }

        /// <summary>
        /// Performs parallel subtraction of packed words (16-bit values).
        /// </summary>
        /// <param name="x">First operand containing packed words.</param>
        /// <param name="y">Second operand containing packed words to subtract.</param>
        /// <returns>Result of parallel word subtraction.</returns>
        public static ulong PSubW (ulong x, ulong y)
        {
            ulong mask = 0xffff;
            ulong r = ((x & mask) - (y & mask)) & mask;
            mask <<= 16;
            r |= ((x & mask) - (y & mask)) & mask;
            mask <<= 16;
            r |= ((x & mask) - (y & mask)) & mask;
            mask <<= 16;
            r |= ((x & mask) - (y & mask)) & mask;
            return r;
        }

        /// <summary>
        /// Performs parallel subtraction of packed doublewords (32-bit values).
        /// </summary>
        /// <param name="x">First operand containing packed doublewords.</param>
        /// <param name="y">Second operand containing packed doublewords to subtract.</param>
        /// <returns>Result of parallel doubleword subtraction.</returns>
        public static ulong PSubD (ulong x, ulong y)
        {
            ulong mask = 0xffffffff;
            ulong r = ((x & mask) - (y & mask)) & mask;
            mask <<= 32;
            return r | ((x & mask) - (y & mask)) & mask;
        }

        /// <summary>
        /// Performs parallel logical left shift on packed doublewords (32-bit values).
        /// </summary>
        /// <param name="x">Operand containing packed doublewords.</param>
        /// <param name="count">Number of bits to shift left (masked to 5 bits).</param>
        /// <returns>Result of parallel left shift.</returns>
        public static ulong PSllD (ulong x, int count)
        {
            count &= 0x1F;
            ulong mask = 0xFFFFFFFFu << count;
            mask |= mask << 32;
            return (x << count) & mask;
        }

        /// <summary>
        /// Performs parallel logical right shift on packed doublewords (32-bit values).
        /// </summary>
        /// <param name="x">Operand containing packed doublewords.</param>
        /// <param name="count">Number of bits to shift right (masked to 5 bits).</param>
        /// <returns>Result of parallel right shift.</returns>
        public static ulong PSrlD (ulong x, int count)
        {
            count &= 0x1F;
            ulong mask = 0xFFFFFFFFu >> count;
            mask |= mask << 32;
            return (x >> count) & mask;
        }
    }

    /// <summary>
    /// Utility class for dumping data to files for debugging purposes.
    /// </summary>
    public static class Dump
    {
        /// <summary>
        /// Gets or sets the directory where dump files will be saved.
        /// Defaults to the user's profile directory.
        /// </summary>
        public static string DirectoryName = Environment.GetFolderPath (Environment.SpecialFolder.UserProfile);

        /// <summary>
        /// Writes a byte array to a file. Only available in DEBUG builds.
        /// </summary>
        /// <param name="mem">The byte array to write.</param>
        /// <param name="filename">The filename to use (default: "index.dat").</param>
        [System.Diagnostics.Conditional("DEBUG")]
        public static void BytesToFile (byte[] mem, string filename = "index.dat")
        {
            using (var dump = File.Create (Path.Combine (DirectoryName, filename)))
                dump.Write (mem, 0, mem.Length);
        }
        
        /// <summary>
        /// Writes the contents of a stream to a file.
        /// </summary>
        /// <param name="stream">The stream to write.</param>
        /// <param name="filePath">The full path where the file should be saved.</param>
        /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
        /// <exception cref="ArgumentException">Thrown when filePath is null or empty.</exception>
        public static void StreamToFile(
                Stream stream, 
                string filePath)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
    
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var oldPos = stream.Position;
            if (stream.CanSeek)
                stream.Position = 0;

            const int bufferSize = 8192;
            byte[] buffer = new byte[bufferSize];
            int bytesRead;
    
            using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                    fileStream.Write(buffer, 0, bytesRead);

                fileStream.Flush();
            }
    
            if (stream.CanSeek)
                stream.Position = oldPos;
        }
    }

    /// <summary>
    /// Generic format handler for unidentified data files.
    /// </summary>
    /// <remarks>
    /// This format is used as a fallback when no specific format can be identified.
    /// </remarks>
    [Export(typeof(ScriptFormat))]
    public class DataFileFormat : GenericScriptFormat
    {
        /// <inheritdoc/>
        public override string        Type { get { return ""; } }
        
        /// <summary>
        /// Gets the format tag identifier.
        /// </summary>
        public override string         Tag { get { return "DAT/GENERIC"; } }
        
        /// <summary>
        /// Gets the format description.
        /// </summary>
        public override string Description { get { return "Unidentified data file"; } }
        
        /// <summary>
        /// Gets the format signature. Returns 0 as this is a generic format.
        /// </summary>
        public override uint     Signature { get { return 0; } }
    }

    /// <summary>
    /// Resource alias that maps GLT files to BMP format.
    /// </summary>
    /// <remarks>
    /// Used for the game [970725][Guilty] Onii-chan e
    /// </remarks>
    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "GLT")]
    [ExportMetadata("Target", "BMP")]
    public class GltFormat : ResourceAlias { }
}
