using System;
using System.Collections.Generic;
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

        private bool IsTextConverterImplemented (ScriptFormat format)
        {
            var formatType = format.GetType();

            var convertFromMethod = formatType.GetMethod ("ConvertFrom",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var baseConvertFromMethod = typeof (GenericScriptFormat).GetMethod ("ConvertFrom",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            bool isOverridden = convertFromMethod != null &&
                convertFromMethod.DeclaringType != typeof (GenericScriptFormat);
            return isOverridden;
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
                    return new TextLoadResult {
                        Error = Localization.Format ("FileTooLarge", stream.Length),
                        IsFileTooLarge = true
                    };
                }

                cancellationToken.ThrowIfCancellationRequested();

                ScriptFormat format = ScriptFormat.FindFormat (binaryStream);
                if (format != null)
                {
                    // NOTE: shouldn't dispose for simplified derivatives of GenericScriptFormat
                    // other formats should create a new stream by themselves in ConvertFrom
                    bool hasCustomConverter = IsTextConverterImplemented (format);
                    shouldDisposeStream = hasCustomConverter;
                    return LoadScriptFile (entry, binaryStream, format, preferredEncoding, hasCustomConverter, cancellationToken);
                }
                else
                {
                    var result = LoadPlainTextOrBinary (entry, stream, preferredEncoding, cancellationToken);
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
                bool isUTF8 = (0xEF == test_buf[0] && 0xBB == test_buf[1] && 0xBF == test_buf[2]);
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

        private TextLoadResult LoadScriptFile (Entry entry, IBinaryStream binaryStream, ScriptFormat format,
            Encoding preferredEncoding, bool hasCustomConverter, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            binaryStream.Position = 0;

            Encoding encodingToUse;

            if (hasCustomConverter)
                encodingToUse = Encoding.UTF8; // custom converters must output UTF8
            else
            {
                if (preferredEncoding != null)
                    encodingToUse = preferredEncoding;
                else
                {
                    encodingToUse = ScriptFormat.DetectEncoding (binaryStream.AsStream, Math.Min (binaryStream.AsStream.Length, 20000));
                    binaryStream.Position = 0;
                }
            }

            var displayStream = format.ConvertFrom (binaryStream);

            cancellationToken.ThrowIfCancellationRequested();

            return new TextLoadResult {
                ContentStream  = displayStream,
                Encoding       = encodingToUse,
                StatusText     = format.Description,
                IsScript       = true,
                KeepStreamOpen = !hasCustomConverter
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
            return new TextLoadResult {
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
                    if (c == '\uFFFD' || c == '\uFFFF') // Unicode Replacement or Non-character characters
                        replacementCount++;
                }
                return replacementCount == 0;
            }
            catch
            {
                return false;
            }
        }

        private TextLoadResult GenerateHexDump (Stream stream, string filename, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string displayName = VFS.GetFileName (filename);
            var hexStream = new HexDumpStream (stream, displayName, cancellationToken);

            return new TextLoadResult {
                ContentStream = hexStream,
                Encoding = Encoding.UTF8,
                StatusText = Localization.Format ("HexDumpOf", displayName, stream.Length),
                IsHexDump = true,
                KeepStreamOpen = true
            };
        }

        public void CancelCurrentLoad()
        {
            _currentLoadCts?.Cancel();
            _currentLoadCts?.Dispose();
            _currentLoadCts = null;
        }
    }

    internal class HexDumpStream : Stream
    {
        private readonly Stream _sourceStream;
        private readonly CancellationToken _cancellationToken;
        private readonly byte[] _header;
        private readonly byte[] _lineBuffer;
        private readonly byte[] _sourceBuffer;
        private readonly Dictionary<int, CachedLine> _generatedLines;
        private readonly int _bytesPerLine;

        private long _position;
        private long _length;
        private  int _currentSourceLine;
        private  int _currentLineOffset;
        private bool _disposed;

        private class CachedLine
        {
            public byte[] Data;
            public int Length;
        }

        public HexDumpStream (Stream sourceStream, string displayName, CancellationToken cancellationToken)
        {
            _sourceStream = sourceStream ?? throw new ArgumentNullException (nameof(sourceStream));
            _cancellationToken = cancellationToken;
            _sourceBuffer = new byte[16];
            _lineBuffer = new byte[512];
            _generatedLines = new Dictionary<int, CachedLine>();

            var headerText = string.Format ("{0}\n{1}\n",
                Localization._T ("HexHeader0"),
                Localization._T ("HexHeader1"));
            _header = Encoding.UTF8.GetBytes (headerText);
            _bytesPerLine = 78;

            long sourceLines = (_sourceStream.Length + 15) / 16;
            _length = _header.Length + (sourceLines * _bytesPerLine);

            _position = 0;
            _currentSourceLine = 0;
            _currentLineOffset = 0;
        }

        public override bool  CanRead { get { return  true; } }
        public override bool  CanSeek { get { return  true; } }
        public override bool CanWrite { get { return  false; } }
        public override long   Length { get { return _length; } }
        public override long Position
        {
            get { return _position; }
            set 
            { 
                if (value < 0 || value > _length)
                    throw new ArgumentOutOfRangeException (nameof(value));

                _position = value;

                if (value < _header.Length)
                {
                    _currentSourceLine = 0;
                    _currentLineOffset = 0;
                }
                else
                {
                    long afterHeader = value - _header.Length;
                    _currentSourceLine = (int)(afterHeader / _bytesPerLine);
                    _currentLineOffset = (int)(afterHeader % _bytesPerLine);
                }
            }
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            if (buffer == null)
                throw new ArgumentNullException (nameof(buffer));
            if (offset < 0 || count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException();

            int totalRead = 0;

            if (_position < _header.Length)
            {
                int headerStart = (int)_position;
                int headerBytesToCopy = Math.Min (count, _header.Length - headerStart);
                Buffer.BlockCopy (_header, headerStart, buffer, offset, headerBytesToCopy);
                _position += headerBytesToCopy;
                totalRead += headerBytesToCopy;
                offset += headerBytesToCopy;
                count -= headerBytesToCopy;

                if (_position >= _header.Length)
                {
                    _currentSourceLine = 0;
                    _currentLineOffset = 0;
                }
            }

            while (count > 0)
            {
                int sourceOffset = _currentSourceLine * 16;
                if (sourceOffset >= _sourceStream.Length)
                    break;

                _cancellationToken.ThrowIfCancellationRequested();

                if (!_generatedLines.TryGetValue (_currentSourceLine, out var cachedLine))
                {
                    _sourceStream.Position = sourceOffset;
                    int bytesRead = _sourceStream.Read (_sourceBuffer, 0, 16);
                    if (bytesRead == 0)
                        break;

                    int lineLength = HexDumpFormatter.FormatHexLine (_sourceBuffer, bytesRead, sourceOffset, _lineBuffer);

                    cachedLine = new CachedLine
                    {
                        Data = new byte[lineLength],
                        Length = lineLength
                    };
                    Buffer.BlockCopy (_lineBuffer, 0, cachedLine.Data, 0, lineLength);
                    _generatedLines[_currentSourceLine] = cachedLine;
                }

                int availableInLine = cachedLine.Length - _currentLineOffset;
                int toCopy = Math.Min (count, availableInLine);

                Buffer.BlockCopy (cachedLine.Data, _currentLineOffset, buffer, offset, toCopy);

                _currentLineOffset += toCopy;
                _position += toCopy;
                totalRead += toCopy;
                offset += toCopy;
                count -= toCopy;

                if (_currentLineOffset >= cachedLine.Length)
                {
                    _currentSourceLine++;
                    _currentLineOffset = 0;
                }
            }

            return totalRead;
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            long newPosition;

            if (origin == SeekOrigin.Begin)
                newPosition = offset;
            else if (origin == SeekOrigin.Current)
                newPosition = _position + offset;
            else if (origin == SeekOrigin.End)
                newPosition = _length + offset;
            else
                throw new ArgumentException ("Invalid seek origin", nameof(origin));

            Position = newPosition;
            return _position;
        }

        public override void Flush() { }

        public override void SetLength (long value)
        {
            throw new NotSupportedException();
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose (bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                    _sourceStream?.Dispose();
                _disposed = true;
            }
            base.Dispose (disposing);
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