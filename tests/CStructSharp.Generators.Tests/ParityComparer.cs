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
/// <remarks>
///     <para>
///         The generated value is a typed class and the runtime value a <see cref="StructValue"/>, so their CLR shapes
///         always differ: structs, unions, enums and pointers are compared by content in both modes. The lenient mode
///         (the default) compares numbers by value. The strict mode also requires what the two shapes can share:
///         generated properties declared in the runtime's member order, scalars of the same CLR type, and the array kind
///         the generated element type implies (<see cref="PrimitiveArray{T}"/> for a one-dimensional scalar <c>T[]</c>;
///         <c>List&lt;object?&gt;</c> for the rows of a multi-dimensional array and for arrays of composites, texts,
///         enums or pointers).
///     </para>
///     <para>
///         One runtime/generator difference stays lenient in the strict mode. A bitfield: the runtime reads one
///         narrower than 32 bits as <see cref="int"/> and a wider one as <see cref="ulong"/>, whatever its declared
///         type, while the generated property has the declared type; an <see cref="int"/> or <see cref="ulong"/>
///         runtime integer against another generated integer type is therefore compared by value.
///     </para>
/// </remarks>
internal static class ParityComparer
{
    /// <summary>Where an array appears in a value, which decides the array kind the runtime returns for it.</summary>
    private enum ArrayPosition
    {
        /// <summary>A struct member, an element of an array of composites, or a pointer target.</summary>
        Member,

        /// <summary>A row of a multi-dimensional array, which the runtime always returns as <c>List&lt;object?&gt;</c>.</summary>
        Row,
    }

