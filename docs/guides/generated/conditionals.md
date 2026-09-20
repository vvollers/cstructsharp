---
title: Conditional fields in generated code
description: if and switch in the layout, how the generated code evaluates them, inactive arms and Has flags, and caller variables.
---

# Conditional fields in generated code

[Choose fields with if and switch](../conditional-fields.md) explains conditional groups: an `if`/`else` or a
`switch` in a struct selects which fields follow, decided once per struct instance from values read earlier. The
generated code follows the same rules; this lesson shows how they surface in C#.

[!code-csharp[A message with two conditional groups](../../examples/GeneratedExamples.cs#generated-conditionals-class)]

[!code-csharp[Reading and writing it](../../examples/GeneratedExamples.cs#generated-conditionals)]

## `Has` flags

A conditional member may be absent, so the generated class gives it a companion: `HasCode` is `true` when the arm
that declares `code` was selected and `Code` holds a value. A reference-typed conditional member (a string, an
array, a nested class) is declared nullable and stays `null` when its arm was not selected; a value-typed one stays
`default`. Test the flag, not the value - a `uint` cannot be "absent" by itself.

The runtime represents the same fact by leaving the member out of the `StructValue`; the mapper bridge turns an
absent member into a `null` on a nullable property (see [mapped classes](mapped-classes.md)).

## How the generated reader decides

The reader keeps one slot per conditional group and evaluates the group's selector when it reaches the group's
first field:

```csharp
int placementArm0 = int.MinValue;
// uint32 code
try
{
    placementArm0 = (Expressions.Equal(Expressions.RequireInt32((long)value.Kind, "kind"), 1)) != 0 ? 1 : 0;
}
catch (Exception expressionFailure)
{
    throw cursor.FailExpression(expressionFailure, "conditional selector", member, memberType);
}
if (placementArm0 == 1)
{
    // read code, set value.HasCode = true
}
```

Every later field of the same group tests the slot; nothing is evaluated twice. A `switch` maps the selector's
value through its case table (`1 => 0, _ => -1` here, the default arm being `-1`). A group nested inside an arm is
evaluated only when that arm is active, so an unknown name inside an inactive arm never fails.

The expression operators are the runtime's: checked 32-bit arithmetic, `&&`/`||` that stop early, and the same
failure text - `Cannot evaluate conditional selector: ...` - when a selector divides by zero or uses a member the
struct has not read yet. Inside a struct with conditional fields, the struct's own member names hide any caller
variable or outer value of the same name until the member is read, as at runtime.

## Caller variables

A selector may use a name the layout does not define. The `variables` argument of `Parse<Name>` supplies it
(`Messages.ParseMessage(bytes, new Dictionary<string, int> { ["MODE"] = 1 })`), with the runtime's precedence: a
caller variable overrides a `#define` of the same name, and a define that depends on it is recomputed.

## Writing

The writer decides from the values it is given. A member whose flag is `true` but whose arm is not selected is
`Inactive conditional field supplied: code`; a selected arm whose member's flag is `false` is
`No value was supplied for 'code'.` - the two texts the runtime uses for the same mistakes.

## Check yourself

1. After parsing `[2, 0x34, 0x12, 4, 9]`, what are `HasCode`, `HasShortCode`, and `Code`?
2. When is a group's selector evaluated?
3. What happens when a selector inside an inactive `if` refers to an undefined name?

<details>
<summary>Answers</summary>

1. `false`, `true`, and `0` - the default of a member that was not read.
2. Once per struct instance, when the reader reaches the first field that belongs to the group.
3. Nothing: the inner group is never evaluated because its outer arm was not selected.

</details>

## Exercise

Add a third arm to the switch - `case 2: { uint16 window; }` - and write the bytes for a message with
`kind = 2`, `short_code = 0x1234`, `window = 0x0100`, `tail = 9`.

<details>
<summary>Solution</summary>

`kind` 2 selects the `else` arm (`short_code`) and case 2 (`window`): `02 34 12 00 01 09`. `HasWindow` is true,
`HasPadding` and `HasRetries` false.

</details>
