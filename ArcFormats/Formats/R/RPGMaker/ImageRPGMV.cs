using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameRes.Formats.RPGMaker
{
    internal class RpgmvpMetaData : ImageMetaData
    {
        public byte[]   Key;
        public ImageFormat Format;
    }

    [Export(typeof(ImageFormat))]
    public class RpgmvpFormat : ImageFormat
    {
        public override string         Tag { get { return "RPGMVP"; } }
        public override string Description { get { return "RPG Maker MV/MZ engine image format"; } }
        public override uint     Signature { get { return  0x4D475052; } } // 'RPGMV'

        public RpgmvpFormat ()
        {
            Extensions = new string[] { "rpgmvp", "png_" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (5);
            if (header[4] != 'V')
                return null;

            var key = RpgmvDecryptor.LastKey;
            System.Tuple<ImageFormat, ImageMetaData> im_format = null;

            if (key != null)
            {
                // we have already viewed some images
                file.Position = 0;
                using (var image = RpgmvDecryptor.DecryptStream (file, key, true))
                {
                    im_format = ImageFormat.FindFormat (image); // there can be webp and jpg too
                    if (im_format == null)
                        key = null; // bad key - maybe we're checking another game?
                }
            }
            if (key == null)
            {
                // first time viewing
                key = RpgmvDecryptor.FindKeyFor (file.Name);
                if (key == null)
                {
                    RpgmvDecryptor.LastKey = null;
                    return null; // not a known format
                }
            }

            file.Position = 0;
            using (var image = RpgmvDecryptor.DecryptStream (file, key, true))
            {
                if (im_format == null)
                {
                    im_format = ImageFormat.FindFormat (image);
                    if (im_format == null)
                    {
                        RpgmvDecryptor.LastKey = null;
                        return null;
                    }
                }

                var info = im_format.Item2;
                if (null == info)
                    return null;

                RpgmvDecryptor.LastKey = key;

                return new RpgmvpMetaData {
                    Width = info.Width,
                    Height = info.Height,
                    OffsetX = info.OffsetX,
                    OffsetY = info.OffsetY,
                    BPP = info.BPP,
                    Key = key,
                    Format = im_format.Item1
                };
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (RpgmvpMetaData)info;
            using (var image = RpgmvDecryptor.DecryptStream (file, meta.Key, true))
                return meta.Format.Read (image, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            byte[] key = RpgmvDecryptor.LastKey ?? RpgmvDecryptor.DefaultKey;

            file.Write(RpgmvDecryptor.DefaultHeader, 0, RpgmvDecryptor.DefaultHeader.Length);

            using (var pngStream = new MemoryStream())
            {
                Png.Write (pngStream, image);
                pngStream.Position = 0;

                var pngHeader = new byte[key.Length];
                pngStream.Read (pngHeader, 0, pngHeader.Length);

                for (int i = 0; i < key.Length; ++i)
                    pngHeader[i] ^= key[i];

                file.Write (pngHeader, 0, pngHeader.Length);

                pngStream.CopyTo (file);
            }
        }
    }

    internal class RpgmvDecryptor
    {
        public static IBinaryStream DecryptStream (IBinaryStream input, byte[] key, bool leave_open = false)
        {
            input.Position = 0x10;
            var header = input.ReadBytes (key.Length);
            for (int i = 0; i < key.Length; ++i)
                header[i] ^= key[i];
            var result = new PrefixStream (header, new StreamRegion (input.AsStream, input.Position, leave_open));
            return new BinaryStream (result, input.Name);
        }

        static byte[] GetKeyFromString (string hex)
        {
            if ((hex.Length & 1) != 0)
                throw new System.ArgumentException ("invalid key string");

            var key = new byte[hex.Length/2];
            for (int i = 0; i < key.Length; ++i)
                key[i] = (byte)(HexToInt (hex[i * 2]) << 4 | HexToInt (hex[i * 2 + 1]));
            return key;
        }

        static int HexToInt (char x)
        {
            if (char.IsDigit (x))
                return x - '0';
            else
                return char.ToUpper (x) - 'A' + 10;
        }

        static byte[] ParseSystemJson (Stream input)
        {
            input.Position = 0;
            using (var reader = new StreamReader (input, Encoding.UTF8))
            {
                try
                {
                    var sys = JObject.Parse (reader.ReadToEnd());
                    var key = sys["encryptionKey"]?.Value<string>();
                    if (null == key)
                        return null;
                    return GetKeyFromString (key);
                }
                catch (JsonException)
                {
                    return null;
                }
            }
        }

        public static byte[] FindKeyFor (string filename)
        {
            foreach (var system_filename in SystemJsonLocations())
            {
                // Search up the directory tree
                var currentDir = VFS.GetDirectoryName (filename);
                bool doRoot = false;

                while (!string.IsNullOrEmpty (currentDir) || doRoot)
                {
                    var path = VFS.CombinePath (currentDir, system_filename);
                    try
                    {
                        var entry = VFS.FindFileInHierarchy (path);
                        if (entry != null)
                        {
                            using (var input = VFS.OpenStreamInHierarchy (entry))
                            {
                                var key = ParseSystemJson (input);
                                if (key != null)
                                    return key;
                            }
                        }
                    }
                    catch (FileNotFoundException) { }

                    if (doRoot) break;

                    var newDir = VFS.GetDirectoryName (currentDir);
                    if ((string.IsNullOrEmpty (newDir) && 
                            !string.IsNullOrEmpty (currentDir))
                            || newDir == currentDir)
                        doRoot = true; // the game can be at the root level
                    currentDir = newDir;
                }
            }

            return null;
        }

        static IEnumerable<string> SystemJsonLocations ()
        {
            yield return @"data\System.json";
            yield return @"www\data\System.json";
        }

        internal static readonly byte[] DefaultKey = {
            0x77, 0x4E, 0x46, 0x45, 0xFC, 0x43, 0x2F, 0x71, 0x47, 0x95, 0xA2, 0x43, 0xE5, 0x10, 0x13, 0xD8
        };

        internal static readonly byte[] DefaultHeader = {
            0x52, 0x50, 0x47, 0x4D, 0x56, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };

        internal static byte[] LastKey = null;
    }
}