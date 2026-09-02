[CmdletBinding()]
param(
    [string]$MatrixPath = (Join-Path $PSScriptRoot '..\CStructSharp.Docs\contracts\quality\feature-operation-matrix.json'),
    [string]$ManualFixturePath = (Join-Path $PSScriptRoot '..\CStructSharp.Docs\contracts\language\manual-fixtures-v1.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$resolvedMatrix = (Resolve-Path -LiteralPath $MatrixPath).Path
$matrix = Get-Content -Raw -LiteralPath $resolvedMatrix | ConvertFrom-Json -Depth 100
$manualFixtures = Get-Content -Raw -LiteralPath $ManualFixturePath | ConvertFrom-Json -Depth 100

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

function Assert-UniqueIds {
    param(
        [Parameter(Mandatory)]
        [object[]]$Items,

        [Parameter(Mandatory)]
        [string]$CollectionName
    )

    $ids = @($Items | ForEach-Object { [string]$_.id })
    Assert-Condition ($ids.Count -eq @($ids | Select-Object -Unique).Count) `
        "$CollectionName contains duplicate ids."
    foreach ($id in $ids) {
        Assert-Condition ($id -match '^[a-z0-9]+(?:-[a-z0-9]+)*$') `
            "$CollectionName id '$id' must be lowercase kebab-case."
    }
}

Assert-Condition ($matrix.schemaVersion -eq 2) 'Unsupported feature-operation matrix schema version.'
Assert-Condition ($manualFixtures.schemaVersion -eq 1) 'Unsupported manual-fixture schema version.'

$manualFixtureById = @{}
foreach ($pair in @($manualFixtures.featurePairs))
{
    $id = [string]$pair.id
    Assert-Condition (-not $manualFixtureById.ContainsKey($id)) "Duplicate manual fixture id '$id'."
    $manualFixtureById[$id] = $pair
}

$operationIds = @($matrix.operations | ForEach-Object { [string]$_.id })
Assert-UniqueIds -Items @($matrix.operations) -CollectionName 'operations'
Assert-Condition ($operationIds.Count -gt 0) 'The matrix must define at least one operation.'

$allowedStatuses = @($matrix.statuses.PSObject.Properties.Name)
Assert-Condition ((($allowedStatuses | Sort-Object) -join ',') -eq
                  ((@('blocked', 'limited', 'notApplicable', 'verified') | Sort-Object) -join ',')) `
    'The status vocabulary must be exactly blocked, limited, notApplicable, and verified.'

$dimensionIds = @($matrix.dimensions.PSObject.Properties.Name)
Assert-Condition ($dimensionIds.Count -gt 0) 'The matrix must define dimensions.'
$allowedDimensionValues = @{}
foreach ($dimension in $matrix.dimensions.PSObject.Properties) {
    $values = @($dimension.Value | ForEach-Object { [string]$_ })
    Assert-Condition ($values.Count -gt 0) "Dimension '$($dimension.Name)' has no allowed values."
    Assert-Condition ($values.Count -eq @($values | Select-Object -Unique).Count) `
        "Dimension '$($dimension.Name)' contains duplicate values."
    $allowedDimensionValues[$dimension.Name] = $values
}

