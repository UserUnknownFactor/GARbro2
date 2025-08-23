using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameRes;
using GameRes.Formats;

namespace SchemeEditor
{
    public partial class MainWindow : Window
    {
        private SchemeDataBase currentDatabase;
        private SchemeDataBase backupDatabase;
        private string currentFilePath;
        private KeyValuePair<string, ResourceScheme>? selectedScheme;
        private Dictionary<string, object> currentFieldValues;
        private Dictionary<FrameworkElement, object> originalValues = new Dictionary<FrameworkElement, object>();
        private bool hasUnsavedChanges = false;

        public MainWindow()
        {
            InitializeComponent();
            InitializeNewDatabase();
            FileOperations.LoadRecentFiles(this);
            UpdateSaveButtonState();
        }

        private void InitializeNewDatabase()
        {
            currentDatabase = new SchemeDataBase
            {
                Version = 1,
                SchemeMap = new Dictionary<string, ResourceScheme>(),
                GameMap = new Dictionary<string, string>()
            };

            backupDatabase = new SchemeDataBase
            {
                Version = 1,
                SchemeMap = new Dictionary<string, ResourceScheme>(),
                GameMap = new Dictionary<string, string>()
            };

            RefreshSchemeList();
        }

        private void UpdateSaveButtonState()
        {
            if (this.FindName("SaveSchemeMenuItem") is MenuItem saveMenuItem)
                saveMenuItem.IsEnabled = hasUnsavedChanges && !string.IsNullOrEmpty(currentFilePath);
        }

        public void SetHasChanges(bool hasChanges)
        {
            hasUnsavedChanges = hasChanges;
            UpdateSaveButtonState();
        }


        private void RefreshSchemeList()
        {
            SchemeListBox.ItemsSource = null;
            SchemeListBox.ItemsSource = currentDatabase.SchemeMap.ToList();
        }

