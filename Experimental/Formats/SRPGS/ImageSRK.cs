using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.SRPGStudio
{
    [Export(typeof(ImageFormat))]
    public class SrkImageFormat : ImageFormat
    {
        public override string         Tag { get { return "SRK/IMAGE"; } }
        public override string Description { get { return "SRPG Studio encrypted image"; } }
        public override uint     Signature { get { return 0; } }

        public SrkImageFormat()
        {
            Extensions = new string[] { "srk" };
        }

        public override ImageMetaData ReadMetaData(IBinaryStream file)
        {
            if (!file.Name.HasExtension(".srk"))
                return null;

            if (file.Length < 8)
                return null;

            // Use shared cache from SrkOpener
            var crypto = SrkOpener.LastCrypto;

            // Test if cached key still works
            if (crypto != null)
            {
                file.Position = 0;
                var testHeader = file.ReadBytes((int)Math.Min(16, file.Length));
                var testDecrypt = crypto.Decrypt(testHeader);
                if (testDecrypt.Length >= 4)
                {
                    uint testSig = testDecrypt.ToUInt32(0);
                    var testRes = AutoEntry.DetectFileType(testSig);
                    if (testRes.Type == null || testRes.Extensions.FirstOrDefault() == "exe")
                        crypto = null;
                }
                else
                    crypto = null;
            }
            if (crypto == null)
            {
                crypto = SrkOpener.DetectKeyFromStream(file.Name, file);
                if (crypto == null)
                    crypto = new SrpgCrypto("keyset");
                SrkOpener.LastCrypto = crypto;
            }

            var headerSize = (int)Math.Min(64, file.Length);
            file.Position = 0;
            var header = file.ReadBytes(headerSize);
            var decryptedHeader = crypto.Decrypt(header);

            if (decryptedHeader.Length < 4)
                return null;

            uint signature = decryptedHeader.ToUInt32(0);
            if (!IsImageSignature(signature))
                return null;

            file.Position = 0;
            var fullData = file.ReadBytes((int)Math.Min(int.MaxValue, file.Length));
            var decryptedData = crypto.Decrypt(fullData);

            using (var decryptedStream = new BinMemoryStream(decryptedData, file.Name))
            {
                var formatInfo = ImageFormat.FindFormat(decryptedStream);
                if (formatInfo == null)
                    return null;
                return formatInfo.Item2;
            }
        }

        public override ImageData Read(IBinaryStream file, ImageMetaData info)
        {
            var crypto = SrkOpener.LastCrypto ?? new SrpgCrypto("keyset");

            file.Position = 0;
            var encryptedData = file.ReadBytes((int)Math.Min(int.MaxValue, file.Length));
            var decryptedData = crypto.Decrypt(encryptedData);

            using (var decryptedStream = new BinMemoryStream(decryptedData, file.Name))
            {
                var formatInfo = ImageFormat.FindFormat(decryptedStream);
                if (formatInfo == null)
                    throw new InvalidFormatException("Unknown image format");

                var format = formatInfo.Item1;
                return format.Read(decryptedStream, info);
            }
        }

        public override void Write(Stream file, ImageData image)
        {
            var crypto = SrkOpener.LastCrypto ?? new SrpgCrypto("keyset");
            using (var ms = new MemoryStream())
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image.Bitmap));
                encoder.Save(ms);
                var imageData = ms.ToArray();
                var encryptedData = crypto.Encrypt(imageData);
                file.Write(encryptedData, 0, encryptedData.Length);
            }
        }

        private bool IsImageSignature(uint signature)
        {
            if (signature == 0x474E5089) return true;            // PNG
            if ((signature & 0xFFFF) == 0xD8FF) return true;     // JPEG
            if ((signature & 0xFFFF) == 0x4D42) return true;     // BMP
            if ((signature & 0xFFFFFF) == 0x464947) return true; // GIF
            if ((signature & 0xFF) <= 1) return true;            // TGA maybe

            var res = AutoEntry.DetectFileType(signature);
            bool is_image = res.Type == "image";
            if (!is_image)
                throw new NotSupportedException("Not an image - open it as archive");
            return is_image;
        }
    }
}