function Assert-WorkItems {
    param(
        [object[]]$WorkItems,
        [string]$Context,
        [bool]$Required = $false
    )

    $items = @(
        $WorkItems |
            ForEach-Object { [string]$_ } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    if ($Required) {
        Assert-Condition ($items.Count -gt 0) "$Context must name at least one work item."
    }

    Assert-Condition ($items.Count -eq @($items | Select-Object -Unique).Count) `
        "$Context contains duplicate work-item ids."
    foreach ($item in $items) {
        Assert-Condition ($item -match '^(?:ADR-\d{3}|[A-Z]+-\d{2})$') `
            "$Context contains invalid traceability id '$item'."
        if ($item -match '^ADR-(?<number>\d{3})$') {
            $filePrefix = '{0:D4}-' -f [int]$Matches['number']
            $adrFiles = @(
                Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'CStructSharp.Docs\decisions\product') `
                    -File `
                    -Filter "$filePrefix*.md")
            Assert-Condition ($adrFiles.Count -eq 1) `
                "$Context expected exactly one document for '$item'; found $($adrFiles.Count)."
        }
    }
}

function Assert-EvidenceReference {
    param(
        [Parameter(Mandatory)]
        [string]$Reference,

        [Parameter(Mandatory)]
        [string]$Context
    )

    $parts = $Reference.Split('#', 2)
    Assert-Condition ($parts.Count -eq 2 -and
                      -not [string]::IsNullOrWhiteSpace($parts[0]) -and
                      -not [string]::IsNullOrWhiteSpace($parts[1])) `
        "$Context evidence '$Reference' must use path#test-method format."
    $candidate = Join-Path $repositoryRoot $parts[0]
    Assert-Condition (Test-Path -LiteralPath $candidate -PathType Leaf) `
        "$Context evidence file '$($parts[0])' does not exist."
    $source = Get-Content -Raw -LiteralPath $candidate
    Assert-Condition ($source -match ('\b' + [regex]::Escape($parts[1]) + '\s*\(')) `
        "$Context evidence method '$($parts[1])' was not found in '$($parts[0])'."
}

$allPrimitiveSpellings = @(
    @($matrix.primitiveSpellings.fixed | ForEach-Object { [string]$_ }) +
    @($matrix.primitiveSpellings.terminated | ForEach-Object { [string]$_ })
)
Assert-Condition ($allPrimitiveSpellings.Count -gt 0) 'primitiveSpellings must not be empty.'
Assert-Condition ($allPrimitiveSpellings.Count -eq @($allPrimitiveSpellings | Select-Object -Unique).Count) `
    'primitiveSpellings contains duplicate names.'

Assert-UniqueIds -Items @($matrix.features) -CollectionName 'features'
foreach ($feature in $matrix.features) {
    $context = "Feature '$($feature.id)'"
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$feature.manual)) `
        "$context has no language-manual reference."
    $manualParts = ([string]$feature.manual).Split('#', 2)
    Assert-Condition ($manualParts.Count -eq 2) `
        "$context manual reference must use repository-path#anchor format."
    Assert-Condition (Test-Path -LiteralPath (Join-Path $repositoryRoot $manualParts[0]) -PathType Leaf) `
        "$context manual page '$($manualParts[0])' does not exist."
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$feature.fixture)) `
        "$context has no executable manual fixture."
    Assert-Condition ($manualFixtureById.ContainsKey([string]$feature.fixture)) `
        "$context fixture '$($feature.fixture)' does not exist."
    $manualPair = $manualFixtureById[[string]$feature.fixture]
    Assert-Condition ($manualPair.featureId -eq $feature.id) `
        "$context fixture belongs to '$($manualPair.featureId)'."
    Assert-Condition ($manualPair.manual -eq $feature.manual) `
        "$context manual reference differs from its executable fixture."
    Assert-Condition ($feature.support -in @('supported', 'limited')) `
        "$context has unknown support value '$($feature.support)'."
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$feature.title)) "$context has no title."

    $featureDimensionIds = @($feature.dimensions.PSObject.Properties.Name)
    Assert-Condition ((($featureDimensionIds | Sort-Object) -join ',') -eq
                      (($dimensionIds | Sort-Object) -join ',')) `
        "$context must fill every dimension exactly once."
    foreach ($dimension in $feature.dimensions.PSObject.Properties) {
        $values = @($dimension.Value | ForEach-Object { [string]$_ })
        Assert-Condition ($values.Count -gt 0) "$context dimension '$($dimension.Name)' is empty."
        foreach ($value in $values) {
            Assert-Condition ($value -in $allowedDimensionValues[$dimension.Name]) `
                "$context dimension '$($dimension.Name)' contains unknown value '$value'."
        }
    }

    $featureOperationIds = @($feature.operations.PSObject.Properties.Name)
    Assert-Condition ((($featureOperationIds | Sort-Object) -join ',') -eq
                      (($operationIds | Sort-Object) -join ',')) `
        "$context must classify every public operation exactly once."

    $evidenceCoverage = @{}
    foreach ($operationId in $operationIds) {
        $evidenceCoverage[$operationId] = [System.Collections.Generic.List[string]]::new()
    }
    foreach ($evidence in @($feature.evidence)) {
        $reference = [string]$evidence.test
        Assert-EvidenceReference -Reference $reference -Context $context
        $evidenceOperations = @($evidence.operations | ForEach-Object { [string]$_ })
        Assert-Condition ($evidenceOperations.Count -gt 0) "$context evidence '$reference' covers no operations."
        foreach ($operationId in $evidenceOperations) {
            Assert-Condition ($operationId -in $operationIds) `
                "$context evidence '$reference' names unknown operation '$operationId'."
            $evidenceCoverage[$operationId].Add($reference)
        }
    }

    $requiresWorkItem = $false
    foreach ($operation in $feature.operations.PSObject.Properties) {
        $status = [string]$operation.Value
        Assert-Condition ($status -in $allowedStatuses) `
            "$context operation '$($operation.Name)' has unknown status '$status'."
        if ($status -in @('verified', 'limited')) {
            Assert-Condition ($evidenceCoverage[$operation.Name].Count -gt 0) `
                "$context operation '$($operation.Name)' is $status but has no executable evidence."
        }
        if ($status -eq 'blocked') {
            $requiresWorkItem = $true
        }
    }

    $limitations = @($feature.limitations | ForEach-Object { [string]$_ })
    if ($feature.support -eq 'limited' -or
        @($feature.operations.PSObject.Properties.Value) -contains 'limited') {
        Assert-Condition ($limitations.Count -gt 0) "$context is limited but has no written limitation."
    }
    $featureWorkItems = if ($feature.PSObject.Properties.Name -contains 'workItems') {
        @($feature.workItems)
    }
    else {
        @()
    }
    Assert-WorkItems -WorkItems $featureWorkItems -Context $context -Required:$requiresWorkItem
}

$memoryIo = $matrix.memoryIoContract
Assert-Condition ($null -ne $memoryIo) 'memoryIoContract is required.'
foreach ($property in @('lifetime', 'pointerCoordinates', 'partialFailure', 'webIntegration')) {
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$memoryIo.$property)) `
        "memoryIoContract has no $property."
}
$requiredMemoryInputApis = @(
    'Parse(ReadOnlySpan<byte>)',
    'Parse(ReadOnlyMemory<byte>)',
    'ReadValue(ReadOnlySpan<byte>)',
    'ReadValue(ReadOnlyMemory<byte>)',
    'ReadValue<T>(ReadOnlySpan<byte>)',
    'ReadValue<T>(ReadOnlyMemory<byte>)',
    'TryReadValue<T>(ReadOnlySpan<byte>)',
    'TryReadValue<T>(ReadOnlyMemory<byte>)'
)
$memoryInputApis = @($memoryIo.inputApis | ForEach-Object { [string]$_ })
Assert-Condition ((($memoryInputApis | Sort-Object) -join ',') -eq
                  (($requiredMemoryInputApis | Sort-Object) -join ',')) `
    'memoryIoContract must list the exact span/memory input overload family.'
$requiredMemoryOutputApis = @('Serialize(Span<byte>)', 'Serialize(IBufferWriter<byte>)')
$memoryOutputApis = @($memoryIo.outputApis | ForEach-Object { [string]$_ })
Assert-Condition ((($memoryOutputApis | Sort-Object) -join ',') -eq
                  (($requiredMemoryOutputApis | Sort-Object) -join ',')) `
    'memoryIoContract must list the exact caller-owned output overload family.'
$memoryEvidence = @($memoryIo.evidence | ForEach-Object { [string]$_ })
Assert-Condition ($memoryEvidence.Count -gt 0) 'memoryIoContract has no executable evidence.'
foreach ($reference in $memoryEvidence) {
    Assert-EvidenceReference -Reference $reference -Context 'memoryIoContract'
}
Assert-WorkItems -WorkItems @($memoryIo.workItem) -Context 'memoryIoContract' -Required:$true

$compiledExecution = $matrix.compiledExecutionContract
Assert-Condition ($null -ne $compiledExecution) 'compiledExecutionContract is required.'
foreach ($property in @('structTraversal', 'unionTraversal', 'debug')) {
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$compiledExecution.$property)) `
        "compiledExecutionContract has no $property."
}
$requiredReadRoutes = @(
    'root',
    'nested-field',
    'selected-struct',
    'struct-array-element',
    'pointer-target',
    'union-member'
)
$readRoutes = @($compiledExecution.readRoutes | ForEach-Object { [string]$_ })
Assert-Condition ((($readRoutes | Sort-Object) -join ',') -eq
                  (($requiredReadRoutes | Sort-Object) -join ',')) `
    'compiledExecutionContract must list the exact read route families.'
$requiredWriteRoutes = @(
    'root',
    'nested-field',
    'selected-field',
    'array-element',
    'pointer-value',
    'union-member'
)
$writeRoutes = @($compiledExecution.writeRoutes | ForEach-Object { [string]$_ })
Assert-Condition ((($writeRoutes | Sort-Object) -join ',') -eq
                  (($requiredWriteRoutes | Sort-Object) -join ',')) `
    'compiledExecutionContract must list the exact write route families.'
$compiledEvidence = @($compiledExecution.evidence | ForEach-Object { [string]$_ })
Assert-Condition ($compiledEvidence.Count -gt 0) 'compiledExecutionContract has no executable evidence.'
foreach ($reference in $compiledEvidence) {
    Assert-EvidenceReference -Reference $reference -Context 'compiledExecutionContract'
}
Assert-WorkItems -WorkItems @($compiledExecution.workItem) -Context 'compiledExecutionContract' -Required:$true

$operationContext = $matrix.operationContextContract
Assert-Condition ($null -ne $operationContext) 'operationContextContract is required.'
foreach ($property in @('snapshotBoundary', 'readState', 'selectedState', 'cycleKey')) {
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$operationContext.$property)) `
        "operationContextContract has no $property."
}
$requiredReadLikeRoutes = @(
    'parse',
    'debug',
    'address',
    'length',
    'read-value',
    'update-address'
)
$readLikeRoutes = @($operationContext.readLikeRoutes | ForEach-Object { [string]$_ })
Assert-Condition ((($readLikeRoutes | Sort-Object) -join ',') -eq
                  (($requiredReadLikeRoutes | Sort-Object) -join ',')) `
    'operationContextContract must list the exact read-like route families.'
$operationContextEvidence = @($operationContext.evidence | ForEach-Object { [string]$_ })
Assert-Condition ($operationContextEvidence.Count -gt 0) 'operationContextContract has no executable evidence.'
foreach ($reference in $operationContextEvidence) {
    Assert-EvidenceReference -Reference $reference -Context 'operationContextContract'
}
Assert-WorkItems -WorkItems @($operationContext.workItem) -Context 'operationContextContract' -Required:$true

$managedCompatibility = $matrix.managedApiCompatibilityContract
Assert-Condition ($null -ne $managedCompatibility) 'managedApiCompatibilityContract is required.'
Assert-Condition ($managedCompatibility.baselineId -eq 'managed-rc1') `
    'managedApiCompatibilityContract names an unexpected baseline.'
Assert-Condition ($managedCompatibility.baselineRevision -eq 1) `
    'managedApiCompatibilityContract names an unexpected baseline revision.'
Assert-Condition ($managedCompatibility.status -eq 'frozen') `
    'managedApiCompatibilityContract must be frozen.'
Assert-Condition ($managedCompatibility.packageVersion -eq '0.2.0-preview') `
    'managedApiCompatibilityContract names an unexpected package version.'
Assert-Condition ($managedCompatibility.exportedTypes -eq 20) `
    'managedApiCompatibilityContract must retain the reviewed 20-type surface.'
Assert-Condition ($managedCompatibility.canonicalLines -eq 227) `
    'managedApiCompatibilityContract must retain the reviewed 227-line surface.'
$managedFrameworks = @($managedCompatibility.frameworks | ForEach-Object { [string]$_ })
Assert-Condition ((($managedFrameworks | Sort-Object) -join ',') -eq
                  ((@('net8.0', 'net10.0') | Sort-Object) -join ',')) `
    'managedApiCompatibilityContract must list exactly net8.0 and net10.0.'
foreach ($property in @('manifest', 'canonical', 'gate', 'policy')) {
    $relativePath = [string]$managedCompatibility.$property
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($relativePath)) `
        "managedApiCompatibilityContract has no $property."
    Assert-Condition (Test-Path -LiteralPath (Join-Path $repositoryRoot $relativePath) -PathType Leaf) `
        "managedApiCompatibilityContract $property '$relativePath' does not exist."
}
Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$managedCompatibility.browserIntegration)) `
    'managedApiCompatibilityContract has no browser-integration boundary.'
Assert-WorkItems -WorkItems @($managedCompatibility.workItem) `
    -Context 'managedApiCompatibilityContract' -Required:$true

