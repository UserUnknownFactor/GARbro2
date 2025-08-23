using System;

namespace GameRes.Formats.GUI
{
    public class ExtensionChangedEventArgs : EventArgs
    {
        public string NewExtension { get; }

        public ExtensionChangedEventArgs(string extension)
        {
            NewExtension = extension;
        }
    }

    /// <summary>
    /// Represents a widget that can notify its host about a suggested file extension change.
    /// </summary>
    public interface IExtensionChangeNotifier
    {
        /// <summary>
        /// Fired when the suggested file extension should change.
        /// </summary>
        event EventHandler<ExtensionChangedEventArgs> ExtensionChanged;

        /// <summary>
        /// Gets the currently suggested file extension from the widget.
        /// </summary>
        string CurrentExtension { get; }
    }
}