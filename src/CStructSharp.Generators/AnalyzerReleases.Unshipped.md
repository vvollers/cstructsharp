; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CSG011 | CStructSharp | Error | The Views surface needs views
CSG012 | CStructSharp | Warning | A CStructSharp project property is not valid
CSG013 | CStructSharp | Error | RuntimeByteOrder is not supported here
CSG014 | CStructSharp | Error | A value-type shape is not supported here
CSG015 | CStructSharp | Error | A scalar shape is not supported here
CSG016 | CStructSharp | Info | A generated layout is never used
CSG017 | CStructSharp | Info | A generated layout generates more than its uses need
CSG018 | CStructSharp | Warning | The conditions on a member can never hold
CSG019 | CStructSharp | Info | A constraint label names different conditions
CSG020 | CStructSharp | Warning | A magic follows a variable-size member
CSG021 | CStructSharp | Info | A layout qualifies for the scalar shape
CSG022 | CStructSharp | Info | A layout qualifies for value-type shapes
