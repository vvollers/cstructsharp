namespace CStructSharp.Fuzzing;

using System.Text;
using CStructSharp;

/// <summary>Owns the five bounded managed QA-04 target entry points.</summary>
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

    public static string[] Names =>
    [
        "binary-roundtrip",
        "definition",
        "expression",
        "path",
        "pointer-union",
    ];

    public FuzzTarget Resolve(string name)
    {
        return name switch
        {
            "definition" => new FuzzTarget(name, this.Definition, IsLayoutFailure),
            "expression" => new FuzzTarget(name, this.Expression, IsLayoutFailure),
            "path" => new FuzzTarget(name, this.Path, IsPathFailure),
            "binary-roundtrip" => new FuzzTarget(name, this.BinaryRoundTrip, IsBinaryFailure),
            "pointer-union" => new FuzzTarget(name, this.PointerUnion, IsPointerUnionFailure),
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

    private void Definition(byte[] input)
    {
        string definition = Encoding.UTF8.GetString(input);
        _ = new CStruct(
            definition,
            pointerSize: 2,
            aligned: input.Length % 2 == 0,
            isLittleEndian: input.Length % 3 != 0,
            compilationOptions: this.CreateCompilationOptions());
    }

    private void Expression(byte[] input)
    {
        string expression = Encoding.UTF8.GetString(input);
        string definition = $"struct root {{ byte values[({expression}) & 15]; }};";
        var cstruct = new CStruct(
            definition,
            pointerSize: 2,
            compilationOptions: this.CreateCompilationOptions());
        _ = cstruct.GetStructSizeInBytes("root");
    }

    private void Path(byte[] input)
    {
        string path = Encoding.UTF8.GetString(input);
        byte[] bytes = new byte[32];
        bytes[14] = 0;
        using var stream = new MemoryStream(bytes);

        switch (input.Length % 3)
        {
        case 0:
            _ = this.pathLayout.ResolveAddress(stream, path, options: this.readOptions);
            break;
        case 1:
            _ = this.pathLayout.ReadValue(stream, path, options: this.readOptions);
            break;
        default:
            _ = this.pathLayout.GetDynamicArrayLength(stream, path, options: this.readOptions);
            break;
        }
    }

    private void BinaryRoundTrip(byte[] input)
    {
        object parsed = this.binaryLayout.Parse(input.AsSpan(), "root", options: this.readOptions);
        byte[] serialized = this.binaryLayout.Serialize("root", parsed, options: this.writeOptions);
        using var output = new MemoryStream();
        this.binaryLayout.WriteStream(output, "root", parsed, options: this.writeOptions);
        if (!serialized.AsSpan().SequenceEqual(output.ToArray()))
        {
            throw new InvalidDataException("Owned and stream writer paths produced different bytes.");
        }

        _ = this.binaryLayout.Parse(serialized.AsSpan(), "root", options: this.readOptions);
    }

    private void PointerUnion(byte[] input)
    {
        using var stream = new MemoryStream(input, writable: false);
        if (input.Length > 0 && (input[0] & 1) != 0)
        {
            _ = this.pointerUnionLayout.ParseStreamWithDebug(stream, "node", this.readOptions);
        }
        else
        {
            _ = this.pointerUnionLayout.ParseStream(stream, "node", options: this.readOptions);
        }
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

/// <summary>Pairs one target action with its intentionally documented failure predicate.</summary>
internal sealed record FuzzTarget(
    string Name,
    Action<byte[]> Execute,
    Func<Exception, bool> IsDocumentedFailure);
