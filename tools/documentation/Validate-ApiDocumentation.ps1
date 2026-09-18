[CmdletBinding()]
param(
    [string]$ApiDirectory = (Join-Path $PSScriptRoot '../../docs/api'),
    [string]$BaselinePath =
        (Join-Path $PSScriptRoot '../../contracts/api/managed-rc1/CStructSharp.public-api.txt'),
    [string]$DocfxConfigPath = (Join-Path $PSScriptRoot '../../docs/docfx.json'),
    [string]$SiteApiDirectory = (Join-Path $PSScriptRoot '../../docs/_site/api'),
    [string]$SearchIndexPath = (Join-Path $PSScriptRoot '../../docs/_site/index.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../CStructSharp.Tooling.psm1') -Force

Assert-Condition (Test-Path -LiteralPath $ApiDirectory -PathType Container) 'Generated API metadata is missing.'
Assert-Condition (Test-Path -LiteralPath $BaselinePath -PathType Leaf) 'Managed API baseline is missing.'
Assert-Condition (Test-Path -LiteralPath $DocfxConfigPath -PathType Leaf) 'DocFX configuration is missing.'
Assert-Condition (Test-Path -LiteralPath $SiteApiDirectory -PathType Container) 'Built API pages are missing.'
Assert-Condition (Test-Path -LiteralPath $SearchIndexPath -PathType Leaf) 'Built search index is missing.'

$baseline = Get-Content -LiteralPath $BaselinePath -Raw
# Track namespaces and declaring types so every public type maps to its complete DocFX UID.
$typeNames = [System.Collections.Generic.List[string]]::new()
$namespaceNames = [System.Collections.Generic.List[string]]::new()
$namespace = $null
$declaringType = $null
foreach ($line in $baseline -split '\r?\n') {
    if ($line -match '^namespace (.+)$') {
        $namespace = $Matches[1]
        $namespaceNames.Add($namespace)
    }
    elseif ($line -match '^(?<indent> *)public (?:abstract |sealed |static |readonly )*(?:class|enum|struct|interface) (?<name>[A-Za-z][A-Za-z0-9]*)') {
        $name = $Matches['name']
        if ($Matches['indent'].Length -le 4) {
            $declaringType = "$namespace.$name"
            $typeNames.Add($declaringType)
        }
        else {
            $typeNames.Add("$declaringType.$name")
        }
    }
}
$typeNames = @($typeNames | Sort-Object -Unique)
# The managed API manifest counts the top-level exported types; nested public types (indented deeper) add to it.
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../../contracts/api/managed-rc1/manifest.json') -Raw | ConvertFrom-Json
$nestedTypeCount = @(($baseline -split '\r?\n') | Where-Object { $_ -match '^ {5,}public (?:abstract |sealed |static |readonly )*(?:class|enum|struct|interface) ' }).Count
$expectedTypeCount = [int]$manifest.exportedTypes + $nestedTypeCount
Assert-Condition ($typeNames.Count -eq $expectedTypeCount) "Expected $expectedTypeCount baseline types, found $($typeNames.Count)."

# Generic metadata file names carry arity, for example PrimitiveArray-1.yml.
$missingTypes = @(
    $typeNames |
        Where-Object {
            -not (Test-Path -LiteralPath (Join-Path $ApiDirectory "$_.yml")) -and
            -not (Test-Path -LiteralPath (Join-Path $ApiDirectory "$_-1.yml"))
        }
)
Assert-Condition ($missingTypes.Count -eq 0) (
    "Generated API metadata is missing baseline types: " + [string]::Join(', ', $missingTypes))

# Positional record declarations generate Deconstruct members absent from the API snapshot.
# Read declarations rather than loading the net10.0 assembly into PowerShell's own runtime.
$positionalRecords = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$sourceDirectory = Join-Path $PSScriptRoot '../../src/CStructSharp'
foreach ($sourceFile in Get-ChildItem -LiteralPath $sourceDirectory -Recurse -Filter '*.cs') {
    if ($sourceFile.FullName -match '[/\\](?:obj|bin)[/\\]') { continue }
    $source = Get-Content -LiteralPath $sourceFile.FullName -Raw
    if ($source -match '(?m)^namespace ([A-Za-z0-9_.]+);') {
        $sourceNamespace = $Matches[1]
        foreach ($declaration in [regex]::Matches($source, '\bpublic\s+(?:sealed\s+)?record\s+(?<name>[A-Za-z0-9_]+)\s*\(')) {
            [void]$positionalRecords.Add("$sourceNamespace.$($declaration.Groups['name'].Value)")
        }
    }
}

function Get-RecordSynthesizedUids {
    <#
    .SYNOPSIS
    Returns compiler-generated public and protected record member UIDs omitted from the API snapshot.
    .PARAMETER Baseline
    Public API text for one namespace.
    .PARAMETER Namespace
    Fully qualified namespace containing the declarations.
    .OUTPUTS
    A set of compiler-generated DocFX member UIDs.
    .DESCRIPTION
    Records synthesize equality, formatting, and comparison members. Sealed root records keep PrintMembers and
    EqualityContract private; unsealed records expose those and a protected copy constructor. Derived records
    also expose an Equals overload for their record base. These generated members cannot carry authored XML
    comments, so they are counted separately and excluded from authored-comment checks.
    #>
    param(
        [Parameter(Mandatory)]
        [string]$Baseline,

        [Parameter(Mandatory)]
        [string]$Namespace
    )

    # Record classes and record structs both declare IEquatable<T> in the snapshot; a record struct is `readonly struct`.
    $declarations = @(
        [regex]::Matches(
            $Baseline,
            '(?m)^\s*public\s+(?<sealed>sealed\s+)?(?<readonly>readonly\s+)?(?<kind>class|struct)\s+(?<name>[A-Za-z][A-Za-z0-9]*)' +
                '(?:\s*:\s*(?<bases>[^\r\n{]+))?'))
    $recordNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($declaration in $declarations) {
        if ($declaration.Groups['bases'].Success -and
            $declaration.Groups['bases'].Value -match
                "System\.IEquatable<$([regex]::Escape($Namespace))\.$($declaration.Groups['name'].Value)>") {
            [void]$recordNames.Add($declaration.Groups['name'].Value)
        }
    }

    $uids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($declaration in $declarations) {
        $name = $declaration.Groups['name'].Value
        if (-not $recordNames.Contains($name)) {
            continue
        }

        $qualified = "$Namespace.$name"
        if ($positionalRecords.Contains($qualified)) {
            $typeBody = [regex]::Match($Baseline, '(?ms)^    public [^\r\n]*\b' + [regex]::Escape($name) + '\b[^\r\n]*\r?\n    \{(?<members>.*?)^    \}').Groups['members'].Value
            if ($typeBody -notmatch '\bDeconstruct\(') {
                $metadata = Get-Content -LiteralPath (Join-Path $ApiDirectory "$qualified.yml") -Raw
                $deconstructs = [regex]::Matches($metadata, '(?m)^- uid: (' + [regex]::Escape($qualified) + '\.Deconstruct\([^\r\n]+\))\r?$')
                Assert-Condition ($deconstructs.Count -eq 1) "Expected one generated Deconstruct member for $qualified."
                [void]$uids.Add($deconstructs[0].Groups[1].Value)
            }
        }
        $isStruct = $declaration.Groups['kind'].Value -eq 'struct'
        $structBody = if ($isStruct) {
            [regex]::Match($Baseline, '(?ms)^    public [^\r\n]*\b' + [regex]::Escape($name) + '\b[^\r\n]*\r?\n    \{(?<members>.*?)^    \}').Groups['members'].Value
        } else { '' }
        foreach ($member in @(
            'ToString',
            "op_Inequality($qualified,$qualified)",
            "op_Equality($qualified,$qualified)",
            'GetHashCode',
            'Equals(System.Object)',
            "Equals($qualified)")) {
            # A record struct's explicit override (ToString) already appears in the snapshot as an authored member.
            if ($isStruct -and $member -eq 'ToString' -and $structBody -match '\boverride string ToString\(') { continue }
            [void]$uids.Add("$qualified.$member")
        }

        if ($isStruct) {
            continue
        }

        if (-not $declaration.Groups['sealed'].Success) {
            [void]$uids.Add("$qualified.#ctor($qualified)")
            [void]$uids.Add("$qualified.PrintMembers(System.Text.StringBuilder)")
            [void]$uids.Add("$qualified.EqualityContract")
        }

        if ($declaration.Groups['bases'].Success) {
            $baseTypeMatch = [regex]::Match($declaration.Groups['bases'].Value, ("^\s*" + [regex]::Escape($Namespace) + "\.([A-Za-z][A-Za-z0-9]*)"))
            if ($baseTypeMatch.Success -and $recordNames.Contains($baseTypeMatch.Groups[1].Value)) {
                [void]$uids.Add("$qualified.Equals($Namespace.$($baseTypeMatch.Groups[1].Value))")
                # Sealed derived records still override their base's protected record members.
                [void]$uids.Add("$qualified.PrintMembers(System.Text.StringBuilder)")
                [void]$uids.Add("$qualified.EqualityContract")
            }
        }
    }

    return ,$uids
}

$recordSynthesizedUids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($block in [regex]::Split($baseline, '(?m)(?=^namespace )')) {
    if ($block -match '^namespace ([^\r\n]+)') {
        $recordSynthesizedUids.UnionWith((Get-RecordSynthesizedUids -Baseline $block -Namespace $Matches[1]))
    }
}
$expectedUidCount =
    $namespaceNames.Count +
    @($baseline -split '\r?\n' | Where-Object { $_ -match '^\s*(?:public|protected) ' }).Count +
    @($baseline -split '\r?\n' |
        Where-Object { $_ -match '^\s{8}[A-Za-z][A-Za-z0-9]* = -?\d+,' }).Count +
    # Interface members carry no access modifier (ICustomCodec).
    @($baseline -split '\r?\n' |
        Where-Object { $_ -match '^\s{8}(?!public |protected )[A-Za-z][\w?<>., \[\]]* [A-Za-z]\w*(?: \{ get; \}|\(.*\);)$' }).Count +
    $recordSynthesizedUids.Count

$uids = [Collections.Generic.List[string]]::new()
$missingSummaries = [Collections.Generic.List[string]]::new()
$missingParameters = [Collections.Generic.List[string]]::new()
$missingTypeParameters = [Collections.Generic.List[string]]::new()
$missingReturns = [Collections.Generic.List[string]]::new()
$missingExceptionDescriptions = [Collections.Generic.List[string]]::new()
$placeholderContent = [Collections.Generic.List[string]]::new()
$parameterCount = 0
$parameterDescriptionCount = 0
$typeParameterCount = 0
$typeParameterDescriptionCount = 0
$returnCount = 0
$returnDescriptionCount = 0
$exceptionCount = 0
$primaryItems = @{}
foreach ($file in Get-ChildItem -LiteralPath $ApiDirectory -File -Filter '*.yml' |
             Where-Object { $_.Name -ne 'toc.yml' })
{
    $contents = Get-Content -LiteralPath $file.FullName -Raw
    $referencesIndex = $contents.IndexOf("references:`n", [StringComparison]::Ordinal)
    if ($referencesIndex -lt 0)
    {
        $referencesIndex = $contents.IndexOf("references:`r`n", [StringComparison]::Ordinal)
    }

    if ($referencesIndex -ge 0)
    {
        $contents = $contents.Substring(0, $referencesIndex)
    }

    foreach ($item in [regex]::Split($contents, '(?m)(?=^- uid: )'))
    {
        $uidMatch = [regex]::Match($item, '(?m)^- uid: (?<uid>.+?)\r?$')
        if (-not $uidMatch.Success)
        {
            continue
        }

        $uid = $uidMatch.Groups['uid'].Value.Trim()
        $uids.Add($uid)
        $primaryItems[$uid] = $item
        if ($recordSynthesizedUids.Contains($uid))
        {
            continue
        }

        if ($item -notmatch '(?m)^  summary:')
        {
            $missingSummaries.Add($uid)
        }
        elseif ($item -match '(?im)^  summary:\s*(?:TODO|TBD|Gets the value\.?|Sets the value\.?)\s*\r?$')
        {
            $placeholderContent.Add("$uid :: summary")
        }

        $parameters = [regex]::Match(
            $item,
            '(?ms)^    parameters:\r?\n(?<body>.*?)(?=^    (?:typeParameters:|return:|content\.vb:)|^  [A-Za-z]|\z)')
        if ($parameters.Success)
        {
            foreach ($entry in [regex]::Split($parameters.Groups['body'].Value, '(?m)(?=^    - id: )'))
            {
                $idMatch = [regex]::Match($entry, '(?m)^    - id: (?<id>.+?)\r?$')
                if (-not $idMatch.Success)
                {
                    continue
                }

                $parameterCount++
                $id = $idMatch.Groups['id'].Value.Trim()
                if ($entry -match '(?m)^      description:\s*(?<text>.*?)\r?$' -and
                    -not [string]::IsNullOrWhiteSpace($Matches['text']))
                {
                    $parameterDescriptionCount++
                    if ($Matches['text'] -match '(?i)^(?:TODO|TBD|The value\.?)$')
                    {
                        $placeholderContent.Add("$uid :: parameter $id")
                    }
                }
                else
                {
                    $missingParameters.Add("$uid :: $id")
                }
            }
        }

        $typeParameters = [regex]::Match(
            $item,
            '(?ms)^    typeParameters:\r?\n(?<body>.*?)(?=^    (?:parameters:|return:|content\.vb:)|^  [A-Za-z]|\z)')
        if ($typeParameters.Success)
        {
            foreach ($entry in [regex]::Split($typeParameters.Groups['body'].Value, '(?m)(?=^    - id: )'))
            {
                $idMatch = [regex]::Match($entry, '(?m)^    - id: (?<id>.+?)\r?$')
                if (-not $idMatch.Success)
                {
                    continue
                }

                $typeParameterCount++
                $id = $idMatch.Groups['id'].Value.Trim()
                if ($entry -match '(?m)^      description:\s*(?<text>.*?)\r?$' -and
                    -not [string]::IsNullOrWhiteSpace($Matches['text']))
                {
                    $typeParameterDescriptionCount++
                }
                else
                {
                    $missingTypeParameters.Add("$uid :: $id")
                }
            }
        }

        if ($item -match '(?m)^  type: Method\r?$' -and
            $item -match '(?m)^    content: (?!(?:public (?:static |readonly |override |virtual |sealed )*)?void )')
        {
            $returnCount++
            $return = [regex]::Match(
                $item,
                '(?ms)^    return:\r?\n(?<body>.*?)(?=^    content\.vb:|^  [A-Za-z]|\z)')
            if ($return.Success -and
                $return.Groups['body'].Value -match '(?m)^      description:\s*(?<text>.*?)\r?$' -and
                -not [string]::IsNullOrWhiteSpace($Matches['text']))
            {
                $returnDescriptionCount++
            }
            else
            {
                $missingReturns.Add($uid)
            }
        }

        $exceptions = [regex]::Match(
            $item,
            '(?ms)^  exceptions:\r?\n(?<body>.*?)(?=^  [A-Za-z]|\z)')
        if ($exceptions.Success)
        {
            foreach ($entry in [regex]::Split($exceptions.Groups['body'].Value, '(?m)(?=^  - type: )'))
            {
                $typeMatch = [regex]::Match($entry, '(?m)^  - type: (?<type>.+?)\r?$')
                if (-not $typeMatch.Success)
                {
                    continue
                }

                $exceptionCount++
                if ($entry -notmatch '(?m)^    description:\s*\S')
                {
                    $missingExceptionDescriptions.Add(
                        "$uid :: $($typeMatch.Groups['type'].Value.Trim())")
                }
            }
        }
    }
}

Assert-Condition ($uids.Count -eq $expectedUidCount) (
    "The baseline implies $expectedUidCount public UIDs, but DocFX generated $($uids.Count).")
Assert-Condition ($uids.Count -eq @($uids | Sort-Object -Unique).Count) 'Generated API UIDs are not unique.'
$missingPublicSummaries = @($missingSummaries | Where-Object { $_ -notin $namespaceNames })
Assert-Condition ($missingPublicSummaries.Count -eq 0) (
    "Generated public API items lack summaries:`n" + [string]::Join("`n", $missingPublicSummaries))
Assert-Condition ($missingParameters.Count -eq 0) (
    "Generated API parameters lack descriptions:`n" + [string]::Join("`n", $missingParameters))
Assert-Condition ($missingTypeParameters.Count -eq 0) (
    "Generated API type parameters lack descriptions:`n" + [string]::Join("`n", $missingTypeParameters))
Assert-Condition ($missingReturns.Count -eq 0) (
    "Generated API return values lack descriptions:`n" + [string]::Join("`n", $missingReturns))
Assert-Condition ($missingExceptionDescriptions.Count -eq 0) (
    "Generated API exception references lack descriptions:`n" +
    [string]::Join("`n", $missingExceptionDescriptions))
Assert-Condition ($placeholderContent.Count -eq 0) (
    "Generated API content contains placeholders or tautologies:`n" +
    [string]::Join("`n", $placeholderContent))

$docfxConfig = Get-Content -LiteralPath $DocfxConfigPath -Raw | ConvertFrom-Json
Assert-Condition ($docfxConfig.metadata.Count -eq 1) 'DocFX must use one explicit library metadata source.'
Assert-Condition ($docfxConfig.metadata[0].memberLayout -eq 'samePage') (
    "The reviewed overload layout must remain 'samePage'.")

$testedComplexModels = [ordered]@{
    'CStructSharp.CStruct' = 'DecodeHeader'
    'CStructSharp.Diagnostics.DebugData' = 'InspectRanges'
    'CStructSharp.Values.EnumValueResult' = 'PreserveEnum'
    'CStructSharp.Values.Pointer' = 'FollowPointer'
    'CStructSharp.ReadOptions' = 'FollowPointer'
    'CStructSharp.Values.UnionValue' = 'PreserveUnion'
    'CStructSharp.WriteOptions' = 'RoundTrip'
    'CStructSharp.UpdateOptions' = 'PatchField'
}
foreach ($entry in $testedComplexModels.GetEnumerator())
{
    Assert-Condition ($primaryItems.ContainsKey($entry.Key)) "Complex public model is absent: $($entry.Key)"
    Assert-Condition ($primaryItems[$entry.Key] -match '(?m)^  remarks:\s*\S') (
        "Complex public model lacks contract remarks: $($entry.Key)")

    $pagePath = Join-Path $SiteApiDirectory ($entry.Key + '.html')
    Assert-Condition (Test-Path -LiteralPath $pagePath -PathType Leaf) (
        "Complex public model page is absent: $($entry.Key)")
    $page = Get-Content -LiteralPath $pagePath -Raw
    Assert-Condition ($page.Contains('>Remarks<', [StringComparison]::Ordinal)) (
        "Complex public model page does not render remarks: $($entry.Key)")
    Assert-Condition ($page.Contains('compiled and executed', [StringComparison]::OrdinalIgnoreCase)) (
        "Complex public model page does not identify its executable example: $($entry.Key)")
    Assert-Condition ($page.Contains($entry.Value, [StringComparison]::Ordinal)) (
        "Complex public model page does not render the tested '$($entry.Value)' scenario: $($entry.Key)")
}

$apiTocPath = Join-Path $SiteApiDirectory 'toc.html'
Assert-Condition (Test-Path -LiteralPath $apiTocPath -PathType Leaf) 'Built API TOC is missing.'
$apiToc = Get-Content -LiteralPath $apiTocPath -Raw
foreach ($typeName in $typeNames)
{
    Assert-Condition (
        $apiToc.Contains("$typeName.html", [StringComparison]::Ordinal) -or
        $apiToc.Contains("$typeName-1.html", [StringComparison]::Ordinal)) (
        "Built API TOC does not link the baseline type $typeName.")
}

$searchIndex = Get-Content -LiteralPath $SearchIndexPath -Raw
foreach ($searchEvidence in @(
        '"api/CStructSharp.CStruct.html"',
        'TryReadValue',
        'CStructReadException',
        'UnionValue'))
{
    Assert-Condition ($searchIndex.Contains($searchEvidence, [StringComparison]::Ordinal)) (
        "Local search index lacks API evidence '$searchEvidence'.")
}

Write-Output 'API documentation validation passed.'
Write-Output "Baseline types: $($typeNames.Count)"
Write-Output "Primary UIDs: $($uids.Count)"
Write-Output "Parameters: $parameterCount/$parameterDescriptionCount documented"
Write-Output "Type parameters: $typeParameterCount/$typeParameterDescriptionCount documented"
Write-Output "Return values: $returnCount/$returnDescriptionCount documented"
Write-Output "Documented exceptions: $exceptionCount"
Write-Output "Overload layout: $($docfxConfig.metadata[0].memberLayout)"
Write-Output "Tested complex-model pages: $($testedComplexModels.Count)"
Write-Output "API TOC baseline links: $($typeNames.Count)"
