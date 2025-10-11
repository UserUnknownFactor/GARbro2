using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Diagnostics;
using GameRes.Utility;

namespace GameRes.Formats.MSFormats
{
    internal class MsuEntry : Entry
    {
        // Reference to source data
        public enum SourceType { Direct, Cab, Delta, Virtual }
        public SourceType DataSource { get; set; }

        // For CAB-contained files
        public string CabPath { get; set; }
        public CabEntry CabEntryRef { get; set; }

        // For delta patches
        public string DeltaPath { get; set; }
        public string BaseFileName { get; set; }
        public string ExpectedHash { get; set; }

        // For virtual (manifest-defined) files
        public string SourcePatch { get; set; }  // Delta patch file name
        public string BasisFile { get; set; }    // Base file for delta

        // Cached data (only for small metadata files)
        public byte[] CachedData { get; set; }
        public bool ShouldCache { get; set; }

        // Metadata
        public string Architecture { get; set; }
        public bool IsMetadata { get; set; }
        public bool IsDelta { get; set; }

        // Delta information
        public DeltaType DeltaFileType { get; set; }  // PA30, PA19, PA31 - from file signature
        public DeltaFolderType DeltaFolderType { get; set; }  // f/, r/, n/ - from folder path
    }

    internal enum DeltaFolderType
    {
        None,
        Forward,    // f folder - patches to apply forward
        Reverse,    // r folder - patches to apply in reverse  
        Null        // n folder - null differential patches
    }

    internal class MsuArchive : ArcFile
    {
        public Dictionary<string, CabArcFile> CabArchives { get; } // References to nested CAB archives
        public Dictionary<string, byte[]> MetadataCache { get; } // Cached metadata only
        public Dictionary<string, MsuEntry> VirtualFiles { get; } // Mapping for virtual entries
        public CabOpener CabHandler { get; }
        public DeltaOpener DeltaHandler { get; }

        public MsuArchive(ArcView arc, ArchiveFormat impl, ICollection<Entry> dir,
            Dictionary<string, CabArcFile> cabArchives,
            Dictionary<string, byte[]> metadataCache,
            Dictionary<string, MsuEntry> virtualFiles)
            : base(arc, impl, dir)
        {
            CabArchives = cabArchives;
            MetadataCache = metadataCache;
            VirtualFiles = virtualFiles;
            CabHandler = new CabOpener();
            DeltaHandler = new DeltaOpener();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var cab in CabArchives.Values)
                {
                    cab?.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }

    [Export(typeof(ArchiveFormat))]
    [ExportMetadata("Priority", 2)]
    public class MsuOpener : ArchiveFormat
    {
        public override string Tag { get { return "MSU"; } }
        public override string Description { get { return "Microsoft update standalone package"; } }
        public override uint Signature { get { return 0; } }
        public override bool IsHierarchic { get { return true; } }
        public override bool CanWrite { get { return false; } }

        private readonly CabOpener cabHandler = new CabOpener();
        private readonly DeltaOpener deltaHandler = new DeltaOpener();

        // Metadata file patterns
        private static readonly string[] MetadataPatterns = new[]
        {
            "*.mum", "*.manifest", "*.cat", "*.xml", "*.txt", "*.ini", "*.psf"
        };

        public MsuOpener()
        {
            Extensions = new[] { "msu", "cab" };
            Signatures = new uint[] { 0x4643534D }; // MSCF
        }

        public override ArcFile TryOpen(ArcView file)
        {
            if (file.Name.EndsWith(".cab", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsMsuStyleCab(file))
                    return null;
            }
            else if (!file.Name.EndsWith(".msu", StringComparison.OrdinalIgnoreCase))
                return null;

            if (file.View.AsciiEqual(0, "MSWIM"))
                throw new NotSupportedException("WIM-based MSU files are not yet supported");

            if (!file.View.AsciiEqual(0, "MSCF"))
                return null;

            return OpenMsuCab(file);
        }

