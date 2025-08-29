using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using GameRes;

namespace GARbro.GUI.Preview
{
    public class FileIsTooBig : Exception
    {
        public FileIsTooBig () { }
        public FileIsTooBig (string message) : base (message) { }
        public FileIsTooBig (string message, Exception innerException) : base (message, innerException) { }
    }

    public class TextPreviewHandler : PreviewHandlerBase
    {
        private const uint MAX_FILE_PREVIEW = 128 * 1024 * 1024;
        private const uint MAX_FILE_HEXDUMP = 1024 * 1024;

        private readonly MainWindow _mainWindow;
        private readonly AsyncTextLoader _textLoader;
        private Stream _currentTextInput;
        private Encoding _currentEncoding;

        public override bool IsActive => _mainWindow.TextView.Visibility == Visibility.Visible;

        public TextPreviewHandler (MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            _textLoader = new AsyncTextLoader();
            _currentEncoding = null;
        }

        public override async Task LoadContentAsync (PreviewFile preview, CancellationToken cancellationToken)
        {
            DisposeTextInput();

            Encoding preferredEncoding = preview.PreferredEncoding;
            try
            {
                if (preview.Entry.Size > MAX_FILE_PREVIEW)
                {
                    _mainWindow.SetFileStatus (Localization.Format ("LoadingFile",
                        Localization._T ($"Type_text")));
                }

                var result = await _textLoader.LoadTextAsync (preview.Entry, preferredEncoding, cancellationToken);

                if (result.IsCancelled)
                    return;

                if (result.HasError)
                {
                    _mainWindow.SetFileStatus (result.Error);
                    Reset();
                    return;
                }

                await _mainWindow.Dispatcher.InvokeAsync(() =>
                {
                    _mainWindow.ShowTextPreview();

                    if (result.IsHexDump)
                    {
                        _mainWindow.EncodingChoice.SelectedItem = result.Encoding;
                        _mainWindow.EncodingChoice.IsEnabled = false;
                    }
                    else if (result.Encoding != null)
                        _mainWindow.EncodingChoice.SelectedItem = result.Encoding;

                    _currentEncoding = result.Encoding;
                    _mainWindow.TextView.DisplayStream (result.ContentStream, result.Encoding);

                    if (!string.IsNullOrEmpty (result.StatusText))
                        _mainWindow.SetPreviewStatus (result.StatusText);

                    _mainWindow.SetFileStatus ("");

                    if (result.KeepStreamOpen)
                        _currentTextInput = result.ContentStream;
                    else
                        result.ContentStream?.Dispose();
                });
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception ex)
            {
                _mainWindow.SetFileStatus (Localization.Format ("LoadingFailure", "", ex.Message));
                Reset();
            }
        }

        private void DisposeTextInput()
        {
            if (_currentTextInput != null)
            {
                _currentTextInput.Dispose();
                _currentTextInput = null;
            }
        }

        public override void Reset()
        {
            _textLoader.CancelCurrentLoad();

            _mainWindow.Dispatcher.Invoke(() =>
            {
                if (_mainWindow.EncodingChoice != null)
                    _mainWindow.EncodingChoice.IsEnabled = true;

                _mainWindow.TextView.Clear();
                _mainWindow.TextView.Visibility = Visibility.Collapsed;
            });

            DisposeTextInput();
            _currentEncoding = null;
        }

        protected override void Dispose (bool disposing)
        {
            if (!_disposed && disposing)
            {
                _textLoader.CancelCurrentLoad();
                Reset();
            }
            base.Dispose (disposing);
        }
    }
}