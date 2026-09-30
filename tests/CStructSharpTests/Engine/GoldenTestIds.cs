namespace CStructSharp.Tests;

using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;

/// <summary>
///     The stable ids that key a test's golden section: the test method's name, followed for a data-driven test by its
///     row's arguments in parentheses, such as <c>Truncations_ReadFromEverySource("count")</c>. The same id is
///     computed from the running test's context and from the test class by reflection, so stale sections can be found
///     without running the tests.
/// </summary>
internal static class GoldenTestIds
{
    /// <summary>Whether each test method (by class and method name) is a sweep.</summary>
    private static readonly ConcurrentDictionary<(string Class, string Method), bool> Sweeps = new();

    /// <summary>The id of the running test.</summary>
    /// <param name="context">The running test's context.</param>
    /// <returns>The id.</returns>
    public static string Of(TestContext context) => Of(context.TestName ?? string.Empty, context.TestData);

    /// <summary>The id of a test method called with one data row.</summary>
    /// <param name="method">The method's name.</param>
    /// <param name="row">The row's arguments, or <see langword="null"/> (or empty) for a test without data.</param>
    /// <returns>The id.</returns>
    public static string Of(string method, object?[]? row)
        => row is null || row.Length == 0 ? method : method + "(" + string.Join(", ", row.Select(Format)) + ")";

    /// <summary>
    ///     Whether the running test is a sweep: a row of a data source member (<see cref="DynamicDataAttribute"/>), such
    ///     as every sweep layout or every corpus case, whose golden outcomes are hashed however small they are.
    /// </summary>
    /// <param name="context">The running test's context.</param>
    /// <returns><see langword="true"/> for a row of a data source member.</returns>
    public static bool IsSweep(TestContext context)
        => Sweeps.GetOrAdd(
            (context.FullyQualifiedTestClassName ?? string.Empty, context.TestName ?? string.Empty),
            key => typeof(GoldenTestIds).Assembly.GetType(key.Class)?.GetMethod(key.Method, BindingFlags.Public | BindingFlags.Instance)?.GetCustomAttribute<DynamicDataAttribute>() is not null);

    /// <summary>
    ///     Every test id a test class declares: each test method without data, and each row of each data source
    ///     (<see cref="DataRowAttribute"/>, <see cref="DynamicDataAttribute"/>) of a data-driven one.
    /// </summary>
    /// <param name="type">The test class.</param>
    /// <returns>The ids.</returns>
    public static HashSet<string> Declared(Type type)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.GetCustomAttribute<TestMethodAttribute>() is null)
            {
                continue;
            }

            ITestDataSource[] sources = [.. method.GetCustomAttributes().OfType<ITestDataSource>()];
            if (sources.Length == 0)
            {
                ids.Add(method.Name);
                continue;
            }

            foreach (object?[] row in sources.SelectMany(source => source.GetData(method)))
            {
                ids.Add(Of(method.Name, row));
            }
        }

        return ids;
    }

    /// <summary>
    ///     Formats one argument invariantly: text quoted with its quotes, backslashes and control characters escaped,
    ///     <see langword="null"/> as <c>null</c>, arrays as their bracketed elements, anything else by its invariant text.
    /// </summary>
    /// <param name="argument">The argument.</param>
    /// <returns>The text.</returns>
    private static string Format(object? argument)
    {
        switch (argument)
        {
        case null:
            return "null";
        case string text:
            {
                var quoted = new StringBuilder("\"");
                foreach (char character in text)
                {
                    if (character is '"' or '\\')
                    {
                        quoted.Append('\\').Append(character);
                    }
                    else if (character < ' ')
                    {
                        quoted.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        quoted.Append(character);
                    }
                }

                return quoted.Append('"').ToString();
            }

        case Array array:
            return "[" + string.Join(", ", array.Cast<object?>().Select(Format)) + "]";
        default:
            return Convert.ToString(argument, CultureInfo.InvariantCulture) ?? "null";
        }
    }
}
