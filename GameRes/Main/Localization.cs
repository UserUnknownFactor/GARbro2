#if DEBUG
    #define DEBUG_MISSING_LOCALIZATIONS
#endif
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Text;


#pragma warning disable CS0162,CS0168
public static partial class Localization
{
    /// <summary>
    /// Gets or sets the ResourceManager used for string localization.
    /// </summary>
    public static System.Resources.ResourceManager ResourceManager { get; set; }

    private static readonly ConcurrentDictionary<string, bool> _hasCultureResources = new ConcurrentDictionary<string, bool>();
    private static readonly object _loadLock = new object();
    private static bool _csvLoaded = false;
    private static string _loadedCulture = null;

    // CSV parsing configuration
    private const char CSV_SEPARATOR = '→';
    private const    char CSV_ESCAPE = '¶';
    private const    string UTF8_BOM = "\uFEFF";

    private struct LocalizationEntry
    {
        public string Singular;
        public string Plural1;
        public string Plural2;
        public string Plural3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string GetPlural (int suffix)
        {
            switch (suffix)
            {
            case 1:  return Singular;
            case 2:  return Plural1 ?? Singular;
            case 3:  return Plural2 ?? Plural1 ?? Singular;
            case 4:  return Plural3 ?? Plural2 ?? Plural1 ?? Singular;
            default: return Singular;
            }
        }
    }

    /// <summary>
    /// Pluralization rules for different cultures.
    /// </summary>
    private static readonly Dictionary<string, Func<int, int>> PluralizationRules = new Dictionary<string, Func<int, int>>()
    {
        ["be-BY"] = GetCyrillicPluralizationSuffix,
        ["cs-CZ"] = GetCzechPluralizationSuffix,
        ["de-DE"] = GetEnglishPluralizationSuffix,
        ["en-GB"] = GetEnglishPluralizationSuffix,
        ["en-US"] = GetEnglishPluralizationSuffix,
        ["es-ES"] = GetEnglishPluralizationSuffix,
        ["fr-FR"] = GetFrenchPluralizationSuffix,
        ["pl-PL"] = GetPolishPluralizationSuffix,
        ["ru-RU"] = GetCyrillicPluralizationSuffix,
        ["sk-SK"] = GetCzechPluralizationSuffix,
        ["uk-UA"] = GetCyrillicPluralizationSuffix
    };

    private static readonly List<System.Resources.ResourceManager> _resourceManagers = new List<System.Resources.ResourceManager>();
    private static readonly object _managersLock = new object();

    static Localization()
    {
        LoadCsvResources();
        GetResourceDiagnostics();
    }

    /// <summary>
    /// Register a ResourceManager for fallback localization.
    /// </summary>
    public static void RegisterResourceManager (System.Resources.ResourceManager manager)
    {
        if (manager == null)
            return;

        lock (_managersLock)
        {
            if (!_resourceManagers.Contains (manager))
                _resourceManagers.Add (manager);
        }
    }

    /// <summary>
    /// Unregister a ResourceManager.
    /// </summary>
    public static void UnregisterResourceManager (System.Resources.ResourceManager manager)
    {
        if (manager == null)
            return;

        lock (_managersLock)
        {
            _resourceManagers.Remove (manager);
        }
    }

    /// <summary>
    /// Clear all registered ResourceManagers except the default one.
    /// </summary>
    public static void ClearResourceManagers()
    {
        lock (_managersLock)
        {
            // Keep only the first one (our own)
            if (_resourceManagers.Count > 1)
                _resourceManagers.RemoveRange (1, _resourceManagers.Count - 1);
        }
    }

