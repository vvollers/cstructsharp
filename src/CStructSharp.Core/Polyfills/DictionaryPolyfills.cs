#if NETSTANDARD2_0
namespace System.Collections.Generic;

/// <summary>Dictionary members that netstandard2.0 lacks, for the Core sources.</summary>
internal static class DictionaryPolyfills
{
    /// <summary>
    ///     Stands in for the .NET Core 2.0+ <c>Dictionary.TryAdd</c>: adds the pair when the key is absent and leaves
    ///     the dictionary unchanged when it is present.
    /// </summary>
    /// <typeparam name="TKey">The dictionary key type.</typeparam>
    /// <typeparam name="TValue">The dictionary value type.</typeparam>
    /// <param name="dictionary">The dictionary to add to; mutated only on success.</param>
    /// <param name="key">The key to insert.</param>
    /// <param name="value">The value stored under <paramref name="key"/>.</param>
    /// <returns><see langword="true"/> when the pair was added; <see langword="false"/> when the key existed.</returns>
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
