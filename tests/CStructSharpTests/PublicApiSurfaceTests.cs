namespace CStructSharp.Tests;

using System.Buffers;
using System.Reflection;

/// <summary>Locks down the deliberate high-level surface selected before the release-candidate API freeze.</summary>
[TestClass]
public class PublicApiSurfaceTests
{
    private static readonly string[] AllowedExportedTypes =
    [
        "CStructSharp.CStruct",
        "CStructSharp.CStructCompilationOptions",
        "CStructSharp.CStructErrorCode",
        "CStructSharp.CStructException",
        "CStructSharp.CStructLayoutException",
        "CStructSharp.CStructPathException",
        "CStructSharp.CStructReadException",
        "CStructSharp.CStructReadLimitException",
        "CStructSharp.CStructWriteException",
        "CStructSharp.CStructWriteLimitException",
        "CStructSharp.DebugData",
        "CStructSharp.EnumValueResult",
        "CStructSharp.Pointer",
        "CStructSharp.PointerAddressingMode",
        "CStructSharp.PocoBindingMode",
        "CStructSharp.ReadOptions",
        "CStructSharp.StaticHelpers",
        "CStructSharp.UnionValue",
        "CStructSharp.UpdateOptions",
        "CStructSharp.WriteOptions",
    ];

    /// <summary>
    ///     Reflection compares exported types and method signatures against the intended public API.
    /// </summary>
    /// <remarks>
    ///     Parser nodes, internal handlers, and obsolete helpers must not become public dependencies. The test contains
    ///     no byte fixture; it protects a compact library surface that users can learn without understanding its
    ///     compiler internals.
    /// </remarks>
    [TestMethod]
    public void ExportedTypesAndSignatures_AreDeliberateAndImplementationAgnostic()
    {
        Assembly assembly = typeof(CStruct).Assembly;
        Type[] exportedTypes = assembly.GetExportedTypes();

        CollectionAssert.AreEqual(
            AllowedExportedTypes.Order(StringComparer.Ordinal).ToArray(),
            exportedTypes.Select(type => type.FullName!).Order(StringComparer.Ordinal).ToArray());
        Assert.IsTrue(typeof(CStruct).IsSealed);
        Assert.IsTrue(typeof(Pointer).IsSealed);

        foreach (Type type in exportedTypes)
        {
            IEnumerable<Type> signatureTypes = type.GetMembers(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .SelectMany(GetSignatureTypes);
            foreach (Type signatureType in signatureTypes.SelectMany(FlattenType))
            {
                Assert.IsFalse(
                    signatureType.Namespace?.StartsWith("Pidgin", StringComparison.Ordinal) == true,
                    $"{type.FullName} exposes Pidgin type {signatureType}.");
                Assert.AreNotEqual(
                    "CStructSharp.Structure",
                    signatureType.Namespace,
                    $"{type.FullName} exposes syntax-tree type {signatureType}.");
            }
        }

        string[] accidentalCStructMembers =
        [
            "CStructElements",
            "FieldAlignments",
            "FieldHandlers",
            "GetStruct",
            "PrettyPrintExpandoObject",
            "WriteHandlers",
        ];
        foreach (string memberName in accidentalCStructMembers)
        {
            Assert.IsNull(
                typeof(CStruct).GetMember(memberName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .SingleOrDefault(),
                $"CStruct.{memberName} is an implementation detail.");
        }

        Assert.IsNull(typeof(WriteOptions).GetProperty("AutoRelocatePointers"));
        CollectionAssert.AreEqual(
            new[] { "ParseHexDataContent", },
            typeof(StaticHelpers).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(method => method.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>
    ///     Every public operation accepting variables must use IReadOnlyDictionary&lt;string, int&gt;.
    /// </summary>
    /// <remarks>
    ///     Callers should supply ordinary names and numbers rather than internal expression objects. Reflection checks
    ///     all relevant overloads so a newly added API cannot accidentally expose a different, harder-to-use input
    ///     shape.
    /// </remarks>
    [TestMethod]
    public void VariableInputs_UseOneReadOnlyIntegerShape()
    {
        Type expectedVariables = typeof(IReadOnlyDictionary<string, int>);
        string[] operationNames =
        [
            nameof(CStruct.GetDynamicArrayLength),
            nameof(CStruct.Parse),
            nameof(CStruct.ParseStream),
            nameof(CStruct.ParseStreamWithDebug),
            nameof(CStruct.ReadValue),
            nameof(CStruct.ResolveAddress),
            nameof(CStruct.Serialize),
            nameof(CStruct.TryReadValue),
            nameof(CStruct.UpdateStream),
            nameof(CStruct.WriteStream),
        ];

        foreach (string operationName in operationNames)
        {
            MethodInfo[] overloads = typeof(CStruct).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => method.Name == operationName)
                .ToArray();
            Assert.IsNotEmpty(overloads, operationName);
            Assert.IsTrue(
                overloads.Any(
                    method => method.GetParameters().Any(parameter => parameter.ParameterType == expectedVariables)),
                $"{operationName} must accept IReadOnlyDictionary<string, int>.");
            Assert.IsFalse(
                overloads.SelectMany(method => method.GetParameters())
                    .Any(parameter => FlattenType(parameter.ParameterType).Any(IsSyntaxTreeType)),
                $"{operationName} must not expose expression syntax-tree types.");
        }
    }

    /// <summary>
    ///     The public API must offer synchronous reads from ReadOnlySpan and ReadOnlyMemory, plus output to Span and
    ///     IBufferWriter.
    /// </summary>
    /// <remarks>
    ///     Reflection verifies those overloads without requiring another public wrapper type. This keeps efficient
    ///     memory access available through familiar .NET abstractions.
    /// </remarks>
    [TestMethod]
    public void MemoryIo_UsesSpanMemoryAndBufferWriterWithoutNewPublicTypes()
    {
        MethodInfo[] methods = typeof(CStruct).GetMethods(BindingFlags.Public | BindingFlags.Instance);
        Type spanInput = typeof(ReadOnlySpan<byte>);
        Type memoryInput = typeof(ReadOnlyMemory<byte>);

        foreach (string operationName in new[] { nameof(CStruct.Parse), nameof(CStruct.ReadValue), })
        {
            MethodInfo[] operationMethods = methods.Where(method => method.Name == operationName).ToArray();
            Assert.IsTrue(
                operationMethods.Any(method => method.GetParameters()[0].ParameterType == spanInput),
                operationName + "/span");
            Assert.IsTrue(
                operationMethods.Any(method => method.GetParameters()[0].ParameterType == memoryInput),
                operationName + "/memory");
        }

        MethodInfo[] tryMethods = methods.Where(method => method.Name == nameof(CStruct.TryReadValue)).ToArray();
        Assert.IsTrue(tryMethods.Any(method => method.GetParameters()[0].ParameterType == spanInput));
        Assert.IsTrue(tryMethods.Any(method => method.GetParameters()[0].ParameterType == memoryInput));

        MethodInfo[] serializeMethods = methods.Where(method => method.Name == nameof(CStruct.Serialize)).ToArray();
        Assert.IsTrue(serializeMethods.Any(method => method.GetParameters()[0].ParameterType == typeof(Span<byte>)));
        Assert.IsTrue(
            serializeMethods.Any(
                method => method.GetParameters()[0].ParameterType == typeof(IBufferWriter<byte>)));
    }

    private static IEnumerable<Type> GetSignatureTypes(MemberInfo member)
    {
        return member switch
        {
            ConstructorInfo constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType),
            FieldInfo field => [field.FieldType,],
            MethodInfo method => method.GetParameters().Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType),
            PropertyInfo property => property.GetIndexParameters().Select(parameter => parameter.ParameterType)
                .Append(property.PropertyType),
            EventInfo @event when @event.EventHandlerType is not null => [@event.EventHandlerType,],
            _ => [],
        };
    }

    private static IEnumerable<Type> FlattenType(Type type)
    {
        yield return type;
        if (type.HasElementType)
        {
            foreach (Type nested in FlattenType(type.GetElementType()!))
            {
                yield return nested;
            }
        }

        foreach (Type argument in type.GetGenericArguments())
        {
            foreach (Type nested in FlattenType(argument))
            {
                yield return nested;
            }
        }
    }

    private static bool IsSyntaxTreeType(Type type)
    {
        return type.Namespace == "CStructSharp.Structure";
    }
}