    /// <summary>Fails the test at the first member where the generated value differs from the runtime value.</summary>
    /// <param name="runtime">
    ///     The runtime's parsed value: a struct, union, enum, pointer, string, list, or scalar.
    /// </param>
    /// <param name="generated">The generated value read by reflection.</param>
    /// <param name="path">The member path used in failure messages.</param>
    /// <param name="strict">
    ///     Whether member order, scalar CLR types and array kinds must also agree (see the remarks on
    ///     <see cref="ParityComparer"/>); the default compares leniently.
    /// </param>
    public static void AssertSame(object? runtime, object? generated, string path, bool strict = false)
    {
        switch (runtime)
        {
        case null:
            Assert.IsNull(generated, path + ": expected null");
            return;
        case StructValue structValue:
            AssertStruct(structValue, generated, path, strict);
            return;
        case UnionValue union:
            AssertUnion(union, generated, path, strict);
            return;
        case EnumValueResult enumValue:
            Assert.IsNotNull(generated, path);
            Assert.IsTrue(generated!.GetType().IsEnum, path + ": expected an enum, found " + generated.GetType());
            Assert.AreEqual(enumValue.Value, new BigInteger(Convert.ToDecimal(Convert.ChangeType(generated, Enum.GetUnderlyingType(generated.GetType()), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)), path);
            return;
        case Pointer pointer:
            AssertPointer(pointer, generated, path, strict);
            return;
        case string text:
            Assert.AreEqual(text, generated, path);
            return;
        case IEnumerable list:
            AssertList(list, generated, path, strict);
            return;
        default:
            AssertScalar(runtime, generated, path, strict);
            return;
        }
    }

    /// <summary>Finds the generated property for a layout member by its exact, Pascal-cased, or escaped name.</summary>
    /// <param name="type">The generated class.</param>
    /// <param name="layoutName">The member name in the layout.</param>
    /// <returns>The property, or <see langword="null"/> when none matches.</returns>
    internal static PropertyInfo? FindProperty(Type type, string layoutName)
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
    ///     Compares every runtime member with its generated property, and checks that each <c>Has</c> flag is set
    ///     exactly when the runtime produced the conditional member.
    /// </summary>
    /// <param name="runtime">The runtime's struct value.</param>
    /// <param name="generated">The generated value read by reflection.</param>
    /// <param name="path">The member path used in failure messages.</param>
    /// <param name="strict">Whether the members are compared strictly and must follow the runtime's order.</param>
    private static void AssertStruct(StructValue runtime, object? generated, string path, bool strict)
    {
        Assert.IsNotNull(generated, path + ": expected a struct");
        var properties = new List<PropertyInfo>();
        foreach (KeyValuePair<string, object?> member in runtime)
        {
            PropertyInfo? property = FindProperty(generated!.GetType(), member.Key);
            Assert.IsNotNull(property, path + "." + member.Key + ": no generated property on " + generated.GetType().Name);
            AssertSame(member.Value, property.GetValue(generated), path + "." + member.Key, strict);
            properties.Add(property);
        }

        if (strict)
        {
            AssertDeclarationOrder(properties, path);
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
    /// <param name="strict">Whether the members are compared strictly and must follow the runtime's order.</param>
    private static void AssertUnion(UnionValue runtime, object? generated, string path, bool strict)
    {
        Assert.IsNotNull(generated, path + ": expected a union");
        Type type = generated!.GetType();
        byte[]? raw = runtime.RawStorage?.ToArray();
        CollectionAssert.AreEqual(raw, (byte[]?)type.GetProperty("RawStorage")!.GetValue(generated), path + ".RawStorage");
        Assert.IsNull(type.GetProperty("SelectedMember")!.GetValue(generated), path + ".SelectedMember");
        var properties = new List<PropertyInfo>();
        foreach (KeyValuePair<string, object?> member in runtime.Members)
        {
            PropertyInfo? property = FindProperty(type, member.Key);
            Assert.IsNotNull(property, path + "." + member.Key + ": no generated property on " + type.Name);
            object? value = property.GetValue(generated);
            AssertSame(member.Value, value, path + "." + member.Key, strict);

            properties.Add(property);
        }

        if (strict)
        {
            AssertDeclarationOrder(properties, path);
        }
    }

    /// <summary>
    ///     Asserts that the generated properties, listed in the runtime's member order, are declared in that order too.
    /// </summary>
    /// <param name="properties">The generated property of each runtime member, in the runtime's order.</param>
    /// <param name="path">The member path used in failure messages.</param>
    private static void AssertDeclarationOrder(List<PropertyInfo> properties, string path)
    {
        // The metadata tokens of one type's properties grow in declaration order.
        for (int index = 1; index < properties.Count; index++)
        {
            Assert.IsGreaterThan(
                properties[index - 1].MetadataToken,
                properties[index].MetadataToken,
                path + ": member order - generated " + properties[index].Name + " is declared before " + properties[index - 1].Name);
        }
    }

    /// <summary>
    ///     Compares a pointer's address, depth, and dereference state, and its target when it was followed.
    /// </summary>
    /// <param name="runtime">The runtime's pointer.</param>
    /// <param name="generated">The generated <c>Pointer&lt;T&gt;</c>.</param>
    /// <param name="path">The member path used in failure messages.</param>
    /// <param name="strict">Whether the target is compared strictly.</param>
    private static void AssertPointer(Pointer runtime, object? generated, string path, bool strict)
    {
        Assert.IsNotNull(generated, path + ": expected a pointer");
        Type type = generated!.GetType();
        Assert.IsTrue(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Pointer<>), path + ": expected Pointer<T>, found " + type);
        Assert.AreEqual(runtime.Address, (long)type.GetProperty("Address")!.GetValue(generated)!, path + ".Address");
        Assert.AreEqual(runtime.Depth, (int)type.GetProperty("Depth")!.GetValue(generated)!, path + ".Depth");
        Assert.AreEqual(runtime.IsDereferenced, (bool)type.GetProperty("IsDereferenced")!.GetValue(generated)!, path + ".IsDereferenced");
        if (runtime.IsDereferenced)
        {
            AssertSame(runtime.Value, type.GetProperty("Value")!.GetValue(generated), path + ".Value", strict);
        }
    }

    /// <summary>Compares an array's length and each element in order.</summary>
    /// <param name="runtime">The runtime's elements.</param>
    /// <param name="generated">The generated array or collection.</param>
    /// <param name="path">The member path used in failure messages.</param>
    /// <param name="strict">Whether the array kind is checked and the elements are compared strictly.</param>
    /// <param name="position">Where the array appears, which decides the array kind the runtime returns.</param>
    private static void AssertList(IEnumerable runtime, object? generated, string path, bool strict, ArrayPosition position = ArrayPosition.Member)
    {
        Assert.IsNotNull(generated, path + ": expected an array");
        object?[] expected = runtime.Cast<object?>().ToArray();
        Assert.IsInstanceOfType<IEnumerable>(generated, path + ": expected an array, found " + generated!.GetType());
        if (strict)
        {
            AssertArrayKind(runtime, generated, path, position == ArrayPosition.Row);
        }

        object?[] actual = ((IEnumerable)generated).Cast<object?>().ToArray();
        Assert.AreEqual(expected.Length, actual.Length, path + ".Length");
        for (int index = 0; index < expected.Length; index++)
        {
            string item = path + "[" + index + "]";
            if (expected[index] is IList nested && actual[index] is Array)
            {
                // An array element that is itself an array is a row of a multi-dimensional array.
                AssertList(nested, actual[index], item, strict, ArrayPosition.Row);
            }
            else
            {
                AssertSame(expected[index], actual[index], item, strict);
            }
        }
    }

    /// <summary>
    ///     Asserts that the runtime chose the array kind the generated element type implies:
    ///     <see cref="PrimitiveArray{T}"/> for a one-dimensional scalar <c>T[]</c>, and <c>List&lt;object?&gt;</c> for a
    ///     row of a multi-dimensional array and for arrays of composites, texts, enums or pointers.
    /// </summary>
    /// <param name="runtime">The runtime's array.</param>
    /// <param name="generated">The generated array or collection.</param>
    /// <param name="path">The member path used in failure messages.</param>
    /// <param name="row">Whether the array is a row of a multi-dimensional array.</param>
    private static void AssertArrayKind(IEnumerable runtime, object generated, string path, bool row)
    {
        Type runtimeType = runtime.GetType();
        Type? element = generated.GetType().IsArray ? generated.GetType().GetElementType() : null;
        string message = path + ": array kind - runtime " + TypeName(runtimeType) + ", generated " + TypeName(generated.GetType());
        if (!row && element is not null && IsScalar(element))
        {
            Type expected = typeof(PrimitiveArray<>).MakeGenericType(element);
            Assert.AreEqual(expected, runtimeType, message);
            return;
        }

        Assert.AreEqual(typeof(List<object?>), runtimeType, message);
    }

    /// <summary>Whether a generated element type is a scalar the runtime can store unboxed.</summary>
    /// <param name="type">The element type.</param>
    /// <returns><see langword="true"/> for a primitive, <see cref="Half"/>, 128-bit integer, decimal, or GUID.</returns>
    private static bool IsScalar(Type type)
        => type.IsPrimitive || type == typeof(Half) || type == typeof(Int128) || type == typeof(UInt128) || type == typeof(decimal) || type == typeof(Guid);

    /// <summary>A readable, generic-aware CLR type name, such as <c>PrimitiveArray&lt;UInt16&gt;</c>.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The name.</returns>
    private static string TypeName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        string name = type.Name;
        int tick = name.IndexOf('`');
        return (tick < 0 ? name : name.Substring(0, tick)) + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">";
    }

    /// <summary>Compares two scalars; in the lenient mode by numeric value when their types differ, as bitfields do.</summary>
    /// <param name="runtime">The runtime's scalar.</param>
    /// <param name="generated">The generated scalar.</param>
    /// <param name="path">The member path used in failure messages.</param>
    /// <param name="strict">Whether the two scalars must also have the same CLR type (bitfields excepted).</param>
    private static void AssertScalar(object runtime, object? generated, string path, bool strict)
    {
        Assert.IsNotNull(generated, path);
        if (strict && !IsBitfieldValue(runtime, generated))
        {
            Assert.AreEqual(runtime.GetType(), generated.GetType(), path + ": scalar CLR type");
        }

        if (runtime is IFormattable && generated is IFormattable && runtime.GetType() != generated.GetType())
        {
            // A bitfield reads as int/ulong at runtime but as its declared type here; compare the numbers.
            Assert.AreEqual(ToDecimal(runtime), ToDecimal(generated), path + " (" + runtime.GetType().Name + " vs " + generated.GetType().Name + ")");
            return;
        }

        Assert.AreEqual(runtime, generated, path);
    }

    /// <summary>
    ///     Whether two differently typed integers have the shape of a bitfield: the runtime reads every bitfield as
    ///     <see cref="int"/> (narrower than 32 bits) or <see cref="ulong"/>, the generated class as its declared type.
    /// </summary>
    /// <param name="runtime">The runtime's scalar.</param>
    /// <param name="generated">The generated scalar.</param>
    /// <returns><see langword="true"/> for an <see cref="int"/> or <see cref="ulong"/> runtime value against another integer type.</returns>
    private static bool IsBitfieldValue(object runtime, object generated)
        => runtime is int or ulong && runtime.GetType() != generated.GetType() &&
           generated is byte or sbyte or short or ushort or int or uint or long or ulong;

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
