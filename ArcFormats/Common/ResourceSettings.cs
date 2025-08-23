using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Text;

namespace GameRes.Formats
{
    public class LocalResourceSetting : ResourceSettingBase
    {
        public override object Value {
            get { return Properties.Settings.Default[Name]; }
            set { Properties.Settings.Default[Name] = value; }
        }

        public LocalResourceSetting () { }

        public LocalResourceSetting (string name) : this (name, name) { }

        public LocalResourceSetting (string name, string text)
        {
            Name = name;
            Text = Localization._T (text) ?? text;
        }
    }

    public class EncodingSetting : LocalResourceSetting
    {
        private Encoding _defaultEncoding = Encoding.GetEncoding (932);

        public Encoding DefaultEncoding
        {
            get         { return _defaultEncoding;  }
            private set { _defaultEncoding = value; }
        }

        public override object Value
        {
            get
            {
                try
                {
                    var baseValue = base.Value;
                    if (baseValue == null)
                    {
                        base.Value = DefaultEncoding.CodePage;
                        return DefaultEncoding;
                    }

                    return Encoding.GetEncoding ((int)baseValue);
                }
                catch // fallback to default encoding
                {
                    Trace.WriteLine (string.Format ("Unknown encoding code page {0}, using default {1}",
                        base.Value, DefaultEncoding.CodePage));

                    // Set the base value to the default
                    base.Value = DefaultEncoding.CodePage;
                    return DefaultEncoding;
                }
            }
            set
            {
                base.Value = ((Encoding)value).CodePage;
            }
        }

        public EncodingSetting () { }

        public EncodingSetting (string name) : base (name) { }

        public EncodingSetting (string name, string text) : base (name, text) { }

        public EncodingSetting (string name, string text, Encoding defaultEnc) : base(name, text)
        {
            if (defaultEnc != null)
                DefaultEncoding = defaultEnc;

            try
            {
                var currentValue = base.Value;
                if (currentValue == null)
                    base.Value = DefaultEncoding.CodePage;
            }
            catch
            {
                base.Value = DefaultEncoding.CodePage;
            }
        }
    }

    [Export(typeof(ISettingsManager))]
    internal class SettingsManager : ISettingsManager
    {
        public void UpgradeSettings ()
        {
            if (Properties.Settings.Default.UpgradeRequired)
            {
                Properties.Settings.Default.Upgrade();
                Properties.Settings.Default.UpgradeRequired = false;
                Properties.Settings.Default.Save();
            }
        }

        public void SaveSettings ()
        {
            Properties.Settings.Default.Save();
        }
    }
}
