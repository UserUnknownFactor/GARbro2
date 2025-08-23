using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;

namespace GARbro.GUI.Preview
{
    public interface IModelPlugin : IDisposable
    {
        string Name { get; }
        string[] SupportedExtensions { get; }
        bool CanHandle (string filename);
        ModelContext Load (Stream stream, string filename);
        void Update (float deltaTime);
        void Render (WriteableBitmap target);
        void SetAnimation (string animationName);
        void SetParameter (string paramName, float value);

        float GetAnimationDuration (string animationName);
        string GetDebugInfo();
    }

    public class ModelContext
    {
        public string Info { get; set; }
        public List<string> Animations { get; set; } = new List<string>();
        public Dictionary<string, ModelParameter> Parameters { get; set; } = new Dictionary<string, ModelParameter>();
        public object NativeHandle { get; set; }
    }

    public class ModelParameter
    {
        public string Name { get; set; }
        public float Value { get; set; }
        public float Min { get; set; }
        public float Max { get; set; }
        public float Default { get; set; }
    }

    public class ParameterViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private ModelParameter _parameter;
        private IModelPlugin _plugin;
        private float _value;

        public ParameterViewModel (ModelParameter parameter, IModelPlugin plugin)
        {
            _parameter = parameter;
            _plugin = plugin;
            _value = parameter.Value;
        }

        public string Name => _parameter.Name;
        public float Min => _parameter.Min;
        public float Max => _parameter.Max;

        public float Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value;
                    _parameter.Value = value;
                    OnPropertyChanged();
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke (this, new System.ComponentModel.PropertyChangedEventArgs (propertyName));
        }
    }
}