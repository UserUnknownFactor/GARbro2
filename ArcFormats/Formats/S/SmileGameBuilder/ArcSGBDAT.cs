using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using GameRes.Formats.PkWare;
using SharpZip = ICSharpCode.SharpZipLib.Zip;

namespace GameRes.Formats.SgbPack
{
    [Export(typeof(ArchiveFormat))]
    public class SgbPackOpener : ArchiveFormat
    {
        public override string Tag => "SGBPACK";
        public override string Description => "Smaile Game Builder archive";
        public override uint Signature => 0x44424753; // 'SGBD'
        public override bool IsHierarchic => true;
        public override bool CanWrite => false;

        static readonly byte[] Key1 = {
            0x48, 0x09, 0x14, 0x9A, 0x30, 0xA9, 0x54, 0xE1,
            0x00, 0x08, 0x0E, 0x09, 0x14, 0x3C, 0x42, 0x46
        };
        static readonly byte[] Key2 = {
            0x00, 0x0E, 0x08, 0x1E, 0x18, 0x37, 0x12, 0x00,
            0x48, 0x87, 0x46, 0x0B, 0x9C, 0x68, 0xA8, 0x4B
        };
        static readonly byte[] ZipHeader = {
            0x50, 0x4B, 0x03, 0x04, 0x20, 0x00, 0x00, 0x00
        };

        public override ArcFile TryOpen(ArcView file)
        {
            if (!file.View.AsciiEqual(0, "SGBDAT"))
                return null;

            var decrypted = new SubtractedStream(file.CreateStream(8), Key1);
            var input = new PrefixStream(ZipHeader, decrypted);
            try
            {
                return OpenZipArchive(file, input);
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }

        internal ArcFile OpenZipArchive(ArcView file, Stream input)
        {
            var zip = new SharpZip.ZipFile(input);
            zip.StringCodec = SharpZip.StringCodec.Default;
            try
            {
                var files = zip.Cast<SharpZip.ZipEntry>().Where(z => !z.IsDirectory);
                var dir = files.Select(z => new ZipEntry(z) as Entry).ToList();
                return new PkZipArchive(file, this, dir, zip);
            }
            catch
            {
                zip.Close();
                throw;
            }
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            var zarc = (PkZipArchive)arc;
            var zent = (ZipEntry)entry;
            var file_result = zarc.Native.GetInputStream(zent.NativeEntry);
            var result = new SubtractedStream(file_result, Key2);
            //Dump.StreamToFile(result, $"{Path.GetFileName(entry.Name)}.dat");
            entry.ChangeType(FormatCatalog.Instance.GetTypeFromName(entry.Name));
            return result;
        }
    }
}