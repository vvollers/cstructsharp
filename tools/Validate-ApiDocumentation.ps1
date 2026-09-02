[CmdletBinding()]
param(
    [string]$ApiDirectory = (Join-Path $PSScriptRoot '..\CStructSharp.Docs\api'),
    [string]$BaselinePath =
        (Join-Path $PSScriptRoot '..\CStructSharp.Docs\contracts\api\managed-rc1\CStructSharp.public-api.txt'),
    [string]$DocfxConfigPath = (Join-Path $PSScriptRoot '..\CStructSharp.Docs\docfx.json'),
    [string]$SiteApiDirectory = (Join-Path $PSScriptRoot '..\CStructSharp.Docs\_site\api'),
    [string]$SearchIndexPath = (Join-Path $PSScriptRoot '..\CStructSharp.Docs\_site\index.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Condition([bool]$Condition, [string]$Message)
{
    if (-not $Condition)
    {
        throw $Message
    }
}

Assert-Condition (Test-Path -LiteralPath $ApiDirectory -PathType Container) 'Generated API metadata is missing.'
Assert-Condition (Test-Path -LiteralPath $BaselinePath -PathType Leaf) 'Managed API baseline is missing.'
Assert-Condition (Test-Path -LiteralPath $DocfxConfigPath -PathType Leaf) 'DocFX configuration is missing.'
Assert-Condition (Test-Path -LiteralPath $SiteApiDirectory -PathType Container) 'Built API pages are missing.'
Assert-Condition (Test-Path -LiteralPath $SearchIndexPath -PathType Leaf) 'Built search index is missing.'

$baseline = Get-Content -LiteralPath $BaselinePath -Raw
$typeNames = @(
    [regex]::Matches(
        $baseline,
        '(?m)^\s*public (?:abstract |sealed |static )?(?:class|enum|struct) (?<name>[A-Za-z][A-Za-z0-9]*)') |
        ForEach-Object { $_.Groups['name'].Value } |
        Sort-Object -Unique
)
Assert-Condition ($typeNames.Count -eq 20) "Expected 20 baseline types, found $($typeNames.Count)."

$missingTypes = @(
    $typeNames |
        Where-Object { -not (Test-Path -LiteralPath (Join-Path $ApiDirectory "CStructSharp.$_.yml")) }
)
Assert-Condition ($missingTypes.Count -eq 0) (
    "Generated API metadata is missing baseline types: " + [string]::Join(', ', $missingTypes))

$expectedUidCount =
    1 +
    @($baseline -split '\r?\n' | Where-Object { $_ -match '^\s*(?:public|protected) ' }).Count +
    @($baseline -split '\r?\n' |
        Where-Object { $_ -match '^\s{8}[A-Za-z][A-Za-z0-9]* = -?\d+,' }).Count

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
            $item -match '(?m)^    content: (?!public void )')
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
$missingPublicSummaries = @($missingSummaries | Where-Object { $_ -ne 'CStructSharp' })
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
Assert-Condition ($docfxConfig.metadata.Count -eq 1) 'DocFX must use one explicit core-only metadata source.'
Assert-Condition ($docfxConfig.metadata[0].memberLayout -eq 'samePage') (
    "The reviewed overload layout must remain 'samePage'.")

$testedComplexModels = [ordered]@{
    'CStructSharp.CStruct' = 'DecodeHeader'
    'CStructSharp.DebugData' = 'InspectRanges'
    'CStructSharp.EnumValueResult' = 'PreserveEnum'
    'CStructSharp.Pointer' = 'FollowPointer'
    'CStructSharp.ReadOptions' = 'FollowPointer'
    'CStructSharp.UnionValue' = 'PreserveUnion'
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
        $apiToc.Contains("CStructSharp.$typeName.html", [StringComparison]::Ordinal)) (
        "Built API TOC does not link the baseline type CStructSharp.$typeName.")
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
