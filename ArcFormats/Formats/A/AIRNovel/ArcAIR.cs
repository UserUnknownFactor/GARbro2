using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using GameRes.Cryptography;
using GameRes.Formats.PkWare;

using SharpZip = ICSharpCode.SharpZipLib.Zip;

namespace GameRes.Formats.AirNovel
{
    internal class AirEntry : ZipEntry
    {
        public AirEntry (SharpZip.ZipEntry zip_entry) : base (zip_entry)
        {
            IsEncrypted = Name.EndsWith ("_");
            if (IsEncrypted)
            {
                Name = Name.Substring (0, Name.Length-1);
                Type = FormatCatalog.Instance.GetTypeFromName (Name);
            }
        }
    }

    internal class AirArchive : PkZipArchive
    {
        public byte[]   Key;
        public uint     CoderLength;

        public AirArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, SharpZip.ZipFile native, byte[] key) : base (arc, impl, dir, native)
        {
            Key = key;
        }
    }

    [Serializable]
    public class AirNovelScheme : ResourceScheme
    {
        public IDictionary<string, string>  KnownKeys;
    }

    [Export(typeof(ArchiveFormat))]
    public class AirOpener : ArchiveFormat
    {
        public override string         Tag { get { return "AIR"; } }
        public override string Description { get { return "Adobe AIR resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        static readonly ResourceInstance<ArchiveFormat> Zip = new ResourceInstance<ArchiveFormat> ("ZIP");

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".air"))
                return null;
            var input = file.CreateStream();
            SharpZip.ZipFile zip = null;
            try
            {
                zip = new SharpZip.ZipFile (input);
                zip.StringCodec = SharpZip.StringCodec.FromEncoding(Encoding.UTF8);
                var files = zip.Cast<SharpZip.ZipEntry>().Where (z => !z.IsDirectory);
                bool has_encrypted = false;
                var dir = new List<Entry>();
                foreach (var f in files)
                {
                    var entry = new AirEntry (f);
                    has_encrypted |= entry.IsEncrypted;
                    dir.Add (entry);
                }
                if (has_encrypted)
                {
                    uint coder_length;
                    if (FindAirNovelCoderLength (zip, out coder_length))
                    {
                        var key = QueryEncryptionKey (file);
                        if (!string.IsNullOrEmpty (key))
                        {
                            var rc4_key = AirRc4Crypt.GenerateKey (key);
                            return new AirArchive (file, this, dir, zip, rc4_key) { CoderLength = coder_length };
                        }
                    }
                }
                return new PkZipArchive (file, Zip.Value, dir, zip);
            }
            catch
            {
                if (zip != null)
                    zip.Close();
                input.Dispose();
                throw;
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var zarc = (AirArchive)arc;
            var zent = (AirEntry)entry;
            var input = zarc.Native.GetInputStream (zent.NativeEntry);
            if (!zent.IsEncrypted)
                return input;
            var data = new byte[zent.UnpackedSize];
            using (input)
                input.Read (data, 0, data.Length);
            int enc_len;
            if (zent.Name.HasExtension (".an"))
                enc_len = data.Length;
            else
                enc_len = Math.Min (data.Length, (int)zarc.CoderLength);
            var rc4 = new AirRc4Crypt (zarc.Key);
            rc4.Decrypt (data, 0, enc_len);
            return new BinMemoryStream (data, entry.Name);
        }

        bool FindAirNovelCoderLength (SharpZip.ZipFile zip, out uint coder_length)
        {
            coder_length = 0;
            var config = zip.GetEntry ("config.anprj");
            if (null == config)
                return false;
            using (var input = zip.GetInputStream (config))
            {
                var coder = FindConfigNode (input, "/config/coder[@len]");
                if (null == coder)
                    return false;
                var lenAttr = coder.Attributes["len"].Value;
                var styles = NumberStyles.Integer;
                if (lenAttr.StartsWith ("0x"))
                {
                    lenAttr = lenAttr.Substring (2, lenAttr.Length-2);
                    styles = NumberStyles.HexNumber;
                }
                return UInt32.TryParse (lenAttr, styles, CultureInfo.InvariantCulture, out coder_length);
            }
        }

        XmlNode FindConfigNode (Stream input, string xpath)
        {
            using (var reader = new StreamReader (input))
            {
                var xml = new XmlDocument();
                xml.Load (reader);
                return xml.DocumentElement.SelectSingleNode (xpath);
            }
        }

        public static AirNovelScheme DefaultScheme = new AirNovelScheme { KnownKeys = new Dictionary<string, string>() };

        internal IDictionary<string, string> KnownKeys { get { return DefaultScheme.KnownKeys; } }

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (AirNovelScheme)value; }
        }

        string QueryEncryptionKey (ArcView file)
        {
            var title = FormatCatalog.Instance.LookupGame (file.Name);
            if (!string.IsNullOrEmpty (title) && KnownKeys.TryGetValue (title, out string key))
                return key;

            var options = Query<AirOptions>("RC4 plain key:");
            if (!string.IsNullOrWhiteSpace (options.key))
                return options.key;

            return null;
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetAIR();
        }

        public override ResourceOptions GetOptions(object w)
        {
            var widget = w as GUI.WidgetAIR;
            if (null != widget)
            {
                return new AirOptions { key = widget.ValueTextBox.Text };
            }
            return this.GetDefaultOptions();
        }
    }

    class AirOptions : ResourceOptions
    {
        public string key;
    }

    internal class AirRc4Crypt
    {
        const int KeyLength = 0xFF;

        byte[]  KeyState = new byte[KeyLength+1];

        public AirRc4Crypt (byte[] key)
        {
            for (int i = 0; i <= KeyLength; ++i)
            {
                KeyState[i] = (byte)i;
            }
            int j = 0;
            for (int i = 0; i <= KeyLength; ++i)
            {
                j = (j + KeyState[i] + key[i]) & KeyLength;
                KeyState[i] ^= KeyState[j];
                KeyState[j] ^= KeyState[i];
                KeyState[i] ^= KeyState[j];
            }
        }

        public static byte[] GenerateKey (string passPhrase)
        {
            if (string.IsNullOrEmpty (passPhrase))
                throw new ArgumentException ("passPhrase");
            var key = new byte[KeyLength+1];
            for (int i = 0; i < key.Length; ++i)
            {
                key[i] = (byte)passPhrase[i % passPhrase.Length];
            }
            return key;
        }

        public void Decrypt (byte[] data, int pos, int length)
        {
            int i = 0;
            int j = 0;
            var keyCopy = KeyState.Clone() as byte[];
            int last = Math.Min (pos + length, data.Length);
            while (pos < last)
            {
                i = (i + 1) & KeyLength;
                j = (j + keyCopy[i]) & KeyLength;
                // NOTE: standard RC4 encryption class doesn't work here,
                // this swap fails when i == j lol
                keyCopy[i] ^= keyCopy[j];
                keyCopy[j] ^= keyCopy[i];
                keyCopy[i] ^= keyCopy[j];
                int k = (keyCopy[i] + keyCopy[j]) & KeyLength;
                data[pos++] ^= keyCopy[k];
            }
        }
    }
}