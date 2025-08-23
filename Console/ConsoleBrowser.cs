using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;
using GameRes;

namespace GARbro
{
    public enum ExistingFileAction
    {
        Ask,
        Skip,
        Overwrite,
        Rename
    }

    public enum ConsoleCommand
    {
        Invalid,
        Info,
        List,
        Extract
    }

    class ConsoleBrowser
    {
        string m_output_dir;
        Regex m_file_filter;
        ImageFormat m_image_format;
        bool m_auto_image_format = false;
        bool m_ignore_errors = true;
        bool m_always_overwrite = false;
        bool m_skip_images;
        bool m_skip_script;
        bool m_skip_audio;
        bool m_convert_audio;
        bool m_adjust_image_offset;

        ExistingFileAction m_existing_file_action = ExistingFileAction.Ask;

        public static readonly HashSet<string> CommonAudioFormats = new HashSet<string> { "wav", "mp3", "ogg" };
        public static readonly HashSet<string> CommonImageFormats = new HashSet<string> { "jpeg", "png", "bmp", "tga" };

        void ListFormats ()
        {
            Console.WriteLine ("Recognized resource formats:\n");
            foreach (var format in FormatCatalog.Instance.ArcFormats.OrderBy (f => f.Tag))
            {
                Console.WriteLine ("{0,-20} {1}", format.Tag, format.Description);
            }
        }

        void ListFiles (Entry[] file_list)
        {
            Console.WriteLine ("     Offset      Size  Name");
            Console.WriteLine (" ----------  --------  ------------------------------------------------------");
            foreach (var entry in file_list)
            {
                Console.WriteLine (" [{1:X8}] {0,9}  {2}", entry.Size, entry.Offset, entry.Name);
            }
            Console.WriteLine (" ----------  --------  ------------------------------------------------------");
            Console.WriteLine ("                       {0} files", file_list.Length);
        }

        void ExtractFiles (Entry[] file_list, ArcFile arc)
        {
            Directory.CreateDirectory (m_output_dir);

            int skipped_count = 0;
            for (int i = 0; i < file_list.Length; ++i)
            {
                var entry = file_list[i];
                PrintProgress (i + 1, file_list.Length, entry.Name);

                if (!ExtractEntry (arc, entry))
                {
                    skipped_count++;
                    if (!m_ignore_errors)
                        break;
                }
            }

            Console.WriteLine();
            Console.WriteLine (skipped_count > 0 ? "{0} files were skipped" : "All OK", skipped_count);
        }

        bool ExtractEntry (ArcFile arc, Entry entry)
        {
            try
            {
                if (m_image_format != null && entry.Type == "image")
                {
                    ExtractImage (arc, entry);
                }
                else if (m_convert_audio && entry.Type == "audio")
                {
                    ExtractAudio (arc, entry);
                }
                else
                {
                    ExtractRaw (arc, entry);
                }
                return true;
            }
            catch (TargetException)
            {
                return false;
            }
            catch (InvalidFormatException)
            {
                PrintWarning ("Invalid file format, extracting as raw");
                try
                {
                    ExtractRaw (arc, entry);
                    return true;
                }
                catch (Exception e)
                {
                    PrintError (string.Format ("Failed to extract {0}: {1}", entry.Name, e.Message));
                    return false;
                }
            }
            catch (Exception e)
            {
                PrintError (string.Format ("Failed to extract {0}: {1}", entry.Name, e.Message));
#if DEBUG
                throw;
#endif
            }
#pragma warning disable CS0162
            return false;
#pragma warning restore CS0162
        }

