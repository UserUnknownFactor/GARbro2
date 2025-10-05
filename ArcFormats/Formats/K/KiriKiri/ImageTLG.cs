// TLG5/6 decoder Copyright (C) 2000-2005  W.Dee and contributors
// C# port by morkt and others

using System;
using System.IO;
using System.ComponentModel.Composition;
using System.Windows.Media;
using GameRes.Utility;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.KiriKiri
{
    internal class TlgMetaData : ImageMetaData
    {
        public int Version;
        public int DataOffset;
    }

    static class TVP_Tables
    {
        public const int TLG6_W_BLOCK_SIZE = 8;
        public const int TLG6_H_BLOCK_SIZE = 8;
        public const int TLG6_GOLOMB_N_COUNT = 4;
        public const int TLG6_LeadingZeroTable_BITS = 12;
        public const int TLG6_LeadingZeroTable_SIZE = (1 << TLG6_LeadingZeroTable_BITS);
        public const int SLIDE_N = 4096;
        public const int SLIDE_M = 18 + 255;
        public const int GOLOMB_GIVE_UP_BYTES = 4;

        public static byte[] TVPTLG6LeadingZeroTable = new byte[TLG6_LeadingZeroTable_SIZE];
        public static sbyte[,] TVPTLG6GolombBitLengthTable = new sbyte[TLG6_GOLOMB_N_COUNT * 2 * 128, TLG6_GOLOMB_N_COUNT];

        static readonly short[,] TVPTLG6GolombCompressed = new short[TLG6_GOLOMB_N_COUNT, 9] {
            {3,7,15,27,63,108,223,448,130},
            {3,5,13,24,51,95,192,384,257},
            {2,5,12,21,39,86,155,320,384},
            {2,3,9,18,33,61,129,258,511}
            /* Tuned by W.Dee, 2004/03/25 */
        };

        static bool TVPTLG6GolombTableInit = false;

        static TVP_Tables()
        {
            TVPTLG6InitLeadingZeroTable();
            TVPTLG6InitGolombTable();
        }

        static void TVPTLG6InitLeadingZeroTable()
        {
            /* table which indicates first set bit position + 1. */
            for (int i = 0; i < TLG6_LeadingZeroTable_SIZE; i++)
            {
                int cnt = 0;
                int j;
                for (j = 1; j != TLG6_LeadingZeroTable_SIZE && 0 == (i & j); j <<= 1, cnt++) ;
                cnt++;
                if (j == TLG6_LeadingZeroTable_SIZE) cnt = 0;
                TVPTLG6LeadingZeroTable[i] = (byte)cnt;
            }
        }

        static void TVPTLG6InitGolombTable()
        {
            if (TVPTLG6GolombTableInit) return;

            for (int n = 0; n < TLG6_GOLOMB_N_COUNT; n++)
            {
                int a = 0;
                for (int i = 0; i < 9; i++)
                {
                    for (int j = 0; j < TVPTLG6GolombCompressed[n, i]; j++)
                        TVPTLG6GolombBitLengthTable[a++, n] = (sbyte)i;
                }
                if (a != TLG6_GOLOMB_N_COUNT * 2 * 128)
                    throw new Exception ("Invalid data initialization");
                /* (this is for compressed table data check) */
            }

            TVPTLG6GolombTableInit = true;
        }

        public static void EnsureInitialized()
        {
            // This will trigger the static constructor if not already called
            if (!TVPTLG6GolombTableInit)
                TVPTLG6InitGolombTable();
        }
    }

    [Export (typeof (ImageFormat))]
    public class TlgFormat : ImageFormat
    {
        public override string         Tag { get { return "TLG"; } }
        public override string Description { get { return "KiriKiri game engine image format"; } }
        public override uint     Signature { get { return  0x30474c54; } } // "TLG0"
        public override bool      CanWrite { get { return  true; } }

        public TlgFormat()
        {
            Extensions = new string[] { "tlg", "tlg5", "tlg6" };
            Signatures = new uint[] { 0x30474C54, 0x35474C54, 0x36474C54, 0x35474CAB, 0x584D4B4A };
            Settings = new[] { TlgVersion };
        }

        FixedSetSetting TlgVersion = new FixedSetSetting (Properties.Settings.Default)
        {
            Name = "TLGVersion",
            Text = "TLG Output Format",
            ValuesSet = new[] { "TLG6 (Golomb)", "TLG5 (LZSS)" },
        };

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x26);
            int offset = 0xf;
            if (!header.AsciiEqual ("TLG0.0\x00sds\x1a"))
                offset = 0;
            int version;
            if (!header.AsciiEqual (offset + 6, "\x00raw\x1a"))
                return null;
            if (0xAB == header[offset])
                header[offset] = (byte)'T';
            if (header.AsciiEqual (offset, "TLG6.0"))
                version = 6;
            else if (header.AsciiEqual (offset, "TLG5.0"))
                version = 5;
            else if (header.AsciiEqual (offset, "XXXYYY"))
            {
                version = 5;
                header[offset + 0x0C] ^= 0xAB;
                header[offset + 0x10] ^= 0xAC;
            }
            else if (header.AsciiEqual (offset, "XXXZZZ"))
            {
                version = 6;
                header[offset + 0x0F] ^= 0xAB;
                header[offset + 0x13] ^= 0xAC;
            }
            else if (header.AsciiEqual (offset, "JKMXE8"))
            {
                version = 5;
                header[offset + 0x0C] ^= 0x1A;
                header[offset + 0x10] ^= 0x1C;
            }
            else
                return null;
            int colors = header[offset + 11];
            if (6 == version)
            {
                if (1 != colors && 4 != colors && 3 != colors)
                    return null;
                if (header[offset + 12] != 0 || header[offset + 13] != 0 || header[offset + 14] != 0)
                    return null;
                offset += 15;
            }
            else
            {
                if (4 != colors && 3 != colors)
                    return null;
                offset += 12;
            }
            return new TlgMetaData
            {
                Width = header.ToUInt32 (offset),
                Height = header.ToUInt32 (offset + 4),
                BPP = colors * 8,
                Version = version,
                DataOffset = offset + 8,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (TlgMetaData)info;

            var image = ReadTlg (file, meta);

            int tail_size = (int)Math.Min (file.Length - file.Position, 512);
            if (tail_size > 8)
            {
                var tail = file.ReadBytes (tail_size);
                try
                {
                    var blended_image = ApplyTags (image, meta, tail);
                    if (null != blended_image)
                        return blended_image;
                }
                catch (FileNotFoundException X)
                {
                    Trace.WriteLine (string.Format ("{0}: {1}", X.Message, X.FileName), "[TlgFormat.Read]");
                }
                catch (Exception X)
                {
                    Trace.WriteLine (X.Message, "[TlgFormat.Read]");
                }
            }
            PixelFormat format = 32 == meta.BPP ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
            return ImageData.Create (meta, format, null, image, (int)meta.Width * 4);
        }

        public override void Write (Stream file, ImageData image)
        {
            string selectedFormat = TlgVersion.Get<string>();
            switch (selectedFormat)
            {
                case "TLG5 (LZSS)":
                    WriteTlg5 (file, image);
                    break;
                case "TLG6 (Golomb)":
                default:
                    WriteTlg6 (file, image);
                    break;
            }
        }

        private byte[] GetPixelData (ImageData image)
        {
            var bitmap = image.Bitmap;
            int stride = ((int)image.Width * 4 + 3) & ~3; // Align to 4 bytes
            var pixels = new byte[stride * (int)image.Height];

            // Convert to BGRA32 if necessary
            if (bitmap.Format != PixelFormats.Bgra32 && bitmap.Format != PixelFormats.Bgr32)
            {
                var converted = new FormatConvertedBitmap (bitmap, PixelFormats.Bgra32, null, 0);
                converted.CopyPixels (pixels, stride, 0);
            }
            else
                bitmap.CopyPixels (pixels, stride, 0);

            // If source was BGR32, set alpha to 255
            if (bitmap.Format == PixelFormats.Bgr32)
            {
                for (int i = 3; i < pixels.Length; i += 4)
                    pixels[i] = 255;
            }

            return pixels;
        }

        #region Writing TLG5
        public void WriteTlg5 (Stream file, ImageData image)
        {
            using (var writer = new BinaryWriter (file))
            {
                writer.Write (Encoding.ASCII.GetBytes ("TLG5.0\x00raw\x1a"));

                int colors = image.BPP == 32 ? 4 : 3;
                writer.Write ((byte)colors);

                writer.Write ((uint)image.Width);
                writer.Write ((uint)image.Height);

                int blockheight = 4;
                writer.Write (blockheight);

                int blockcount = ((int)image.Height - 1) / blockheight + 1;

                // Reserve space for block sizes
                long blockSizePos = file.Position;
                for (int i = 0; i < blockcount; i++)
                    writer.Write ((uint)0);

                var pixels = GetPixelData (image);
                int stride = ((int)image.Width * 4 + 3) & ~3;

                // Create LZSS compressor with persistent state
                var compressor = new SlideCompressor();
                var blockSizes = new List<uint>();

                var cmpinbuf = new byte[colors][];
                var cmpoutbuf = new byte[colors][];
                for (int i = 0; i < colors; i++)
                {
                    cmpinbuf[i] = new byte[image.Width * blockheight];
                    cmpoutbuf[i] = new byte[image.Width * blockheight * 9 / 4];
                }

                // Process each block
                for (int blk_y = 0; blk_y < image.Height; blk_y += blockheight)
                {
                    long blockStart = file.Position;
                    int ylim = Math.Min (blk_y + blockheight, (int)image.Height);

                    int inp = 0;

                    // Process each line in block
                    for (int y = blk_y; y < ylim; y++)
                    {
                        // Get scanlines
                        byte[] upper = null;
                        if (y != 0)
                        {
                            upper = new byte[image.Width * 4];
                            int upperLine = (y - 1) * stride;
                            Array.Copy (pixels, upperLine, upper, 0, image.Width * 4);
                        }

                        byte[] current = new byte[image.Width * 4];
                        int currentLine = y * stride;
                        Array.Copy (pixels, currentLine, current, 0, image.Width * 4);

                        // Prepare buffer with differences
                        int[] prevcl = new int[4];
                        int[] val = new int[4];

                        for (int c = 0; c < colors; c++)
                            prevcl[c] = 0;

                        for (int x = 0; x < image.Width; x++)
                        {
                            int idx = x * 4;

                            for (int c = 0; c < colors; c++)
                            {
                                int cl;
                                if (upper != null)
                                    cl = current[idx + c] - upper[idx + c];
                                else
                                    cl = current[idx + c];

                                val[c] = cl - prevcl[c];
                                prevcl[c] = cl;
                            }

                            // Composite colors
                            switch (colors)
                            {
                                case 1:
                                    cmpinbuf[0][inp] = (byte)val[0];
                                    break;
                                case 3:
                                    cmpinbuf[0][inp] = (byte)(val[0] - val[1]);
                                    cmpinbuf[1][inp] = (byte)val[1];
                                    cmpinbuf[2][inp] = (byte)(val[2] - val[1]);
                                    break;
                                case 4:
                                    cmpinbuf[0][inp] = (byte)(val[0] - val[1]);
                                    cmpinbuf[1][inp] = (byte)val[1];
                                    cmpinbuf[2][inp] = (byte)(val[2] - val[1]);
                                    cmpinbuf[3][inp] = (byte)val[3];
                                    break;
                            }

                            inp++;
                        }
                    }

                    // Compress buffer and write to file
                    for (int c = 0; c < colors; c++)
                    {
                        compressor.Store();
                        long wrote = 0;
                        compressor.Encode (cmpinbuf[c], inp, cmpoutbuf[c], ref wrote);

                        if (wrote < inp)
                        {
                            writer.Write ((byte)0); // Compressed
                            writer.Write ((int)wrote);
                            writer.Write (cmpoutbuf[c], 0, (int)wrote);
                        }
                        else
                        {
                            compressor.Restore();
                            writer.Write ((byte)1); // Raw
                            writer.Write (inp);
                            writer.Write (cmpinbuf[c], 0, inp);
                        }
                    }

                    uint blockSize = (uint)(file.Position - blockStart);
                    blockSizes.Add (blockSize);
                }

                // Write block sizes
                long endPos = file.Position;
                file.Position = blockSizePos;
                foreach (var size in blockSizes)
                    writer.Write (size);
                file.Position = endPos;
            }
        }

        class SlideCompressor
        {
            struct Chain
            {
                public int Prev;
                public int Next;
            }

            byte[] Text  = new byte[TVP_Tables.SLIDE_N + TVP_Tables.SLIDE_M - 1];
            int[] Map    =  new int[256 * 256];
            Chain[] Chains = new Chain[TVP_Tables.SLIDE_N];

            byte[] Text2 = new byte[TVP_Tables.SLIDE_N + TVP_Tables.SLIDE_M - 1];
            int[] Map2   =  new int[256 * 256];
            Chain[] Chains2 = new Chain[TVP_Tables.SLIDE_N];

            int S = 0;
            int S2 = 0;

            public SlideCompressor()
            {
                for (int i = 0; i < 256 * 256; i++)
                    Map[i] = -1;
                for (int i = 0; i < TVP_Tables.SLIDE_N; i++)
                {
                    Chains[i].Prev = -1;
                    Chains[i].Next = -1;
                }
                for (int i = TVP_Tables.SLIDE_N - 1; i >= 0; i--)
                    AddMap (i);
            }

            void AddMap (int p)
            {
                int place = Text[p] + (Text[(p + 1) & (TVP_Tables.SLIDE_N - 1)] << 8);

                if (Map[place] == -1)
                {
                    Map[place] = p;
                }
                else
                {
                    int old = Map[place];
                    Map[place] = p;
                    Chains[old].Prev = p;
                    Chains[p].Next = old;
                    Chains[p].Prev = -1;
                }
            }

            void DeleteMap (int p)
            {
                int n;
                if ((n = Chains[p].Next) != -1)
                    Chains[n].Prev = Chains[p].Prev;

                if ((n = Chains[p].Prev) != -1)
                {
                    Chains[n].Next = Chains[p].Next;
                }
                else if (Chains[p].Next != -1)
                {
                    int place = Text[p] + (Text[(p + 1) & (TVP_Tables.SLIDE_N - 1)] << 8);
                    Map[place] = Chains[p].Next;
                }
                else
                {
                    int place = Text[p] + (Text[(p + 1) & (TVP_Tables.SLIDE_N - 1)] << 8);
                    Map[place] = -1;
                }

                Chains[p].Prev = -1;
                Chains[p].Next = -1;
            }

            int GetMatch (byte[] cur, int curpos, int curlen, ref int pos, int s)
            {
                if (curlen < 3) return 0;

                int place = cur[curpos] + (cur[curpos + 1] << 8);

                int maxlen = 0;
                if ((place = Map[place]) != -1)
                {
                    int place_org;
                    curlen -= 1;
                    do
                    {
                        place_org = place;
                        if (s == place || s == ((place + 1) & (TVP_Tables.SLIDE_N - 1))) continue;
                        place += 2;
                        int lim = (TVP_Tables.SLIDE_M < curlen ? TVP_Tables.SLIDE_M : curlen) + place_org;
                        int c = 2;
                        if (lim >= TVP_Tables.SLIDE_N)
                        {
                            if (place_org <= s && s < TVP_Tables.SLIDE_N)
                                lim = s;
                            else if (s < (lim & (TVP_Tables.SLIDE_N - 1)))
                                lim = s + TVP_Tables.SLIDE_N;
                        }
                        else
                        {
                            if (place_org <= s && s < lim)
                                lim = s;
                        }
                        while (place < lim && Text[place] == cur[curpos + c])
                        {
                            place++;
                            c++;
                        }
                        int matchlen = place - place_org;
                        if (matchlen > maxlen)
                        {
                            pos = place_org;
                            maxlen = matchlen;
                        }
                        if (matchlen == TVP_Tables.SLIDE_M) return maxlen;

                    } while ((place = Chains[place_org].Next) != -1);
                }
                return maxlen;
            }

            public void Encode (byte[] input, int inlen, byte[] output, ref long outlen)
            {
                outlen = 0;
                if (inlen == 0) return;

                byte[] code = new byte[40];
                int codeptr = 1;
                byte mask = 1;
                code[0] = 0;

                int s = S;
                int inpos = 0;

                while (inlen > 0)
                {
                    int pos = 0;
                    int len = GetMatch (input, inpos, inlen, ref pos, s);
                    if (len >= 3)
                    {
                        code[0] |= mask;
                        if (len >= 18)
                        {
                            code[codeptr++] = (byte)(pos & 0xff);
                            code[codeptr++] = (byte)(((pos & 0xf00) >> 8) | 0xf0);
                            code[codeptr++] = (byte)(len - 18);
                        }
                        else
                        {
                            code[codeptr++] = (byte)(pos & 0xff);
                            code[codeptr++] = (byte)(((pos & 0xf00) >> 8) | ((len - 3) << 4));
                        }
                        while (len-- > 0)
                        {
                            byte c = input[inpos++];
                            DeleteMap ((s - 1) & (TVP_Tables.SLIDE_N - 1));
                            DeleteMap (s);
                            if (s < TVP_Tables.SLIDE_M - 1) Text[s + TVP_Tables.SLIDE_N] = c;
                            Text[s] = c;
                            AddMap ((s - 1) & (TVP_Tables.SLIDE_N - 1));
                            AddMap (s);
                            s++;
                            inlen--;
                            s &= (TVP_Tables.SLIDE_N - 1);
                        }
                    }
                    else
                    {
                        byte c = input[inpos++];
                        DeleteMap ((s - 1) & (TVP_Tables.SLIDE_N - 1));
                        DeleteMap (s);
                        if (s < TVP_Tables.SLIDE_M - 1) Text[s + TVP_Tables.SLIDE_N] = c;
                        Text[s] = c;
                        AddMap ((s - 1) & (TVP_Tables.SLIDE_N - 1));
                        AddMap (s);
                        s++;
                        inlen--;
                        s &= (TVP_Tables.SLIDE_N - 1);
                        code[codeptr++] = c;
                    }
                    mask <<= 1;

                    if (mask == 0)
                    {
                        for (int i = 0; i < codeptr; i++)
                            output[outlen++] = code[i];
                        mask = 1;
                        codeptr = 1;
                        code[0] = 0;
                    }
                }

                if (mask != 1)
                {
                    for (int i = 0; i < codeptr; i++)
                        output[outlen++] = code[i];
                }

                S = s;
            }

            public void Store()
            {
                S2 = S;
                Array.Copy (Text, Text2, TVP_Tables.SLIDE_N + TVP_Tables.SLIDE_M - 1);
                Array.Copy (Map, Map2, 256 * 256);
                Array.Copy (Chains, Chains2, TVP_Tables.SLIDE_N);
            }

            public void Restore()
            {
                S = S2;
                Array.Copy (Text2, Text, TVP_Tables.SLIDE_N + TVP_Tables.SLIDE_M - 1);
                Array.Copy (Map2, Map, 256 * 256);
                Array.Copy (Chains2, Chains, TVP_Tables.SLIDE_N);
            }
        }
        #endregion

        #region Writing TLG6

        void CompressValuesGolomb (TLG6BitStream bs, sbyte[] buf, int size)
        {
            bs.PutValue (buf[0] != 0 ? 1 : 0, 1); // initial value state

            int n = TVP_Tables.TLG6_GOLOMB_N_COUNT - 1;
            int a = 0;
            int count = 0;

            for (int i = 0; i < size; i++)
            {
                if (buf[i] != 0)
                {
                    // Write zero count
                    if (count > 0) bs.PutGamma (count);

                    // Count non-zero values
                    count = 0;
                    int ii;
                    for (ii = i; ii < size; ii++)
                    {
                        if (buf[ii] != 0) count++;
                        else break;
                    }

                    // Write non-zero count
                    bs.PutGamma (count);

                    // Write non-zero values
                    for (; i < ii; i++)
                    {
                        int e = buf[i];
                        int k = TVP_Tables.TVPTLG6GolombBitLengthTable[a, n];
                        int m = ((e >= 0) ? 2 * e : -2 * e - 1) - 1;
                        int store_limit = bs.GetBytePos() + TVP_Tables.GOLOMB_GIVE_UP_BYTES;
                        bool put1 = true;

                        for (int c = (m >> k); c > 0; c--)
                        {
                            if (store_limit == bs.GetBytePos())
                            {
                                bs.PutValue (m >> k, 8);
                                put1 = false;
                                break;
                            }
                            bs.Put1Bit (false);
                        }

                        if (store_limit == bs.GetBytePos())
                        {
                            bs.PutValue (m >> k, 8);
                            put1 = false;
                        }

                        if (put1) bs.Put1Bit (true);
                        bs.PutValue (m, k);
                        a += (m >> 1);

                        if (--n < 0)
                        {
                            a >>= 1;
                            n = TVP_Tables.TLG6_GOLOMB_N_COUNT - 1;
                        }
                    }

                    i = ii - 1;
                    count = 0;
                }
                else
                {
                    count++;
                }
            }

            if (count > 0) bs.PutGamma (count);
        }

        void ApplyColorFilter (sbyte[] bufb, sbyte[] bufg, sbyte[] bufr, int offset, int len, int code)
        {
            switch (code)
            {
                case 0: // No filter
                    break;
                case 1: // B-G, G, R-G
                    for (int d = 0; d < len; d++)
                    {
                        bufr[offset + d] -= bufg[offset + d];
                        bufb[offset + d] -= bufg[offset + d];
                    }
                    break;
                case 2: // B, G-B, R-B-G
                    for (int d = 0; d < len; d++)
                    {
                        bufr[offset + d] -= bufg[offset + d];
                        bufg[offset + d] -= bufb[offset + d];
                    }
                    break;
                case 3: // B-R-G, G-R, R
                    for (int d = 0; d < len; d++)
                    {
                        bufb[offset + d] -= bufg[offset + d];
                        bufg[offset + d] -= bufr[offset + d];
                    }
                    break;
                case 4: // B-R, G-B-R, R-B-R-G
                    for (int d = 0; d < len; d++)
                    {
                        bufr[offset + d] -= bufg[offset + d];
                        bufg[offset + d] -= bufb[offset + d];
                        bufb[offset + d] -= bufr[offset + d];
                    }
                    break;
                case 5: // B-R, G-B-R, R
                    for (int d = 0; d < len; d++)
                    {
                        bufg[offset + d] -= bufb[offset + d];
                        bufb[offset + d] -= bufr[offset + d];
                    }
                    break;
                case 6: // B-G, G, R
                    for (int d = 0; d < len; d++)
                    {
                        bufb[offset + d] -= bufg[offset + d];
                    }
                    break;
                case 7: // B, G-B, R
                    for (int d = 0; d < len; d++)
                    {
                        bufg[offset + d] -= bufb[offset + d];
                    }
                    break;
                case 8: // B, G, R-G
                    for (int d = 0; d < len; d++)
                    {
                        bufr[offset + d] -= bufg[offset + d];
                    }
                    break;
                case 9: // B-G-R-B, G-R-B, R-B
                    for (int d = 0; d < len; d++)
                    {
                        bufb[offset + d] -= bufg[offset + d];
                        bufg[offset + d] -= bufr[offset + d];
                        bufr[offset + d] -= bufb[offset + d];
                    }
                    break;
                case 10: // B-R, G-R, R
                    for (int d = 0; d < len; d++)
                    {
                        bufg[offset + d] -= bufr[offset + d];
                        bufb[offset + d] -= bufr[offset + d];
                    }
                    break;
                case 11: // B, G-B, R-B
                    for (int d = 0; d < len; d++)
                    {
                        bufr[offset + d] -= bufb[offset + d];
                        bufg[offset + d] -= bufb[offset + d];
                    }
                    break;
                case 12: // B, G-R-B, R-B
                    for (int d = 0; d < len; d++)
                    {
                        bufg[offset + d] -= bufr[offset + d];
                        bufr[offset + d] -= bufb[offset + d];
                    }
                    break;
                case 13: // B-G, G-R-B-G, R-B-G
                    for (int d = 0; d < len; d++)
                    {
                        bufg[offset + d] -= bufr[offset + d];
                        bufr[offset + d] -= bufb[offset + d];
                        bufb[offset + d] -= bufg[offset + d];
                    }
                    break;
                case 14: // B-G-R, G-R, R-B-G-R
                    for (int d = 0; d < len; d++)
                    {
                        bufr[offset + d] -= bufb[offset + d];
                        bufb[offset + d] -= bufg[offset + d];
                        bufg[offset + d] -= bufr[offset + d];
                    }
                    break;
                case 15: // B, G-(B<<1), R-(B<<1)
                    for (int d = 0; d < len; d++)
                    {
                        sbyte t = (sbyte)(bufb[offset + d] << 1);
                        bufr[offset + d] -= t;
                        bufg[offset + d] -= t;
                    }
                    break;
            }
        }

        class TLG6BitStream : IDisposable
        {
            private Stream OutStream;
            private int BufferBits = 0;
            private int BufferBitPos = 0;
            private long TotalBitsWritten = 0;

            public TLG6BitStream (Stream outstream)
            {
                OutStream = outstream;
                BufferBits = 0;
                BufferBitPos = 0;
            }

            public int GetBytePos() => (int)(TotalBitsWritten / 8);
            public long GetBitLength() => TotalBitsWritten;

            public void Put1Bit (bool b)
            {
                if (b)
                    BufferBits |= (1 << BufferBitPos);

                BufferBitPos++;
                TotalBitsWritten++;

                if (BufferBitPos == 8)
                {
                    OutStream.WriteByte ((byte)BufferBits);
                    BufferBits = 0;
                    BufferBitPos = 0;
                }
            }

            public void PutGamma (int v)
            {
                // Put a gamma code
                int t = v;
                t >>= 1;
                int cnt = 0;
                while (t != 0)
                {
                    Put1Bit (false);
                    t >>= 1;
                    cnt++;
                }
                Put1Bit (true);
                while (cnt-- > 0)
                {
                    Put1Bit ((v & 1) != 0);
                    v >>= 1;
                }
            }

            public void PutValue (int v, int len)
            {
                while (len-- > 0)
                {
                    Put1Bit ((v & 1) != 0);
                    v >>= 1;
                }
            }

            public void Flush()
            {
                if (BufferBitPos > 0)
                {
                    OutStream.WriteByte ((byte)BufferBits);
                    BufferBits = 0;
                    BufferBitPos = 0;
                }
            }

            public void Dispose()
            {
                Flush();
            }

            public static int GetGammaBitLength (int v)
            {
                if (v <= 1) return 1;
                if (v <= 3) return 3;
                if (v <= 7) return 5;
                if (v <= 15) return 7;
                if (v <= 31) return 9;
                if (v <= 63) return 11;
                if (v <= 127) return 13;
                if (v <= 255) return 15;
                if (v <= 511) return 17;

                int needbits = 1;
                v >>= 1;
                while (v != 0)
                {
                    needbits += 2;
                    v >>= 1;
                }
                return needbits;
            }
        }

        public void WriteTlg6 (Stream file, ImageData image)
        {
            TVP_Tables.EnsureInitialized();

            using (var writer = new BinaryWriter (file))
            {
                writer.Write (Encoding.ASCII.GetBytes ("TLG6.0\x00raw\x1a"));

                int colors = image.BPP == 32 ? 4 : 3;
                writer.Write ((byte)colors);
                writer.Write ((byte)0); // Data flag
                writer.Write ((byte)0); // Color type  
                writer.Write ((byte)0); // External golomb table

                writer.Write ((uint)image.Width);
                writer.Write ((uint)image.Height);

                var pixels = GetPixelData (image);
                int stride = ((int)image.Width * 4 + 3) & ~3;

                int x_block_count = ((int)image.Width - 1) / TVP_Tables.TLG6_W_BLOCK_SIZE + 1;
                int y_block_count = ((int)image.Height - 1) / TVP_Tables.TLG6_H_BLOCK_SIZE + 1;

                var buf = new byte[colors][];
                var block_buf = new sbyte[colors][];
                for (int c = 0; c < colors; c++)
                {
                    buf[c] = new byte[TVP_Tables.TLG6_W_BLOCK_SIZE * TVP_Tables.TLG6_H_BLOCK_SIZE * 3];
                    block_buf[c] = new sbyte[TVP_Tables.TLG6_H_BLOCK_SIZE * image.Width];
                }

                var filter_types = new byte[x_block_count * y_block_count];
                int fc = 0;
                long max_bit_length = 0;

                // Collect all compressed data first
                var compressedData = new MemoryStream();

                // Process each horizontal block row
                for (int y = 0; y < image.Height; y += TVP_Tables.TLG6_H_BLOCK_SIZE)
                {
                    int ylim = Math.Min (y + TVP_Tables.TLG6_H_BLOCK_SIZE, (int)image.Height);
                    int gwp = 0;
                    int xp = 0;

                    // Process each block in the row
                    for (int x = 0; x < image.Width; x += TVP_Tables.TLG6_W_BLOCK_SIZE, xp++)
                    {
                        int xlim = Math.Min (x + TVP_Tables.TLG6_W_BLOCK_SIZE, (int)image.Width);
                        int bw = xlim - x;

                        int minp = 0; // Use MED by default
                        int ft = 0;   // Use filter 0 by default

                        // Apply MED prediction
                        for (int c = 0; c < colors; c++)
                        {
                            int wp = 0;
                            for (int yy = y; yy < ylim; yy++)
                            {
                                for (int xx = x; xx < xlim; xx++)
                                {
                                    int pixIdx = yy * stride + xx * 4 + c;
                                    byte px = pixels[pixIdx];

                                    byte pa = (xx > 0) ? pixels[pixIdx - 4] : (byte)0;
                                    byte pb = (yy > 0) ? pixels[pixIdx - stride] : (byte)0;
                                    byte pc = (xx > 0 && yy > 0) ? pixels[pixIdx - stride - 4] : (byte)0;

                                    // MED prediction
                                    byte py;
                                    byte min_a_b = Math.Min (pa, pb);
                                    byte max_a_b = Math.Max (pa, pb);

                                    if (pc >= max_a_b)
                                        py = min_a_b;
                                    else if (pc < min_a_b)
                                        py = max_a_b;
                                    else
                                        py = (byte)(pa + pb - pc);

                                    buf[c][wp++] = (byte)(px - py);
                                }
                            }
                        }

                        // Reordering - zigzag pattern
                        int wp2 = 0;
                        for (int yy = y; yy < ylim; yy++)
                        {
                            int ofs = (yy - y) * bw;
                            bool dir = ((yy - y) & 1) != 0;

                            if ((xp & 1) != 0)
                            {
                                ofs = (ylim - yy - 1) * bw;
                                dir = !dir;
                            }

                            if (!dir)
                            {
                                // Forward
                                for (int xx = 0; xx < bw; xx++)
                                {
                                    for (int c = 0; c < colors; c++)
                                        block_buf[c][gwp + wp2] = (sbyte)buf[c][ofs + xx];
                                    wp2++;
                                }
                            }
                            else
                            {
                                // Backward
                                for (int xx = bw - 1; xx >= 0; xx--)
                                {
                                    for (int c = 0; c < colors; c++)
                                        block_buf[c][gwp + wp2] = (sbyte)buf[c][ofs + xx];
                                    wp2++;
                                }
                            }
                        }

                        filter_types[fc++] = (byte)((ft << 1) + minp);
                        gwp += wp2;
                    }

                    // Compress values for this block row
                    for (int c = 0; c < colors; c++)
                    {
                        var channelStream = new MemoryStream();
                        using (var bs = new TLG6BitStream (channelStream))
                        {
                            var channelData = new sbyte[gwp];
                            Array.Copy (block_buf[c], 0, channelData, 0, gwp);

                            CompressValuesGolomb (bs, channelData, gwp);
                            bs.Flush();

                            long bitLength = bs.GetBitLength();
                            max_bit_length = Math.Max (max_bit_length, bitLength);

                            // Write bit length and data to temporary stream
                            var tempWriter = new BinaryWriter (compressedData);
                            tempWriter.Write ((int)(bitLength & 0x3FFFFFFF));
                            tempWriter.Write (channelStream.ToArray());
                        }
                    }
                }

                // Write max bit length
                writer.Write ((int)max_bit_length);

                // Write filter types
                var compressor = new SlideCompressor();

                // Initialize compressor
                byte[] initCode = new byte[4096];
                int p = 0;
                for (int i = 0; i < 32; i++)
                {
                    for (int j = 0; j < 16; j++)
                    {
                        for (int k = 0; k < 8; k++)
                        {
                            initCode[p++] = (byte)((k & 1) == 1 ? j : i);
                        }
                    }
                }
                long dummy = 0;
                byte[] dummyOut = new byte[4096];
                compressor.Encode (initCode, 4096, dummyOut, ref dummy);

                // Compress filter types
                byte[] compressed_filters = new byte[filter_types.Length * 2];
                long filter_len = 0;
                compressor.Encode (filter_types, filter_types.Length, compressed_filters, ref filter_len);

                writer.Write ((int)filter_len);
                writer.Write (compressed_filters, 0, (int)filter_len);

                // Write all compressed data
                writer.Write (compressedData.ToArray());
            }
        }

        #endregion

        byte[] ReadTlg (IBinaryStream src, TlgMetaData info)
        {
            src.Position = info.DataOffset;
            if (6 == info.Version)
                return ReadV6 (src, info);
            else
                return ReadV5 (src, info);
        }

        ImageData ApplyTags (byte[] image, TlgMetaData meta, byte[] tail)
        {
            int i = tail.Length - 8;
            while (i >= 0)
            {
                if ('s' == tail[i + 3] && 'g' == tail[i + 2] && 'a' == tail[i + 1] && 't' == tail[i])
                    break;
                --i;
            }
            if (i < 0)
                return null;
            var tags = new TagsParser (tail, i + 4);
            if (!tags.Parse() || !tags.HasKey (1))
                return null;

            var base_name = tags.GetString (1);
            meta.OffsetX = tags.GetInt (2) & 0xFFFF;
            meta.OffsetY = tags.GetInt (3) & 0xFFFF;
            if (string.IsNullOrEmpty (base_name))
                return null;
            int method = 1;
            if (tags.HasKey (4))
                method = tags.GetInt (4);

            base_name = VFS.CombinePath (VFS.GetDirectoryName (meta.FileName), base_name);
            if (base_name == meta.FileName)
                return null;

            TlgMetaData base_info;
            byte[] base_image;
            using (var base_file = VFS.OpenBinaryStream (base_name))
            {
                base_info = ReadMetaData (base_file) as TlgMetaData;
                if (null == base_info)
                    return null;
                base_info.FileName = base_name;
                base_image = ReadTlg (base_file, base_info);
            }
            var pixels = BlendImage (base_image, base_info, image, meta, method);
            PixelFormat format = 32 == base_info.BPP ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
            return ImageData.Create (base_info, format, null, pixels, (int)base_info.Width * 4);
        }

        byte[] BlendImage(
            byte[] base_image, ImageMetaData base_info, byte[] overlay,
            ImageMetaData overlay_info, int method)
        {
            int dst_stride = (int)base_info.Width * 4;
            int src_stride = (int)overlay_info.Width * 4;
            int dst = overlay_info.OffsetY * dst_stride + overlay_info.OffsetX * 4;
            int src = 0;
            int gap = dst_stride - src_stride;

            for (uint y = 0; y < overlay_info.Height; ++y)
            {
                for (uint x = 0; x < overlay_info.Width; ++x)
                {
                    byte src_alpha = overlay[src + 3];

                    if (2 == method)
                    {
                        // XOR blending mode
                        base_image[dst] ^= overlay[src];
                        base_image[dst + 1] ^= overlay[src + 1];
                        base_image[dst + 2] ^= overlay[src + 2];
                        base_image[dst + 3] ^= src_alpha;
                    }
                    else if (src_alpha != 0)
                    {
                        byte dst_alpha = base_image[dst + 3];

                        if (0xFF == src_alpha || 0 == dst_alpha)
                        {
                            // Source is opaque or destination is transparent - simple copy
                            base_image[dst] = overlay[src];
                            base_image[dst + 1] = overlay[src + 1];
                            base_image[dst + 2] = overlay[src + 2];
                            base_image[dst + 3] = src_alpha;
                        }
                        else
                        {
                            // Proper alpha compositing using Porter-Duff "over" operator
                            float src_a = src_alpha / 255.0f;
                            float dst_a = dst_alpha / 255.0f;

                            // Calculate output alpha using the formula
                            float out_a = src_a + dst_a * (1.0f - src_a);
                            if (out_a > 0)
                            {
                                // Calculate output color components
                                for (int c = 0; c < 3; c++)
                                {
                                    float src_c = overlay[src + c] / 255.0f;
                                    float dst_c = base_image[dst + c] / 255.0f;

                                    // Apply "over" operator for color
                                    float out_c = (src_c * src_a + dst_c * dst_a * (1.0f - src_a)) / out_a;

                                    // Convert back to byte range
                                    base_image[dst + c] = (byte)(Math.Min (Math.Max (out_c * 255.0f, 0), 255));
                                }

                                // Set output alpha
                                base_image[dst + 3] = (byte)(Math.Min (Math.Max (out_a * 255.0f, 0), 255));
                            }
                            else
                            {
                                // Both alphas were effectively 0, result is transparent
                                base_image[dst] = 0;
                                base_image[dst + 1] = 0;
                                base_image[dst + 2] = 0;
                                base_image[dst + 3] = 0;
                            }
                        }
                    }
                    dst += 4;
                    src += 4;
                }
                dst += gap;
            }
            return base_image;
        }

        byte[] ReadV6 (IBinaryStream src, TlgMetaData info)
        {
            int width = (int)info.Width;
            int height = (int)info.Height;
            int colors = info.BPP / 8;
            int max_bit_length = src.ReadInt32();

            int x_block_count = ((width - 1) / TVP_Tables.TLG6_W_BLOCK_SIZE) + 1;
            int y_block_count = ((height - 1) / TVP_Tables.TLG6_H_BLOCK_SIZE) + 1;
            int main_count = width / TVP_Tables.TLG6_W_BLOCK_SIZE;
            int fraction = width - main_count * TVP_Tables.TLG6_W_BLOCK_SIZE;

            var image_bits = new uint[height * width];
            var bit_pool = new byte[max_bit_length / 8 + 5];
            var pixelbuf = new uint[width * TVP_Tables.TLG6_H_BLOCK_SIZE + 1];
            var filter_types = new byte[x_block_count * y_block_count];
            var zeroline = new uint[width];
            var LZSS_text = new byte[4096];

            // initialize zero line (virtual y=-1 line)
            uint zerocolor = 3 == colors ? 0xff000000 : 0x00000000;
            for (var i = 0; i < width; ++i)
                zeroline[i] = zerocolor;

            uint[] prevline = zeroline;
            int prevline_index = 0;

            // initialize LZSS text (used by chroma filter type codes)
            int p = 0;
            for (uint i = 0; i < 32 * 0x01010101; i += 0x01010101)
            for (uint j = 0; j < 16 * 0x01010101; j += 0x01010101)
            {
                LZSS_text[p++] = (byte)(i & 0xff);
                LZSS_text[p++] = (byte)(i >> 8 & 0xff);
                LZSS_text[p++] = (byte)(i >> 16 & 0xff);
                LZSS_text[p++] = (byte)(i >> 24 & 0xff);
                LZSS_text[p++] = (byte)(j & 0xff);
                LZSS_text[p++] = (byte)(j >> 8 & 0xff);
                LZSS_text[p++] = (byte)(j >> 16 & 0xff);
                LZSS_text[p++] = (byte)(j >> 24 & 0xff);
            }

            // read chroma filter types
            // they are compressed via LZSS as used by TLG5
            {
                int inbuf_size = src.ReadInt32();
                byte[] inbuf = src.ReadBytes (inbuf_size);
                if (inbuf_size != inbuf.Length)
                    return null;
                TVPTLG5DecompressSlide (filter_types, inbuf, inbuf_size, LZSS_text, 0);
            }

            // for each horizontal block group ...
            for (int y = 0; y < height; y += TVP_Tables.TLG6_H_BLOCK_SIZE)
            {
                int ylim = y + TVP_Tables.TLG6_H_BLOCK_SIZE;
                if (ylim >= height) ylim = height;

                int pixel_count = (ylim - y) * width;

                // decode values
                for (int c = 0; c < colors; c++)
                {
                    // read bit length
                    int bit_length = src.ReadInt32();

                    // get compress method
                    int method = (bit_length >> 30) & 3;
                    bit_length &= 0x3fffffff;

                    // compute byte length
                    int byte_length = bit_length / 8;
                    if (0 != (bit_length % 8)) byte_length++;

                    // read source from input
                    src.Read (bit_pool, 0, byte_length);

                    // decode values
                    // two most significant bits of bitlength are
                    // entropy coding method;
                    // 00 means Golomb method,
                    // 01 means Gamma method (not yet suppoted),
                    // 10 means modified LZSS method (not yet supported),
                    // 11 means raw (uncompressed) data (not yet supported).
                    switch (method)
                    {
                    case 0:
                        if (c == 0 && colors != 1)
                            TVPTLG6DecodeGolombValuesForFirst (pixelbuf, pixel_count, bit_pool);
                        else
                            TVPTLG6DecodeGolombValues (pixelbuf, c * 8, pixel_count, bit_pool);
                        break;
                    default:
                        throw new InvalidFormatException ("Unsupported entropy coding method");
                    }
                }

                // for each line
                int ft = (y / TVP_Tables.TLG6_H_BLOCK_SIZE) * x_block_count; // within filter_types
                int skipbytes = (ylim - y) * TVP_Tables.TLG6_W_BLOCK_SIZE;

                for (int yy = y; yy < ylim; yy++)
                {
                    int curline = yy * width;

                    int dir = (yy & 1) ^ 1;
                    int oddskip = ((ylim - yy - 1) - (yy - y));
                    if (0 != main_count)
                    {
                        int start =
                            ((width < TVP_Tables.TLG6_W_BLOCK_SIZE) ? width : TVP_Tables.TLG6_W_BLOCK_SIZE) *
                                (yy - y);
                        TVPTLG6DecodeLineGeneric(
                            prevline, prevline_index,
                            image_bits, curline,
                            width, 0, main_count,
                            filter_types, ft,
                            skipbytes,
                            pixelbuf, start,
                            zerocolor, oddskip, dir);
                    }

                    if (main_count != x_block_count)
                    {
                        int ww = fraction;
                        if (ww > TVP_Tables.TLG6_W_BLOCK_SIZE) ww = TVP_Tables.TLG6_W_BLOCK_SIZE;
                        int start = ww * (yy - y);
                        TVPTLG6DecodeLineGeneric(
                            prevline, prevline_index,
                            image_bits, curline,
                            width, main_count, x_block_count,
                            filter_types, ft,
                            skipbytes,
                            pixelbuf, start,
                            zerocolor, oddskip, dir);
                    }
                    prevline = image_bits;
                    prevline_index = curline;
                }
            }
            int stride = width * 4;
            var pixels = new byte[height * stride];
            Buffer.BlockCopy (image_bits, 0, pixels, 0, pixels.Length);
            return pixels;
        }

        byte[] ReadV5 (IBinaryStream src, TlgMetaData info)
        {
            int width = (int)info.Width;
            int height = (int)info.Height;
            int colors = info.BPP / 8;
            int blockheight = src.ReadInt32();
            int blockcount = (height - 1) / blockheight + 1;

            // skip block size section
            src.Seek (blockcount * 4, SeekOrigin.Current);

            int stride = width * 4;
            var image_bits = new byte[height * stride];
            var text = new byte[4096];
            for (int i = 0; i < 4096; ++i)
                text[i] = 0;

            var inbuf = new byte[blockheight * width + 10];
            byte[][] outbuf = new byte[4][];
            for (int i = 0; i < colors; i++)
                outbuf[i] = new byte[blockheight * width + 10];

            int z = 0;
            int prevline = -1;
            for (int y_blk = 0; y_blk < height; y_blk += blockheight)
            {
                // read file and decompress
                for (int c = 0; c < colors; c++)
                {
                    byte mark = src.ReadUInt8();
                    int size;
                    size = src.ReadInt32();
                    if (mark == 0)
                    {
                        // modified LZSS compressed data
                        if (size != src.Read (inbuf, 0, size))
                            return null;
                        z = TVPTLG5DecompressSlide (outbuf[c], inbuf, size, text, z);
                    }
                    else
                    {
                        // raw data
                        src.Read (outbuf[c], 0, size);
                    }
                }

                // compose colors and store
                int y_lim = y_blk + blockheight;
                if (y_lim > height) y_lim = height;
                int outbuf_pos = 0;
                for (int y = y_blk; y < y_lim; y++)
                {
                    int current = y * stride;
                    int current_org = current;
                    if (prevline >= 0)
                    {
                        // not first line
                        switch (colors)
                        {
                        case 3:
                            TVPTLG5ComposeColors3To4(
                                image_bits, current, prevline, outbuf, outbuf_pos, width);
                            break;
                        case 4:
                            TVPTLG5ComposeColors4To4(
                                image_bits, current, prevline, outbuf, outbuf_pos, width);
                            break;
                        }
                    }
                    else
                    {
                        // first line
                        switch (colors)
                        {
                        case 3:
                            for (int pr = 0, pg = 0, pb = 0, x = 0;
                                    x < width; x++)
                            {
                                int b = outbuf[0][outbuf_pos + x];
                                int g = outbuf[1][outbuf_pos + x];
                                int r = outbuf[2][outbuf_pos + x];
                                b += g; r += g;
                                image_bits[current++] = (byte)(pb += b);
                                image_bits[current++] = (byte)(pg += g);
                                image_bits[current++] = (byte)(pr += r);
                                image_bits[current++] = 0xff;
                            }
                            break;
                        case 4:
                            for (int pr = 0, pg = 0, pb = 0, pa = 0, x = 0;
                                    x < width; x++)
                            {
                                int b = outbuf[0][outbuf_pos + x];
                                int g = outbuf[1][outbuf_pos + x];
                                int r = outbuf[2][outbuf_pos + x];
                                int a = outbuf[3][outbuf_pos + x];
                                b += g; r += g;
                                image_bits[current++] = (byte)(pb += b);
                                image_bits[current++] = (byte)(pg += g);
                                image_bits[current++] = (byte)(pr += r);
                                image_bits[current++] = (byte)(pa += a);
                            }
                            break;
                        }
                    }
                    outbuf_pos += width;
                    prevline = current_org;
                }
            }
            return image_bits;
        }

        void TVPTLG5ComposeColors3To4 (byte[] outp, int outp_index, int upper,
                                       byte[][] buf, int bufpos, int width)
        {
            byte pc0 = 0, pc1 = 0, pc2 = 0;
            byte c0, c1, c2;
            for (int x = 0; x < width; x++)
            {
                c0 = buf[0][bufpos + x];
                c1 = buf[1][bufpos + x];
                c2 = buf[2][bufpos + x];
                c0 += c1; c2 += c1;
                outp[outp_index++] = (byte)(((pc0 += c0) + outp[upper + 0]) & 0xff);
                outp[outp_index++] = (byte)(((pc1 += c1) + outp[upper + 1]) & 0xff);
                outp[outp_index++] = (byte)(((pc2 += c2) + outp[upper + 2]) & 0xff);
                outp[outp_index++] = 0xff;
                upper += 4;
            }
        }

        void TVPTLG5ComposeColors4To4 (byte[] outp, int outp_index, int upper,
                                       byte[][] buf, int bufpos, int width)
        {
            byte pc0 = 0, pc1 = 0, pc2 = 0, pc3 = 0;
            byte c0, c1, c2, c3;
            for (int x = 0; x < width; x++)
            {
                c0 = buf[0][bufpos + x];
                c1 = buf[1][bufpos + x];
                c2 = buf[2][bufpos + x];
                c3 = buf[3][bufpos + x];
                c0 += c1; c2 += c1;
                outp[outp_index++] = (byte)(((pc0 += c0) + outp[upper + 0]) & 0xff);
                outp[outp_index++] = (byte)(((pc1 += c1) + outp[upper + 1]) & 0xff);
                outp[outp_index++] = (byte)(((pc2 += c2) + outp[upper + 2]) & 0xff);
                outp[outp_index++] = (byte)(((pc3 += c3) + outp[upper + 3]) & 0xff);
                upper += 4;
            }
        }

        int TVPTLG5DecompressSlide (byte[] outbuf, byte[] inbuf, int inbuf_size, byte[] text, int initialr)
        {
            int r = initialr;
            uint flags = 0;
            int o = 0;
            for (int i = 0; i < inbuf_size;)
            {
                if (((flags >>= 1) & 256) == 0)
                {
                    flags = (uint)(inbuf[i++] | 0xff00);
                }
                if (0 != (flags & 1))
                {
                    int mpos = inbuf[i] | ((inbuf[i + 1] & 0xf) << 8);
                    int mlen = (inbuf[i + 1] & 0xf0) >> 4;
                    i += 2;
                    mlen += 3;
                    if (mlen == 18) mlen += inbuf[i++];

                    while (0 != mlen--)
                    {
                        outbuf[o++] = text[r++] = text[mpos++];
                        mpos &= (4096 - 1);
                        r &= (4096 - 1);
                    }
                }
                else
                {
                    byte c = inbuf[i++];
                    outbuf[o++] = c;
                    text[r++] = c;
                    r &= (4096 - 1);
                }
            }
            return r;
        }

        static uint tvp_make_gt_mask (uint a, uint b)
        {
            uint tmp2 = ~b;
            uint tmp = ((a & tmp2) + (((a ^ tmp2) >> 1) & 0x7f7f7f7f)) & 0x80808080;
            tmp = ((tmp >> 7) + 0x7f7f7f7f) ^ 0x7f7f7f7f;
            return tmp;
        }

        static uint tvp_packed_bytes_add (uint a, uint b)
        {
            uint tmp = (uint)((((a & b) << 1) + ((a ^ b) & 0xfefefefe)) & 0x01010100);
            return a + b - tmp;
        }

        static uint tvp_med2 (uint a, uint b, uint c)
        {
            /* Median Edge Detector   thx, Mr. sugi at kirikiri.info */
            uint aa_gt_bb = tvp_make_gt_mask (a, b);
            uint a_xor_b_and_aa_gt_bb = ((a ^ b) & aa_gt_bb);
            uint aa = a_xor_b_and_aa_gt_bb ^ a;
            uint bb = a_xor_b_and_aa_gt_bb ^ b;
            uint n = tvp_make_gt_mask (c, bb);
            uint nn = tvp_make_gt_mask (aa, c);
            uint m = ~(n | nn);
            return (n & aa) | (nn & bb) | ((bb & m) - (c & m) + (aa & m));
        }

        static uint tvp_med (uint a, uint b, uint c, uint v)
        {
            return tvp_packed_bytes_add (tvp_med2 (a, b, c), v);
        }

        static uint tvp_avg (uint a, uint b, uint c, uint v)
        {
            return tvp_packed_bytes_add ((((a & b) + (((a ^ b) & 0xfefefefe) >> 1)) + ((a ^ b) & 0x01010101)), v);
        }

        delegate uint tvp_decoder (uint a, uint b, uint c, uint v);

        void TVPTLG6DecodeLineGeneric (uint[] prevline, int prevline_index,
                                       uint[] curline, int curline_index,
                                       int width, int start_block, int block_limit,
                                       byte[] filtertypes, int filtertypes_index,
                                       int skipblockbytes,
                                       uint[] inbuf, int inbuf_index,
                                       uint initialp, int oddskip, int dir)
        {
            /*
                chroma/luminosity decoding
                (this does reordering, color correlation filter, MED/AVG  at a time)
            */
            uint p, up;

            if (0 != start_block)
            {
                prevline_index += start_block * TVP_Tables.TLG6_W_BLOCK_SIZE;
                curline_index += start_block * TVP_Tables.TLG6_W_BLOCK_SIZE;
                p = curline[curline_index - 1];
                up = prevline[prevline_index - 1];
            }
            else
            {
                p = up = initialp;
            }

            inbuf_index += skipblockbytes * start_block;
            int step = 0 != (dir & 1) ? 1 : -1;

            for (int i = start_block; i < block_limit; i++)
            {
                int w = width - i * TVP_Tables.TLG6_W_BLOCK_SIZE;
                if (w > TVP_Tables.TLG6_W_BLOCK_SIZE) w = TVP_Tables.TLG6_W_BLOCK_SIZE;
                int ww = w;
                if (step == -1) inbuf_index += ww - 1;
                if (0 != (i & 1)) inbuf_index += oddskip * ww;

                tvp_decoder decoder;
                switch (filtertypes[filtertypes_index + i])
                {
                case 0:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, v);
                    break;
                case 1:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, v);
                    break;
                case 2:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+((v>>8)&0xff))<<16)) + (((v>>8)&0xff)<<8) + (0xff & ((v&0xff)+((v>>8)&0xff))) + ((v&0xff000000))));
                    break;
                case 3:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+((v>>8)&0xff))<<16)) + (((v>>8)&0xff)<<8) + (0xff & ((v&0xff)+((v>>8)&0xff))) + ((v&0xff000000))));
                    break;
                case 4:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff)+((v>>8)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+(v&0xff))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
                case 5:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff)+((v>>8)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+(v&0xff))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
                case 6:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v>>16)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>16)&0xff)+((v>>8)&0xff))) + ((v&0xff000000))));
                    break;
                case 7:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v>>16)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>16)&0xff)+((v>>8)&0xff))) + ((v&0xff000000))));
                    break;
                case 8:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff)+((v>>16)&0xff)+((v>>8)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+(v&0xff)+((v>>16)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>16)&0xff))) + ((v&0xff000000))));
                    break;
                case 9:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff)+((v>>16)&0xff)+((v>>8)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+(v&0xff)+((v>>16)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>16)&0xff))) + ((v&0xff000000))));
                    break;
                case 10:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+(v&0xff)+((v>>16)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>16)&0xff))) + ((v&0xff000000))));
                    break;
                case 11:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+(v&0xff)+((v>>16)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>16)&0xff))) + ((v&0xff000000))));
                    break;
                case 12:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>8)&0xff))) + ((v&0xff000000))));
                    break;
                case 13:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>8)&0xff))) + ((v&0xff000000))));
                    break;
                case 14:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+(v&0xff))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
                case 15:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+(v&0xff))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
                case 16:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+((v>>8)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
                case 17:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+((v>>8)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
                case 18:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v>>16)&0xff)+(v&0xff))<<8)) + (0xff & ((v&0xff)+((v>>8)&0xff)+((v>>16)&0xff)+(v&0xff))) + ((v&0xff000000))));
                    break;
                case 19:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v>>16)&0xff)+(v&0xff))<<8)) + (0xff & ((v&0xff)+((v>>8)&0xff)+((v>>16)&0xff)+(v&0xff))) + ((v&0xff000000))));
                    break;
                case 20:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v>>16)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>16)&0xff))) + ((v&0xff000000))));
                    break;
                case 21:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v>>16)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>16)&0xff))) + ((v&0xff000000))));
                    break;
                case 22:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+(v&0xff))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
                case 23:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+(v&0xff))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
                case 24:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v>>16)&0xff)+(v&0xff))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
                case 25:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v>>16)&0xff)+(v&0xff))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
                case 26:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff)+((v>>8)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v>>16)&0xff)+(v&0xff)+((v>>8)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>8)&0xff))) + ((v&0xff000000))));
                    break;
                case 27:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff)+((v>>8)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v>>16)&0xff)+(v&0xff)+((v>>8)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>8)&0xff))) + ((v&0xff000000))));
                    break;
                case 28:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff)+((v>>8)&0xff)+((v>>16)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v>>16)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>8)&0xff)+((v>>16)&0xff))) + ((v&0xff000000))));
                    break;
                case 29:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff)+((v>>8)&0xff)+((v>>16)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v>>16)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>8)&0xff)+((v>>16)&0xff))) + ((v&0xff000000))));
                    break;
                case 30:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+((v&0xff)<<1))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v&0xff)<<1))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
                case 31:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+((v&0xff)<<1))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v&0xff)<<1))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
                default: return;
                }
                do {
                    uint u = prevline[prevline_index];
                    p = decoder (p, u, up, inbuf[inbuf_index]);
                    up = u;
                    curline[curline_index] = p;
                    curline_index++;
                    prevline_index++;
                    inbuf_index += step;
                } while (0 != --w);
                if (step == 1)
                    inbuf_index += skipblockbytes - ww;
                else
                    inbuf_index += skipblockbytes + 1;
                if (0 != (i & 1)) inbuf_index -= oddskip * ww;
            }
        }

        void TVPTLG6DecodeGolombValuesForFirst (uint[] pixelbuf, int pixel_count, byte[] bit_pool)
        {
            /*
                Decode values packed in "bit_pool", values are coded using golomb code.

                "ForFirst" function do dword access to pixelbuf,
                clearing with zero except for blue (least siginificant byte).
            */
            int bit_pool_index = 0;

            int n = TVP_Tables.TLG6_GOLOMB_N_COUNT - 1; /* output counter */
            int a = 0; /* summary of absolute values of errors */

            int bit_pos = 1;
            bool zero = 0 == (bit_pool[bit_pool_index] & 1);

            for (int pixel = 0; pixel < pixel_count;)
            {
                /* get running count */
                int count;

                {
                    uint t = LittleEndian.ToUInt32 (bit_pool, bit_pool_index) >> bit_pos;
                    int b = TVP_Tables.TVPTLG6LeadingZeroTable[t & (TVP_Tables.TLG6_LeadingZeroTable_SIZE - 1)];
                    int bit_count = b;
                    while (0 == b)
                    {
                        bit_count += TVP_Tables.TLG6_LeadingZeroTable_BITS;
                        bit_pos += TVP_Tables.TLG6_LeadingZeroTable_BITS;
                        bit_pool_index += bit_pos >> 3;
                        bit_pos &= 7;
                        t = LittleEndian.ToUInt32 (bit_pool, bit_pool_index) >> bit_pos;
                        b = TVP_Tables.TVPTLG6LeadingZeroTable[t & (TVP_Tables.TLG6_LeadingZeroTable_SIZE - 1)];
                        bit_count += b;
                    }
                    bit_pos += b;
                    bit_pool_index += bit_pos >> 3;
                    bit_pos &= 7;

                    bit_count--;
                    count = 1 << bit_count;
                    count += ((LittleEndian.ToInt32 (bit_pool, bit_pool_index) >> (bit_pos)) & (count - 1));

                    bit_pos += bit_count;
                    bit_pool_index += bit_pos >> 3;
                    bit_pos &= 7;
                }
                if (zero)
                {
                    /* zero values: fill distination with zero */
                    do { pixelbuf[pixel++] = 0; } while (0 != --count);

                    zero = !zero;
                }
                else
                {
                    /* non-zero values: fill distination with glomb code */
                    do
                    {
                        int k = TVP_Tables.TVPTLG6GolombBitLengthTable[a, n];
                        int v, sign;

                        uint t = LittleEndian.ToUInt32 (bit_pool, bit_pool_index) >> bit_pos;
                        int bit_count;
                        int b;
                        if (0 != t)
                        {
                            b = TVP_Tables.TVPTLG6LeadingZeroTable[t & (TVP_Tables.TLG6_LeadingZeroTable_SIZE - 1)];
                            bit_count = b;
                            while (0 == b)
                            {
                                bit_count += TVP_Tables.TLG6_LeadingZeroTable_BITS;
                                bit_pos += TVP_Tables.TLG6_LeadingZeroTable_BITS;
                                bit_pool_index += bit_pos >> 3;
                                bit_pos &= 7;
                                t = LittleEndian.ToUInt32 (bit_pool, bit_pool_index) >> bit_pos;
                                b = TVP_Tables.TVPTLG6LeadingZeroTable[t & (TVP_Tables.TLG6_LeadingZeroTable_SIZE - 1)];
                                bit_count += b;
                            }
                            bit_count--;
                        }
                        else
                        {
                            bit_pool_index += 5;
                            bit_count = bit_pool[bit_pool_index - 1];
                            bit_pos = 0;
                            t = LittleEndian.ToUInt32 (bit_pool, bit_pool_index);
                            b = 0;
                        }

                        v = (int)((bit_count << k) + ((t >> b) & ((1<<k)-1)));
                        sign = (v & 1) - 1;
                        v >>= 1;
                        a += v;
                        pixelbuf[pixel++] = (byte)((v ^ sign) + sign + 1);

                        bit_pos += b;
                        bit_pos += k;
                        bit_pool_index += bit_pos >> 3;
                        bit_pos &= 7;

                        if (--n < 0)
                        {
                            a >>= 1;
                            n = TVP_Tables.TLG6_GOLOMB_N_COUNT - 1;
                        }
                    } while (0 != --count);
                    zero = !zero;
                }
            }
        }

        void TVPTLG6DecodeGolombValues (uint[] pixelbuf, int offset, int pixel_count, byte[] bit_pool)
        {
            /*
                Decode values packed in "bit_pool", values are coded using golomb code.
            */
            uint mask = (uint)~(0xff << offset);
            int bit_pool_index = 0;

            int n = TVP_Tables.TLG6_GOLOMB_N_COUNT - 1; /* output counter */
            int a = 0; /* summary of absolute values of errors */

            int bit_pos = 1;
            bool zero = 0 == (bit_pool[bit_pool_index] & 1);

            for (int pixel = 0; pixel < pixel_count;)
            {
                /* get running count */
                int count;

                {
                    uint t = LittleEndian.ToUInt32 (bit_pool, bit_pool_index) >> bit_pos;
                    int b = TVP_Tables.TVPTLG6LeadingZeroTable[t & (TVP_Tables.TLG6_LeadingZeroTable_SIZE - 1)];
                    int bit_count = b;
                    while (0 == b)
                    {
                        bit_count += TVP_Tables.TLG6_LeadingZeroTable_BITS;
                        bit_pos += TVP_Tables.TLG6_LeadingZeroTable_BITS;
                        bit_pool_index += bit_pos >> 3;
                        bit_pos &= 7;
                        t = LittleEndian.ToUInt32 (bit_pool, bit_pool_index) >> bit_pos;
                        b = TVP_Tables.TVPTLG6LeadingZeroTable[t & (TVP_Tables.TLG6_LeadingZeroTable_SIZE - 1)];
                        bit_count += b;
                    }
                    bit_pos += b;
                    bit_pool_index += bit_pos >> 3;
                    bit_pos &= 7;

                    bit_count--;
                    count = 1 << bit_count;
                    count += (int)((LittleEndian.ToUInt32 (bit_pool, bit_pool_index) >> (bit_pos)) & (count - 1));

                    bit_pos += bit_count;
                    bit_pool_index += bit_pos >> 3;
                    bit_pos &= 7;
                }
                if (zero)
                {
                    /* zero values: fill distination with zero */
                    do { pixelbuf[pixel++] &= mask; } while (0 != --count);

                    zero = !zero;
                }
                else
                {
                    /* non-zero values: fill distination with glomb code */
                    do
                    {
                        int k = TVP_Tables.TVPTLG6GolombBitLengthTable[a, n];
                        int v, sign;

                        uint t = LittleEndian.ToUInt32 (bit_pool, bit_pool_index) >> bit_pos;
                        int bit_count;
                        int b;
                        if (0 != t)
                        {
                            b = TVP_Tables.TVPTLG6LeadingZeroTable[t & (TVP_Tables.TLG6_LeadingZeroTable_SIZE - 1)];
                            bit_count = b;
                            while (0 == b)
                            {
                                bit_count += TVP_Tables.TLG6_LeadingZeroTable_BITS;
                                bit_pos += TVP_Tables.TLG6_LeadingZeroTable_BITS;
                                bit_pool_index += bit_pos >> 3;
                                bit_pos &= 7;
                                t = LittleEndian.ToUInt32 (bit_pool, bit_pool_index) >> bit_pos;
                                b = TVP_Tables.TVPTLG6LeadingZeroTable[t & (TVP_Tables.TLG6_LeadingZeroTable_SIZE - 1)];
                                bit_count += b;
                            }
                            bit_count--;
                        }
                        else
                        {
                            bit_pool_index += 5;
                            bit_count = bit_pool[bit_pool_index - 1];
                            bit_pos = 0;
                            t = LittleEndian.ToUInt32 (bit_pool, bit_pool_index);
                            b = 0;
                        }

                        v = (int)((bit_count << k) + ((t >> b) & ((1 << k) - 1)));
                        sign = (v & 1) - 1;
                        v >>= 1;
                        a += v;
                        uint c = (uint)((pixelbuf[pixel] & mask) | (uint)((byte)((v ^ sign) + sign + 1) << offset));
                        pixelbuf[pixel++] = c;

                        bit_pos += b;
                        bit_pos += k;
                        bit_pool_index += bit_pos >> 3;
                        bit_pos &= 7;

                        if (--n < 0)
                        {
                            a >>= 1;
                            n = TVP_Tables.TLG6_GOLOMB_N_COUNT - 1;
                        }
                    } while (0 != --count);
                    zero = !zero;
                }
            }
        }
    }

    internal class TagsParser
    {
        byte[] m_tags;
        Dictionary<int, Tuple<int, int>> m_map = new Dictionary<int, Tuple<int, int>>();
        int m_offset;

        public TagsParser (byte[] tags, int offset)
        {
            m_tags = tags;
            m_offset = offset;
        }

        public bool Parse()
        {
            int length = LittleEndian.ToInt32 (m_tags, m_offset);
            m_offset += 4;
            if (length <= 0 || length > m_tags.Length - m_offset)
                return false;
            while (m_offset < m_tags.Length)
            {
                int key_len = ParseInt();
                if (key_len < 0)
                    return false;
                int key;
                switch (key_len)
                {
                case 1:
                    key = m_tags[m_offset];
                    break;
                case 2:
                    key = LittleEndian.ToUInt16 (m_tags, m_offset);
                    break;
                case 4:
                    key = LittleEndian.ToInt32 (m_tags, m_offset);
                    break;
                default:
                    return false;
                }
                m_offset += key_len + 1;
                int value_len = ParseInt();
                if (value_len < 0)
                    return false;
                m_map[key] = Tuple.Create (m_offset, value_len);
                m_offset += value_len + 1;
            }
            return m_map.Count > 0;
        }

        int ParseInt ()
        {
            int colon = Array.IndexOf (m_tags, (byte)':', m_offset);
            if (-1 == colon)
                return -1;
            var len_str = Encoding.ASCII.GetString (m_tags, m_offset, colon - m_offset);
            m_offset = colon + 1;
            return Int32.Parse (len_str);
        }

        public bool HasKey (int key)
        {
            return m_map.ContainsKey (key);
        }

        public int GetInt (int key)
        {
            if (!m_map.ContainsKey (key))
                return 0;
            var val = m_map[key];
            switch (val.Item2)
            {
            case 0: return 0;
            case 1: return m_tags[val.Item1];
            case 2: return LittleEndian.ToUInt16 (m_tags, val.Item1);
            case 4: return LittleEndian.ToInt32 (m_tags, val.Item1);
            default: throw new InvalidFormatException();
            }
        }

        public string GetString (int key)
        {
            if (!m_map.ContainsKey (key))
                return null;
            var val = m_map[key];
            return Encodings.cp932.GetString (m_tags, val.Item1, val.Item2);
        }
    }
}
