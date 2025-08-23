using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GameRes.Compression;

namespace GameRes.Formats.Audio
{
    [Export (typeof (ArchiveFormat))]
    public class CueAudioOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CUE/AUDIO"; } }
        public override string Description { get { return "Audio file with CUE sheet"; } }
        public override uint     Signature { get { return  0; } }
        public override bool  IsHierarchic { get { return  false; } }
        public override bool      CanWrite { get { return  false; } }

        static readonly string[] AudioExtensions = {
            ".wav", ".flac", ".ape", ".tta", ".wv", ".tak", ".mp3", ".ogg", ".m4a", ".aac", ".opus"
        };

        EncodingSetting CueEncoding = new EncodingSetting ("ZipEncodingCP", "DefaultEncoding"); // set same as zip for now

        public CueAudioOpener()
        {
            Settings = new[] { CueEncoding };
            Extensions = new[] { "cue" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            var ext = Path.GetExtension (file.Name).ToLowerInvariant();

            if (ext == ".cue")
            {
                return TryOpenCueFile (file);
            }

            // Check if this is an audio file with accompanying CUE
            if (!AudioExtensions.Contains (ext))
                return null;

            // Look for corresponding CUE file
            var cue_name = Path.ChangeExtension (file.Name, ".cue");
            if (!VFS.FileExists (cue_name))
            {
                cue_name = Path.ChangeExtension (file.Name, ".CUE");
                if (!VFS.FileExists (cue_name))
                    return null;
            }

            return TryOpenAudioWithCue (file, cue_name);
        }

        ArcFile TryOpenCueFile (ArcView cue_file)
        {
            CueSheet cue_sheet;
            string audio_file_name = null;

            try
            {
                using (var stream = cue_file.CreateStream())
                {
                    var encoding = CueEncoding.Get<Encoding>();
                    cue_sheet = ParseCueSheetForFile (stream, encoding, out audio_file_name);
                }
            }
            catch
            {
                return null;
            }

            if (cue_sheet == null || string.IsNullOrEmpty (audio_file_name))
                return null;

            string audio_path = null;
            var cue_dir = Path.GetDirectoryName (cue_file.Name);

            audio_path = Path.Combine (cue_dir, audio_file_name);
            if (!VFS.FileExists (audio_path))
            {
                audio_path = Path.Combine (cue_dir, Path.GetFileName (audio_file_name));
                if (!VFS.FileExists (audio_path))
                {
                    // Bruteforce common audio extensions
                    var base_name = Path.GetFileNameWithoutExtension (audio_file_name);
                    foreach (var ext in AudioExtensions)
                    {
                        audio_path = Path.Combine (cue_dir, base_name + ext);
                        if (VFS.FileExists (audio_path))
                            break;
                        audio_path = null;
                    }
                }
            }

            if (string.IsNullOrEmpty (audio_path) || !VFS.FileExists (audio_path))
                return null;

            // Open the audio file
            ArcView audio_view = null;
            AudioFormat audio_format = null;
            SoundInput audio_input = null;

            try
            {
                audio_view = VFS.OpenView (audio_path);

                // First, find the audio format
                using (var stream = audio_view.CreateStream())
                {
                    audio_format = FindAudioFormat (stream, audio_path);
                }

                if (audio_format == null)
                {
                    audio_view?.Dispose();
                    return null;
                }

                // Then open it with a fresh stream to get format info
                using (var stream = audio_view.CreateStream())
                {
                    audio_input = audio_format.TryOpen (stream);
                }

                // Complete the cue sheet parsing with audio info
                CompleteCueSheet (cue_sheet, audio_view, audio_input, audio_format);

                // Create directory entries
                var dir = CreateDirectoryEntries (cue_sheet, audio_format);

                return new CueAudioArchive (audio_view, this, dir, cue_sheet, audio_format, cue_file);
            }
            catch
            {
                audio_view?.Dispose();
                return null;
            }
            finally
            {
                audio_input?.Dispose();
            }
        }

        ArcFile TryOpenAudioWithCue (ArcView audio_file, string cue_name)
        {
            AudioFormat audio_format = null;
            SoundInput audio_input = null;

            try
            {
                // First find the format
                using (var stream = audio_file.CreateStream())
                {
                    audio_format = FindAudioFormat (stream, audio_file.Name);
                }

                if (audio_format == null)
                    return null;

                // Then open with fresh stream for format info
                using (var stream = audio_file.CreateStream())
                {
                    audio_input = audio_format.TryOpen (stream);
                }
            }
            catch
            {
                audio_input?.Dispose();
                return null;
            }

            CueSheet cue_sheet;
            try
            {
                using (var cue_stream = VFS.OpenStream (cue_name))
                {
                    var encoding = CueEncoding.Get<Encoding>();
                    cue_sheet = ParseCueSheet (cue_stream, audio_file, audio_input, audio_format, encoding);
                }
            }
            catch (Exception)
            {
                audio_input?.Dispose();
                return null;
            }
            finally
            {
                audio_input?.Dispose();
            }

            if (cue_sheet == null || cue_sheet.Tracks.Count == 0)
                return null;

            // Create directory entries
            var dir = CreateDirectoryEntries (cue_sheet, audio_format);

            return new CueAudioArchive (audio_file, this, dir, cue_sheet, audio_format, null);
        }

        List<Entry> CreateDirectoryEntries (CueSheet cue_sheet, AudioFormat audio_format)
        {
            var dir = new List<Entry>();
            foreach (var track in cue_sheet.Tracks)
            {
                var entry = new CueAudioEntry
                {
                    Name = track.Title ?? $"Track{track.Number:D2}",
                    Type = "audio",
                    Offset = track.StartOffset,
                    Size = track.Length,
                    TrackNumber = track.Number,
                    Performer = track.Performer ?? cue_sheet.Performer,
                    Title = track.Title,
                    AudioFormat = audio_format,
                    SourceFormat = cue_sheet.SourceFormat,
                    TimeStart = track.TimeStart,
                    TimeEnd = track.TimeEnd
                };

                // Sanitize filename
                entry.Name = SanitizeFileName (entry.Name);

                // Add appropriate extension based on audio format
                if (!Path.HasExtension (entry.Name))
                    entry.Name += GetOutputExtension (audio_format);

                dir.Add (entry);
            }
            return dir;
        }

        string SanitizeFileName (string name)
        {
            if (string.IsNullOrEmpty (name))
                return "Track";

            // Remove invalid characters
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new StringBuilder();
            foreach (char c in name)
            {
                if (!invalid.Contains (c))
                    sanitized.Append (c);
                else
                    sanitized.Append('_');
            }

            return sanitized.ToString().Trim();
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var carc = arc as CueAudioArchive;
            var centry = entry as CueAudioEntry;
            if (carc == null || centry == null)
                return arc.File.CreateStream (entry.Offset, entry.Size);

            return new CueTrackAsyncStream (arc.File, centry, carc.AudioFormat, carc.CueSheet.Format);
        }

        public class CueTrackPreloadStream : Stream
        {
            byte[] m_data;
            long m_position;

            public CueTrackPreloadStream (ArcView file, CueAudioEntry entry, AudioFormat format, WaveFormat waveFormat)
            {
                // Pre-decode the entire track
                using (var source_stream = file.CreateStream())
                using (var audio_input = format.TryOpen (source_stream))
                {
                    if (audio_input == null)
                        throw new InvalidOperationException ("Failed to open audio stream");

                    // Calculate positions
                    int block_align = waveFormat.BlockAlign;
                    long bytes_per_second = waveFormat.AverageBytesPerSecond;

                    long pcm_start = (long)(entry.TimeStart.TotalSeconds * bytes_per_second);
                    long pcm_end = entry.TimeEnd == TimeSpan.MaxValue ? 
                        audio_input.PcmSize : 
                        Math.Min (audio_input.PcmSize, (long)(entry.TimeEnd.TotalSeconds * bytes_per_second));

                    // Align to block boundaries
                    if (block_align > 1)
                    {
                        pcm_start = (pcm_start / block_align) * block_align;
                        pcm_end = ((pcm_end + block_align - 1) / block_align) * block_align;
                        if (pcm_end > audio_input.PcmSize)
                            pcm_end = (audio_input.PcmSize / block_align) * block_align;
                    }

                    long pcm_length = pcm_end - pcm_start;

                    // Read all PCM data at once
                    var pcm_data = new byte[pcm_length];
                    audio_input.Position = pcm_start;
                    int total_read = 0;
                    while (total_read < pcm_length)
                    {
                        int to_read = (int)Math.Min (pcm_length - total_read, 1048576);
                        int read = audio_input.Read (pcm_data, total_read, to_read);
                        if (read <= 0)
                            break;
                        total_read += read;
                    }

                    if (total_read < pcm_length)
                        Array.Resize (ref pcm_data, total_read);

                    // Create complete WAV file in memory
                    using (var wav_stream = new MemoryStream())
                    {
                        // Write WAV header
                        using (var writer = new BinaryWriter (wav_stream, Encoding.UTF8, true))
                        {
                            writer.Write (0x46464952); // "RIFF"
                            writer.Write ((uint)(pcm_data.Length + 36));
                            writer.Write (0x45564157); // "WAVE"
                            writer.Write (0x20746D66); // "fmt "
                            writer.Write (16u);
                            writer.Write (waveFormat.FormatTag);
                            writer.Write (waveFormat.Channels);
                            writer.Write (waveFormat.SamplesPerSecond);
                            writer.Write (waveFormat.AverageBytesPerSecond);
                            writer.Write (waveFormat.BlockAlign);
                            writer.Write (waveFormat.BitsPerSample);
                            writer.Write (0x61746164); // "data"
                            writer.Write ((uint)pcm_data.Length);
                        }

                        // Write PCM data
                        wav_stream.Write (pcm_data, 0, pcm_data.Length);

                        m_data = wav_stream.ToArray();
                    }
                }

                m_position = 0;
            }

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => m_data.Length;
            public override long Position
            {
                get => m_position;
                set => m_position = Math.Max (0, Math.Min (value, Length));
            }

            public override int Read (byte[] buffer, int offset, int count)
            {
                int available = (int)Math.Min (count, Length - m_position);
                if (available > 0)
                {
                    Array.Copy (m_data, (int)m_position, buffer, offset, available);
                    m_position += available;
                }
                return available;
            }

            public override long Seek (long offset, SeekOrigin origin)
            {
                switch (origin)
                {
                    case SeekOrigin.Begin:
                        Position = offset;
                        break;
                    case SeekOrigin.Current:
                        Position = m_position + offset;
                        break;
                    case SeekOrigin.End:
                        Position = Length + offset;
                        break;
                }
                return m_position;
            }

            public override void SetLength (long value) => throw new NotSupportedException();
            public override void Write (byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override void Flush() { }
        }

        public class CueTrackAsyncStream : Stream
        {
            ArcView m_file;
            CueAudioEntry m_entry;
            AudioFormat m_format;
            WaveFormat m_wave_format;
            SoundInput m_audio_input;
            MemoryStream m_wav_header_stream;
            long m_position;
            long m_pcm_start;
            long m_pcm_length;

            // Double buffering with async prefetch
            const int BufferSize = 5242880; // 5MB per buffer
            byte[] m_buffer_a;
            byte[] m_buffer_b;
            byte[] m_current_buffer;
            byte[] m_next_buffer;
            long m_current_buffer_start;
            int m_current_buffer_length;
            long m_next_buffer_start;
            int m_next_buffer_length;

            Task m_prefetch_task;
            readonly object m_swap_lock = new object();
            CancellationTokenSource m_cancel_source;
            bool m_initialized;

            public CueTrackAsyncStream(ArcView file, CueAudioEntry entry, AudioFormat format, WaveFormat waveFormat)
            {
                m_file = file;
                m_entry = entry;
                m_format = format;
                m_wave_format = waveFormat;
                m_position = 0;
                m_initialized = false;
                m_cancel_source = new CancellationTokenSource();
            }

            void Initialize()
            {
                if (m_initialized)
                    return;

                // Create WAV header
                var header = CreateWavHeader();
                m_wav_header_stream = new MemoryStream(header);

                // Open the audio input
                var stream = m_file.CreateStream();
                m_audio_input = m_format.TryOpen(stream);

                if (m_audio_input == null)
                {
                    stream.Dispose();
                    throw new InvalidOperationException("Failed to open audio stream");
                }

                // Calculate PCM positions
                int block_align = m_wave_format.BlockAlign;
                long bytes_per_second = m_wave_format.AverageBytesPerSecond;

                m_pcm_start = (long)(m_entry.TimeStart.TotalSeconds * bytes_per_second);
                long pcm_end = m_entry.TimeEnd == TimeSpan.MaxValue ? 
                    m_audio_input.PcmSize : 
                    Math.Min(m_audio_input.PcmSize, (long)(m_entry.TimeEnd.TotalSeconds * bytes_per_second));

                // Align to block boundaries
                if (block_align > 1)
                {
                    m_pcm_start = (m_pcm_start / block_align) * block_align;
                    pcm_end = ((pcm_end + block_align - 1) / block_align) * block_align;
                    if (pcm_end > m_audio_input.PcmSize)
                        pcm_end = (m_audio_input.PcmSize / block_align) * block_align;
                }

                m_pcm_length = pcm_end - m_pcm_start;

                // Allocate double buffers
                m_buffer_a = new byte[Math.Min(BufferSize, m_pcm_length)];
                m_buffer_b = new byte[Math.Min(BufferSize, m_pcm_length)];

                m_current_buffer_start = -1;
                m_current_buffer_length = 0;
                m_next_buffer_start = -1;
                m_next_buffer_length = 0;

                m_initialized = true;
            }

            byte[] CreateWavHeader()
            {
                using (var mem = new MemoryStream())
                using (var writer = new BinaryWriter(mem))
                {
                    long bytes_per_second = m_wave_format.AverageBytesPerSecond;
                    long data_size = (long)((m_entry.TimeEnd - m_entry.TimeStart).TotalSeconds * bytes_per_second);
                    int block_align = m_wave_format.BlockAlign;
                    if (block_align > 1)
                        data_size = (data_size / block_align) * block_align;

                    writer.Write(0x46464952); // "RIFF"
                    writer.Write((uint)(data_size + 36));
                    writer.Write(0x45564157); // "WAVE"
                    writer.Write(0x20746D66); // "fmt "
                    writer.Write(16u);
                    writer.Write(m_wave_format.FormatTag);
                    writer.Write(m_wave_format.Channels);
                    writer.Write(m_wave_format.SamplesPerSecond);
                    writer.Write(m_wave_format.AverageBytesPerSecond);
                    writer.Write(m_wave_format.BlockAlign);
                    writer.Write(m_wave_format.BitsPerSample);
                    writer.Write(0x61746164); // "data"
                    writer.Write((uint)data_size);

                    return mem.ToArray();
                }
            }

            void EnsureBufferAsync(long pcm_position)
            {
                lock (m_swap_lock)
                {
                    // Check if position is in current buffer
                    if (m_current_buffer_start >= 0 && 
                        pcm_position >= m_current_buffer_start && 
                        pcm_position < m_current_buffer_start + m_current_buffer_length)
                    {
                        // We're good with current buffer
                        // Check if we should start prefetching next buffer
                        long position_in_buffer = pcm_position - m_current_buffer_start;
                        long buffer_remaining = m_current_buffer_length - position_in_buffer;

                        // Start prefetch when we're 75% through current buffer
                        if (buffer_remaining < BufferSize / 4)
                        {
                            StartPrefetchIfNeeded(m_current_buffer_start + m_current_buffer_length);
                        }
                        return;
                    }

                    // Check if position is in next buffer (prefetched)
                    if (m_next_buffer_start >= 0 && 
                        pcm_position >= m_next_buffer_start && 
                        pcm_position < m_next_buffer_start + m_next_buffer_length)
                    {
                        // Wait for prefetch to complete if still running
                        if (m_prefetch_task != null && !m_prefetch_task.IsCompleted)
                        {
                            m_prefetch_task.Wait();
                        }

                        // Swap buffers
                        SwapBuffers();

                        // Start prefetching the next segment
                        StartPrefetchIfNeeded(m_current_buffer_start + m_current_buffer_length);
                        return;
                    }
                }

                // Position is not in any buffer - need immediate load
                CancelPrefetch();
                LoadBufferImmediate(pcm_position);

                // Start prefetching next buffer
                StartPrefetchIfNeeded(m_current_buffer_start + m_current_buffer_length);
            }

            void StartPrefetchIfNeeded(long start_position)
            {
                // Don't prefetch if we're already prefetching or if we're at the end
                if (m_prefetch_task != null && !m_prefetch_task.IsCompleted)
                    return;

                if (start_position >= m_pcm_length)
                    return;

                // Don't prefetch if next buffer is already loaded
                lock (m_swap_lock)
                {
                    if (m_next_buffer_start == start_position && m_next_buffer_length > 0)
                        return;
                }

                // Start async prefetch
                var token = m_cancel_source.Token;
                m_prefetch_task = Task.Run(() => PrefetchBuffer(start_position, token), token);
            }

            void PrefetchBuffer(long start_position, CancellationToken token)
            {
                try
                {
                    if (token.IsCancellationRequested)
                        return;

                    // Calculate how much to read
                    int to_read = (int)Math.Min(BufferSize, m_pcm_length - start_position);
                    int block_align = m_wave_format.BlockAlign;
                    if (block_align > 1)
                    {
                        to_read = (to_read / block_align) * block_align;
                    }

                    if (to_read <= 0)
                        return;

                    // Allocate temporary buffer for reading
                    var temp_buffer = new byte[to_read];

                    // Read from audio input
                    lock (m_audio_input)
                    {
                        if (token.IsCancellationRequested)
                            return;

                        m_audio_input.Position = m_pcm_start + start_position;

                        int total_read = 0;
                        while (total_read < to_read && !token.IsCancellationRequested)
                        {
                            int chunk = Math.Min(65536, to_read - total_read);
                            int read = m_audio_input.Read(temp_buffer, total_read, chunk);
                            if (read <= 0)
                                break;
                            total_read += read;
                        }

                        if (token.IsCancellationRequested)
                            return;

                        // Copy to next buffer under lock
                        lock (m_swap_lock)
                        {
                            var target = (m_current_buffer == m_buffer_a) ? m_buffer_b : m_buffer_a;
                            Array.Copy(temp_buffer, 0, target, 0, total_read);
                            m_next_buffer = target;
                            m_next_buffer_start = start_position;
                            m_next_buffer_length = total_read;
                        }
                    }
                }
                catch
                {
                    // Prefetch failed - will fall back to immediate load
                }
            }

            void LoadBufferImmediate(long pcm_position)
            {
                lock (m_audio_input)
                {
                    // Align position to block boundary
                    int block_align = m_wave_format.BlockAlign;
                    if (block_align > 1)
                    {
                        pcm_position = (pcm_position / block_align) * block_align;
                    }

                    // Read into current buffer
                    m_audio_input.Position = m_pcm_start + pcm_position;

                    int to_read = (int)Math.Min(BufferSize, m_pcm_length - pcm_position);
                    if (block_align > 1)
                    {
                        to_read = (to_read / block_align) * block_align;
                    }

                    var target = (m_current_buffer == m_buffer_a) ? m_buffer_b : m_buffer_a;

                    int total_read = 0;
                    while (total_read < to_read)
                    {
                        int chunk = Math.Min(65536, to_read - total_read);
                        int read = m_audio_input.Read(target, total_read, chunk);
                        if (read <= 0)
                            break;
                        total_read += read;
                    }

                    lock (m_swap_lock)
                    {
                        m_current_buffer = target;
                        m_current_buffer_start = pcm_position;
                        m_current_buffer_length = total_read;

                        // Clear next buffer info
                        m_next_buffer = null;
                        m_next_buffer_start = -1;
                        m_next_buffer_length = 0;
                    }
                }
            }

            void SwapBuffers()
            {
                m_current_buffer = m_next_buffer;
                m_current_buffer_start = m_next_buffer_start;
                m_current_buffer_length = m_next_buffer_length;

                m_next_buffer = null;
                m_next_buffer_start = -1;
                m_next_buffer_length = 0;
            }

            void CancelPrefetch()
            {
                if (m_prefetch_task != null && !m_prefetch_task.IsCompleted)
                {
                    m_cancel_source.Cancel();
                    try { m_prefetch_task.Wait(100); } catch { }
                    m_cancel_source = new CancellationTokenSource();
                }
            }

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;

            public override long Length 
            { 
                get 
                { 
                    if (!m_initialized)
                        Initialize();
                    return m_wav_header_stream.Length + m_pcm_length; 
                } 
            }

            public override long Position
            {
                get => m_position;
                set => Seek(value, SeekOrigin.Begin);
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (!m_initialized)
                    Initialize();

                int total_read = 0;

                // Read from WAV header if needed
                if (m_position < m_wav_header_stream.Length)
                {
                    m_wav_header_stream.Position = m_position;
                    int header_read = m_wav_header_stream.Read(buffer, offset, count);
                    m_position += header_read;
                    offset += header_read;
                    count -= header_read;
                    total_read += header_read;
                }

                // Read PCM data if needed
                if (count > 0 && m_position >= m_wav_header_stream.Length)
                {
                    long pcm_position = m_position - m_wav_header_stream.Length;

                    while (count > 0 && pcm_position < m_pcm_length)
                    {
                        // Ensure we have data buffered
                        EnsureBufferAsync(pcm_position);

                        lock (m_swap_lock)
                        {
                            if (m_current_buffer_start < 0 || m_current_buffer == null)
                                break;

                            // Read from current buffer
                            long buffer_offset = pcm_position - m_current_buffer_start;
                            if (buffer_offset < 0 || buffer_offset >= m_current_buffer_length)
                                break;

                            int available = (int)Math.Min(count, m_current_buffer_length - buffer_offset);
                            Array.Copy(m_current_buffer, (int)buffer_offset, buffer, offset, available);

                            offset += available;
                            count -= available;
                            total_read += available;
                            pcm_position += available;
                            m_position += available;
                        }
                    }
                }

                return total_read;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                if (!m_initialized && origin != SeekOrigin.Begin)
                    Initialize();

                long new_position = m_position;

                switch (origin)
                {
                    case SeekOrigin.Begin:
                        new_position = offset;
                        break;
                    case SeekOrigin.Current:
                        new_position = m_position + offset;
                        break;
                    case SeekOrigin.End:
                        if (!m_initialized)
                            Initialize();
                        new_position = Length + offset;
                        break;
                }

                m_position = Math.Max(0, Math.Min(new_position, Length));

                // Cancel prefetch on large seeks
                if (Math.Abs(m_position - new_position) > BufferSize)
                {
                    CancelPrefetch();
                }

                return m_position;
            }

            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override void Flush() { }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    CancelPrefetch();
                    m_cancel_source?.Dispose();
                    m_audio_input?.Dispose();
                    m_wav_header_stream?.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        void CompleteCueSheet (CueSheet cue, ArcView audio_file, SoundInput audio_input, AudioFormat audio_format)
        {
            if (audio_input != null)
            {
                cue.Format = audio_input.Format;
                cue.SourceFormat = audio_input.Format;
                cue.PcmSize = audio_input.PcmSize;
            }
            else
            {
                // Fallback to standard CD audio
                cue.Format = new WaveFormat
                {
                    FormatTag = 1,
                    Channels = 2,
                    SamplesPerSecond = 44100,
                    BitsPerSample = 16,
                    BlockAlign = 4,
                    AverageBytesPerSecond = 44100 * 4
                };
                cue.SourceFormat = cue.Format;
                cue.PcmSize = audio_file.MaxOffset;
            }

            // Calculate byte offsets and lengths
            if (cue.Tracks.Count > 0)
            {
                long bytes_per_second = cue.Format.AverageBytesPerSecond;
                long total_pcm_size = cue.PcmSize;
                int block_align = cue.Format.BlockAlign;

                for (int i = 0; i < cue.Tracks.Count; i++)
                {
                    var track = cue.Tracks[i];

                    // Always set time boundaries
                    track.TimeStart = track.Index01;
                    track.TimeEnd = (i < cue.Tracks.Count - 1) ?
                        cue.Tracks[i + 1].Index01 :
                        TimeSpan.FromSeconds ((double)total_pcm_size / bytes_per_second);

                    // Calculate byte positions in PCM data
                    track.StartOffset = (long)(track.TimeStart.TotalSeconds * bytes_per_second);

                    // Align to block boundaries
                    if (block_align > 1)
                    {
                        track.StartOffset = (track.StartOffset / block_align) * block_align;
                    }

                    if (i < cue.Tracks.Count - 1)
                    {
                        long next_offset = (long)(track.TimeEnd.TotalSeconds * bytes_per_second);
                        if (block_align > 1)
                        {
                            next_offset = (next_offset / block_align) * block_align;
                        }
                        track.Length = next_offset - track.StartOffset;
                    }
                    else
                    {
                        track.Length = total_pcm_size - track.StartOffset;
                        if (block_align > 1)
                        {
                            track.Length = (track.Length / block_align) * block_align;
                        }
                    }

                    // Ensure we don't exceed PCM bounds
                    if (track.StartOffset > total_pcm_size)
                    {
                        track.StartOffset = (total_pcm_size / block_align) * block_align;
                        track.Length = 0;
                    }
                    else if (track.StartOffset + track.Length > total_pcm_size)
                    {
                        track.Length = total_pcm_size - track.StartOffset;
                        if (block_align > 1)
                        {
                            track.Length = (track.Length / block_align) * block_align;
                        }
                    }
                }
            }

            cue.AudioFormat = audio_format;
        }

        AudioFormat FindAudioFormat (IBinaryStream file, string filename)
        {
            var formatImpls = FormatCatalog.Instance.FindFormats<AudioFormat>(filename, file.Signature);
            foreach (var impl in formatImpls)
            {
                try
                {
                    file.Position = 0;
                    using (var input = impl.TryOpen (file))
                    {
                        if (input != null)
                        {
                            if (file.CanSeek)
                                file.Position = 0;
                            return impl;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch { }
            }
            return null;
        }

        CueSheet ParseCueSheetForFile (Stream input, Encoding encoding, out string audio_file_name)
        {
            var cue = new CueSheet();
            var tracks = new List<CueTrack>();
            CueTrack current_track = null;
            string current_file = null;
            audio_file_name = null;

            using (var reader = new StreamReader (input, encoding))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (string.IsNullOrEmpty (line))
                        continue;

                    // Skip comments
                    if (line.StartsWith ("REM", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var parts = ParseCueLine (line);
                    if (parts == null || parts.Length == 0)
                        continue;

                    switch (parts[0].ToUpperInvariant())
                    {
                        case "PERFORMER":
                            if (parts.Length >= 2)
                            {
                                if (current_track != null)
                                    current_track.Performer = UnquoteString (parts[1]);
                                else
                                    cue.Performer = UnquoteString (parts[1]);
                            }
                            break;

                        case "TITLE":
                            if (parts.Length >= 2)
                            {
                                if (current_track != null)
                                    current_track.Title = UnquoteString (parts[1]);
                                else
                                    cue.Title = UnquoteString (parts[1]);
                            }
                            break;

                        case "FILE":
                            if (parts.Length >= 2)
                            {
                                current_file = UnquoteString (parts[1]);
                                if (string.IsNullOrEmpty (audio_file_name))
                                    audio_file_name = current_file;
                            }
                            break;

                        case "TRACK":
                            if (current_track != null)
                                tracks.Add (current_track);

                            if (parts.Length >= 2 && int.TryParse (parts[1], out int trackNum))
                            {
                                current_track = new CueTrack
                                {
                                    Number = trackNum,
                                    Type = parts.Length > 2 ? parts[2] : "AUDIO",
                                    FileName = current_file
                                };
                            }
                            break;

                        case "INDEX":
                            if (current_track != null && parts.Length >= 3)
                            {
                                if (int.TryParse (parts[1], out int index_num))
                                {
                                    var time = ParseTimeCode (parts[2]);

                                    if (index_num == 0)
                                        current_track.Index00 = time;
                                    else if (index_num == 1)
                                        current_track.Index01 = time;
                                }
                            }
                            break;

                        case "PREGAP":
                            if (current_track != null && parts.Length >= 2)
                                current_track.Pregap = ParseTimeCode (parts[1]);
                            break;

                        case "POSTGAP":
                            if (current_track != null && parts.Length >= 2)
                                current_track.Postgap = ParseTimeCode (parts[1]);
                            break;
                    }
                }

                if (current_track != null)
                    tracks.Add (current_track);
            }

            cue.Tracks = tracks;
            return cue;
        }

        CueSheet ParseCueSheet (Stream input, ArcView audio_file, SoundInput audio_input, AudioFormat audio_format, Encoding encoding)
        {
            string dummy;
            var cue = ParseCueSheetForFile (input, encoding, out dummy);
            CompleteCueSheet (cue, audio_file, audio_input, audio_format);
            return cue;
        }

        bool IsCompressedFormat (AudioFormat format)
        {
            if (format == null)
                return false;

            var tag = format.Tag.ToLowerInvariant();

            if (tag.Contains ("mp3") || tag.Contains ("ogg") || tag.Contains ("opus") ||
                tag.Contains ("aac") || tag.Contains ("m4a") || tag.Contains ("wma"))
                return true;

            if (tag.Contains ("flac") || tag.Contains ("ape") || tag.Contains ("tta") ||
                tag.Contains ("wv") || tag.Contains ("tak"))
                return true;

            if (tag == "wav" || tag == "raw" || tag == "pcm")
                return false;

            // treat unknown formats as compressed
            return true;
        }

        string[] ParseCueLine (string line)
        {
            var result = new List<string>();
            var regex = new Regex(@"(?:""([^""]*)""|(\S+))");
            var matches = regex.Matches (line);

            foreach (Match match in matches)
            {
                if (match.Groups[1].Success)
                    result.Add (match.Groups[1].Value);
                else if (match.Groups[2].Success)
                    result.Add (match.Groups[2].Value);
            }

            return result.ToArray();
        }

        string UnquoteString (string str)
        {
            if (string.IsNullOrEmpty (str))
                return str;
            if (str.StartsWith ("\"") && str.EndsWith ("\"") && str.Length > 1)
                return str.Substring (1, str.Length - 2);
            return str;
        }

        TimeSpan ParseTimeCode (string timecode)
        {
            var parts = timecode.Split(':');
            if (parts.Length != 3)
                return TimeSpan.Zero;

            if (!int.TryParse (parts[0], out int minutes) ||
                !int.TryParse (parts[1], out int seconds) ||
                !int.TryParse (parts[2], out int frames))
                return TimeSpan.Zero;

            // Convert CD frames (75 per second) to milliseconds
            double total_seconds = minutes * 60 + seconds + (frames / 75.0);
            return TimeSpan.FromSeconds (total_seconds);
        }

        string GetOutputExtension (AudioFormat format)
        {
            if (format == null)
                return ".wav";

            // Use the format's preferred extension
            if (format.Extensions != null && format.Extensions.Count() > 0)
                return "." + format.Extensions.First();

            return ".wav";
        }

        bool HasWavHeader (Stream stream)
        {
            if (stream.Length < 44)
                return false;

            var pos = stream.Position;
            try
            {
                stream.Position = 0;
                var header = new byte[4];
                if (stream.Read (header, 0, 4) != 4)
                    return false;
                return header[0] == 'R' && header[1] == 'I' &&
                       header[2] == 'F' && header[3] == 'F';
            }
            finally
            {
                stream.Position = pos;
            }
        }
    }

    #region Support classes

    public class CueAudioArchive : ArcFile
    {
        public CueSheet       CueSheet { get; private set; }
        public AudioFormat AudioFormat { get; private set; }
        public ArcView         CueFile { get; private set; }

        public CueAudioArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, CueSheet cue, AudioFormat format, ArcView cueFile)
            : base (arc, impl, dir)
        {
            CueSheet = cue;
            AudioFormat = format;
            CueFile = cueFile;
        }

        protected override void Dispose (bool disposing)
        {
            if (disposing && CueFile != null)
                CueFile.Dispose();
            base.Dispose (disposing);
        }
    }

    public class CueAudioEntry : Entry
    {
        public int         TrackNumber { get; set; }
        public string        Performer { get; set; }
        public string            Title { get; set; }
        public AudioFormat AudioFormat { get; set; }
        public WaveFormat SourceFormat { get; set; }
        public TimeSpan      TimeStart { get; set; }
        public TimeSpan        TimeEnd { get; set; }
    }

    public class CueSheet
    {
        public string        Performer { get; set; }
        public string            Title { get; set; }
        public List<CueTrack>   Tracks { get; set; }
        public WaveFormat       Format { get; set; }
        public WaveFormat SourceFormat { get; set; }
        public AudioFormat AudioFormat { get; set; }
        public long            PcmSize { get; set; }

        public CueSheet()
        {
            Tracks = new List<CueTrack>();
        }
    }

    public class CueTrack
    {
        public int         Number { get; set; }
        public string        Type { get; set; }
        public string       Title { get; set; }
        public string   Performer { get; set; }
        public string    FileName { get; set; }
        public TimeSpan   Index00 { get; set; }
        public TimeSpan   Index01 { get; set; }
        public TimeSpan    Pregap { get; set; }
        public TimeSpan   Postgap { get; set; }
        public long   StartOffset { get; set; }
        public long        Length { get; set; }
        public TimeSpan TimeStart { get; set; }
        public TimeSpan   TimeEnd { get; set; }
    }

    #endregion
}