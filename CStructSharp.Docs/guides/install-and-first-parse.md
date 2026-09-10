---
title: Install and make a first parse
description: Create a console project and read six bytes using a complete CStructSharp program.
---

# Install and make a first parse

This lesson reads a six-byte header into C# values. You need basic C# and a stable .NET 10 SDK.
You do not need to clone CStructSharp. For JavaScript, use the separate [Node.js and browser quick start](browser/index.md).

## Create an application

Open a terminal in a directory where you keep projects. Run these commands in PowerShell or a Unix shell:

```sh
dotnet new console -n BinaryHeader -f net10.0
cd BinaryHeader
dotnet add package CStructSharp
```

The first command creates an application targeting .NET 10.
The second enters its directory. The third downloads the published package and adds it to the project.
If `dotnet` cannot be found, install the SDK and reopen your terminal.

The library also supports .NET 8. For an existing .NET 8 application, run only the package-add command.
To try this new project on .NET 8 using the .NET 10 SDK, change the `TargetFramework` value in `BinaryHeader.csproj`
from `net10.0` to `net8.0` and install the .NET 8 runtime before running it. The .NET 10 console template does not
accept `-f net8.0` directly.

## Read six bytes

Replace the entire contents of `Program.cs` with this program:

[!code-csharp[Complete first program](../examples/starter/Program.cs)]

Run it from the application directory:

```sh
dotnet run
```

Expected output:

```text
kind = 2
length = 6
```

`new CStruct(...)` prepares the layout. Reuse that object when reading more data with the same format.
`bytes` is the input; `header` is the resulting C# object. The string `"header"` selects the declaration to read.
`AsSpan()` passes a view over the array without copying it and selects the same overload on both supported runtimes.
Names are case-sensitive, so `"Header"` is a different name.

| Field | Offset | Width | Bytes | Value |
| --- | --- | --- | --- | --- |
| `kind` | 0 | 2 bytes | `02 00` | 2 |
| `length` | 2 | 4 bytes | `06 00 00 00` | 6 |

The default is packed placement and little-endian byte order. Packed means no gaps between these fields.
Little-endian means the least significant byte comes first. The eight-byte pointer default has no effect here
because the layout contains no pointers. See [binary layout basics](binary-layout-basics.md) for diagrams and
explicit constructor options.

## Try a change

Change `0x02` to `0x03` and run again. Predict which output changes before running it.

Answer: `kind` becomes `3`; `length` stays `6`. Next, remove the final byte. The read fails because the layout
requires six bytes. Restore the byte to fix it. [Handle errors](errors-and-recovery.md) explains how applications
can report such failures.

You can also [run this lesson in the explorer](https://vvollers.github.io/cstructsharp/explorer/#lesson=header).

## Continue with the same header

Follow [Write, update, and use a C# class](header-next-steps.md). It adds one operation at a time and includes the
complete class definition. Use [Choose an API](choosing-an-api.md) when you are ready to compare other input and
output choices.

Continue the [layout-language tutorial](../language/tutorial/index.md) to learn more declarations and byte layouts.
