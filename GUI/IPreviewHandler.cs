using System;
using System.Threading;
using System.Threading.Tasks;

namespace GARbro.GUI.Preview
{
    public interface ILoadResult
    {
        bool IsCancelled { get; set; }
        string Error { get; set; }
        bool HasError { get; }
    }

    public abstract class LoadResultBase : ILoadResult
    {
        public bool IsCancelled { get; set; }
        public string Error { get; set; }
        public bool HasError => !string.IsNullOrEmpty(Error);
        
        public static T CreateError<T>(string error) where T : LoadResultBase, new()
        {
            return new T { Error = error };
        }
        
        public static T CreateCancelled<T>() where T : LoadResultBase, new()
        {
            return new T { IsCancelled = true };
        }
    }

    public interface IPreviewHandler : IDisposable
    {
        Task LoadContentAsync(PreviewFile preview, CancellationToken cancellationToken);
        void Reset();
        bool IsActive { get; }
    }

    public abstract class PreviewHandlerBase : IPreviewHandler
    {
        protected bool _disposed = false;
        public abstract bool IsActive { get; }

        public abstract Task LoadContentAsync(PreviewFile preview, CancellationToken cancellationToken);
        public abstract void Reset();

        #region IDisposable members
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                    Reset();

                _disposed = true;
            }
        }
        #endregion
    }
}