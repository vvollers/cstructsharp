namespace CStructSharp.Generators.Tests;

using System;
using System.Reflection;

/// <summary>
///     The Views surface has <c>Matches</c> like the readers: a recognition check on the bytes, before a view is made of them, that
///     agrees with the runtime layout's <c>CStruct.Matches</c>.
/// </summary>
[TestClass]
public class ViewMatchesTests
{
    private const string Layout = "struct sb { uint32 inodes; uint16 magic @expect(magic == 0xEF53); uint16 state @supports(state < 4); };";

    /// <summary>The shape of the generated static <c>Matches</c>.</summary>
    /// <param name="source">The bytes.</param>
    /// <param name="variables">The caller variables.</param>
    /// <returns>Whether the bytes match.</returns>
    private delegate bool MatchesCall(ReadOnlySpan<byte> source, System.Collections.Generic.IReadOnlyDictionary<string, int>? variables);

    /// <summary>The generated <c>Matches</c> of the Views surface answers like the runtime layout for matching, wrong and short input.</summary>
    [TestMethod]
    public void ViewsSurface_Matches_AgreesWithTheRuntimeLayout()
    {
        GeneratorResult result = GeneratorRunner.Run("using CStructSharp;\nnamespace Demo;\n[CStructLayout(\"" + Layout + "\", Root = \"sb\", Surface = GeneratedSurface.Views)]\npublic static partial class Image { }").AssertClean();
        Type type = result.Load().GetType("Demo.Image")!;
        MethodInfo matches = type.GetMethod("Matches", new[] { typeof(ReadOnlySpan<byte>), typeof(System.Collections.Generic.IReadOnlyDictionary<string, int>) })
            ?? throw new AssertFailedException("The Views surface has no Matches");
        var layout = new CStruct(Layout);

        foreach (byte[] bytes in new[] { new byte[] { 1, 0, 0, 0, 0x53, 0xEF, 9, 0 }, new byte[] { 1, 0, 0, 0, 0x53, 0xEE, 0, 0 }, new byte[] { 1, 0, 0, 0, 0x53 }, Array.Empty<byte>() })
        {
            Assert.AreEqual(layout.Matches("sb", bytes), Invoke(matches, bytes), BitConverter.ToString(bytes));
        }
    }

    /// <summary>Calls the generated static <c>Matches</c> through a delegate, because a span cannot be boxed for reflection.</summary>
    /// <param name="matches">The generated method.</param>
    /// <param name="bytes">The source.</param>
    /// <returns>Its answer.</returns>
    private static bool Invoke(MethodInfo matches, byte[] bytes)
    {
        var call = (MatchesCall)Delegate.CreateDelegate(typeof(MatchesCall), matches);
        return call(bytes, null);
    }
}
