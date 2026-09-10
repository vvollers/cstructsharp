[CmdletBinding()]
param(
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../CStructSharp.Tooling.psm1') -Force

$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$WorkflowPath = Join-Path $RepositoryRoot '.github/workflows/docs.yml'
$ContractPath = Join-Path $RepositoryRoot 'contracts/documentation/pages-v1.json'

function Get-WorkflowRuleCodes {
    param(
        [string]$Text,
        [object]$Contract
    )

    $codes = [Collections.Generic.List[string]]::new()
    if ($Text -match '(?i)(?:CStructSharpWeb|apps/workshop|src/CStructSharp\.Wasm)')
    {
        $codes.Add('web-target')
    }
    if ($Text -match 'uses:\s+[^@\s]+@v\d+')
    {
        $codes.Add('moving-action-tag')
    }

    $uses = @([regex]::Matches($Text, '(?m)^\s*uses:\s+(?<name>[^@\s]+)@(?<ref>[^\s#]+)'))
    foreach ($use in $uses)
    {
        if ($use.Groups['ref'].Value -notmatch '^[0-9a-f]{40}$')
        {
            $codes.Add('non-immutable-action')
        }
    }
    foreach ($property in $Contract.actions.PSObject.Properties)
    {
        $expected = "uses: $($property.Name)@$($property.Value)"
        if (-not $Text.Contains($expected, [StringComparison]::Ordinal))
        {
            $codes.Add("missing-action:$($property.Name)")
        }
    }

    $buildMatch = [regex]::Match($Text, '(?ms)^  build:\r?\n(?<body>.*)\z')
    $deployMatch = [regex]::Match($Text, '(?ms)^  deploy:\r?\n(?<body>.*)\z')
    if (-not $buildMatch.Success -or
        -not $buildMatch.Groups['body'].Value.Contains('contents: read', [StringComparison]::Ordinal) -or
        $buildMatch.Groups['body'].Value.Contains('pages: write', [StringComparison]::Ordinal) -or
        $buildMatch.Groups['body'].Value.Contains('id-token: write', [StringComparison]::Ordinal))
    {
        $codes.Add('build-permissions')
    }
    if ($deployMatch.Success -or $Text.Contains('pages: write') -or $Text.Contains('id-token: write'))
    {
        $codes.Add('deploy-boundary')
    }

    foreach ($required in @(
        'push:',
        'pull_request:',
        'workflow_dispatch:',
        './tools/documentation/Validate-Documentation.ps1',
        'docs/_site/',
        'docs/_site',
        'actions/upload-artifact@'))
    {
        if (-not $Text.Contains($required, [StringComparison]::Ordinal))
        {
            $codes.Add("missing-boundary:$required")
        }
    }

    return $codes
}

Assert-Condition (Test-Path -LiteralPath $ContractPath -PathType Leaf) `
    "Pages contract does not exist: $ContractPath"
$contract = Get-Content -LiteralPath $ContractPath -Raw | ConvertFrom-Json -Depth 20
Assert-Condition ($contract.schemaVersion -eq 1) "Unsupported Pages contract schema '$($contract.schemaVersion)'."

if ($SelfTest)
{
    Assert-Condition (Test-Path -LiteralPath $WorkflowPath -PathType Leaf) `
        "Documentation workflow does not exist: $WorkflowPath"
    $validText = Get-Content -LiteralPath $WorkflowPath -Raw
    $invalidText = $validText.
        Replace(
            "actions/checkout@$($contract.actions.'actions/checkout')",
            'actions/checkout@v6').
        Replace(
            './tools/documentation/Validate-Documentation.ps1',
            'missing-validator.ps1') +
        "`n# apps/workshop"
    $codes = @(Get-WorkflowRuleCodes -Text $invalidText -Contract $contract)
    foreach ($expected in @('web-target', 'moving-action-tag', 'non-immutable-action'))
    {
        Assert-Condition ($codes -contains $expected) `
            "Documentation workflow fail-first fixture did not trigger '$expected'."
    }
    Assert-Condition (@($codes | Where-Object { $_ -like 'missing-action:*' }).Count -gt 0) `
        'Documentation workflow fail-first fixture did not reject the replaced action pin.'
    Assert-Condition (@($codes | Where-Object { $_ -like 'missing-boundary:*' }).Count -gt 0) `
        'Documentation workflow fail-first fixture did not reject the removed deployment boundary.'
    Write-Host 'Documentation workflow self-test passed: moving pin, Web target, and deploy-boundary defects rejected.'
    return
}

Assert-Condition (Test-Path -LiteralPath $WorkflowPath -PathType Leaf) `
    "Documentation workflow does not exist: $WorkflowPath"
$workflowText = Get-Content -LiteralPath $WorkflowPath -Raw
$errors = @(Get-WorkflowRuleCodes -Text $workflowText -Contract $contract)
Assert-Condition ($errors.Count -eq 0) (
    "Documentation workflow validation failed:`n" +
    [string]::Join("`n", $errors))

Write-Host (
    'Documentation workflow validation passed: {0} immutable actions, read-only build, no deployment permissions.' -f
    @($contract.actions.PSObject.Properties).Count
)
