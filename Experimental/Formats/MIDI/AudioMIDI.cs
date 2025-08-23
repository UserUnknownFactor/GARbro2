using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Midi;
using NAudio.MediaFoundation;

namespace GameRes.Formats.Audio
{
    [Export(typeof(AudioFormat))]
    public class MidiAudio : AudioFormat
    {
        public override string         Tag { get { return "MIDI"; } }
        public override string Description { get { return "MIDI Audio File"; } }
        public override uint     Signature { get { return  0x6468544D; } } // 'MThd'
        public override bool      CanWrite { get { return  false; } }

        private static MidiOut s_sharedMidiOut;
        public static readonly object s_midiLock = new object();
        private static int s_midiUseCount = 0;

        static MidiAudio()
        {
            try { MediaFoundationApi.Startup(); } catch { }
        }

        public MidiAudio()
        {
            Extensions = new string[] { "mid", "midi", "rmi", "kar" };
        }

        public static MidiOut GetSharedMidiOut()
        {
            lock (s_midiLock)
            {
                if (s_sharedMidiOut == null)
                    s_sharedMidiOut = new MidiOut(0);

                s_midiUseCount++;
                return s_sharedMidiOut;
            }
        }

        public static void ReleaseSharedMidiOut()
        {
            lock (s_midiLock)
            {
                s_midiUseCount--;
                if (s_midiUseCount <= 0)
                {
                    s_sharedMidiOut?.Dispose();
                    s_sharedMidiOut = null;
                    s_midiUseCount = 0;
                }
            }
        }

        public static void ResetMidiDevice()
        {
            lock (s_midiLock)
            {
                if (s_sharedMidiOut != null)
                {
                    try
                    {
                        // Reset all channels
                        for (int channel = 1; channel <= 16; channel++)
                        {
                            // All notes off
                            s_sharedMidiOut.Send(MidiMessage.ChangeControl(123, 0, channel).RawData);
                            // All sound off
                            s_sharedMidiOut.Send(MidiMessage.ChangeControl(120, 0, channel).RawData);
                            // Reset all controllers
                            s_sharedMidiOut.Send(MidiMessage.ChangeControl(121, 0, channel).RawData);
                            // Reset program
                            s_sharedMidiOut.Send(MidiMessage.ChangePatch(0, channel).RawData);
                        }

                        Thread.Sleep(50);
                    }
                    catch { }
                }
            }
        }

        public override SoundInput TryOpen(IBinaryStream file)
        {
            if (file.Signature != 0x6468544D) // 'MThd'
            {
                if (file.Signature == 0x46464952) // 'RIFF'
                {
                    file.Position = 8;
                    if (file.ReadUInt32() != 0x44494D52) // 'RMID'
                        return null;

                    file.Position = 12;
                    while (file.Position < file.Length - 8)
                    {
                        uint chunkId = file.ReadUInt32();
                        uint chunkSize = file.ReadUInt32();

                        if (chunkId == 0x61746164) // 'data'
                            break;

                        file.Position += chunkSize;
                        if ((chunkSize & 1) != 0)
                            file.Position++;
                    }
                }
                else
                    return null;
            }
            else
            {
                file.Position = 0;
            }

            long startPos = file.Position;
            var data = file.ReadBytes((int)(file.Length - startPos));

            return new MidiInput(file.AsStream, data);
        }
    }

    public class MidiInput : SoundInput
    {
        private                  Stream m_waveStream;
        private readonly         byte[] m_midiData;
        private readonly            int m_sampleRate = 44100;
        private                    Task m_renderTask;
        private CancellationTokenSource m_cancellationTokenSource;
        private                TimeSpan m_actualDuration;
        private                    long m_actualPcmSize;

        public override string SourceFormat { get { return "MIDI"; } }
        public override int   SourceBitrate { get { return  128000; } }

