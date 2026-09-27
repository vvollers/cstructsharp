namespace CStructSharp.Generators;

using CStructSharp.Compilation;
using CStructSharp.Diagnostics;

/// <summary>
///     Deferred pointers in the struct readers: a pointer field's address is read in declaration order, and its
///     target is followed after the struct's last field, as the runtime does. Following last lets a target's
///     <c>@count(N)</c> name a field declared after the pointer, and keeps the first failure the same as the runtime's.
/// </summary>
internal sealed partial class LayoutEmitter
{
    /// <summary>
    ///     Finds the pointer fields the struct reader being emitted defers (its own and those of promoted anonymous
    ///     structs, which read into the same value) and declares the locals that remember where each was read.
    /// </summary>
    /// <param name="writer">The generated reader body, before its first field.</param>
    /// <param name="composite">The struct being read.</param>
    private void PrepareDeferredPointers(SourceWriter writer, CompiledCompositeType composite)
    {
        this.CollectDeferredPointers(composite);
        foreach (DeferredPointer deferred in this.deferredPointerOrder)
        {
            // A position of -1 means the field was not read (an inactive conditional arm).
            writer.Line("int " + deferred.Position + " = -1;");
            if (deferred.Field.Array.Kind != CompiledArrayKind.Scalar)
            {
                GeneratedMember shape = this.model.DescribePointer(deferred.Field);
                string elementType = ElementType(shape.TypeName, deferred.Field.Array.Dimensions.Length == 0 ? 1 : deferred.Field.Array.Dimensions.Length);
                writer.Line(elementType + "[]? " + deferred.Elements + " = null;");
            }
        }
    }

    /// <summary>Registers the deferred pointer fields of <paramref name="composite"/> in declaration order.</summary>
    /// <param name="composite">A struct, or a promoted anonymous struct inside it.</param>
    private void CollectDeferredPointers(CompiledCompositeType composite)
    {
        foreach (CompiledField field in composite.Fields)
        {
            if (field.IsUnnamed && this.InlineComposite(field) is { IsUnion: false } promoted)
            {
                this.CollectDeferredPointers(promoted);
            }
            else if (field.FollowsAfterStruct)
            {
                var deferred = new DeferredPointer(field, this.deferredPointerOrder.Count);
                this.deferredPointers[field] = deferred;
                this.deferredPointerOrder.Add(deferred);
            }
        }
    }

    /// <summary>
    ///     The read of one deferred pointer's address when <paramref name="field"/> is deferred by the reader being
    ///     emitted; otherwise <see langword="null"/>. The address becomes an unfollowed pointer until the struct's
    ///     last field is read.
    /// </summary>
    /// <param name="field">The field being read.</param>
    /// <param name="generated">The field's generated member, whose type is the pointer type.</param>
    /// <param name="member">The field name expression for failures.</param>
    /// <param name="memberType">The field type expression for failures.</param>
    /// <returns>The C# expression, or <see langword="null"/>.</returns>
    private string? DeferredAddressRead(CompiledField field, GeneratedMember generated, string member, string memberType)
    {
        if (!this.deferredPointers.ContainsKey(field))
        {
            return null;
        }

        string pointerType = ElementType(generated.TypeName, field.Array.Kind == CompiledArrayKind.Scalar ? 0 : field.Array.Dimensions.Length == 0 ? 1 : field.Array.Dimensions.Length);
        return "new " + pointerType + "(cursor.TakePointerAddress(PointerSize, LittleEndian, " + member + ", " + memberType + "), " + Int(field.PointerDepth) + ")";
    }

    /// <summary>
    ///     Records where a deferred scalar pointer was read and the expressions its follow needs, when the field is
    ///     deferred by the reader being emitted.
    /// </summary>
    /// <param name="writer">The generated reader, just after the address read.</param>
    /// <param name="field">The field that was read.</param>
    /// <param name="property">The property holding the pointer.</param>
    /// <param name="member">The field name expression for failures.</param>
    /// <param name="memberType">The field type expression for failures.</param>
    private void RecordDeferredScalar(SourceWriter writer, CompiledField field, string property, string member, string memberType)
    {
        if (this.deferredPointers.TryGetValue(field, out DeferredPointer? deferred))
        {
            deferred.Bind(property, member, memberType);
            writer.Line(deferred.Position + " = cursor.Position;");
        }
    }

