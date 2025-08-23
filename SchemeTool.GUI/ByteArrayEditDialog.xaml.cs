using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace SchemeEditor
{
    public partial class ByteArrayEditDialog : Window
    {
        public byte[] ByteArray { get; private set; }

        public ByteArrayEditDialog(byte[] initialData)
        {
            InitializeComponent();
            ByteArray = initialData ?? new byte[0];
            
            // Display initial data as hex with proper spacing
            if (ByteArray.Length > 0)
            {
                var hexString = BitConverter.ToString(ByteArray).Replace("-", " ");
                DataTextBox.Text = hexString;
            }
            
            UpdatePreview();
        }

        private void FormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (InstructionText == null || FormatHelpText == null)
                return;
                
            switch (FormatCombo.SelectedIndex)
            {
                case 0: // Hex
                    InstructionText.Text = "Enter hex values (e.g., FF 00 AA BB):";
                    FormatHelpText.Text = "Spaces, commas, and dashes are ignored. Odd-length values are padded with leading zero.";
                    
                    // Convert current data to hex if switching from another format
                    if (ByteArray != null && ByteArray.Length > 0)
                    {
                        DataTextBox.Text = BitConverter.ToString(ByteArray).Replace("-", " ");
                    }
                    break;
                    
                case 1: // Text
                    InstructionText.Text = "Enter text (UTF-8 encoding):";
                    FormatHelpText.Text = "Text will be encoded as UTF-8 bytes.";
                    
                    // Convert current data to text if possible
                    if (ByteArray != null && ByteArray.Length > 0)
                    {
                        try
                        {
                            DataTextBox.Text = Encoding.UTF8.GetString(ByteArray);
                        }
                        catch
                        {
                            DataTextBox.Text = "";
                        }
                    }
                    break;
                    
                case 2: // Base64
                    InstructionText.Text = "Enter Base64 encoded data:";
                    FormatHelpText.Text = "Whitespace is automatically removed.";
                    
                    // Convert current data to base64
                    if (ByteArray != null && ByteArray.Length > 0)
                    {
                        DataTextBox.Text = Convert.ToBase64String(ByteArray);
                    }
                    break;
            }
            
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            try
            {
                var text = DataTextBox.Text.Trim();
                
                if (string.IsNullOrEmpty(text))
                {
                    StatusText.Text = "0 bytes";
                    return;
                }
                    
                switch (FormatCombo.SelectedIndex)
                {
                    case 0: // Hex
                        var cleanHex = System.Text.RegularExpressions.Regex.Replace(text, @"[^0-9A-Fa-f]", "");
                        var byteCount = (cleanHex.Length + 1) / 2;
                        StatusText.Text = $"{byteCount} bytes";
                        break;
                        
                    case 1: // Text
                        var textBytes = Encoding.UTF8.GetByteCount(text);
                        StatusText.Text = $"{textBytes} bytes";
                        break;
                        
                    case 2: // Base64
                        try
                        {
                            var cleanBase64 = System.Text.RegularExpressions.Regex.Replace(text, @"\s", "");
                            var base64Bytes = Convert.FromBase64String(cleanBase64);
                            StatusText.Text = $"{base64Bytes.Length} bytes";
                        }
                        catch
                        {
                            StatusText.Text = "Invalid Base64";
                        }
                        break;
                }
            }
            catch
            {
                StatusText.Text = "Error";
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var text = DataTextBox.Text.Trim();
                
                if (string.IsNullOrEmpty(text))
                {
                    ByteArray = new byte[0];
                    DialogResult = true;
                    return;
                }
                
                switch (FormatCombo.SelectedIndex)
                {
                    case 0: // Hex
                        // Remove all non-hex characters and normalize
                        text = System.Text.RegularExpressions.Regex.Replace(text, @"[^0-9A-Fa-f]", " ");
                        
                        var hexValues = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        
                        // Handle odd-length hex strings by padding with 0
                        for (int i = 0; i < hexValues.Length; i++)
                        {
                            if (hexValues[i].Length % 2 != 0)
                                hexValues[i] = "0" + hexValues[i];
                        }
                        
                        // Parse hex pairs
                        var byteList = new List<byte>();
                        foreach (var hex in hexValues)
                        {
                            // Process each pair of hex digits
                            for (int i = 0; i < hex.Length; i += 2)
                            {
                                var hexPair = hex.Substring(i, Math.Min(2, hex.Length - i));
                                if (hexPair.Length == 2)
                                {
                                    byteList.Add(Convert.ToByte(hexPair, 16));
                                }
                            }
                        }
                        
                        ByteArray = byteList.ToArray();
                        break;
                        
                    case 1: // Text
                        ByteArray = Encoding.UTF8.GetBytes(text);
                        break;
                        
                    case 2: // Base64
                        // Remove whitespace from base64
                        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s", "");
                        ByteArray = Convert.FromBase64String(text);
                        break;
                }
                
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error parsing data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadFromFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Load byte array from file",
                Filter = "All files (*.*)|*.*|Binary files (*.bin)|*.bin|Text files (*.txt)|*.txt"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    ByteArray = File.ReadAllBytes(dialog.FileName);
                    
                    // Display in current format
                    switch (FormatCombo.SelectedIndex)
                    {
                        case 0: // Hex
                            DataTextBox.Text = BitConverter.ToString(ByteArray).Replace("-", " ");
                            break;
                            
                        case 1: // Text
                            try
                            {
                                DataTextBox.Text = Encoding.UTF8.GetString(ByteArray);
                            }
                            catch
                            {
                                // If not valid UTF-8, switch to hex
                                FormatCombo.SelectedIndex = 0;
                                DataTextBox.Text = BitConverter.ToString(ByteArray).Replace("-", " ");
                                MessageBox.Show("File contains non-text data. Switched to hex view.", 
                                    "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                            break;
                            
                        case 2: // Base64
                            DataTextBox.Text = Convert.ToBase64String(ByteArray);
                            break;
                    }
                    
                    UpdatePreview();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading file: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SaveToFile_Click(object sender, RoutedEventArgs e)
        {
            // First, parse current text to ensure ByteArray is up to date
            try
            {
                var text = DataTextBox.Text.Trim();
                
                if (string.IsNullOrEmpty(text))
                {
                    MessageBox.Show("No data to save", "Warning",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // Parse according to current format (same as OK button)
                switch (FormatCombo.SelectedIndex)
                {
                    case 0: // Hex
                        text = System.Text.RegularExpressions.Regex.Replace(text, @"[^0-9A-Fa-f]", " ");
                        var hexValues = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        var byteList = new List<byte>();
                        foreach (var hex in hexValues)
                        {
                            for (int i = 0; i < hex.Length; i += 2)
                            {
                                if (i + 1 < hex.Length)
                                {
                                    byteList.Add(Convert.ToByte(hex.Substring(i, 2), 16));
                                }
                                else
                                {
                                    byteList.Add(Convert.ToByte("0" + hex[i], 16));
                                }
                            }
                        }
                        ByteArray = byteList.ToArray();
                        break;
                        
                    case 1: // Text
                        ByteArray = Encoding.UTF8.GetBytes(text);
                        break;
                        
                    case 2: // Base64
                        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s", "");
                        ByteArray = Convert.FromBase64String(text);
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error parsing data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Save byte array to file",
                Filter = "Binary files (*.bin)|*.bin|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = "data.bin"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllBytes(dialog.FileName, ByteArray);
                    MessageBox.Show($"Saved {ByteArray.Length} bytes to {Path.GetFileName(dialog.FileName)}", 
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving file: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void DataTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePreview();
        }
    }
}