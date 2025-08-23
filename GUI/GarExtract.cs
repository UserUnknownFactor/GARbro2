using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

using GameRes;
using GARbro.GUI.Properties;

namespace GARbro.GUI
{
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Handle "Extract item" command.
        /// </summary>
        private void ExtractItemExec (object sender, ExecutedRoutedEventArgs e)
        {
            var entry = CurrentDirectory.SelectedItem as EntryViewModel;
            if (null == entry && !ViewModel.IsArchive)
            {
                SetFileStatus (Localization._T("MsgChooseFiles"));
                return;
            }
            GarExtract extractor = null;
            try
            {
                string destination = Settings.Default.appLastDestination;
                if (!Directory.Exists (destination))
                    destination = "";
                var vm = ViewModel;
                if (vm.IsArchive)
                {
                    if (string.IsNullOrEmpty (destination))
                        destination = Path.GetDirectoryName (vm.Path.First());
                    var archive_name = vm.Path[vm.Path.Count-2];
                    extractor = new GarExtract (this, archive_name, VFS.Top as ArchiveFileSystem);
                    if (null == entry || (entry.Name == VFS.DIR_PARENT && string.IsNullOrEmpty (vm.Path.Last()))) // root entry
                        extractor.ExtractAll (destination);
                    else
                        extractor.Extract (entry, destination);
                }
                else if (!entry.IsDirectory)
                {
                    if (entry.Type == "image" || 
                        entry.Type == "audio" || 
                        entry.Type == "video")
                    {
                        // Use format conversion instead of extraction
                        ConvertSingleFile (entry);
                        return;
                    }

                    var source = entry.Source.Name;
                    SetBusyState();

                    // extract into dir named after it when clicking on top-level archive
                    if (string.IsNullOrEmpty (destination))
                    {
                        destination = Path.GetDirectoryName (source);
                    }

                    var archiveName = Path.GetFileNameWithoutExtension (source);
                    destination = Path.Combine (destination, archiveName);

                    extractor = new GarExtract (this, source);
                    extractor.ExtractAll (destination);
                }
            }
            catch (OperationCanceledException X)
            {
                SetFileStatus (X.Message);
            }
            catch (Exception X)
            {
                PopupError (X.Message, Localization._T("MsgErrorExtracting"));
            }
            finally
            {
                if (null != extractor && !extractor.IsActive)
                    extractor.Dispose();
            }
        }

