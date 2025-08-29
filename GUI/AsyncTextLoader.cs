using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameRes;

namespace GARbro.GUI.Preview
{
    public class AsyncTextLoader
    {
        private CancellationTokenSource _currentLoadCts;
        private Task<TextLoadResult> _currentLoadTask;

        const uint MAX_FILE_PREVIEW = 128 * 1024 * 1024;
        const uint MAX_FILE_HEXDUMP = 1024 * 1024;

        public async Task<TextLoadResult> LoadTextAsync (Entry entry, Encoding preferredEncoding, CancellationToken cancellationToken = default)
        {
            CancelCurrentLoad();

            _currentLoadCts = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
            var cts = _currentLoadCts;

            try
            {
                _currentLoadTask = Task.Run(() => LoadTextCore (entry, preferredEncoding, cts.Token), cts.Token);
                return await _currentLoadTask;
            }
            catch (Exception ex) when (ex is ObjectDisposedException ||
                                       ex is OperationCanceledException ||
                                       ex is OutOfMemoryException)
            {
                return LoadResultBase.CreateCancelled<TextLoadResult>();
            }
            catch (Exception ex)
            {
                return LoadResultBase.CreateError<TextLoadResult>(ex.Message);
            }
            finally
            {
                if (_currentLoadCts == cts)
                {
                    _currentLoadCts  = null;
                    _currentLoadTask = null;
                }
            }
        }

        private TextLoadResult LoadTextCore (Entry entry, Encoding preferredEncoding, CancellationToken cancellationToken)
        {
            Stream stream = null;
            IBinaryStream binaryStream = null;
            bool shouldDisposeStream   = true;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                binaryStream = VFS.OpenBinaryStream (entry);
                stream = binaryStream.AsStream;

                if (stream.Length > MAX_FILE_PREVIEW)
                {
                    return new TextLoadResult 
                    { 
                        Error = Localization.Format ("FileTooLarge", stream.Length),
                        IsFileTooLarge = true 
                    };
                }

                cancellationToken.ThrowIfCancellationRequested();

                ScriptFormat format = ScriptFormat.FindFormat (binaryStream);
                if (format != null)
                    return LoadScriptFile (entry, stream, format, preferredEncoding, cancellationToken);
                else
                { 
                    var result =  LoadPlainTextOrBinary (entry, stream, preferredEncoding, cancellationToken);
                    shouldDisposeStream = !result.KeepStreamOpen;
                    return result;
                }
            }
            catch (Exception ex) when (ex is ObjectDisposedException ||
                                       ex is OperationCanceledException ||
                                       ex is AccessViolationException)
            {
                return LoadResultBase.CreateCancelled<TextLoadResult>();
            }
            catch (Exception ex)
            {
                return LoadResultBase.CreateError<TextLoadResult>(ex.Message);
            }
            finally
            {
                if (shouldDisposeStream)
                    stream?.Dispose();
            }
        }

        private bool CheckIfTextFile (Stream file)
        {
            byte[] test_buf = new byte[0x400];
            int read = file.Read (test_buf, 0, Math.Min (test_buf.Length, (int)file.Length));
            file.Position = 0;

            // Check for BOM
            if (read > 3)
            {
                bool isUTF8    = (0xEF == test_buf[0] && 0xBB == test_buf[1] && 0xBF == test_buf[2]);
                bool isUTF16LE = (0xFF == test_buf[0] && 0xFE == test_buf[1]);
                bool isUTF16BE = (0xFE == test_buf[0] && 0xFF == test_buf[1]);

                if (isUTF8 || isUTF16LE || isUTF16BE)
                    return true;
            }

            // Check for binary characters
            bool found_eol = false;
            for (int i = 0; i < read; ++i)
            {
                byte c = test_buf[i];
                if (c < 9 || (c > 0x0d && c < 0x1a) || (c > 0x1b && c < 0x20))
                    return false;
                found_eol = found_eol || 0x0A == c;
            }
            return found_eol || read < 80;
        }

        private TextLoadResult LoadScriptFile (Entry entry, Stream stream, ScriptFormat format,
    Encoding preferredEncoding, CancellationToken cancellationToken)
        {
            stream.Position = 0;
            ScriptData scriptData = null;
            Encoding detectedEncoding = null;

            cancellationToken.ThrowIfCancellationRequested();

            if (preferredEncoding != null)
            {
                try
                {
                    scriptData = format.Read (entry.Name, stream, preferredEncoding);
                    if (scriptData != null)
                        detectedEncoding = preferredEncoding;
                }
                catch
                {
                    stream.Position = 0;
                    scriptData = null;
                }
            }

            if (scriptData == null)
            {
                scriptData = format.Read (entry.Name, stream);
                detectedEncoding = scriptData?.Encoding;
            }

            if (scriptData == null)
            {
                return new TextLoadResult { Error = Localization._T ("FailedToReadScript") };
            }

            cancellationToken.ThrowIfCancellationRequested();

            var displayStream = new MemoryStream();
            scriptData.Serialize (displayStream);
            displayStream.Position = 0;

            string scriptInfo = string.Format ("{0} - {1}",
                format.Description,
                scriptData.GetNewLineInfo());

            return new TextLoadResult
            {
                ContentStream = displayStream,
                Encoding = detectedEncoding,
                StatusText = scriptInfo,
                IsScript = true
            };
        }

