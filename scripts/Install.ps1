[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$GameRoot, [string]$LegacyReceiptPath)
$ErrorActionPreference = 'Stop'
$taskModule = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskGame = (Resolve-Path -LiteralPath $GameRoot).Path.TrimEnd('\')
$taskTargetRoot = [IO.Path]::GetFullPath((Join-Path $taskGame 'BepInEx\plugins\PlaqueRamen'))
$taskLegacyRoot = [IO.Path]::GetFullPath((Join-Path $taskGame 'BepInEx\plugins\MenuPlaqueSync'))
if (Get-Process -Name 'Ramen Shop Simulator' -ErrorAction SilentlyContinue) { throw 'Save and close the game before installing PlaqueRamen.' }
if (-not (Test-Path -LiteralPath (Join-Path $taskGame 'BepInEx\core\BepInEx.dll'))) { throw 'Install BepInEx 5 Windows x64 first.' }
if (Get-ChildItem -LiteralPath (Join-Path $taskGame 'BepInEx\plugins') -Filter 'RamenSigns.dll' -Recurse -File) { throw 'Remove the old RamenSigns trial before installing.' }
$taskBuild = Get-Content -LiteralPath (Join-Path $taskModule 'artifacts\build-verification.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath (Join-Path $taskGame 'Ramen Shop Simulator_Data\Managed\Assembly-CSharp.dll') -Algorithm SHA256).Hash -ne $taskBuild.GameSHA256) { throw 'Run Build.ps1 against this game installation before deploying.' }
$taskStage = [IO.Path]::GetFullPath($taskBuild.Stage).TrimEnd('\')
if (-not $taskStage.StartsWith($taskModule + '\dist\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Release stage is outside this mod.' }
$taskPlan = @($taskBuild.Files | Where-Object { $_.Relative -ne 'README.md' })
if ($taskPlan.Count -ne 33 -or @($taskPlan.Relative | Sort-Object -Unique).Count -ne 33) { throw 'Expected 33 distinct plugin files.' }
$taskReceiptPath = Join-Path $taskModule 'artifacts\installation-receipt.json'
$taskPrevious = $null
if (Test-Path -LiteralPath $taskReceiptPath) { $taskPrevious = Get-Content -LiteralPath $taskReceiptPath -Raw | ConvertFrom-Json }
if ($taskPrevious -and $taskPrevious.GameRoot -ne $taskGame) { throw 'Installation receipt belongs to another game folder.' }
foreach ($taskItem in $taskPlan) {
    $taskSource = [IO.Path]::GetFullPath((Join-Path $taskStage $taskItem.Relative))
    $taskTarget = [IO.Path]::GetFullPath((Join-Path $taskGame $taskItem.Relative))
    if (-not $taskSource.StartsWith($taskStage + '\', [StringComparison]::OrdinalIgnoreCase) -or -not $taskTarget.StartsWith($taskTargetRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'A deployment path escaped its folder.' }
    if ((Get-FileHash -LiteralPath $taskSource -Algorithm SHA256).Hash -ne $taskItem.SHA256) { throw ('Staged file changed: ' + $taskItem.Relative) }
    if (Test-Path -LiteralPath $taskTarget) {
        $taskHash = (Get-FileHash -LiteralPath $taskTarget -Algorithm SHA256).Hash
        $taskOwned = @($taskPrevious.Files | Where-Object { $_.Relative -eq $taskItem.Relative -and $_.SHA256 -eq $taskHash })
        if ($taskHash -ne $taskItem.SHA256 -and $taskOwned.Count -ne 1) { throw ('Unrecognised existing file retained: ' + $taskTarget) }
    }
}
if (Test-Path -LiteralPath $taskTargetRoot) {
    foreach ($taskExisting in Get-ChildItem -LiteralPath $taskTargetRoot -Recurse -File) {
        if ($taskPlan.Relative -notcontains $taskExisting.FullName.Substring($taskGame.Length+1)) { throw ('Unexpected file retained: ' + $taskExisting.FullName) }
    }
}
$taskLegacy = $null
if (Test-Path -LiteralPath $taskLegacyRoot) {
    if (-not $LegacyReceiptPath) { throw 'Remove the private MenuPlaqueSync prototype first, or pass its verified -LegacyReceiptPath for migration.' }
    $taskLegacy = Get-Content -LiteralPath $LegacyReceiptPath -Raw | ConvertFrom-Json
    if ($taskLegacy.GameRoot -ne $taskGame -or $taskLegacy.Files.Count -ne 16 -or @($taskLegacy.Files.Relative | Sort-Object -Unique).Count -ne 16) { throw 'Legacy receipt does not describe this installation.' }
    foreach ($taskItem in $taskLegacy.Files) {
        $taskTarget = [IO.Path]::GetFullPath((Join-Path $taskGame $taskItem.Relative))
        if (-not $taskTarget.StartsWith($taskLegacyRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Legacy path escaped its folder.' }
        if ((Get-FileHash -LiteralPath $taskTarget -Algorithm SHA256).Hash -ne $taskItem.SHA256) { throw ('Changed legacy file retained: ' + $taskTarget) }
    }
    foreach ($taskExisting in Get-ChildItem -LiteralPath $taskLegacyRoot -Recurse -File) {
        if ($taskLegacy.Files.Relative -notcontains $taskExisting.FullName.Substring($taskGame.Length+1)) { throw ('Unexpected legacy file retained: ' + $taskExisting.FullName) }
    }
}
$taskBackup = Join-Path $taskModule ('backups\install-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
New-Item -ItemType Directory -Path $taskBackup -Force | Out-Null
$taskProtected = @(Get-ChildItem -LiteralPath (Join-Path $taskGame 'BepInEx') -Recurse -File | Where-Object {
    -not $_.FullName.StartsWith($taskTargetRoot + '\', [StringComparison]::OrdinalIgnoreCase) -and
    -not $_.FullName.StartsWith($taskLegacyRoot + '\', [StringComparison]::OrdinalIgnoreCase) -and $_.Extension -in @('.dll','.ini','.cfg','.json')
} | ForEach-Object { [pscustomobject]@{ Path=$_.FullName; SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
foreach ($taskRelative in @('Ramen Shop Simulator_Data\Managed\Assembly-CSharp.dll','winhttp.dll','doorstop_config.ini')) {
    $taskPath = Join-Path $taskGame $taskRelative
    if (Test-Path -LiteralPath $taskPath) { $taskProtected += [pscustomobject]@{ Path=$taskPath; SHA256=(Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash } }
}
$taskSaves = @()
foreach ($taskName in @('SaveFile.es3','SaveFile.es3.bac')) {
    $taskPath = Join-Path $env:USERPROFILE ('AppData\LocalLow\Sannin Interactive AB\Ramen Shop Simulator\' + $taskName)
    if (-not (Test-Path -LiteralPath $taskPath)) { continue }
    $taskHash = (Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash
    $taskCopy = Join-Path $taskBackup $taskName
    Copy-Item -LiteralPath $taskPath -Destination $taskCopy
    if ((Get-FileHash -LiteralPath $taskCopy -Algorithm SHA256).Hash -ne $taskHash -or (Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash -ne $taskHash) { throw 'Save changed during backup.' }
    $taskSaves += [pscustomobject]@{ Original=$taskPath; Backup=$taskCopy; SHA256=$taskHash }
}
$taskBackupFiles = @($taskPlan)
if ($taskLegacy) { $taskBackupFiles += @($taskLegacy.Files); Copy-Item -LiteralPath $LegacyReceiptPath -Destination (Join-Path $taskBackup 'legacy-receipt.json') }
if (Test-Path -LiteralPath $taskReceiptPath) { Copy-Item -LiteralPath $taskReceiptPath -Destination (Join-Path $taskBackup 'previous-receipt.json') }
foreach ($taskItem in $taskBackupFiles) {
    $taskTarget = Join-Path $taskGame $taskItem.Relative
    if (Test-Path -LiteralPath $taskTarget) {
        $taskCopy = Join-Path $taskBackup $taskItem.Relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $taskCopy) -Force | Out-Null
        Copy-Item -LiteralPath $taskTarget -Destination $taskCopy
        if ((Get-FileHash -LiteralPath $taskTarget -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $taskCopy -Algorithm SHA256).Hash) { throw 'Previous plugin backup did not verify.' }
    }
}
if (Get-Process -Name 'Ramen Shop Simulator' -ErrorAction SilentlyContinue) { throw 'Game started during preparation; installation stopped.' }
foreach ($taskItem in $taskPlan) {
    $taskTarget = Join-Path $taskGame $taskItem.Relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $taskTarget) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $taskStage $taskItem.Relative) -Destination $taskTarget -Force
    if ((Get-FileHash -LiteralPath $taskTarget -Algorithm SHA256).Hash -ne $taskItem.SHA256) { throw 'Installed file did not verify.' }
}
if ($taskLegacy) {
    foreach ($taskItem in $taskLegacy.Files) {
        $taskTarget = [IO.Path]::GetFullPath((Join-Path $taskGame $taskItem.Relative))
        if (-not $taskTarget.StartsWith($taskLegacyRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or (Get-FileHash -LiteralPath $taskTarget -Algorithm SHA256).Hash -ne $taskItem.SHA256) { throw 'Legacy file changed before retirement.' }
        Remove-Item -LiteralPath $taskTarget
    }
    # Remove only verified empty legacy directories; no recursive deletion.
    foreach ($taskDirectory in @(Get-ChildItem -LiteralPath $taskLegacyRoot -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending)) {
        if ($taskDirectory.FullName.StartsWith($taskLegacyRoot + '\', [StringComparison]::OrdinalIgnoreCase) -and @(Get-ChildItem -LiteralPath $taskDirectory.FullName -Force).Count -eq 0) { Remove-Item -LiteralPath $taskDirectory.FullName }
    }
    if (@(Get-ChildItem -LiteralPath $taskLegacyRoot -Force).Count -eq 0) { Remove-Item -LiteralPath $taskLegacyRoot }
}
foreach ($taskItem in $taskProtected) { if ((Get-FileHash -LiteralPath $taskItem.Path -Algorithm SHA256).Hash -ne $taskItem.SHA256) { throw ('Protected file changed: ' + $taskItem.Path) } }
foreach ($taskSave in $taskSaves) { if ((Get-FileHash -LiteralPath $taskSave.Original -Algorithm SHA256).Hash -ne $taskSave.SHA256) { throw 'Save changed during installation.' } }
[ordered]@{ Version=$taskBuild.Version; InstalledUtc=[DateTime]::UtcNow.ToString('O'); GameRoot=$taskGame; Files=$taskPlan; Backup=$taskBackup; Saves=$taskSaves; Protected=$taskProtected; LegacyPrototypeRetired=[bool]$taskLegacy; AutomaticLoadLiveVerified=$false } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $taskReceiptPath -Encoding UTF8
Write-Output ('Installed PlaqueRamen ' + $taskBuild.Version + ': ' + $taskPlan.Count + ' verified files; ' + $taskProtected.Count + ' protected files and ' + $taskSaves.Count + ' saves unchanged. Game remains closed.')
