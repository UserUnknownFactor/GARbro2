using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.Linq;
using GameRes.Collections;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using GameRes.Compression;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json.Linq;
using System.Text;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace GameRes
{
    public sealed class FormatCatalog
    {
        private static readonly FormatCatalog m_instance = new FormatCatalog();
        private const long ArchivePreferenceThreshold = 30 * 1024 * 1024; // 30 MiB

        #pragma warning disable 649
        private IEnumerable<ArchiveFormat>    m_arc_formats;
        private IEnumerable<ImageFormat>      m_image_formats;
        private IEnumerable<VideoFormat>      m_video_formats;
        private IEnumerable<AudioFormat>      m_audio_formats;
        [ImportMany(typeof(ScriptFormat))]
        private IEnumerable<ScriptFormat>     m_script_formats;
        [ImportMany(typeof(ISettingsManager))]
        private IEnumerable<ISettingsManager> m_settings_managers;
        private Dictionary<IResource, int> m_format_priorities = new Dictionary<IResource, int> ();

        private Dictionary<string, ArchiveFormat> m_arc_formats_by_tag;
        private Dictionary<string, ImageFormat>   m_image_formats_by_tag;
        private Dictionary<string, VideoFormat>   m_video_formats_by_tag;
        private Dictionary<string, AudioFormat>   m_audio_formats_by_tag;
        private Dictionary<string, ScriptFormat>  m_script_formats_by_tag;
        private Dictionary<string, IResource>     m_all_formats_by_tag;
        #pragma warning restore 649

        private MultiValueDictionary<string, IResource> m_extension_map = new MultiValueDictionary<string, IResource>();
        private MultiValueDictionary<uint, IResource>   m_signature_map = new MultiValueDictionary<uint,   IResource>();

        private Dictionary<string, string> m_game_map = new Dictionary<string, string>();
        private List<FormatLoadError> m_load_errors = new List<FormatLoadError>();

        /// <summary> The only instance of this class.</summary>
        public static FormatCatalog       Instance      { get { return m_instance;       } }

        public IEnumerable<ArchiveFormat> ArcFormats    { get { return m_arc_formats;    } }
        public IEnumerable<ImageFormat>   ImageFormats  { get { return m_image_formats;  } }
        public IEnumerable<VideoFormat>   VideoFormats  { get { return m_video_formats;  } }
        public IEnumerable<AudioFormat>   AudioFormats  { get { return m_audio_formats;  } }
        public IEnumerable<ScriptFormat>  ScriptFormats { get { return m_script_formats; } }

        public IEnumerable<IResource> Formats
        {
            get
            {
                return ((IEnumerable<IResource>)ArcFormats).Concat (ImageFormats).Concat (AudioFormats).Concat (ScriptFormats).Concat (VideoFormats);
            }
        }

        public int CurrentSchemeVersion { get; private set; }
        public string          SchemeID { get { return "GARbroDB"; } }
        public string  AssemblyLocation { get; private set; }
        public string     DataDirectory { get { return m_gamedata_dir.Value; } }

        public Exception LastError { get; set; }
        public IReadOnlyList<FormatLoadError> LoadErrors { get { return m_load_errors.AsReadOnly(); } }

        public event ParametersRequestEventHandler  ParametersRequest;

        private Lazy<string> m_gamedata_dir;

        private FormatCatalog ()
        {
            AssemblyLocation = Path.GetDirectoryName (System.Reflection.Assembly.GetExecutingAssembly().Location);
            m_gamedata_dir = new Lazy<string>(() => Path.Combine (AssemblyLocation, "GameData"));

            try
            {
                InitializeCatalog();
            }
            catch (Exception ex)
            {
                LastError = ex;
                ShowInitializationError(ex);
                InitializeEmptyCollections();
            }
        }

        private void InitializeCatalog()
        {
            //An aggregate catalog that combines multiple catalogs
            var catalog = new AggregateCatalog();
            //Adds all the parts found in the same assembly as the Program class
            catalog.Catalogs.Add (new AssemblyCatalog (typeof(FormatCatalog).Assembly));
            //Adds parts matching pattern found in the directory of the assembly
            catalog.Catalogs.Add (new DirectoryCatalog (AssemblyLocation, "Arc*.dll"));

            //Create the CompositionContainer with the parts in the catalog
            using (var container = new CompositionContainer (catalog))
            {
                m_arc_formats   = ImportWithPrioritiesSafe<ArchiveFormat> (container);
                m_image_formats = ImportWithPrioritiesSafe<ImageFormat>   (container);
                m_video_formats = ImportWithPrioritiesSafe<VideoFormat>   (container);
                m_audio_formats = ImportWithPrioritiesSafe<AudioFormat>   (container);

                //Fill the imports of this object
                try
                {
                    container.ComposeParts (this);
                }
                catch (CompositionException ex)
                {
                    RecordCompositionErrors(ex);
                }

                InitializeTagLookupDictionaries();

                AddResourceImpl (m_image_formats,  container);
                AddResourceImpl (m_video_formats,  container);
                AddResourceImpl (m_audio_formats,  container);
                AddResourceImpl (m_script_formats, container);
                AddResourceImpl (m_arc_formats,    container);

                AddAliases (container);
            }

            if (m_load_errors.Any())
                ShowLoadErrors();
        }

        private void InitializeEmptyCollections()
        {
            m_arc_formats    = Enumerable.Empty<ArchiveFormat>();
            m_image_formats  = Enumerable.Empty<ImageFormat>();
            m_video_formats  = Enumerable.Empty<VideoFormat>();
            m_audio_formats  = Enumerable.Empty<AudioFormat>();
            m_script_formats = Enumerable.Empty<ScriptFormat>();

            InitializeTagLookupDictionaries();
        }

        private void ShowInitializationError(Exception ex)
        {
            var errorDialog = new FormatLoadErrorDialog(
                "Critical Error Initializing FormatCatalog",
                "Failed to initialize the format catalog.\nThe application will not function correctly.",
                new List<FormatLoadError> 
                { 
                    new FormatLoadError 
                    { 
                        FormatType = "FormatCatalog",
                        ErrorMessage = ex.Message,
                        StackTrace = ex.ToString()
                    }
                }
            );
            errorDialog.ShowDialog();
        }

        private void ShowLoadErrors()
        {
            var errorDialog = new FormatLoadErrorDialog(
                "Format Loading Errors",
                $"{m_load_errors.Count} format(s) failed to load.\nSome file types may not be supported.",
                m_load_errors
            );
            errorDialog.ShowDialog();
        }

        private void RecordCompositionErrors(CompositionException ex)
        {
            foreach (var error in ex.Errors)
            {
                m_load_errors.Add(new FormatLoadError
                {
                    FormatType = "Composition",
                    ErrorMessage = error.Description,
                    StackTrace = error.Exception?.ToString() ?? "No stack trace available"
                });
            }
        }

        private IEnumerable<Format> ImportWithPrioritiesSafe<Format> (ExportProvider provider) where Format : IResource
        {
            try
            {
                var exports = provider.GetExports<Format, IResourceMetadata> ()
                    .OrderByDescending (f => f.Metadata.Priority)
                    .ToArray ();

                var formats = new List<Format> ();
                foreach (var export in exports)
                {
                    m_format_priorities[export.Value] = export.Metadata.Priority;
                    formats.Add (export.Value);
                }
                return formats;
            }
            catch (Exception ex)
            {
                m_load_errors.Add (new FormatLoadError {
                    FormatType = typeof (Format).Name,
                    ErrorMessage = $"Failed to import {typeof (Format).Name} format",
                    StackTrace = ex.ToString()
                });
                return Enumerable.Empty<Format>();
            }
        }

        private void InitializeTagLookupDictionaries()
        {
            var comparer = StringComparer.OrdinalIgnoreCase;

            m_arc_formats_by_tag    = new Dictionary<string, ArchiveFormat>(comparer);
            m_image_formats_by_tag  = new Dictionary<string, ImageFormat>(comparer);
            m_video_formats_by_tag  = new Dictionary<string, VideoFormat>(comparer);
            m_audio_formats_by_tag  = new Dictionary<string, AudioFormat>(comparer);
            m_script_formats_by_tag = new Dictionary<string, ScriptFormat>(comparer);
            m_all_formats_by_tag    = new Dictionary<string, IResource>(comparer);

            foreach (var format in m_arc_formats)
            {
                if (!string.IsNullOrEmpty (format.Tag))
                {
                    m_arc_formats_by_tag[format.Tag] = format;
                    m_all_formats_by_tag[format.Tag] = format;
                }
            }

            foreach (var format in m_image_formats)
            {
                if (!string.IsNullOrEmpty (format.Tag))
                {
                    m_image_formats_by_tag[format.Tag] = format;
                    m_all_formats_by_tag[format.Tag] = format;
                }
            }

            foreach (var format in m_video_formats)
            {
                if (!string.IsNullOrEmpty (format.Tag))
                {
                    m_video_formats_by_tag[format.Tag] = format;
                    m_all_formats_by_tag[format.Tag] = format;
                }
            }

            foreach (var format in m_audio_formats)
            {
                if (!string.IsNullOrEmpty (format.Tag))
                {
                    m_audio_formats_by_tag[format.Tag] = format;
                    m_all_formats_by_tag[format.Tag] = format;
                }
            }

            if (m_script_formats != null)
            {
                foreach (var format in m_script_formats)
                {
                    if (!string.IsNullOrEmpty (format.Tag))
                    {
                        m_script_formats_by_tag[format.Tag] = format;
                        m_all_formats_by_tag[format.Tag] = format;
                    }
                }
            }
        }

        private void RecordFormatError(IResource format, Exception ex)
        {
            m_load_errors.Add(new FormatLoadError
            {
                FormatType = format?.GetType().Name ?? "Unknown",
                FormatTag = format?.Tag ?? "Unknown",
                ErrorMessage = ex.Message,
                StackTrace = ex.ToString()
            });
        }

        /// <summary>
        /// Get a format by its tag. Returns null if not found.
        /// </summary>
        public IResource GetFormatByTag (string tag)
        {
            if (string.IsNullOrEmpty (tag))
                return null;

            IResource format;
            return m_all_formats_by_tag.TryGetValue (tag, out format) ? format : null;
        }

        /// <summary>
        /// Get a typed format by its tag. Returns null if not found or wrong type.
        /// </summary>
        public T GetFormatByTag<T>(string tag) where T : IResource
        {
            if (string.IsNullOrEmpty (tag))
                return null;

            var format = GetFormatByTag (tag);
            return format as T;
        }

        /// <summary>
        /// Get an archive format by its tag. Returns null if not found.
        /// </summary>
        public ArchiveFormat GetArchiveFormatByTag (string tag)
        {
            if (string.IsNullOrEmpty (tag))
                return null;

            ArchiveFormat format;
            return m_arc_formats_by_tag.TryGetValue (tag, out format) ? format : null;
        }

        /// <summary>
        /// Get an image format by its tag. Returns null if not found.
        /// </summary>
        public ImageFormat GetImageFormatByTag (string tag)
        {
            if (string.IsNullOrEmpty (tag))
                return null;

            ImageFormat format;
            return m_image_formats_by_tag.TryGetValue (tag, out format) ? format : null;
        }

        /// <summary>
        /// Get a video format by its tag. Returns null if not found.
        /// </summary>
        public VideoFormat GetVideoFormatByTag (string tag)
        {
            if (string.IsNullOrEmpty (tag))
                return null;

            VideoFormat format;
            return m_video_formats_by_tag.TryGetValue (tag, out format) ? format : null;
        }

        /// <summary>
        /// Get an audio format by its tag. Returns null if not found.
        /// </summary>
        public AudioFormat GetAudioFormatByTag (string tag)
        {
            if (string.IsNullOrEmpty (tag))
                return null;

            AudioFormat format;
            return m_audio_formats_by_tag.TryGetValue (tag, out format) ? format : null;
        }

        /// <summary>
        /// Get a script format by its tag. Returns null if not found.
        /// </summary>
        public ScriptFormat GetScriptFormatByTag (string tag)
        {
            if (string.IsNullOrEmpty (tag))
                return null;

            ScriptFormat format;
            return m_script_formats_by_tag.TryGetValue (tag, out format) ? format : null;
        }

        /// <summary>
        /// Check if a format with the given tag exists.
        /// </summary>
        public bool HasFormat (string tag)
        {
            return !string.IsNullOrEmpty (tag) && m_all_formats_by_tag.ContainsKey (tag);
        }

        private void AddResourceImpl (IEnumerable<IResource> formats, ICompositionService container)
        {
            if (formats == null)
                return;

            foreach (var impl in formats)
            {
                try
                {
                    var part = AttributedModelServices.CreatePart (impl);
                    if (part.ImportDefinitions.Any())
                        container.SatisfyImportsOnce (part);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine (ex.Message, impl.Tag);
                    RecordFormatError(impl, ex);
                }
                foreach (var ext in impl.Extensions)
                    m_extension_map.Add (ext.ToUpperInvariant(), impl);
                foreach (var signature in impl.Signatures)
                    m_signature_map.Add (signature, impl);
            }
        }

        private IEnumerable<Format> ImportWithPriorities<Format> (ExportProvider provider)
        {
            return provider.GetExports<Format, IResourceMetadata>()
                    .OrderByDescending (f => f.Metadata.Priority)
                    .Select (f => f.Value)
                    .ToArray();
        }

        private void AddAliases (ExportProvider provider)
        {
            foreach (var alias in provider.GetExports<ResourceAlias, IResourceAliasMetadata>())
            {
                var metadata = alias.Metadata;
                IEnumerable<IResource> target_list;
                if (string.IsNullOrEmpty (metadata.Type))
                    target_list = Formats;
                else if ("archive" == metadata.Type || "data" == metadata.Type)
                    target_list = ArcFormats;
                else if ("image" == metadata.Type)
                    target_list = ImageFormats;
                else if ("video" == metadata.Type)
                    target_list = VideoFormats;
                else if ("audio" == metadata.Type)
                    target_list = AudioFormats;
                else if ("script" == metadata.Type || "text" == metadata.Type || "config" == metadata.Type)
                    target_list = ScriptFormats;
                else
                {
                    System.Diagnostics.Trace.WriteLine ("Unknown resource type specified", metadata.Extension);
                    continue;
                }
                var ext    = metadata.Extension;
                var target = metadata.Target;
                if (!string.IsNullOrEmpty (ext) && !string.IsNullOrEmpty (target))
                {
                    var target_res = target_list.FirstOrDefault (f => f.Tag == target);
                    if (target_res != null)
                        m_extension_map.Add (ext.ToUpperInvariant(), target_res);
                }
            }
        }

        public void UpgradeSettings ()
        {
            if (Properties.Settings.Default.UpgradeRequired)
            {
                Properties.Settings.Default.Upgrade();
                Properties.Settings.Default.UpgradeRequired = false;
                Properties.Settings.Default.Save();
            }
            foreach (var mgr in m_settings_managers)
            {
                mgr.UpgradeSettings();
            }
        }

        public void SaveSettings ()
        {
            Properties.Settings.Default.Save();
            foreach (var mgr in m_settings_managers)
            {
                mgr.SaveSettings();
            }
        }

        /// <summary>
        /// Look up filename in format registry by filename extension and return corresponding interfaces.
        /// if no formats available, return empty range.
        /// </summary>
        public IEnumerable<IResource> LookupFileName (string filename)
        {
            string ext = VFS.GetExtension (filename);
            if (string.IsNullOrEmpty (ext))
                return Enumerable.Empty<IResource>();
            return LookupExtension (ext.TrimStart ('.'));
        }

        public IEnumerable<IResource> LookupExtension (string ext)
        {
            return m_extension_map.GetValues (ext.ToUpperInvariant(), true);
        }

        public IEnumerable<Type> LookupExtension<Type> (string ext) where Type : IResource
        {
            return LookupExtension (ext).OfType<Type>();
        }

        public IEnumerable<IResource> LookupSignature (uint signature)
        {
            return m_signature_map.GetValues (signature, true);
        }

        public IEnumerable<Type> LookupSignature<Type> (uint signature) where Type : IResource
        {
            return LookupSignature (signature).OfType<Type>();
        }

        /// <summary>
        /// Enumerate resources matching specified <paramref name="signature"/> and filename extension.
        /// Prefers archive formats over image formats when file size > treshold.
        /// </summary>
        public IEnumerable<ResourceType> FindFormats<ResourceType> (string filename, uint signature, long fileSize) where ResourceType : IResource
        {
            var ext = new Lazy<string> (() => VFS.GetExtension (filename).TrimStart ('.').ToLowerInvariant(), false);
            var tried = Enumerable.Empty<ResourceType>();
            IEnumerable<string> preferred = null;

            if (VFS.IsVirtual)
            {
                var arc_fs = VFS.Top as ArchiveFileSystem;
                if (arc_fs != null)
                    preferred = arc_fs.Source.ContainedFormats;
            }

            if (fileSize == 0)
            {
                try
                {
                    var entry = VFS.FindFile (filename);
                    if (entry != null)
                        fileSize = entry.Size;
                }
                catch { }
            }

            for (;;)
            {
                var range = LookupSignature<ResourceType> (signature);
                if (tried.Any())
                    range = range.Except (tried);

                IOrderedEnumerable<ResourceType> orderedRange = null;

                orderedRange = range.OrderByDescending (f => {
                    m_format_priorities.TryGetValue (f, out int priority);
                    return priority;
                });

                if (range.Skip (1).Any())
                {
                    orderedRange = orderedRange.ThenByDescending (f =>
                        f.Extensions?.Any (e => e == ext.Value) ?? false);
                }

                if (preferred != null && preferred.Any())
                {
                    orderedRange = orderedRange.ThenByDescending (f =>
                        preferred.Contains (f.Tag));
                }

                if (fileSize > ArchivePreferenceThreshold)
                {
                    orderedRange = orderedRange.ThenByDescending (f =>{
                        if (f is ArchiveFormat) return 4;
                        else if (f is AudioFormat) return 3;
                        else if (f is VideoFormat) return 2;
                        else if (f is ImageFormat) return 0;
                        else return 1;
                    });
                }

                //var test = orderedRange.ToList();
                foreach (var impl in orderedRange)
                    yield return impl;

                if (0 == signature)
                    break;
                signature = 0;
                tried = orderedRange;
            }
        }

        /// <summary>
        /// Enumerate resources matching specified <paramref name="signature"/> and filename extension.
        /// </summary>
        public IEnumerable<ResourceType> FindFormats<ResourceType> (string filename, uint signature) where ResourceType : IResource
        {
            return FindFormats<ResourceType> (filename, signature, 0);
        }

        /// <summary>
        /// Create GameRes.Entry corresponding to <paramref name="filename"/> extension.
        /// </summary>
        /// <exception cref="System.ArgumentException">May be thrown if filename contains invalid
        /// characters.</exception>
        public EntryType Create<EntryType> (string filename) where EntryType : Entry, new()
        {
            return new EntryType {
                Name = filename,
                Type = GetTypeFromName (filename),
            };
        }

        private static readonly Dictionary<string, string> m_nametype_map = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
        {
            // Config/Script files
            { ".ini",  "script" },
            { ".cfg",  "script" },
            { ".conf", "script" },
            { ".lua",  "script" },
            { ".rb",   "script" },
            { ".py",   "script" },
            { ".js",   "script" },
            { ".tjs",  "script" },
            { ".vbs",  "script" },

            // Text files
            { ".txt",  "script" },
            { ".html", "script" },
            { ".xml",  "script" },
            { ".json", "script" },
            { ".csv",  "script" },
            { ".log",  "script" },
            { ".md",   "script" },

            // Image files
            { ".png",  "image" },
            { ".jpg",  "image" },
            { ".jpeg", "image" },
            { ".bmp",  "image" },
            { ".tga",  "image" },
            { ".gif",  "image" },
            { ".dds",  "image" },
            { ".psd",  "image" },
            { ".tif",  "image" },
            { ".tiff", "image" },
            { ".webp", "image" },

            // Video files
            { ".wmv",  "video" },
            { ".mp4",  "video" },
            { ".avi",  "video" },
            { ".mov",  "video" },
            { ".mkv",  "video" },
            { ".webm", "video" },
            { ".flv",  "video" },

            // Audio files
            { ".mp3",  "audio" },
            { ".wav",  "audio" },
            { ".ogg",  "audio" },
            { ".flac", "audio" },
            { ".m4a",  "audio" },
            { ".wma",  "audio" },
            { ".aac",  "audio" },

            // 3D/Scene files
            { ".fbx",   "" },
            { ".obj",   "" },
            { ".dae",   "" },
            { ".3ds",   "" },
            { ".blend", "" },

            // Binary/Executable files
            { ".dll",   "archive" },
            { ".exe",   "archive" },
            { ".so",    "archive" },
            { ".dylib", "archive" }
        };

        public string GetTypeFromName (string filename, IEnumerable<string> preferred_formats = null, Dictionary<string, string> custom_map = null, long fileSize = 0)
        {
            var formats = LookupFileName (filename);
            string extension = VFS.GetExtension (filename);

            if (custom_map != null && !string.IsNullOrEmpty (extension) && custom_map.TryGetValue (extension, out string customType))
                return customType;

            if (formats.Any()) 
            {
                if (fileSize > ArchivePreferenceThreshold)
                {
                    formats = formats.OrderByDescending (f => 
                    {
                        if (f is ArchiveFormat) return 4;
                        if (f is AudioFormat)   return 3;
                        if (f is VideoFormat)   return 2;
                        if (f is ImageFormat)   return 0;
                        return 1;
                    });
                }

                if (preferred_formats != null && preferred_formats.Any())
                    formats = formats.OrderByDescending (f => preferred_formats.Contains (f.Tag));

                string type = formats.First().Type;
                if (!string.IsNullOrEmpty (type))
                    return type;
            } 

            // only fallback to builtins as a last resort
            if (!string.IsNullOrEmpty (extension) && m_nametype_map.TryGetValue (extension, out string builtInType))
                return builtInType;

            return "";
        }

        public static string GetTypeFromSignature (uint signature) {
            return DetectFileType (signature)?.Type;
        }

        public static string GetTypeFromSignature (byte[] signature)
        {
            // assume we get LittleEndian signature as always
            if (signature == null) return "";
            byte[] fourByteBuffer = null;
            if (signature.Length < 4 || signature.Length > 4)
            {
                fourByteBuffer = new byte[4];
                int bytesToCopy = Math.Min (signature.Length, 4);
                Array.Copy (signature, fourByteBuffer, bytesToCopy);
            }
            else
                fourByteBuffer = signature;
            uint signatureAsUint = (uint)(fourByteBuffer[3] << 24 | 
                                          fourByteBuffer[2] << 16 | 
                                          fourByteBuffer[1] << 8 | 
                                          fourByteBuffer[0]);
            return DetectFileType (signatureAsUint)?.Type;
        }

        public static IResource DetectFileType (uint signature)
        {
            if (0 == signature) return null;

            // resolve some special cases first
            if (0x5367674f == signature)
                return AudioFormat.FindByTag ("OGG");
            if (0x46464952 == signature)
                return AudioFormat.Wav;
            if (0x4D42 == (signature & 0xFFFF)) // 'BM'
                return ImageFormat.Bmp;
            if (ImageFormat.Png.Signature == signature)
                return ImageFormat.Png;

            if (0xE0FFD8FF == signature || 0xE1FFD8FF == signature) // JPEG
                return ImageFormat.Jpeg;
            if (0x38464947 == signature || 0x39464947 == signature) // GIF
                return ImageFormat.Gif;

            if (0x002A4949 == signature || 0x2A004D4D == signature) // TIFF
                return ImageFormat.FindByTag ("TIFF");
            if (0x20534444 == signature) // 'DDS '
                return ImageFormat.FindByTag ("DDS");

            // Audio formats
            if (0x03334449 == signature) // 'ID3\x03'
                return AudioFormat.FindByTag ("MP3");
            if ((signature & 0xFFE0) == 0xFFE0) // MP3 frame sync
                return AudioFormat.FindByTag ("MP3");
            if (0x43614C66 == signature) // 'fLaC'
                return AudioFormat.FindByTag ("FLAC");

            // Archive formats (if supported)
            if (0x04034B50 == signature || 0x06054B50 == signature) // ZIP
                return FormatCatalog.Instance.ArcFormats.FirstOrDefault (x => x.Tag == "ZIP");

            if (0x21726152 == signature) // 'Rar!'
                return FormatCatalog.Instance.ArcFormats.FirstOrDefault (x => x.Tag == "7Z/OTHERS");
            if (0xAFBC7A37 == signature) // '7z\xBC\xAF'
                return FormatCatalog.Instance.ArcFormats.FirstOrDefault (x => x.Tag == "7Z/OTHERS");
            if (0x685A42 == (signature & 0xFFFFFF)) // 'BZh' - BZip2
                return FormatCatalog.Instance.ArcFormats.FirstOrDefault (x => x.Tag == "7Z/OTHERS");
            if (0x8B1F == (signature & 0xFFFF)) // GZip - only 2 bytes
                return FormatCatalog.Instance.ArcFormats.FirstOrDefault (x => x.Tag == "7Z/OTHERS");

            // Fall back to catalog lookup
            var res = FormatCatalog.Instance.LookupSignature (signature);
            if (!res.Any())
                return null;
            if (res.Skip (1).Any()) // type is ambiguous
                return null;
            return res.First();
        }

        public void InvokeParametersRequest (object source, ParametersRequestEventArgs args)
        {
            if (null != ParametersRequest)
                ParametersRequest (source, args);
        }

        /// <summary>
        /// Read first 4 bytes from stream and return them as 32-bit signature.
        /// </summary>
        public static uint ReadSignature (Stream file)
        {
            file.Position = 0;
            uint signature = (byte)file.ReadByte();
            signature |= (uint)file.ReadByte() << 8;
            signature |= (uint)file.ReadByte() << 16;
            signature |= (uint)file.ReadByte() << 24;
            return signature;
        }

        /// <summary>
        /// Look up game title based on archive <paramref name="arc_name"/> and files matching
        /// <paramref name="pattern"/> in the same directory as archive.
        /// </summary>
        /// <returns>Game title, or null if no match was found.</returns>
        public string LookupGame (string arc_name, string pattern = "*.exe")
        {
            string title;
            if (m_game_map.TryGetValue (Path.GetFileName (arc_name), out title))
                return title;
            pattern = VFS.CombinePath (VFS.GetDirectoryName (arc_name), pattern);
            foreach (var file in VFS.GetFiles (pattern).Select (e => Path.GetFileName (e.Name)))
            {
                if (m_game_map.TryGetValue (file, out title))
                    return title;
            }
            return null;
        }

        public void DeserializeScheme (Stream input)
        {
            int version = GetSerializedSchemeVersion (input);
            if (version <= CurrentSchemeVersion)
                return;
            using (var zs = new ZLibStream (input, CompressionMode.Decompress, true))
            {
                var bin = new BinaryFormatter();
                var db = (SchemeDataBase)bin.Deserialize (zs);

                foreach (var format in Formats)
                {
                    ResourceScheme scheme;
                    if (db.SchemeMap.TryGetValue (format.Tag, out scheme))
                        format.Scheme = scheme;
                }
                CurrentSchemeVersion = db.Version;
                if (db.GameMap != null)
                    m_game_map = db.GameMap;
            }
        }

        public void SerializeScheme (Stream output)
        {
            var db = new SchemeDataBase {
                Version = CurrentSchemeVersion,
                SchemeMap = new Dictionary<string, ResourceScheme>(),
                GameMap = m_game_map,
            };
            foreach (var format in Formats)
            {
                var scheme = format.Scheme;
                if (null != scheme)
                    db.SchemeMap[format.Tag] = scheme;
            }
            SerializeScheme (output, db);
        }

        public void SerializeScheme (Stream output, SchemeDataBase db)
        {
            using (var writer = new BinaryWriter (output, System.Text.Encoding.UTF8, true))
            {
                writer.Write (SchemeID.ToCharArray());
                writer.Write (db.Version);
            }
            var bin = new BinaryFormatter();
            using (var zs = new ZLibStream (output, CompressionMode.Compress, true))
                bin.Serialize (zs, db);
        }

        /// <summary>
        /// Serialize scheme database to JSON format.
        /// </summary>
        public void SerializeSchemeJson (Stream output, SchemeDataBase db)
        {
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                ContractResolver = new IncludeFieldsContractResolver(),
                Converters = { new ByteArrayToHexStringConverter() },
                Formatting = Formatting.Indented,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            };

            using (var sw = new StreamWriter (output, Encoding.UTF8, 1024, true))
            {
                var json = JsonConvert.SerializeObject (db, settings);
                sw.Write (json);
                sw.Flush();
            }
        }

        /// <summary>
        /// Deserialize scheme database from JSON format.
        /// </summary>
        public void DeserializeSchemeJson (Stream input)
        {
            using (var reader = new StreamReader (input, Encoding.UTF8, true, 1024, true))
            {
                var json = reader.ReadToEnd();

                var settings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    ContractResolver = new IncludeFieldsContractResolver(),
                    Converters = { new ByteArrayToHexStringConverter() },
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                };

                var db = JsonConvert.DeserializeObject<SchemeDataBase>(json, settings);

                if (db.Version <= CurrentSchemeVersion)
                    return;

                foreach (var format in Formats)
                {
                    ResourceScheme scheme;
                    if (db.SchemeMap.TryGetValue (format.Tag, out scheme))
                        format.Scheme = scheme;
                }
                CurrentSchemeVersion = db.Version;
                if (db.GameMap != null)
                    m_game_map = db.GameMap;
            }
        }

        /// <summary>
        /// Helper method to convert ResourceScheme to JSON-serializable format.
        /// </summary>
        private JsonResourceScheme ConvertToJsonScheme (ResourceScheme scheme)
        {
            // Serialize the scheme object to binary, then convert to base64
            using (var ms = new MemoryStream())
            {
                var formatter = new BinaryFormatter();
                formatter.Serialize (ms, scheme);
                return new JsonResourceScheme
                {
                    TypeName = scheme.GetType().AssemblyQualifiedName,
                    Data = Convert.ToBase64String (ms.ToArray())
                };
            }
        }

        /// <summary>
        /// Helper method to convert JSON-serializable format back to ResourceScheme.
        /// </summary>
        private ResourceScheme ConvertFromJsonScheme (JsonResourceScheme jsonScheme)
        {
            var bytes = Convert.FromBase64String (jsonScheme.Data);
            using (var ms = new MemoryStream (bytes))
            {
                var formatter = new BinaryFormatter();
                return (ResourceScheme)formatter.Deserialize (ms);
            }
        }

        public int GetSerializedSchemeVersion (Stream input)
        {
            using (var reader = new BinaryReader (input, System.Text.Encoding.UTF8, true))
            {
                var header = reader.ReadChars (SchemeID.Length);
                if (!header.SequenceEqual (SchemeID))
                    throw new FormatException ("Invalid serialization file");
                return reader.ReadInt32();
            }
        }

        /// <summary>
        /// Read text file <paramref name="filename"/> from data directory, performing <paramref name="process_line"/> action on each non-empty line.
        /// </summary>
        public void ReadFileList (string filename, Action<string> process_line)
        {
            var lst_file = Path.Combine (DataDirectory, filename);
            if (!File.Exists (lst_file))
                return;
            using (var input = new StreamReader (lst_file))
            {
                string line;
                while ((line = input.ReadLine()) != null)
                {
                    if (line.Length > 0)
                    {
                        process_line (line);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Lazily initialized wrapper for resource instances.
    /// </summary>
    public class ResourceInstance<T> where T : IResource
    {
        T           m_format;
        Func<T>     m_resolver;

        public ResourceInstance (string tag)
        {
            var t = typeof(T);
            if (typeof(ImageFormat) == t || t.IsSubclassOf (typeof(ImageFormat)))
                m_resolver = () => ImageFormat.FindByTag (tag) as T;
            else if (typeof(ArchiveFormat) == t || t.IsSubclassOf (typeof(ArchiveFormat)))
                m_resolver = () => FormatCatalog.Instance.ArcFormats.FirstOrDefault (f => f.Tag == tag) as T;
            else if (typeof(AudioFormat) == t || t.IsSubclassOf (typeof(AudioFormat)))
                m_resolver = () => FormatCatalog.Instance.AudioFormats.FirstOrDefault (f => f.Tag == tag) as T;
            else if (typeof(VideoFormat) == t || t.IsSubclassOf (typeof(VideoFormat)))
                m_resolver = () => FormatCatalog.Instance.VideoFormats.FirstOrDefault (f => f.Tag == tag) as T;
            else if (typeof(ScriptFormat) == t || t. IsSubclassOf (typeof(ScriptFormat)))
                m_resolver = () => FormatCatalog.Instance.ScriptFormats.FirstOrDefault (f => f.Tag == tag) as T;
            else
                throw new ApplicationException ("Invalid resource type specified for ResourceInstance<T>");
        }

        public T Value { get { return LazyInitializer.EnsureInitialized (ref m_format, m_resolver); } }
    }

    [Serializable]
    public class SchemeDataBase
    {
        public int Version;

        public Dictionary<string, ResourceScheme>   SchemeMap;
        public Dictionary<string, string>           GameMap;
    }

    /// <summary>
    /// JSON-serializable version of SchemeDataBase
    /// </summary>
    public class JsonSchemeDataBase
    {
        public int Version { get; set; }
        public Dictionary<string, JsonResourceScheme> SchemeMap { get; set; }
        public Dictionary<string, string> GameMap { get; set; }
    }

    /// <summary>
    /// JSON-serializable wrapper for ResourceScheme objects
    /// </summary>
    public class JsonResourceScheme
    {
        public string TypeName { get; set; }
        public string Data { get; set; }  // Base64-encoded binary data
    }
}

/// <summary>
/// Custom JSON converter for ResourceScheme objects that intelligently handles serialization
/// </summary>
public class ByteArrayToHexStringConverter : JsonConverter<byte[]>
{
    public override void WriteJson (JsonWriter writer, byte[] value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }
        writer.WriteValue (ByteArrayToString (value));
    }

    public override byte[] ReadJson (JsonReader reader, Type objectType, byte[] existingValue, 
        bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;

        if (reader.TokenType == JsonToken.String)
        {
            string hex = (string)reader.Value;
            return StringToByteArray (hex);
        }

        throw new JsonSerializationException ($"Unexpected token type: {reader.TokenType}");
    }

    private static string ByteArrayToString (byte[] input)
    {
        var sb = new StringBuilder (input.Length * 2);
        foreach (var b in input)
            sb.AppendFormat ("{0:X2}", b);
        return sb.ToString();
    }

    private static byte[] StringToByteArray (string hex)
    {
        if (string.IsNullOrEmpty (hex))
            return new byte[0];

        int length = hex.Length / 2;
        byte[] bytes = new byte[length];
        for (int i = 0; i < length; i++)
        {
            bytes[i] = Convert.ToByte (hex.Substring (i * 2, 2), 16);
        }
        return bytes;
    }
}

public class IncludeFieldsContractResolver : DefaultContractResolver
{
    protected override IList<JsonProperty> CreateProperties (Type type, MemberSerialization memberSerialization)
    {
        var properties = base.CreateProperties (type, memberSerialization);

        // Create a HashSet of existing property names to avoid duplicates
        var existingNames = new HashSet<string>(properties.Select (p => p.PropertyName));

        var fields = type.GetFields (BindingFlags.Public | BindingFlags.Instance)
            .Where (f => !f.IsInitOnly && !f.IsLiteral);

        foreach (var field in fields)
        {
            // Skip compiler-generated backing fields
            if (field.Name.Contains ("<") || field.Name.Contains (">") || 
                field.Name.Contains ("k__BackingField"))
                continue;

            // Skip if a property with the same name already exists
            if (existingNames.Contains (field.Name))
                continue;

            var jsonProperty = CreateProperty (field, memberSerialization);
            jsonProperty.Writable = true;
            jsonProperty.Readable = true;
            properties.Add (jsonProperty);
            existingNames.Add (field.Name);
        }

        return properties;
    }
}

public class FormatLoadError
{
    public string FormatType { get; set; }
    public string FormatTag { get; set; }
    public string ErrorMessage { get; set; }
    public string StackTrace { get; set; }
}

public class FormatLoadErrorDialog : Window
{
    public FormatLoadErrorDialog (string title, string message, List<FormatLoadError> errors)
    {
        Title = title;
        Width = 800;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var grid = new Grid ();
        grid.RowDefinitions.Add (new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add (new RowDefinition { Height = new GridLength (1, GridUnitType.Star) });
        grid.RowDefinitions.Add (new RowDefinition { Height = GridLength.Auto });

        // Message label
        var messageLabel = new TextBlock
        {
            Text = message,
            Margin = new Thickness (10),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow (messageLabel, 0);
        grid.Children.Add (messageLabel);

        // Error text box
        var errorTextBox = new TextBox
        {
            IsReadOnly = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily ("Consolas"),
            Margin = new Thickness (10, 0, 10, 10)
        };

        var errorText = new StringBuilder ();
        errorText.AppendLine (
            "=============== Format Loading Errors ({DateTime.Now}) ==============="
        );
        errorText.AppendLine ();

        foreach (var error in errors)
        {
            errorText.AppendLine (string.Format("Format Type: {0}", error.FormatType));
            if (!string.IsNullOrEmpty (error.FormatTag))
                errorText.AppendLine (string.Format("Format Tag: {0}", error.FormatTag));
            errorText.AppendLine (string.Format("Error: {0}", error.ErrorMessage));
            errorText.AppendLine ("Stack Trace:");
            errorText.AppendLine (error.StackTrace);
            errorText.AppendLine (new string ('-', 80));
            errorText.AppendLine ();
        }

        errorTextBox.Text = errorText.ToString ();
        Grid.SetRow (errorTextBox, 1);
        grid.Children.Add (errorTextBox);

        // Button panel
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness (10)
        };

        var copyButton = new Button
        {
            Content = "Copy to Clipboard",
            Width = 120,
            Height = 30,
            Margin = new Thickness (0, 0, 10, 0)
        };
        copyButton.Click += (s, e) =>
        {
            try
            {
                Clipboard.SetText (errorTextBox.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show (
                    string.Format("Failed to copy to clipboard:\n{0}", ex.Message),
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error
                );
            }
        };

        var closeButton = new Button
        {
            Content = "Close",
            Width = 100,
            Height = 30,
            IsDefault = true
        };
        closeButton.Click += (s, e) => this.Close ();

        buttonPanel.Children.Add (copyButton);
        buttonPanel.Children.Add (closeButton);
        Grid.SetRow (buttonPanel, 2);
        grid.Children.Add (buttonPanel);

        Content = grid;
    }
}

/*
using (var fileStream = File.Create ("scheme.json"))
{
    var db = new SchemeDataBase 
    {
        Version = CurrentSchemeVersion,
        SchemeMap = schemeMap,
        GameMap = gameMap
    };
    catalog.SerializeSchemeJson (fileStream, db);
}
using (var fileStream = File.OpenRead ("scheme.json"))
{
    catalog.DeserializeSchemeJson (fileStream);
}
*/