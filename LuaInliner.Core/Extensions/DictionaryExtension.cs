using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LuaInliner.Core.Extensions;

internal static class DictionaryExtension
{
    /// <summary>
    /// Gets a value in the dictionary with the specified key or initialize one
    /// if it doesn't exist.
    /// <br/><br/>
    /// Replicates the behaviour of <c>dict.setdefault()</c> in Python.
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    /// <typeparam name="TValue"></typeparam>
    /// <param name="dictionary"></param>
    /// <param name="key"></param>
    /// <returns></returns>
    public static TValue GetOrCreate<TKey, TValue>(
        this IDictionary<TKey, TValue> dictionary,
        TKey key
    )
        where TValue : new()
    {
        bool exists = dictionary.TryGetValue(key, out TValue? ret);

        if (!exists || ret is null)
        {
            ret = new TValue();
            dictionary[key] = ret;
        }

        return ret;
    }
}
