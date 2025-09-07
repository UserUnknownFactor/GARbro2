// FieldEditor.TypeEditors.cs
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Reflection;

namespace SchemeEditor
{
    public partial class FieldEditor
    {
        private FrameworkElement CreateEditorForType(string fieldName, Type fieldType,
            object value, Dictionary<string, object> fieldValues,
            Dictionary<FrameworkElement, object> originalValues, int indentLevel,
            Border container)
        {
            if (value == null)
                return CreateNullEditor(fieldName, fieldType, fieldValues, originalValues, indentLevel, container);

            if (value is System.Collections.IDictionary && !(value is string))
                return CreateDictionaryEditor(fieldName, value.GetType(), value, indentLevel, container);
            else if (value is System.Collections.IList && !(value is byte[]))
                return CreateListEditor(fieldName, value.GetType(), value, indentLevel, container);
            else if (value.GetType().IsGenericType)
            {
                var genericDef = value.GetType().GetGenericTypeDefinition();
                if (genericDef == typeof(HashSet<>))
                    return CreateHashSetEditor(fieldName, value.GetType(), value, indentLevel, container);
            }

            if (fieldType.IsInterface || fieldType.IsAbstract)
            {
                return CreateInterfaceEditor(fieldName, fieldType, value, fieldValues, container, indentLevel);
            }

            if (fieldType.IsArray)
            {
                if (fieldType == typeof(byte[]))
                    return CreateByteArrayEditor(fieldName, value as byte[], fieldValues, container);
                else
                    return CreateArrayEditor(fieldName, fieldType, value, indentLevel, container);
            }

            if (fieldType == typeof(string))
            {
                return controlFactory.CreateTextBox(value?.ToString() ?? "", (text) =>
                {
                    fieldValues[fieldName] = text;
                    MarkFieldAsEdited(container);
                });
            }
            else if (fieldType == typeof(bool))
            {
                return controlFactory.CreateCheckBox((bool)(value ?? false), (isChecked) =>
                {
                    fieldValues[fieldName] = isChecked;
                    MarkFieldAsEdited(container);
                });
            }
            else if (fieldType.IsEnum)
            {
                return controlFactory.CreateEnumComboBox(fieldType, value, (selected) =>
                {
                    fieldValues[fieldName] = selected;
                    MarkFieldAsEdited(container);
                });
            }
            else if (fieldType.IsPrimitive || fieldType == typeof(decimal))
            {
                return controlFactory.CreateNumericTextBox(fieldType, value, (newValue) =>
                {
                    fieldValues[fieldName] = newValue;
                    MarkFieldAsEdited(container);
                });
            }
            else if (fieldType.IsClass && fieldType != typeof(string) && fieldType != typeof(object))
                return CreateComplexTypeEditor(fieldName, fieldType, value, indentLevel, container);

            return new TextBlock
            {
                Text = $"[Unsupported type: {fieldType.Name}] {value?.ToString() ?? "[null]"}",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Red
            };
        }

        private FrameworkElement CreateByteArrayEditor(string fieldName, byte[] value,
            Dictionary<string, object> fieldValues, Border container)
        {
            var stackPanel = new StackPanel { Orientation = Orientation.Horizontal };

            var textBox = new TextBox
            {
                Text = value != null ? $"[{value.Length} bytes]" : "[null]",
                IsReadOnly = true,
                Width = 150,
                VerticalAlignment = VerticalAlignment.Center
            };

            var loadButton = new Button
            {
                Content = "Load File",
                Margin = new Thickness(5, 0, 0, 0),
                Padding = new Thickness(10, 2, 10, 2)
            };

            loadButton.Click += (s, e) =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = $"Load binary data for {fieldName}"
                };

                if (dialog.ShowDialog() == true)
                {
                    var bytes = System.IO.File.ReadAllBytes(dialog.FileName);
                    fieldValues[fieldName] = bytes;
                    textBox.Text = $"[{bytes.Length} bytes]";
                    MarkFieldAsEdited(container);
                }
            };

            var saveButton = new Button
            {
                Content = "Save",
                Margin = new Thickness(5, 0, 0, 0),
                Padding = new Thickness(10, 2, 10, 2)
            };

            saveButton.Click += (s, e) =>
            {
                var bytes = fieldValues.ContainsKey(fieldName) ? 
                    fieldValues[fieldName] as byte[] : value;
                if (bytes != null)
                {
                    var dialog = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = $"Save binary data from {fieldName}",
                        FileName = $"{fieldName}.dat"
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        System.IO.File.WriteAllBytes(dialog.FileName, bytes);
                        MessageBox.Show($"Saved {bytes.Length} bytes to {System.IO.Path.GetFileName(dialog.FileName)}",
                            "Save Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    MessageBox.Show("No data to save", "Warning", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            stackPanel.Children.Add(textBox);
            stackPanel.Children.Add(loadButton);
            stackPanel.Children.Add(saveButton);
            return stackPanel;
        }
    }
}