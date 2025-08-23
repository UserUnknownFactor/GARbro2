using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using GameRes;
using GameRes.Formats;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace SchemeEditor
{
    public static class FileOperations
    {
        private const int MaxRecentFiles = 10;
        private static List<string> recentFiles = new List<string>();
        private static string settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SchemeEditor", "settings.json");

        public static void LoadRecentFiles(MainWindow window)
        {
            try
            {
                var settingsDir = Path.GetDirectoryName(settingsPath);
                if (!Directory.Exists(settingsDir))
                    Directory.CreateDirectory(settingsDir);

                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
                    var settings = JsonConvert.DeserializeObject<AppSettings>(json);
                    if (settings?.RecentFiles != null)
                    {
                        recentFiles = settings.RecentFiles.Where(File.Exists).Take(MaxRecentFiles).ToList();
                    }
                }
            }
            catch { }

            UpdateRecentFilesMenu(window);
        }

        private static void SaveRecentFiles()
        {
            try
            {
                var settingsDir = Path.GetDirectoryName(settingsPath);
                if (!Directory.Exists(settingsDir))
                    Directory.CreateDirectory(settingsDir);

                var settings = new AppSettings { RecentFiles = recentFiles };
                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(settingsPath, json);
            }
            catch { }
        }

        private static void AddToRecentFiles(string filePath, MainWindow window)
        {
            recentFiles.RemoveAll(f => f.Equals(filePath, StringComparison.OrdinalIgnoreCase));
            recentFiles.Insert(0, filePath);
            if (recentFiles.Count > MaxRecentFiles)
                recentFiles.RemoveRange(MaxRecentFiles, recentFiles.Count - MaxRecentFiles);

            SaveRecentFiles();
            UpdateRecentFilesMenu(window);
        }

        private static void UpdateRecentFilesMenu(MainWindow window)
        {
            var recentFilesMenuItem = window.FindName("RecentFilesMenuItem") as MenuItem;
            if (recentFilesMenuItem == null) return;

            recentFilesMenuItem.Items.Clear();

            if (recentFiles.Count == 0)
            {
                var emptyItem = new MenuItem { Header = "(No recent files)", IsEnabled = false };
                recentFilesMenuItem.Items.Add(emptyItem);
            }
            else
            {
                for (int i = 0; i < recentFiles.Count; i++)
                {
                    var filePath = recentFiles[i];
                    var menuItem = new MenuItem
                    {
                        Header = $"{i + 1}. {Path.GetFileName(filePath)}",
                        ToolTip = filePath
                    };

                    menuItem.Click += (s, e) => OpenRecentFile(filePath, window);
                    recentFilesMenuItem.Items.Add(menuItem);
                }

                recentFilesMenuItem.Items.Add(new Separator());

                var clearItem = new MenuItem { Header = "Clear Recent Files" };
                clearItem.Click += (s, e) =>
                {
                    recentFiles.Clear();
                    SaveRecentFiles();
                    UpdateRecentFilesMenu(window);
                };
                recentFilesMenuItem.Items.Add(clearItem);
            }
        }

        private static void OpenRecentFile(string filePath, MainWindow window)
        {
            if (File.Exists(filePath))
            {
                // Get the current database from MainWindow
                var databaseField = window.GetType().GetField("currentDatabase",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var database = databaseField?.GetValue(window) as SchemeDataBase;

                // Get the RefreshSchemeList method
                var refreshMethod = window.GetType().GetMethod("RefreshSchemeList",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Action refreshCallback = () => refreshMethod?.Invoke(window, null);

                if (database != null)
                {
                    OpenFile(window, filePath, database, refreshCallback);
                }
            }
            else
            {
                MessageBox.Show($"File not found: {filePath}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                recentFiles.Remove(filePath);
                SaveRecentFiles();
                UpdateRecentFilesMenu(window);
            }
        }

        public static void OpenScheme(MainWindow window, SchemeDataBase database, Action refreshCallback)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Scheme Files (*.dat)|*.dat|JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                Title = "Open Scheme File",
                InitialDirectory = Directory.GetCurrentDirectory()
            };

            if (dialog.ShowDialog() == true)
            {
                OpenFile(window, dialog.FileName, database, refreshCallback);
            }
        }

        public static void OpenFile(MainWindow mainWindow, string filePath, SchemeDataBase currentDatabase, Action refreshCallback)
        {
            try
            {
                using (var stream = File.OpenRead(filePath))
                {
                    if (filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        FormatCatalog.Instance.DeserializeSchemeJson(stream);
                    else
                        FormatCatalog.Instance.DeserializeScheme(stream);
                }

                // Get backup database field
                var backupDatabaseField = mainWindow.GetType()
                    .GetField("backupDatabase", BindingFlags.NonPublic | BindingFlags.Instance);
                var backupDatabase = backupDatabaseField?.GetValue(mainWindow) as SchemeDataBase;

                // Extract the loaded data from FormatCatalog to both current and backup
                ExtractSchemeDataFromCatalog(currentDatabase, backupDatabase);

                SetCurrentFilePath(mainWindow, filePath);
                AddToRecentFiles(filePath, mainWindow);
                refreshCallback?.Invoke();
                SetStatusText(mainWindow, $"Loaded: {Path.GetFileName(filePath)}");

                // Clear the has changes flag since we just loaded
                var setHasChangesMethod = mainWindow.GetType()
                    .GetMethod("SetHasChanges", BindingFlags.Public | BindingFlags.Instance);
                setHasChangesMethod?.Invoke(mainWindow, new object[] { false });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading scheme: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void ExtractSchemeDataFromCatalog(SchemeDataBase currentDatabase, SchemeDataBase backupDatabase = null)
        {
            // Clear current database
            currentDatabase.SchemeMap.Clear();
            currentDatabase.GameMap.Clear();

            // Clear backup if provided
            if (backupDatabase != null)
            {
                backupDatabase.SchemeMap.Clear();
                backupDatabase.GameMap.Clear();
            }

            // Copy schemes from formats
            foreach (var format in FormatCatalog.Instance.Formats)
            {
                if (format.Scheme != null)
                {
                    try
                    {
                        // Serialize once
                        var json = JsonConvert.SerializeObject(format.Scheme);

                        // Create deep copy for current database
                        var currentClone = JsonConvert.DeserializeObject(json, format.Scheme.GetType()) as ResourceScheme;
                        currentDatabase.SchemeMap[format.Tag] = currentClone;

                        // Create another deep copy for backup if needed
                        if (backupDatabase != null)
                        {
                            var backupClone = JsonConvert.DeserializeObject(json, format.Scheme.GetType()) as ResourceScheme;
                            backupDatabase.SchemeMap[format.Tag] = backupClone;
                        }
                    }
                    catch
                    {
                        // Fallback to manual cloning
                        try
                        {
                            var type = format.Scheme.GetType();

                            // Clone for current database
                            var currentClone = Activator.CreateInstance(type) as ResourceScheme;
                            CopySchemeData(format.Scheme, currentClone);
                            currentDatabase.SchemeMap[format.Tag] = currentClone;

                            // Clone for backup if needed
                            if (backupDatabase != null)
                            {
                                var backupClone = Activator.CreateInstance(type) as ResourceScheme;
                                CopySchemeData(format.Scheme, backupClone);
                                backupDatabase.SchemeMap[format.Tag] = backupClone;
                            }
                        }
                        catch (Exception ex)
                        {
                            // Log which scheme failed
                            System.Diagnostics.Debug.WriteLine($"Failed to clone scheme for format {format.Tag}: {ex.Message}");

                            // As last resort, use the original reference (not ideal but better than losing the scheme)
                            currentDatabase.SchemeMap[format.Tag] = format.Scheme;
                            if (backupDatabase != null)
                            {
                                backupDatabase.SchemeMap[format.Tag] = format.Scheme;
                            }
                        }
                    }
                }
            }

            var catalogType = typeof(FormatCatalog);
            var gameMapField = catalogType.GetField("m_game_map", BindingFlags.NonPublic | BindingFlags.Instance);
            if (gameMapField != null)
            {
                var gameMap = gameMapField.GetValue(FormatCatalog.Instance) as Dictionary<string, string>;
                if (gameMap != null)
                {
                    currentDatabase.GameMap = new Dictionary<string, string>(gameMap);
                    if (backupDatabase != null)
                    {
                        backupDatabase.GameMap = new Dictionary<string, string>(gameMap);
                    }
                }
            }

            // Update version
            currentDatabase.Version = FormatCatalog.Instance.CurrentSchemeVersion;
            if (backupDatabase != null)
            {
                backupDatabase.Version = FormatCatalog.Instance.CurrentSchemeVersion;
            }
        }

        private static void CopySchemeData(ResourceScheme source, ResourceScheme target)
        {
            var type = source.GetType();

            // Copy fields
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                try
                {
                    var value = field.GetValue(source);
                    field.SetValue(target, value);
                }
                catch { }
            }

            // Copy properties
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.CanRead && prop.CanWrite && prop.GetIndexParameters().Length == 0)
                {
                    try
                    {
                        var value = prop.GetValue(source);
                        prop.SetValue(target, value);
                    }
                    catch { }
                }
            }
        }

        private static ResourceScheme DeepCloneScheme(ResourceScheme source)
        {
            if (source == null) return null;
            
            try
            {
                // Use JSON serialization for deep clone
                var json = JsonConvert.SerializeObject(source);
                return JsonConvert.DeserializeObject(json, source.GetType()) as ResourceScheme;
            }
            catch
            {
                // Fallback to reflection-based cloning
                var type = source.GetType();
                var clone = Activator.CreateInstance(type) as ResourceScheme;
                
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    field.SetValue(clone, field.GetValue(source));
                }
                
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (prop.CanRead && prop.CanWrite)
                    {
                        prop.SetValue(clone, prop.GetValue(source));
                    }
                }
                
                return clone;
            }
        }

        public static void SaveScheme(MainWindow window, SchemeDataBase database, string currentPath, Action<string> updatePath)
        {
            if (string.IsNullOrEmpty(currentPath))
            {
                SaveSchemeAs(window, database, updatePath);
                return;
            }

            SaveToFile(currentPath, database, window);

            var setHasChangesMethod = window.GetType()
                .GetMethod("SetHasChanges", BindingFlags.Public | BindingFlags.Instance);
            setHasChangesMethod?.Invoke(window, new object[] { false });
        }

        public static void SaveSchemeAs(MainWindow window, SchemeDataBase database, Action<string> updatePath)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Scheme Files (*.dat)|*.dat|All Files (*.*)|*.*",
                Title = "Save Scheme File",
                InitialDirectory = Directory.GetCurrentDirectory()
            };

            if (dialog.ShowDialog() == true)
            {
                SaveToFile(dialog.FileName, database, window);
                updatePath?.Invoke(dialog.FileName);
                AddToRecentFiles(dialog.FileName, window);

                // Clear the has changes flag after successful save
                var setHasChangesMethod = window.GetType()
                    .GetMethod("SetHasChanges", BindingFlags.Public | BindingFlags.Instance);
                setHasChangesMethod?.Invoke(window, new object[] { false });
            }
        }

        private static void SaveToFile(string filePath, SchemeDataBase database, MainWindow window)
        {
            System.Diagnostics.Debug.WriteLine($"Save called from: {Environment.StackTrace}");
            try
            {
                // Apply any pending changes first
                var applyMethod = window.GetType()
                    .GetMethod("ApplyChanges", BindingFlags.Public | BindingFlags.Instance);
                applyMethod?.Invoke(window, null);

                // Update FormatCatalog with current schemes
                foreach (var kvp in database.SchemeMap)
                {
                    var format = FormatCatalog.Instance.Formats.FirstOrDefault(f => f.Tag == kvp.Key);
                    if (format != null)
                    {
                        format.Scheme = kvp.Value;
                    }
                    else
                    {
                        // If format doesn't exist in catalog but exists
                        // in our database, we need to add it somehow
                        // But for now, we'll skip it
                        System.Diagnostics.Debug.WriteLine($"Warning: Format {kvp.Key} not found in catalog");
                    }
                }

                // Update game map in FormatCatalog
                var catalogType = typeof(FormatCatalog);
                var gameMapField = catalogType.GetField("m_game_map", BindingFlags.NonPublic | BindingFlags.Instance);
                if (gameMapField != null && database.GameMap != null)
                {
                    gameMapField.SetValue(FormatCatalog.Instance, new Dictionary<string, string>(database.GameMap));
                }

                // Update version
                var versionProperty = catalogType.GetProperty("CurrentSchemeVersion", BindingFlags.Public | BindingFlags.Instance);
                if (versionProperty != null && versionProperty.CanWrite)
                {
                    versionProperty.SetValue(FormatCatalog.Instance, database.Version);
                }

                // Save using FormatCatalog's serialization
                using (var stream = File.Create(filePath))
                {
                    FormatCatalog.Instance.SerializeScheme(stream);
                }

                SetStatusText(window, $"Saved: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving scheme: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void ExportJson(MainWindow window, SchemeDataBase database)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                Title = "Export to JSON",
                InitialDirectory = Directory.GetCurrentDirectory()
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // Update FormatCatalog before exporting
                    foreach (var kvp in database.SchemeMap)
                    {
                        var format = FormatCatalog.Instance.Formats.FirstOrDefault(f => f.Tag == kvp.Key);
                        if (format != null)
                        {
                            format.Scheme = kvp.Value;
                        }
                    }

                    using (var stream = File.Create(dialog.FileName))
                    {
                        FormatCatalog.Instance.SerializeSchemeJson(stream, database);
                    }
                    AddToRecentFiles(dialog.FileName, window);

                    SetStatusText(window, $"Exported: {Path.GetFileName(dialog.FileName)}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting to JSON: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // Helper methods
        private static void SetCurrentFilePath(MainWindow window, string filePath)
        {
            var field = window.GetType().GetField("currentFilePath",
                BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(window, filePath);
        }

        private static void SetStatusText(MainWindow window, string text)
        {
            var statusText = window.FindName("StatusText") as TextBlock;
            if (statusText != null)
                statusText.Text = text;
        }
    }

    public class AppSettings
    {
        public List<string> RecentFiles { get; set; } = new List<string>();
    }
}