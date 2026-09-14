namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

/// <summary>One target member bound to one plan operation, or to the failure the general path raises for it.</summary>
internal sealed class TypedMember
{
    public TypedValueConverter.MappedMember Member { get; init; } = null!;

    public string SourceName { get; init; } = string.Empty;

    public StaticReadOperation? Operation { get; init; }

    public TypedMemberMode Mode { get; init; }

    public TypedReadPlan? Nested { get; init; }

    public Type? ElementType { get; init; }

    /// <summary>The concrete <see cref="List{T}"/> type to build when the member is a list shape; null for an array.</summary>
    public Type? ElementList { get; init; }

    public Func<string, CStructReadException>? Failure { get; init; }
}
