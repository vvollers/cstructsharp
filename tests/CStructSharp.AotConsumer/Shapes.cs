using CStructSharp;

/// <summary>A generated layout: the classes, readers, writers, views, and setters come from the package's generator.</summary>
[CStructLayout("struct point { int16 x; int16 y; }; struct record { uint8 tag; point origin; point corners[2]; uint8 flags[3]; };", Root = "record")]
public static partial class Shapes
{
}
