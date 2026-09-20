namespace CStructSharp.Generators.Tests;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using CStructSharp.Generated;
using CStructSharp.Values;
using Pointer = CStructSharp.Values.Pointer;

/// <summary>
///     Compares a generated value (a class the generator emitted, loaded by reflection) with the runtime's parsed
///     value for the same bytes: every member the runtime produced must have the generated property with the same
///     content, member by member, element by element, pointer by pointer.
/// </summary>
internal static class ParityComparer
{
    public static void AssertSame(object? runtime, object? generated, string path)
    {
        switch (runtime)
        {
        case null:
            Assert.IsNull(generated, path + ": expected null");
            return;
        case StructValue structValue:
            AssertStruct(structValue, generated, path);
            return;
        case UnionValue union:
            AssertUnion(union, generated, path);
            return;
        case EnumValueResult enumValue:
            Assert.IsNotNull(generated, path);
            Assert.IsTrue(generated!.GetType().IsEnum, path + ": expected an enum, found " + generated.GetType());
            Assert.AreEqual(enumValue.Value, new BigInteger(Convert.ToDecimal(Convert.ChangeType(generated, Enum.GetUnderlyingType(generated.GetType()), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)), path);
            return;
        case Pointer pointer:
            AssertPointer(pointer, generated, path);
            return;
        case string text:
            Assert.AreEqual(text, generated, path);
            return;
        case IEnumerable list:
            AssertList(list, generated, path);
            return;
        default:
            AssertScalar(runtime, generated, path);
            return;
        }
    }

    private static void AssertStruct(StructValue runtime, object? generated, string path)
    {
        Assert.IsNotNull(generated, path + ": expected a struct");
        foreach (KeyValuePair<string, object?> member in runtime)
        {
            PropertyInfo? property = FindProperty(generated!.GetType(), member.Key);
            Assert.IsNotNull(property, path + "." + member.Key + ": no generated property on " + generated.GetType().Name);
            AssertSame(member.Value, property.GetValue(generated), path + "." + member.Key);
        }

        // A conditional member is present at runtime exactly when its generated presence flag is set; an absent
        // one is null (reference types) or default.
        foreach (PropertyInfo flag in generated!.GetType().GetProperties())
        {
            if (flag.PropertyType != typeof(bool) || !flag.Name.StartsWith("Has", StringComparison.Ordinal))
            {
                continue;
            }

            PropertyInfo? valueProperty = generated.GetType().GetProperty(flag.Name.Substring(3)) ?? generated.GetType().GetProperty("@" + flag.Name.Substring(3));
            if (valueProperty is null)
            {
                continue;
            }

            bool present = runtime.Any(member => FindProperty(generated.GetType(), member.Key) == valueProperty);
            Assert.AreEqual(present, (bool)flag.GetValue(generated)!, path + "." + flag.Name);
            if (!present && !valueProperty.PropertyType.IsValueType)
            {
                Assert.IsNull(valueProperty.GetValue(generated), path + "." + valueProperty.Name + ": inactive member must be null");
            }
        }
    }

    private static void AssertUnion(UnionValue runtime, object? generated, string path)
    {
        Assert.IsNotNull(generated, path + ": expected a union");
        Type type = generated!.GetType();
        byte[]? raw = runtime.RawStorage?.ToArray();
        CollectionAssert.AreEqual(raw, (byte[]?)type.GetProperty("RawStorage")!.GetValue(generated), path + ".RawStorage");
        Assert.IsNull(type.GetProperty("SelectedMember")!.GetValue(generated), path + ".SelectedMember");
        foreach (KeyValuePair<string, object?> member in runtime.Members)
        {
            PropertyInfo? property = FindProperty(type, member.Key);
            Assert.IsNotNull(property, path + "." + member.Key + ": no generated property on " + type.Name);
            AssertSame(member.Value, property.GetValue(generated), path + "." + member.Key);
        }
    }

    private static void AssertPointer(Pointer runtime, object? generated, string path)
    {
        Assert.IsNotNull(generated, path + ": expected a pointer");
        Type type = generated!.GetType();
        Assert.IsTrue(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Pointer<>), path + ": expected Pointer<T>, found " + type);
        Assert.AreEqual(runtime.Address, (long)type.GetProperty("Address")!.GetValue(generated)!, path + ".Address");
        Assert.AreEqual(runtime.Depth, (int)type.GetProperty("Depth")!.GetValue(generated)!, path + ".Depth");
        Assert.AreEqual(runtime.IsDereferenced, (bool)type.GetProperty("IsDereferenced")!.GetValue(generated)!, path + ".IsDereferenced");
        if (runtime.IsDereferenced)
        {
            AssertSame(runtime.Value, type.GetProperty("Value")!.GetValue(generated), path + ".Value");
        }
    }

    private static void AssertList(IEnumerable runtime, object? generated, string path)
    {
        Assert.IsNotNull(generated, path + ": expected an array");
        object?[] expected = runtime.Cast<object?>().ToArray();
        Assert.IsInstanceOfType<IEnumerable>(generated, path + ": expected an array, found " + generated!.GetType());
        object?[] actual = ((IEnumerable)generated).Cast<object?>().ToArray();
        Assert.AreEqual(expected.Length, actual.Length, path + ".Length");
        for (int index = 0; index < expected.Length; index++)
        {
            AssertSame(expected[index], actual[index], path + "[" + index + "]");
        }
    }

    private static void AssertScalar(object runtime, object? generated, string path)
    {
        Assert.IsNotNull(generated, path);
        if (runtime is IFormattable && generated is IFormattable && runtime.GetType() != generated.GetType())
        {
            // A bitfield reads as int/ulong at runtime but as its declared type here; compare the numbers.
            Assert.AreEqual(ToDecimal(runtime), ToDecimal(generated), path + " (" + runtime.GetType().Name + " vs " + generated.GetType().Name + ")");
            return;
        }

        Assert.AreEqual(runtime, generated, path);
    }

    private static decimal ToDecimal(object value)
    {
        return value switch
        {
            Half half => (decimal)(double)half,
            Int128 wide => (decimal)wide,
            UInt128 wide => (decimal)wide,
            BigInteger big => (decimal)big,
            _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
        };
    }

    private static PropertyInfo? FindProperty(Type type, string layoutName)
    {
        foreach (PropertyInfo property in type.GetProperties())
        {
            if (property.Name == layoutName || property.Name == Pascal(layoutName) || property.Name.TrimStart('@') == layoutName)
            {
                return property;
            }
        }

        return null;
    }

    private static string Pascal(string identifier)
    {
        var builder = new System.Text.StringBuilder();
        foreach (string part in identifier.Split('_'))
        {
            if (part.Length == 0)
            {
                continue;
            }

            bool allCaps = part.All(character => !char.IsLetter(character) || char.IsUpper(character));
            builder.Append(char.ToUpperInvariant(part[0])).Append(allCaps ? part.Substring(1).ToLowerInvariant() : part.Substring(1));
        }

        return builder.ToString();
    }
}
