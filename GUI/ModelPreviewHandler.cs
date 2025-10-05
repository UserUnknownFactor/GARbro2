using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using GameRes;
using GARbro.GUI.Preview;

namespace GARbro.GUI.Preview
{
    public class ModelPreviewHandler : PreviewHandlerBase
    {
        private readonly MainWindow _mainWindow;
        private readonly Dictionary<string, IModelPlugin> _plugins = new Dictionary<string, IModelPlugin>();
        private IModelPlugin _activePlugin;

        public override bool IsActive => _activePlugin != null;

        public ModelPreviewHandler (MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            RegisterPlugins();
        }

        private void RegisterPlugins()
        {
            //RegisterPlugin (new Live2DPlugin());
            //RegisterPlugin (new SpinePlugin());
        }

        private void RegisterPlugin (IModelPlugin plugin)
        {
            foreach (var ext in plugin.SupportedExtensions)
                _plugins[ext.ToLower()] = plugin;
        }

        public bool IsModelFile (Entry entry)
        {
            if (entry == null || string.IsNullOrEmpty (entry.Name))
                return false;

            var filename = entry.Name.ToLower();
            var extension = VFS.GetExtension (filename);
            
            if (!string.IsNullOrEmpty (extension) && _plugins.ContainsKey (extension))
            {
                var plugin = _plugins[extension];
                if (plugin.CanHandle (filename))
                    return true;
            }

            foreach (var plugin in _plugins.Values.Distinct())
            {
                try
                {
                    if (plugin.CanHandle (filename))
                        return true;
                }
                catch
                {
                    continue;
                }
            }

            return false;
        }

        public string GetModelTypeInfo (Entry entry)
        {
            var plugin = GetPluginForFile (entry);
            return plugin?.Name ?? "Unknown";
        }

        private IModelPlugin GetPluginForFile (Entry entry)
        {
            if (entry == null || string.IsNullOrEmpty (entry.Name))
                return null;

            var filename = entry.Name.ToLower();

            foreach (var plugin in _plugins.Values.Distinct())
            {
                try
                {
                    if (plugin.CanHandle (filename))
                        return plugin;
                }
                catch
                {
                    continue;
                }
            }

            return null;
        }

        public override async Task LoadContentAsync (PreviewFile preview, CancellationToken cancellationToken)
        {
            var plugin = GetPluginForFile (preview.Entry);
            if (plugin == null)
            {
                Reset();
                return;
            }

            try
            {
                _activePlugin?.Dispose();
                _activePlugin = plugin;

                ModelContext context = null;

                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using (var stream = VFS.OpenBinaryStream (preview.Entry))
                    {
                        context = plugin.Load (stream.AsStream, preview.Entry.Name);
                    }
                }, cancellationToken);

                await _mainWindow.Dispatcher.InvokeAsync (() =>
                {
                    _mainWindow.ShowModelControls (context, plugin);
                    _mainWindow.SetPreviewStatus ($"{plugin.Name}: {context.Info}");
                });
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception ex) when (ex is ObjectDisposedException)
            {
                Reset();
            }
            catch (Exception ex)
            {
                Reset();
                _mainWindow.SetFileStatus ($"Model Error: {ex.Message}");
            }
        }

        public void TogglePlayback()
        {
            _mainWindow.ToggleModelPlayback();
        }

        public void StopPlayback()
        {
            _mainWindow.StopModelPlayback();
        }

        public override void Reset()
        {
            _activePlugin?.Dispose();
            _activePlugin = null;
            _mainWindow.Dispatcher.Invoke (() => _mainWindow.HideModelControls());
        }

        protected override void Dispose (bool disposing)
        {
            if (!_disposed && disposing)
            {
                Reset();
            }
            base.Dispose (disposing);
        }
    }
}