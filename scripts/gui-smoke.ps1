[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$Executable)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$program = [IO.Path]::GetFullPath($Executable)
$stream = [IO.File]::OpenRead($program)
$reader = New-Object IO.BinaryReader($stream)
try {
    if ($reader.ReadUInt16() -ne 0x5a4d) { throw 'Published application has no DOS header.' }
    $stream.Position = 0x3c
    $stream.Position = $reader.ReadInt32()
    if ($reader.ReadUInt32() -ne 0x4550) { throw 'Published application has no PE header.' }
    $optionalHeader = $stream.Position + 20
    $stream.Position = $optionalHeader + 68
    if ($reader.ReadUInt16() -ne 2) { throw 'Published application must use the Windows GUI subsystem.' }
} finally { $reader.Dispose() }
$process = Start-Process -FilePath $program -WorkingDirectory ([IO.Path]::GetDirectoryName($program)) -WindowStyle Hidden -PassThru
try {
    if (-not $process.WaitForInputIdle(20000)) { throw 'Published WPF window did not initialize within 20 seconds.' }
    $process.Refresh()
    if ($process.HasExited) { throw ('Published WPF application exited: ' + $process.ExitCode) }
    Write-Output 'PASS: framework-dependent single EXE starts a WPF message loop.'
} finally {
    $process.Refresh()
    if (-not $process.HasExited) {
        [void]$process.CloseMainWindow()
        if (-not $process.WaitForExit(2000)) { $process.Kill(); $process.WaitForExit() }
    }
    $process.Dispose()
}
