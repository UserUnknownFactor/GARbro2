using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.PyInstaller
{
    [Export(typeof(ArchiveFormat))]
    public class PyInstOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PYINST"; } }
        public override string Description { get { return "PyInstaller Archive"; } }
        public override uint     Signature { get { return  0; } }
        public override bool  IsHierarchic { get { return  true; } }
        public override bool      CanWrite { get { return  false; } }

        public PyInstOpener()
        {
            Extensions = new string[] { "exe", "dll", "so", "dylib" };
        }

        // PyInstaller magic string: "MEI\014\013\012\013\016"
        static readonly byte[] MAGIC = { 0x4D, 0x45, 0x49, 0x0C, 0x0B, 0x0A, 0x0B, 0x0E };
        const int PYINST20_COOKIE_SIZE = 24;
        const int PYINST21_COOKIE_SIZE = 24 + 64;

        public override ArcFile TryOpen(ArcView file)
        {
            long cookiePos = ExeFile.FindSignatureInOverlayReversed(file, MAGIC, 1, 0,
                SignatureValidation.AlwaysValid);

            /*if (cookiePos == 0 && !file.Name.EndsWith("exe"))
            {
                var section = new ExeFile.Section { Offset = 0, Size = (uint)file.MaxOffset };
                cookiePos = ExeFile.FindStringReversed(file, section, MAGIC, 1);
            }*/

            if (cookiePos <= 0)
                return null;

            int pyinstVer = DeterminePyInstallerVersion(file, cookiePos);
            if (pyinstVer == 0)
                return null;

            var archiveInfo = GetCArchiveInfo(file, cookiePos, pyinstVer);
            if (archiveInfo == null)
                return null;

            var dir = ParseTOC(file, archiveInfo);
            if (dir == null || dir.Count == 0)
                return null;

            return new PyInstArchive(file, this, dir, archiveInfo);
        }

        int DeterminePyInstallerVersion(ArcView file, long cookiePos)
        {
            if (cookiePos + PYINST20_COOKIE_SIZE + 64 > file.MaxOffset)
                return 0;

            var buffer = file.View.ReadBytes(cookiePos + PYINST20_COOKIE_SIZE, 64);
            string bufferStr = Encoding.ASCII.GetString(buffer);

            if (bufferStr.ToLower().Contains("python"))
                return 21; // PyInstaller 2.1+
            else
                return 20; // PyInstaller 2.0
        }

        internal class ArchiveInfo
        {
            public  int PyInstVer;
            public long OverlayPos;
            public long OverlaySize;
            public long TableOfContentsPos;
            public uint TableOfContentsSize;
            public  int PyMajor;
            public  int PyMinor;
        }

        ArchiveInfo GetCArchiveInfo(ArcView file, long cookiePos, int pyinstVer)
        {
            var info = new ArchiveInfo { PyInstVer = pyinstVer };

            try
            {
                int cookieSize = (pyinstVer == 20) ? PYINST20_COOKIE_SIZE : PYINST21_COOKIE_SIZE;

                // Read cookie data - PyInstaller uses Big-Endian
                uint lengthOfPackage = Binary.BigEndian(file.View.ReadUInt32(cookiePos + 8));
                uint toc             = Binary.BigEndian(file.View.ReadUInt32(cookiePos + 12));
                uint tocLen          = Binary.BigEndian(file.View.ReadUInt32(cookiePos + 16));
                uint pyver           = Binary.BigEndian(file.View.ReadUInt32(cookiePos + 20));

                // Parse Python version
                if (pyver >= 100)
                {
                    info.PyMajor = (int)(pyver / 100);
                    info.PyMinor = (int)(pyver % 100);
                }
                else
                {
                    info.PyMajor = (int)(pyver / 10);
                    info.PyMinor = (int)(pyver % 10);
                }

                // Calculate overlay info
                long tailBytes           = file.MaxOffset - cookiePos - cookieSize;
                info.OverlaySize         = lengthOfPackage + tailBytes;
                info.OverlayPos          = file.MaxOffset - info.OverlaySize;
                info.TableOfContentsPos  = info.OverlayPos + toc;
                info.TableOfContentsSize = tocLen;

                return info;
            }
            catch
            {
                return null;
            }
        }

        List<Entry> ParseTOC(ArcView file, ArchiveInfo info)
        {
            var dir = new List<Entry>();

            try
            {
                long pos = info.TableOfContentsPos;
                uint parsedLen = 0;

                while (parsedLen < info.TableOfContentsSize)
                {
                    uint entrySize = Binary.BigEndian(file.View.ReadUInt32(pos));
                    pos += 4;
                    parsedLen += 4;

                    if (entrySize < 18) // Minimum entry size
                        break;

                    uint entryPos = Binary.BigEndian(file.View.ReadUInt32(pos));
                    uint cmprsdDataSize = Binary.BigEndian(file.View.ReadUInt32(pos + 4));
                    uint uncmprsdDataSize = Binary.BigEndian(file.View.ReadUInt32(pos + 8));
                    byte cmprsFlag = file.View.ReadByte(pos + 12);
                    byte typeCmprsData = file.View.ReadByte(pos + 13);

                    uint nameLen = entrySize - 18;
                    var nameBytes = file.View.ReadBytes(pos + 14, nameLen);
                    string name = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');

                    pos += entrySize - 4;
                    parsedLen += entrySize - 4;

                    // Skip runtime options and dependencies
                    if (typeCmprsData == 'd' || typeCmprsData == 'o')
                        continue;

                    // Sanitize name
                    if (string.IsNullOrEmpty(name))
                        name = $"unnamed_{parsedLen:X8}";
                    else if (name.StartsWith("/"))
                        name = name.TrimStart('/');

                    name = name.Replace('/', Path.DirectorySeparatorChar);

                    // Add appropriate extension for Python files
                    if ((typeCmprsData == 's' || typeCmprsData == 'm' || typeCmprsData == 'M') &&
                        !name.Contains('.'))
                    {
                        name += ".pyc";
                    }

                    var entry = new PyInstEntry
                    {
                        Name = name,
                        Type = GetFileType(name, typeCmprsData),
                        Offset = info.OverlayPos + entryPos,
                        Size = cmprsdDataSize,
                        UnpackedSize = uncmprsdDataSize,
                        IsPacked = cmprsFlag == 1,
                        DataType = (char)typeCmprsData,
                        ArchiveInfo = info
                    };

                    dir.Add(entry);
                }
            }
            catch
            {
                return null;
            }

            return dir;
        }

        string GetFileType(string name, byte dataType)
        {
            // Python-specific types based on PyInstaller data type markers
            if (dataType == 's' || dataType == 'm' || dataType == 'M')
                return "script";
            if (dataType == 'z' || dataType == 'Z')
                return "";  // PYZ archives - untyped

            return FormatCatalog.Instance.GetTypeFromName(name);
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            var pyarc = arc as PyInstArchive;
            var pyentry = entry as PyInstEntry;
            if (pyarc == null || pyentry == null)
                return base.OpenEntry(arc, entry);

            var input = arc.File.CreateStream(entry.Offset, entry.Size);

            if (pyentry.IsPacked)
            {
                var output = new MemoryStream((int)pyentry.UnpackedSize);
                using (var zstream = new ZLibStream(input, CompressionMode.Decompress))
                {
                    zstream.CopyTo(output);
                }
                output.Position = 0;

                if (pyentry.DataType == 'M' || pyentry.DataType == 'm' || pyentry.DataType == 's')
                    return FixPycHeader(output, pyentry.ArchiveInfo);

                return output;
            }
            else
            {
                // Handle uncompressed Python bytecode
                if (pyentry.DataType == 'M' || pyentry.DataType == 'm' || pyentry.DataType == 's')
                {
                    var data = new byte[entry.Size];
                    input.Read(data, 0, data.Length);
                    input.Dispose();

                    // Check if it already has a valid header (pre-5.3)
                    if (data.Length > 4 && data[2] == 0x0D && data[3] == 0x0A)
                        return new MemoryStream(data);

                    // Need to restore header (5.3+)
                    var ms = new MemoryStream(data);
                    return FixPycHeader(ms, pyentry.ArchiveInfo);
                }

                return input;
            }
        }

        Stream FixPycHeader(Stream input, ArchiveInfo info)
        {
            var output = new MemoryStream();

            byte[] magic = GetPythonMagic(info.PyMajor, info.PyMinor);
            output.Write(magic, 0, 4);

            if (info.PyMajor >= 3 && info.PyMinor >= 7)
            {
                // PEP 552 - Deterministic pycs
                output.Write(new byte[4], 0, 4); // Bitfield
                output.Write(new byte[8], 0, 8); // Timestamp + size or hash
            }
            else
            {
                output.Write(new byte[4], 0, 4); // Timestamp
                if (info.PyMajor >= 3 && info.PyMinor >= 3)
                {
                    output.Write(new byte[4], 0, 4); // Size parameter
                }
            }

            input.CopyTo(output);
            output.Position = 0;
            return output;
        }

        byte[] GetPythonMagic(int major, int minor)
        {
            // Python magic numbers for different versions
            if (major == 3)
            {
                if (minor >= 11) return new byte[] { 0xA7, 0x0D, 0x0D, 0x0A };
                if (minor >= 10) return new byte[] { 0x6F, 0x0D, 0x0D, 0x0A };
                if (minor >= 9)  return new byte[] { 0x61, 0x0D, 0x0D, 0x0A };
                if (minor >= 8)  return new byte[] { 0x55, 0x0D, 0x0D, 0x0A };
                if (minor >= 7)  return new byte[] { 0x42, 0x0D, 0x0D, 0x0A };
                if (minor >= 6)  return new byte[] { 0x33, 0x0D, 0x0D, 0x0A };
                if (minor >= 5)  return new byte[] { 0x17, 0x0D, 0x0D, 0x0A };
                if (minor >= 4)  return new byte[] { 0x0E, 0x0D, 0x0D, 0x0A };
            }
            else if (major == 2)
            {
                if (minor == 7)  return new byte[] { 0x03, 0xF3, 0x0D, 0x0A };
                if (minor == 6)  return new byte[] { 0xD1, 0xF2, 0x0D, 0x0A };
            }

            return new byte[] { 0x00, 0x00, 0x0D, 0x0A };
        }
    }

    internal class PyInstEntry : PackedEntry
    {
        public char DataType { get; set; }
        public PyInstOpener.ArchiveInfo ArchiveInfo { get; set; }
    }

    internal class PyInstArchive : ArcFile
    {
        public PyInstOpener.ArchiveInfo ArchiveInfo { get; private set; }

        public PyInstArchive(ArcView arc, ArchiveFormat impl, ICollection<Entry> dir,
                           PyInstOpener.ArchiveInfo info)
            : base(arc, impl, dir)
        {
            ArchiveInfo = info;
        }
    }
}