        void ExtractImage (ArcFile arc, Entry entry)
        {
            var target_format = m_image_format ?? ImageFormat.Png;

            using (var decoder = arc.OpenImage (entry))
            {
                var source_format = decoder.SourceFormat;
                if (m_auto_image_format && source_format != null)
                {
                    target_format = CommonImageFormats.Contains (source_format.Tag.ToLowerInvariant()) 
                        ? source_format 
                        : ImageFormat.Png;
                }

                var output_ext = target_format.Extensions.FirstOrDefault() ?? "";
                var output_name = Path.ChangeExtension (entry.Name, output_ext);
                
                if (source_format == target_format)
                {
                    using (var output = CreateNewFile (output_name))
                    {
                        if (output != null)
                            decoder.Source.CopyTo (output);
                    }
                    return;
                }

                var image = decoder.Image;
                if (m_adjust_image_offset)
                    image = AdjustImageOffset (image);

                using (var output = CreateNewFile (output_name))
                {
                    if (output != null)
                        target_format.Write (output, image);
                }
            }
        }

        static ImageData AdjustImageOffset (ImageData image)
        {
            if (0 == image.OffsetX && 0 == image.OffsetY)
                return image;
            
            int width = (int)image.Width + image.OffsetX;
            int height = (int)image.Height + image.OffsetY;
            if (width <= 0 || height <= 0)
                return image;

            int x = Math.Max (image.OffsetX, 0);
            int y = Math.Max (image.OffsetY, 0);
            int src_x = image.OffsetX < 0 ? Math.Abs (image.OffsetX) : 0;
            int src_y = image.OffsetY < 0 ? Math.Abs (image.OffsetY) : 0;
            int src_stride = (int)image.Width * (image.BPP + 7) / 8;
            int dst_stride = width * (image.BPP + 7) / 8;
            var pixels = new byte[height * dst_stride];
            int offset = y * dst_stride + x * image.BPP / 8;
            var rect = new Int32Rect (src_x, src_y, (int)image.Width - src_x, 1);
            for (int row = src_y; row < image.Height; ++row)
            {
                rect.Y = row;
                image.Bitmap.CopyPixels (rect, pixels, src_stride, offset);
                offset += dst_stride;
            }

            var bitmap = BitmapSource.Create (width, height, image.Bitmap.DpiX, image.Bitmap.DpiY,
                                              image.Bitmap.Format, image.Bitmap.Palette, pixels, dst_stride);
            return new ImageData (bitmap);
        }

        void ExtractAudio (ArcFile arc, Entry entry)
        {
            using (var file = arc.OpenBinaryEntry (entry))
            using (var sound = AudioFormat.Read (file))
            {
                if (sound == null)
                    throw new InvalidFormatException ("Unable to interpret audio format");
                ConvertAudio (entry.Name, sound);
            }
        }

        void ExtractRaw (ArcFile arc, Entry entry)
        {
            using (var input = arc.OpenEntry (entry))
            using (var output = CreateNewFile (entry.Name))
            {
                if (output != null)
                    input.CopyTo (output);
            }
        }

        void ConvertAudio (string filename, SoundInput sound_input)
        {
            var source_format = sound_input.SourceFormat;

            if (CommonAudioFormats.Contains (source_format))
            {
                var output_name = Path.ChangeExtension (filename, source_format);
                using (var output = CreateNewFile (output_name))
                {
                    if (output != null)
                    {
                        sound_input.Source.Position = 0;
                        sound_input.Source.CopyTo (output);
                    }
                }
            }
            else
            {
                var output_name = Path.ChangeExtension (filename, "wav");
                using (var output = CreateNewFile (output_name))
                {
                    if (output != null)
                        AudioFormat.Wav.Write (sound_input, output);
                }
            }
        }

        bool ConvertAudio (string filename, IBinaryStream input)
        {
            using (var sound = AudioFormat.Read (input))
            {
                if (sound == null)
                    return false;

                Console.WriteLine ("Converting {0} audio", sound.SourceFormat);
                ConvertAudio (filename, sound);
                return true;
            }
        }

        bool ConvertFile (string file)
        {
            var filename = Path.GetFileName (file);
            PrintProgress (1, 1, filename);

            try
            {
                using (var input = BinaryStream.FromFile (file))
                {
                    if (!m_skip_images && ConvertImage (filename, input))
                        return true;
                    if (!m_skip_audio && ConvertAudio (filename, input))
                        return true;
                    throw new UnknownFormatException();
                }
            }
            catch (Exception e)
            {
                PrintError ("Failed to convert file: " + e.Message);
                return false;
            }
        }

