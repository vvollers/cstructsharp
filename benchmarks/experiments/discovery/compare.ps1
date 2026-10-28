# Runs immutable executable pairs serially, alternating process order and retaining every sample.
# Usage: pwsh -File compare.ps1 -Configuration cases.json -Output samples.jsonl [-Pairs 5] [-Affinity 65536]
param(
    [Parameter(Mandatory)] [string] $Configuration,
    [Parameter(Mandatory)] [string] $Output,
    [int] $Pairs = 5,
    [long] $Affinity = 65536
)
$ErrorActionPreference = 'Stop'
$cases = Get-Content -LiteralPath $Configuration -Raw | ConvertFrom-Json
$runner = [Diagnostics.Process]::GetCurrentProcess()
$originalAffinity = $runner.ProcessorAffinity
$runner.ProcessorAffinity = [IntPtr]$Affinity
$metadata = @{
    kind = 'environment'; utc = [DateTime]::UtcNow.ToString('O'); affinity = $Affinity
    processor = (Get-CimInstance Win32_Processor).Name; os = [Environment]::OSVersion.VersionString
    dotnet = (dotnet --info | Out-String); pairs = $Pairs; cache = 'OS cache retained; no disk-cache eviction'
}
$metadata | ConvertTo-Json -Compress | Set-Content -LiteralPath $Output -Encoding utf8
try {
    foreach ($case in $cases) {
        for ($pair = -1; $pair -lt $Pairs; $pair++) {
            $order = if ($pair % 2 -eq 0) { @('reference', 'candidate') } else { @('candidate', 'reference') }
            foreach ($side in $order) {
                $entry = $case.$side
                $executable = (Resolve-Path -LiteralPath $entry.executable).Path
                $info = [Diagnostics.ProcessStartInfo]::new($executable)
                $info.UseShellExecute = $false
                $info.CreateNoWindow = $true
                $info.RedirectStandardOutput = $true
                $info.RedirectStandardError = $true
                foreach ($argument in $entry.arguments) { $info.ArgumentList.Add([string]$argument) }
                $watch = [Diagnostics.Stopwatch]::StartNew()
                $process = [Diagnostics.Process]::Start($info)
                $stdout = $process.StandardOutput.ReadToEndAsync()
                $stderr = $process.StandardError.ReadToEndAsync()
                $process.WaitForExit()
                $watch.Stop()
                $sample = @{
                    kind = 'sample'; case = $case.name; side = $side; pair = $pair; warmup = ($pair -eq -1)
                    executable = $executable; sha256 = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
                    arguments = $entry.arguments; wallMs = $watch.Elapsed.TotalMilliseconds; exit = $process.ExitCode
                    stdout = $stdout.GetAwaiter().GetResult(); stderr = $stderr.GetAwaiter().GetResult()
                }
                $sample | ConvertTo-Json -Depth 5 -Compress | Add-Content -LiteralPath $Output -Encoding utf8
                if ($process.ExitCode -ne 0) { throw "Failed $($case.name)/$side; evidence retained in $Output" }
            }
        }
        Write-Output "Measured $($case.name)"
    }
}
finally { $runner.ProcessorAffinity = $originalAffinity }
