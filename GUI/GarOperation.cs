using System;
using System.IO;

namespace GARbro.GUI
{
    internal class GarOperation
    {
        internal MainWindow             m_main;
        internal ProgressDialog         m_progress_dialog;
        internal Exception              m_pending_error;
        internal FileExistsDialogResult m_duplicate_action;
        internal string                 m_title;

        const int MaxRenameAttempts = 100;

        protected GarOperation (MainWindow parent, string dialog_title)
        {
            m_main = parent;
            m_title = dialog_title;
        }

        /// <summary>
        /// Create file <paramref name="filename"/>.  Also create path to file if <paramref name="create_path"/> is true.
        /// If file aready exists, popup dialog asking for necessary action.
        /// </summary>
        /// <remarks>
        /// WARNING: path to file should be relative, ArchiveFormat.CreatePath strips drive/root specification.
        /// </remarks>
        protected Stream CreateNewFile (string filename, bool create_path = false)
        {
            if (create_path)
                filename = GameRes.PhysicalFileSystem.CreatePath (filename);
            FileMode open_mode = FileMode.CreateNew;
            if (m_duplicate_action.ApplyToAll &&
                m_duplicate_action.Action == ExistingFileAction.Overwrite)
                open_mode = FileMode.Create;
            try
            {
                return File.Open (filename, open_mode);
            }
            catch (IOException) // file already exists?
            {
                if (!File.Exists (filename) || FileMode.Create == open_mode) // some unforseen I/O error, give up
                    throw;
            }
            if (!m_duplicate_action.ApplyToAll)
            {
                var msg_text = Localization.Format ("TextFileAlreadyExists", Path.GetFileName (filename));
                m_duplicate_action = m_main.Dispatcher.Invoke (() => m_main.ShowFileExistsDialog (m_title, msg_text, m_progress_dialog.GetWindowHandle()));
            }
            switch (m_duplicate_action.Action)
            {
            default:
            case ExistingFileAction.Abort:
                throw new OperationCanceledException();
            case ExistingFileAction.Skip:
                throw new SkipExistingFileException();
            case ExistingFileAction.Rename:
                return CreateRenamedFile (filename);
            case ExistingFileAction.Overwrite:
                return File.Open (filename, FileMode.Create);
            }
        }

        /// <summary>
        /// Creates new file with specified filename, or, if it's already exists, tries to open
        /// files named "FILENAME.1.EXT", "FILENAME.2.EXT" and so on.
        /// <exception cref="System.IOException">Throws exception after 100th failed attempt.</exception>
        /// </summary>

        public static Stream CreateRenamedFile (string filename)
        {
            var ext = Path.GetExtension (filename);
            for (int attempt = 1; ; ++attempt)
            {
                var name = Path.ChangeExtension (filename, attempt.ToString()+ext);
                try
                {
                    return File.Open (name, FileMode.CreateNew);
                }
                catch (IOException) // file already exists
                {
                    if (MaxRenameAttempts == attempt) // limit number of attempts
                        throw;
                }
            }
        }

        protected FileErrorDialogResult ShowErrorDialog (string error_text)
        {
            return m_main.Dispatcher.Invoke (() => m_main.ShowErrorDialog (m_title, error_text, m_progress_dialog.GetWindowHandle()));
        }
    }

    internal class SkipExistingFileException : ApplicationException
    {
    }
}
