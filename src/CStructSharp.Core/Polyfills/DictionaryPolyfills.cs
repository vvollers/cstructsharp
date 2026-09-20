#if NETSTANDARD2_0
namespace System.Collections.Generic;

/// <summary>Dictionary members that netstandard2.0 lacks, for the Core sources.</summary>
internal static class DictionaryPolyfills
{
    /// <summary>Adds the pair when the key is absent; false when it was present.</summary>
    public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
        where TKey : notnull
    {
        if (dictionary.ContainsKey(key))
        {
            return false;
        }

        dictionary.Add(key, value);
        return true;
    }
}
#endif
