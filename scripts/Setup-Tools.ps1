[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskModule = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskZip = Join-Path $taskModule '.tools\downloads\BepInEx_win_x64_5.4.23.6.zip'
if (-not (Test-Path -LiteralPath $taskZip)) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $taskZip) -Force | Out-Null
    Invoke-WebRequest -Uri 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.6/BepInEx_win_x64_5.4.23.6.zip' -OutFile $taskZip -UseBasicParsing
}
if ((Get-FileHash -LiteralPath $taskZip -Algorithm SHA256).Hash -ne '6C57A714A892EFB4E9E1FEF63D608A3391F30EC984031C465404E4EE381A0755') { throw 'Pinned BepInEx archive hash differs from the official release.' }
$taskFolder = Join-Path $taskModule '.tools\bepinex'
if (-not (Test-Path -LiteralPath $taskFolder)) { Expand-Archive -LiteralPath $taskZip -DestinationPath $taskFolder }
Write-Output 'Pinned BepInEx build dependencies are ready.'