        bool ConvertImage (string filename, IBinaryStream input)
        {
            var format_tuple = ImageFormat.FindFormat (input);
            if (format_tuple == null)
                return false;

            var source_format = format_tuple.Item1;
            var target_format = m_image_format;

            input.Position = 0;
            var image_data = source_format.Read (input, format_tuple.Item2);

            if (m_auto_image_format && source_format != null)
            {
                target_format = CommonImageFormats.Contains (source_format.Tag.ToLowerInvariant()) 
                    ? source_format 
                    : ImageFormat.Png;
            }

            var output_ext = target_format.Extensions.FirstOrDefault() ?? "";
            var output_name = Path.ChangeExtension (filename, output_ext);

            if (source_format == target_format)
            {
                PrintWarning ("Input and output format are identical. No conversion necessary");
                return true;
            }

            using (var output = CreateNewFile (output_name))
            {
                if (output != null)
                    target_format.Write (output, image_data);
            }
            return true;
        }

        Stream CreateNewFile (string filename)
        {
            var path = Path.Combine (m_output_dir, filename);
            path = Path.GetFullPath (path);
            Directory.CreateDirectory (Path.GetDirectoryName (path));

            if (File.Exists (path) && !m_always_overwrite)
            {
                path = OverwritePrompt (path);
                if (path == null)
                    return null;
            }

            return File.Open (path, FileMode.Create);
        }

        void Run (string[] args)
        {
            if (args.Length < 1)
            {
                Usage();
                return;
            }

            var command = ParseCommand (args[0]);
            if (command == ConsoleCommand.Invalid)
                return;

            if (args.Length < 2)
            {
                PrintError ("No archive file specified");
                return;
            }

            var input_file = args[args.Length - 1];
            if (!File.Exists (input_file))
            {
                PrintError ("Input file " + input_file + " does not exist");
                return;
            }

            if (!ParseArguments (args, 1, args.Length - 1))
                return;

            DeserializeGameData();

            try
            {
                VFS.ChDir (input_file);
                ProcessArchiveFile (command);
            }
            catch (Exception)
            {
                ProcessNonArchiveFile (input_file, command);
            }
        }

        ConsoleCommand ParseCommand (string cmd)
        {
            switch (cmd)
            {
            case "h":
            case "-h":
            case "--help":
            case "/?":
            case "-?":
                Usage();
                return ConsoleCommand.Invalid;
            case "f":
                ListFormats();
                return ConsoleCommand.Invalid;
            case "i":
                return ConsoleCommand.Info;
            case "l":
                return ConsoleCommand.List;
            case "x":
                return ConsoleCommand.Extract;
            default:
                PrintError (File.Exists (cmd) 
                    ? "No command specified. Use -h command line parameter to show help." 
                    : "Invalid command: " + cmd);
                return ConsoleCommand.Invalid;
            }
        }

        bool ParseArguments (string[] args, int start, int count)
        {
            m_output_dir = Directory.GetCurrentDirectory();
            
            for (int i = start; i < start + count; ++i)
            {
                switch (args[i])
                {
                case "-o":
                    if (++i >= start + count)
                    {
                        PrintError ("No output directory specified");
                        return false;
                    }
                    m_output_dir = args[i];
                    if (File.Exists (m_output_dir))
                    {
                        PrintError ("Invalid output directory");
                        return false;
                    }
                    break;
                    
                case "-f":
                    if (++i >= start + count)
                    {
                        PrintError ("No filter specified");
                        return false;
                    }
                    try
                    {
                        m_file_filter = new Regex (args[i]);
                    }
                    catch (ArgumentException e)
                    {
                        PrintError ("Invalid filter: " + e.Message);
                        return false;
                    }
                    break;
                    
                case "-if":
                    if (++i >= start + count)
                    {
                        PrintError ("No image format specified");
                        return false;
                    }
                    var format_tag = args[i].ToUpperInvariant();
                    if (format_tag == "JPG")
                        format_tag = "JPEG";
                    m_image_format = ImageFormat.FindByTag (format_tag);
                    if (m_image_format == null)
                    {
                        PrintError ("Unknown image format specified: " + args[i]);
                        return false;
                    }
                    break;
                    
                case "-ca":
                    m_convert_audio = true;
                    break;
                case "-na":
                    m_skip_audio = true;
                    break;
                case "-ni":
                    m_skip_images = true;
                    break;
                case "-ns":
                    m_skip_script = true;
                    break;
                case "-aio":
                    m_adjust_image_offset = true;
                    break;
                case "-ocu":
                    m_auto_image_format = true;
                    break;
                case "-y":
                    m_always_overwrite = true;
                    break;
                default:
                    PrintWarning ("Unknown command line parameter: " + args[i]);
                    break;
                }
            }

            if (m_auto_image_format && m_image_format == null)
            {
                PrintError ("The parameter -ocu requires the image format (-if parameter) to be set");
                return false;
            }
            return true;
        }

