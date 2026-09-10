namespace CStructSharp;

using System;
using System.Globalization;
using System.Numerics;
using CstructEnum = CStructSharp.Structure.Enum;

/// <summary>Converts a caller-supplied write value into the exact integer domain of one compiled enum.</summary>
internal static class EnumFieldValueParser
{
    /// <summary>Accepts one exact enum input shape and validates all supplied metadata against the compiled declaration.</summary>
    public static BigInteger GetEnumValue(
        CompiledEnumType compiled,
        CstructEnum enm,
        object value,
        PocoBindingMode bindingMode)
    {
        try
        {
            BigInteger result;
            if (value is EnumValueResult parsed)
            {
                ValidateEnumName(enm, parsed.Enum);
                ValidateEnumDomainMetadata(compiled, parsed);
                result = parsed.Value;
                ValidateEnumMemberMetadata(compiled, parsed.Name, result);
            }
            else if (value is string text)
            {
                if (compiled.MembersByName.TryGetValue(text, out CompiledEnumMember member))
                {
                    result = compiled.Integer.FromRawBits(member.RawBits);
                }
                else if (!BigInteger.TryParse(
                             text,
                             NumberStyles.Integer,
                             CultureInfo.InvariantCulture,
                             out result))
                {
                    throw new FormatException(
                        $"'{text}' is neither a member of enum '{enm.Name.Name}' nor an invariant decimal integer.");
                }
            }
            else if (EnumIntegerCodec.TryConvertIntegral(value, out result))
            {
                // The direct integral shape is already exact.
            }
            else
            {
                result = GetEnumObjectValue(compiled, enm, value, bindingMode);
            }

            compiled.Integer.EnsureInRange(result);
            return result;
        }
        catch (CStructWriteException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or ArithmeticException or
                                          FormatException or InvalidCastException or InvalidOperationException)
        {
            throw new CStructWriteException(
                $"Cannot convert the supplied value for enum '{enm.Name.Name}'.",
                exception);
        }
    }

    /// <summary>Reads the browser/POCO enum object shape and rejects absent or contradictory metadata.</summary>
    private static BigInteger GetEnumObjectValue(
        CompiledEnumType compiled,
        CstructEnum enm,
        object value,
        PocoBindingMode bindingMode)
    {
        bool hasEnum = PocoDataBinding.TryGetMemberValue(value, "Enum", bindingMode, out object enumName);
        if (hasEnum && enumName is not null)
        {
            ValidateEnumName(enm, enumName.ToString());
        }

        bool hasName = PocoDataBinding.TryGetMemberValue(value, "Name", bindingMode, out object memberName);
        BigInteger? namedValue = null;
        string? selectedName = memberName?.ToString();
        if (hasName && selectedName is not null)
        {
            if (!compiled.MembersByName.TryGetValue(selectedName, out CompiledEnumMember member))
            {
                throw new InvalidOperationException(
                    $"Enum '{enm.Name.Name}' has no member named '{selectedName}'.");
            }

            namedValue = compiled.Integer.FromRawBits(member.RawBits);
        }

        bool hasValue = PocoDataBinding.TryGetMemberValue(value, "Value", bindingMode, out object rawValue);
        BigInteger? numericValue = null;
        if (hasValue && rawValue is not null)
        {
            numericValue = ConvertEnumNumericInput(rawValue);
        }

        if (namedValue is null && numericValue is null)
        {
            throw new InvalidOperationException(
                $"Enum '{enm.Name.Name}' input must supply Name or Value.");
        }

        if (namedValue is not null && numericValue is not null && namedValue.Value != numericValue.Value)
        {
            throw new InvalidOperationException(
                $"Enum '{enm.Name.Name}' Name and Value identify different members.");
        }

        return numericValue ?? namedValue!.Value;
    }

    /// <summary>Converts only an integral CLR value or invariant decimal string without floating coercion.</summary>
    private static BigInteger ConvertEnumNumericInput(object value)
    {
        if (EnumIntegerCodec.TryConvertIntegral(value, out BigInteger result))
        {
            return result;
        }

        if (value is string text &&
            BigInteger.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
        {
            return result;
        }

        throw new InvalidCastException(
            "Enum Value must be an integral CLR value, BigInteger, or invariant decimal integer string.");
    }

    private static void ValidateEnumName(CstructEnum enm, string? suppliedName)
    {
        if (!string.Equals(enm.Name.Name, suppliedName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Enum value '{suppliedName}' cannot be written as '{enm.Name.Name}'.");
        }
    }

    /// <summary>Rejects moving a self-describing parsed value into an incompatible same-named enum domain.</summary>
    private static void ValidateEnumDomainMetadata(
        CompiledEnumType compiled,
        EnumValueResult parsed)
    {
        if (!string.Equals(parsed.StorageType, compiled.Integer.StorageType, StringComparison.Ordinal) ||
            parsed.BitWidth != compiled.Integer.BitWidth ||
            parsed.IsSigned != compiled.Integer.IsSigned ||
            parsed.RawBits != compiled.Integer.ToRawBits(parsed.Value))
        {
            throw new InvalidOperationException(
                $"Enum value '{parsed.Enum}' does not match the target storage domain.");
        }
    }

    private static void ValidateEnumMemberMetadata(
        CompiledEnumType compiled,
        string? memberName,
        BigInteger value)
    {
        if (memberName is null)
        {
            return;
        }

        if (!compiled.MembersByName.TryGetValue(memberName, out CompiledEnumMember member) ||
            member.RawBits != compiled.Integer.ToRawBits(value))
        {
            throw new InvalidOperationException(
                $"Enum member metadata '{memberName}' does not match value {value}.");
        }
    }
}
