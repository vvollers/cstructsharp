namespace CStructSharp.Fuzzing;

using System.Text;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>Owns the five bounded managed fuzz target entry points.</summary>
internal sealed class FuzzTargets
{
    private const string BinaryDefinition = "struct root { byte count; uint16 values[count]; char name[]; };";
    private const string PathDefinition =
        "struct leaf { byte value; }; " +
        "union choice { uint16 number; byte raw[2]; }; " +
        "struct root { byte count; uint16 values[4]; leaf nested; choice selected; uint16 *link; char name[]; };";

    private const string PointerUnionDefinition =
        "union payload { uint32 number; byte raw[4]; }; " +
        "struct node { byte tag; payload data; node *next; };";

    private readonly FuzzLimits limits;
    private readonly CStruct binaryLayout;
    private readonly CStruct pathLayout;
    private readonly CStruct pointerUnionLayout;
    private readonly ReadOptions readOptions;
    private readonly WriteOptions writeOptions;

    /// <summary>
    ///     Compiles the fixed binary, path and pointer-union layouts once and builds read and write options from the
    ///     harness resource budgets.
    /// </summary>
    /// <param name="limits">The resource budgets that every target applies.</param>
    public FuzzTargets(FuzzLimits limits)
    {
        this.limits = limits;
        CStructCompilationOptions compilationOptions = this.CreateCompilationOptions();
        this.binaryLayout = new CStruct(
            BinaryDefinition,
            pointerSize: 2,
            compilationOptions: compilationOptions);
        this.pathLayout = new CStruct(
            PathDefinition,
            pointerSize: 2,
            compilationOptions: compilationOptions);
        this.pointerUnionLayout = new CStruct(
            PointerUnionDefinition,
            pointerSize: 2,
            compilationOptions: compilationOptions);
        this.readOptions = new ReadOptions
        {
            MaxArrayElements = limits.MaxArrayElements,
            MaxStringBytes = limits.MaxStringBytes,
            MaxTotalBytesRead = limits.MaxTotalBytesRead,
            MaxNestingDepth = limits.MaxNestingDepth,
            MaxPointerDepth = limits.MaxPointerDepth,
            MaxPointerTargetBytes = limits.MaxPointerTargetBytes,
        };
        this.writeOptions = new WriteOptions
        {
            MaxArrayElements = limits.MaxArrayElements,
            MaxStringBytes = limits.MaxStringBytes,
            MaxTotalBytesWritten = limits.MaxTotalBytesWritten,
            MaxNestingDepth = limits.MaxNestingDepth,
        };
    }

    /// <summary>Gets the names of every target, sorted ordinally, as accepted by <see cref="Resolve"/>.</summary>
    public static string[] Names =>
    [
        "binary-roundtrip",
        "definition",
        "expression",
        "generated-differential",
        "path",
        "pointer-union",
    ];

    /// <summary>Creates the named target with its entry point and its documented-failure classifier.</summary>
    /// <param name="name">One of <see cref="Names"/>.</param>
    /// <returns>The target to execute; <c>generated-differential</c> documents no failures.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a known target.</exception>
    public FuzzTarget Resolve(string name)
    {
        return name switch
        {
            "definition" => new FuzzTarget(name, this.Definition, IsLayoutFailure),
            "expression" => new FuzzTarget(name, this.Expression, IsLayoutFailure),
            "path" => new FuzzTarget(name, this.Path, IsPathFailure),
            "binary-roundtrip" => new FuzzTarget(name, this.BinaryRoundTrip, IsBinaryFailure),
            "pointer-union" => new FuzzTarget(name, this.PointerUnion, IsPointerUnionFailure),
            "generated-differential" => new FuzzTarget(name, this.GeneratedDifferentialTarget, static _ => false),
            _ => throw new ArgumentException($"Unknown managed fuzz target '{name}'.", nameof(name)),
        };
    }

    private static bool IsLayoutFailure(Exception exception)
    {
        return exception is CStructLayoutException;
    }

    private static bool IsPathFailure(Exception exception)
    {
        return exception is CStructPathException or CStructReadException;
    }

    private static bool IsBinaryFailure(Exception exception)
    {
        return exception is CStructReadException or CStructWriteException;
    }

    private static bool IsPointerUnionFailure(Exception exception)
    {
        return exception is CStructReadException;
    }

    /// <summary>Compiles the input as a layout definition and renders the compiled layout as its canonical definition text.</summary>
    /// <param name="input">The definition as UTF-8; its length selects alignment and byte order.</param>
    /// <param name="output">The rendering of the outcome.</param>
    private void Definition(byte[] input, CanonicalText output)
    {
        string definition = Encoding.UTF8.GetString(input);
        var cstruct = new CStruct(
            definition,
            pointerSize: 2,
            aligned: input.Length % 2 == 0,
            isLittleEndian: input.Length % 3 != 0,
            compilationOptions: this.CreateCompilationOptions());
        output.Value("definition", cstruct.ToDefinition());
    }

