; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CSG001 | CStructSharp | Error | Layout does not compile
CSG002 | CStructSharp | Error | Layout file not found
CSG003 | CStructSharp | Error | Generated name collision
CSG004 | CStructSharp | Warning | Unknown root declaration
CSG005 | CStructSharp | Error | Attributed class must be partial
CSG006 | CStructSharp | Error | Custom codec declaration is invalid
CSG010 | CStructSharp | Error | C# 12 or later is required
CSG100 | CStructSharp | Error | Mapped type must be partial with a parameterless constructor
CSG101 | CStructSharp | Error | Mapped member type is not mapped
CSG102 | CStructSharp | Warning | Mapped member has no layout counterpart
CSG200 | CStructSharp | Warning | Path does not resolve against the layout
CSG201 | CStructSharp | Info | Parse selects a root that is not a struct
CSG300 | CStructSharp | Warning | dynamic over a parsed value in a trimmed or AOT-published project
