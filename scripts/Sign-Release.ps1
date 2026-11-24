param(
    [Parameter(Mandatory = $true)][string]$ScreenSaver,
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$CertificateThumbprint,
    [string]$TimestampServer = 'http://timestamp.digicert.com',
    [string]$SignTool
)

$ErrorActionPreference = 'Stop'
$source = Get-Item -LiteralPath $ScreenSaver
if ($source.PSIsContainer -or $source.Name -ine 'Gallery.scr') {
    throw 'Select the Gallery.scr produced by the release build.'
}

$certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint" -ErrorAction SilentlyContinue
if (-not $certificate -or -not $certificate.HasPrivateKey) {
    throw 'The selected certificate and its private key must be available in the current user Personal certificate store.'
}
if ('1.3.6.1.5.5.7.3.3' -notin $certificate.EnhancedKeyUsageList.ObjectId) {
    throw 'The selected certificate is not valid for code signing.'
}

if (-not $SignTool) {
    $SignTool = (Get-Command signtool.exe -CommandType Application -ErrorAction SilentlyContinue).Source
}
if (-not $SignTool) {
    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
    $SignTool = Get-ChildItem -LiteralPath $sdkRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "$arch\signtool.exe" } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
}
if (-not $SignTool) {
    throw 'SignTool was not found. Install the Windows SDK or pass -SignTool with the path to signtool.exe.'
}

$destination = Join-Path $source.DirectoryName 'signed\Gallery.scr'
if (Test-Path -LiteralPath $destination) {
    throw "A signed build already exists at $destination."
}
$output = New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
$staged = Join-Path $output.FullName ("Gallery.{0:N}.scr" -f [Guid]::NewGuid())
try {
    Copy-Item -LiteralPath $source.FullName -Destination $staged
    & $SignTool sign /s My /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampServer /td SHA256 $staged
    if ($LASTEXITCODE -ne 0) { throw "SignTool could not sign and timestamp Gallery.scr (exit code $LASTEXITCODE)." }

    & $SignTool verify /pa /tw /v $staged
    if ($LASTEXITCODE -ne 0) { throw "The Authenticode signature or timestamp did not pass verification (exit code $LASTEXITCODE)." }

    [IO.File]::Move($staged, $destination)
    Get-FileHash -LiteralPath $destination -Algorithm SHA256
}
finally {
    if (Test-Path -LiteralPath $staged) { Remove-Item -LiteralPath $staged }
}