        void ProcessArchiveFile (ConsoleCommand command)
        {
            var arc_fs = (ArchiveFileSystem)VFS.Top;
            var file_list = arc_fs.GetFilesRecursive().Where (e => e.Offset >= 0);

            if (m_skip_images || m_skip_script || m_skip_audio || m_file_filter != null)
            {
                file_list = file_list.Where (f => !(m_skip_images && f.Type == "image") &&
                                                   !(m_skip_script && f.Type == "script") &&
                                                   !(m_skip_audio && f.Type == "audio") &&
                                                   (m_file_filter == null || m_file_filter.IsMatch (f.Name)));
            }

            if (!file_list.Any())
            {
                bool has_filter = m_skip_audio || m_skip_images || m_skip_script || m_file_filter != null;
                PrintError (has_filter ? "No files match the given filter" : "Archive is empty");
                return;
            }

            var file_array = file_list.OrderBy (e => e.Offset).ToArray();

            switch (command)
            {
            case ConsoleCommand.Info:
                Console.WriteLine (arc_fs.Source.Tag);
                break;
            case ConsoleCommand.List:
                ListFiles (file_array);
                break;
            case ConsoleCommand.Extract:
                ExtractFiles (file_array, arc_fs.Source);
                break;
            }
        }

        void ProcessNonArchiveFile (string file, ConsoleCommand command)
        {
            var filename = Path.GetFileName (file);
            if (m_file_filter != null && !m_file_filter.IsMatch (filename))
            {
                PrintError ("No files match the given filter");
                return;
            }

            switch (command)
            {
            case ConsoleCommand.Info:
                var format = IdentifyFile (file);
                if (format != null)
                    Console.WriteLine (format);
                break;
                
            case ConsoleCommand.List:
                if (IdentifyFile (file) != null)
                {
                    var entry = new Entry {
                        Name = filename,
                        Offset = 0,
                        Size = (uint)new FileInfo (file).Length
                    };
                    ListFiles (new Entry[] { entry });
                }
                break;
                
            case ConsoleCommand.Extract:
                if (ConvertFile (file))
                    Console.WriteLine ("All OK");
                break;
            }
        }

        string IdentifyFile (string file)
        {
            try
            {
                using (var input = BinaryStream.FromFile (file))
                {
                    var image_format = ImageFormat.FindFormat (input);
                    if (image_format != null)
                        return image_format.Item1.Tag;

                    input.Position = 0;
                    using (var sound = AudioFormat.Read (input))
                    {
                        if (sound != null)
                            return sound.SourceFormat;
                    }
                }
            }
            catch (Exception)
            {
                // Format identification failed
            }
            
            PrintError ("Input file has an unknown format");
            return null;
        }

        void DeserializeGameData ()
        {
            var scheme_file = Path.Combine (FormatCatalog.Instance.DataDirectory, "Formats.dat");
            try
            {
                using (var file = File.OpenRead (scheme_file))
                    FormatCatalog.Instance.DeserializeScheme (file);
            }
            catch (Exception)
            {
                // Scheme file not found or invalid
            }
        }

