param([string]$ScreenSaver = (Join-Path $PSScriptRoot '../bin/Release/net48/Gallery.scr'))

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $ScreenSaver).Path
$test = New-Item -ItemType Directory -Path (Join-Path ([IO.Path]::GetTempPath()) ("gallery-screensaver-test-{0:N}" -f [Guid]::NewGuid()))
$copy = Join-Path $test.FullName 'Gallery.scr'
$data = Join-Path $test.FullName 'data'
Copy-Item -LiteralPath $source -Destination $copy
$startInfo = [Diagnostics.ProcessStartInfo]::new($copy)
$startInfo.UseShellExecute = $false
$startInfo.EnvironmentVariables['GALLERY_SAVER_DATA_DIR'] = $data

function Test-EntryPoint([string]$argument) {
    $startInfo.Arguments = $argument
    $process = [Diagnostics.Process]::Start($startInfo)
    try {
        if (-not $process.WaitForExit(45000)) {
            $process.Kill()
            throw "Gallery.scr $argument timed out."
        }
        if ($process.ExitCode -ne 0) {
            $report = Join-Path $data 'selftest.txt'
            if ($argument -eq '/selftest' -and (Test-Path -LiteralPath $report)) {
                Get-Content -LiteralPath $report | Write-Host
            }
            throw "Gallery.scr $argument exited with code $($process.ExitCode)."
        }
    } finally {
        $process.Dispose()
    }
}

try {
    Test-EntryPoint '/selftest'
    Get-Content -LiteralPath (Join-Path $data 'selftest.txt')
    Test-EntryPoint '/p:0'

    $previewTest = Join-Path $PSScriptRoot 'Test-Preview.ps1'
    powershell.exe -NoProfile -STA -File $previewTest -ScreenSaver $copy
    if ($LASTEXITCODE -ne 0) { throw 'Windows preview test failed.' }
} finally {
    $path = [IO.Path]::GetFullPath($test.FullName)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if ($path.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
    }
}
