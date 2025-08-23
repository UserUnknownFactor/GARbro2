using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Mokopro
{
    internal class NNNNMetaData : ImageMetaData
    {
        public ImageMetaData    BmpInfo;
        public MokoCrypt        Input;
    }

    internal class MokoCrypt
    {
        static readonly byte[] DefaultKey = new byte[] { 1, 0x23 };

        byte[]  m_input;
        int     m_unpacked_size;

        public MokoCrypt (Stream stream)
        {
            var header = new byte[8];
            if (8 != stream.Read (header, 0, 8) || !Binary.AsciiEqual (header, 0, "NNNN"))
                throw new InvalidFormatException();
            m_unpacked_size = LittleEndian.ToInt32 (header, 4);
            if (m_unpacked_size <= 0)
                throw new InvalidFormatException();
            m_input = new byte[stream.Length-8];
            stream.Read (m_input, 0, m_input.Length);
            Decrypt (m_input, DefaultKey);
        }

        public Stream UnpackStream ()
        {
            var lzss = new LzssStream (new MemoryStream (m_input));
            lzss.Config.FrameFill = 0x20;
            return lzss;
        }

        public byte[] UnpackBytes ()
        {
            using (var mem = new MemoryStream (m_input))
            using (var lzss = new LzssReader (mem, m_input.Length, m_unpacked_size))
            {
                lzss.FrameFill = 0x20;
                lzss.Unpack();
                return lzss.Data;
            }
        }

        public static void Decrypt (byte[] input, byte[] key)
        {
            for (int i = input.Length-2; i >= 0; --i)
            {
                input[i]   ^= (byte)(key[1] ^ input[i+1]);
                input[i+1] ^= (byte)(key[0] ^ input[i]);
            }
        }
    }

    [Export(typeof(ImageFormat))]
    public class NNNNBmpFormat : ImageFormat
    {
        public override string         Tag { get { return "BMP/NNNN"; } }
        public override string Description { get { return "Mokopro compressed bitmap"; } }
        public override uint     Signature { get { return 0x4E4E4E4E; } } // 'NNNN'
        public override bool      CanWrite { get { return false; } }

        public NNNNBmpFormat ()
        {
            Extensions = new string[0];
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var moko = new MokoCrypt (stream.AsStream);
            using (var lzss = moko.UnpackStream())
            using (var bmp = new BinaryStream (lzss, stream.Name))
            {
                var info = Bmp.ReadMetaData (bmp);
                if (null == info)
                    return null;
                return new NNNNMetaData
                {
                    Width   = info.Width,
                    Height  = info.Height,
                    BPP     = info.BPP,
                    BmpInfo = info,
                    Input   = moko,
                };
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (NNNNMetaData)info;
            using (var lzss = meta.Input.UnpackStream())
            using (var bmp = new BinaryStream (lzss, stream.Name))
                return Bmp.Read (bmp, meta.BmpInfo);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("NNNNFormat.Write not implemented");
        }
    }

    [Export(typeof(AudioFormat))]
    public class NNNNOggAudio : AudioFormat
    {
        public override string         Tag { get { return "OGG/NNNN"; } }
        public override string Description { get { return "Mokopro compressed audio"; } }
        public override uint     Signature { get { return 0x4E4E4E4E; } } // 'NNNN'

        public NNNNOggAudio ()
        {
            Extensions = new string[0];
        }

        public override SoundInput TryOpen (IBinaryStream stream)
        {
            var moko = new MokoCrypt (stream.AsStream);
            var ogg = moko.UnpackBytes();
            var output = new MemoryStream (ogg);
            try
            {
                return new OggInput (output);
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class NNNNOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/NNNN"; } }
        public override string Description { get { return "Mokopro compressed file"; } }
        public override uint     Signature { get { return 0x4E4E4E4E; } } // 'NNNN'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanWrite { get { return false; } }

        public NNNNOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            var name = Path.GetFileName (file.Name);
            var dir = new List<Entry> (1);
            var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
            entry.Offset = 0;
            entry.Size = (uint)file.MaxOffset;
            entry.UnpackedSize = file.View.ReadUInt32 (4);
            dir.Add (entry);
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var moko = new MokoCrypt (input);
            return moko.UnpackStream();
        }
    }
}
