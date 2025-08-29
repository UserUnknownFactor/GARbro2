using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using GameRes;
using GARbro.GUI.Properties;

namespace GARbro.GUI
{
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Convert selected images to another format.
        /// </summary>
        void ConvertMediaExec (object sender, ExecutedRoutedEventArgs e)
        {
            if (ViewModel.IsArchive)
                return;

            var entries = CurrentDirectory.SelectedItems.Cast<EntryViewModel>().ToList();
            var imageEntries = entries.Where (entry => entry.Type == "image").Select (entry => entry.Source).ToList();
            var audioEntries = entries.Where (entry => entry.Type == "audio").Select (entry => entry.Source).ToList();
            //var videoEntries = entries.Where (entry => entry.Type == "video").Select (entry => entry.Source).ToList();

            if (!imageEntries.Any() && !audioEntries.Any()) // && !videoEntries.Any())
            {
                PopupError (Localization._T("MsgNoMediaFiles"), Localization._T("TextMediaConvertError"));
                return;
            }

            var convert_dialog = new ConvertMedia();

            string destination = ViewModel.Path.First();
            if (ViewModel.IsArchive)
                destination = Path.GetDirectoryName (destination);
            if (!IsWritableDirectory (destination) && Directory.Exists (Settings.Default.appLastDestination))
                destination = Settings.Default.appLastDestination;
            convert_dialog.DestinationDir.Text = destination;

            convert_dialog.Owner = this;
            var result = convert_dialog.ShowDialog() ?? false;
            if (!result)
                return;

            try
            {
                destination = convert_dialog.DestinationDir.Text;
                Directory.SetCurrentDirectory (destination);
                var converter = new GarConvertMedia (this);
                converter.IgnoreErrors = convert_dialog.IgnoreErrors.IsChecked ?? false;
                converter.ForceConversion = convert_dialog.ForceConversion.IsChecked ?? false;

                if (imageEntries.Any())
                {
                    var imageFormat = convert_dialog.ImageConversionFormat.SelectedItem as ImageFormat;
                    if (imageFormat != null)
                        converter.ConvertImages (imageEntries, imageFormat);
                }

                if (audioEntries.Any())
                {
                    var audioFormat = convert_dialog.AudioConversionFormat.SelectedItem as AudioFormat;
                    if (audioFormat != null)
                        converter.ConvertAudios (audioEntries, audioFormat);
                }

                /*
                if (videoEntries.Any())
                {
                    var videoFormat = convert_dialog.VideoConversionFormat.SelectedItem as VideoFormat;
                    if (videoFormat != null)
                        converter.ConvertVideos (videoEntries, videoFormat);
                }
                */

                Settings.Default.appLastDestination = destination;
            }
            catch (Exception X)
            {
                PopupError (X.Message, Localization._T("TextMediaConvertError"));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        static extern bool GetVolumeInformation (
            string rootName, string volumeName, uint volumeNameSize,
            IntPtr serialNumber, IntPtr maxComponentLength, 
            out uint flags, string fs, uint fs_size
        );

        bool IsWritableDirectory (string path)
        {
            var root = Path.GetPathRoot (path);
            if (null == root)
                return false;
            uint flags;
            if (!GetVolumeInformation (root, null, 0, IntPtr.Zero, IntPtr.Zero, out flags, null, 0))
                return false;
            return (flags & 0x00080000) == 0; // FILE_READ_ONLY_VOLUME
        }
    }

    internal class GarConvertMedia : GarOperation
    {
        private IEnumerable<Entry> m_source;
        private ImageFormat     m_image_format;
        private AudioFormat     m_audio_format;
        //private VideoFormat   m_video_format;
        private List<Tuple<string,string>> m_failed = new List<Tuple<string,string>>();
        private bool            m_converting = false;

        public bool IgnoreErrors { get; set; }
        public bool ForceConversion { get; set; }
        public IEnumerable<Tuple<string,string>> FailedFiles { get { return m_failed; } }

        public GarConvertMedia (MainWindow parent) : base (parent, Localization._T("TextMediaConvertError"))
        {
        }

        public void ConvertImages (IEnumerable<Entry> images, ImageFormat format)
        {
            if (m_converting)
                return;
            m_main.StopWatchDirectoryChanges();
            m_source = images;
            m_image_format = format;
            m_converting = true;
            m_progress_dialog = new ProgressDialog ()
            {
                WindowTitle = Localization._T("TextTitle"),
                Text        = Localization.Format("MsgConvertingFile", Localization._T("Type_image")),
                Description = "",
                MinimizeBox = true,
            };
            m_progress_dialog.DoWork += ConvertWorker;
            m_progress_dialog.RunWorkerCompleted += OnConvertComplete;
            m_progress_dialog.ShowDialog (m_main);
        }

        public void ConvertAudios (IEnumerable<Entry> audios, AudioFormat format)
        {
            if (m_converting)
                return;
            m_main.StopWatchDirectoryChanges();
            m_source = audios;
            m_audio_format = format;
            m_converting = true;
            m_progress_dialog = new ProgressDialog ()
            {
                WindowTitle = Localization._T("TextTitle"),
                Text        = Localization._T("Converting audio"),
                Description = "",
                MinimizeBox = true,
            };
            m_progress_dialog.DoWork += ConvertWorker;
            m_progress_dialog.RunWorkerCompleted += OnConvertComplete;
            m_progress_dialog.ShowDialog (m_main);
        }

        /*
        public void ConvertVideos (IEnumerable<Entry> videos, VideoFormat format)
        {
            if (m_converting)
                return;
            m_main.StopWatchDirectoryChanges();
            m_source = videos;
            m_video_format = format;
            m_converting = true;
            m_progress_dialog = new ProgressDialog ()
            {
                WindowTitle = Localization._T("TextTitle"),
                Text        = Localization._T("Converting video"),
                Description = "",
                MinimizeBox = true,
            };
            m_progress_dialog.DoWork += ConvertWorker;
            m_progress_dialog.RunWorkerCompleted += OnConvertComplete;
            m_progress_dialog.ShowDialog (m_main);
        }
        */

        void ConvertWorker (object sender, DoWorkEventArgs e)
        {
            m_pending_error = null;
            int total = m_source.Count();
            int i = 0;
            foreach (var entry in m_source)
            {
                if (m_progress_dialog.CancellationPending)
                {
                    m_pending_error = new OperationCanceledException();
                    break;
                }
                var filename = entry.Name;
                int progress = i++*100/total;
                m_progress_dialog.ReportProgress (progress, string.Format (Localization._T("MsgConvertingFile"),
                    Path.GetFileName (filename)), null);
                try
                {
                    switch (entry.Type) {
                    //case "video":
                        //ConvertVideo (filename,);
                        //break;
                    case "audio":
                        ConvertAudio (filename);
                        break;
                    default:
                    case "image":
                        ConvertImage (filename);
                        break;
                    }
                }
                catch (SkipExistingFileException)
                {
                    continue;
                }
                catch (OperationCanceledException X)
                {
                    m_pending_error = X;
                    break;
                }
                catch (Exception X)
                {
                    if (!IgnoreErrors)
                    {
                        var error_text = string.Format (Localization._T("TextErrorConverting"), entry.Name, X.Message);
                        var result = ShowErrorDialog (error_text);
                        if (!result.Continue)
                            break;
                        IgnoreErrors = result.IgnoreErrors;
                    }
                    m_failed.Add (Tuple.Create (Path.GetFileName (filename), X.Message));
                }
            }
        }

        public static readonly HashSet<string> CommonAudioFormats = new HashSet<string> { "wav", "mp3", "ogg" };
        //public static readonly HashSet<string> CommonVideoFormats = new HashSet<string> { "mp4", "avi", "mkv", "webm", "mov" };

        void ConvertAudio (string filename)
        {
            AudioFormat src_format = null;
            // NOTE: Many parsers close the file on TryOpen so we need a separate step
            using (var file = BinaryStream.FromFile (filename))
                src_format = AudioFormat.FindFormat (file, true);

            using (var file = BinaryStream.FromFile (filename))
            using (var input = AudioFormat.Read (file))
            {
                if (null == input)
                    return;

                string source_ext = Path.GetExtension (filename).TrimStart('.').ToLowerInvariant();
                string targetName = Path.GetFileName (filename);
                string target_ext = m_audio_format.Extensions.FirstOrDefault();
                targetName = Path.ChangeExtension (targetName, target_ext);
                bool overwritingSource = AreSamePaths (filename, targetName);
                string tempFile = null;
                Stream output = null;

                try
                {
                    file.Position = 0;
                    if (!ForceConversion && src_format != null && src_format == m_audio_format && 
                        m_audio_format.Extensions.Any (ext => ext == source_ext))
                    {
                        if (overwritingSource)
                            return;

                        // Just copy the file if it's already in the target format?
                        output = CreateNewFile (targetName);
                        file.Position = 0;
                        file.AsStream.CopyTo (output);
                    }
                    else
                    {
                        // If overwriting source, write to temp file first
                        if (overwritingSource)
                        {
                            tempFile = Path.GetTempFileName();
                            using (output = File.Create (tempFile))
                                m_audio_format.Write (input, output);
                        }
                        else
                        {
                            using (output = CreateNewFile (targetName))
                                m_audio_format.Write (input, output);
                        }

                        if (tempFile != null)
                        {
                            output.Dispose();
                            output = null;
                            file.Dispose();
                            input.Dispose();

                            string backupFile = filename + ".bak";
                            if (File.Exists (backupFile))
                                File.Delete (backupFile);
                            File.Move (filename, backupFile);

                            try
                            {
                                File.Move (tempFile, filename);
                                File.Delete (backupFile);
                            }
                            catch
                            {
                                // Restore backup on failure
                                if (File.Exists (backupFile))
                                {
                                    if (File.Exists (filename))
                                        File.Delete (filename);
                                    File.Move (backupFile, filename);
                                }
                                throw;
                            }
                        }
                    }
                }
                catch
                {
                    output?.Dispose();
                    output = null;

                    if (tempFile != null && File.Exists (tempFile))
                    {
                        try { File.Delete (tempFile); } catch { }
                    }
                    else if (!overwritingSource && File.Exists (targetName))
                    {
                        try { File.Delete (targetName); } catch { }
                    }
                    throw;
                }
                finally
                {
                    output?.Dispose();
                }
            }
        }

        /*
        void ConvertVideo (string filename)
        {
            using (var file = BinaryStream.FromFile (filename))
            using (var input = VideoFormat.Read (file))
            {
                if (null == input)
                    return;

                string output_name = Path.GetFileName (filename);
                var source_ext = Path.GetExtension (filename).TrimStart ('.').ToLowerInvariant();
                string source_format = input.SourceFormat;

                if (m_video_format != null)
                {
                    string target_ext = m_video_format.Extensions.FirstOrDefault();
                    if (target_ext != null)
                    {
                        output_name = Path.ChangeExtension (output_name, target_ext);
                        using (var output = CreateNewFile (output_name))
                        {
                            m_video_format.Write (input, output);
                        }
                        return;
                    }
                }

                if (CommonVideoFormats.Contains (source_format))
                {
                    if (source_ext == source_format)
                        return;
                    output_name = Path.ChangeExtension (output_name, source_format);
                    using (var output = CreateNewFile (output_name))
                    {
                        input.Source.Position = 0;
                        input.Source.CopyTo (output);
                    }
                }
                else
                {
                    // Default to MP4 for unknown video formats
                    if (source_ext == "mp4")
                        return;
                    output_name = Path.ChangeExtension (output_name, "mp4");
                    using (var output = CreateNewFile (output_name))
                        VideoFormat.Mp4.Write (input, output);
                }
            }
        }
        */

        void ConvertImage (string filename)
        {
            string source_ext = Path.GetExtension (filename).TrimStart('.').ToLowerInvariant();
            string target_name = Path.GetFileName (filename);
            string target_ext = m_image_format.Extensions.FirstOrDefault();
            target_name = Path.ChangeExtension (target_name, target_ext);

            using (var file = BinaryStream.FromFile (filename))
            {
                var src_format = ImageFormat.FindFormat (file);
                if (null == src_format)
                    return;

                bool overwritingSource = AreSamePaths (filename, target_name);
                string tempFile = null;

                Stream output = null;
                try
                {
                    if (!ForceConversion && src_format.Item1 == m_image_format && m_image_format.Extensions.Any (ext => ext == source_ext))
                    {
                        if (overwritingSource)
                            return;
                        output = CreateNewFile (target_name);
                        file.Position = 0;
                        file.AsStream.CopyTo (output);
                    }
                    else
                    {
                        if (overwritingSource)
                        {
                            tempFile = Path.GetTempFileName();
                            output = File.Create (tempFile);
                        }
                        else
                        {
                            output = CreateNewFile (target_name);
                        }

                        file.Position = 0;
                        var image = src_format.Item1.Read (file, src_format.Item2);
                        m_image_format.Write (output, image);

                        if (tempFile != null)
                        {
                            output.Dispose();
                            output = null;
                            file.Dispose();

                            string backupFile = filename + ".bak";
                            if (File.Exists (backupFile)) File.Delete (backupFile);
                            File.Move (filename, backupFile);

                            try
                            {
                                File.Move (tempFile, filename);
                                File.Delete (backupFile);
                            }
                            catch
                            {
                                if (File.Exists (backupFile))
                                {
                                    if (File.Exists (filename)) File.Delete (filename);
                                    File.Move (backupFile, filename);
                                }
                                throw;
                            }
                        }
                    }
                }
                catch
                {
                    output?.Dispose();
                    output = null;
                    if (tempFile != null && File.Exists (tempFile))
                    {
                        try { File.Delete (tempFile); } catch { }
                    }
                    else if (!overwritingSource && File.Exists (target_name))
                    {
                        try { File.Delete (target_name); } catch { }
                    }
                    throw;
                }
                finally
                {
                    output?.Dispose();
                }
            }
        }

        void OnConvertComplete (object sender, RunWorkerCompletedEventArgs e)
        {
            m_main.ResumeWatchDirectoryChanges();
            m_progress_dialog.Dispose();
            if (null != m_pending_error)
            {
                if (m_pending_error is OperationCanceledException)
                    m_main.SetFileStatus (m_pending_error.Message);
                else
                    m_main.PopupError (m_pending_error.Message, Localization._T("TextMediaConvertError"));
            }
            m_main.Activate();
            m_main.ListViewFocus();
            m_main.RefreshView();
            m_converting = false;
        }

        static internal bool AreSamePaths (string filename1, string filename2)
        {
            filename1 = Path.GetFullPath (filename1);
            filename2 = Path.GetFullPath (filename2);
            return string.Equals (filename1, filename2, StringComparison.OrdinalIgnoreCase);
        }
    }
}
