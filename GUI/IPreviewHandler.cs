using System;

namespace GARbro.GUI.Preview
{
    public interface IPreviewHandler : IDisposable
    {
        void LoadContent(PreviewFile preview);
        void Reset();
        bool IsActive { get; }
    }

    public abstract class PreviewHandlerBase : IPreviewHandler
    {
        protected bool _disposed = false;
        public abstract bool IsActive { get; }

        public abstract void LoadContent(PreviewFile preview);
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