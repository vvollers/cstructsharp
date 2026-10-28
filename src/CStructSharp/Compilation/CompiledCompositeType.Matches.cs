namespace CStructSharp.Compilation;

using System.Threading;

/// <summary>Runtime-only lazy recognition plans; shared compiler metadata contains no runtime reader dependency.</summary>
internal sealed partial class CompiledCompositeType
{
    private PreparedMatcher? matcher;

    /// <summary>Gets a fully published immutable matcher, without allocating for layouts that never recognize input.</summary>
    internal PreparedMatcher? CachedMatcher => Volatile.Read(ref this.matcher);

    /// <summary>Publishes the first completed plan; concurrent callers always receive that identical immutable instance.</summary>
    /// <param name="prepared">Independently prepared candidate.</param>
    /// <returns>The shared published plan, without retaining execution state.</returns>
    internal PreparedMatcher PublishMatcher(PreparedMatcher prepared) => Interlocked.CompareExchange(ref this.matcher, prepared, null) ?? prepared;
}