        static void Usage ()
        {
            var version = Assembly.GetAssembly (typeof (FormatCatalog)).GetName().Version;
            Console.WriteLine ("GARbro - Game Resource browser, version {0}", version);
            Console.WriteLine ("2014-2025 by mørkt, crskycode and others, published under a MIT license");
            Console.WriteLine ("-----------------------------------------------------------------------------\n");
            Console.WriteLine ("Usage: {0} <command> [<switches>...] <archive_name>", 
                               Process.GetCurrentProcess().ProcessName);
            Console.WriteLine ("\nCommands:");
            Console.WriteLine ("  i   Identify archive format");
            Console.WriteLine ("  f   List supported formats");
            Console.WriteLine ("  l   List contents of archive");
            Console.WriteLine ("  x   Extract files from archive");
            Console.WriteLine ("\nSwitches:");
            Console.WriteLine ("  -o <Directory>   Set output directory for extraction");
            Console.WriteLine ("  -y               Do not prompt if file exists (always overwrite)");
            Console.WriteLine ("  -f <Filter>      Only process files matching regular expression");
            Console.WriteLine ("  -if <Format>     Set image output format (e.g. 'png', 'jpg', 'bmp')");
            Console.WriteLine ("  -ca              Convert audio files to wav format");
            Console.WriteLine ("  -na              Ignore audio files");
            Console.WriteLine ("  -ni              Ignore image files");
            Console.WriteLine ("  -ns              Ignore scripts");
            Console.WriteLine ("  -aio             Adjust image offset");
            Console.WriteLine ("  -ocu             Only convert unknown image formats");
            Console.WriteLine();
        }

        static void PrintProgress (int current, int total, string filename)
        {
            Console.WriteLine ("[{0}/{1}] {2}", current, total, filename);
        }

        static void PrintWarning (string msg)
        {
            Console.WriteLine ("Warning: " + msg);
        }

        static void PrintError (string msg)
        {
            Console.WriteLine ("Error: " + msg);
        }

        string OverwritePrompt (string filename)
        {
            switch (m_existing_file_action)
            {
            case ExistingFileAction.Skip:
                return null;
            case ExistingFileAction.Overwrite:
                return filename;
            case ExistingFileAction.Rename:
                return GetUniqueFilename (filename);
            }

            Console.WriteLine ("The file {0} already exists. Overwrite?", filename);
            Console.WriteLine ("[Y]es | [N]o | [A]lways | n[E]ver | [R]ename | A[l]ways rename");

            while (true)
            {
                var key = Console.ReadKey().KeyChar;
                Console.WriteLine();
                
                switch (char.ToUpperInvariant (key))
                {
                case 'Y':
                    return filename;
                case 'N':
                    return null;
                case 'A':
                    m_existing_file_action = ExistingFileAction.Overwrite;
                    return filename;
                case 'E':
                    m_existing_file_action = ExistingFileAction.Skip;
                    return null;
                case 'R':
                    return GetUniqueFilename (filename);
                case 'L':
                    m_existing_file_action = ExistingFileAction.Rename;
                    return GetUniqueFilename (filename);
                }
            }
        }

        string GetUniqueFilename (string path)
        {
            var dir = Path.GetDirectoryName (path);
            var name = Path.GetFileNameWithoutExtension (path);
            var ext = Path.GetExtension (path);

            int n = 2;
            do
            {
                path = Path.Combine (dir, string.Format ("{0} ({1}){2}", name, n++, ext));
            }
            while (File.Exists (path));

            return path;
        }

        static void OnParametersRequest (object sender, ParametersRequestEventArgs e)
        {
            var format = sender as IResource;
            if (format != null)
            {
                e.InputResult = true;
                e.Options = format.GetDefaultOptions();
            }
        }

        static void Main (string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            FormatCatalog.Instance.ParametersRequest += OnParametersRequest;
            new ConsoleBrowser().Run (args);

#if DEBUG
            Console.Read();
#endif
        }
    }
}
