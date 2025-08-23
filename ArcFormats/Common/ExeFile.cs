using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats
{
    public delegate bool ValidatorFunction(ArcView file, long offset, int signatureLength);

    public static class SignatureValidation
    {
        // Default: check for non-zero after signature
        public static bool HasNonZeroAfter (ArcView file, long offset, int signatureLength)
        {
            if (offset + signatureLength + 4 > file.MaxOffset)
                return false;
            return 0 != file.View.ReadUInt32 (offset + signatureLength);
        }

        // Alternative: always valid
        public static bool AlwaysValid (ArcView file, long offset, int signatureLength)
        {
            return offset + signatureLength <= file.MaxOffset;
        }

        // Specific magic after signature: SignatureValidation.HasMagicAfter(0x12345678)
        public static Func<ArcView, long, int, bool> HasMagicAfter (uint magic)
        {
            return (file, offset, signatureLength) =>
            {
                if (offset + signatureLength + 4 > file.MaxOffset)
                    return false;
                return magic == file.View.ReadUInt32 (offset + signatureLength);
            };
        }

        /* For custom validation logic we can supply something like:
        (f, o, len) => {
            return o + len + 8 <= f.MaxOffset && 
                   f.View.ReadUInt32(o + len) > 0 &&
                   f.View.ReadUInt32(o + len + 4) > 0;
        }
        */
    }

    public class ExeFile
    {
        ArcView                     m_file;
        Dictionary<string, Section> m_section_table;
        Section                     m_overlay;
        uint                        m_image_base = 0;
        List<ImageSection>          m_section_list;
        bool?                       m_is_NE;

        public ExeFile (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "MZ"))
                throw new InvalidFormatException ("File is not a valid win32 executable.");

            m_file = file;
            Whole = new Section { Offset = 0, Size = (uint)Math.Min (m_file.MaxOffset, uint.MaxValue) };
        }

        public ArcView.Frame View { get { return m_file.View; } }

        /// <summary>
        /// Section representing the whole file.
        /// </summary>
        public Section Whole { get; private set; }

        public bool IsWin16 => m_is_NE ?? (m_is_NE = IsNe()).Value;

        private bool IsNe ()
        {
            uint ne_offset = View.ReadUInt32 (0x3C);
            return ne_offset < m_file.MaxOffset - 2 && View.AsciiEqual (ne_offset, "NE");
        }

        /// <summary>
        /// Dictionary of executable file sections.
        /// </summary>
        ///
        public IReadOnlyDictionary<string, Section> Sections
        {
            get
            {
                if (null == m_section_table)
                    InitSectionTable();
                return m_section_table;
            }
        }

        /// <summary>
        /// Overlay section of executable file.
        /// </summary>
        public Section Overlay
        {
            get
            {
                if (null == m_section_table)
                    InitSectionTable();
                return m_overlay;
            }
        }

        public uint ImageBase
        {
            get
            {
                if (0 == m_image_base)
                    InitImageBase();
                return m_image_base;
            }
        }

        static int[] COMMON_ALIGNMENTS = { 0, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096 };

        /// <summary>
        /// Attempts to detect and open appended data as a known archive format.
        /// </summary>
        /// <returns>An opened archive if format is recognized, null otherwise.</returns>
        public ArcFile TryOpenOverlay ()
        {
            var overlay = Overlay;
            if (overlay.Size < 4)
                return null;

            var catalog = FormatCatalog.Instance;

            foreach (int alignment in COMMON_ALIGNMENTS)
            {
                if (alignment >= overlay.Size)
                    continue;

                long searchOffset = overlay.Offset + alignment;

                if (searchOffset + 4 > m_file.MaxOffset)
                    continue;

                uint signature = View.ReadUInt32 (searchOffset);

                var formats = catalog.FindFormats<ArchiveFormat>(m_file.Name, signature);
                if (!formats.Any())
                    continue;

                uint dataSize = (uint)(m_file.MaxOffset - searchOffset);

                using (var stream = m_file.CreateStream (searchOffset, dataSize))
                {
                    var appended_view = new ArcView (stream, m_file.Name, dataSize);

                    foreach (var format in formats)
                    {
                        try
                        {
                            var arc = format.TryOpen (appended_view);
                            if (arc != null)
                                return arc;
                        }
                        catch { }
                    }

                    appended_view.Dispose();
                }
            }

            return ScanOverlayForArchive();
        }

        /// <summary>
        /// Performs a thorough scan of the overlay section looking for known archive signatures.
        /// </summary>
        private ArcFile ScanOverlayForArchive ()
        {
            var overlay = Overlay;
            var catalog = FormatCatalog.Instance;

            var signatures = new HashSet<uint>();
            foreach (var format in catalog.ArcFormats)
                foreach (var sig in format.Signatures)
                    signatures.Add (sig);

            // Scan through the overlay looking for these signatures
            const int scanStep = 16; // Check every 16 bytes
            long maxScan = Math.Min (overlay.Size, 65536); // Don't scan more than 64KB

            for (long offset = 0; offset < maxScan; offset += scanStep)
            {
                long searchOffset = overlay.Offset + offset;

                if (searchOffset + 4 > m_file.MaxOffset)
                    break;

                uint signature = View.ReadUInt32 (searchOffset);

                if (!signatures.Contains (signature))
                    continue;

                var formats = catalog.FindFormats<ArchiveFormat>(m_file.Name, signature);

                uint dataSize = (uint)(m_file.MaxOffset - searchOffset);

                using (var stream = m_file.CreateStream (searchOffset, dataSize))
                {
                    var appended_view = new ArcView (stream, m_file.Name, dataSize);

                    foreach (var format in formats)
                    {
                        try
                        {
                            var arc = format.TryOpen (appended_view);
                            if (arc != null)
                                return arc;
                        }
                        catch { }
                    }

                    appended_view.Dispose();
                }
            }

            return null;
        }

        /// <summary>
        /// Gets information about appended data format if detected.
        /// </summary>
        public AppendedDataInfo GetOverlayInfo ()
        {
            var overlay = Overlay;
            if (overlay.Size < 4)
                return null;

            var catalog = FormatCatalog.Instance;

            foreach (int alignment in COMMON_ALIGNMENTS)
            {
                if (alignment >= overlay.Size)
                    continue;

                long searchOffset = overlay.Offset + alignment;

                if (searchOffset + 4 > m_file.MaxOffset)
                    continue;

                uint signature = View.ReadUInt32 (searchOffset);
                var formats = catalog.LookupSignature (signature);

                if (formats.Any())
                {
                    var format = formats.FirstOrDefault();
                    if (format == null)
                        return null;
                    return new AppendedDataInfo
                    {
                        Offset            = searchOffset,
                        Size              = (uint)(m_file.MaxOffset - searchOffset),
                        Format            = format.Tag,
                        FormatDescription = format.Description,
                        Signature         = signature,
                        AlignmentOffset   = alignment
                    };
                }
            }

            // If alignment search failed, do a more thorough scan
            return ScanOverlayForInfo();
        }

        /// <summary>
        /// Performs a thorough scan looking for format information in the overlay.
        /// </summary>
        private AppendedDataInfo ScanOverlayForInfo ()
        {
            var overlay = Overlay;
            var catalog = FormatCatalog.Instance;

            const int scanStep = 16;
            long maxScan = Math.Min (overlay.Size, 65536);

            for (long offset = 0; offset < maxScan; offset += scanStep)
            {
                long searchOffset = overlay.Offset + offset;

                if (searchOffset + 4 > m_file.MaxOffset)
                    break;

                uint signature = View.ReadUInt32 (searchOffset);
                var formats    = catalog.LookupSignature (signature);

                if (formats.Any())
                {
                    var format = formats.First();
                    return new AppendedDataInfo
                    {
                        Offset = searchOffset,
                        Size = (uint)(m_file.MaxOffset - searchOffset),
                        Format = format.Tag,
                        FormatDescription = format.Description,
                        Signature = signature,
                        AlignmentOffset = offset
                    };
                }
            }

            return null;
        }

        /// <summary>
        /// Creates an ArcView for the overlay section.
        /// </summary>
        public ArcView GetOverlayView ()
        {
            var overlay = Overlay;
            if (overlay.Size == 0)
                return null;

            var stream = m_file.CreateStream (overlay.Offset, overlay.Size);
            return new ArcView (stream, m_file.Name, overlay.Size);
        }

        /// <summary>
        /// Information about appended data found in the executable.
        /// </summary>
        public class AppendedDataInfo
        {
            public long              Offset { get; set; }
            public uint                Size { get; set; }
            public string            Format { get; set; }
            public string FormatDescription { get; set; }
            public uint           Signature { get; set; }
            public long     AlignmentOffset { get; set; }
        }

        /// <summary>
        /// Structure representing section of executable file in the form of its offset and size.
        /// </summary>
        public struct Section
        {
            public long Offset;
            public uint Size;
        }

        public class ImageSection // IMAGE_SECTION_HEADER
        {
            public string Name;
            public uint   VirtualSize;
            public uint   VirtualAddress;
            public uint   SizeOfRawData;
            public uint   PointerToRawData;
            public uint   Characteristics;
        }

        /// <summary>
        /// Returns true if executable file contains section <paramref name="name"/>.
        /// </summary>
        public bool ContainsSection (string name)
        {
            return Sections.ContainsKey (name);
        }

        /// <summary>
        /// Search for byte sequence within specified section.
        /// </summary>
        /// <returns>Offset of byte sequence, if found, -1 otherwise.</returns>
        public long FindString (Section section, byte[] seq, int step = 1, long limit = -1)
        {
            if (step <= 0)
                throw new ArgumentOutOfRangeException ("step", "Search step should be positive integer.");
            long offset = section.Offset;
            if (offset < 0 || offset > m_file.MaxOffset)
                throw new ArgumentOutOfRangeException ("section", "Invalid executable file section specified.");
            uint seq_length = (uint)seq.Length;
            if (0 == seq_length || section.Size < seq_length)
                return -1;
            if (limit < seq_length)
                limit = section.Size;
            long end_offset = Math.Min (m_file.MaxOffset, offset + limit);
            unsafe
            {
                while (offset < end_offset)
                {
                    uint page_size = (uint)Math.Min (0x10000L, end_offset - offset);
                    if (page_size < seq_length)
                        break;
                    using (var view = m_file.CreateViewAccessor (offset, page_size))
                    using (var ptr = new ViewPointer (view, offset))
                    {
                        byte* page_begin = ptr.Value;
                        byte* page_end   = page_begin + page_size - seq_length;
                        byte* p;
                        for (p = page_begin; p <= page_end; p += step)
                        {
                            int i = 0;
                            while (p[i] == seq[i])
                            {
                                if (++i == seq.Length)
                                    return offset + (p - page_begin);
                            }
                        }
                        offset += p - page_begin;
                    }
                }
            }
            return -1;
        }

        public Section SectionByOffset (long offset)
        {
            foreach (var section in Sections.Values)
            {
                if (offset >= section.Offset && offset < section.Offset + section.Size)
                    return section;
            }
            return new Section { Offset = Whole.Size, Size = 0 };
        }

        public long FindAsciiString (Section section, string seq, int step = 1)
        {
            return FindString (section, Encoding.ASCII.GetBytes (seq), step);
        }

        public long FindSignature (Section section, uint signature, int step = 4, long limit = 0x10000)
        {
            var bytes = new byte[4];
            LittleEndian.Pack (signature, bytes, 0);
            return FindString (section, bytes, step, limit);
        }

        /// <summary>
        /// Finds the provided signature in exe file's specified section or overlay.
        /// </summary>
        /// <returns>Offset to signature if found; 0 otherwise or if it's not exe.</returns>
        public static long FindSignature (ArcView file, byte[] signature, string section = ".rsrc", 
            int step = 4, long limit = 0x10000, ValidatorFunction validator = null)
        {
            if (0x5a4d != file.View.ReadUInt16 (0)) // 'MZ'
                return 0;

            validator = validator ?? SignatureValidation.HasNonZeroAfter;
            var exe = new ExeFile (file);

            // Try section first if specified
            if (!string.IsNullOrEmpty (section))
            {
                var offset = FindSignatureInSection (file, exe, signature, section, step, limit, validator);
                if (offset != 0)
                    return offset;
            }

            // Try overlay
            return FindSignatureInOverlay (file, exe, signature, step, limit, validator);
        }

        /// <summary>
        /// Finds the provided signature in exe file's specified section.
        /// </summary>
        /// <returns>Offset to signature if found; 0 otherwise.</returns>
        public static long FindSignatureInSection (ArcView file, ExeFile exe, byte[] signature, 
            string sectionName, int step, long limit, ValidatorFunction validator)
        {
            if (!exe.ContainsSection (sectionName))
                return 0;

            validator = validator ?? SignatureValidation.AlwaysValid;

            var section = exe.Sections[sectionName];
            var offset = exe.FindString (section, signature, step, limit);

            if (offset != -1 && validator (file, offset, signature.Length))
                return offset;

            return 0;
        }

        /// <summary>
        /// Finds the provided signature in exe file's overlay.
        /// </summary>
        /// <returns>Offset to signature if found; 0 otherwise.</returns>
        public static long FindSignatureInOverlay (ArcView file, ExeFile exe, byte[] signature, 
            int step, long limit, ValidatorFunction validator)
        {
            var overlay = new ExeFile.Section 
            {
                Offset = exe.Overlay.Offset,
                Size = exe.Overlay.Size
            };

            validator = validator ?? SignatureValidation.AlwaysValid;

            long maxSearch = Math.Min (overlay.Offset + overlay.Size, file.MaxOffset - signature.Length - 4);

            while (overlay.Offset < maxSearch)
            {
                var offset = exe.FindString (overlay, signature, step, limit);
                if (-1 == offset)
                    break;

                if (validator (file, offset, signature.Length))
                    return offset;

                overlay.Offset = offset + step;
                overlay.Size   = (uint)(file.MaxOffset - overlay.Offset);
            }

            return 0;
        }

        /// <summary>
        /// Convenience method to search only in a section.
        /// </summary>
        public static long FindSignatureInSection (ArcView file, byte[] signature, string sectionName, 
            int step = 4, long limit = 0x10000, ValidatorFunction validator = null)
        {
            if (0x5a4d != file.View.ReadUInt16 (0)) // 'MZ'
                return 0;

            var exe = new ExeFile (file);
            return FindSignatureInSection (file, exe, signature, sectionName, step, limit, validator);
        }

        /// <summary>
        /// Convenience method to search only in overlay.
        /// </summary>
        public static long FindSignatureInOverlay (ArcView file, byte[] signature, 
            int step = 4, long limit = 0x10000, ValidatorFunction validator = null)
        {
            if (0x5a4d != file.View.ReadUInt16 (0)) // 'MZ'
                return 0;

            var exe = new ExeFile (file);
            return FindSignatureInOverlay (file, exe, signature, step, limit, validator);
        }

        /// <summary>
        /// Finds the provided signature in exe file's biggest sections.
        /// </summary>
        /// <returns>Offset to signature if found; 0 otherwise or if it's not exe.</returns>
        public static long AutoFindSignature (ArcView file, byte[] signature, 
            int step = 1, long limit = 0x10000, ValidatorFunction validator = null)
        {
            if (0x5a4d != file.View.ReadUInt16 (0)) // 'MZ'
                return 0;

            validator = validator ?? SignatureValidation.HasNonZeroAfter;

            var exe = new ExeFile (file);
            var sections = exe.Sections;

            var biggestSections = sections
                .OrderByDescending (s => s.Value.Size)
                .Take (2)
                .ToList();

            foreach (var section in biggestSections)
            {
                long offset = exe.FindString (section.Value, signature, step, limit);
                if (offset != -1 && validator (file, offset, signature.Length))
                    return offset;
            }

            return 0;
        }

        /// <summary>
        /// Finds the provided signature in exe file's overlay, searching backwards from the end.
        /// Useful for formats where signatures are located near the end of the file (e.g., PyInstaller).
        /// </summary>
        /// <returns>Offset to signature if found; 0 otherwise.</returns>
        public static long FindSignatureInOverlayReversed (ArcView file, ExeFile exe, byte[] signature, 
            int step, long limit, ValidatorFunction validator)
        {
            var overlay = exe.Overlay;
            if (overlay.Size < signature.Length)
                return 0;

            validator = validator ?? SignatureValidation.AlwaysValid;

            long searchEnd = Math.Min (overlay.Offset + overlay.Size, file.MaxOffset);
            long searchStart = overlay.Offset;

            const uint blockSize = 0x10000; // 64KB blocks
            byte[] buffer = new byte[blockSize + signature.Length];

            long currentPos = searchEnd;

            while (currentPos > searchStart + signature.Length - 1)
            {
                // Calculate block boundaries
                long blockStart = Math.Max (searchStart, currentPos - blockSize);
                uint readSize = (uint)(currentPos - blockStart);

                if (readSize < signature.Length)
                    break;

                using (var view = file.CreateViewAccessor (blockStart, readSize))
                using (var ptr = new ViewPointer (view, blockStart))
                {
                    unsafe
                    {
                        byte* page_begin = ptr.Value;

                        for (long i = readSize - signature.Length; i >= 0; i -= step)
                        {
                            bool match = true;
                            for (int j = 0; j < signature.Length; j++)
                            {
                                if (page_begin[i + j] != signature[j])
                                {
                                    match = false;
                                    break;
                                }
                            }

                            if (match)
                            {
                                long foundOffset = blockStart + i;
                                if (validator (file, foundOffset, signature.Length))
                                    return foundOffset;
                            }
                        }
                    }
                }

                currentPos = blockStart + signature.Length - 1;

                if (blockStart == searchStart)
                    break;
            }

            return 0;
        }

        /// <summary>
        /// Convenience method to search in overlay backwards from the end.
        /// </summary>
        public static long FindSignatureInOverlayReversed (ArcView file, byte[] signature, 
            int step = 4, long limit = 0x10000, ValidatorFunction validator = null)
        {
            if (0x5a4d != file.View.ReadUInt16 (0)) // 'MZ'
                return 0;

            var exe = new ExeFile (file);
            return FindSignatureInOverlayReversed (file, exe, signature, step, limit, validator);
        }

        /// <summary>
        /// Finds the provided signature in exe file from the end.
        /// </summary>
        /// <returns>Offset to signature if found; 0 otherwise or if it's not exe.</returns>
        public static long FindSignatureReversed (ArcView file, byte[] signature, 
            int step = 1, long limit = 0, ValidatorFunction validator = null)
        {
            if (0x5a4d != file.View.ReadUInt16 (0)) // 'MZ'
                return 0;

            validator = validator ?? SignatureValidation.HasNonZeroAfter;

            var exe = new ExeFile (file);

            long offset = FindSignatureInOverlayReversed (file, exe, signature, step, limit, validator);
            if (offset != 0)
                return offset;

            return 0;
        }

        /// <summary>
        /// Finds the provided signature in exe file from the end.
        /// </summary>
        /// <returns>Offset to signature if found; 0 otherwise.</returns>
        public static long AutoFindSignatureReversed (ArcView file, byte[] signature, 
            int step = 1, long limit = 0x10000, ValidatorFunction validator = null)
        {
            if (0x5a4d != file.View.ReadUInt16 (0)) // 'MZ'
                return 0;

            validator = validator ?? SignatureValidation.HasNonZeroAfter;

            var exe = new ExeFile (file);

            var sections = exe.Sections;
            var lastSections = sections
                .OrderByDescending (s => s.Value.Offset)
                .Take (2)
                .ToList();

            foreach (var section in lastSections)
            {
                long offset = FindStringReversed (file, section.Value, signature, step, limit);
                if (offset != -1 && validator (file, offset, signature.Length))
                    return offset;
            }

            return 0;
        }

        /// <summary>
        /// Search for byte sequence within specified section, searching backwards.
        /// </summary>
        /// <returns>Offset of byte sequence, if found, -1 otherwise.</returns>
        public static long FindStringReversed (ArcView file, Section section, byte[] seq, 
            int step = 1, long limit = -1)
        {
            if (step <= 0)
                throw new ArgumentOutOfRangeException ("step", "Search step should be positive integer.");

            long offset = section.Offset;
            if (offset < 0 || offset > file.MaxOffset)
                throw new ArgumentOutOfRangeException ("section", "Invalid executable file section specified.");

            uint seq_length = (uint)seq.Length;
            if (0 == seq_length || section.Size < seq_length)
                return -1;

            if (limit < seq_length)
                limit = section.Size;

            long start_offset = offset;
            long end_offset = Math.Min (file.MaxOffset, offset + limit);

            unsafe
            {
                long current = end_offset;
                while (current > start_offset + seq_length - 1)
                {
                    long block_start = Math.Max (start_offset, current - 0x10000);
                    uint page_size = (uint)(current - block_start);

                    if (page_size < seq_length)
                        break;

                    using (var view = file.CreateViewAccessor (block_start, page_size))
                    using (var ptr = new ViewPointer (view, block_start))
                    {
                        byte* page_begin = ptr.Value;

                        for (long i = page_size - seq_length; i >= 0; i -= step)
                        {
                            int j = 0;
                            while (page_begin[i + j] == seq[j])
                            {
                                if (++j == seq.Length)
                                    return block_start + i;
                            }
                        }
                    }

                    current = block_start + seq_length - 1;

                    if (block_start == start_offset)
                        break;
                }
            }

            return -1;
        }

        /// <summary>
        /// Convert virtual address into raw file offset.
        /// </summary>
        public long GetAddressOffset (uint address)
        {
            var section = GetAddressSection (address);
            if (null == section)
                return m_file.MaxOffset;
            uint rva = address - ImageBase;
            return section.PointerToRawData + (rva - section.VirtualAddress);
        }

        public string GetCString (uint address)
        {
            return GetCString (address, Encodings.cp932);
        }

        static readonly byte[] ZeroByte = new byte[1] { 0 };

        /// <summary>
        /// Returns null-terminated string from specified virtual address.
        /// </summary>
        public string GetCString (uint address, Encoding enc)
        {
            var section = GetAddressSection (address);
            if (null == section)
                return null;
            uint rva    = address - ImageBase;
            uint offset = section.PointerToRawData + (rva - section.VirtualAddress);
            uint size   = section.PointerToRawData + section.SizeOfRawData - offset;
            long eos    = FindString (new Section { Offset = offset, Size = size }, ZeroByte);
            if (eos < 0)
                return null;
            return View.ReadString (offset, (uint)(eos - offset), enc);
        }

        private ImageSection GetAddressSection (uint address)
        {
            var img_base = ImageBase;
            if (address < img_base)
                throw new ArgumentException ("Invalid virtual address.");
            if (null == m_section_list)
                InitSectionTable();
            uint rva = address - img_base;
            foreach (var section in m_section_list)
            {
                if (rva >= section.VirtualAddress && rva < section.VirtualAddress + section.SizeOfRawData)
                    return section;
            }
            return null;
        }

        private void InitImageBase ()
        {
            long hdr_offset = GetHeaderOffset() + 0x18;
            if (View.ReadUInt16 (hdr_offset) != 0x010B)
                throw new InvalidFormatException ("File is not a valid Windows 32-bit executable.");
            m_image_base = View.ReadUInt32 (hdr_offset + 0x1C); // ImageBase
        }

        private long GetHeaderOffset ()
        {
            long pe_offset = View.ReadUInt32 (0x3C);
            if (pe_offset >= m_file.MaxOffset - 0x58 || !View.AsciiEqual (pe_offset, "PE\0\0"))
                throw new InvalidFormatException ("File is not a valid Windows 32-bit executable.");
            return pe_offset;
        }

        private void InitSectionTable ()
        {
            if (IsWin16)
            {
                InitNe();
                return;
            }
            long pe_offset = GetHeaderOffset();
            int opt_header = View.ReadUInt16 (pe_offset + 0x14); // SizeOfOptionalHeader
            long section_table = pe_offset + opt_header + 0x18;
            long offset = View.ReadUInt32 (pe_offset + 0x54); // SizeOfHeaders
            int count = View.ReadUInt16 (pe_offset + 0x6);  // NumberOfSections

            var table = new Dictionary<string, Section>(count);
            var list  = new List<ImageSection>(count);
            if (section_table + 0x28 * count < m_file.MaxOffset)
            {
                for (int i = 0; i < count; ++i)
                {
                    var name = View.ReadString (section_table, 8);
                    var img_section = new ImageSection {
                        Name = name,
                        VirtualSize      = View.ReadUInt32 (section_table + 0x08),
                        VirtualAddress   = View.ReadUInt32 (section_table + 0x0C),
                        SizeOfRawData    = View.ReadUInt32 (section_table + 0x10),
                        PointerToRawData = View.ReadUInt32 (section_table + 0x14),
                        Characteristics  = View.ReadUInt32 (section_table + 0x24),
                    };
                    var section = new Section {
                        Offset = img_section.PointerToRawData,
                        Size = img_section.SizeOfRawData
                    };
                    list.Add (img_section);
                    if (!table.ContainsKey (name))
                        table.Add (name, section);
                    if (0 != section.Size)
                        offset = Math.Max (section.Offset + section.Size, offset);
                    section_table += 0x28;
                }
            }
            offset = Math.Min ((offset + 0xF) & ~0xFL, m_file.MaxOffset);
            m_overlay.Offset = offset;
            m_overlay.Size   = (uint)(m_file.MaxOffset - offset);
            m_section_table  = table;
            m_section_list   = list;
        }

        void InitNe ()
        {
            uint ne_offset    = m_file.View.ReadUInt32 (0x3C);
            int segment_count = m_file.View.ReadUInt16 (ne_offset + 0x1C);
            uint seg_table    = m_file.View.ReadUInt16 (ne_offset + 0x22) + ne_offset;
            int shift         = m_file.View.ReadUInt16 (ne_offset + 0x32);

            uint last_seg_end = 0;
            for (int i = 0; i < segment_count; ++i)
            {
                uint offset = (uint)m_file.View.ReadUInt16 (seg_table) << shift;
                uint size   = m_file.View.ReadUInt16 (seg_table + 2);
                if (offset + size > last_seg_end)
                    last_seg_end = offset + size;
            }
            m_overlay.Offset = last_seg_end;
            m_overlay.Size   = (uint)(m_file.MaxOffset - last_seg_end);
            m_section_table  = new Dictionary<string, Section>();    // these are empty for 16-bit executables
            m_section_list   = new List<ImageSection>();              //
        }

        /// <summary>
        /// Helper class for executable file resources access.
        /// </summary>
        public sealed class ResourceAccessor : IDisposable
        {
            IntPtr m_exe;

            public ResourceAccessor (string filename)
            {
                const uint LOAD_LIBRARY_AS_DATAFILE       = 0x02;
                const uint LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x20;

                m_exe = NativeMethods.LoadLibraryEx (filename, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE);
                if (IntPtr.Zero == m_exe)
                    throw new Win32Exception (Marshal.GetLastWin32Error());
            }

            public byte[] GetResource (string name, string type)
            {
                var res = FindResource (name, type);
                if (IntPtr.Zero == res)
                    return null;
                var src = LockResource (res);
                if (IntPtr.Zero == src)
                    return null;
                uint size = NativeMethods.SizeofResource (m_exe, res);
                var dst = new byte[size];
                Marshal.Copy (src, dst, 0, dst.Length);
                return dst;
            }

            public int ReadResource (string name, string type, byte[] dest, int pos)
            {
                var res = FindResource (name, type);
                if (IntPtr.Zero == res)
                    return 0;
                var src = LockResource (res);
                if (IntPtr.Zero == src)
                    return 0;
                int length = (int)NativeMethods.SizeofResource (m_exe, res);
                length = Math.Min (dest.Length - pos, length);
                Marshal.Copy (src, dest, pos, length);
                return length;
            }

            public uint GetResourceSize (string name, string type)
            {
                var res = FindResource (name, type);
                if (IntPtr.Zero == res)
                    return 0;
                return NativeMethods.SizeofResource (m_exe, res);
            }

            private IntPtr FindResource (string name, string type)
            {
                if (m_disposed)
                    throw new ObjectDisposedException ("Access to disposed ResourceAccessor object failed.");
                return NativeMethods.FindResource (m_exe, name, type);
            }

            private IntPtr LockResource (IntPtr res)
            {
                var glob = NativeMethods.LoadResource (m_exe, res);
                if (IntPtr.Zero == glob)
                    return IntPtr.Zero;
                return NativeMethods.LockResource (glob);
            }

            public IEnumerable<string> EnumTypes()
            {
                var types = new List<string>();
                if (!NativeMethods.EnumResourceTypes (m_exe, (m, t, p) => AddResourceName (types, t), IntPtr.Zero))
                    return Enumerable.Empty<string>();
                return types;
            }

            public IEnumerable<string> EnumNames (string type)
            {
                var names = new List<string>();
                if (!NativeMethods.EnumResourceNames (m_exe, type, (m, t, n, p) => AddResourceName (names, n), IntPtr.Zero))
                    return Enumerable.Empty<string>();
                return names;
            }

            private static bool AddResourceName (List<string> list, IntPtr name)
            {
                list.Add (ResourceNameToString (name));
                return true;
            }

            private static string ResourceNameToString (IntPtr resName)
            {
                if ((resName.ToInt64() >> 16) == 0)
                {
                    return "#" + resName.ToString();
                }
                else
                {
                    return Marshal.PtrToStringUni (resName);
                }
            }

            #region IDisposable implementation
            bool m_disposed = false;
            public void Dispose ()
            {
                Dispose (true);
                GC.SuppressFinalize (this);
            }

            ~ResourceAccessor()
            {
                Dispose (false);
            }

            void Dispose (bool disposing)
            {
                if (!m_disposed)
                {
                    NativeMethods.FreeLibrary (m_exe);
                    m_disposed = true;
                }
            }
            #endregion
        }
    }

    static internal class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static internal extern IntPtr LoadLibraryEx (string lpFileName, IntPtr hReservedNull, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs (UnmanagedType.Bool)]
        static internal extern bool FreeLibrary (IntPtr hModule);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static internal extern IntPtr FindResource (IntPtr hModule, string lpName, string lpType);

        [DllImport("Kernel32.dll", SetLastError = true)]
        static internal extern IntPtr LoadResource (IntPtr hModule, IntPtr hResource);

        [DllImport("Kernel32.dll", SetLastError = true)]
        static internal extern uint SizeofResource (IntPtr hModule, IntPtr hResource);

        [DllImport("kernel32.dll")]
        static internal extern IntPtr LockResource (IntPtr hResData);

        internal delegate bool EnumResTypeProc (IntPtr hModule, IntPtr lpszType, IntPtr lParam);
        internal delegate bool EnumResNameProc (IntPtr hModule, IntPtr lpszType, IntPtr lpszName, IntPtr lParam);
        internal delegate bool EnumResLangProc (IntPtr hModule, IntPtr lpszType, IntPtr lpszName, ushort wIDLanguage, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static internal extern bool EnumResourceTypes (IntPtr hModule, [MarshalAs (UnmanagedType.FunctionPtr)] EnumResTypeProc lpEnumFunc, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static internal extern bool EnumResourceNames (IntPtr hModule, IntPtr lpszType, EnumResNameProc lpEnumFunc, IntPtr lParam);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static internal extern bool EnumResourceNames (IntPtr hModule, string lpszType, EnumResNameProc lpEnumFunc, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static internal extern bool EnumResourceLanguages (IntPtr hModule, IntPtr lpszType, string lpName, EnumResLangProc lpEnumFunc, IntPtr lParam);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static internal extern bool EnumResourceLanguages (IntPtr hModule, string lpszType, string lpName, EnumResLangProc lpEnumFunc, IntPtr lParam);
    }
}