using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using GameRes.Utility;
using GameRes.Formats.NonColor;

namespace GameRes.Formats.Minato
{
    [Serializable]
    public class NcSchemeCrc32 : Scheme
    {
        public NcSchemeCrc32 (string title) : base (title) { }

        public override ulong ComputeHash (byte[] name)
        {
            var bytes = name.Clone() as byte[];
            bytes.ToLowerShiftJis();
            return Crc32.Compute (bytes, 0, bytes.Length);
        }
    }

    /// <summary>
    /// This format is mostly identical to NonColor.DatOpener, but uses CRC32 for hashes and big-endian
    /// byte order.
    /// </summary>
    [Export(typeof(ArchiveFormat))]
    public class MinatoDatOpener : NonColor.DatOpener
    {
        public override string         Tag { get { return "DAT/MINATO"; } }
        public override string Description { get { return "MinatoSoft resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".dat"))
                return null;
            int count = Binary.BigEndian (file.View.ReadInt32 (0)) ^ SignatureKey;
            if (!IsSaneCount (count))
                return null;

            var scheme = QueryScheme (file.Name);
            if (null == scheme)
                return null;

            using (var index = new MinatoIndexReader (file, count))
                return index.Read (this, scheme);
        }
    }

    internal class MinatoIndexReader : NcIndexReaderBase
    {
        public MinatoIndexReader (ArcView file, int count) : base (file, count)
        {
            ExtendByteSign = true;
        }

        protected override ArcDatEntry ReadEntry ()
        {
            uint key            = Binary.BigEndian (m_input.ReadUInt32());
            int  flags          = m_input.ReadUInt8() ^ (byte)key;
            uint offset         = Binary.BigEndian (m_input.ReadUInt32()) ^ key;
            uint packed_size    = Binary.BigEndian (m_input.ReadUInt32()) ^ key;
            uint unpacked_size  = Binary.BigEndian (m_input.ReadUInt32()) ^ key;
            return new ArcDatEntry {
                Hash   = key,
                Flags  = flags,
                Offset = offset,
                Size   = packed_size,
                UnpackedSize = unpacked_size,
                IsPacked = 0 != (flags & 2),
            };
        }
    }
}
