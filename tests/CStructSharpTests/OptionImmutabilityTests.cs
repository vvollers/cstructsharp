namespace CStructSharp.Tests;

using System.Reflection;
using System.Runtime.CompilerServices;
using CStructSharp.Syntax;

/// <summary>Defines the immutable public option contract.</summary>
[TestClass]
public class OptionImmutabilityTests
{
    /// <summary>
    ///     Reflection inspects compilation, read, write, and update option properties.
    /// </summary>
    /// <remarks>
    ///     Each must support object initialization but have an init-only setter, preventing ordinary reassignment
    ///     afterward. This is an API contract test for reusable settings, not a test of any binary structure.
    /// </remarks>
    [TestMethod]
    public void PublicOptionProperties_AreInitOnly()
    {
        Type[] optionTypes =
        [
            typeof(CStructCompilationOptions),
            typeof(ReadOptions),
            typeof(WriteOptions),
            typeof(UpdateOptions),
        ];

        foreach (Type optionType in optionTypes)
        {
            PropertyInfo[] properties = optionType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotEmpty(properties, optionType.Name);

            foreach (PropertyInfo property in properties)
            {
                MethodInfo? setter = property.SetMethod;
                Assert.IsNotNull(setter, $"{optionType.Name}.{property.Name} must support object initialization.");
                CollectionAssert.Contains(
                    setter.ReturnParameter.GetRequiredCustomModifiers(),
                    typeof(IsExternalInit),
                    $"{optionType.Name}.{property.Name} must not expose an ordinary mutable setter.");
            }
        }
    }

    /// <summary>
    ///     Default options must retain documented choices such as absolute addressing, pointer following, union
    ///     clearing, and a 64 MiB write budget.
    /// </summary>
    /// <remarks>
    ///     Reusing the same option objects for several one-byte operations must produce each operation's expected
    ///     result. Budgets and temporary state must not accumulate across calls.
    /// </remarks>
    [TestMethod]
    public void ImmutableOptions_RetainDefaultsAndSupportReuse()
    {
        var cstruct = new CStruct("struct root { uint8 value; };", compilationOptions: new CStructCompilationOptions());
        var readOptions = new ReadOptions();
        var writeOptions = new WriteOptions();
        var updateOptions = new UpdateOptions();

        Assert.AreEqual(PointerAddressingMode.Absolute, readOptions.AddressingMode);
        Assert.IsTrue(readOptions.DereferencePointers);
        Assert.AreEqual(1_000_000, readOptions.MaxArrayElements);
        Assert.AreEqual(64 * 1024 * 1024L, writeOptions.MaxTotalBytesWritten);
        Assert.IsTrue(updateOptions.DereferencePointers);
        Assert.IsTrue(updateOptions.RequireExistingPointerTarget);
        Assert.IsTrue(updateOptions.ClearUnionStorage);

        for (int index = 0; index < 2; index++)
        {
            using var readStream = new MemoryStream(new byte[] { (byte)(index + 1), });
            dynamic parsed = cstruct.Parse(
                readStream,
                "root",
                new Dictionary<string, Expr>(),
                readOptions);
            Assert.AreEqual((byte)(index + 1), (byte)parsed.value);

            CollectionAssert.AreEqual(
                new byte[] { (byte)(index + 2), },
                cstruct.Serialize("root.value", (byte)(index + 2), options: writeOptions));

            using var updateStream = new MemoryStream(new byte[] { 0, });
            cstruct.Update(updateStream, "root.value", (byte)(index + 3), options: updateOptions);
            CollectionAssert.AreEqual(new byte[] { (byte)(index + 3), }, updateStream.ToArray());
        }
    }

    /// <summary>
    ///     Every option type is a record: a <c>with</c> expression copies the other members, equality is member-wise,
    ///     and the collection-typed members of the compilation options compare by reference.
    /// </summary>
    [TestMethod]
    public void OptionRecords_CopyWithAndCompareByMembers()
    {
        var read = new ReadOptions { MaxArrayElements = 12, TrimFixedText = true, Origin = 4, };
        ReadOptions readCopy = read with { MaxTotalBytesRead = 99, };
        Assert.AreEqual(12, readCopy.MaxArrayElements);
        Assert.IsTrue(readCopy.TrimFixedText);
        Assert.AreEqual(4L, readCopy.Origin);
        Assert.AreEqual(99L, readCopy.MaxTotalBytesRead);
        Assert.AreEqual(read, read with { }, "a copy without changes equals the original");
        Assert.AreNotEqual(read, readCopy);
        Assert.AreEqual(new ReadOptions { Origin = 4, }, new ReadOptions { Origin = 4, });
        Assert.AreEqual(new ReadOptions().GetHashCode(), new ReadOptions().GetHashCode());

        var write = new WriteOptions { MaxStringBytes = 7, };
        Assert.AreEqual(7L, (write with { MaxArrayElements = 3, }).MaxStringBytes);
        var update = new UpdateOptions { MaxStringBytes = 7, DereferencePointers = false, };
        UpdateOptions updateCopy = update with { ClearUnionStorage = false, };
        Assert.AreEqual(7L, updateCopy.MaxStringBytes);
        Assert.IsFalse(updateCopy.DereferencePointers);
        Assert.AreNotEqual<WriteOptions>(write, update, "a derived record never equals its base");

        var codecs = new List<Codecs.ICustomCodec>();
        var defined = new HashSet<string> { "A", };
        var compilation = new CStructCompilationOptions { CLongWidth = 32, Codecs = codecs, Defined = defined, };
        CStructCompilationOptions compilationCopy = compilation with { Prelude = "// p", };
        Assert.AreEqual(32, compilationCopy.CLongWidth);
        Assert.AreSame(codecs, compilationCopy.Codecs);
        Assert.AreEqual(compilation, compilation with { });
        Assert.AreNotEqual(compilation, compilation with { Codecs = new List<Codecs.ICustomCodec>(), }, "collection members compare by reference");
        Assert.AreNotEqual(compilation, compilation with { Defined = new HashSet<string> { "A", }, });

        // Options with the same members build the same layout, and a copied option still drives an operation.
        var layout = new CStruct("struct root { uint8 value; };", compilationOptions: compilationCopy);
        Assert.AreEqual((byte)5, layout.ReadValue<byte>(new byte[] { 5, }, "root.value", options: readCopy));
    }
}
