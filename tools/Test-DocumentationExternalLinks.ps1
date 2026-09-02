[CmdletBinding()]
param(
    [ValidateRange(1, 5)]
    [int]$Retries = 3,
    [ValidateRange(5, 120)]
    [int]$TimeoutSeconds = 20,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$DocumentationRoot = Join-Path $RepositoryRoot 'CStructSharp.Docs'
$AllowlistPath = Join-Path $DocumentationRoot 'contracts/documentation/external-link-allowlist.json'

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition)
    {
        throw $Message
    }
}

function Get-ExternalUrls {
    param([string]$Text)

    return @(
        [regex]::Matches($Text, '\[[^\]]+\]\((?<url>https?://[^\s)>]+)') |
            ForEach-Object { $_.Groups['url'].Value.TrimEnd('.') } |
            Sort-Object -Unique
    )
}

function Get-AllowlistErrors {
    param([object[]]$Exceptions)

    $errors = [Collections.Generic.List[string]]::new()
    $urls = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($exception in $Exceptions)
    {
        $url = [string]$exception.url
        if (-not [Uri]::IsWellFormedUriString($url, [UriKind]::Absolute) -or
            $url -notmatch '^https://')
        {
            $errors.Add("invalid-url:$url")
        }
        if ($url -match '\*' -or -not $urls.Add($url))
        {
            $errors.Add("non-exact-or-duplicate:$url")
        }
        if ([string]::IsNullOrWhiteSpace([string]$exception.owner))
        {
            $errors.Add("missing-owner:$url")
        }
        if ([string]::IsNullOrWhiteSpace([string]$exception.reason))
        {
            $errors.Add("missing-reason:$url")
        }

        $reviewDate = [DateTime]::MinValue
        if (-not [DateTime]::TryParseExact(
            [string]$exception.reviewAfter,
            'yyyy-MM-dd',
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::None,
            [ref]$reviewDate))
        {
            $errors.Add("invalid-review-date:$url")
        }
        elseif ($reviewDate.Date -lt [DateTime]::UtcNow.Date)
        {
            $errors.Add("expired-review-date:$url")
        }
    }
    return $errors
}

if ($SelfTest)
{
    $urls = @(Get-ExternalUrls -Text @'
[First](https://example.test/one)
[Duplicate](https://example.test/one)
[Second](https://example.test/two)
'@)
    Assert-Condition ($urls.Count -eq 2) 'External-link extraction did not deduplicate the fail-first fixture.'

    $invalid = @(
        [pscustomobject]@{
            url = 'https://example.test/*'
            owner = ''
            reason = ''
            reviewAfter = '2000-01-01'
        }
    )
    $errors = @(Get-AllowlistErrors -Exceptions $invalid)
    foreach ($expected in @(
        'non-exact-or-duplicate:',
        'missing-owner:',
        'missing-reason:',
        'expired-review-date:'))
    {
        Assert-Condition (@($errors | Where-Object { $_.StartsWith($expected) }).Count -eq 1) `
            "External-link allowlist fail-first fixture did not trigger '$expected'."
    }

    Write-Host 'External-link validator self-test passed: extraction and 4 invalid allowlist rules rejected.'
    return
}

Assert-Condition (Test-Path -LiteralPath $AllowlistPath -PathType Leaf) `
    "External-link allowlist does not exist: $AllowlistPath"
$allowlist = Get-Content -LiteralPath $AllowlistPath -Raw | ConvertFrom-Json -Depth 10
Assert-Condition ($allowlist.schemaVersion -eq 1) `
    "Unsupported external-link allowlist schema '$($allowlist.schemaVersion)'."
$allowlistErrors = @(Get-AllowlistErrors -Exceptions @($allowlist.exceptions))
Assert-Condition ($allowlistErrors.Count -eq 0) (
    "External-link allowlist is invalid:`n" +
    [string]::Join("`n", $allowlistErrors))

$allowedUrls = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($exception in @($allowlist.exceptions))
{
    [void]$allowedUrls.Add([string]$exception.url)
}

$externalUrls = @(
    Get-ChildItem -LiteralPath $DocumentationRoot -Recurse -File -Filter '*.md' |
        Where-Object {
            $_.FullName -notmatch '[\\/](?:_site|bin|obj|node_modules|test-results|playwright-report)[\\/]'
        } |
        ForEach-Object { Get-ExternalUrls -Text (Get-Content -LiteralPath $_.FullName -Raw) } |
        Sort-Object -Unique
)

$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $true
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)
$client.DefaultRequestHeaders.UserAgent.ParseAdd('CStructSharp-documentation-link-check/1.0')
$failures = [Collections.Generic.List[string]]::new()
$checkedCount = 0
$allowedCount = 0

try
{
    foreach ($url in $externalUrls)
    {
        if ($allowedUrls.Contains($url))
        {
            $allowedCount++
            Write-Host "ALLOW $url"
            continue
        }

        $success = $false
        $lastResult = 'no response'
        for ($attempt = 1; $attempt -le $Retries -and -not $success; $attempt++)
        {
            try
            {
                $head = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Head, $url)
                $response = $client.Send($head, [Net.Http.HttpCompletionOption]::ResponseHeadersRead)
                try
                {
                    if ($response.IsSuccessStatusCode)
                    {
                        $success = $true
                    }
                    else
                    {
                        $lastResult = "HTTP $([int]$response.StatusCode)"
                    }
                }
                finally
                {
                    $response.Dispose()
                    $head.Dispose()
                }

                if (-not $success)
                {
                    $get = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, $url)
                    $response = $client.Send($get, [Net.Http.HttpCompletionOption]::ResponseHeadersRead)
                    try
                    {
                        $success = $response.IsSuccessStatusCode
                        $lastResult = "HTTP $([int]$response.StatusCode)"
                    }
                    finally
                    {
                        $response.Dispose()
                        $get.Dispose()
                    }
                }
            }
            catch
            {
                $lastResult = $_.Exception.Message
            }
        }

        if ($success)
        {
            $checkedCount++
            Write-Host "OK    $url"
        }
        else
        {
            $failures.Add("$url ($lastResult after $Retries attempts)")
        }
    }
}
finally
{
    $client.Dispose()
    $handler.Dispose()
}

Assert-Condition ($failures.Count -eq 0) (
    "External documentation links failed:`n" +
    [string]::Join("`n", $failures))

Write-Host (
    'External-link validation passed: {0} checked, {1} reviewed exceptions, {2} total.' -f
    $checkedCount,
    $allowedCount,
    $externalUrls.Count
)
