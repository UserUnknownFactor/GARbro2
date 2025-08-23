using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace GameRes.Formats.Audio
{
    /// <summary>
    /// Tracker music audio format handler using libopenmpt
    /// </summary>
    [Export(typeof(AudioFormat))]
    public class TrackerAudio : AudioFormat
    {
        public override string Tag { get { return "TRACKER"; } }
        public override string Description { get { return "Tracker Music Formats"; } }
        public override uint Signature { get { return 0; } }
        public override bool CanWrite { get { return false; } }

        // All supported tracker extensions
        static readonly HashSet<string> TrackerExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // ProTracker and variants
            "mod", "nst", "m15", "stk", "wow", "ult", "669", "mtm", "med", "far", "mdl",
            // ScreamTracker
            "s3m", "stm", "dsm", "amf", "ams", "umx",
            // FastTracker
            "xm", 
            // Impulse Tracker
            "it", "itp",
            // OpenMPT
            "mptm",
            // Various other formats
            "psm", "mt2", "gdm", "imf", "j2b", "plm", "pt36", "sfx", "sfx2", "mms",
            "mo3", "oxm", "dtm", "dmf", "dbm", "digi", "symmod", "ice", "okt", "ptm"
        };

        static readonly Dictionary<uint, string> KnownSignatures = new Dictionary<uint, string>
        {
            { 0x4D54484D, "MTM" },  // "MTHM"
            { 0x4D524353, "S3M" },  // "SCRM"
            { 0x6468584D, "XM" },   // "Extended Module:"
            { 0x4D504D49, "IT" },   // "IMPM"
            { 0x4D545047, "MPTM" }, // "GPTM"
            { 0x21544942, "DBM" },  // "BIT!"
            { 0x20465449, "IT" },   // "ITF "
            { 0x204D5350, "PSM" },  // "PSM "
            { 0x204D5446, "FAR" },  // "FTM "
        };

        public TrackerAudio()
        {
            Extensions = TrackerExtensions.ToArray();
            LibOpenMpt.Initialize();
        }

        public override SoundInput TryOpen(IBinaryStream file)
        {
            if (!LibOpenMpt.IsAvailable)
                return null;

            var signature = file.Signature;
            bool isProbablyTracker = KnownSignatures.ContainsKey(signature);

            if (!isProbablyTracker)
            {
                var ext = Path.GetExtension(file.Name);
                if (string.IsNullOrEmpty(ext))
                    return null;
                ext = ext.Substring(1).ToLowerInvariant();
                isProbablyTracker = TrackerExtensions.Contains(ext);
            }

            if (!isProbablyTracker)
                return null;

            file.Position = 0;
            var data = file.ReadBytes((int)Math.Min(file.Length, int.MaxValue));

            try
            {
                if (!LibOpenMpt.ProbeFileHeader(data))
                    return null;

                return new TrackerInput(file.AsStream, data);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[TrackerAudio] Failed to open file: {ex.Message}", "Audio");
                return null;
            }
        }
    }

    /// <summary>
    /// Sound input implementation for tracker music
    /// </summary>
    public class TrackerInput : SoundInput
    {
        private IntPtr m_module;
        private readonly byte[] m_data;
        private long m_position;
        private readonly int m_sampleRate = 44100;
        private readonly int m_channels = 2;

        public override string SourceFormat { get { return "Tracker"; } }
        public override int SourceBitrate 
        { 
            get { return (int)Format.AverageBytesPerSecond * 8; } 
        }

        public TrackerInput(Stream file, byte[] data) : base(file)
        {
            m_data = data;
            InitializeModule();
        }

        private void InitializeModule()
        {
            m_module = LibOpenMpt.LoadFromMemory(m_data);
            if (m_module == IntPtr.Zero)
            {
                Trace.WriteLine("[TrackerInput] Failed to load module from memory", "Audio");
                throw new InvalidOperationException("Failed to load tracker module");
            }

            var format = new GameRes.WaveFormat
            {
                FormatTag = 1, // PCM
                Channels = (ushort)m_channels,
                SamplesPerSecond = (uint)m_sampleRate,
                BitsPerSample = 16,
                BlockAlign = (ushort)(m_channels * 2),
            };
            format.AverageBytesPerSecond = format.SamplesPerSecond * format.BlockAlign;
            this.Format = format;

            double duration = LibOpenMpt.GetDuration(m_module);
            this.PcmSize = (long)(duration * format.AverageBytesPerSecond);
            m_position = 0;
        }

        public override long Position
        {
            get { return m_position; }
            set 
            { 
                m_position = value;
                double seconds = (double)value / Format.AverageBytesPerSecond;
                LibOpenMpt.SetPositionSeconds(m_module, seconds);
            }
        }

        public override bool CanSeek { get { return true; } }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (m_module == IntPtr.Zero)
                return 0;

            int bytesPerFrame = Format.BlockAlign;
            int framesToRead = count / bytesPerFrame;

            byte[] tempBuffer = new byte[framesToRead * bytesPerFrame];

            int framesRead = LibOpenMpt.ReadInterleavedStereo(
                m_module, m_sampleRate, framesToRead, tempBuffer);

            if (framesRead == 0)
                return 0;

            int bytesRead = framesRead * bytesPerFrame;
            Array.Copy(tempBuffer, 0, buffer, offset, bytesRead);
            m_position += bytesRead;

            return bytesRead;
        }

        public override void Reset()
        {
            Position = 0;
        }

        #region IDisposable Members
        private bool _disposed = false;
        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (m_module != IntPtr.Zero)
                {
                    LibOpenMpt.Unload(m_module);
                    m_module = IntPtr.Zero;
                }
                _disposed = true;
                base.Dispose(disposing);
            }
        }
        #endregion
    }

    /// <summary>
    /// P/Invoke wrapper for libopenmpt C API
    /// </summary>
    internal static class LibOpenMpt
    {
        private const string DllName32 = @"x86\libopenmpt.dll";
        private const string DllName64 = @"x64\libopenmpt.dll";

        private static readonly bool Is64Bit = IntPtr.Size == 8;
        private static string DllPath => Is64Bit ? DllName64 : DllName32;
        private static string DllDirectory => Is64Bit ? "x64" : "x86";

        private static IntPtr hModule = IntPtr.Zero;
        private static readonly object InitLock = new object();
        private static bool initialized = false;
        private static bool initializationAttempted = false;
        private static readonly List<IntPtr> dependencyHandles = new List<IntPtr>();

        public static bool IsAvailable => initialized;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr ModuleCreateFromMemory2Delegate(
            [In] byte[] filedata,
            UIntPtr filesize,
            IntPtr logfunc,
            IntPtr loguser,
            IntPtr errfunc,
            IntPtr erruser,
            IntPtr error,
            IntPtr error_message,
            IntPtr ctls);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ModuleDestroyDelegate(IntPtr module);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate UIntPtr ModuleReadInterleavedStereoDelegate(
            IntPtr module,
            Int32 samplerate,
            UIntPtr count,
            [Out] Int16[] interleaved_stereo);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate double ModuleGetDurationSecondsDelegate(IntPtr module);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate double ModuleSetPositionSecondsDelegate(IntPtr module, double seconds);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ProbeFileHeaderDelegate(
            UInt64 flags,
            [In] byte[] data,
            UIntPtr size,
            UInt64 filesize,
            IntPtr logfunc,
            IntPtr loguser,
            IntPtr errfunc,
            IntPtr erruser,
            IntPtr error,
            IntPtr error_message);

        private static ModuleCreateFromMemory2Delegate openmpt_module_create_from_memory2;
        private static ModuleDestroyDelegate openmpt_module_destroy;
        private static ModuleReadInterleavedStereoDelegate openmpt_module_read_interleaved_stereo;
        private static ModuleGetDurationSecondsDelegate openmpt_module_get_duration_seconds;
        private static ModuleSetPositionSecondsDelegate openmpt_module_set_position_seconds;
        private static ProbeFileHeaderDelegate openmpt_probe_file_header;

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int GetLastError();

        public static void Initialize()
        {
            lock (InitLock)
            {
                if (initialized || initializationAttempted)
                    return;

                initializationAttempted = true;

                try
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string dllDir = Path.Combine(baseDir, DllDirectory);

                    if (!Directory.Exists(dllDir))
                    {
                        Trace.WriteLine($"[LibOpenMpt] DLL directory not found: {dllDir}", "Audio");
                        return;
                    }

                    string oldDir = Environment.CurrentDirectory;

                    try
                    {
                        Environment.CurrentDirectory = dllDir;
                        SetDllDirectory(dllDir);
                        if (!LoadDependencies(dllDir))
                            return;

                        string libPath = Path.Combine(dllDir, "libopenmpt.dll");
                        if (!File.Exists(libPath))
                        {
                            Trace.WriteLine($"[LibOpenMpt] libopenmpt.dll not found at {libPath}", "Audio");
                            return;
                        }

                        hModule = LoadLibrary(libPath);
                        if (hModule == IntPtr.Zero)
                        {
                            int error = GetLastError();
                            Trace.WriteLine($"[LibOpenMpt] Failed to load libopenmpt.dll. Error code: {error}", "Audio");
                            return;
                        }

                        if (!LoadFunctionPointers())
                        {
                            Cleanup();
                            return;
                        }
                        initialized = true;
                    }
                    finally
                    {
                        Environment.CurrentDirectory = oldDir;
                        SetDllDirectory(null);
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[LibOpenMpt] Initialization failed: {ex.Message}", "Audio");
                    Cleanup();
                }
            }
        }

        private static bool LoadDependencies(string dllDir)
        {
            string[] dependencies = new[]
            {
                // usually already loaded, but try anyway
                "vcruntime140.dll",
                "vcruntime140_1.dll",
                "msvcp140.dll",

                // OpenMPT dependencies (leaf nodes)
                "openmpt-ogg.dll",
                "openmpt-zlib.dll",
                "openmpt-mpg123.dll",
                "openmpt-vorbis.dll"  // Depends on ogg
            };

            foreach (var dep in dependencies)
            {
                string depPath = Path.Combine(dllDir, dep);
                if (!File.Exists(depPath))
                {
                    if (dep.StartsWith("openmpt-"))
                        Trace.WriteLine($"[LibOpenMpt] Optional dependency not found: {dep}", "Audio");
                    continue;
                }

                IntPtr handle = LoadLibrary(depPath);
                if (handle == IntPtr.Zero)
                {
                    int error = GetLastError();
                    if (!dep.StartsWith("vcruntime") && !dep.StartsWith("msvcp"))
                    {
                        Trace.WriteLine($"[LibOpenMpt] Failed to load dependency {dep}. Error code: {error}", "Audio");
                        return false;
                    }
                }
                else
                {
                    dependencyHandles.Add(handle);
                }
            }
            return true;
        }

        private static bool LoadFunctionPointers()
        {
            try
            {
                openmpt_module_create_from_memory2 = GetDelegate<ModuleCreateFromMemory2Delegate>("openmpt_module_create_from_memory2");
                openmpt_module_destroy = GetDelegate<ModuleDestroyDelegate>("openmpt_module_destroy");
                openmpt_module_read_interleaved_stereo = GetDelegate<ModuleReadInterleavedStereoDelegate>("openmpt_module_read_interleaved_stereo");
                openmpt_module_get_duration_seconds = GetDelegate<ModuleGetDurationSecondsDelegate>("openmpt_module_get_duration_seconds");
                openmpt_module_set_position_seconds = GetDelegate<ModuleSetPositionSecondsDelegate>("openmpt_module_set_position_seconds");
                openmpt_probe_file_header = GetDelegate<ProbeFileHeaderDelegate>("openmpt_probe_file_header");

                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[LibOpenMpt] Failed to load function pointers: {ex.Message}", "Audio");
                return false;
            }
        }

        private static T GetDelegate<T>(string functionName) where T : class
        {
            IntPtr procAddress = GetProcAddress(hModule, functionName);
            if (procAddress == IntPtr.Zero)
            {
                int error = GetLastError();
                throw new EntryPointNotFoundException($"Function {functionName} not found in libopenmpt. Error: {error}");
            }
            return Marshal.GetDelegateForFunctionPointer(procAddress, typeof(T)) as T;
        }

        private static void Cleanup()
        {
            if (hModule != IntPtr.Zero)
            {
                FreeLibrary(hModule);
                hModule = IntPtr.Zero;
            }

            for (int i = dependencyHandles.Count - 1; i >= 0; i--)
                FreeLibrary(dependencyHandles[i]);

            dependencyHandles.Clear();

            initialized = false;
        }

        public static IntPtr LoadFromMemory(byte[] data)
        {
            if (!EnsureInitialized())
                return IntPtr.Zero;

            try
            {
                UIntPtr size = new UIntPtr((uint)data.Length);

                // Call with simplified parameters
                return openmpt_module_create_from_memory2(
                    data,              // filedata - marshalled directly
                    size,              // filesize
                    IntPtr.Zero,       // logfunc
                    IntPtr.Zero,       // loguser
                    IntPtr.Zero,       // errfunc
                    IntPtr.Zero,       // erruser
                    IntPtr.Zero,       // error
                    IntPtr.Zero,       // error_message
                    IntPtr.Zero        // ctls
                );
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[LibOpenMpt] LoadFromMemory failed: {ex.Message}", "Audio");
                return IntPtr.Zero;
            }
        }

        public static void Unload(IntPtr module)
        {
            if (module != IntPtr.Zero && openmpt_module_destroy != null && initialized)
            {
                try
                {
                    openmpt_module_destroy(module);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[LibOpenMpt] Unload failed: {ex.Message}", "Audio");
                }
            }
        }

        public static int ReadInterleavedStereo(IntPtr module, int sampleRate, int count, byte[] buffer)
        {
            if (!EnsureInitialized())
                return 0;

            try
            {
                // Convert byte buffer to int16 buffer
                Int16[] tempBuffer = new Int16[buffer.Length / 2];
                UIntPtr countPtr = new UIntPtr((uint)count);

                // Read as int16 samples
                UIntPtr framesRead = openmpt_module_read_interleaved_stereo(module, sampleRate, countPtr, tempBuffer);

                // Convert back to bytes
                int frames = (int)framesRead.ToUInt32();
                Buffer.BlockCopy(tempBuffer, 0, buffer, 0, frames * 4); // frames * 2 channels * 2 bytes

                return frames;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[LibOpenMpt] ReadInterleavedStereo failed: {ex.Message}", "Audio");
                return 0;
            }
        }

        public static double GetDuration(IntPtr module)
        {
            if (!EnsureInitialized())
                return 0.0;

            try
            {
                return openmpt_module_get_duration_seconds(module);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[LibOpenMpt] GetDuration failed: {ex.Message}", "Audio");
                return 0.0;
            }
        }

        public static double SetPositionSeconds(IntPtr module, double seconds)
        {
            if (!EnsureInitialized())
                return 0.0;

            try
            {
                return openmpt_module_set_position_seconds(module, seconds);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[LibOpenMpt] SetPositionSeconds failed: {ex.Message}", "Audio");
                return 0.0;
            }
        }

        public static bool ProbeFileHeader(byte[] data)
        {
            if (!EnsureInitialized())
                return false;

            try
            {
                const UInt64 ProbeModules = 1;
                const UInt64 ProbeContainers = 2;
                UInt64 flags = ProbeModules | ProbeContainers;

                UIntPtr size = new UIntPtr((uint)Math.Min(data.Length, 2048));

                int result = openmpt_probe_file_header(
                    flags, 
                    data,
                    size, 
                    (UInt64)data.Length,
                    IntPtr.Zero,  // logfunc
                    IntPtr.Zero,  // loguser
                    IntPtr.Zero,  // errfunc
                    IntPtr.Zero,  // erruser
                    IntPtr.Zero,  // error
                    IntPtr.Zero   // error_message
                );
                return result == 1; // OPENMPT_PROBE_FILE_HEADER_RESULT_SUCCESS
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[LibOpenMpt] ProbeFileHeader failed: {ex.Message}", "Audio");
                return false;
            }
        }

        private static bool EnsureInitialized()
        {
            if (!initialized)
                Initialize();
            return initialized;
        }
    }

}