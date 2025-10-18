using System;
using System.Collections.Generic;

namespace GameRes.Collections
{
    /// <summary>
    /// Extension to the normal Dictionary. This class can store more than one value for every key.
    /// It keeps a HashSet for every Key value.  Calling Add with the same Key and multiple values
    /// will store each value under the same Key in the Dictionary. Obtaining the values for a Key
    /// will return the HashSet with the Values of the Key. 
    /// </summary>
    /// <typeparam name="TKey">The type of the key.</typeparam>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    public class MultiValueDictionary<TKey, TValue> : Dictionary<TKey, HashSet<TValue>> //, IEnumerable<KeyValuePair<TKey, TValue>>, System.Collections.IEnumerable
    {
    	/// <summary>
    	/// Initializes a new instance of the <see cref="MultiValueDictionary&lt;TKey, TValue&gt;"/> class.
    	/// </summary>
    	public MultiValueDictionary() : base()
    	{
    	}

    	/// <summary>
    	/// Adds the specified value under the specified key
    	/// </summary>
    	/// <param name="key">The key.</param>
    	/// <param name="value">The value.</param>
    	public void Add(TKey key, TValue value)
    	{
    		HashSet<TValue> container = null;
    		if(!this.TryGetValue(key, out container))
    		{
    			container = new HashSet<TValue>();
    			base.Add(key, container);
    		}
    		container.Add(value);
    	}

    	/// <summary>
    	/// Removes the specified value for the specified key. It will leave the key in the dictionary.
    	/// </summary>
    	/// <param name="key">The key.</param>
    	/// <param name="value">The value.</param>
    	public void Remove(TKey key, TValue value)
    	{
    		HashSet<TValue> container = null;
    		if(this.TryGetValue(key, out container))
    		{
    			container.Remove(value);
    			if(container.Count <= 0)
    			{
    				this.Remove(key);
    			}
    		}
    	}

    	/// <summary>
        /// Gets the values for the key specified. This method is useful if you want to avoid an
        /// exception for key value retrieval and you can't use TryGetValue (e.g. in lambdas)
    	/// </summary>
    	/// <param name="key">The key.</param>
        /// <param name="returnEmptySet">if set to true and the key isn't found, an empty hashset is
        /// returned, otherwise, if the key isn't found, null is returned</param>
    	/// <returns>
        /// This method will return null (or an empty set if returnEmptySet is true) if the key
        /// wasn't found, or the values if key was found.
    	/// </returns>
    	public HashSet<TValue> GetValues(TKey key, bool returnEmptySet)
    	{
    		HashSet<TValue> toReturn = null;
    		if (!base.TryGetValue(key, out toReturn) && returnEmptySet)
    		{
    			toReturn = new HashSet<TValue>();
    		}
    		return toReturn;
    	}
    }
}
