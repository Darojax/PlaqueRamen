[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference = 'Stop'
$taskModule = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskGame = (Resolve-Path -LiteralPath $GameRoot).Path.TrimEnd('\')
$taskNative = Join-Path $taskGame 'Ramen Shop Simulator_Data\Managed\Assembly-CSharp.dll'
if (-not (Test-Path -LiteralPath $taskNative)) { throw 'GameRoot must contain the installed Ramen Shop Simulator.' }
& (Join-Path $PSScriptRoot 'Setup-Tools.ps1')
$taskPrepared = Join-Path $taskModule 'artifacts\prepared-certificates'
& dotnet run --project (Join-Path $taskModule 'tools\CertificateAssetCompiler\CertificateAssetCompiler.csproj') -c Release -- $taskModule $taskPrepared
if ($LASTEXITCODE -ne 0) { throw 'Framed artwork preparation failed.' }
& dotnet build (Join-Path $taskModule 'src\PlaqueRamen.csproj') -c Release --nologo --verbosity quiet "-p:GameRoot=$taskGame"
if ($LASTEXITCODE -ne 0) { throw 'PlaqueRamen build failed.' }
$taskChecks = @(& dotnet run --project (Join-Path $taskModule 'tests\PlaqueRamen.Checks.csproj') -c Release -- $taskGame)
$taskChecks | Write-Output
if ($LASTEXITCODE -ne 0) { throw 'PlaqueRamen checks failed.' }
$taskCount = [regex]::Match(($taskChecks -join "`n"), '(\d+) PlaqueRamen checks passed')
if (-not $taskCount.Success) { throw 'Check completion summary is missing.' }
$taskVersion = ([xml](Get-Content -LiteralPath (Join-Path $taskModule 'src\PlaqueRamen.csproj') -Raw)).Project.PropertyGroup.Version
$taskStage = Join-Path $taskModule ('dist\PlaqueRamen-' + $taskVersion)
$taskPlugin = Join-Path $taskStage 'BepInEx\plugins\PlaqueRamen'
New-Item -ItemType Directory -Path $taskPlugin -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $taskModule 'src\bin\Release\netstandard2.1\PlaqueRamen.dll') -Destination $taskPlugin -Force
Copy-Item -LiteralPath (Join-Path $taskModule 'README.md') -Destination $taskStage -Force
$taskExpected = @('README.md','BepInEx\plugins\PlaqueRamen\PlaqueRamen.dll')
$taskExpected += @('Artwork.json','T_Tanzaku001_BASE.original.png','T_Tanzaku001_GLOSS.original.png','paper-beer\price-70.original.png','paper-beer\price-55.generated.png') | ForEach-Object { 'BepInEx\plugins\PlaqueRamen\assets\' + $_ }
$taskCatalogue = Get-Content -LiteralPath (Join-Path $taskModule 'assets\Artwork.json') -Raw | ConvertFrom-Json
$taskExpected += $taskCatalogue | ForEach-Object { 'BepInEx\plugins\PlaqueRamen\assets\art\' + $_.File }
$taskExpected += $taskCatalogue | Where-Object Drink | ForEach-Object {
    'BepInEx\plugins\PlaqueRamen\assets\art\' + $_.DarkFile
    'BepInEx\plugins\PlaqueRamen\assets\art\' + $_.DarkMaskFile
}
$taskCertificates = Get-Content -LiteralPath (Join-Path $taskModule 'assets\Certificates.json') -Raw | ConvertFrom-Json
$taskExpected += 'BepInEx\plugins\PlaqueRamen\assets\Certificates.json'
$taskExpected += 'BepInEx\plugins\PlaqueRamen\assets\certificates\frame-wood.original.png'
$taskPreparedManifest = Get-Content -LiteralPath (Join-Path $taskPrepared 'PreparedCertificates.json') -Raw | ConvertFrom-Json
$taskExpected += 'BepInEx\plugins\PlaqueRamen\assets\certificates\prepared\PreparedCertificates.json'
foreach ($taskCertificate in $taskPreparedManifest.Items) {
    foreach ($taskKind in @('Paper','Gloss','Icon','Frame')) {
        $taskTexture = $taskCertificate.$taskKind
        if ($taskTexture) { $taskExpected += 'BepInEx\plugins\PlaqueRamen\assets\certificates\prepared\' + $taskTexture.File }
    }
}
foreach ($taskRelative in $taskExpected | Where-Object { $_.StartsWith('BepInEx\plugins\PlaqueRamen\assets\') }) {
    $taskAsset = $taskRelative.Substring('BepInEx\plugins\PlaqueRamen\assets\'.Length)
    if ($taskAsset.StartsWith('certificates\prepared\')) {
        $taskSource = Join-Path $taskPrepared $taskAsset.Substring('certificates\prepared\'.Length)
    } else { $taskSource = Join-Path (Join-Path $taskModule 'assets') $taskAsset }
    $taskDestination = Join-Path $taskStage $taskRelative
    New-Item -ItemType Directory -Path (Split-Path -Parent $taskDestination) -Force | Out-Null
    Copy-Item -LiteralPath $taskSource -Destination $taskDestination -Force
}
$taskFiles = @(Get-ChildItem -LiteralPath $taskStage -Recurse -File)
if ($taskFiles.Count -ne 60 -or $taskExpected.Count -ne 60 -or @($taskExpected | Sort-Object -Unique).Count -ne 60) { throw 'Unexpected files in the package stage.' }
$taskPlan = @(foreach ($taskFile in $taskFiles) {
    $taskRelative = $taskFile.FullName.Substring($taskStage.Length + 1)
    if ($taskRelative -notin $taskExpected) { throw ('Unexpected package file: ' + $taskRelative) }
    [pscustomobject]@{ Relative=$taskRelative; Bytes=$taskFile.Length; SHA256=(Get-FileHash -LiteralPath $taskFile.FullName -Algorithm SHA256).Hash }
})
$taskZip = Join-Path $taskModule ('dist\PlaqueRamen-' + $taskVersion + '.zip')
Compress-Archive -Path (Join-Path $taskStage '*') -DestinationPath $taskZip -Force
New-Item -ItemType Directory -Path (Join-Path $taskModule 'artifacts') -Force | Out-Null
[ordered]@{ Version=$taskVersion; BuiltUtc=[DateTime]::UtcNow.ToString('O'); ChecksPassed=[int]$taskCount.Groups[1].Value; GameSHA256=(Get-FileHash -LiteralPath $taskNative -Algorithm SHA256).Hash; Stage=$taskStage; Archive=$taskZip; ArchiveSHA256=(Get-FileHash -LiteralPath $taskZip -Algorithm SHA256).Hash; Files=$taskPlan; Compatibility='Required game APIs, native mesh identity and price-art matching; no game-version allowlist'; AutomaticLoadLiveVerified=$false } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskModule 'artifacts\build-verification.json') -Encoding UTF8
Write-Output ('Prepared ' + $taskPlan.Count + ' verified files: ' + $taskZip)
