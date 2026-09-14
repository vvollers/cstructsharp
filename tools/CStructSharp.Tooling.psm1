<#
.SYNOPSIS
Shared helpers used by tools/*.ps1 (the architecture improvement plan, AP-4.2).

.DESCRIPTION
Assert-Condition and Invoke-DotNet were previously copy-pasted into most scripts under tools/, with
Assert-Condition drifting into two subtly different forms ([bool]$Condition vs. [Parameter(Mandatory)]). This
module is the single source of truth for both, reconciled to the stricter, fail-fast forms.
#>

Set-StrictMode -Version Latest

function Assert-Condition {
    <#
    .SYNOPSIS
    Throws $Message when $Condition is false.
    #>
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

function Format-CommandArgument {
    <#
    .SYNOPSIS
    Quotes one dotnet CLI argument for display only, if it contains whitespace or a double quote.
    #>
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string]$Argument
    )

    if ($Argument -match '[\s"]') {
        return "'" + ($Argument -replace "'", "''") + "'"
    }

    return $Argument
}

function Invoke-DotNet {
    <#
    .SYNOPSIS
    Runs `dotnet` with the given arguments, logging the command, its output, and its elapsed time, and throws if
    it exits non-zero. Returns the elapsed time in seconds; callers that don't need it should wrap the call in
    [void](...) to avoid the value falling through to the output stream.
    #>
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,

        [string]$Label = ($Arguments -join ' ')
    )

    $display = ($Arguments | ForEach-Object { Format-CommandArgument $_ }) -join ' '
    Write-Host "==> dotnet $display"
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $commandOutput = @(& dotnet @Arguments)
    $exitCode = $LASTEXITCODE
    $stopwatch.Stop()
    foreach ($line in $commandOutput) {
        Write-Host $line
    }

    Write-Host ("<== {0}: exit {1}, {2:N3} s" -f $Label, $exitCode, $stopwatch.Elapsed.TotalSeconds)
    if ($exitCode -ne 0) {
        throw "$Label failed with exit code $exitCode."
    }

    return $stopwatch.Elapsed.TotalSeconds
}

Export-ModuleMember -Function Assert-Condition, Format-CommandArgument, Invoke-DotNet
