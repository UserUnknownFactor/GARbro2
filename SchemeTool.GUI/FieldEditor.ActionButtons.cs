// FieldEditor.ActionButtons.cs
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Reflection;
using GameRes;

namespace SchemeEditor
{
    public partial class FieldEditor
    {
        private StackPanel CreateActionButtons(string fieldName, Type fieldType,
            Dictionary<string, object> fieldValues,
            Dictionary<FrameworkElement, object> originalValues,
            Border fieldContainer, StackPanel parentContainer)
        {
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(5, 0, 0, 0)
            };

            // Confirm button
            var confirmButton = new Button
            {
                Content = "✓",
                Width = 25,
                Height = 25,
                ToolTip = "Confirm changes and save",
                Margin = new Thickness(0, 0, 0, 2)
            };
            confirmButton.Click += (s, e) => HandleConfirmClick(fieldName, fieldValues, fieldContainer);

            // Reset button
            var resetButton = new Button
            {
                Content = "↺",
                Width = 25,
                Height = 25,
                ToolTip = "Reset to original value from backup",
                Margin = new Thickness(0, 0, 0, 2)
            };
            resetButton.Click += (s, e) => HandleResetClick(fieldName, fieldType, fieldValues, 
                originalValues, fieldContainer, parentContainer);

            // Remove button
            var removeButton = new Button
            {
                Content = "X",
                Width = 25,
                Height = 25,
                ToolTip = "Remove this field",
                Margin = new Thickness(0, 0, 0, 0)
            };
            removeButton.Click += (s, e) => HandleRemoveClick(fieldName, fieldValues, 
                originalValues, fieldContainer, parentContainer);

            buttonPanel.Children.Add(confirmButton);
            buttonPanel.Children.Add(resetButton);
            buttonPanel.Children.Add(removeButton);

            return buttonPanel;
        }

        private void HandleConfirmClick(string fieldName, Dictionary<string, object> fieldValues, 
            Border fieldContainer)
        {
            MarkFieldAsConfirmed(fieldContainer);

            if (fieldValues.ContainsKey(fieldName))
            {
                var selectedSchemeField = mainWindow.GetType()
                    .GetField("selectedScheme", BindingFlags.NonPublic | BindingFlags.Instance);
                if (selectedSchemeField != null)
                {
                    var selectedScheme = selectedSchemeField.GetValue(mainWindow);
                    if (selectedScheme != null)
                    {
                        var currentFilePathField = mainWindow.GetType()
                            .GetField("currentFilePath", BindingFlags.NonPublic | BindingFlags.Instance);
                        var currentFilePath = currentFilePathField?.GetValue(mainWindow) as string;

                        var currentDatabaseField = mainWindow.GetType()
                            .GetField("currentDatabase", BindingFlags.NonPublic | BindingFlags.Instance);
                        var currentDatabase = currentDatabaseField?.GetValue(mainWindow) as SchemeDataBase;

                        if (!string.IsNullOrEmpty(currentFilePath) && currentDatabase != null)
                        {
                            var applyMethod = mainWindow.GetType()
                                .GetMethod("ApplyChanges", BindingFlags.Public | BindingFlags.Instance);
                            applyMethod?.Invoke(mainWindow, null);

                            var kvp = (KeyValuePair<string, ResourceScheme>)selectedScheme;
                            currentDatabase.SchemeMap[kvp.Key] = kvp.Value;

                            FileOperations.SaveScheme(mainWindow, currentDatabase, currentFilePath,
                                (path) => currentFilePathField.SetValue(mainWindow, path));

                            var statusText = mainWindow.FindName("StatusText") as TextBlock;
                            if (statusText != null)
                            {
                                statusText.Text = $"Saved changes to {fieldName}";
                            }

                            var setHasChangesMethod = mainWindow.GetType()
                                .GetMethod("SetHasChanges", BindingFlags.Public | BindingFlags.Instance);
                            setHasChangesMethod?.Invoke(mainWindow, new object[] { false });
                        }
                        else
                        {
                            var result = MessageBox.Show("No file is currently loaded. Would you like to save as a new file?",
                                "Save As", MessageBoxButton.YesNo, MessageBoxImage.Question);

                            if (result == MessageBoxResult.Yes)
                            {
                                var applyMethod = mainWindow.GetType()
                                    .GetMethod("ApplyChanges", BindingFlags.Public | BindingFlags.Instance);
                                applyMethod?.Invoke(mainWindow, null);

                                FileOperations.SaveSchemeAs(mainWindow, currentDatabase,
                                    (path) => {
                                        currentFilePathField?.SetValue(mainWindow, path);
                                        var setHasChangesMethod = mainWindow.GetType()
                                            .GetMethod("SetHasChanges", BindingFlags.Public | BindingFlags.Instance);
                                        setHasChangesMethod?.Invoke(mainWindow, new object[] { false });
                                    });
                            }
                        }
                    }
                }
            }
        }

        private void HandleResetClick(string fieldName, Type fieldType,
            Dictionary<string, object> fieldValues,
            Dictionary<FrameworkElement, object> originalValues,
            Border fieldContainer, StackPanel parentContainer)
        {
            // Reset implementation...
            // (Copy the reset button click handler code here)
        }

        private void HandleRemoveClick(string fieldName,
            Dictionary<string, object> fieldValues,
            Dictionary<FrameworkElement, object> originalValues,
            Border fieldContainer, StackPanel parentContainer)
        {
            var result = MessageBox.Show($"Are you sure you want to remove the field '{fieldName}'?", 
                "Confirm Remove", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                parentContainer.Children.Remove(fieldContainer);
                fieldValues.Remove(fieldName);
                originalValues.Remove(fieldContainer);

                var parentBorder = parentContainer.Parent as Border;
                if (parentBorder != null)
                    MarkFieldAsEdited(parentBorder);

                var setHasChangesMethod = mainWindow.GetType()
                    .GetMethod("SetHasChanges", BindingFlags.Public | BindingFlags.Instance);
                setHasChangesMethod?.Invoke(mainWindow, new object[] { true });

                var statusText = mainWindow.FindName("StatusText") as TextBlock;
                if (statusText != null)
                    statusText.Text = $"Removed field: {fieldName}";
            }
        }
    }
}