        public MidiInput(Stream file, byte[] midiData) : base(file)
        {
            m_midiData = midiData;

            MidiAudio.ResetMidiDevice();

            // Calculate actual duration first
            try
            {
                var midiFile = new MidiFile(new MemoryStream(m_midiData), false);
                m_actualDuration = GetMidiDuration(midiFile);
                m_actualPcmSize = (long)(m_actualDuration.TotalSeconds * m_sampleRate * 2 * 2); // stereo, 16-bit
            }
            catch
            {
                m_actualDuration = TimeSpan.FromSeconds(30); // Default fallback
                m_actualPcmSize = m_sampleRate * 2 * 2 * 30;
            }

            CreateSilentWave(m_actualDuration);

            m_cancellationTokenSource = new CancellationTokenSource();
            m_renderTask = Task.Run(() => RenderMidiAsync(m_cancellationTokenSource.Token));
        }

        private void CreateSilentWave(TimeSpan duration)
        {
            var format = new GameRes.WaveFormat
            {
                FormatTag        = 1,
                Channels         = 2,
                SamplesPerSecond = (uint)m_sampleRate,
                BitsPerSample    = 16,
                BlockAlign       = 4,
            };
            format.AverageBytesPerSecond = format.SamplesPerSecond * format.BlockAlign;
            this.Format = format;

            // Create full-size silent PCM data
            int dataSize = (int)(format.AverageBytesPerSecond * duration.TotalSeconds);
            byte[] silentPcm = new byte[dataSize];

            CreateWaveStream(silentPcm, format);
            this.PcmSize = dataSize;
        }

        private async Task RenderMidiAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (cancellationToken.IsCancellationRequested) return;

                byte[] pcmData = await RenderUsingMediaFoundation(cancellationToken);

                if ((pcmData == null || pcmData.Length == 0) && !cancellationToken.IsCancellationRequested)
                    pcmData = await RenderUsingSharedMidiOut(cancellationToken);

