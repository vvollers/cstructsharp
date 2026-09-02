[CmdletBinding()]
param(
    [string]$PolicyPath = (Join-Path $PSScriptRoot '..\CStructSharp.Docs\contracts\performance\non-web-rc1.json'),

    [string]$BenchmarkSummaryPath,

    [string]$PackageArtifactPath,

    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Condition {
    param(
        [Parameter(Mandatory)]
        [bool]$Condition,

        [Parameter(Mandatory)]
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-CaseKey {
    param(
        [Parameter(Mandatory)]
        [object]$Case
    )

    return "$([string]$Case.type)|$([string]$Case.method)|$([string]$Case.parameters)"
}

function Get-BenchmarkFailures {
    param(
        [Parameter(Mandatory)]
        [object]$Summary,

        [Parameter(Mandatory)]
        [object]$BenchmarkPolicy
    )

    $failures = [System.Collections.Generic.List[string]]::new()
    if ($Summary.schemaVersion -ne 1) {
        $failures.Add('Benchmark summary has an unsupported schema version.')
        return ,$failures
    }

    $runtimeVersion = [string]$Summary.hostEnvironment.RuntimeVersion
    if (-not $runtimeVersion.StartsWith(
        [string]$BenchmarkPolicy.runtimePrefix,
        [StringComparison]::Ordinal)) {
        $failures.Add(
            "Benchmark runtime '$runtimeVersion' does not match '$($BenchmarkPolicy.runtimePrefix)'.")
    }

    $currentCases = @($Summary.benchmarks)
    $currentByKey = @{}
    foreach ($current in $currentCases) {
        $key = Get-CaseKey $current
        if ($currentByKey.ContainsKey($key)) {
            $failures.Add("Benchmark summary contains duplicate case '$key'.")
        }
        else {
            $currentByKey[$key] = $current
        }
    }

    $policyKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($expected in @($BenchmarkPolicy.cases)) {
        $key = Get-CaseKey $expected
        [void]$policyKeys.Add($key)
        if (-not $currentByKey.ContainsKey($key)) {
            $failures.Add("Benchmark summary is missing required case '$key'.")
            continue
        }

        $current = $currentByKey[$key]
        $minimumSamples = [int]$BenchmarkPolicy.minimumSamples
        if ([int]$current.samples -lt $minimumSamples) {
            $failures.Add(
                "Benchmark '$key' has $($current.samples) samples; at least $minimumSamples are required.")
        }

        $median = [double]$current.medianNanoseconds
        $standardDeviation = [double]$current.standardDeviationNanoseconds
        $baselineMedian = [double]$expected.baselineMedianNanoseconds
        $timingBudget = [Math]::Max(
            $baselineMedian * [double]$BenchmarkPolicy.maximumMedianMultiplier,
            [double]$BenchmarkPolicy.minimumMedianBudgetNanoseconds)
        if ($median -gt $timingBudget) {
            $failures.Add(
                "Benchmark '$key' median $median ns exceeds the noise-aware $timingBudget ns budget.")
        }

        if ($median -le 0 -or
            ($standardDeviation / $median) -gt [double]$BenchmarkPolicy.maximumRelativeStandardDeviation) {
            $failures.Add(
                "Benchmark '$key' is too unstable for gating: median $median ns, standard deviation " +
                "$standardDeviation ns.")
        }

        $allocated = [double]$current.allocatedBytes
        $baselineAllocated = [double]$expected.baselineAllocatedBytes
        $allocationBudget = [Math]::Max(
            $baselineAllocated * (1 + [double]$BenchmarkPolicy.maximumAllocationGrowthRatio),
            $baselineAllocated + [double]$BenchmarkPolicy.minimumAllocationHeadroomBytes)
        if ($allocated -gt $allocationBudget) {
            $failures.Add(
                "Benchmark '$key' allocation $allocated bytes exceeds the $allocationBudget-byte budget.")
        }
    }

    foreach ($key in $currentByKey.Keys) {
        if (-not $policyKeys.Contains([string]$key)) {
            $failures.Add("Benchmark summary contains unreviewed release-gate case '$key'.")
        }
    }

    return ,$failures
}

function Get-PackageFailures {
    param(
        [Parameter(Mandatory)]
        [object]$Artifact,

        [Parameter(Mandatory)]
        [object]$PackagePolicy
    )

    $failures = [System.Collections.Generic.List[string]]::new()
    if ($Artifact.schemaVersion -ne 1) {
        $failures.Add('Package artifact report has an unsupported schema version.')
        return ,$failures
    }

    $packages = $Artifact.packages
    if ($null -eq $packages) {
        $failures.Add('Artifact report has no package measurement.')
        return ,$failures
    }

    if ([int]$packages.files -ne 2) {
        $failures.Add("Package artifact report contains $($packages.files) files; exactly two are required.")
    }

    if ([double]$packages.bytes -gt [double]$PackagePolicy.maximumBytes) {
        $failures.Add(
            "Package pair is $($packages.bytes) bytes; budget is $($PackagePolicy.maximumBytes).")
    }

    if ([double]$packages.gzipBytes -gt [double]$PackagePolicy.maximumGzipBytes) {
        $failures.Add(
            "Compressed package pair is $($packages.gzipBytes) bytes; budget is " +
            "$($PackagePolicy.maximumGzipBytes).")
    }

    $extensions = @($packages.byExtension)
    foreach ($expected in @($PackagePolicy.extensions)) {
        $actual = @($extensions | Where-Object { $_.extension -eq $expected.extension })
        if ($actual.Count -ne 1) {
            $failures.Add("Package artifact report must contain one '$($expected.extension)' summary.")
            continue
        }

        if ([int]$actual[0].files -ne 1) {
            $failures.Add("Package artifact '$($expected.extension)' count must be one.")
        }

        if ([double]$actual[0].bytes -gt [double]$expected.maximumBytes) {
            $failures.Add(
                "$($expected.extension) is $($actual[0].bytes) bytes; budget is $($expected.maximumBytes).")
        }

        if ([double]$actual[0].gzipBytes -gt [double]$expected.maximumGzipBytes) {
            $failures.Add(
                "Compressed $($expected.extension) is $($actual[0].gzipBytes) bytes; budget is " +
                "$($expected.maximumGzipBytes).")
        }
    }

    return ,$failures
}

function Assert-NoFailures {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[string]]$Failures,

        [Parameter(Mandatory)]
        [string]$Context
    )

    if ($Failures.Count -gt 0) {
        throw "$Context failed.`n$([string]::Join("`n", $Failures))"
    }
}

Assert-Condition (Test-Path -LiteralPath $PolicyPath -PathType Leaf) `
    "Non-Web release budget policy '$PolicyPath' does not exist."
$policy = Get-Content -Raw -LiteralPath $PolicyPath | ConvertFrom-Json -Depth 100
Assert-Condition ($policy.schemaVersion -eq 1) 'Unsupported non-Web release budget schema.'
Assert-Condition ($policy.budgetId -eq 'non-web-rc1') 'Unexpected non-Web release budget id.'
Assert-Condition ($policy.status -eq 'enforced') 'Non-Web release budgets are not enforced.'
Assert-Condition ($policy.workItem -eq 'QA-08') 'Non-Web release budgets are not assigned to QA-08.'
Assert-Condition ($policy.benchmark.generator -eq 'BenchmarkDotNet') `
    'The benchmark policy names an unexpected generator.'
Assert-Condition ($policy.benchmark.generatorVersion -eq '0.15.8') `
    'The benchmark policy must pin BenchmarkDotNet 0.15.8.'
Assert-Condition ($policy.benchmark.targetFramework -eq 'net10.0') `
    'The benchmark policy must target net10.0.'
Assert-Condition ($policy.benchmark.job.launchCount -eq 3 -and
                  $policy.benchmark.job.warmupCount -eq 5 -and
                  $policy.benchmark.job.iterationCount -eq 8) `
    'The benchmark release-gate job must retain 3 launches, 5 warmups, and 8 measured iterations.'
Assert-Condition ($policy.benchmark.minimumSamples -ge 16) `
    'The benchmark release gate must require at least 16 retained samples.'
Assert-Condition ($policy.benchmark.maximumMedianMultiplier -ge 1) `
    'The benchmark median multiplier is invalid.'
Assert-Condition ($policy.benchmark.maximumRelativeStandardDeviation -gt 0 -and
                  $policy.benchmark.maximumRelativeStandardDeviation -le 0.5) `
    'The benchmark dispersion limit must be in (0, 0.5].'

$policyCases = @($policy.benchmark.cases)
Assert-Condition ($policyCases.Count -eq 14) 'The non-Web benchmark gate must contain exactly 14 cases.'
$caseKeys = @($policyCases | ForEach-Object { Get-CaseKey $_ })
Assert-Condition ($caseKeys.Count -eq @($caseKeys | Select-Object -Unique).Count) `
    'The non-Web benchmark policy contains duplicate cases.'
foreach ($case in $policyCases) {
    Assert-Condition ([double]$case.baselineMedianNanoseconds -gt 0) `
        "Benchmark policy case '$(Get-CaseKey $case)' has no positive median."
    Assert-Condition ([double]$case.baselineAllocatedBytes -ge 0) `
        "Benchmark policy case '$(Get-CaseKey $case)' has invalid allocation."
}

Assert-Condition ($policy.package.baseline.files -eq 2) `
    'The package baseline must contain exactly two files.'
Assert-Condition ($policy.package.maximumBytes -gt $policy.package.baseline.bytes -and
                  $policy.package.maximumGzipBytes -gt $policy.package.baseline.gzipBytes) `
    'The package pair budgets must exceed the frozen QA-07 baseline.'
$packageExtensions = @($policy.package.extensions)
Assert-Condition ($packageExtensions.Count -eq 2) `
    'The package policy must contain exactly .nupkg and .snupkg budgets.'
Assert-Condition ((($packageExtensions.extension | Sort-Object) -join ',') -eq '.nupkg,.snupkg') `
    'The package policy must contain exactly .nupkg and .snupkg budgets.'
Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$policy.webIntegration)) `
    'The non-Web release budget policy has no final Web/WASM boundary.'

if ($SelfTest) {
    $syntheticSummary = [pscustomobject]@{
        schemaVersion = 1
        hostEnvironment = [pscustomobject]@{ RuntimeVersion = $policy.benchmark.runtimePrefix + '.self-test' }
        benchmarks = @(
            $policyCases | ForEach-Object {
                [pscustomobject]@{
                    type = $_.type
                    method = $_.method
                    parameters = $_.parameters
                    samples = $policy.benchmark.minimumSamples
                    medianNanoseconds = $_.baselineMedianNanoseconds
                    standardDeviationNanoseconds = 0
                    allocatedBytes = $_.baselineAllocatedBytes
                }
            }
        )
    }
    Assert-NoFailures (Get-BenchmarkFailures $syntheticSummary $policy.benchmark) 'Benchmark self-test baseline'

    $timingRegression = $syntheticSummary | ConvertTo-Json -Depth 20 | ConvertFrom-Json -Depth 20
    $timingRegression.benchmarks[0].medianNanoseconds = 1e15
    Assert-Condition ((Get-BenchmarkFailures $timingRegression $policy.benchmark).Count -gt 0) `
        'Benchmark self-test did not reject a timing regression.'

    $allocationRegression = $syntheticSummary | ConvertTo-Json -Depth 20 | ConvertFrom-Json -Depth 20
    $allocationRegression.benchmarks[0].allocatedBytes = 1e15
    Assert-Condition ((Get-BenchmarkFailures $allocationRegression $policy.benchmark).Count -gt 0) `
        'Benchmark self-test did not reject an allocation regression.'

    $missingCase = $syntheticSummary | ConvertTo-Json -Depth 20 | ConvertFrom-Json -Depth 20
    $missingCase.benchmarks = @($missingCase.benchmarks | Select-Object -Skip 1)
    Assert-Condition ((Get-BenchmarkFailures $missingCase $policy.benchmark).Count -gt 0) `
        'Benchmark self-test did not reject a missing case.'

    $syntheticPackage = [pscustomobject]@{
        schemaVersion = 1
        packages = [pscustomobject]@{
            files = 2
            bytes = $policy.package.baseline.bytes
            gzipBytes = $policy.package.baseline.gzipBytes
            byExtension = @(
                $packageExtensions | ForEach-Object {
                    [pscustomobject]@{
                        extension = $_.extension
                        files = 1
                        bytes = $_.baselineBytes
                        gzipBytes = $_.baselineGzipBytes
                    }
                }
            )
        }
    }
    Assert-NoFailures (Get-PackageFailures $syntheticPackage $policy.package) 'Package self-test baseline'

    $packageRegression = $syntheticPackage | ConvertTo-Json -Depth 20 | ConvertFrom-Json -Depth 20
    $packageRegression.packages.bytes = 1e15
    Assert-Condition ((Get-PackageFailures $packageRegression $policy.package).Count -gt 0) `
        'Package self-test did not reject an artifact regression.'

    Write-Output 'Non-Web release budget self-test passed.'
    Write-Output 'Negative checks: timing, allocation, missing case, package size.'
}

if (-not [string]::IsNullOrWhiteSpace($BenchmarkSummaryPath)) {
    Assert-Condition (Test-Path -LiteralPath $BenchmarkSummaryPath -PathType Leaf) `
        "Benchmark summary '$BenchmarkSummaryPath' does not exist."
    $summary = Get-Content -Raw -LiteralPath $BenchmarkSummaryPath | ConvertFrom-Json -Depth 100
    Assert-NoFailures (Get-BenchmarkFailures $summary $policy.benchmark) 'Non-Web benchmark release gate'

    $timingUse = @()
    $allocationUse = @()
    foreach ($case in @($summary.benchmarks)) {
        $expected = $policyCases | Where-Object { (Get-CaseKey $_) -eq (Get-CaseKey $case) }
        $timingBudget = [Math]::Max(
            [double]$expected.baselineMedianNanoseconds *
                [double]$policy.benchmark.maximumMedianMultiplier,
            [double]$policy.benchmark.minimumMedianBudgetNanoseconds)
        $allocationBudget = [Math]::Max(
            [double]$expected.baselineAllocatedBytes *
                (1 + [double]$policy.benchmark.maximumAllocationGrowthRatio),
            [double]$expected.baselineAllocatedBytes +
                [double]$policy.benchmark.minimumAllocationHeadroomBytes)
        $timingUse += [double]$case.medianNanoseconds / $timingBudget
        $allocationUse += [double]$case.allocatedBytes / $allocationBudget
    }

    Write-Output 'Non-Web benchmark release gate passed.'
    Write-Output "Cases: $(@($summary.benchmarks).Count)"
    Write-Output "Minimum samples: $($policy.benchmark.minimumSamples)"
    Write-Output ('Maximum timing-budget use: {0:P2}' -f ($timingUse | Measure-Object -Maximum).Maximum)
    Write-Output ('Maximum allocation-budget use: {0:P2}' -f ($allocationUse | Measure-Object -Maximum).Maximum)
}

if (-not [string]::IsNullOrWhiteSpace($PackageArtifactPath)) {
    Assert-Condition (Test-Path -LiteralPath $PackageArtifactPath -PathType Leaf) `
        "Package artifact report '$PackageArtifactPath' does not exist."
    $artifact = Get-Content -Raw -LiteralPath $PackageArtifactPath | ConvertFrom-Json -Depth 100
    Assert-NoFailures (Get-PackageFailures $artifact $policy.package) 'Non-Web package artifact gate'
    Write-Output 'Non-Web package artifact gate passed.'
    Write-Output "Files: $($artifact.packages.files)"
    Write-Output "Raw bytes: $($artifact.packages.bytes) / $($policy.package.maximumBytes)"
    Write-Output "Gzip-equivalent bytes: $($artifact.packages.gzipBytes) / $($policy.package.maximumGzipBytes)"
}

if (-not $SelfTest -and
    [string]::IsNullOrWhiteSpace($BenchmarkSummaryPath) -and
    [string]::IsNullOrWhiteSpace($PackageArtifactPath)) {
    throw 'Specify -SelfTest, -BenchmarkSummaryPath, or -PackageArtifactPath.'
}