        private bool IsMsuStyleCab(ArcView file)
        {
            try
            {
                var cabHeader = new CFHeader();
                using (var stream = file.CreateStream())
                {
                    if (cabHeader.FromStream(stream) != 0)
                        return false;

                    stream.Position = cabHeader.uiFilesOffset;
                    for (uint i = 0; i < cabHeader.usNumFiles; i++)
                    {
                        var cff = new CFFile(stream);

                        if (cff.FileName.EndsWith("_manifest_.cix.xml", StringComparison.OrdinalIgnoreCase) ||
                            cff.FileName.EndsWith(".mum", StringComparison.OrdinalIgnoreCase) ||
                            cff.FileName.EndsWith("update.mum", StringComparison.OrdinalIgnoreCase) ||
                            (cff.FileName.StartsWith("Package", StringComparison.OrdinalIgnoreCase) &&
                             cff.FileName.Contains("_for_KB")) ||
                            (cff.FileName.Contains("microsoft-windows-") &&
                             (cff.FileName.Contains("\\r\\") || cff.FileName.Contains("\\f\\") || cff.FileName.Contains("\\n\\"))))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private ArcFile OpenMsuCab(ArcView file)
        {
            try
            {
                var entries = new List<Entry>();
                var cabArchives = new Dictionary<string, CabArcFile>();
                var metadataCache = new Dictionary<string, byte[]>();
                var virtualFiles = new Dictionary<string, MsuEntry>();
                var processedCabs = new HashSet<string>();

                var deltasByComponent = new Dictionary<string, Dictionary<string, MsuEntry>>(StringComparer.OrdinalIgnoreCase);
                var deltasByFilename = new Dictionary<string, MsuEntry>(StringComparer.OrdinalIgnoreCase);
                var allManifests = new List<(byte[] data, string type, string component)>();

                var mainCab = cabHandler.TryOpen(file) as CabArcFile;
                if (mainCab == null)
                    return null;

                cabArchives[""] = mainCab;

                ProcessCabContents(mainCab, "", entries, cabArchives, metadataCache,
                                  virtualFiles, processedCabs, deltasByComponent, deltasByFilename, allManifests);

                var createdVirtualPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (manifestData, type, component) in allManifests)
                {
                    ProcessManifest(manifestData, type, component, deltasByFilename, deltasByComponent,
                                   entries, virtualFiles, createdVirtualPaths);
                }

                // Update sizes from delta headers
                UpdateVirtualEntrySizesFromDeltas(entries, virtualFiles, cabArchives);

                if (!entries.Any())
                {
                    mainCab.Dispose();
                    return null;
                }

                return new MsuArchive(file, this, entries, cabArchives, metadataCache, virtualFiles);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"OpenMsuCab error: {ex.Message}");
                return null;
            }
        }

        private void ProcessCabContents(CabArcFile cab, string cabPath,
            List<Entry> entries, Dictionary<string, CabArcFile> cabArchives,
            Dictionary<string, byte[]> metadataCache, Dictionary<string, MsuEntry> virtualFiles,
            HashSet<string> processedCabs,
            Dictionary<string, Dictionary<string, MsuEntry>> deltasByComponent,
            Dictionary<string, MsuEntry> deltasByFilename,
            List<(byte[] data, string type, string component)> allManifests)
        {
            var cabKey = string.IsNullOrEmpty(cabPath) ? "main" : cabPath;
            if (processedCabs.Contains(cabKey))
                return;

            processedCabs.Add(cabKey);

            foreach (var entry in cab.Dir)
            {
                var cabEntry = entry as CabEntry;
                if (cabEntry == null)
                    continue;

                var fullPath = entry.Name;
                var fileName = Path.GetFileName(entry.Name);

                if (entry.Name.EndsWith("_manifest_.cix.xml", StringComparison.OrdinalIgnoreCase))
                {
                    // Load CIX manifest and store for later processing
                    byte[] manifestData;
                    using (var stream = cabHandler.OpenEntry(cab, entry))
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        manifestData = ms.ToArray();
                    }

                    allManifests.Add((manifestData, "cix", ""));

                    var mEntry = new MsuEntry
                    {
                        Name = "METADATA" + VFS.DIR_DELIMITER + fileName,
                        Type = "script",
                        Offset = 0,
                        Size = entry.Size,
                        DataSource = MsuEntry.SourceType.Cab,
                        CabPath = cabPath,
                        CabEntryRef = cabEntry,
                        CachedData = manifestData,
                        IsMetadata = true
                    };
                    entries.Add(mEntry);
                    metadataCache[fileName] = manifestData;
                }
                else if (entry.Name.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                {
                    // Load component manifest and store for later processing
                    byte[] manifestData;
                    using (var stream = cabHandler.OpenEntry(cab, entry))
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        manifestData = ms.ToArray();
                    }

                    var component = Path.GetFileNameWithoutExtension(entry.Name);
                    allManifests.Add((manifestData, "component", component));

                    // Add manifest entry itself
                    var mEntry = new MsuEntry
                    {
                        Name = "METADATA" + VFS.DIR_DELIMITER + fileName,
                        Type = "script",
                        Offset = 0,
                        Size = entry.Size,
                        DataSource = MsuEntry.SourceType.Cab,
                        CabPath = cabPath,
                        CabEntryRef = cabEntry,
                        CachedData = manifestData,
                        IsMetadata = true
                    };
                    entries.Add(mEntry);
                    metadataCache[fileName] = manifestData;
                }
                else if (entry.Name.EndsWith(".mum", StringComparison.OrdinalIgnoreCase))
                {
                    // Handle .mum files
                    byte[] mumData;
                    using (var stream = cabHandler.OpenEntry(cab, entry))
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        mumData = ms.ToArray();
                    }

                    var mEntry = new MsuEntry
                    {
                        Name = "METADATA" + VFS.DIR_DELIMITER + fileName,
                        Type = "script",
                        Offset = 0,
                        Size = entry.Size,
                        DataSource = MsuEntry.SourceType.Cab,
                        CabPath = cabPath,
                        CabEntryRef = cabEntry,
                        CachedData = mumData,
                        IsMetadata = true
                    };
                    entries.Add(mEntry);
                    metadataCache[fileName] = mumData;
                }
                else if (entry.Name.EndsWith(".cab", StringComparison.OrdinalIgnoreCase))
                {
                    // Handle nested CABs
                    var cabName = Path.GetFileName(entry.Name);
                    if (cabName.Equals("WSUSSCAN.cab", StringComparison.OrdinalIgnoreCase) ||
                        cabName.StartsWith("SSU-", StringComparison.OrdinalIgnoreCase))
                        continue;

                    byte[] cabData;
                    using (var stream = cabHandler.OpenEntry(cab, entry))
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        cabData = ms.ToArray();
                    }

                    ProcessNestedCab(cabData, fullPath, entries, cabArchives, metadataCache,
                                    virtualFiles, processedCabs, deltasByComponent, deltasByFilename, allManifests);
                }
                else
                {
                    // Process all other files including deltas
                    var deltaFolderType = DetermineDeltaFolderType(fullPath);
                    bool isMetadata = IsMetadataFile(entry.Name);
                    bool isDelta = deltaFolderType != DeltaFolderType.None || entry.Name.EndsWith(".p_", StringComparison.OrdinalIgnoreCase);

                    string entryName;
                    MsuEntry.SourceType sourceType;

                    if (isDelta)
                    {
                        entryName = "DELTA" + VFS.DIR_DELIMITER + fullPath.Replace("\\", VFS.DIR_DELIMITER);
                        sourceType = MsuEntry.SourceType.Delta;
                    }
                    else if (isMetadata)
                    {
                        entryName = "METADATA" + VFS.DIR_DELIMITER + fileName;
                        sourceType = MsuEntry.SourceType.Cab;
                    }
                    else
                    {
                        entryName = fullPath.Replace("\\", VFS.DIR_DELIMITER);
                        sourceType = MsuEntry.SourceType.Cab;
                    }

                    var msuEntry = new MsuEntry
                    {
                        Name = entryName,
                        Type = FormatCatalog.Instance.GetTypeFromName(entry.Name),
                        Offset = 0,
                        Size = entry.Size,
                        DataSource = sourceType,
                        CabPath = cabPath,
                        CabEntryRef = cabEntry,
                        IsMetadata = isMetadata,
                        IsDelta = isDelta,
                        DeltaFolderType = deltaFolderType,
                        Architecture = DetectArchitecture(fullPath),
                        BaseFileName = fileName
                    };

                    // Cache small metadata files
                    if (isMetadata && entry.Size < 64 * 1024)
                    {
                        using (var stream = cabHandler.OpenEntry(cab, entry))
                        using (var ms = new MemoryStream())
                        {
                            stream.CopyTo(ms);
                            msuEntry.CachedData = ms.ToArray();
                            metadataCache[fileName] = msuEntry.CachedData;
                        }
                    }

                    entries.Add(msuEntry);

                    // Store delta entries for lookup
                    if (isDelta)
                    {
                        var pathParts = fullPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                        var deltaComponent = pathParts.Length > 2 ? pathParts[0] : "";

                        // Store BOTH Forward and Null deltas for component lookup
                        if (deltaFolderType == DeltaFolderType.Forward || deltaFolderType == DeltaFolderType.Null)
                        {
                            if (!deltasByComponent.ContainsKey(deltaComponent))
                                deltasByComponent[deltaComponent] = new Dictionary<string, MsuEntry>(StringComparer.OrdinalIgnoreCase);

                            deltasByComponent[deltaComponent][fileName] = msuEntry;
                            Debug.WriteLine($"Stored {deltaFolderType} delta: {fileName} in component: {deltaComponent}");
                        }

                        deltasByFilename[fileName] = msuEntry;
                        virtualFiles[msuEntry.Name] = msuEntry;
                    }
                }
            }
        }