    /// <summary>
    /// Get diagnostic information about registered resources.
    /// </summary>
    public static string GetResourceDiagnostics()
    {
        var sb = new StringBuilder();
        sb.AppendLine ($"CSV Resources loaded: {_csvResources.Count}");
        sb.AppendLine ($"ResourceManagers registered: {_resourceManagers.Count}");

        lock (_managersLock)
        {
            int index = 0;
            foreach (var manager in _resourceManagers)
                sb.AppendLine ($"  [{index++}] {manager.BaseName}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Loads CSV localization files for current culture.
    /// </summary>
    private static void LoadCsvResources()
    {
        var culture = CultureInfo.CurrentUICulture.Name;

        if (_csvLoaded && _loadedCulture == culture)
            return;

        lock (_loadLock)
        {
            if (_csvLoaded && _loadedCulture == culture)
                return;

            //_csvResources.Clear();

            try
            {
                var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var location = assembly.Location;
                var directory = Path.GetDirectoryName (location);
                var fileName = Path.GetFileNameWithoutExtension (location);
                var langCode = culture.Split('-')[0];

                var csvPath = Path.Combine (directory, "Translations", "GARbro.en-US.csv");
                if (File.Exists (csvPath))
                {
                    var fileInfo = new FileInfo(csvPath);
                    if (fileInfo.Length != DEFAULT_CSV_SIZE)
                    {
                        Trace.WriteLine ($"Translations\\GARbro.en-US.csv is loaded since it's not the default file (length {fileInfo.Length} != {DEFAULT_CSV_SIZE})...");
                        var csvContent = File.ReadAllText (csvPath, Encoding.UTF8);
                        ParseCsvToResources (csvContent);
                    }
                }
                csvPath = Path.Combine (directory, "Translations", $"GARbro.{culture}.csv");
                if (!File.Exists (csvPath))
                    csvPath = Path.Combine (directory, "Translations", $"{fileName}.{langCode}.csv");
                if (!File.Exists (csvPath))
                    csvPath = Path.Combine (directory, "Translations", $"{fileName}.{langCode}.csv");
                if (csvPath != null && File.Exists (csvPath))
                {
                    var csvContent = File.ReadAllText (csvPath, Encoding.UTF8);
                    ParseCsvToResources (csvContent); // this will replace default trasnaltions
                }

                _loadedCulture = culture;
                _csvLoaded = true;
            }
            catch (Exception ex)
            {
#if DEBUG_MISSING_LOCALIZATIONS
                Trace.WriteLine ($"Error loading CSV resources: {ex.Message}", "Localization.LoadCsv");
#endif
            }
        }
    }

    /// <summary>
    /// Optimized CSV parser with custom escape handling.
    /// </summary>
    private static void ParseCsvToResources (string text)
    {
        if (string.IsNullOrEmpty (text))
            return;

        var rows = new List<List<string>>();
        var currentRow = new List<StringBuilder> { new StringBuilder (256) };
        var i = 0;
        var unescaped = true;
        var prevChar = '\0';

        // Remove BOM if present
        if (text.StartsWith (UTF8_BOM))
            text = text.Substring (UTF8_BOM.Length);

        for (int pos = 0; pos < text.Length; pos++)
        {
            var ch = text[pos];
            var isLast = (pos >= text.Length - 1);

            if (ch == CSV_ESCAPE)
            {
                if (!unescaped && prevChar == CSV_ESCAPE)
                    currentRow[i].Append (ch);

                unescaped = !unescaped;
            }
            else if (ch == CSV_SEPARATOR && unescaped)
            {
                i++;
                currentRow.Add (new StringBuilder (256));
                ch = '\0';
            }
            else if ((ch == '\n' && unescaped) || isLast)
            {
                if (prevChar == '\r' && currentRow[i].Length > 0)
                    currentRow[i].Length--;  // remove last char

                if (ch != '\n')
                    currentRow[i].Append (ch);

                if (currentRow.Count == 1 && currentRow[0].Length == 0)
                    break;

                var stringRow = new List<string>(currentRow.Count);
                foreach (var sb in currentRow)
                    stringRow.Add (sb.ToString());

                if (!stringRow[0].StartsWith ("//"))
                    rows.Add (stringRow);

                currentRow = new List<StringBuilder> { new StringBuilder (256) };
                i = 0;
                ch = '\0';
            }
            else
            {
                currentRow[i].Append (ch);
                if (!unescaped)
                    unescaped = true;
            }

            prevChar = ch;
        }

        bool isHeader = true;
        foreach (var row in rows)
        {
            if (isHeader)
            {
                isHeader = false;
                continue;
            }

            if (row.Count < 2)
                continue;

            var entry = new LocalizationEntry
            {
                Singular = row.Count > 1 ? (string.IsNullOrEmpty (row[1]) ? null : row[1]) : null,
                Plural1  = row.Count > 2 ? (string.IsNullOrEmpty (row[2]) ? null : row[2]) : null,
                Plural2  = row.Count > 3 ? (string.IsNullOrEmpty (row[3]) ? null : row[3]) : null,
                Plural3  = row.Count > 4 ? (string.IsNullOrEmpty (row[4]) ? null : row[4]) : null
            };

            _csvResources[row[0]] = entry;
        }
    }

    /// <summary>
    /// Returns the appropriate pluralized string for the given count and message identifier.
    /// </summary>
    public static string Plural (string messageId, int count)
    {
        if (string.IsNullOrEmpty (messageId))
            return messageId ?? string.Empty;

        try
        {
            var suffix = GetPluralizationSuffix (count);

            // Try CSV resources first
            if (_csvResources.TryGetValue (messageId, out var entry))
                return entry.GetPlural (suffix) ?? messageId;

            // Fallback to ResourceManager
            if (ResourceManager != null)
            {
                var primaryKey = $"{messageId}{suffix}";
                return TryGetResource (primaryKey) ??
                       TryGetResource ($"{messageId}1") ??
                       TryGetResource (messageId) ??
                       messageId;
            }
        }
        catch (Exception ex)
        {
#if DEBUG_MISSING_LOCALIZATIONS
            Trace.WriteLine ($"Error in pluralization for '{messageId}': {ex.Message}", "Localization.Plural");
#endif
        }
        return messageId;
    }

    /// <summary>
    /// Extension method for pluralization with count formatting.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Pluralize (this int count, string messageId)
    {
        return string.Format (Plural (messageId, count), count);
    }

    /// <summary>
    /// Extension method for pluralization with decimal count formatting.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Pluralize (this decimal count, string messageId, string format = null)
    {
        int pluralForm = GetDecimalPluralizationForm (count);
        string pattern = GetPluralPattern (pluralForm, messageId);
        return Format (pattern, format != null ? count.ToString (format) : count.ToString());
    }

    /// <summary>
    /// Extension method for pluralization with double count formatting.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Pluralize (this double count, string messageId, string format = null)
    {
        return ((decimal)count).Pluralize (messageId, format);
    }

    /// <summary>
    /// Extension method for pluralization with float count formatting.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Pluralize (this float count, string messageId, string format = null)
    {
        return ((decimal)count).Pluralize (messageId, format);
    }

    /// <summary>
    /// Determines pluralization form for decimal numbers.
    /// </summary>
    private static int GetDecimalPluralizationForm (decimal count)
    {
        var culture = CultureInfo.CurrentUICulture.Name;

        // Check if it's a whole number
        if (count == Math.Truncate (count))
            return GetPluralizationSuffix((int)Math.Abs (count));

        // For non-whole numbers
        switch (culture)
        {
        case "ru-RU":
        case "uk-UA":
        case "be-BY":
            return 2; // decimals use genitive singular
        case "pl-PL":
            return 3; // decimals use genitive plural
        case "cs-CZ":
        case "sk-SK":
            return 3;
        case "fr-FR":
            return Math.Abs (count) > 0 && Math.Abs (count) < 2 ? 1 : 2;
        default:
            return Math.Abs (count) == 1.0m ? 1 : 2;
        }
    }

    /// <summary>
    /// Gets the appropriate plural pattern from resources.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetPluralPattern (int pluralForm, string messageId)
    {
        // Try CSV resources first
        if (_csvResources.TryGetValue (messageId, out var entry))
            return entry.GetPlural (pluralForm) ?? messageId;

        // Fallback to ResourceManager
        if (ResourceManager != null)
        {
            var key = $"{messageId}{pluralForm}";
            return TryGetResource (key) ?? 
                   TryGetResource ($"{messageId}1") ?? 
                   TryGetResource (messageId) ?? 
                   messageId;
        }

        return messageId;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetPluralizationSuffix (int count)
    {
        var culture = CultureInfo.CurrentUICulture.Name;

        if (PluralizationRules.TryGetValue (culture, out var rule))
        {
            if (!_hasCultureResources.TryGetValue (culture, out var hasResources))
            {
                hasResources = CheckCultureHasSpecificResources();
                _hasCultureResources[culture] = hasResources;
            }

            if (hasResources)
                return rule (count);
        }

        return GetEnglishPluralizationSuffix (count);
    }

    private static bool CheckCultureHasSpecificResources()
    {
        // Check CSV resources first
        if (_csvResources.Count > 0)
            return true;

        // Then check ResourceManager
        try
        {
            if (ResourceManager != null)
            {
                var resourceSet = ResourceManager.GetResourceSet (CultureInfo.CurrentUICulture, true, false);
                if (resourceSet != null)
                {
                    var invariantSet = ResourceManager.GetResourceSet (CultureInfo.InvariantCulture, true, false);
                    if (resourceSet != invariantSet)
                        return true;
                }
            }
        }
        catch { }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string TryGetResource (string key)
    {
        if (_csvResources.TryGetValue (key, out var entry))
            return entry.Singular;

        lock (_managersLock)
        {
            foreach (var manager in _resourceManagers)
            {
                try
                {
                    var result = manager.GetString (key);
                    if (!string.IsNullOrEmpty (result))
                        return result;
                }
                catch 
                { 
                    // This manager doesn't have the key, continue to next
                }
            }
        }

#if DEBUG_MISSING_LOCALIZATIONS
        Trace.WriteLine ($"Missing string resource for '{key}' token", "Localization");
#endif

        return null;
    }

    #region Pluralization Rules

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetEnglishPluralizationSuffix (int count)
    {
        return Math.Abs (count) == 1 ? 1 : 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetFrenchPluralizationSuffix (int count)
    {
        var absCount = Math.Abs (count);
        return absCount == 0 || absCount == 1 ? 1 : 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetCyrillicPluralizationSuffix (int count)
    {
        var absCount = Math.Abs (count);
        var lastDigit = absCount % 10;
        var lastTwoDigits = absCount % 100;

        if (lastDigit == 1 && lastTwoDigits != 11)
            return 1;

        if (lastDigit >= 2 && lastDigit <= 4 && (lastTwoDigits < 12 || lastTwoDigits > 14))
            return 2;

        return 3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetPolishPluralizationSuffix (int count)
    {
        if (count == 1)
            return 1;

        var absCount = Math.Abs (count);
        var lastDigit = absCount % 10;
        var lastTwoDigits = absCount % 100;

        if (lastDigit >= 2 && lastDigit <= 4 && (lastTwoDigits < 12 || lastTwoDigits > 14))
            return 2;

        return 3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetCzechPluralizationSuffix (int count)
    {
        var absCount = Math.Abs (count);

        if (absCount == 1)
            return 1;

        if (absCount >= 2 && absCount <= 4)
            return 2;

        return 3;
    }

    #endregion

    /// <summary>
    /// Formats a string using named placeholders.
    /// </summary>
    public static string Format (string msgText, params (string name, object value)[] namedArgs)
    {
        if (string.IsNullOrEmpty (msgText))
            return msgText ?? string.Empty;

        try
        {
            var localized = TryGetResource (msgText);
            if (!string.IsNullOrEmpty (localized))
                msgText = localized;

            var sb = new StringBuilder (msgText);
            var args = new object[namedArgs.Length];

            for (int i = 0; i < namedArgs.Length; i++)
            {
                var (name, value) = namedArgs[i];
                args[i] = value;
                sb.Replace ($"{{{name}}}", $"{{{i}}}");
            }

            return string.Format (sb.ToString(), args);
        }
        catch (FormatException e)
        {
#if DEBUG_MISSING_LOCALIZATIONS
            Trace.TraceError ($"Localization format exception {msgText}: {e.Message}");
#endif
        }
        catch (Exception e)
        {
#if DEBUG_MISSING_LOCALIZATIONS
            Trace.TraceError ($"Localization exception {msgText}: {e.Message}");
#endif
        }
        return msgText;
    }

    /// <summary>
    /// Formats a string using indexed placeholders.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Format (string msgText, params object[] args)
    {
        if (string.IsNullOrEmpty (msgText))
            return msgText ?? string.Empty;

        try
        {
            var localized = TryGetResource (msgText);
            if (!string.IsNullOrEmpty (localized))
                msgText = localized;
            return string.Format (msgText, args);
        }
        catch (FormatException e)
        {
#if DEBUG_MISSING_LOCALIZATIONS
            Trace.TraceError ($"Localization format exception for {msgText}: {e.Message}");
#endif
        }
        catch (Exception e)
        {
#if DEBUG_MISSING_LOCALIZATIONS
            Trace.TraceError ($"Localization exception for {msgText}: {e.Message}");
#endif
        }
        return msgText;
    }

    /// <summary>
    /// Localizes a string or StringID.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Format (string msgText)
    {
        if (string.IsNullOrEmpty (msgText))
            return msgText ?? string.Empty;

        try
        {
            return TryGetResource (msgText) ?? msgText;
        }
        catch (Exception e)
        {
#if DEBUG_MISSING_LOCALIZATIONS
            Trace.TraceError ($"Localization exception for {msgText}: {e.Message}");
#endif
        }
        return msgText;
    }

    /// <summary>
    /// Short alias for Format (string).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string _T (string msgText)
    {
        return Format (msgText);
    }

    /// <summary>
    /// Reloads CSV resources for a different culture.
    /// </summary>
    public static void ReloadForCulture (CultureInfo culture)
    {
        CultureInfo.CurrentUICulture = culture;
        _csvLoaded = false;
        LoadCsvResources();
    }

    /// <summary>
    /// Plural with custom formatting for any IFormattable type.
    /// </summary>
    public static string Pluralize<T>(this T count, string messageId, string format = null) 
        where T : IFormattable, IComparable
    {
        decimal decimalCount = Convert.ToDecimal (count);
        int pluralForm = GetDecimalPluralizationForm (decimalCount);
        string pattern = GetPluralPattern (pluralForm, messageId);

        string formattedCount = format != null 
            ? count.ToString (format, CultureInfo.CurrentCulture) 
            : count.ToString();

        return Format (pattern, formattedCount);
    }

    public static string FormatFileSize (long bytes)
    {
        string[] sizes = { "B", "KiB", "MiB", "GiB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        string size_order = sizes[order];
        var localized = TryGetResource ($"Size_{size_order}");
        if (!string.IsNullOrEmpty (localized))
            size_order = localized;

        return $"{len:0.##} {size_order}";
    }
}
#pragma warning restore CS0162,CS0168