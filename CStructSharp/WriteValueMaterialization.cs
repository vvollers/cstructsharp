namespace CStructSharp;

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>Normalizes a caller-supplied array or character-buffer value while enforcing its declared bound.</summary>
internal static class WriteValueMaterialization
{
    /// <summary>Consumes at most one item beyond a fixed character buffer so arbitrary sequences cannot materialize unboundedly.</summary>
    public static string ConvertToBoundedCharString(object value, int maximumCount, string fieldName)
    {
        IEnumerable<char> characters = value switch
        {
            char[] chars => chars,
            IEnumerable<char> chars => chars,
            IEnumerable<byte> bytes => bytes.Select(b => (char)b),
            _ => throw new CStructWriteException("Unsupported char array source: " + value.GetType().Name),
        };

        var result = new StringBuilder();
        foreach (char character in characters)
        {
            if (result.Length >= maximumCount)
            {
                throw new CStructWriteException(
                    $"String is too long for {fieldName}: more than {maximumCount} characters.");
            }

            result.Append(character);
        }

        return result.ToString();
    }

    /// <summary>Normalizes an array value while consuming at most one item beyond its permitted count.</summary>
    public static IList<object> ConvertToObjectList(object value, int maximumCount, string fieldName)
    {
        if (value is IList<object> list)
        {
            // Keep the existing list when possible so no extra allocation is needed for the common dynamic-object path.
            EnsureMaterializedCount(list.Count, maximumCount, fieldName);
            return list;
        }

        if (value is System.Array array)
        {
            // Arrays are converted once so later code can use simple indexing for every input shape.
            EnsureMaterializedCount(array.Length, maximumCount, fieldName);
            return array.Cast<object>().ToList();
        }

        if (value is IEnumerable enumerable)
        {
            // Enumerables may be single-pass or infinite. Consume only the permitted values plus one proof of overflow.
            var result = new List<object>();
            foreach (object item in enumerable)
            {
                if (result.Count >= maximumCount)
                {
                    throw new CStructWriteException(
                        $"Array value for {fieldName} exceeds its permitted element count of {maximumCount}.");
                }

                result.Add(item);
            }

            return result;
        }

        throw new CStructWriteException("Expected an array or list for field value.");
    }

    /// <summary>Rejects already materialized collections before allocating a normalized copy.</summary>
    private static void EnsureMaterializedCount(int count, int maximumCount, string fieldName)
    {
        if (count > maximumCount)
        {
            throw new CStructWriteException(
                $"Array value for {fieldName} exceeds its permitted element count of {maximumCount}.");
        }
    }
}