    /// <summary>
    ///     Follows every deferred pointer of the struct reader being emitted, in declaration order. Each follow starts
    ///     at the position just after the pointer's address, where the pointer would have been followed in place, so a
    ///     failure reports the same offset; the reader then seeks to the struct's end.
    /// </summary>
    /// <param name="writer">The generated reader, after its last field.</param>
    /// <param name="scope">The expressions scope, in which every field of the struct is now visible.</param>
    private void EmitDeferredPointerFollows(SourceWriter writer, ReaderScope scope)
    {
        foreach (DeferredPointer deferred in this.deferredPointerOrder)
        {
            CompiledField field = deferred.Field;
            if (deferred.Property is null)
            {
                // The field is never read by this reader (for example only inside a union view).
                continue;
            }

            this.RequirePointerReader(field);
            string follow = "Follow" + PointerReaderName(field);
            string member = deferred.Member!;
            string memberType = deferred.MemberType!;
            if (field.Array.Kind == CompiledArrayKind.Scalar)
            {
                writer.Open("if (" + deferred.Position + " >= 0 && " + deferred.Property + ".Address != 0 && cursor.FollowsPointers)");
                writer.Line("cursor.Position = " + deferred.Position + ";");
                string countArgument = string.Empty;
                if (field.HasCountedTarget)
                {
                    this.EmitPointerCount(writer, field, scope, member, memberType);
                    countArgument = "count, ";
                }

                writer.Line(deferred.Property + " = " + follow + "(ref cursor, variables, " + deferred.Property + ".Address, " + countArgument + member + ", " + memberType + ");");
                writer.Close();
                continue;
            }

            // A pointer array: the elements are consecutive pointer-width addresses from the recorded start.
            string elements = deferred.Elements;
            writer.Open("if (" + elements + " is not null)");
            writer.Open("for (int index = 0; index < " + elements + ".Length; index++)");
            writer.Open("if (" + elements + "[index].Address != 0 && cursor.FollowsPointers)");
            writer.Line("cursor.Position = " + deferred.Position + " + (index + 1) * PointerSize;");
            writer.Line(elements + "[index] = " + follow + "(ref cursor, variables, " + elements + "[index].Address, " + member + ", " + memberType + ");");
            writer.Close();
            writer.Close();
            GeneratedMember shape = this.model.DescribePointer(field);
            string elementType = ElementType(shape.TypeName, field.Array.Dimensions.Length == 0 ? 1 : field.Array.Dimensions.Length);
            writer.Line(deferred.Property + " = " + Reshape(elements, field.Array.Dimensions, elementType) + ";");
            writer.Close();
        }
    }

    /// <summary>
    ///     Evaluates a counted pointer's <c>@count(N)</c> into a <c>count</c> local with the runtime's checks: the
    ///     expression's own failures, a negative count, and the array element limit.
    /// </summary>
    private void EmitPointerCount(SourceWriter writer, CompiledField field, ReaderScope scope, string member, string memberType)
    {
        CompiledArrayShape elements = field.PointerElements!;
        writer.Line("int count;");
        this.EmitExpression(writer, elements.CountExpression!, scope, "count", "pointer element count for " + field.Name, member, memberType, "int");
        if (!ExpressionEmitter.IsInt32Literal(elements.CountExpression!))
        {
            writer.Open("if (count < 0)");
            writer.Line("throw cursor.Fail(" + SourceWriter.Literal(LayoutFailures.NegativeArrayLength(field.Name)) + ", " + member + ", " + memberType + ");");
            writer.Close();
        }

        writer.Line("cursor.RequireArrayLength(count, " + member + ", " + memberType + ");");
    }

    /// <summary>One pointer field a struct reader defers, with the locals and expressions its follow uses.</summary>
    private sealed class DeferredPointer
    {
        /// <summary>Creates the record for the <paramref name="index"/>th deferred pointer of the reader.</summary>
        /// <param name="field">The pointer field.</param>
        /// <param name="index">Its position among the reader's deferred pointers, which names its locals.</param>
        public DeferredPointer(CompiledField field, int index)
        {
            this.Field = field;
            this.Position = "deferredPointer" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            this.Elements = this.Position + "Elements";
        }

        /// <summary>The pointer field.</summary>
        public CompiledField Field { get; }

        /// <summary>The local holding the position after the address (scalar) or the first address (array); -1 until read.</summary>
        public string Position { get; }

        /// <summary>The local holding a pointer array's flat elements until they are followed.</summary>
        public string Elements { get; }

        /// <summary>The property expression that holds the pointer, once the field's read is emitted.</summary>
        public string? Property { get; private set; }

        /// <summary>The field name expression for failures.</summary>
        public string? Member { get; private set; }

        /// <summary>The field type expression for failures.</summary>
        public string? MemberType { get; private set; }

        /// <summary>Records the expressions of the emitted read.</summary>
        /// <param name="property">The property holding the pointer.</param>
        /// <param name="member">The field name expression.</param>
        /// <param name="memberType">The field type expression.</param>
        public void Bind(string property, string member, string memberType)
        {
            this.Property = property;
            this.Member = member;
            this.MemberType = memberType;
        }
    }
}