        private TextLoadResult LoadPlainTextOrBinary (Entry entry, Stream stream, Encoding preferredEncoding,
    CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool isTextFile = CheckIfTextFile (stream);

            if (!isTextFile && !(entry.Type == "script" || entry.Type == "text"))
            {
                // Binary file - generate hex dump
                if (stream.Length <= MAX_FILE_HEXDUMP)
                    return GenerateHexDump (stream, entry.Name, cancellationToken);
                else
                    return new TextLoadResult {
                        Error = Localization.Format ("BinaryFileTooLarge", stream.Length),
                        IsBinaryTooLarge = true
                    };
            }

            cancellationToken.ThrowIfCancellationRequested();

            stream.Position = 0;
            var detectedEncoding = ScriptFormat.DetectEncoding (stream, Math.Min (stream.Length, 20000));

            var encodingToUse = preferredEncoding ?? detectedEncoding;
            if (!IsEncodingCompatible (stream, encodingToUse) && detectedEncoding != preferredEncoding)
                encodingToUse = detectedEncoding;

            stream.Position = 0;
            return new TextLoadResult
            {
                ContentStream = stream,
                Encoding = encodingToUse,
                KeepStreamOpen = true,
                AutoEncoding = detectedEncoding != preferredEncoding
            };
        }

        private bool IsEncodingCompatible (Stream stream, Encoding encoding)
        {
            try
            {
                var buffer = new byte[Math.Min (1024, stream.Length)];
                stream.Position = 0;
                int bytesRead = stream.Read (buffer, 0, buffer.Length);
                stream.Position = 0;

                var decoder = encoding.GetDecoder();
                var charBuffer = new char[decoder.GetCharCount (buffer, 0, bytesRead)];
                decoder.GetChars (buffer, 0, bytesRead, charBuffer, 0);

                int replacementCount = 0;
                foreach (char c in charBuffer)
                {
                    if (c == '\uFFFD') // Unicode replacement character
                        replacementCount++;
                }

                // Allow up to 1% replacement characters
                return replacementCount < (charBuffer.Length / 100);
            }
            catch
            {
                return false;
            }
        }

        private TextLoadResult GenerateHexDump (Stream stream, string filename, CancellationToken cancellationToken)
        {
            var sb = new StringBuilder();
            var buffer = new byte[16];
            int offset = 0;

            sb.AppendLine (Localization._T ("HexHeader0"));
            sb.AppendLine (Localization._T ("HexHeader1"));

            stream.Position = 0;
            int bytesRead;

            while ((bytesRead = stream.Read (buffer, 0, 16)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                sb.AppendFormat ("{0:X8}  ", offset);

                for (int i = 0; i < 16; i++)
                {
                    if (i < bytesRead)
                        sb.AppendFormat ("{0:X2} ", buffer[i]);
                    else
                        sb.Append ("   ");
                }

                sb.Append (" | ");

                for (int i = 0; i < bytesRead; i++)
                {
                    byte b = buffer[i];
                    if (b >= 0x20 && b < 0x7F)
                        sb.Append((char)b);
                    else
                        sb.Append('.');
                }

                sb.AppendLine();
                offset += bytesRead;
            }

            var hexDump = sb.ToString();
            var hexStream = new MemoryStream (Encoding.UTF8.GetBytes (hexDump));
            string displayName = VFS.GetFileName (filename);

            return new TextLoadResult
            {
                ContentStream = hexStream,
                Encoding = Encoding.UTF8,
                StatusText = Localization.Format ("HexDumpOf", displayName, stream.Length),
                IsHexDump = true
            };
        }

        public void CancelCurrentLoad()
        {
            _currentLoadCts?.Cancel();
            _currentLoadCts?.Dispose();
            _currentLoadCts = null;
        }
    }

    public class TextLoadResult : LoadResultBase
    {
        public Stream  ContentStream { get; set; }
        public Encoding     Encoding { get; set; }
        public bool     AutoEncoding { get; set; }
        public string     StatusText { get; set; }
        public bool         IsScript { get; set; }
        public bool        IsHexDump { get; set; }
        public bool   KeepStreamOpen { get; set; }
        public bool   IsFileTooLarge { get; set; }
        public bool IsBinaryTooLarge { get; set; }
    }
}