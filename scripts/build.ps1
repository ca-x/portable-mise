[CmdletBinding()]
param(
    [string]$DotnetPath = 'dotnet',
    [ValidatePattern('^v?\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.1.0',
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\artifacts'),
    [ValidateSet('win-x64', 'win-arm64')]
    [string[]]$RuntimeIdentifier = @('win-x64')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputRoot)
$sdk = (Get-Command $DotnetPath -ErrorAction Stop).Source
$env:DOTNET_ROOT = Split-Path -Parent $sdk
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$cleanVersion = $Version.TrimStart('v')
$numericVersion = ($cleanVersion -split '-', 2)[0] + '.0'
$project = Join-Path $repo 'src\PortableMise\PortableMise.csproj'
$tests = Join-Path $repo 'tests\PortableMise.Tests\PortableMise.Tests.csproj'
New-Item -ItemType Directory -Force -Path $output | Out-Null

function Invoke-Dotnet([string[]]$Arguments) {
    & $sdk @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet command failed with exit code $LASTEXITCODE" }
}

Push-Location $repo
try {
Invoke-Dotnet @('restore', $tests, '--nologo')
Invoke-Dotnet @('build', $tests, '-c', 'Release', '--no-restore', '--nologo', '-p:PublishSingleFile=false')
$testAssembly = Join-Path $repo 'tests\PortableMise.Tests\bin\Release\net10.0-windows\PortableMise.Tests.dll'
$evidence = Join-Path $output ('test-' + [guid]::NewGuid().ToString('N'))
Invoke-Dotnet @($testAssembly, $evidence)
$checksumLines = @()
foreach ($runtime in $RuntimeIdentifier) {
    $publish = Join-Path $output ('publish-' + $runtime + '-' + [guid]::NewGuid().ToString('N'))
    Invoke-Dotnet @('publish', $project, '-c', 'Release', '-r', $runtime, '--self-contained', 'false', '--nologo', '-o', $publish,
        '-p:PublishSingleFile=true', '-p:EnableCompressionInSingleFile=false', '-p:DebugType=embedded',
        ('-p:Version=' + $cleanVersion), ('-p:AssemblyVersion=' + $numericVersion), ('-p:FileVersion=' + $numericVersion))
    $files = @(Get-ChildItem -LiteralPath $publish -File)
    $directories = @(Get-ChildItem -LiteralPath $publish -Directory)
    if ($files.Count -ne 1 -or $files[0].Name -ne 'PortableMise.exe' -or $directories.Count -ne 0) {
        throw 'The published application must be a single framework-dependent PortableMise.exe, without separate control DLLs or runtime files.'
    }
    if ($runtime -eq 'win-x64') { & (Join-Path $PSScriptRoot 'gui-smoke.ps1') -Executable $files[0].FullName }
    $name = 'PortableMise-v' + $cleanVersion + '-' + $runtime + '.exe'
    $asset = Join-Path $output $name
    Copy-Item -LiteralPath $files[0].FullName -Destination $asset -Force
    $checksumLines += (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $name
    Write-Output ('BINARY=' + $asset)
}
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), (($checksumLines -join "`n") + "`n"), [Text.Encoding]::ASCII)
Write-Output ('TEST_EVIDENCE=' + $evidence)
Write-Output ('CHECKSUMS=' + (Join-Path $output 'SHA256SUMS.txt'))
} finally { Pop-Location }