        private void ProcessManifest(byte[] manifestData, string manifestType, string componentFolder,
    Dictionary<string, MsuEntry> deltasByFilename,
    Dictionary<string, Dictionary<string, MsuEntry>> deltasByComponent,
    List<Entry> entries, Dictionary<string, MsuEntry> virtualFiles,
    HashSet<string> createdVirtualPaths)
        {
            try
            {
                var xml = new XmlDocument();
                xml.LoadXml(Encoding.UTF8.GetString(manifestData));

                if (manifestType == "cix")
                {
                    // CIX manifest processing
                    var nsmgr = new XmlNamespaceManager(xml.NameTable);
                    nsmgr.AddNamespace("ci", "urn:ContainerIndex");

                    var fileIdMap = new Dictionary<string, string>();
                    var files = xml.SelectNodes("//ci:Container/ci:Files/ci:File", nsmgr);

                    foreach (XmlNode fileNode in files)
                    {
                        var id = fileNode.Attributes["id"]?.Value;
                        var name = fileNode.Attributes["name"]?.Value;
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                            fileIdMap[id] = name;
                    }

                    foreach (XmlNode fileNode in files)
                    {
                        var fileName = fileNode.Attributes["name"]?.Value;
                        if (string.IsNullOrEmpty(fileName) || IsMetadataFile(fileName))
                            continue;

                        var deltaNode = fileNode.SelectSingleNode("ci:Delta/ci:Source", nsmgr);
                        if (deltaNode == null)
                            continue;

                        var deltaType = deltaNode.Attributes["type"]?.Value;
                        var sourceName = deltaNode.Attributes["name"]?.Value;

                        if ((deltaType == "PA30" || deltaType == "PA19" || deltaType == "PA31") &&
                            !string.IsNullOrEmpty(sourceName))
                        {
                            var virtualPath = "PATCHED" + VFS.DIR_DELIMITER + fileName.Replace("\\", VFS.DIR_DELIMITER);

                            if (!createdVirtualPaths.Add(virtualPath))
                                continue;

                            if (!deltasByFilename.TryGetValue(sourceName, out var deltaEntry))
                            {
                                Debug.WriteLine($"Delta not found for CIX entry: {sourceName}");
                                continue;
                            }

                            var length = fileNode.Attributes["length"]?.Value;
                            var hash = fileNode.SelectSingleNode("ci:Hash", nsmgr)?.Attributes["value"]?.Value;

                            string basisFile = null;
                            var basisNode = fileNode.SelectSingleNode("ci:Delta/ci:Basis", nsmgr);
                            if (basisNode != null)
                            {
                                var basisId = basisNode.Attributes["file"]?.Value;
                                if (!string.IsNullOrEmpty(basisId))
                                    fileIdMap.TryGetValue(basisId, out basisFile);
                            }

                            var virtualEntry = new MsuEntry
                            {
                                Name = virtualPath,
                                Type = FormatCatalog.Instance.GetTypeFromName(fileName),
                                Offset = 0,
                                Size = string.IsNullOrEmpty(length) ? 0 : uint.Parse(length),
                                DataSource = MsuEntry.SourceType.Virtual,
                                SourcePatch = deltaEntry.Name,
                                BasisFile = basisFile,
                                ExpectedHash = hash,
                                Architecture = DetectArchitecture(fileName)
                            };

                            entries.Add(virtualEntry);
                            virtualFiles[virtualEntry.Name] = virtualEntry;
                        }
                    }
                }
                else // component manifest
                {
                    var nsmgr = new XmlNamespaceManager(xml.NameTable);
                    nsmgr.AddNamespace("asm", "urn:schemas-microsoft-com:asm.v3");

                    var files = xml.SelectNodes("//asm:file", nsmgr);

                    foreach (XmlNode fileNode in files)
                    {
                        var fileName = fileNode.Attributes["name"]?.Value;
                        var destPath = fileNode.Attributes["destinationPath"]?.Value;
                        var sourceName = fileNode.Attributes["sourceName"]?.Value;

                        if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(destPath) || IsMetadataFile(fileName))
                            continue;

                        var resolvedPath = ResolveRuntimeVariables(destPath);
                        var fullPath = Path.Combine(resolvedPath, fileName).Replace("\\", VFS.DIR_DELIMITER);
                        var virtualPath = "PATCHED" + VFS.DIR_DELIMITER + fullPath;

                        if (!createdVirtualPaths.Add(virtualPath))
                            continue;

                        // Find delta in same component
                        MsuEntry deltaEntry = null;
                        string deltaFileName = null;
                        if (deltasByComponent.TryGetValue(componentFolder, out var componentDeltas))
                        {
                            deltaFileName = fileName ?? sourceName;
                            if (!componentDeltas.TryGetValue(deltaFileName, out deltaEntry))
                                componentDeltas.TryGetValue(deltaFileName.ToLower(), out deltaEntry);
                        }

                        if (deltaEntry == null)
                        {
                            Debug.WriteLine($"No delta found for {fileName} in component {componentFolder}");
                            Debug.WriteLine($"  Component key used: '{componentFolder}'");
                            Debug.WriteLine($"  File key used: '{deltaFileName}'");

                            // Check if component exists but with different case
                            var matchingComponent = deltasByComponent.Keys.FirstOrDefault(k =>
                                k.Equals(componentFolder, StringComparison.OrdinalIgnoreCase));
                            if (matchingComponent != null)
                            {
                                Debug.WriteLine($"  Found component with different case: '{matchingComponent}'");
                                if (deltasByComponent[matchingComponent].ContainsKey(deltaFileName))
                                {
                                    Debug.WriteLine($"  File exists in that component!");
                                }
                            }
                            continue;
                        }

                        var virtualEntry = new MsuEntry
                        {
                            Name = virtualPath,
                            Type = FormatCatalog.Instance.GetTypeFromName(fileName),
                            Offset = 0,
                            Size = 0, // Will be updated from delta header
                            DataSource = MsuEntry.SourceType.Virtual,
                            SourcePatch = deltaEntry.Name,
                            Architecture = DetectArchitecture(fullPath),
                            BaseFileName = fileName
                        };

                        entries.Add(virtualEntry);
                        virtualFiles[virtualEntry.Name] = virtualEntry;
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"ProcessManifest error: {ex.Message}");
            }
        }


