namespace CStructSharp.Generators.Tests;

using System;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>Checks that signed-byte arrays compile and retain their values across supported compiler hosts.</summary>
[TestClass]
public class CompilerCompatibilityTests
{
    /// <summary>Compiles fixed and counted arrays, then checks signed values and exact serialized bytes.</summary>
    /// <param name="languageVersion">The consumer language version, independent of the generator's build language.</param>
    [TestMethod]
    [DataRow(LanguageVersion.CSharp12)]
#if MODERN_ROSLYN
    [DataRow(LanguageVersion.CSharp14)]
#endif
    public void SignedByteArrays_CompileAndRoundTrip(LanguageVersion languageVersion)
    {
        const string Source = """
            using CStructSharp;
            namespace Compatibility;
            [CStructLayout("struct root { int8 fixedValues[4]; uint8 count; int8 countedValues[count]; };", Root = "root")]
            public static partial class Packet
            {
                /// <summary>Checks decoding and returns the encoded bytes for an exact round trip.</summary>
                public static byte[] Probe(byte[] bytes)
                {
                    var value = Parse(bytes);
                    if (value.FixedValues[0] != -128 || value.FixedValues[1] != -1 ||
                        value.FixedValues[2] != 0 || value.FixedValues[3] != 127 ||
                        value.CountedValues.Length != value.Count)
                        throw new System.InvalidOperationException("Incorrect fixed array or count.");
                    for (int i = 0; i < value.CountedValues.Length; i++)
                        if (value.CountedValues[i] != value.FixedValues[i])
                            throw new System.InvalidOperationException("Incorrect counted array.");
                    return Serialize(value);
                }
            }
            """;
        Type packet = GeneratorRunner.Run(Source, languageVersion: languageVersion).AssertClean().Load().GetType("Compatibility.Packet")!;
        byte[][] inputs = [[128, 255, 0, 127, 4, 128, 255, 0, 127], [128, 255, 0, 127, 0]];
        foreach (byte[] input in inputs)
        {
            byte[] output = (byte[])packet.GetMethod("Probe")!.Invoke(null, [input])!;
            CollectionAssert.AreEqual(input, output);
        }
    }
}
