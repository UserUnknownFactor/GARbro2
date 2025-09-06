using System;
using System.Buffers;
using System.Threading;

namespace GameRes.Utility
{
    /// <summary>
    /// A disposable for rentals from ArrayPool that ensures return to the pool.
    /// The ArrayPool is useful when you need short-lived arrays of similar sizes, 
    /// as it reduces allocation overhead and GC pressure significantly.
    /// </summary>
    /// <typeparam name="T">The type of elements in the array</typeparam>
    internal struct ArrayPoolGuard<T> : IDisposable
    {
        private readonly ArrayPool<T> _pool;
        private readonly          T[] _array;
        private                   int _disposed;

        /// <summary>
        /// Gets the rented array.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown when accessed after disposal</exception>
        public T[] Array
        {
            get
            {
                if (_disposed != 0)
                    throw new ObjectDisposedException(nameof(ArrayPoolGuard<T>));
                return _array;
            }
        }

        /// <summary>
        /// Initializes a new instance of ArrayPoolGuard with a rented array.
        /// </summary>
        /// <param name="pool">The array pool to rent from</param>
        /// <param name="minimumLength">The minimum length of the array to rent</param>
        /// <exception cref="ArgumentNullException">Thrown when pool is null</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when minimumLength is negative</exception>
        public ArrayPoolGuard(ArrayPool<T> pool, int minimumLength)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            if (minimumLength < 0)
                throw new ArgumentOutOfRangeException(nameof(minimumLength), "Minimum length cannot be negative");
            _array = pool.Rent(minimumLength);
            if (_array == null)
                throw new InvalidOperationException("ArrayPool.Rent returned null");
            _disposed = 0;
        }

        /// <summary>
        /// Implicitly converts the guard to the underlying array.
        /// </summary>
        public static implicit operator T[](ArrayPoolGuard<T> guard) => guard.Array;

        /// <summary>
        /// Returns the array to the pool and marks this instance as disposed.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
                _pool?.Return(_array);//, clearArray: true);
        }

        #region Array Properties

        /// <summary>
        /// Gets or sets the element at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index of the element</param>
        public T this[int index]
        {
            get => Array[index];
            set => Array[index] = value;
        }

        /// <summary>
        /// Gets the length of the rented array.
        /// </summary>
        public int Length => Array.Length;

        #endregion
    }

    /// <summary>
    /// Extension methods for ArrayPool to provide safe rental operations.
    /// </summary>
    internal static class ArrayPoolExtension
    {
        /// <summary>
        /// Rents an array from the pool that will be automatically returned when disposed.
        /// Use with 'using' statement to ensure the array is returned to the pool.
        /// </summary>
        /// <param name="pool">The array pool to rent from</param>
        /// <param name="minimumLength">The minimum length of the array to rent</param>
        /// <typeparam name="T">The type of elements in the array</typeparam>
        /// <returns>An ArrayPoolGuard that will return the array to the pool when disposed</returns>
        /// <example>
        /// using (var guard = pool.RentSafe(1024))
        /// {
        ///     var array = guard.Array;
        ///     // Use array...
        /// } // Array is automatically returned here
        /// </example>
        public static ArrayPoolGuard<T> RentSafe<T>(this ArrayPool<T> pool, int minimumLength)
        {
            return new ArrayPoolGuard<T>(pool, minimumLength);
        }
    }
}

/*
  INFO: 

  Without pooling:
    // Each iteration creates garbage
    for (int i = 0; i < 1000; i++)
    {
        byte[] buffer = new byte[4096];  // new 4KB allocation
        ProcessData(buffer);
        // buffer becomes garbage
    }
    // Created 4MB of garbage!

  With pooling:       
    ArrayPool<byte> pool = ArrayPool<byte>.Create();
    // Reuses the same arrays
    for (int i = 0; i < 1000; i++)
    {
        using (var guard = pool.RentSafe(4096))
        {
            ProcessData(guard.Array);
        } // Array returned to pool even on throws
    }
    // Minimal garbage created!
*/