$browserCompatibility = $matrix.browserApiCompatibilityContract
Assert-Condition ($null -ne $browserCompatibility) 'browserApiCompatibilityContract is required.'
Assert-Condition ($browserCompatibility.baselineId -eq 'browser-rc1') `
    'browserApiCompatibilityContract names an unexpected baseline.'
Assert-Condition ($browserCompatibility.status -eq 'frozen') `
    'browserApiCompatibilityContract must be frozen.'
Assert-Condition ($browserCompatibility.packageVersion -eq '0.2.0-preview') `
    'browserApiCompatibilityContract names an unexpected package version.'
Assert-Condition ($browserCompatibility.contractVersion -eq 4) `
    'browserApiCompatibilityContract must retain contract version 4.'
Assert-Condition ($browserCompatibility.managedExports -eq 4) `
    'browserApiCompatibilityContract must retain four managed exports.'
Assert-Condition ($browserCompatibility.operations -eq 3) `
    'browserApiCompatibilityContract must retain three operations.'
Assert-Condition ($browserCompatibility.optionFields -eq 27) `
    'browserApiCompatibilityContract must retain 27 option fields.'
Assert-Condition ($browserCompatibility.errorCodes -eq 9) `
    'browserApiCompatibilityContract must retain nine error codes.'
foreach ($property in @('manifest', 'gate', 'policy')) {
    $relativePath = [string]$browserCompatibility.$property
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($relativePath)) `
        "browserApiCompatibilityContract has no $property."
    Assert-Condition (Test-Path -LiteralPath (Join-Path $RepositoryRoot $relativePath) -PathType Leaf) `
        "browserApiCompatibilityContract $property '$relativePath' does not exist."
}
foreach ($evidencePath in @($browserCompatibility.evidence)) {
    Assert-Condition (Test-Path -LiteralPath (Join-Path $RepositoryRoot $evidencePath) -PathType Leaf) `
        "browserApiCompatibilityContract evidence '$evidencePath' does not exist."
}
Assert-WorkItems -WorkItems @($browserCompatibility.workItem) `
    -Context 'browserApiCompatibilityContract' -Required:$true

$allowedRoundTripStatuses = @($matrix.roundTripStatuses.PSObject.Properties.Name)
Assert-Condition ((($allowedRoundTripStatuses | Sort-Object) -join ',') -eq
                  ((@('blocked', 'conditional', 'notApplicable', 'verified') | Sort-Object) -join ',')) `
    'The round-trip status vocabulary must be exactly blocked, conditional, notApplicable, and verified.'

Assert-UniqueIds `
    -Items @($matrix.roundTripContracts | ForEach-Object { [pscustomobject]@{ id = $_.featureId } }) `
    -CollectionName 'roundTripContracts'
$featureIds = @($matrix.features | ForEach-Object { [string]$_.id })
$roundTripFeatureIds = @($matrix.roundTripContracts | ForEach-Object { [string]$_.featureId })
Assert-Condition ((($roundTripFeatureIds | Sort-Object) -join ',') -eq (($featureIds | Sort-Object) -join ',')) `
    'roundTripContracts must classify every feature exactly once.'
foreach ($contract in $matrix.roundTripContracts) {
    foreach ($propertyName in @('value', 'bytes')) {
        $classification = $contract.$propertyName
        $context = "Round-trip contract '$($contract.featureId).$propertyName'"
        $status = [string]$classification.status
        Assert-Condition ($status -in $allowedRoundTripStatuses) `
            "$context has unknown status '$status'."
        $conditions = @(
            $classification.conditions |
                ForEach-Object { [string]$_ } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        )
        Assert-Condition ($conditions.Count -gt 0) "$context has no conditions."
        $evidence = @($classification.evidence | ForEach-Object { [string]$_ })
        if ($status -ne 'notApplicable') {
            Assert-Condition ($evidence.Count -gt 0) "$context is $status but has no executable evidence."
        }
        foreach ($reference in $evidence) {
            Assert-EvidenceReference -Reference $reference -Context $context
        }

        $workItems = if ($classification.PSObject.Properties.Name -contains 'workItems') {
            @($classification.workItems)
        }
        else {
            @()
        }
        Assert-WorkItems -WorkItems $workItems -Context $context -Required:($status -eq 'blocked')
    }
}

Assert-UniqueIds -Items @($matrix.knownContractLimits) -CollectionName 'knownContractLimits'
foreach ($limit in $matrix.knownContractLimits) {
    $context = "Known contract limit '$($limit.id)'"
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$limit.summary)) "$context has no summary."
    Assert-WorkItems -WorkItems @($limit.workItems) -Context $context -Required:$true
}

Assert-UniqueIds -Items @($matrix.exclusions) -CollectionName 'exclusions'
foreach ($exclusion in $matrix.exclusions) {
    $context = "Exclusion '$($exclusion.id)'"
    foreach ($property in @('syntax', 'rationale', 'diagnosticPolicy')) {
        Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$exclusion.$property)) `
            "$context has no $property."
    }
    Assert-WorkItems -WorkItems @($exclusion.workItems) -Context $context -Required:$true
}

Write-Output 'Feature-operation matrix validation passed.'
Write-Output "Operations: $($operationIds.Count)"
Write-Output "Features: $(@($matrix.features).Count)"
Write-Output "Round-trip contracts: $(@($matrix.roundTripContracts).Count)"
Write-Output "Primitive spellings: $($allPrimitiveSpellings.Count)"
Write-Output "Memory I/O APIs: $($memoryInputApis.Count + $memoryOutputApis.Count)"
Write-Output "Compiled read/write routes: $($readRoutes.Count + $writeRoutes.Count)"
Write-Output "Shared read-like context routes: $($readLikeRoutes.Count)"
Write-Output "Frozen managed API frameworks: $($managedFrameworks.Count)"
Write-Output "Known contract limits: $(@($matrix.knownContractLimits).Count)"
Write-Output "Deliberate exclusions: $(@($matrix.exclusions).Count)"
Write-Output "Manual valid/invalid pairs: $($manualFixtureById.Count)"