        /// <summary>
        /// Convert a single image or audio file using the conversion dialog.
        /// </summary>
        private void ConvertSingleFile (EntryViewModel entry)
        {
            if (entry == null || (entry.Type != "image" && entry.Type != "audio"))
                return;

            var convert_dialog = new ConvertMedia();

            string destination = Path.GetDirectoryName (entry.Source.Name);
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

                var entries = new List<Entry> { entry.Source };

                if (entry.Type == "image")
                {
                    var imageFormat = convert_dialog.ImageConversionFormat.SelectedItem as ImageFormat;
                    if (imageFormat != null)
                        converter.ConvertImages (entries, imageFormat);
                }
                else if (entry.Type == "audio")
                {
                    var audioFormat = convert_dialog.AudioConversionFormat.SelectedItem as AudioFormat;
                    if (audioFormat != null)
                        converter.ConvertAudios (entries, audioFormat);
                }

                Settings.Default.appLastDestination = destination;
                SetFileStatus (Localization.Format("Converted", entry.Name));
            }
            catch (Exception X)
            {
                PopupError (X.Message, Localization._T("TextMediaConvertError"));
            }
        }
    }

    sealed internal class GarExtract : GarOperation, IDisposable
    {
        private string              m_arc_name;
        private ArchiveFileSystem   m_fs;
        private readonly bool       m_should_ascend;
        private bool                m_skip_images = false;
        private bool                m_skip_script = false;
        private bool                m_skip_audio  = false;
        private bool                m_adjust_image_offset = false;
        private bool                m_convert_audio;
        private ImageFormat         m_image_format;
        private int                 m_extract_count;
        private int                 m_skip_count;
        private bool                m_extract_in_progress = false;

        public bool IsActive { get { return m_extract_in_progress; } }

        public GarExtract (MainWindow parent, string source) : base (parent, Localization._T("TextExtractionError"))
        {
            m_arc_name = Path.GetFileName (source);
            try
            {
                VFS.ChDir (source);
                m_should_ascend = true;
            }
            catch (Exception X)
            {
                throw new OperationCanceledException (string.Format ("{1}: {0}", X.Message, m_arc_name));
            }
            m_fs = VFS.Top as ArchiveFileSystem;
        }

        public GarExtract (MainWindow parent, string source, ArchiveFileSystem fs) : base (parent, Localization._T("TextExtractionError"))
        {
            if (null == fs)
                throw new UnknownFormatException();
            m_fs = fs;
            m_arc_name = Path.GetFileName (source);
            m_should_ascend = false;
        }

        private void PrepareDestination (string destination)
        {
            bool stop_watch = !m_main.ViewModel.IsArchive;
            if (stop_watch)
                m_main.StopWatchDirectoryChanges();
            try
            {
                Directory.CreateDirectory (destination);
                Directory.SetCurrentDirectory (destination);
                Settings.Default.appLastDestination = destination;
            }
            finally
            {
                if (stop_watch)
                    m_main.ResumeWatchDirectoryChanges();
            }
        }

        public void ExtractAll (string destination)
        {
            var file_list = m_fs.GetFilesRecursive();
            if (!file_list.Any())
            {
                m_main.SetFileStatus (string.Format ("{1}: {0}", Localization._T("MsgEmptyArchive"), m_arc_name));
                return;
            }
            var extractDialog = new ExtractArchiveDialog (m_arc_name, destination);
            extractDialog.Owner = m_main;
            var result = extractDialog.ShowDialog();
            if (!result.Value)
                return;

            destination = extractDialog.Destination;
            if (!string.IsNullOrEmpty (destination))
            {
                destination = Path.GetFullPath (destination);
                PrepareDestination (destination);
            }
            else
                destination = ".";
            m_skip_images = !extractDialog.ExtractImages.IsChecked.Value;
            m_skip_script = !extractDialog.ExtractText.IsChecked.Value;
            m_skip_audio  = !extractDialog.ExtractAudio.IsChecked.Value;
            if (!m_skip_images)
                m_image_format = extractDialog.GetImageFormat (extractDialog.ImageConversionFormat);

            m_main.SetFileStatus (Localization.Format ("MsgExtractingTo", m_arc_name, destination));
            ExtractFilesFromArchive (Localization.Format ("MsgExtractingArchive", m_arc_name), file_list);
        }

        public void Extract (EntryViewModel entry, string destination)
        {
            var view_model = m_main.ViewModel;
            var selected = m_main.CurrentDirectory.SelectedItems.Cast<EntryViewModel>();
            if (!selected.Any() && entry.Name == VFS.DIR_PARENT)
                selected = view_model;

            IEnumerable<Entry> file_list = selected.Select (e => e.Source);
            if (m_fs is TreeArchiveFileSystem)
                file_list = (m_fs as TreeArchiveFileSystem).GetFilesRecursive (file_list);

            if (!file_list.Any())
            {
                m_main.SetFileStatus (Localization._T("MsgChooseFiles"));
                return;
            }

            ExtractDialog extractDialog;
            bool multiple_files = file_list.Skip (1).Any();
            if (multiple_files)
                extractDialog = new ExtractArchiveDialog (m_arc_name, destination);
            else
                extractDialog = new ExtractFile (entry, destination);
            extractDialog.Owner = m_main;
            var result = extractDialog.ShowDialog();
            if (!result.Value)
                return;
            if (multiple_files)
            {
                m_skip_images = !Settings.Default.appExtractImages;
                m_skip_script = !Settings.Default.appExtractText;
                m_skip_audio  = !Settings.Default.appExtractAudio;
            }
            destination = extractDialog.Destination;
            if (!string.IsNullOrEmpty (destination))
            {
                destination = Path.GetFullPath (destination);
                PrepareDestination (destination);
            }
            if (!m_skip_images)
                m_image_format = FormatCatalog.Instance.ImageFormats.FirstOrDefault (f => f.Tag.Equals (Settings.Default.appImageFormat));

            ExtractFilesFromArchive (Localization.Format ("MsgExtractingFile", m_arc_name), file_list);
        }

        private void ExtractFilesFromArchive (string text, IEnumerable<Entry> file_list)
        {
            file_list = file_list.Where (e => e.Offset >= 0);
            if (file_list.Skip (1).Any() // file_list.Count() > 1
                && (m_skip_images || m_skip_script || m_skip_audio))
                file_list = file_list.Where (f => !(m_skip_images && f.Type == "image") && 
                                                  !(m_skip_script && f.Type == "script") &&
                                                  !(m_skip_audio  && f.Type == "audio"));
            if (!file_list.Any())
            {
                m_main.SetFileStatus (string.Format ("{1}: {0}", Localization._T("MsgNoFiles"), m_arc_name));
                return;
            }
            file_list = file_list.OrderBy (e => e.Offset);
            m_progress_dialog = new ProgressDialog ()
            {
                WindowTitle = Localization._T("TextTitle"),
                Text        = text,
                Description = "",
                MinimizeBox = true,
            };
            if (!file_list.Skip (1).Any()) // 1 == file_list.Count()
            {
                m_progress_dialog.Description = file_list.First().Name;
                m_progress_dialog.ProgressBarStyle = ProgressBarStyle.MarqueeProgressBar;
            }
            m_convert_audio = !m_skip_audio && Settings.Default.appConvertAudio;
            m_progress_dialog.DoWork += (s, e) => ExtractWorker (file_list);
            m_progress_dialog.RunWorkerCompleted += OnExtractComplete;
            m_main.IsEnabled = false;
            m_progress_dialog.ShowDialog (m_main);
            m_extract_in_progress = true;
        }

        void ExtractWorker (IEnumerable<Entry> file_list)
        {
            m_extract_count = 0;
            m_skip_count = 0;
            var arc = m_fs.Source;
            int total = file_list.Count();
            int progress_count = 0;
            bool ignore_errors = false;
            foreach (var entry in file_list)
            {
                if (m_progress_dialog.CancellationPending)
                    break;
                if (total > 1)
                    m_progress_dialog.ReportProgress (progress_count++*100/total, null, entry.Name);
                try
                {
                    if (null != m_image_format && entry.Type == "image")
                        ExtractImage (arc, entry, m_image_format);
                    else if (m_convert_audio && entry.Type == "audio")
                        ExtractAudio (arc, entry);
                    else
                        ExtractEntryAsIs (arc, entry);
                    ++m_extract_count;
                }
                catch (SkipExistingFileException)
                {
                    ++m_skip_count;
                    continue;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception X)
                {
                    if (!ignore_errors)
                    {
                        var error_text = Localization.Format ("TextErrorExtracting", entry.Name, X.Message);
                        var result = ShowErrorDialog (error_text);
                        if (!result.Continue)
                            break;
                        ignore_errors = result.IgnoreErrors;
                    }
                    ++m_skip_count;
                }
            }
        }

        void ExtractEntryAsIs (ArcFile arc, Entry entry)
        {
            using (var input = arc.OpenEntry (entry))
            using (var output = CreateNewFile (entry.Name, true))
                input.CopyTo (output);
        }

        void ExtractImage (ArcFile arc, Entry entry, ImageFormat target_format)
        {
            using (var decoder = arc.OpenImage (entry))
            {
                var src_format = decoder.SourceFormat; // could be null
                string target_ext = target_format.Extensions.FirstOrDefault() ?? "";
                string outname = Path.ChangeExtension (entry.Name, target_ext);
                if (src_format == target_format)
                {
                    // source format is the same as a target, copy file as is
                    using (var output = CreateNewFile (outname, true))
                        decoder.Source.CopyTo (output);
                    return;
                }
                ImageData image = decoder.Image;
                if (m_adjust_image_offset)
                {
                    image = AdjustImageOffset (image);
                }
                using (var outfile = CreateNewFile (outname, true))
                {
                    target_format.Write (outfile, image);
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
            int src_stride = (int)image.Width * (image.BPP+7) / 8;
            int dst_stride = width * (image.BPP+7) / 8;
            var pixels = new byte[height*dst_stride];
            int offset = y * dst_stride + x * image.BPP / 8;
            Int32Rect rect = new Int32Rect (src_x, src_y, (int)image.Width - src_x, 1);

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
                if (null == sound)
                    throw new InvalidFormatException (Localization._T("MsgUnableInterpretAudio"));
                ConvertAudio (entry.Name, sound);
            }
        }

        public void ConvertAudio (string filename, SoundInput input)
        {
            string source_format = input.SourceFormat;
            if (GarConvertMedia.CommonAudioFormats.Contains (source_format))
            {
                var output_name = Path.ChangeExtension (filename, source_format);
                using (var output = CreateNewFile (output_name, true))
                {
                    input.Source.Position = 0;
                    input.Source.CopyTo (output);
                }
            }
            else
            {
                var output_name = Path.ChangeExtension (filename, "wav");
                using (var output = CreateNewFile (output_name, true))
                    AudioFormat.Wav.Write (input, output);
            }
        }

        void OnExtractComplete (object sender, RunWorkerCompletedEventArgs e)
        {
            m_main.IsEnabled = true;
            m_extract_in_progress = false;
            m_progress_dialog.Dispose();
            m_main.Activate();
            m_main.ListViewFocus();
            if (!m_main.ViewModel.IsArchive)
                m_main.Dispatcher.Invoke (m_main.RefreshView);

            m_main.SetFileStatus (m_extract_count.Pluralize ("MsgExtractedFiles"));
            this.Dispose();
        }

        #region IDisposable Members
        bool disposed = false;

        public void Dispose ()
        {
            if (!disposed)
            {
                if (m_should_ascend)
                {
                    VFS.ChDir (VFS.DIR_PARENT);
                }
                disposed = true;
            }
            GC.SuppressFinalize (this);
        }
        #endregion
    }
}
