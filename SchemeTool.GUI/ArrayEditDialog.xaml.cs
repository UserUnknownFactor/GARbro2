// ArrayEditDialog.xaml.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace SchemeEditor
{
    public partial class ArrayEditDialog : Window
    {
        public Array ResultArray { get; private set; }
        private Type elementType;
        private Type arrayType;

        public ArrayEditDialog(Type arrayType, Array initialData)
        {
            InitializeComponent();
            this.arrayType = arrayType;
            this.elementType = arrayType.GetElementType();
            ResultArray = initialData;

            TypeInfoText.Text = $"Array Type: {elementType.Name}[]";
            
            SetupInstructions();
            DisplayArray();
        }

        private void SetupInstructions()
        {
            if (elementType == typeof(uint))
            {
                InstructionText.Text = "Enter unsigned integers (one per line or comma-separated). Supports hex with 0x prefix:";
            }
            else if (elementType == typeof(int))
            {
                InstructionText.Text = "Enter integers (one per line or comma-separated):";
            }
            else if (elementType == typeof(byte))
            {
                InstructionText.Text = "Enter byte values (0-255) or hex values (one per line or comma-separated):";
            }
            else if (elementType == typeof(string))
            {
                InstructionText.Text = "Enter strings (one per line):";
            }
            else if (elementType == typeof(float) || elementType == typeof(double))
            {
                InstructionText.Text = "Enter decimal numbers (one per line or comma-separated):";
            }
            else
            {
                InstructionText.Text = $"Enter {elementType.Name} values (one per line or comma-separated):";
            }
        }

        private void DisplayArray()
        {
            if (ResultArray == null || ResultArray.Length == 0)
            {
                DataTextBox.Text = "";
                StatusText.Text = "0 items";
                return;
            }

            var lines = new List<string>();
            for (int i = 0; i < ResultArray.Length; i++)
            {
                var value = ResultArray.GetValue(i);
                if (value != null)
                {
                    if (elementType == typeof(uint) || elementType == typeof(byte) || elementType == typeof(ushort))
                        lines.Add($"{value} (0x{value:X})"); // Display value as both decimal and hex for a reference
                    else
                        lines.Add(value.ToString());
                }
            }

            DataTextBox.Text = string.Join("\n", lines);
            StatusText.Text = $"{ResultArray.Length} items";
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var text = DataTextBox.Text.Trim();
                if (string.IsNullOrEmpty(text))
                {
                    ResultArray = Array.CreateInstance(elementType, 0);
                    DialogResult = true;
                    return;
                }

                // Parse values
                var values = ParseValues(text);
                
                ResultArray = Array.CreateInstance(elementType, values.Count);
                for (int i = 0; i < values.Count; i++)
                {
                    ResultArray.SetValue(values[i], i);
                }

                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error parsing values: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<object> ParseValues(string text)
        {
            var values = new List<object>();
            var lines = text.Split(new[] { "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(new[] { ",", ";" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var trimmed = part.Trim();
                    if (string.IsNullOrEmpty(trimmed))
                        continue;

                    // Remove comments like (0xFF) for display purposes
                    if (trimmed.Contains("(") && trimmed.Contains(")"))
                    {
                        var commentStart = trimmed.IndexOf('(');
                        trimmed = trimmed.Substring(0, commentStart).Trim();
                    }

                    values.Add(ParseSingleValue(trimmed));
                }
            }

            return values;
        }

        private object ParseSingleValue(string value)
        {
            if (elementType == typeof(string))
            {
                return value;
            }
            else if (elementType == typeof(uint))
            {
                if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    return Convert.ToUInt32(value.Substring(2), 16);
                else
                    return Convert.ToUInt32(value);
            }
            else if (elementType == typeof(int))
            {
                if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    return Convert.ToInt32(value.Substring(2), 16);
                else
                    return Convert.ToInt32(value);
            }
            else if (elementType == typeof(byte))
            {
                if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    return Convert.ToByte(value.Substring(2), 16);
                else
                    return Convert.ToByte(value);
            }
            else if (elementType == typeof(long))
            {
                if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    return Convert.ToInt64(value.Substring(2), 16);
                else
                    return Convert.ToInt64(value);
            }
            else if (elementType == typeof(ulong))
            {
                if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    return Convert.ToUInt64(value.Substring(2), 16);
                else
                    return Convert.ToUInt64(value);
            }
            else if (elementType == typeof(short))
            {
                return Convert.ToInt16(value);
            }
            else if (elementType == typeof(ushort))
            {
                return Convert.ToUInt16(value);
            }
            else if (elementType == typeof(float))
            {
                return Convert.ToSingle(value);
            }
            else if (elementType == typeof(double))
            {
                return Convert.ToDouble(value);
            }
            else if (elementType == typeof(decimal))
            {
                return Convert.ToDecimal(value);
            }
            else
            {
                return Convert.ChangeType(value, elementType);
            }
        }

        private void LoadFromFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = $"Load {elementType.Name} array from file",
                Filter = "Binary files (*.bin)|*.bin|Text files (*.txt)|*.txt|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    if (Path.GetExtension(dialog.FileName).ToLower() == ".bin")
                    {
                        LoadFromBinaryFile(dialog.FileName);
                    }
                    else
                    {
                        LoadFromTextFile(dialog.FileName);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading file: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void LoadFromBinaryFile(string fileName)
        {
            var bytes = File.ReadAllBytes(fileName);
            
            if (elementType == typeof(byte))
            {
                ResultArray = bytes;
                DisplayArray();
                return;
            }

            var elementSize = System.Runtime.InteropServices.Marshal.SizeOf(elementType);
            var count = bytes.Length / elementSize;
            
            ResultArray = Array.CreateInstance(elementType, count);
            
            using (var ms = new MemoryStream(bytes))
            using (var br = new BinaryReader(ms))
            {
                for (int i = 0; i < count; i++)
                {
                    object value = null;
                    
                    if (elementType == typeof(uint))
                        value = br.ReadUInt32();
                    else if (elementType == typeof(int))
                        value = br.ReadInt32();
                    else if (elementType == typeof(ushort))
                        value = br.ReadUInt16();
                    else if (elementType == typeof(short))
                        value = br.ReadInt16();
                    else if (elementType == typeof(ulong))
                        value = br.ReadUInt64();
                    else if (elementType == typeof(long))
                        value = br.ReadInt64();
                    else if (elementType == typeof(float))
                        value = br.ReadSingle();
                    else if (elementType == typeof(double))
                        value = br.ReadDouble();
                    
                    if (value != null)
                        ResultArray.SetValue(value, i);
                }
            }
            
            DisplayArray();
        }

        private void LoadFromTextFile(string fileName)
        {
            var text = File.ReadAllText(fileName);
            DataTextBox.Text = text;
        }

        private void SaveToFile_Click(object sender, RoutedEventArgs e)
        {
            if (ResultArray == null || ResultArray.Length == 0)
            {
                MessageBox.Show("No data to save", "Warning",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = $"Save {elementType.Name} array to file",
                Filter = "Binary files (*.bin)|*.bin|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = $"{elementType.Name.ToLower()}_array"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    if (Path.GetExtension(dialog.FileName).ToLower() == ".bin")
                    {
                        SaveToBinaryFile(dialog.FileName);
                    }
                    else
                    {
                        SaveToTextFile(dialog.FileName);
                    }
                    
                    MessageBox.Show($"Saved {ResultArray.Length} items to {Path.GetFileName(dialog.FileName)}", 
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving file: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SaveToBinaryFile(string fileName)
        {
            using (var fs = new FileStream(fileName, FileMode.Create))
            using (var bw = new BinaryWriter(fs))
            {
                for (int i = 0; i < ResultArray.Length; i++)
                {
                    var value = ResultArray.GetValue(i);
                    
                    if (elementType == typeof(uint))
                        bw.Write((uint)value);
                    else if (elementType == typeof(int))
                        bw.Write((int)value);
                    else if (elementType == typeof(byte))
                        bw.Write((byte)value);
                    else if (elementType == typeof(ushort))
                        bw.Write((ushort)value);
                    else if (elementType == typeof(short))
                        bw.Write((short)value);
                    else if (elementType == typeof(ulong))
                        bw.Write((ulong)value);
                    else if (elementType == typeof(long))
                        bw.Write((long)value);
                    else if (elementType == typeof(float))
                        bw.Write((float)value);
                    else if (elementType == typeof(double))
                        bw.Write((double)value);
                }
            }
        }

        private void SaveToTextFile(string fileName)
        {
            File.WriteAllText(fileName, DataTextBox.Text);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}