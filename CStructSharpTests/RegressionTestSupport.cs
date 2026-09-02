namespace CStructSharp.Tests;

/// <summary>Provides shared fixtures and assertions for review-derived regression tests.</summary>
internal static class RegressionTestSupport
{
    /// <summary>Gets the portable byte-order matrix used by layout tests.</summary>
    public static IReadOnlyList<bool> Endianness { get; } = [true, false,];

    /// <summary>Gets every portable combination of packed/aligned layout and byte order.</summary>
    public static IEnumerable<object[]> AlignmentAndEndianMatrix
    {
        get
        {
            foreach (bool aligned in new[] { false, true, })
            {
                foreach (bool isLittleEndian in Endianness)
                {
                    yield return [aligned, isLittleEndian];
                }
            }
        }
    }

    /// <summary>Gets every public operation category that must remain unreachable for an invalid layout.</summary>
    public static IEnumerable<object[]> PublicOperationMatrix
    {
        get
        {
            yield return ["parse"];
            yield return ["debug"];
            yield return ["address"];
            yield return ["serialize"];
            yield return ["write"];
            yield return ["update"];
            yield return ["pointer"];
        }
    }

    /// <summary>Creates a seekable stream that returns no more than <paramref name="maximumReadSize"/> per read.</summary>
    /// <param name="bytes">The initial stream bytes.</param>
    /// <param name="maximumReadSize">The positive maximum number of bytes returned by one read.</param>
    /// <param name="writable">Whether the stream permits writes.</param>
    /// <returns>A throttled seekable in-memory stream.</returns>
    public static ChunkedMemoryStream CreateChunkedStream(
        byte[] bytes,
        int maximumReadSize,
        bool writable = false)
    {
        return new ChunkedMemoryStream(bytes, maximumReadSize, writable);
    }

    /// <summary>Creates a root containing one absolute pointer to a supplied target declaration and payload.</summary>
    /// <param name="targetDeclaration">The declaration that defines <paramref name="targetType"/>.</param>
    /// <param name="targetType">The target type spelling used by the root pointer field.</param>
    /// <param name="targetBytes">The exact target storage placed immediately after the pointer.</param>
    /// <param name="isLittleEndian">Whether pointer and target scalars use least-significant-byte-first order.</param>
    /// <param name="pointerSize">The pointer storage width.</param>
    /// <param name="aligned">Whether the compiled layout applies portable alignment.</param>
    /// <returns>A disposable compiled layout and initialized stream fixture.</returns>
    public static PointerFixture CreatePointerFixture(
        string targetDeclaration,
        string targetType,
        byte[] targetBytes,
        bool isLittleEndian,
        byte pointerSize = 1,
        bool aligned = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDeclaration);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetType);
        ArgumentNullException.ThrowIfNull(targetBytes);
        if (targetBytes.Length == 0)
        {
            throw new ArgumentException("Pointer target bytes cannot be empty.", nameof(targetBytes));
        }

        int targetAddress = pointerSize;
        var bytes = new byte[checked(pointerSize + targetBytes.Length)];
        WriteUnsigned(bytes, 0, pointerSize, (ulong)targetAddress, isLittleEndian);
        targetBytes.CopyTo(bytes, targetAddress);

        string layout = targetDeclaration + $" struct root {{ {targetType} *target; }};";
        return new PointerFixture(
            new CStruct(
                layout,
                pointerSize: pointerSize,
                aligned: aligned,
                isLittleEndian: isLittleEndian),
            new MemoryStream(bytes, writable: true),
            targetAddress);
    }

    /// <summary>Asserts that an operation restored the caller's original stream position.</summary>
    /// <param name="stream">The stream inspected after the operation.</param>
    /// <param name="expectedPosition">The position recorded before the operation.</param>
    /// <param name="message">Optional assertion context.</param>
    public static void AssertPositionRestored(Stream stream, long expectedPosition, string? message = null)
    {
        Assert.AreEqual(expectedPosition, stream.Position, message);
    }

    /// <summary>Asserts that validation changed neither a memory stream's bytes nor its caller-visible position.</summary>
    /// <param name="stream">The stream inspected after validation fails.</param>
    /// <param name="expectedBytes">The complete bytes recorded before the operation.</param>
    /// <param name="expectedPosition">The caller position recorded before the operation.</param>
    /// <param name="message">Optional assertion context.</param>
    public static void AssertStreamUntouched(
        MemoryStream stream,
        byte[] expectedBytes,
        long expectedPosition,
        string? message = null)
    {
        CollectionAssert.AreEqual(expectedBytes, stream.ToArray(), message);
        AssertPositionRestored(stream, expectedPosition, message);
    }

    /// <summary>Writes an unsigned fixture value without invoking the production codec under test.</summary>
    /// <param name="target">The target storage.</param>
    /// <param name="offset">The first target byte.</param>
    /// <param name="size">The encoded width in bytes.</param>
    /// <param name="value">The unsigned value to encode.</param>
    /// <param name="isLittleEndian">Whether the least-significant byte is stored first.</param>
    public static void WriteUnsigned(
        byte[] target,
        int offset,
        int size,
        ulong value,
        bool isLittleEndian)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        if (size > sizeof(ulong) || offset > target.Length - size)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "The encoded value does not fit the target.");
        }

        for (int index = 0; index < size; index++)
        {
            int destination = isLittleEndian ? offset + index : offset + size - index - 1;
            target[destination] = (byte)(value >> (index * 8));
        }
    }
}