        private void UpdateVirtualEntrySizesFromDeltas(List<Entry> entries, Dictionary<string, MsuEntry> virtualFiles,
            Dictionary<string, CabArcFile> cabArchives)
        {
            foreach (var entry in entries.OfType<MsuEntry>().Where(e => e.DataSource == MsuEntry.SourceType.Virtual && e.Size == 0))
            {
                if (string.IsNullOrEmpty(entry.SourcePatch))
                    continue;

                // Find the delta entry
                if (!virtualFiles.TryGetValue(entry.SourcePatch, out var deltaEntry))
                    continue;

                // Get the CAB containing this delta
                var cabPath = deltaEntry.CabPath ?? "";
                if (!cabArchives.TryGetValue(cabPath, out var cab))
                    continue;

                try
                {
                    // Read delta data and use DeltaOpener to get info
                    using (var stream = cabHandler.OpenEntry(cab, deltaEntry.CabEntryRef))
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        var deltaData = ms.ToArray();

                        // Use the existing DeltaOpener.GetDeltaInfo method
                        var deltaInfo = DeltaOpener.GetDeltaInfo(deltaData);
                        if (deltaInfo != null && deltaInfo.TargetSize > 0)
                        {
                            entry.Size = (uint)deltaInfo.TargetSize;
                            //Debug.WriteLine($"Got size {deltaInfo.TargetSize} for {entry.Name} from delta header");
                        }
                    }
                }
                catch { }
            }
        }

