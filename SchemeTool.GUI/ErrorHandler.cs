using System;
using System.Windows;

namespace SchemeEditor
{
    public static class ErrorHandler
    {
        public static T SafeExecute<T>(Func<T> action, T defaultValue = default(T), string errorContext = null)
        {
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(errorContext))
                {
                    System.Diagnostics.Debug.WriteLine($"Error in {errorContext}: {ex.Message}");
                }
                return defaultValue;
            }
        }

        public static void SafeExecute(Action action, string errorContext = null, bool showMessage = false)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(errorContext))
                {
                    System.Diagnostics.Debug.WriteLine($"Error in {errorContext}: {ex.Message}");
                }
                
                if (showMessage)
                {
                    MessageBox.Show($"An error occurred: {ex.Message}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        public static bool TryGetValue<T>(object source, string propertyName, out T value)
        {
            value = default(T);
            
            if (source == null)
                return false;

            try
            {
                var type = source.GetType();
                var property = type.GetProperty(propertyName);
                if (property != null && property.CanRead)
                {
                    var rawValue = property.GetValue(source);
                    if (rawValue is T typedValue)
                    {
                        value = typedValue;
                        return true;
                    }
                }

                var field = type.GetField(propertyName);
                if (field != null)
                {
                    var rawValue = field.GetValue(source);
                    if (rawValue is T typedValue)
                    {
                        value = typedValue;
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }
    }
}