                if (pcmData != null && pcmData.Length > 0 && !cancellationToken.IsCancellationRequested)
                {
                    ApplyFadeInOut(pcmData, 50);

                    lock (this)
                    {
                        CreateWaveStream(pcmData, this.Format);
                    }
                }
            }
            catch
            {
                // Silent on error
            }
        }

        private async Task<byte[]> RenderUsingMediaFoundation(CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested) return null;

                    using (var midiStream = new MemoryStream(m_midiData))
                    using (var reader = new StreamMediaFoundationReader(midiStream))
                    {
                        var outFormat = new NAudio.Wave.WaveFormat(m_sampleRate, 16, 2);

                        using (var resampler = new MediaFoundationResampler(reader, outFormat))
                        {
                            resampler.ResamplerQuality = 60;

                            using (var memStream = new MemoryStream())
                            {
                                var buffer = new byte[outFormat.AverageBytesPerSecond];
                                int bytesRead;

                                while ((bytesRead = resampler.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    if (cancellationToken.IsCancellationRequested) return null;
                                    memStream.Write(buffer, 0, bytesRead);
                                }

                                return memStream.ToArray();
                            }
                        }
                    }
                }
                catch
                {
                    return null;
                }
            }, cancellationToken);
        }

        private async Task<byte[]> RenderUsingSharedMidiOut(CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                MidiOut midiOut = null;
                try
                {
                    if (cancellationToken.IsCancellationRequested) return null;

                    var midiFile = new MidiFile(new MemoryStream(m_midiData), false);
                    var duration = GetMidiDuration(midiFile);

                    midiOut = MidiAudio.GetSharedMidiOut();

                    lock (MidiAudio.s_midiLock)
                    {
                        MidiAudio.ResetMidiDevice();

                        var events = midiFile.Events
                            .SelectMany(track => track)
                            .OrderBy(e => e.AbsoluteTime)
                            .ToList();

                        int tempo = 500000;
                        var ticksPerQuarterNote = midiFile.DeltaTicksPerQuarterNote;

                        long lastTick = 0;
                        foreach (var midiEvent in events)
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                MidiAudio.ResetMidiDevice();
                                return null;
                            }

                            long deltaTicks = midiEvent.AbsoluteTime - lastTick;
                            if (deltaTicks > 0)
                            {
                                double ms = (deltaTicks * tempo) / (ticksPerQuarterNote * 1000.0);
                                if (ms > 1)
                                {
                                    // Release lock during wait
                                    Monitor.Exit(MidiAudio.s_midiLock);
                                    try
                                    {
                                        var sw = System.Diagnostics.Stopwatch.StartNew();
                                        while (sw.Elapsed.TotalMilliseconds < ms && !cancellationToken.IsCancellationRequested)
                                        {
                                            if (ms - sw.Elapsed.TotalMilliseconds > 10)
                                                Thread.Sleep(1);
                                            else
                                                Thread.Yield();
                                        }
                                    }
                                    finally
                                    {
                                        Monitor.Enter(MidiAudio.s_midiLock);
                                    }

                                    if (cancellationToken.IsCancellationRequested)
                                    {
                                        MidiAudio.ResetMidiDevice();
                                        return null;
                                    }
                                }
                            }
                            lastTick = midiEvent.AbsoluteTime;

                            ProcessMidiEventDirect(midiEvent, midiOut, ref tempo);
                        }

                        if (!cancellationToken.IsCancellationRequested)
                        {
                            Monitor.Exit(MidiAudio.s_midiLock);
                            Thread.Sleep(500);
                            Monitor.Enter(MidiAudio.s_midiLock);
                        }

                        // Reset after playing
                        MidiAudio.ResetMidiDevice();
                    }

                    return GenerateSilentPcm(duration);
                }
                catch
                {
                    return null;
                }
                finally
                {
                    if (midiOut != null)
                        MidiAudio.ReleaseSharedMidiOut();
                }
            }, cancellationToken);
        }

        private void ProcessMidiEventDirect(MidiEvent midiEvent, MidiOut midiOut, ref int tempo)
        {
            try
            {
                switch (midiEvent)
                {
                case TempoEvent tempoEvent:
                    tempo = tempoEvent.MicrosecondsPerQuarterNote;
                    break;

                case NoteOnEvent noteOn:
                    midiOut.Send(MidiMessage.StartNote(
                        noteOn.NoteNumber, noteOn.Velocity, noteOn.Channel).RawData);
                    break;

                case NoteEvent noteOff when noteOff.CommandCode == MidiCommandCode.NoteOff:
                    midiOut.Send(MidiMessage.StopNote(
                        noteOff.NoteNumber, noteOff.Velocity, noteOff.Channel).RawData);
                    break;

                case ControlChangeEvent cc:
                    midiOut.Send(MidiMessage.ChangeControl(
                        (int)cc.Controller, cc.ControllerValue, cc.Channel).RawData);
                    break;

                case PatchChangeEvent pc:
                    midiOut.Send(MidiMessage.ChangePatch(pc.Patch, pc.Channel).RawData);
                    break;

                case PitchWheelChangeEvent pw:
                    int pitchValue = pw.Pitch + 8192;
                    int status = 0xE0 | (pw.Channel - 1);
                    int data1 = pitchValue & 0x7F;
                    int data2 = (pitchValue >> 7) & 0x7F;
                    midiOut.Send(status | (data1 << 8) | (data2 << 16));
                    break;
                }
            }
            catch { }
        }

        private byte[] GenerateSilentPcm(TimeSpan duration)
        {
            int samples = (int)(duration.TotalSeconds * m_sampleRate * 2);
            return new byte[samples * 2];
        }

        private TimeSpan GetMidiDuration(MidiFile midiFile)
        {
            var ticksPerQuarterNote = midiFile.DeltaTicksPerQuarterNote;
            var tempo = 500000; // Default tempo (120 BPM)
            double totalSeconds = 0;

            // Create a combined event list with tempo changes tracked
            var allEvents = midiFile.Events
                .SelectMany(track => track)
                .OrderBy(e => e.AbsoluteTime)
                .ToList();

            if (!allEvents.Any())
                return TimeSpan.Zero;

            // Build tempo map first
            var tempoMap = new List<(long tick, int tempo)> { (0, tempo) };
            foreach (var e in allEvents.OfType<TempoEvent>())
                tempoMap.Add((e.AbsoluteTime, e.MicrosecondsPerQuarterNote));

            // Find the last actual musical event (not just meta events)
            long lastMusicalEventTime = 0;
            foreach (var e in allEvents)
            {
                // Consider note offs, note ons, and controller changes as musical events
                if (e is NoteEvent || e is ControlChangeEvent || e is PitchWheelChangeEvent)
                {
                    lastMusicalEventTime = Math.Max(lastMusicalEventTime, e.AbsoluteTime);
                }
            }

            // If no musical events, check for any events
            if (lastMusicalEventTime == 0)
                lastMusicalEventTime = allEvents.Max(e => e.AbsoluteTime);

            // Calculate duration using tempo changes
            int currentTempoIndex = 0;
            long currentTick = 0;

            while (currentTick < lastMusicalEventTime && currentTempoIndex < tempoMap.Count())
            {
                var currentTempo = tempoMap[currentTempoIndex].tempo;
                long nextTempoTick = (currentTempoIndex + 1 < tempoMap.Count()) 
                    ? tempoMap[currentTempoIndex + 1].tick 
                    : lastMusicalEventTime;

                long ticksInSegment = Math.Min(nextTempoTick, lastMusicalEventTime) - currentTick;
                totalSeconds += (ticksInSegment * currentTempo) / (ticksPerQuarterNote * 1000000.0);

                currentTick = nextTempoTick;
                currentTempoIndex++;
            }

            // Add time for note releases and reverb tail
            double releaseTime = 0.5;

            // Check if there are sustained notes that might need more time
            var noteOnEvents = allEvents.OfType<NoteOnEvent>().Where(n => n.Velocity > 0).ToList();
            var noteOffEvents = allEvents.OfType<NoteEvent>().Where(n => n.CommandCode == MidiCommandCode.NoteOff || n.Velocity == 0).ToList();

            // Find any notes that are still on at the end
            var activeNotes = new HashSet<(int channel, int note)>();
            foreach (var e in allEvents)
            {
                if (e is NoteOnEvent noteOn && noteOn.Velocity > 0)
                    activeNotes.Add((noteOn.Channel, noteOn.NoteNumber));
                else if (e is NoteEvent noteOff && (noteOff.CommandCode == MidiCommandCode.NoteOff || noteOff.Velocity == 0))
                    activeNotes.Remove((noteOff.Channel, noteOff.NoteNumber));
            }

            // If there are sustained notes or we're using sustained instruments, add more time
            bool hasSustainedInstruments = false;
            foreach (var e in allEvents.OfType<PatchChangeEvent>())
            {
                // Check for typically sustained instruments (strings, pads, organs, etc.)
                if ((e.Patch >= 40 && e.Patch <= 55) ||  // Strings
                    (e.Patch >= 88 && e.Patch <= 95) ||  // Pads
                    (e.Patch >= 16 && e.Patch <= 23))    // Organs
                {
                    hasSustainedInstruments = true;
                    break;
                }
            }

            if (activeNotes.Any() || hasSustainedInstruments)
                releaseTime = 2.0; // Longer release for sustained instruments

            var finalTempo = tempoMap.Last().tempo;
            if (finalTempo > 600000) // Slower than 100 BPM
                releaseTime *= 1.5;

            return TimeSpan.FromSeconds(totalSeconds + releaseTime);
        }

        private TimeSpan GetMidiDurationEOT(MidiFile midiFile)
        {
            var ticksPerQuarterNote = midiFile.DeltaTicksPerQuarterNote;
            var tempo = 500000;

            // Find EndOfTrack events
            long maxEndOfTrack = 0;
            foreach (var track in midiFile.Events)
            {
                var endOfTrack = track.OfType<MetaEvent>()
                    .FirstOrDefault(e => e.MetaEventType == MetaEventType.EndTrack);
                if (endOfTrack != null)
                {
                    maxEndOfTrack = Math.Max(maxEndOfTrack, endOfTrack.AbsoluteTime);
                }
            }

            if (maxEndOfTrack > 0)
            {
                // Calculate with tempo changes
                var allEvents = midiFile.Events.SelectMany(t => t).OrderBy(e => e.AbsoluteTime);
                double totalSeconds = 0;
                long lastTick = 0;

                foreach (var e in allEvents)
                {
                    if (e.AbsoluteTime > maxEndOfTrack) break;

                    if (e is TempoEvent tempoEvent)
                    {
                        var deltaTicks = e.AbsoluteTime - lastTick;
                        totalSeconds += (deltaTicks * tempo) / (ticksPerQuarterNote * 1000000.0);
                        tempo = tempoEvent.MicrosecondsPerQuarterNote;
                        lastTick = e.AbsoluteTime;
                    }
                }

                var finalDelta = maxEndOfTrack - lastTick;
                totalSeconds += (finalDelta * tempo) / (ticksPerQuarterNote * 1000000.0);

                return TimeSpan.FromSeconds(totalSeconds + 0.5); // Small buffer for release
            }

            return GetMidiDuration(midiFile);
        }

        private void ApplyFadeInOut(byte[] pcmData, int fadeMs)
        {
            int bytesPerSample = 2;
            int channels = 2;
            int samplesPerChannel = pcmData.Length / (bytesPerSample * channels);
            int fadeSamples = (m_sampleRate * fadeMs) / 1000;

            // Fade in
            for (int i = 0; i < Math.Min(fadeSamples, samplesPerChannel); i++)
            {
                float factor = (float)i / fadeSamples;
                for (int ch = 0; ch < channels; ch++)
                {
                    int idx = (i * channels + ch) * bytesPerSample;
                    if (idx + 1 < pcmData.Length)
                    {
                        short sample = (short)(pcmData[idx] | (pcmData[idx + 1] << 8));
                        sample = (short)(sample * factor);
                        pcmData[idx    ] = (byte)(sample & 0xFF);
                        pcmData[idx + 1] = (byte)(sample >> 8);
                    }
                }
            }

            // Fade out
            int fadeOutStart = Math.Max(0, samplesPerChannel - fadeSamples);
            for (int i = fadeOutStart; i < samplesPerChannel; i++)
            {
                float factor = (float)(samplesPerChannel - i) / fadeSamples;
                for (int ch = 0; ch < channels; ch++)
                {
                    int idx = (i * channels + ch) * bytesPerSample;
                    if (idx + 1 < pcmData.Length)
                    {
                        short sample = (short)(pcmData[idx] | (pcmData[idx + 1] << 8));
                        sample = (short)(sample * factor);
                        pcmData[idx    ] = (byte)(sample & 0xFF);
                        pcmData[idx + 1] = (byte)(sample >> 8);
                    }
                }
            }
        }

        private void CreateWaveStream(byte[] pcmData, GameRes.WaveFormat format)
        {
            var waveData = new byte[44 + pcmData.Length];

            using (var ms = new MemoryStream(waveData))
            using (var writer = new BinaryWriter(ms))
            {
                // RIFF header
                writer.Write(0x46464952); // "RIFF"
                writer.Write((uint)(36 + pcmData.Length));
                writer.Write(0x45564157); // "WAVE"

                // fmt chunk
                writer.Write(0x20746D66); // "fmt "
                writer.Write(16u);
                writer.Write(format.FormatTag);
                writer.Write(format.Channels);
                writer.Write(format.SamplesPerSecond);
                writer.Write(format.AverageBytesPerSecond);
                writer.Write(format.BlockAlign);
                writer.Write(format.BitsPerSample);

                // data chunk
                writer.Write(0x61746164); // "data"
                writer.Write((uint)pcmData.Length);
                writer.Write(pcmData);
            }

            var oldStream = m_waveStream;
            m_waveStream = new MemoryStream(waveData, false);
            oldStream?.Dispose();
        }

        public override long Position
        {
            get { return m_waveStream?.Position ?? 0; }
            set 
            { 
                if (m_waveStream != null) 
                    m_waveStream.Position = value; 
            }
        }

        public override bool CanSeek { get { return true; } }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (m_waveStream == null)
                return 0;

            if (m_waveStream.Position == 0)
                m_waveStream.Position = 44;

            lock (this)
            {
                return m_waveStream.Read(buffer, offset, count);
            }
        }

        public override void Reset()
        {
            if (m_waveStream != null)
                m_waveStream.Position = 44;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_cancellationTokenSource?.Cancel();

                MidiAudio.ResetMidiDevice();

                try 
                { 
                    m_renderTask?.Wait(100); 
                } 
                catch { }

                m_cancellationTokenSource?.Dispose();
                m_waveStream?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}