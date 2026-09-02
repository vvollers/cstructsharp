namespace CStructSharp.Tests;

using System.Reflection;
using System.Runtime.CompilerServices;
using CStructSharp.Structure;

/// <summary>Defines the immutable public option contract accepted by ADR-007 and ADR-011.</summary>
[TestClass]
public class OptionImmutabilityTests
{
    /// <summary>Requires every writable public option property to be assignable only during initialization.</summary>
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

    /// <summary>Retains the documented defaults while one immutable value is safely reused across operations.</summary>
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
        Assert.AreEqual(PocoBindingMode.PublicReadable, writeOptions.BindingMode);
        Assert.AreEqual(64 * 1024 * 1024L, writeOptions.MaxTotalBytesWritten);
        Assert.IsTrue(updateOptions.AllowPointerDereference);
        Assert.IsTrue(updateOptions.RequireExistingPointerTarget);
        Assert.IsTrue(updateOptions.ClearUnionStorage);

        for (int index = 0; index < 2; index++)
        {
            using var readStream = new MemoryStream(new byte[] { (byte)(index + 1), });
            dynamic parsed = cstruct.ParseStream(
                readStream,
                "root",
                new Dictionary<string, Expr>(),
                readOptions);
            Assert.AreEqual((byte)(index + 1), (byte)parsed.value);

            CollectionAssert.AreEqual(
                new byte[] { (byte)(index + 2), },
                cstruct.Serialize("root.value", (byte)(index + 2), options: writeOptions));

            using var updateStream = new MemoryStream(new byte[] { 0, });
            cstruct.UpdateStream(updateStream, "root.value", (byte)(index + 3), options: updateOptions);
            CollectionAssert.AreEqual(new byte[] { (byte)(index + 3), }, updateStream.ToArray());
        }
    }
}
