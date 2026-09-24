param(
    [switch]$UI,
    [switch]$Network
)

$ErrorActionPreference = 'Stop'
$outputDir = Join-Path $PSScriptRoot 'build'
$executable = Join-Path $outputDir 'ST4RR.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw 'Run build.ps1 first.'
}

$testArguments = @('--test')
if ($Network) {
    $testArguments += '--network', '--extended'
}
$process = Start-Process -FilePath $executable -ArgumentList $testArguments -WindowStyle Hidden -PassThru -Wait
Get-Content -LiteralPath (Join-Path $outputDir 'test-results.txt') -Encoding UTF8
if ($process.ExitCode -ne 0) {
    throw 'Tests failed.'
}

if ($UI) {
    $compiler = Join-Path $PSScriptRoot 'tools\microsoft.net.compilers.toolset\tasks\net472\csc.exe'
    $runner = Join-Path $PSScriptRoot 'tools\qa-runner.exe'
    & $compiler /nologo /target:exe "/out:$runner" (Join-Path $PSScriptRoot 'tools\qa-runner.cs')
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not compile the UI test runner.'
    }

    foreach ($language in @('ru', 'en')) {
        $failure = Join-Path $outputDir 'smoke-failure.txt'
        $report = Join-Path $outputDir "screenshots\smoke-$language.txt"
        foreach ($previous in @($failure, $report)) {
            if (Test-Path -LiteralPath $previous) {
                Remove-Item -LiteralPath $previous
            }
        }
        if ($language -eq 'en') {
            & $runner $executable --en
        } else {
            & $runner $executable
        }
        if ($LASTEXITCODE -ne 0) {
            throw "UI tests failed: $language."
        }
        if (Test-Path -LiteralPath $failure) {
            throw (Get-Content -LiteralPath $failure -Raw -Encoding UTF8)
        }
        if (-not (Test-Path -LiteralPath $report)) {
            throw "Missing UI test report: $language."
        }
        Get-Content -LiteralPath $report -Encoding UTF8
    }
}
