using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using GameRes.Formats.PkWare;
using SharpZip = ICSharpCode.SharpZipLib.Zip;

namespace GameRes.Formats.GameCreator
{
    [Export(typeof(ArchiveFormat))]
    public class GcZipOpener : ZipOpener
    {
        public override string         Tag { get { return "ZIP/GC"; } }
        public override string Description { get { return "GameCreator protected archive"; } }
        public override uint     Signature { get { return  0x04034B50; } } // PK

        private const string GC_PASSWORD = "gc_zip_2024";

        public GcZipOpener()
        {
            Extensions = new string[] { "json", "mp3", "ogg", "mp4" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            // Check extension first
            string ext = Path.GetExtension (file.Name).ToLowerInvariant();
            if (!Extensions.Contains (ext.TrimStart('.')))
                return null;

            // Check for ZIP signature
            if (file.View.ReadUInt32 (0) != 0x04034B50)
                return null;

            var input = file.CreateStream();
            try
            {
                var zip = new SharpZip.ZipFile (input);
                zip.StringCodec = SharpZip.StringCodec.FromCodePage (65001); // UTF-8

                try
                {
                    var files = zip.Cast<SharpZip.ZipEntry>().Where (z => !z.IsDirectory).ToList();

                    if (files.Any (z => z.IsCrypted || z.AESKeySize > 0))
                    {
                        zip.Password = GC_PASSWORD;
                        var testEntry = files.FirstOrDefault();
                        if (testEntry != null)
                        {
                            try
                            {
                                using (var testStream = zip.GetInputStream (testEntry))
                                {
                                    var buffer = new byte[4];
                                    testStream.Read (buffer, 0, 4);
                                }
                            }
                            catch (SharpZip.ZipException ex)
                            {
                                if (ex.Message.Contains ("password") || ex.Message.Contains ("AES"))
                                {
                                    zip.Close();
                                    input.Dispose();
                                    return null;
                                }

                                zip.Close();
                                input.Position = 0;
                                return base.TryOpen (file);
                            }
                        }
                    }

                    var dir = files.Select (z => new ZipEntry (z) as Entry).ToList();

                    if (dir.Count == 1)
                    {
                        var entry = dir[0];
                        var origExt = Path.GetExtension (entry.Name);
                        entry.Name = Path.GetFileNameWithoutExtension (file.Name) + origExt;
                        entry.Type = FormatCatalog.Instance.GetTypeFromName (entry.Name);
                    }

                    return new PkZipArchive (file, this, dir, zip);
                }
                catch
                {
                    zip.Close();
                    throw;
                }
            }
            catch
            {
                input.Dispose();
                return null;
            }
        }
    }

    [Export(typeof(AudioFormat))]
    [ExportMetadata("Priority", 50)]
    public class GcZipAudio : AudioFormat
    {
        public override string         Tag { get { return "GC/AUDIO"; } }
        public override string Description { get { return "GameCreator ZIP-compressed audio"; } }
        public override uint     Signature { get { return  0x04034B50; } }

        private const string GC_PASSWORD = "gc_zip_2024";

        public GcZipAudio()
        {
            Extensions = new string[] { "mp3", "ogg" };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            string ext = VFS.GetExtension (file.Name, true).ToLowerInvariant();
            if (!Extensions.Contains (ext))
                return null;

            if (file.Signature != 0x04034B50)
                return null;

            try
            {
                var stream = file.AsStream;
                stream.Position = 0;

                using (var zip = new SharpZip.ZipFile (stream))
                {
                    zip.Password = GC_PASSWORD;

                    var entry = zip.Cast<SharpZip.ZipEntry>().FirstOrDefault (e => !e.IsDirectory);
                    if (entry == null)
                        return null;

                    if (entry.AESKeySize > 0 || entry.IsCrypted)
                    {
                        try
                        {
                            var output = new MemoryStream();
                            using (var input = zip.GetInputStream (entry))
                            {
                                input.CopyTo (output);
                            }
                            output.Position = 0;

                            var extracted = new BinMemoryStream (output.ToArray(), file.Name);
                            var sound = AudioFormat.Read (extracted);
                            if (sound != null)
                                return sound;
                        }
                        catch (SharpZip.ZipException) { }
                    }
                }
            }
            catch { }

            return null;
        }
    }

    [Export(typeof(ScriptFormat))]
    [ExportMetadata("Priority", 50)]
    public class GcZipJsonFormat : GenericScriptFormat
    {
        public override string          Tag { get { return "GC/JSON"; } }
        public override string  Description { get { return "GameCreator ZIP-compressed JSON"; } }
        public override uint      Signature { get { return  0x04034B50; } }
        public override ScriptType DataType { get { return  ScriptType.JsonScript; } }

        private const string GC_PASSWORD = "gc_zip_2024";

        public GcZipJsonFormat()
        {
            Extensions = new string[] { "json" };
        }

        public override bool IsScript (IBinaryStream file)
        {
            string ext = VFS.GetExtension (file.Name, true).ToLowerInvariant();
            if (!Extensions.Contains (ext))
                return false;

            try
            {
                var stream = file.AsStream;
                stream.Position = 0;

                using (var test = new SharpZip.ZipFile (stream))
                {
                    test.Password = GC_PASSWORD;
                    var entries = test.Cast<SharpZip.ZipEntry>().Where (e => !e.IsDirectory).ToList();

                    if (entries.Count == 0)
                        return false;

                    var entry = entries.First();
                    if (entry.IsCrypted || entry.AESKeySize > 0)
                    {
                        try
                        {
                            using (var testStream = test.GetInputStream (entry))
                            {
                                var buffer = new byte[4];
                                testStream.Read (buffer, 0, 4);
                            }
                        }
                        catch (SharpZip.ZipException)
                        {
                            return false;
                        }
                    }

                    return true;
                }
            }
            catch { }
            return false;
        }

        public override Stream ConvertFrom (IBinaryStream file)
        {
            var stream = file.AsStream;
            stream.Position = 0;

            using (var zip = new SharpZip.ZipFile (stream))
            {
                zip.Password = GC_PASSWORD;

                var entry = zip.Cast<SharpZip.ZipEntry>().FirstOrDefault (e => !e.IsDirectory);
                if (entry == null)
                    throw new InvalidFormatException ("Nothing found in ZIP archive");

                var output = new MemoryStream();
                try
                {
                    using (var input = zip.GetInputStream (entry))
                    {
                        input.CopyTo (output);
                    }
                }
                catch (SharpZip.ZipException ex)
                {
                    if (ex.Message.Contains ("password") || ex.Message.Contains ("AES"))
                        throw new InvalidFormatException ("Invalid password for encrypted ZIP");
                    throw;
                }

                output.Position = 0;
                return output;
            }
        }

        public override Stream ConvertBack (IBinaryStream file)
        {
            var output = new MemoryStream();

            using (var zip = new SharpZip.ZipOutputStream (output))
            {
                zip.IsStreamOwner = false;
                zip.Password = GC_PASSWORD;

                // Use AES encryption
                zip.UseZip64 = SharpZip.UseZip64.Off;

                var entryName = Path.GetFileNameWithoutExtension (file.Name) + ".json";
                var entry = new SharpZip.ZipEntry (entryName) {
                    DateTime = DateTime.Now,
                    AESKeySize = 256
                };

                zip.PutNextEntry (entry);
                file.AsStream.CopyTo (zip);
                zip.CloseEntry();
                zip.Finish();
            }

            output.Position = 0;
            return output;
        }
    }
}