        private void SchemeListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SchemeListBox.SelectedItem is KeyValuePair<string, ResourceScheme> scheme)
            {
                selectedScheme = scheme;
                LoadSchemeFields(scheme.Value);
            }
            else
            {
                selectedScheme = null;
                FieldsPanel.Children.Clear();
            }
        }

        private void LoadSchemeFields(ResourceScheme scheme)
        {
            FieldsPanel.Children.Clear();
            currentFieldValues = new Dictionary<string, object>();
            originalValues.Clear();

            var type = scheme.GetType();
            var fieldLoader = new FieldLoader(this);
            fieldLoader.LoadFields(FieldsPanel, type, scheme, currentFieldValues, originalValues);
        }

        private void AddScheme_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AddSchemeDialog();
            if (dialog.ShowDialog() == true)
            {
                var schemeName = dialog.SchemeName;
                var schemeType = dialog.SelectedSchemeType;

                if (!string.IsNullOrEmpty(schemeName) && schemeType != null)
                {
                    try
                    {
                        var newScheme = Activator.CreateInstance(schemeType) as ResourceScheme;
                        if (newScheme != null)
                        {
                            currentDatabase.SchemeMap[schemeName] = newScheme;
                            RefreshSchemeList();
                            StatusText.Text = $"Added scheme: {schemeName}";
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error creating scheme: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void RemoveScheme_Click(object sender, RoutedEventArgs e)
        {
            if (selectedScheme.HasValue)
            {
                var current_scheme = selectedScheme.Value.Key;
                var result = MessageBox.Show($"Remove scheme '{current_scheme}'?", 
                    "Confirm Remove", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    currentDatabase.SchemeMap.Remove(current_scheme);
                    RefreshSchemeList();
                    FieldsPanel.Children.Clear();
                    StatusText.Text = $"Removed scheme: {current_scheme}";
                }
            }
        }

        private void AddField_Click(object sender, RoutedEventArgs e)
        {
            if (!selectedScheme.HasValue)
            {
                MessageBox.Show("Please select a scheme first.", "No Scheme Selected",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new AddFieldDialog();
            if (dialog.ShowDialog() == true)
            {
                var fieldEditor = new FieldEditor(this);
                fieldEditor.AddFieldEditor(FieldsPanel, dialog.FieldName, dialog.FieldType, 
                    dialog.DefaultValue, currentFieldValues, originalValues);
            }
        }

        private void ApplyChanges_Click(object sender, RoutedEventArgs e)
        {
            if (!selectedScheme.HasValue)
                return;

            try
            {
                var scheme = selectedScheme.Value.Value;
                var type = scheme.GetType();

                foreach (var kvp in currentFieldValues)
                {
                    var prop = type.GetProperty(kvp.Key);
                    if (prop != null && prop.CanWrite)
                    {
                        prop.SetValue(scheme, kvp.Value);
                        continue;
                    }

                    var field = type.GetField(kvp.Key);
                    if (field != null)
                    {
                        field.SetValue(scheme, kvp.Value);
                    }
                }

                StatusText.Text = "Changes applied successfully";

                // Reset all borders to indicate changes are saved
                foreach (var child in FieldsPanel.Children)
                {
                    if (child is Border border)
                        border.BorderBrush = Brushes.LightGray;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying changes: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ApplyChanges()
        {
            if (!selectedScheme.HasValue)
                return;

            try
            {
                var scheme = selectedScheme.Value.Value;
                var type = scheme.GetType();

                foreach (var kvp in currentFieldValues)
                {
                    System.Diagnostics.Debug.WriteLine($"Applying field {kvp.Key} with value type {kvp.Value?.GetType()?.Name ?? "null"}");

                    var prop = type.GetProperty(kvp.Key);
                    if (prop != null && prop.CanWrite)
                    {
                        prop.SetValue(scheme, kvp.Value);
                        System.Diagnostics.Debug.WriteLine($"Set property {kvp.Key}");
                        continue;
                    }

                    var field = type.GetField(kvp.Key);
                    if (field != null)
                    {
                        field.SetValue(scheme, kvp.Value);
                        System.Diagnostics.Debug.WriteLine($"Set field {kvp.Key}");
                    }
                }

                StatusText.Text = "Changes applied successfully";

                currentDatabase.SchemeMap[selectedScheme.Value.Key] = scheme;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying changes: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // File menu handlers
        private void OpenScheme_Click(object sender, RoutedEventArgs e) => 
            FileOperations.OpenScheme(this, currentDatabase, RefreshSchemeList);

        private void SaveScheme_Click(object sender, RoutedEventArgs e)
        {
            FileOperations.SaveScheme(this, currentDatabase, currentFilePath, (path) =>
            {
                currentFilePath = path;
                SetHasChanges(false);
            });
        }

        private void CloseFile_Click(object sender, RoutedEventArgs e)
        {
            if (hasUnsavedChanges)
            {
                var result = MessageBox.Show("You have unsaved changes. Do you want to save before closing?",
                    "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                    SaveScheme_Click(sender, e);
                else if (result == MessageBoxResult.Cancel)
                    return;
            }

            currentFilePath = null;
            selectedScheme = null;
            currentFieldValues.Clear();
            originalValues.Clear();
            FieldsPanel.Children.Clear();
            SchemeListBox.ItemsSource = null;

            InitializeNewDatabase();

            StatusText.Text = "No file loaded";
            SetHasChanges(false);
        }

        private void SaveSchemeAs_Click(object sender, RoutedEventArgs e) => 
            FileOperations.SaveSchemeAs(this, currentDatabase, (path) => currentFilePath = path);

        private void ExportJson_Click(object sender, RoutedEventArgs e) => 
            FileOperations.ExportJson(this, currentDatabase);

        private void ImportJson_Click(object sender, RoutedEventArgs e) => 
            OpenScheme_Click(sender, e);

        private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    }
}