        private string ResolveRuntimeVariables(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // Common runtime variable mappings
            var replacements = new Dictionary<string, string>
            {
                { "$(runtime.drivers)", "System32\\drivers" },
                { "$(runtime.system32)", "System32" },
                { "$(runtime.windows)", "Windows" },
                { "$(runtime.inf)", "inf" },
                { "$(runtime.help)", "Help" },
                { "$(runtime.fonts)", "Fonts" },
                { "$(runtime.bootDrive)", "Boot" },
                { "$(runtime.programFiles)", "Program Files" },
                { "$(runtime.commonFiles)", "Program Files\\Common Files" },
                { "$(runtime.wbem)", "System32\\wbem" }
            };

            string result = path;
            foreach (var kvp in replacements)
            {
                result = result.Replace(kvp.Key, kvp.Value);
            }

            return result;
        }

        // Parse .mum files (Microsoft Update Manifest)
        private void ParseMumFile(byte[] mumData, Dictionary<string, string> deltaSourceFiles)
        {
            try
            {
                var xml = new XmlDocument();
                xml.LoadXml(Encoding.UTF8.GetString(mumData));

                var nsmgr = new XmlNamespaceManager(xml.NameTable);
                nsmgr.AddNamespace("asm", "urn:schemas-microsoft-com:asm.v3");

                // MUM files typically reference wrapper packages which contain the actual files
                // Look for package references
                /*
                var packages = xml.SelectNodes("//asm:package//asm:assemblyIdentity", nsmgr);
                foreach (XmlNode packageNode in packages)
                {
                    var packageName = packageNode.Attributes["name"]?.Value;
                    if (!string.IsNullOrEmpty(packageName))
                    {
                        Debug.WriteLine($"MUM references package: {packageName}");
                    }
                }*/

                // Look for update elements that might reference delta files
                /*var updates = xml.SelectNodes("//asm:update", nsmgr);
                foreach (XmlNode updateNode in updates)
                {
                    var updateName = updateNode.Attributes["name"]?.Value;
                    if (!string.IsNullOrEmpty(updateName))
                    {
                        Trace.WriteLine($"MUM update: {updateName}");
                    }

                }*/
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"ParseMumFile error: {ex.Message}");
            }
        }

        private void ProcessNestedCab(byte[] cabData, string cabPath,
            List<Entry> entries, Dictionary<string, CabArcFile> cabArchives,
            Dictionary<string, byte[]> metadataCache, Dictionary<string, MsuEntry> virtualFiles,
            HashSet<string> processedCabs,
            Dictionary<string, Dictionary<string, MsuEntry>> deltasByComponent,
            Dictionary<string, MsuEntry> deltasByFilename,
            List<(byte[] data, string type, string component)> allManifests)
        {
            try
            {
                using (var ms = new MemoryStream(cabData))
                using (var view = new ArcView(ms, cabPath, (uint)cabData.Length))
                {
                    var nestedCab = cabHandler.TryOpen(view) as CabArcFile;
                    if (nestedCab != null)
                    {
                        cabArchives[cabPath] = nestedCab;
                        ProcessCabContents(nestedCab, cabPath, entries, cabArchives,
                                         metadataCache, virtualFiles, processedCabs,
                                         deltasByComponent, deltasByFilename, allManifests);
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"ProcessNestedCab error: {ex.Message}", "ArcMSU");
            }
        }

        private void ParseManifestForDeltaSources(byte[] manifestData, Dictionary<string, string> deltaSourceFiles)
        {
            try
            {
                var xml = new XmlDocument();
                xml.LoadXml(Encoding.UTF8.GetString(manifestData));

                var nsmgr = new XmlNamespaceManager(xml.NameTable);
                nsmgr.AddNamespace("ci", "urn:ContainerIndex");

                // Find all delta source files referenced in manifest
                var sourceNodes = xml.SelectNodes("//ci:Delta/ci:Source[@type='PA30' or @type='PA19' or @type='PA31']", nsmgr);

                foreach (XmlNode sourceNode in sourceNodes)
                {
                    var sourceName = sourceNode.Attributes["name"]?.Value;
                    if (!string.IsNullOrEmpty(sourceName))
                    {
                        // Just record that this file is a delta source
                        // We'll find its actual path when we process the CAB entries
                        if (!deltaSourceFiles.ContainsKey(sourceName))
                            deltaSourceFiles[sourceName] = null; // Will be filled in later
                        Trace.WriteLine($"Manifest references delta source: {sourceName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"ParseManifestForDeltaSources error: {ex.Message}", "ArcMSU");
            }
        }
  
        private DeltaFolderType DetermineDeltaFolderType(string path)
        {
            if (path.Contains("\\f\\") || path.Contains("/f/"))
                return DeltaFolderType.Forward;
            if (path.Contains("\\r\\") || path.Contains("/r/"))
                return DeltaFolderType.Reverse;
            if (path.Contains("\\n\\") || path.Contains("/n/"))
                return DeltaFolderType.Null;
            return DeltaFolderType.None;
        }

        private DeltaType GetDeltaType(byte[] data)
        {
            if (data == null || data.Length < 4)
                return DeltaType.None;

            // Check for delta signatures at offset 0 or 4 (with CRC)
            for (int offset = 0; offset <= 4 && offset < data.Length - 3; offset += 4)
            {
                if (data[offset] == 'P' && data[offset + 1] == 'A')
                {
                    if (data[offset + 2] == '3' && data[offset + 3] == '0')
                        return DeltaType.PA30;
                    if (data[offset + 2] == '1' && data[offset + 3] == '9')
                        return DeltaType.PA19;
                    if (data[offset + 2] == '3' && data[offset + 3] == '1')
                        return DeltaType.PA31;
                }
            }
            return DeltaType.None;
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            var msuArc = arc as MsuArchive;
            var msuEntry = entry as MsuEntry;

            if (msuArc == null || msuEntry == null)
                return Stream.Null;

            try
            {
                switch (msuEntry.DataSource)
                {
                    case MsuEntry.SourceType.Direct:
                        return OpenDirectEntry(msuEntry);

                    case MsuEntry.SourceType.Cab:
                    case MsuEntry.SourceType.Delta:
                        return OpenCabEntry(msuArc, msuEntry);

                    case MsuEntry.SourceType.Virtual:
                        return OpenVirtualEntry(msuArc, msuEntry);

                    default:
                        return Stream.Null;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"OpenEntry error: {ex.Message}", "ArcMSU");
                return Stream.Null;
            }
        }

        private Stream OpenDirectEntry(MsuEntry entry)
        {
            if (entry.CachedData != null)
                return new MemoryStream(entry.CachedData, false);

            return Stream.Null;
        }

        private Stream OpenCabEntry(MsuArchive arc, MsuEntry entry)
        {
            Trace.WriteLine($"OpenCabEntry called for: {entry.Name}");

            if (entry.CachedData != null)
            {
                Trace.WriteLine($"Returning cached data, size: {entry.CachedData.Length}");
                return new MemoryStream(entry.CachedData, false);
            }

            // Find the CAB archive
            var cabPath = entry.CabPath ?? "";
            if (!arc.CabArchives.TryGetValue(cabPath, out var cab))
            {
                Trace.WriteLine($"CAB archive not found for path: {cabPath}");
                return Stream.Null;
            }

            if (entry.CabEntryRef == null)
            {
                Trace.WriteLine($"No CabEntry reference for: {entry.Name}");
                return Stream.Null;
            }

            try
            {
                Trace.WriteLine($"Opening from CAB: {entry.CabEntryRef.Name}");
                Trace.WriteLine($"  Offset: {entry.CabEntryRef.Offset}, Size: {entry.CabEntryRef.Size}");

                // Use CAB handler to open
                var stream = arc.CabHandler.OpenEntry(cab, entry.CabEntryRef);
                if (stream == null || stream == Stream.Null)
                {
                    Trace.WriteLine($"CAB handler returned null stream", "ArcMSU");
                    return Stream.Null;
                }

                //Trace.WriteLine($"Successfully opened stream from CAB");
                return stream;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Failed to open CAB entry {entry.Name}: {ex.Message}", "ArcMSU");
                Trace.WriteLine($"Stack trace: {ex.StackTrace}", "ArcMSU");
                return Stream.Null;
            }
        }

        private string ExtractComponentName(string componentPath)
        {
            // amd64_microsoft-windows-tcpip-binaries_31bf3856ad364e35_10.0.19041.3324_none_7e3bd5cc5e0e8754
            // -> microsoft-windows-tcpip-binaries
            var parts = componentPath.Split('_');
            return parts.Length >= 2 ? parts[1] : null;
        }

        private string ExtractVersionFromComponentPath(string componentPath)
        {
            // Extract version like 10.0.19041.3324
            var parts = componentPath.Split('_');
            return parts.Length >= 4 ? parts[3] : null;
        }

        private int CompareVersions(string version1, string version2)
        {
            try
            {
                var v1 = new Version(version1);
                var v2 = new Version(version2);
                return v1.CompareTo(v2);
            }
            catch
            {
                return string.Compare(version1, version2, StringComparison.OrdinalIgnoreCase);
            }
        }

        // NOTE: UpdateCompression.ApplyDelta already handles CRC32 prefix stripping - no need to handle it here
        private byte[] GetBaseFileForDelta(MsuEntry deltaEntry, MsuArchive arc)
        {
            Debug.WriteLine($"GetBaseFileForDelta: Finding base file for delta: {deltaEntry.Name}");

            if (deltaEntry.DeltaFolderType != DeltaFolderType.Forward)
                return null;

            // Extract component info from the delta path
            var pathParts = deltaEntry.Name.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

            string componentFolder = null;
            string fileName = null;

            for (int i = 0; i < pathParts.Length - 2; i++)
            {
                if (pathParts[i + 1] == "f" || pathParts[i + 1] == "r" || pathParts[i + 1] == "n")
                {
                    componentFolder = pathParts[i];
                    fileName = pathParts[i + 2];
                    break;
                }
            }

            if (componentFolder == null || fileName == null)
                return null;

            var componentParts = componentFolder.Split('_');
            if (componentParts.Length < 5)
                return null;

            var arch = componentParts[0];
            var componentName = componentParts[1];
            var publicKeyToken = componentParts[2];
            var targetVersion = componentParts[3];
            var language = componentParts[4];

            Debug.WriteLine($"Looking for {componentName}, target version: {targetVersion}");

            // Search WinSxS for current version
            var winsxsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "WinSxS");
            var searchPattern = $"{arch}_{componentName}_{publicKeyToken}_*_{language}_*";

            try
            {
                var directories = Directory.GetDirectories(winsxsPath, searchPattern);

                foreach (var dir in directories)
                {
                    // Skip target version
                    if (dir.Contains($"_{targetVersion}_"))
                        continue;

                    var currentFilePath = Path.Combine(dir, fileName);
                    if (!File.Exists(currentFilePath))
                        continue;

                    Debug.WriteLine($"Found current file: {currentFilePath}");
                    var currentData = File.ReadAllBytes(currentFilePath);

                    // Check for reverse delta to get base version
                    var reverseDeltaPath = Path.Combine(dir, "r", fileName);
                    if (File.Exists(reverseDeltaPath))
                    {
                        Debug.WriteLine($"Applying reverse delta from: {reverseDeltaPath}");

                        var reverseDeltaData = File.ReadAllBytes(reverseDeltaPath);
                        var dllPath = DeltaOpener.GetUpdateCompressionDllPath();

                        if (!string.IsNullOrEmpty(dllPath))
                        {
                            var baseData = UpdateCompression.ApplyDelta(currentData, reverseDeltaData, dllPath);
                            if (baseData != null)
                            {
                                Debug.WriteLine($"Got base version via reverse delta, size: {baseData.Length}");
                                return baseData;
                            }
                        }
                    }

                    // No reverse delta, try current file as base
                    Debug.WriteLine($"Using current file as base");
                    return currentData;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error: {ex.Message}");
            }

            // Fallback to system search
            return DeltaOpener.GetBaseFile(fileName);
        }


        private Stream OpenVirtualEntry(MsuArchive arc, MsuEntry entry)
        {
            Debug.WriteLine($"OpenVirtualEntry called for: {entry.Name}");
            Debug.WriteLine($"  SourcePatch: {entry.SourcePatch}");

            if (string.IsNullOrEmpty(entry.SourcePatch))
            {
                Trace.WriteLine($"No SourcePatch for virtual entry: {entry.Name}");
                return Stream.Null;
            }

            // SourcePatch now contains the full delta path
            if (!arc.VirtualFiles.TryGetValue(entry.SourcePatch, out var deltaEntry))
            {
                Trace.WriteLine($"Delta not found: {entry.SourcePatch}");
                return Stream.Null;
            }

            Debug.WriteLine($"Found delta entry: {deltaEntry.Name}");
            Debug.WriteLine($"  Delta type: {deltaEntry.DeltaFolderType}");
            Debug.WriteLine($"  Delta size: {deltaEntry.Size}");

            // Get delta data
            byte[] deltaData = null;
            if (deltaEntry.CachedData != null)
            {
                deltaData = deltaEntry.CachedData;
                Trace.WriteLine($"Using cached delta data, size: {deltaData.Length}");
            }
            else
            {
                Trace.WriteLine($"Reading delta from CAB...");
                try
                {
                    using (var stream = OpenCabEntry(arc, deltaEntry))
                    {
                        if (stream == null || stream == Stream.Null)
                        {
                            Trace.WriteLine($"Failed to open delta entry from CAB");
                            return Stream.Null;
                        }

                        using (var ms = new MemoryStream())
                        {
                            stream.CopyTo(ms);
                            deltaData = ms.ToArray();
                            Debug.WriteLine($"Read delta data, size: {deltaData.Length}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Error reading delta data: {ex.Message}", "ArcMSU");
                    return Stream.Null;
                }

                // Since we have the data now, detect type if not already done
                if (deltaEntry.DeltaFileType == DeltaType.None && deltaData != null)
                {
                    deltaEntry.DeltaFileType = GetDeltaType(deltaData);
                    Trace.WriteLine($"Detected delta type: {deltaEntry.DeltaFileType}");
                }
            }

            if (deltaData == null || deltaData.Length == 0)
            {
                Trace.WriteLine($"Failed to get delta data or delta is empty");
                return Stream.Null;
            }

            // Get base file if needed
            byte[] baseData = null;

            // For forward deltas, find the current version in WinSxS
            if (deltaEntry.DeltaFolderType == DeltaFolderType.Forward)
            {
                Debug.WriteLine($"Forward delta - looking for base file");
                baseData = GetBaseFileForDelta(deltaEntry, arc);
                if (baseData == null)
                {
                    Trace.WriteLine($"Could not find base file for forward delta: {deltaEntry.Name}");
                    // For forward deltas, we might still try without base (null delta)
                }
                else
                {
                    Debug.WriteLine($"Found base file, size: {baseData.Length}");
                }
            }
            else if (!string.IsNullOrEmpty(entry.BasisFile))
            {
                Debug.WriteLine($"Looking for basis file: {entry.BasisFile}");
                // For other cases, use the specified basis file
                if (arc.VirtualFiles.TryGetValue(entry.BasisFile, out var basisEntry))
                {
                    using (var stream = OpenEntry(arc, basisEntry))
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        baseData = ms.ToArray();
                    }
                    Debug.WriteLine($"Found basis in virtual files, size: {baseData.Length}");
                }
                else
                {
                    // Try to get from system
                    baseData = DeltaOpener.GetBaseFile(entry.BasisFile);
                    if (baseData != null)
                        Debug.WriteLine($"Found basis in system, size: {baseData.Length}");
                }
            }
            else if (deltaEntry.DeltaFolderType == DeltaFolderType.Null)
            {
                // Null deltas don't need a base file
                Debug.WriteLine($"Null delta - no base file needed");
                baseData = null;
            }

            // Apply delta
            var dllPath = DeltaOpener.GetUpdateCompressionDllPath();
            if (string.IsNullOrEmpty(dllPath))
            {
                Trace.WriteLine("UpdateCompression.dll not found");
                return Stream.Null;
            }

            Debug.WriteLine($"Applying delta with UpdateCompression.dll:");
            Debug.WriteLine($"  Base data: {(baseData != null ? baseData.Length.ToString() : "null")} bytes");
            Debug.WriteLine($"  Delta data: {deltaData.Length} bytes");

            var result = UpdateCompression.ApplyDelta(baseData, deltaData, dllPath);
            if (result == null)
            {
                Trace.WriteLine($"Delta application failed for {entry.Name}");
                return Stream.Null;
            }

            Debug.WriteLine($"Delta applied successfully, result size: {result.Length}");

            // Verify hash if provided
            if (!string.IsNullOrEmpty(entry.ExpectedHash))
            {
                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    var hash = sha256.ComputeHash(result);
                    var hashStr = BitConverter.ToString(hash).Replace("-", "").ToLower();

                    if (!hashStr.Equals(entry.ExpectedHash, StringComparison.OrdinalIgnoreCase))
                        Trace.WriteLine($"Hash mismatch: expected {entry.ExpectedHash}, got {hashStr}");
                    else
                        Debug.WriteLine($"Hash verified successfully");
                }
            }

            // Cache result if small
            if (result.Length < 1024 * 1024)
            {
                entry.CachedData = result;
                Debug.WriteLine($"Cached result (under 1MB)");
            }

            return new MemoryStream(result, false);
        }

        private bool IsMetadataFile(string fileName)
        {
            return MetadataPatterns.Any(pattern =>
                fileName.EndsWith(pattern.TrimStart('*'), StringComparison.OrdinalIgnoreCase));
        }

        private string DetectArchitecture(string path)
        {
            if (path.IndexOf("x86_",   StringComparison.OrdinalIgnoreCase) >= 0)   return "x86";
            if (path.IndexOf("amd64_", StringComparison.OrdinalIgnoreCase) >= 0) return "x64";
            if (path.IndexOf("wow64_", StringComparison.OrdinalIgnoreCase) >= 0) return "WOW64";
            if (path.IndexOf("msil_",  StringComparison.OrdinalIgnoreCase) >= 0)  return "MSIL";
            return null;
        }

        private string GetUpdateCompressionDllPath()
        {
            return DeltaOpener.GetUpdateCompressionDllPath();
        }
    }
}