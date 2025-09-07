// CsvImporter.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace SchemeEditor
{
    public static class CsvImporter
    {
        public static List<string> ImportStringList(string title = "Import CSV")
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FilterIndex = 1
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var lines = File.ReadAllLines(dialog.FileName);
                    var result = new List<string>();
                    
                    foreach (var line in lines)
                    {
                        // Handle quoted values
                        var values = ParseCsvLine(line);
                        result.AddRange(values.Where(v => !string.IsNullOrWhiteSpace(v)));
                    }
                    
                    return result;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error reading CSV: {ex.Message}", "Import Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            
            return null;
        }

        public static List<int> ImportIntList(string title = "Import CSV")
        {
            var stringList = ImportStringList(title);
            if (stringList == null) return null;

            var result = new List<int>();
            foreach (var str in stringList)
            {
                if (int.TryParse(str.Trim(), out int value))
                {
                    result.Add(value);
                }
            }
            
            return result;
        }

        public static Dictionary<string, string> ImportStringDictionary(string title = "Import CSV")
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FilterIndex = 1
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var lines = File.ReadAllLines(dialog.FileName);
                    var result = new Dictionary<string, string>();
                    
                    foreach (var line in lines) 
                    {
                        var values = ParseCsvLine(line);
                        if (values.Count >= 2)
                        {
                            var key = values[0].Trim();
                            var value = values[1].Trim();
                            if (!string.IsNullOrEmpty(key))
                            {
                                result[key] = value;
                            }
                        }
                    }
                    
                    return result;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error reading CSV: {ex.Message}", "Import Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            
            return null;
        }

        public static Dictionary<TKey, TValue> ImportDictionary<TKey, TValue>(string title = "Import CSV")
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FilterIndex = 1
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var lines = File.ReadAllLines(dialog.FileName);
                    var result = new Dictionary<TKey, TValue>();
                    
                    foreach (var line in lines) 
                    {
                        var values = ParseCsvLine(line);
                        if (values.Count >= 2)
                        {
                            try
                            {
                                var key = (TKey)Convert.ChangeType(values[0].Trim(), typeof(TKey));
                                var value = (TValue)Convert.ChangeType(values[1].Trim(), typeof(TValue));
                                result[key] = value;
                            }
                            catch
                            {
                                // Skip invalid entries
                            }
                        }
                    }
                    
                    return result;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error reading CSV: {ex.Message}", "Import Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            
            return null;
        }
            
        public static Dictionary<int, int> ImportIntIntDictionary(string title = "Import CSV")
        {
            return ImportDictionary<int, int>(title);
        }
        
        public static Dictionary<uint, uint> ImportUIntUIntDictionary(string title = "Import CSV")
        {
            return ImportDictionary<uint, uint>(title);
        }
        
        public static Dictionary<string, int> ImportStringIntDictionary(string title = "Import CSV")
        {
            return ImportDictionary<string, int>(title);
        }
        
        public static Dictionary<int, string> ImportIntStringDictionary(string title = "Import CSV")
        {
            return ImportDictionary<int, string>(title);
        }
        
        public static Dictionary<uint, string> ImportUIntStringDictionary(string title = "Import CSV")
        {
            return ImportDictionary<uint, string>(title);
        }
        
        public static Dictionary<string, uint> ImportStringUIntDictionary(string title = "Import CSV")
        {
            return ImportDictionary<string, uint>(title);
        }
        
        public static Dictionary<string, long> ImportStringLongDictionary(string title = "Import CSV")
        {
            return ImportDictionary<string, long>(title);
        }
        
        public static Dictionary<string, ulong> ImportStringULongDictionary(string title = "Import CSV")
        {
            return ImportDictionary<string, ulong>(title);
        }
        
        public static List<uint> ImportUIntList(string title = "Import CSV")
        {
            var stringList = ImportStringList(title);
            if (stringList == null) return null;
        
            var result = new List<uint>();
            foreach (var str in stringList)
            {
                if (uint.TryParse(str.Trim(), out uint value))
                {
                    result.Add(value);
                }
            }
            
            return result;
        }
        
        public static List<long> ImportLongList(string title = "Import CSV")
        {
            var stringList = ImportStringList(title);
            if (stringList == null) return null;
        
            var result = new List<long>();
            foreach (var str in stringList)
            {
                if (long.TryParse(str.Trim(), out long value))
                {
                    result.Add(value);
                }
            }
            
            return result;
        }
        
        public static HashSet<uint> ImportUIntHashSet(string title = "Import CSV")
        {
            var list = ImportUIntList(title);
            return list != null ? new HashSet<uint>(list) : null;
        }

        public static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inQuotes = false;
        
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
        
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        
            result.Add(current.ToString());
            return result;
        }
    }
}