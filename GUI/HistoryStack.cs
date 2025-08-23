using System;
using System.Collections.Generic;
using System.Linq;

namespace GARbro.GUI.History
{
    /// <summary>
    /// Graph-based navigation history
    /// </summary>
    public class NavigationHistory<TState> where TState : class
    {
        private readonly List<TState>              m_history = new List<TState>();
        private int                                m_currentIndex = -1;
        private readonly IEqualityComparer<TState> m_comparer;
        private bool                               m_hasInitialState = false;

        public int                 CurrentIndex => m_currentIndex;

        public int Limit { get; set; }

        public NavigationHistory (int limit = 50, IEqualityComparer<TState> comparer = null)
        {
            Limit      = limit;
            m_comparer = comparer ?? EqualityComparer<TState>.Default;
        }

        public void NavigateTo (TState state)
        {
            if (state == null)
                return;

            // Special handling for first (initial) state
            if (!m_hasInitialState && m_currentIndex == -1)
            {
                m_history.Add (state);
                m_currentIndex = 0;
                m_hasInitialState = true;
                OnStateChanged();
                return;
            }

            if (m_currentIndex >= 0 && m_comparer.Equals(m_history[m_currentIndex], state))
            {
                m_history[m_currentIndex] = state;
                OnStateChanged();
                return;
            }

            // Check if this state already exists in future history
            int existingIndex = -1;
            for (int i = m_currentIndex + 1; i < m_history.Count; i++)
            {
                if (m_comparer.Equals (m_history[i], state))
                {
                    existingIndex = i;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                // State exists in future - just move to it
                m_currentIndex = existingIndex;
            }
            else
            {
                // New state - add it after current position
                // Remove any forward history
                if (m_currentIndex < m_history.Count - 1)
                    m_history.RemoveRange (m_currentIndex + 1, m_history.Count - m_currentIndex - 1);

                m_history.Add (state);
                m_currentIndex = m_history.Count - 1;

                if (m_history.Count > Limit)
                {
                    int removeCount = m_history.Count - Limit;
                    m_history.RemoveRange (0, removeCount);
                    m_currentIndex -= removeCount;
                }
            }

            OnStateChanged();
        }

        /// <summary>
        /// Search for a state in history without changing current position
        /// </summary>
        public int FindState(TState state)
        {
            if (state == null)
                return -1;

            for (int i = 0; i < m_history.Count; i++)
            {
                if (m_comparer.Equals(m_history[i], state))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Navigate to a specific index in history
        /// </summary>
        public TState NavigateToIndex(int index)
        {
            if (index < 0 || index >= m_history.Count)
                return null;

            m_currentIndex = index;
            OnStateChanged();
            return m_history[index];
        }

        public TState GoBack ()
        {
            if (CanGoBack())
            {
                m_currentIndex--;
                OnStateChanged();
                return m_history[m_currentIndex];
            }
            return null;
        }

        public TState GoForward ()
        {
            if (CanGoForward())
            {
                m_currentIndex++;
                OnStateChanged();
                return m_history[m_currentIndex];
            }
            return null;
        }

        public bool CanGoBack()    => m_currentIndex > 0;
        public bool CanGoForward() => m_currentIndex < m_history.Count - 1;

        public TState Current => m_currentIndex >= 0 ? m_history[m_currentIndex] : null;

        public event EventHandler StateChanged;

        private void OnStateChanged ()
        {
            StateChanged?.Invoke (this, EventArgs.Empty);
        }
    }
}