    /// <summary>Uses the input as an array-count expression and renders the size of the struct it produces.</summary>
    /// <param name="input">The expression as UTF-8.</param>
    /// <param name="output">The rendering of the outcome.</param>
    private void Expression(byte[] input, CanonicalText output)
    {
        string expression = Encoding.UTF8.GetString(input);
        string definition = $"struct root {{ byte values[({expression}) & 15]; }};";
        var cstruct = new CStruct(
            definition,
            pointerSize: 2,
            compilationOptions: this.CreateCompilationOptions());
        output.Value("size", cstruct.GetStructSizeInBytes("root"));
    }

    /// <summary>Uses the input as a path into a fixed 32-byte buffer and renders the resolved address, value, or array length.</summary>
    /// <param name="input">The path as UTF-8; its length modulo 3 selects the operation.</param>
    /// <param name="output">The rendering of the outcome.</param>
    private void Path(byte[] input, CanonicalText output)
    {
        string path = Encoding.UTF8.GetString(input);
        byte[] bytes = new byte[32];
        bytes[14] = 0;
        using var stream = new MemoryStream(bytes);

        switch (input.Length % 3)
        {
        case 0:
            output.Value("address", this.pathLayout.ResolveAddress(stream, path, options: this.readOptions));
            break;
        case 1:
            output.Value("value", this.pathLayout.ReadValue(stream, path, options: this.readOptions));
            break;
        default:
            output.Value("length", this.pathLayout.GetArrayLength(stream, path, options: this.readOptions));
            break;
        }
    }

    /// <summary>
    ///     Parses the input, writes the value back through the owned and stream writers (which must agree), parses the
    ///     written bytes again, and renders the value, the bytes, and the second value.
    /// </summary>
    /// <param name="input">The encoded root struct.</param>
    /// <param name="output">The rendering of the outcome.</param>
    /// <exception cref="InvalidDataException">The two writers produce different bytes.</exception>
    private void BinaryRoundTrip(byte[] input, CanonicalText output)
    {
        object parsed = this.binaryLayout.Parse(input.AsSpan(), "root", options: this.readOptions);
        byte[] serialized = this.binaryLayout.Serialize("root", parsed, options: this.writeOptions);
        using var written = new MemoryStream();
        this.binaryLayout.Write(written, "root", parsed, options: this.writeOptions);
        if (!serialized.AsSpan().SequenceEqual(written.ToArray()))
        {
            throw new InvalidDataException("Owned and stream writer paths produced different bytes.");
        }

        output.Value("parsed", parsed);
        output.Bytes("serialized", serialized);
        output.Value("reparsed", this.binaryLayout.Parse(serialized.AsSpan(), "root", options: this.readOptions));
    }

    /// <summary>Parses the input as a pointer-linked node, with debug records when the first byte is odd, and renders the result.</summary>
    /// <param name="input">The encoded node.</param>
    /// <param name="output">The rendering of the outcome.</param>
    private void PointerUnion(byte[] input, CanonicalText output)
    {
        using var stream = new MemoryStream(input, writable: false);
        if (input.Length > 0 && (input[0] & 1) != 0)
        {
            ParseResult result = this.pointerUnionLayout.ParseWithDebug(stream, "node", options: this.readOptions);
            output.Value("result", result.Value);
            output.Debug("debug", result.Debug);
        }
        else
        {
            output.Value("result", this.pointerUnionLayout.Parse(stream, "node", options: this.readOptions));
        }
    }

    /// <summary>The generated readers and writers against the runtime over the harness's layouts; every outcome must agree, so no failure is a documented one.</summary>
    /// <param name="input">The encoded input every layout reads.</param>
    /// <param name="output">The rendering of the agreed outcomes.</param>
    private void GeneratedDifferentialTarget(byte[] input, CanonicalText output)
    {
        GeneratedDifferential.Run(input, this.readOptions, this.writeOptions, output);
    }

    private CStructCompilationOptions CreateCompilationOptions()
    {
        return new CStructCompilationOptions
        {
            MaxDefinitionLength = this.limits.MaxDefinitionLength,
            MaxLayoutNestingDepth = this.limits.MaxLayoutNestingDepth,
            MaxExpressionNestingDepth = this.limits.MaxExpressionNestingDepth,
            MaxExpressionTokens = this.limits.MaxExpressionTokens,
        };
    }
}
