namespace CStructSharp.Tests;

/// <summary>
///     Exercises <see cref="LayoutSourceValidator"/> directly, independent of the parser it protects. Only reachable
///     indirectly through <see cref="CStruct"/> construction before this type was extracted from the God-Object
///     <c>CStruct</c> partial class.
/// </summary>
[TestClass]
public class LayoutSourceValidatorTests
{
    /// <summary>Ordinary layout text within every configured limit passes without throwing.</summary>
    [TestMethod]
    public void ValidateLayoutSource_WithinAllLimits_DoesNotThrow()
    {
        LayoutSourceValidator.ValidateLayoutSource(
            "struct root { uint8 value; };",
            new CStructCompilationOptions());
    }

    /// <summary>A null layout cannot be validated.</summary>
    [TestMethod]
    public void ValidateLayoutSource_NullLayout_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => LayoutSourceValidator.ValidateLayoutSource(null!, new CStructCompilationOptions()));
    }

    /// <summary>Empty or whitespace-only layout text has no declarations to compile.</summary>
    [TestMethod]
    public void ValidateLayoutSource_WhitespaceOnly_Throws()
    {
        Assert.Throws<CStructLayoutException>(
            () => LayoutSourceValidator.ValidateLayoutSource("   ", new CStructCompilationOptions()));
    }

    /// <summary>Every non-positive compilation limit is rejected before any text is scanned.</summary>
    [TestMethod]
    public void ValidateLayoutSource_NonPositiveLimit_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LayoutSourceValidator.ValidateLayoutSource(
                "struct root { uint8 value; };",
                new CStructCompilationOptions { MaxDefinitionLength = 0, }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LayoutSourceValidator.ValidateLayoutSource(
                "struct root { uint8 value; };",
                new CStructCompilationOptions { MaxLayoutNestingDepth = 0, }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LayoutSourceValidator.ValidateLayoutSource(
                "struct root { uint8 value; };",
                new CStructCompilationOptions { MaxExpressionNestingDepth = 0, }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LayoutSourceValidator.ValidateLayoutSource(
                "struct root { uint8 value; };",
                new CStructCompilationOptions { MaxExpressionTokens = 0, }));
    }

    /// <summary>Layout text longer than the configured length limit is rejected before the nesting scan runs.</summary>
    [TestMethod]
    public void ValidateLayoutSource_ExceedsDefinitionLength_Throws()
    {
        Assert.Throws<CStructLayoutException>(
            () => LayoutSourceValidator.ValidateLayoutSource(
                "struct root { uint8 value; };",
                new CStructCompilationOptions { MaxDefinitionLength = 5, }));
    }

    /// <summary>Brace nesting beyond the configured depth limit is rejected.</summary>
    [TestMethod]
    public void ValidateLayoutSource_ExceedsNestingDepth_Throws()
    {
        Assert.Throws<CStructLayoutException>(
            () => LayoutSourceValidator.ValidateLayoutSource(
                "struct a { struct b { struct c { uint8 v; }; }; };",
                new CStructCompilationOptions { MaxLayoutNestingDepth = 2, }));
    }

    /// <summary>Parenthesis nesting in array-length expressions beyond the configured depth limit is rejected.</summary>
    [TestMethod]
    public void ValidateLayoutSource_ExceedsExpressionNestingDepth_Throws()
    {
        Assert.Throws<CStructLayoutException>(
            () => LayoutSourceValidator.ValidateLayoutSource(
                "struct root { uint8 values[((( 1 )))]; };",
                new CStructCompilationOptions { MaxExpressionNestingDepth = 2, }));
    }

    /// <summary>Braces and parentheses inside a line comment do not count toward either nesting limit.</summary>
    [TestMethod]
    public void ValidateLayoutSource_LineComment_BracesAndParensAreIgnored()
    {
        LayoutSourceValidator.ValidateLayoutSource(
            "struct root { // {{{ ((( \n uint8 value; };",
            new CStructCompilationOptions { MaxLayoutNestingDepth = 1, MaxExpressionNestingDepth = 1, });
    }

    /// <summary>Braces and parentheses inside a block comment do not count toward either nesting limit.</summary>
    [TestMethod]
    public void ValidateLayoutSource_BlockComment_BracesAndParensAreIgnored()
    {
        LayoutSourceValidator.ValidateLayoutSource(
            "struct root { /* {{{ ((( */ uint8 value; };",
            new CStructCompilationOptions { MaxLayoutNestingDepth = 1, MaxExpressionNestingDepth = 1, });
    }

    /// <summary>An unterminated block comment simply consumes the remaining text without throwing from unmatched braces.</summary>
    [TestMethod]
    public void ValidateLayoutSource_UnterminatedBlockComment_DoesNotThrowFromNestingScan()
    {
        LayoutSourceValidator.ValidateLayoutSource(
            "struct root { /* {{{ (((",
            new CStructCompilationOptions { MaxLayoutNestingDepth = 1, MaxExpressionNestingDepth = 1, });
    }
}
