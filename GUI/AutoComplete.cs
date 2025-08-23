using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GARbro.GUI
{
    /// <summary>
    /// TextBox that uses filesystem as source for autocomplete.
    /// </summary>
    public class ExtAutoCompleteBox : AutoCompleteBox
    {
        public delegate void EnterKeyDownEvent (object sender, KeyEventArgs e);
        public event EnterKeyDownEvent EnterKeyDown;

        public ExtAutoCompleteBox ()
        {
            this.GotFocus += (s, e) => { IsTextBoxFocused = true; };
            this.LostFocus += (s, e) => { IsTextBoxFocused = false; };
        }

        public bool IsTextBoxFocused
        {
            get { return (bool)GetValue (HasFocusProperty); }
            private set { SetValue (HasFocusProperty, value); }
        }

        public static readonly DependencyProperty HasFocusProperty = 
            DependencyProperty.RegisterAttached ("IsTextBoxFocused", typeof(bool), typeof(ExtAutoCompleteBox), new UIPropertyMetadata());

        protected override void OnKeyDown (KeyEventArgs e)
        {
            base.OnKeyDown (e);
            if (e.Key == Key.Enter)
                RaiseEnterKeyDownEvent (e);
        }

        private void RaiseEnterKeyDownEvent (KeyEventArgs e)
        {
            if (EnterKeyDown != null)
                EnterKeyDown (this, e);
        }

        protected override void OnPopulating (PopulatingEventArgs e)
        {
            try
            {
                if (!GameRes.VFS.IsVirtual)
                {
                    var candidates = new List<string>();
                    string dirname = Path.GetDirectoryName (this.Text);
                    if (!string.IsNullOrEmpty (dirname) && Directory.Exists (dirname))
                    {
                        foreach (var dir in Directory.GetDirectories (dirname))
                        {
                            if (dir.StartsWith (dirname, StringComparison.CurrentCultureIgnoreCase))
                                candidates.Add (dir);
                        }
                    }
                    this.ItemsSource = candidates;
                }
            }
            catch
            {
                // ignore filesystem errors
            }
            base.OnPopulating (e);
        }
    }
}
