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
    /// <summary>Fails the test at the first member where the generated value differs from the runtime value.</summary>
    /// <param name="runtime">
    ///     The runtime's parsed value: a struct, union, enum, pointer, string, list, or scalar.
    /// </param>
    /// <param name="generated">The generated value read by reflection.</param>
    /// <param name="path">The member path used in failure messages.</param>
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

    /// <summary>
    ///     Compares every runtime member with its generated property, and checks that each <c>Has</c> flag is set
    ///     exactly when the runtime produced the conditional member.
    /// </summary>
    /// <param name="runtime">The runtime's struct value.</param>
    /// <param name="generated">The generated value read by reflection.</param>
    /// <param name="path">The member path used in failure messages.</param>
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

    /// <summary>
    ///     Compares a union's raw storage and every decoded member; the generated selection must be unset.
    /// </summary>
    /// <param name="runtime">The runtime's union value.</param>
    /// <param name="generated">The generated value read by reflection.</param>
    /// <param name="path">The member path used in failure messages.</param>
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

    /// <summary>
    ///     Compares a pointer's address, depth, and dereference state, and its target when it was followed.
    /// </summary>
    /// <param name="runtime">The runtime's pointer.</param>
    /// <param name="generated">The generated <c>Pointer&lt;T&gt;</c>.</param>
    /// <param name="path">The member path used in failure messages.</param>
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

    /// <summary>Compares an array's length and each element in order.</summary>
    /// <param name="runtime">The runtime's elements.</param>
    /// <param name="generated">The generated array or collection.</param>
    /// <param name="path">The member path used in failure messages.</param>
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

    /// <summary>Compares two scalars, by numeric value when their types differ, as bitfields do.</summary>
    /// <param name="runtime">The runtime's scalar.</param>
    /// <param name="generated">The generated scalar.</param>
    /// <param name="path">The member path used in failure messages.</param>
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

    /// <summary>
    ///     Converts a numeric value, including <see cref="Half"/> and 128-bit and big integers, to a decimal.
    /// </summary>
    /// <param name="value">The boxed number.</param>
    /// <returns>The number as a decimal.</returns>
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

    /// <summary>Finds the generated property for a layout member by its exact, Pascal-cased, or escaped name.</summary>
    /// <param name="type">The generated class.</param>
    /// <param name="layoutName">The member name in the layout.</param>
    /// <returns>The property, or <see langword="null"/> when none matches.</returns>
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

    /// <summary>
    ///     Converts a snake_case layout name to PascalCase; an all-capitals part is lowered after its first letter.
    /// </summary>
    /// <param name="identifier">The layout name.</param>
    /// <returns>The expected generated property name.</returns>
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
