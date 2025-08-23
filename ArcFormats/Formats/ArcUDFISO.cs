using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using GameRes.Formats.DiscImages;
using GameRes.Utility;

namespace GameRes.Formats.Udf
{
    [Export(typeof(ArchiveFormat))]
    [ExportMetadata("Priority", 60)] // Higher than ISO
    public class UdfOpener : DiscImageOpener
    {
        public override string         Tag { get { return "UDF"; } }
        public override string Description { get { return "Universal Disk Format ISO"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public UdfOpener ()
        {
            Extensions = new string[] { "iso", "img", "udf" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            // Check for BEA01 at sector 16 (0x8000 for 2048-byte sectors)
            if (file.MaxOffset < 0x8800)
                return null;

            var bea = file.View.ReadBytes (0x8000, 6);
            if (bea[1] != 'B' || bea[2] != 'E' || bea[3] != 'A' || 
                bea[4] != '0' || bea[5] != '1')
                return null;

            var discInfo = ParseDiscImage (file);
            if (discInfo == null)
                return null;

            var udf = new UdfReader (file);
            if (!udf.Open())
                return null;

            var entries = new List<Entry>();
            if (!udf.ReadFileSystem (entries))
                return null;

            return new DiscImageArchive (file, this, entries, discInfo);
        }

        protected override DiscInfo ParseDiscImage (ArcView file)
        {
            var discInfo = new DiscInfo
            {
                MediumType = "DVD"
            };

            var track = new TrackInfo
            {
                Number = 1,
                Session = 1,
                StartSector = 0,
                EndSector = (long)(file.MaxOffset / 2048) - 1,
                Length = file.MaxOffset / 2048,
                ImageOffset = 0,
                SectorSize = 2048,
                MainDataSize = 2048,
                SubchannelSize = 0,
                DataOffset = 0,
                Mode = TrackMode.Mode1,
                Ctl = 0x04
            };

            var session = new SessionInfo
            {
                Number = 1,
                Type = SessionType.CDROM
            };

            session.Tracks.Add (track);
            discInfo.Sessions.Add (session);
            discInfo.TotalSize = file.MaxOffset;

            return discInfo;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var udfEntry = entry as UdfEntry;
            if (udfEntry != null)
            {
                return new UdfFileStream (arc.File, udfEntry);
            }
            return base.OpenEntry (arc, entry);
        }
    }

    internal class UdfEntry : Entry
    {
        public List<UdfExtent> Extents { get; set; } = new List<UdfExtent>();
        public byte[] InlineData { get; set; }
        public bool IsInline { get; set; }
        public int SecLogSize { get; set; }
    }

    internal class UdfExtent
    {
        public uint Pos { get; set; }
        public uint Len { get; set; }
        public uint PartitionRef { get; set; }
        public uint PartitionPos { get; set; }

        public uint GetLen() { return Len & 0x3FFFFFFF; }
        public new uint GetType() { return Len >> 30; }
        public bool IsRecAndAlloc() { return GetType() == 0; }
    }

    internal class UdfReader
    {
        private ArcView m_file;
        private int m_secLogSize;
        private List<UdfPartition> m_partitions = new List<UdfPartition>();
        private List<UdfLogicalVolume> m_logicalVolumes = new List<UdfLogicalVolume>();
        private Dictionary<uint, UdfItem> m_items = new Dictionary<uint, UdfItem>();

        public UdfReader (ArcView file)
        {
            m_file = file;
        }

        public bool Open()
        {
            // Try different sector sizes (2048, 512, 1024)
            for (m_secLogSize = 11; m_secLogSize >= 9; m_secLogSize -= 2)
            {
                if (TryReadAnchorVolumeDescriptor())
                    return true;
            }
            return false;
        }

        private bool TryReadAnchorVolumeDescriptor()
        {
            uint sectorSize = (uint)(1 << m_secLogSize);
            uint avdpLocation = 256; // Standard location
            long offset = avdpLocation * sectorSize;

            if (offset + sectorSize > m_file.MaxOffset)
                return false;

            var buffer = m_file.View.ReadBytes (offset, sectorSize);

            // Check tag
            if (!CheckTag (buffer, 2)) // Anchor Volume Descriptor Pointer
                return false;

            // Read Main Volume Descriptor Sequence Extent
            uint vdsLen = LittleEndian.ToUInt32 (buffer, 16);
            uint vdsPos = LittleEndian.ToUInt32 (buffer, 20);

            return ReadVolumeDescriptorSequence (vdsPos, vdsLen);
        }

        private bool ReadVolumeDescriptorSequence (uint vdsPos, uint vdsLen)
        {
            uint sectorSize = (uint)(1 << m_secLogSize);
            uint numSectors = vdsLen / sectorSize;

            for (uint i = 0; i < numSectors; i++)
            {
                long offset = ((long)vdsPos + i) * sectorSize;
                if (offset + sectorSize > m_file.MaxOffset)
                    break;

                var buffer = m_file.View.ReadBytes (offset, sectorSize);
                ushort tagId = LittleEndian.ToUInt16 (buffer, 0);

                switch (tagId)
                {
                case 1: // Primary Volume Descriptor
                    ParsePrimaryVolumeDescriptor (buffer);
                    break;
                case 5: // Partition Descriptor
                    ParsePartitionDescriptor (buffer);
                    break;
                case 6: // Logical Volume Descriptor
                    ParseLogicalVolumeDescriptor (buffer);
                    break;
                case 8: // Terminating Descriptor
                    return true;
                }
            }

            return m_logicalVolumes.Count > 0 && m_partitions.Count > 0;
        }

        private void ParsePartitionDescriptor (byte[] buffer)
        {
            if (!CheckTag (buffer, 5))
                return;

            var partition = new UdfPartition
            {
                Number = LittleEndian.ToUInt16 (buffer, 22),
                Pos    = LittleEndian.ToUInt32 (buffer, 188),
                Len    = LittleEndian.ToUInt32 (buffer, 192)
            };

            m_partitions.Add (partition);
        }

        private void ParseLogicalVolumeDescriptor (byte[] buffer)
        {
            if (!CheckTag (buffer, 6))
                return;

            var vol = new UdfLogicalVolume
            {
                BlockSize = LittleEndian.ToUInt32 (buffer, 212)
            };

            // Parse File Set Descriptor location
            vol.FileSetLocation = new UdfLongAllocDesc();
            vol.FileSetLocation.Parse (buffer, 248);

            uint mapTableLen = LittleEndian.ToUInt32 (buffer, 264);
            uint numPartitionMaps = LittleEndian.ToUInt32 (buffer, 268);

            // Parse partition maps
            int pos = 440;
            for (uint i = 0; i < numPartitionMaps && pos < 440 + mapTableLen; i++)
            {
                byte type = buffer[pos    ];
                byte len  = buffer[pos + 1];

                var map = new UdfPartitionMap
                {
                    Type = type,
                    PartitionNumber = LittleEndian.ToUInt16 (buffer, pos + 4)
                };

                // Find corresponding partition
                foreach (var part in m_partitions)
                {
                    if (part.Number == map.PartitionNumber)
                    {
                        map.PartitionIndex = m_partitions.IndexOf (part);
                        break;
                    }
                }

                vol.PartitionMaps.Add (map);
                pos += len;
            }

            m_logicalVolumes.Add (vol);
        }

        private void ParsePrimaryVolumeDescriptor (byte[] buffer)
        {
            // We don't need much from PVD for basic file reading
        }

        public bool ReadFileSystem (List<Entry> entries)
        {
            if (m_logicalVolumes.Count == 0)
                return false;

            var vol = m_logicalVolumes[0];

            // Read File Set Descriptor
            var fsdBuffer = ReadLad (0, vol.FileSetLocation);
            if (fsdBuffer == null || !CheckTag (fsdBuffer, 256)) // File Set Descriptor
                return false;

            // Get root directory ICB
            var rootIcb = new UdfLongAllocDesc();
            rootIcb.Parse (fsdBuffer, 400);

            // Read root directory
            return ReadDirectory (0, rootIcb, "", entries);
        }

        private bool ReadDirectory (int volIndex, UdfLongAllocDesc icb, string path, List<Entry> entries)
        {
            var dirBuffer = ReadLad (volIndex, icb);
            if (dirBuffer == null)
                return false;

            ushort tagId = LittleEndian.ToUInt16 (dirBuffer, 0);
            if (tagId != 261 && tagId != 266) // File Entry or Extended File Entry
                return false;

            var item = ParseFileEntry (dirBuffer);
            if (item == null || !item.IsDir)
                return false;

            // Read directory contents
            var contentBuffer = ReadItemData (volIndex, item);
            if (contentBuffer == null)
                return false;

            int pos = 0;
            while (pos < contentBuffer.Length)
            {
                if (pos + 38 > contentBuffer.Length)
                    break;

                var fid = ParseFileIdentifier (contentBuffer, pos);
                if (fid == null)
                    break;

                pos += fid.TotalLength;

                if (fid.IsParent)
                    continue;

                string fullPath = string.IsNullOrEmpty (path) ? fid.Name : path + "/" + fid.Name;

                if (fid.IsDir)
                    ReadDirectory (volIndex, fid.Icb, fullPath, entries);
                else
                {
                    var fileBuffer = ReadLad (volIndex, fid.Icb);
                    if (fileBuffer != null)
                    {
                        var fileItem = ParseFileEntry (fileBuffer);
                        if (fileItem != null)
                        {
                            var entry = new UdfEntry
                            {
                                Name = fullPath,
                                Type = FormatCatalog.Instance.GetTypeFromName (fid.Name),
                                Size = (uint)fileItem.Size,
                                Offset = 0,
                                Extents = fileItem.Extents,
                                InlineData = fileItem.InlineData,
                                IsInline = fileItem.IsInline,
                                SecLogSize = m_secLogSize
                            };

                            // Add partition info to extents
                            var vol = m_logicalVolumes[volIndex];
                            foreach (var extent in entry.Extents)
                            {
                                if (extent.PartitionRef < vol.PartitionMaps.Count)
                                {
                                    var map = vol.PartitionMaps[(int)extent.PartitionRef];
                                    if (map.PartitionIndex >= 0 && map.PartitionIndex < m_partitions.Count)
                                    {
                                        extent.PartitionPos = m_partitions[map.PartitionIndex].Pos;
                                    }
                                }
                            }

                            entries.Add (entry);
                        }
                    }
                }
            }

            return true;
        }

        private UdfItem ParseFileEntry (byte[] buffer)
        {
            ushort tagId = LittleEndian.ToUInt16 (buffer, 0);
            if (!CheckTag (buffer, tagId))
                return null;

            bool isExtended = (tagId == 266);
            int offset = isExtended ? 40 : 0;

            var item = new UdfItem {
                IsExtended = isExtended
            };

            // Parse ICB tag
            byte fileType = buffer[11 + 16];
            item.IsDir = (fileType == 4); // Directory

            ushort flags = LittleEndian.ToUInt16 (buffer, 18 + 16);
            int allocType = flags & 3;

            item.Size = LittleEndian.ToUInt64 (buffer, 56 + offset);

            // Parse allocation descriptors
            uint allocDescLen = LittleEndian.ToUInt32 (buffer, 172 + offset);
            int allocPos = 176 + offset + LittleEndian.ToInt32 (buffer, 168 + offset); // Extended attributes length

            if (allocType == 3) // Inline
            {
                item.IsInline = true;
                item.InlineData = new byte[allocDescLen];
                Array.Copy (buffer, allocPos, item.InlineData, 0, allocDescLen);
            }
            else if (allocType == 0) // Short
            {
                for (uint i = 0; i < allocDescLen; i += 8)
                {
                    var extent = new UdfExtent
                    {
                        Len = LittleEndian.ToUInt32 (buffer, allocPos + (int)i),
                        Pos = LittleEndian.ToUInt32 (buffer, allocPos + (int)i + 4),
                        PartitionRef = 0 // Assuming first partition for short descs
                    };
                    item.Extents.Add (extent);
                }
            }
            else if (allocType == 1) // Long
            {
                for (uint i = 0; i < allocDescLen; i += 16)
                {
                    var extent = new UdfExtent
                    {
                        Len = LittleEndian.ToUInt32 (buffer, allocPos + (int)i),
                        Pos = LittleEndian.ToUInt32 (buffer, allocPos + (int)i + 4),
                        PartitionRef = LittleEndian.ToUInt16 (buffer, allocPos + (int)i + 8)
                    };
                    item.Extents.Add (extent);
                }
            }

            return item;
        }

        private UdfFileId ParseFileIdentifier (byte[] buffer, int offset)
        {
            if (offset + 38 > buffer.Length)
                return null;

            if (!CheckTag (buffer, 257, offset))
                return null;

            var fid = new UdfFileId {
                FileCharacteristics = buffer[offset + 18]
            };

            // Parse ICB
            fid.Icb = new UdfLongAllocDesc();
            fid.Icb.Parse (buffer, offset + 20);

            // Parse filename
            byte idLen = buffer[offset + 19];
            ushort impLen = LittleEndian.ToUInt16 (buffer, offset + 36);

            int nameOffset = offset + 38 + impLen;
            if (nameOffset + idLen > buffer.Length)
                return null;

            // Decode filename
            if (idLen > 0)
            {
                if (buffer[nameOffset] == 8)       // 8-bit characters
                    fid.Name = Encoding.UTF8.GetString (buffer, nameOffset + 1, idLen - 1);
                else if (buffer[nameOffset] == 16) // 16-bit characters
                    fid.Name = Encoding.BigEndianUnicode.GetString (buffer, nameOffset + 1, idLen - 1);
            }

            // Calculate total length (must be 4-byte aligned)
            fid.TotalLength = 38 + impLen + idLen;
            fid.TotalLength = (fid.TotalLength + 3) & ~3;

            return fid;
        }

        private byte[] ReadLad (int volIndex, UdfLongAllocDesc lad)
        {
            if (lad.Len == 0)
                return null;

            var vol = m_logicalVolumes[volIndex];
            if (lad.PartitionRef >= vol.PartitionMaps.Count)
                return null;

            var map = vol.PartitionMaps[lad.PartitionRef];
            if (map.PartitionIndex < 0 || map.PartitionIndex >= m_partitions.Count)
                return null;

            var partition = m_partitions[map.PartitionIndex];
            long offset = ((long)partition.Pos << m_secLogSize) + (long)lad.Pos * vol.BlockSize;

            if (offset + lad.GetLen() > m_file.MaxOffset)
                return null;

            return m_file.View.ReadBytes (offset, lad.GetLen());
        }

        private byte[] ReadItemData (int volIndex, UdfItem item)
        {
            if (item.IsInline)
                return item.InlineData;

            var result = new byte[item.Size];
            int pos = 0;

            var vol = m_logicalVolumes[volIndex];

            foreach (var extent in item.Extents)
            {
                uint len = extent.GetLen();
                if (!extent.IsRecAndAlloc() || len == 0)
                    continue;

                if (extent.PartitionRef >= vol.PartitionMaps.Count)
                    return null;

                var map = vol.PartitionMaps[(int)extent.PartitionRef];
                if (map.PartitionIndex < 0 || map.PartitionIndex >= m_partitions.Count)
                    return null;

                var partition = m_partitions[map.PartitionIndex];
                long offset = ((long)partition.Pos << m_secLogSize) + (long)extent.Pos * vol.BlockSize;

                if (offset + len > m_file.MaxOffset)
                    return null;

                m_file.View.Read (offset, result, pos, (uint)len);
                pos += (int)len;
            }

            return result;
        }

        private bool CheckTag (byte[] buffer, ushort expectedId, int offset = 0)
        {
            if (buffer.Length < offset + 16)
                return false;

            ushort tagId = LittleEndian.ToUInt16 (buffer, offset);
            if (tagId != expectedId)
                return false;

            // Verify checksum
            byte checksum = 0;
            for (int i = 0; i < 16; i++)
            {
                if (i != 4) // Skip checksum byte itself
                    checksum += buffer[offset + i];
            }

            return checksum == buffer[offset + 4];
        }

        #region Helper classes

        private class UdfPartition
        {
            public ushort Number { get; set; }
            public uint Pos { get; set; }
            public uint Len { get; set; }
        }

        private class UdfLogicalVolume
        {
            public uint BlockSize { get; set; }
            public UdfLongAllocDesc FileSetLocation { get; set; }
            public List<UdfPartitionMap> PartitionMaps { get; set; } = new List<UdfPartitionMap>();
        }

        private class UdfPartitionMap
        {
            public byte Type { get; set; }
            public ushort PartitionNumber { get; set; }
            public int PartitionIndex { get; set; } = -1;
        }

        private class UdfLongAllocDesc
        {
            public uint Len { get; set; }
            public uint Pos { get; set; }
            public ushort PartitionRef { get; set; }

            public void Parse (byte[] buffer, int offset)
            {
                Len = LittleEndian.ToUInt32 (buffer, offset);
                Pos = LittleEndian.ToUInt32 (buffer, offset + 4);
                PartitionRef = LittleEndian.ToUInt16 (buffer, offset + 8);
            }

            public uint GetLen() { return Len & 0x3FFFFFFF; }
        }

        private class UdfItem
        {
            public bool IsExtended { get; set; }
            public bool IsDir { get; set; }
            public bool IsInline { get; set; }
            public ulong Size { get; set; }
            public byte[] InlineData { get; set; }
            public List<UdfExtent> Extents { get; set; } = new List<UdfExtent>();
        }

        private class UdfFileId
        {
            public byte FileCharacteristics { get; set; }
            public string Name { get; set; }
            public UdfLongAllocDesc Icb { get; set; }
            public int TotalLength { get; set; }

            public bool IsDir => (FileCharacteristics & 2) != 0;
            public bool IsParent => (FileCharacteristics & 8) != 0;
        }

        #endregion
    }

    internal class UdfFileStream : Stream
    {
        private readonly ArcView m_file;
        private readonly UdfEntry m_entry;
        private long m_position;

        public UdfFileStream (ArcView file, UdfEntry entry)
        {
            m_file = file;
            m_entry = entry;
            m_position = 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => m_entry.Size;
        public override long Position
        {
            get => m_position;
            set => m_position = Math.Max (0, Math.Min (value, Length));
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            if (m_position >= Length)
                return 0;

            if (m_entry.IsInline)
            {
                int available = (int)Math.Min (count, m_entry.InlineData.Length - m_position);
                Array.Copy (m_entry.InlineData, m_position, buffer, offset, available);
                m_position += available;
                return available;
            }

            count = (int)Math.Min (count, Length - m_position);
            int totalRead = 0;
            long currentPos = m_position;

            foreach (var extent in m_entry.Extents)
            {
                uint extentLen = extent.GetLen();
                if (!extent.IsRecAndAlloc() || extentLen == 0)
                    continue;

                if (currentPos >= extentLen)
                {
                    currentPos -= extentLen;
                    continue;
                }

                uint sectorSize = (uint)(1 << m_entry.SecLogSize);
                long physicalOffset = ((long)extent.PartitionPos << m_entry.SecLogSize) + 
                                     (long)extent.Pos * sectorSize + currentPos;

                int toRead = (int)Math.Min (count - totalRead, extentLen - currentPos);

                using (var frame = m_file.CreateFrame())
                {
                    frame.Reserve (physicalOffset, (uint)toRead);
                    int read = frame.Read (physicalOffset, buffer, offset + totalRead, (uint)toRead);
                    totalRead += read;
                    m_position += read;

                    if (read < toRead)
                        break;
                }

                currentPos = 0;

                if (totalRead >= count)
                    break;
            }

            return totalRead;
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    Position = offset;
                    break;
                case SeekOrigin.Current:
                    Position = m_position + offset;
                    break;
                case SeekOrigin.End:
                    Position = Length + offset;
                    break;
            }
            return m_position;
        }

        public override void Flush () { }
        public override void SetLength (long value) { throw new NotSupportedException(); }
        public override void Write (byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }
}