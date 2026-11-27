<#
.SYNOPSIS
    Build, verify, install, inspect, and cleanly remove StoryModeTrainer + GTA 5 Legacy dependencies.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('SyncDist', 'Install', 'Remove', 'Status', 'Verify', 'LaunchOffline')]
    [string]$Action,

    [string]$GamePath,

    [switch]$WaitAndClean,

    [switch]$RestorePreExistingBackup,

    [switch]$OmitScOfflineOnly
)

$ErrorActionPreference = 'Stop'
$WorkspaceRoot   = Split-Path -Parent $MyInvocation.MyCommand.Path
$DepsExtracted   = Join-Path $WorkspaceRoot 'deps\extracted'
$DistRoot        = Join-Path $WorkspaceRoot 'dist'
$ManifestPath    = Join-Path $WorkspaceRoot '.install-manifest.json'
$BackupRoot      = Join-Path $WorkspaceRoot 'backup'
$ProjectPath     = Join-Path $WorkspaceRoot 'StoryModeTrainer.csproj'
$ReleaseDll      = Join-Path $WorkspaceRoot 'build\Release\StoryModeTrainer.dll'
$SourceCs        = Join-Path $WorkspaceRoot 'StoryModeTrainer.cs'
$SourceIni       = Join-Path $WorkspaceRoot 'StoryModeTrainer.ini'
$SourceShvdnIni  = Join-Path $WorkspaceRoot 'ScriptHookVDotNet.ini'
$SourceCmdline   = Join-Path $WorkspaceRoot 'commandline.txt'

$ExpectedGameBranch = '1.0.3889.0'
$ScriptHookVVersion = '3889.0.1158.13'
$ShvdnVersion       = '3.7.0-nightly.206'
$LemonUiVersion     = '2.2.0.0'

function Find-MsBuildPath {
    $candidates = @(
        'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
    )

    foreach ($p in $candidates) {
        if (Test-Path -LiteralPath $p) { return $p }
    }

    $cmd = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

function Find-Gta5LegacyPath {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        return $ExplicitPath
    }

    if (Test-Path -LiteralPath $ManifestPath) {
        try {
            $m = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
            if ($m.GamePath -and (Test-Path -LiteralPath (Join-Path $m.GamePath 'GTA5.exe'))) {
                return $m.GamePath
            }
        } catch { }
    }

    $candidates = [System.Collections.Generic.List[string]]::new()

    $steamPath = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if ($steamPath) {
        $steamNorm = $steamPath -replace '/', '\'
        $candidates.Add((Join-Path $steamNorm 'steamapps\common\Grand Theft Auto V'))

        $libVdf = [System.IO.Path]::Combine($steamNorm, 'steamapps\libraryfolders.vdf')
        if (Test-Path -LiteralPath $libVdf) {
            $matches = Select-String -Path $libVdf -Pattern '"path"\s+"([^"]+)"' -AllMatches
            foreach ($match in $matches) {
                foreach ($g in $match.Matches) {
                    $libPath = $g.Groups[1].Value -replace '\\\\', '\'
                    $candidates.Add([System.IO.Path]::Combine($libPath, 'steamapps\common\Grand Theft Auto V'))
                }
            }
        }
    }

    foreach ($regPath in @(
        'HKLM:\SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V',
        'HKLM:\SOFTWARE\Rockstar Games\Grand Theft Auto V',
        'HKLM:\SOFTWARE\WOW6432Node\Rockstar Games\Launcher\Titles\GTA V'
    )) {
        $installFolder = (Get-ItemProperty $regPath -ErrorAction SilentlyContinue).InstallFolder
        if ($installFolder) { $candidates.Add($installFolder) }
    }

    $epicManifest = 'C:\ProgramData\Epic\UnrealEngineLauncher\LauncherInstalled.dat'
    if (Test-Path -LiteralPath $epicManifest) {
        try {
            $epicJson = Get-Content -LiteralPath $epicManifest -Raw | ConvertFrom-Json
            foreach ($app in @($epicJson.InstallationList)) {
                if ($app.InstallLocation -and ($app.AppName -match 'GTA|GrandTheftAuto' -or (Test-Path -LiteralPath (Join-Path $app.InstallLocation 'GTA5.exe')))) {
                    $candidates.Add([string]$app.InstallLocation)
                }
            }
        } catch { }
    }

    foreach ($c in ($candidates | Select-Object -Unique)) {
        $exe = [System.IO.Path]::Combine($c, 'GTA5.exe')
        if ([System.IO.File]::Exists($exe)) {
            return $c
        }
    }

    throw 'Could not locate GTA 5 Legacy (GTA5.exe). Pass -GamePath "<path to Grand Theft Auto V>".'
}

function Assert-ValidGta5LegacyDir {
    param([string]$Dir)

    if (-not (Test-Path -LiteralPath $Dir)) {
        throw "GTA 5 directory does not exist: $Dir"
    }

    $gtaExe = Join-Path $Dir 'GTA5.exe'
    $enhExe = Join-Path $Dir 'GTA5_Enhanced.exe'
    if ((-not (Test-Path -LiteralPath $gtaExe)) -and (Test-Path -LiteralPath $enhExe)) {
        throw "Target directory appears to be GTA 5 Enhanced (GTA5_Enhanced.exe), not GTA 5 Legacy: $Dir"
    }
    if (-not (Test-Path -LiteralPath $gtaExe)) {
        throw "GTA5.exe was not found in: $Dir"
    }

    return $gtaExe
}

function Assert-GameNotRunning {
    $running = Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $_.ProcessName -in @('GTA5', 'GTA5_BE', 'BEService_x64', 'BEService', 'PlayGTAV', 'GTAVLauncher')
    }
    if ($running) {
        $names = ($running | Select-Object -ExpandProperty ProcessName -Unique) -join ', '
        throw "Cannot modify GTA 5 files while game/BattlEye processes are running ($names). Close GTA 5 first."
    }

    $beSvc = Get-Service -Name 'BEService' -ErrorAction SilentlyContinue
    if ($beSvc -and $beSvc.Status -in @('Running', 'StartPending', 'ContinuePending')) {
        throw "Cannot modify GTA 5 files while Windows service 'BEService' is $($beSvc.Status). Close any BattlEye-protected game first."
    }

    $enhanced = Get-Process -Name 'GTA5_Enhanced' -ErrorAction SilentlyContinue
    if ($enhanced) {
        Write-Warning "GTA5_Enhanced.exe is currently running. Verify no BattlEye session is active before modifying Legacy files."
    }
}

function Get-DistPayloadMap {
    return @(
        @{ Source = (Join-Path $DepsExtracted 'ScriptHookV\bin\dinput8.dll');                        RelTarget = 'dinput8.dll' },
        @{ Source = (Join-Path $DepsExtracted 'ScriptHookV\bin\ScriptHookV.dll');                    RelTarget = 'ScriptHookV.dll' },
        @{ Source = (Join-Path $DepsExtracted 'ScriptHookV\bin\args.txt');                           RelTarget = 'args.txt' },
        @{ Source = $SourceCmdline;                                                                  RelTarget = 'commandline.txt' },
        @{ Source = (Join-Path $DepsExtracted 'ScriptHookVDotNet\ScriptHookVDotNet-Loader.asi');     RelTarget = 'ScriptHookVDotNet-Loader.asi' },
        @{ Source = (Join-Path $DepsExtracted 'ScriptHookVDotNet\ScriptHookVDotNet.dll');            RelTarget = 'ScriptHookVDotNet.dll' },
        @{ Source = (Join-Path $DepsExtracted 'ScriptHookVDotNet\ScriptHookVDotNet2.dll');           RelTarget = 'ScriptHookVDotNet2.dll' },
        @{ Source = (Join-Path $DepsExtracted 'ScriptHookVDotNet\ScriptHookVDotNet3.dll');           RelTarget = 'ScriptHookVDotNet3.dll' },
        @{ Source = $SourceShvdnIni;                                                                 RelTarget = 'ScriptHookVDotNet.ini' },
        @{ Source = (Join-Path $DepsExtracted 'LemonUI\SHVDN3\LemonUI.SHVDN3.dll');                  RelTarget = 'scripts\LemonUI.SHVDN3.dll' },
        @{ Source = $ReleaseDll;                                                                     RelTarget = 'scripts\StoryModeTrainer.dll' },
        @{ Source = $SourceIni;                                                                      RelTarget = 'scripts\StoryModeTrainer.ini' }
    )
}

function Invoke-BuildTrainer {
    $msbuild = Find-MsBuildPath
    if (-not $msbuild) {
        if (Test-Path -LiteralPath $ReleaseDll) {
            Write-Warning "MSBuild.exe not found; using existing compiled $ReleaseDll"
            return
        }
        throw 'MSBuild.exe not found and no prebuilt StoryModeTrainer.dll exists.'
    }

    Write-Host "Building StoryModeTrainer.csproj (Release|x64)..."
    & $msbuild $ProjectPath /p:Configuration=Release /p:Platform=x64 /v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild failed with exit code $LASTEXITCODE"
    }
}

function Invoke-SyncDist {
    param([switch]$SkipBuild)

    if (-not $SkipBuild) {
        $csInputs = @(Get-ChildItem -LiteralPath $WorkspaceRoot -Filter '*.cs' -File -ErrorAction Stop)
        foreach ($extraInput in @(
            $ProjectPath,
            (Join-Path $DepsExtracted 'ScriptHookVDotNet\ScriptHookVDotNet3.dll'),
            (Join-Path $DepsExtracted 'LemonUI\SHVDN3\LemonUI.SHVDN3.dll')
        )) {
            if (Test-Path -LiteralPath $extraInput) {
                $csInputs += (Get-Item -LiteralPath $extraInput)
            }
        }
        $newestSourceUtc = ($csInputs | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc
        $needsBuild = (-not (Test-Path -LiteralPath $ReleaseDll)) -or
                      ($newestSourceUtc -gt (Get-Item -LiteralPath $ReleaseDll).LastWriteTimeUtc)
        if ($needsBuild) {
            Invoke-BuildTrainer
        }
    }

    $distRootFolder    = Join-Path $DistRoot 'root'
    $distScriptsFolder = Join-Path $DistRoot 'scripts'
    New-Item -ItemType Directory -Force -Path $distRootFolder, $distScriptsFolder | Out-Null

    $expectedTargets = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in (Get-DistPayloadMap)) {
        if (-not (Test-Path -LiteralPath $entry.Source)) {
            throw "Missing required staging source file: $($entry.Source)"
        }
        if ($entry.RelTarget.StartsWith('scripts\')) {
            $leaf = Split-Path $entry.RelTarget -Leaf
            $dest = Join-Path $distScriptsFolder $leaf
        } else {
            $dest = Join-Path $distRootFolder $entry.RelTarget
        }
        $null = $expectedTargets.Add([System.IO.Path]::GetFullPath($dest))
        $srcHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $entry.Source).Hash
        $dstHash = if (Test-Path -LiteralPath $dest) { (Get-FileHash -Algorithm SHA256 -LiteralPath $dest).Hash } else { '' }
        if ($srcHash -ne $dstHash) {
            Copy-Item -LiteralPath $entry.Source -Destination $dest -Force
        }
    }

    foreach ($existing in (Get-ChildItem -LiteralPath $DistRoot -Recurse -File)) {
        if (-not $expectedTargets.Contains([System.IO.Path]::GetFullPath($existing.FullName))) {
            Remove-Item -LiteralPath $existing.FullName -Force
        }
    }

    Write-Host "Staged clean Legacy mod payload in: $DistRoot"
}

function Get-DistStageEntries {
    $distRootFolder    = Join-Path $DistRoot 'root'
    $distScriptsFolder = Join-Path $DistRoot 'scripts'
    return @(
        'dinput8.dll',
        'ScriptHookV.dll',
        'args.txt',
        'commandline.txt',
        'ScriptHookVDotNet-Loader.asi',
        'ScriptHookVDotNet.dll',
        'ScriptHookVDotNet2.dll',
        'ScriptHookVDotNet3.dll',
        'ScriptHookVDotNet.ini',
        'scripts\LemonUI.SHVDN3.dll',
        'scripts\StoryModeTrainer.dll',
        'scripts\StoryModeTrainer.ini'
    ) | ForEach-Object {
        $src = if ($_.StartsWith('scripts\')) {
            Join-Path $distScriptsFolder (Split-Path $_ -Leaf)
        } else {
            Join-Path $distRootFolder $_
        }
        [PSCustomObject]@{
            RelPath = $_
            StagePath = $src
        }
    }
}

function Invoke-InstallMod {
    param([string]$ExplicitGamePath)

    Invoke-SyncDist
    $gtaDir = Find-Gta5LegacyPath -ExplicitPath $ExplicitGamePath
    $gtaExe = Assert-ValidGta5LegacyDir -Dir $gtaDir
    Assert-GameNotRunning

    $gtaVersion = ([Diagnostics.FileVersionInfo]::GetVersionInfo($gtaExe)).FileVersion
    Write-Host "Target GTA 5 Legacy : $gtaDir (GTA5.exe $gtaVersion)"

    $existingManifest = $null
    $sameTargetManifest = $false
    $alreadyTracked = @{}
    if (Test-Path -LiteralPath $ManifestPath) {
        try {
            $existingManifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
            if ($existingManifest.GamePath -and (Test-Path -LiteralPath $existingManifest.GamePath)) {
                $normManifest = [System.IO.Path]::GetFullPath($existingManifest.GamePath).TrimEnd('\', '/')
                $normTarget   = [System.IO.Path]::GetFullPath($gtaDir).TrimEnd('\', '/')
                if ([string]::Equals($normManifest, $normTarget, [StringComparison]::OrdinalIgnoreCase)) {
                    $sameTargetManifest = $true
                    foreach ($f in $existingManifest.Files) {
                        if ($f.RelPath) {
                            $alreadyTracked[$f.RelPath.ToLowerInvariant()] = $true
                        }
                    }
                }
            }
        } catch { }
    }

    $backupDir = $null
    $backedUp = [System.Collections.Generic.List[string]]::new()
    if ($sameTargetManifest -and $existingManifest.BackupDir) {
        $backupDir = [string]$existingManifest.BackupDir
        foreach ($prevRel in @($existingManifest.BackedUpFiles)) {
            if ($prevRel) {
                $backedUp.Add([string]$prevRel)
            }
        }
    }
    $createdNewBackupDir = $false
    $newlyBackedUp = [System.Collections.Generic.List[string]]::new()
    $deployed = [System.Collections.Generic.List[object]]::new()
    $createdNew = [System.Collections.Generic.List[string]]::new()

    $scriptsRoot = Join-Path $gtaDir 'scripts'
    $scriptsExistedBefore = Test-Path -LiteralPath $scriptsRoot
    New-Item -ItemType Directory -Force -Path $scriptsRoot | Out-Null

    try {
        foreach ($item in (Get-DistStageEntries)) {
            if (-not (Test-Path -LiteralPath $item.StagePath)) {
                throw "Staged file missing: $($item.StagePath)"
            }

            $targetPath = Join-Path $gtaDir $item.RelPath
            $targetDir  = Split-Path -Parent $targetPath
            if (-not (Test-Path -LiteralPath $targetDir)) {
                New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
            }

            $existedBeforeCopy = Test-Path -LiteralPath $targetPath
            if ($existedBeforeCopy -and (-not $alreadyTracked.ContainsKey($item.RelPath.ToLowerInvariant()))) {
                if (-not $backupDir) {
                    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
                    $backupDir = Join-Path $BackupRoot $stamp
                    New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
                    $createdNewBackupDir = $true
                } elseif (-not (Test-Path -LiteralPath $backupDir)) {
                    New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
                    $createdNewBackupDir = $true
                }
                $bkpTarget = Join-Path $backupDir $item.RelPath
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $bkpTarget) | Out-Null
                Copy-Item -LiteralPath $targetPath -Destination $bkpTarget -Force
                $backedUp.Add($item.RelPath)
                $newlyBackedUp.Add($item.RelPath)
            } elseif (-not $existedBeforeCopy) {
                $createdNew.Add($item.RelPath)
            }

            if ($OmitScOfflineOnly -and [string]::Equals($item.RelPath, 'commandline.txt', [StringComparison]::OrdinalIgnoreCase)) {
                [System.IO.File]::WriteAllText($targetPath, "-nobattleye`r`n", (New-Object System.Text.UTF8Encoding($false)))
                $targetHash = (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash
            } else {
                Copy-Item -LiteralPath $item.StagePath -Destination $targetPath -Force
                $stageHash  = (Get-FileHash -LiteralPath $item.StagePath -Algorithm SHA256).Hash
                $targetHash = (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash
                if ($stageHash -ne $targetHash) {
                    throw "Hash verification failed after copying $($item.RelPath)"
                }
            }

            $deployed.Add([PSCustomObject]@{
                RelPath = $item.RelPath
                Sha256  = $targetHash
            })
        }
    } catch {
        Write-Warning "Install failed mid-deployment ($($_.Exception.Message)). Rolling back partial changes..."
        foreach ($rel in $createdNew) {
            $p = Join-Path $gtaDir $rel
            if (Test-Path -LiteralPath $p) {
                Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue
            }
        }
        if ($backupDir -and (Test-Path -LiteralPath $backupDir)) {
            foreach ($rel in $newlyBackedUp) {
                $bkp = Join-Path $backupDir $rel
                if (Test-Path -LiteralPath $bkp) {
                    $dst = Join-Path $gtaDir $rel
                    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dst) | Out-Null
                    Copy-Item -LiteralPath $bkp -Destination $dst -Force -ErrorAction SilentlyContinue
                    if (-not $createdNewBackupDir) {
                        Remove-Item -LiteralPath $bkp -Force -ErrorAction SilentlyContinue
                    }
                }
            }
            if ($createdNewBackupDir) {
                Remove-Item -LiteralPath $backupDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
        if (-not $scriptsExistedBefore -and (Test-Path -LiteralPath $scriptsRoot)) {
            if (@(Get-ChildItem -LiteralPath $scriptsRoot -Force -ErrorAction SilentlyContinue).Count -eq 0) {
                Remove-Item -LiteralPath $scriptsRoot -Force -ErrorAction SilentlyContinue
            }
        }
        throw
    }

    $manifest = [PSCustomObject]@{
        InstalledAtUtc      = (Get-Date).ToUniversalTime().ToString('o')
        GamePath            = $gtaDir
        GameExeVersion      = $gtaVersion
        ScriptHookVVersion  = $ScriptHookVVersion
        ShvdnVersion        = $ShvdnVersion
        LemonUiVersion      = $LemonUiVersion
        BackupDir           = $backupDir
        BackedUpFiles       = $backedUp
        Files               = $deployed
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ManifestPath -Encoding UTF8

    Write-Host ""
    Write-Host "[OK] Installed $($deployed.Count) mod/dependency files into GTA 5 Legacy."
    if ($gtaVersion -ne $ExpectedGameBranch) {
        Write-Warning "Installed GTA5.exe is $gtaVersion (built/tested for $ExpectedGameBranch)."
    }
    Write-Host "Reminder before launching Story Mode Offline:"
    Write-Host "  1. Disable BattlEye in Rockstar Games Launcher (Settings -> uncheck 'Enable BattlEye')"
    Write-Host "     (ScriptHookV's args.txt with '-nobattleye -noBE' is also staged)."
    Write-Host "  2. Launch GTA 5 Legacy Story Mode and press F3 for Trainer Menu, F9 for No-Clip."
}

function Get-NonVanillaRootItems {
    param([string]$GameDir)

    return @(Get-ChildItem -LiteralPath $GameDir -Force -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -in @(
            'dinput8.dll', 'dinput.dll', 'dsound.dll', 'xinput1_4.dll', 'xinput1_3.dll',
            'version.dll', 'winmm.dll', 'dxgi.dll', 'd3d9.dll', 'd3d10.dll', 'd3d11.dll',
            'bink2w64_orig.dll', 'ScriptHookV.dll', 'scripthookv.dev',
            'args.txt', 'commandline.txt', 'NativeTrainer.asi',
            'scripts', 'mods', 'plugins', 'lspdfr', 'menyooStuff'
        ) -or
        $_.Name -like '*.asi' -or
        $_.Name -like '*.pdb' -or
        $_.Name -like 'ScriptHookVDotNet*' -or
        $_.Name -like 'ScriptHook*.log*' -or
        $_.Name -like 'asiloader*' -or
        $_.Name -like 'RAGEPluginHook*' -or
        $_.Name -like 'LemonUI*'
    })
}

function Get-IniSettingValue {
    param(
        [string]$IniText,
        [string]$KeyName
    )

    foreach ($rawLine in ($IniText -split "`r?`n")) {
        $line = $rawLine.Trim()
        if ($line.Length -eq 0 -or $line.StartsWith(';') -or $line.StartsWith('#') -or $line.StartsWith('[')) {
            continue
        }
        $eq = $line.IndexOf('=')
        if ($eq -le 0) { continue }
        $k = $line.Substring(0, $eq).Trim()
        if ([string]::Equals($k, $KeyName, [StringComparison]::OrdinalIgnoreCase)) {
            $val = $line.Substring($eq + 1).Trim()
            $commentIdx = $val.IndexOfAny(@(';', '#'))
            if ($commentIdx -ge 0) {
                $val = $val.Substring(0, $commentIdx).Trim()
            }
            return $val
        }
    }
    return ''
}

function Invoke-RemoveMod {
    param(
        [string]$ExplicitGamePath,
        [switch]$RestorePreExisting
    )

    $gtaDir = Find-Gta5LegacyPath -ExplicitPath $ExplicitGamePath
    $null   = Assert-ValidGta5LegacyDir -Dir $gtaDir
    Assert-GameNotRunning

    $manifest = $null
    $manifestMatchesTarget = $false
    if (Test-Path -LiteralPath $ManifestPath) {
        try {
            $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
            if ($manifest.GamePath) {
                $normManifest = [System.IO.Path]::GetFullPath($manifest.GamePath).TrimEnd('\', '/')
                $normTarget   = [System.IO.Path]::GetFullPath($gtaDir).TrimEnd('\', '/')
                $manifestMatchesTarget = [string]::Equals($normManifest, $normTarget, [StringComparison]::OrdinalIgnoreCase)
            }
        } catch { }
    }

    $knownModFiles = @(
        'dinput8.dll',
        'ScriptHookV.dll',
        'args.txt',
        'commandline.txt',
        'ScriptHookVDotNet-Loader.asi',
        'ScriptHookVDotNet.asi',
        'ScriptHookVDotNet.dll',
        'ScriptHookVDotNet2.dll',
        'ScriptHookVDotNet3.dll',
        'ScriptHookVDotNet.ini',
        'scripts\LemonUI.SHVDN3.dll',
        'scripts\StoryModeTrainer.dll',
        'scripts\StoryModeTrainer.pdb',
        'scripts\StoryModeTrainer.ini'
    )

    if ($manifestMatchesTarget -and $manifest -and $manifest.Files) {
        foreach ($f in $manifest.Files) {
            if ($f.RelPath -and ($knownModFiles -notcontains $f.RelPath)) {
                $knownModFiles += $f.RelPath
            }
        }
    }

    $runtimeLogs = @(
        'asiloader.log',
        'ScriptHookV.log',
        'ScriptHookVDotNet.log',
        'ScriptHookVDotNet-Loader.log'
    )

    $removedCount = 0
    foreach ($rel in ($knownModFiles + $runtimeLogs)) {
        $full = Join-Path $gtaDir $rel
        if (Test-Path -LiteralPath $full) {
            Remove-Item -LiteralPath $full -Force
            Write-Host "  Removed: $rel"
            $removedCount++
        }
    }

    foreach ($logPattern in @('asiloader*.log*', 'ScriptHook*.log*', 'ScriptHookVDotNet*.log*')) {
        Get-ChildItem -LiteralPath $gtaDir -Filter $logPattern -File -ErrorAction SilentlyContinue | ForEach-Object {
            Remove-Item -LiteralPath $_.FullName -Force
            Write-Host "  Removed: $($_.Name)"
            $removedCount++
        }
    }

    # Pre-existing files backed up before installation are preserved in backup\ by default so Remove
    # always leaves the GTA 5 root Clean Vanilla for GTA Online. Restore only when explicitly requested.
    if ($manifestMatchesTarget -and $manifest -and $manifest.BackupDir -and (Test-Path -LiteralPath $manifest.BackupDir) -and $manifest.BackedUpFiles) {
        if ($RestorePreExisting) {
            foreach ($rel in $manifest.BackedUpFiles) {
                $bkp = Join-Path $manifest.BackupDir $rel
                if (Test-Path -LiteralPath $bkp) {
                    $dst = Join-Path $gtaDir $rel
                    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dst) | Out-Null
                    Copy-Item -LiteralPath $bkp -Destination $dst -Force
                    Write-Host "  Restored pre-existing file: $rel"
                }
            }
        } else {
            Write-Host "  Preserved pre-existing file backup in: $($manifest.BackupDir) (omitted from GTA 5 root to guarantee Clean Vanilla state for Online)"
        }
    }

    $scriptsDir = Join-Path $gtaDir 'scripts'
    if (Test-Path -LiteralPath $scriptsDir) {
        $remainingInScripts = @(Get-ChildItem -LiteralPath $scriptsDir -Force -ErrorAction SilentlyContinue)
        if ($remainingInScripts.Count -eq 0) {
            Remove-Item -LiteralPath $scriptsDir -Force
            Write-Host "  Removed empty folder: scripts\"
        } else {
            Write-Warning "Kept scripts\ because non-trainer files remain inside it:"
            $remainingInScripts | ForEach-Object { Write-Warning "    $($_.Name)" }
        }
    }

    if ($manifestMatchesTarget -and (Test-Path -LiteralPath $ManifestPath)) {
        Remove-Item -LiteralPath $ManifestPath -Force
    }

    # Post-removal clean-state verification scan
    $suspects = @(Get-NonVanillaRootItems -GameDir $gtaDir)

    Write-Host ""
    Write-Host "[OK] Removed $removedCount mod/log file(s) from: $gtaDir"
    if ($suspects.Count -gt 0) {
        Write-Warning "Warning: Non-vanilla items still detected in GTA 5 root:"
        $suspects | ForEach-Object { Write-Warning "  - $($_.Name)" }
        Write-Warning "Resolve these before enabling BattlEye / going Online."
    } else {
        Write-Host "[VERIFIED VANILLA] Zero ASI loaders, mod DLLs, scripts, or mod logs remain."
        Write-Host "You can safely re-enable BattlEye in Rockstar Games Launcher and play GTA Online."
    }
}

function Show-ModStatus {
    param([string]$ExplicitGamePath)

    $gtaDir = Find-Gta5LegacyPath -ExplicitPath $ExplicitGamePath
    $gtaExe = Assert-ValidGta5LegacyDir -Dir $gtaDir
    $gtaVersion = ([Diagnostics.FileVersionInfo]::GetVersionInfo($gtaExe)).FileVersion

    $beSvc = Get-Service -Name 'BEService' -ErrorAction SilentlyContinue
    $beSvcState = if ($beSvc) { $beSvc.Status.ToString() } else { 'NotInstalled' }
    $argsPresent = Test-Path -LiteralPath (Join-Path $gtaDir 'args.txt')
    $cmdlinePresent = Test-Path -LiteralPath (Join-Path $gtaDir 'commandline.txt')

    Write-Host "=== GTA 5 Legacy Offline Trainer Status ==="
    Write-Host "Game Folder    : $gtaDir"
    Write-Host "GTA5.exe       : $gtaVersion (expected $ExpectedGameBranch)"
    Write-Host "BattlEye Files : GTA5_BE.exe=$((Test-Path (Join-Path $gtaDir 'GTA5_BE.exe'))), BEService_x64.exe=$((Test-Path (Join-Path $gtaDir 'BattlEye\BEService_x64.exe')))"
    Write-Host "BEService Win32: $beSvcState (read-only check) | args.txt: $argsPresent | commandline.txt: $cmdlinePresent"

    $manifestHashes = @{}
    if (Test-Path -LiteralPath $ManifestPath) {
        try {
            $mf = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
            foreach ($mfItem in @($mf.Files)) {
                if ($mfItem.RelPath -and $mfItem.Sha256) {
                    $manifestHashes[$mfItem.RelPath.ToLowerInvariant()] = [string]$mfItem.Sha256
                }
            }
        } catch { }
    }

    $entries = Get-DistStageEntries
    $present = 0
    $matched = 0
    Write-Host ""
    Write-Host "Deployed Payload Check:"
    foreach ($item in $entries) {
        $dst = Join-Path $gtaDir $item.RelPath
        $exists = Test-Path -LiteralPath $dst
        $stageExists = Test-Path -LiteralPath $item.StagePath
        if ($exists) { $present++ }
        $state = if (-not $exists) {
            'MISSING'
        } elseif (-not $stageExists) {
            'PRESENT (no dist hash)'
        } else {
            $h1 = (Get-FileHash -LiteralPath $item.StagePath -Algorithm SHA256).Hash
            $h2 = (Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash
            $mfKey = $item.RelPath.ToLowerInvariant()
            if ($h1 -eq $h2) {
                $matched++; 'OK (hash match)'
            } elseif ($manifestHashes.ContainsKey($mfKey) -and $manifestHashes[$mfKey] -eq $h2) {
                $matched++; 'OK (manifest match)'
            } else {
                'MISMATCH'
            }
        }
        Write-Host ("  {0,-32} : {1}" -f $item.RelPath, $state)
    }

    $logs = @(Get-ChildItem -LiteralPath $gtaDir -Force | Where-Object {
        $_.Name -like 'asiloader*.log*' -or
        $_.Name -like 'ScriptHook*.log*' -or
        $_.Name -like 'ScriptHookVDotNet*.log*'
    })
    $suspects = @(Get-NonVanillaRootItems -GameDir $gtaDir)
    $expectedRootNames = @($entries | ForEach-Object { ($_.RelPath -split '[\\/]+')[0] }) + @('scripts') | Select-Object -Unique
    $logNames = @($logs | ForEach-Object { $_.Name })
    $extraSuspects = @($suspects | Where-Object { ($expectedRootNames -notcontains $_.Name) -and ($logNames -notcontains $_.Name) })

    Write-Host ""
    if ($present -eq 0 -and -not (Test-Path -LiteralPath (Join-Path $gtaDir 'scripts')) -and $logs.Count -eq 0 -and $suspects.Count -eq 0) {
        Write-Host "Overall State  : CLEAN VANILLA (Safe to re-enable BattlEye & go Online)"
        if ($beSvcState -eq 'Running') {
            Write-Warning "BEService Windows service is currently Running. Ensure no BattlEye game session is active before modifying files."
        }
    } elseif ($present -eq $entries.Count -and $matched -eq $entries.Count -and $extraSuspects.Count -eq 0) {
        Write-Host "Overall State  : OFFLINE-MODDED (All $($entries.Count)/$($entries.Count) files verified - Story Mode Only!)"
    } elseif ($present -eq $entries.Count -and $matched -eq $entries.Count) {
        Write-Host "Overall State  : OFFLINE-MODDED + EXTRA NON-VANILLA ITEMS ($($entries.Count)/$($entries.Count) trainer files verified, $($extraSuspects.Count) unexpected item(s))"
        Write-Host ("  Unexpected root items: {0}" -f (($extraSuspects | ForEach-Object { $_.Name }) -join ', '))
    } else {
        Write-Host "Overall State  : PARTIAL / MODIFIED ($present/$($entries.Count) trainer files, $($logs.Count) log(s), $($suspects.Count) non-vanilla item(s))"
        if ($suspects.Count -gt 0) {
            Write-Host ("  Non-vanilla root items: {0}" -f (($suspects | ForEach-Object { $_.Name }) -join ', '))
        }
    }

    if ($logs.Count -gt 0) {
        Write-Host ""
        Write-Host "Runtime Logs Found:"
        foreach ($log in $logs) {
            Write-Host "--- $($log.Name) (last 12 lines) ---"
            Get-Content -LiteralPath $log.FullName -Tail 12
        }
    }
}

function Test-PeMachineIsX64 {
    param([string]$FilePath)

    $fs = [System.IO.File]::OpenRead($FilePath)
    try {
        $br = New-Object System.IO.BinaryReader($fs)
        if ($br.ReadUInt16() -ne 0x5A4D) { throw "Invalid MZ header: $FilePath" }
        $fs.Seek(0x3C, [System.IO.SeekOrigin]::Begin) | Out-Null
        $peOffset = $br.ReadInt32()
        $fs.Seek($peOffset, [System.IO.SeekOrigin]::Begin) | Out-Null
        if ($br.ReadUInt32() -ne 0x00004550) { throw "Invalid PE signature: $FilePath" }
        $machine = $br.ReadUInt16()
        if ($machine -ne 0x8664) { throw ("Expected x64 (0x8664) PE in {0}, got 0x{1:X4}" -f $FilePath, $machine) }
    } finally {
        $fs.Dispose()
    }
}

function Invoke-VerifySetup {
    param([string]$ExplicitGamePath)

    Write-Host "=== 1. Clean Release|x64 Build & Dist Sync ==="
    Invoke-SyncDist

    Write-Host "=== 2. Game & Runtime Prerequisites ==="
    $gtaDir = Find-Gta5LegacyPath -ExplicitPath $ExplicitGamePath
    $gtaExe = Assert-ValidGta5LegacyDir -Dir $gtaDir
    $gtaVer = ([Diagnostics.FileVersionInfo]::GetVersionInfo($gtaExe)).FileVersion
    if ($gtaVer -ne $ExpectedGameBranch) {
        throw "GTA5.exe version $gtaVer does not match expected $ExpectedGameBranch"
    }
    Write-Host "  [PASS] GTA 5 Legacy version: $gtaVer ($gtaDir)"

    $netRel = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue).Release
    if (-not $netRel -or $netRel -lt 528040) {
        throw ".NET Framework >= 4.8 required (Registry Release >= 528040); found '$netRel'"
    }
    Write-Host "  [PASS] .NET Framework 4.8+ present (Release=$netRel)"

    $vc = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64' -ErrorAction SilentlyContinue
    if (-not $vc -or $vc.Major -lt 14) {
        throw "Visual C++ v14 x64 Redistributable required"
    }
    Write-Host "  [PASS] VC++ x64 Redistributable present ($($vc.Version))"

    Write-Host "=== 3. Native PE Architecture & Version Check ==="
    foreach ($nativeRel in @('dinput8.dll', 'ScriptHookV.dll', 'ScriptHookVDotNet-Loader.asi')) {
        $p = Join-Path $DistRoot "root\$nativeRel"
        Test-PeMachineIsX64 -FilePath $p
        $vi = [Diagnostics.FileVersionInfo]::GetVersionInfo($p)
        Write-Host "  [PASS] x64 PE: $nativeRel (FileVersion=$($vi.FileVersion), Product=$($vi.ProductName))"
    }

    $shvVer = ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $DistRoot 'root\ScriptHookV.dll'))).FileVersion
    if ($shvVer -ne $ScriptHookVVersion) {
        throw "Unexpected ScriptHookV.dll version: $shvVer"
    }

    Write-Host "=== 4. Managed Assembly, IL Token & API Resolution ==="
    $shvdn3Path  = Join-Path $DistRoot 'root\ScriptHookVDotNet3.dll'
    $lemonPath   = Join-Path $DistRoot 'scripts\LemonUI.SHVDN3.dll'
    $trainerPath = Join-Path $DistRoot 'scripts\StoryModeTrainer.dll'

    $inspectScript = @"
`$ErrorActionPreference = 'Stop'
`$shvdn3  = [Reflection.Assembly]::LoadFrom('$($shvdn3Path -replace "'","''")')
`$null    = [Reflection.Assembly]::LoadFrom('$($lemonPath -replace "'","''")')
`$trainer = [Reflection.Assembly]::LoadFrom('$($trainerPath -replace "'","''")')

`$gvEnum = `$shvdn3.GetType('GTA.GameVersion')
if (-not ([Enum]::GetNames(`$gvEnum) -contains 'v1_0_3889_0')) {
    throw 'SHVDN3 GameVersion enum does not include v1_0_3889_0!'
}
Write-Host ("  [PASS] SHVDN3 v{0} supports GTA.GameVersion.v1_0_3889_0" -f `$shvdn3.GetName().Version)

`$scriptBaseType = `$shvdn3.GetType('GTA.Script')
`$exportedScripts = @(`$trainer.GetExportedTypes() | Where-Object { `$_.IsSubclassOf(`$scriptBaseType) -and -not `$_.IsAbstract -and `$_.GetConstructor([Type]::EmptyTypes) -ne `$null })
if (`$exportedScripts.Count -eq 0) {
    throw 'No public non-abstract GTA.Script subclass with default constructor found in StoryModeTrainer.dll'
}
Write-Host ("  [PASS] Exported GTA.Script subclass verified: {0}" -f ((`$exportedScripts | ForEach-Object { `$_.FullName }) -join ', '))

`$requiredMethods = @(
    'EnforceStoryModeOnlineLockout',
    'EnforceSelectorOnlineSlotLockout',
    'IsSupportedScriptGlobalBuild',
    'IsMultiplayerOrTransitionActive',
    'AttemptCancelOnlineTransition',
    'IsMultiplayerActive',
    'WaitWithOnlineGuard',
    'ApplyPlayerAndVehicleProtections',
    'TryPromptUserInput',
    'TryFindSolidSurfaceAt'
)
`$bindingFlags = [Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
foreach (`$mName in `$requiredMethods) {
    if (`$null -eq `$exportedScripts[0].GetMethod(`$mName, `$bindingFlags)) {
        throw "Missing required Online-lockout or safety method on StoryModeTrainer: `$mName"
    }
}
Write-Host ("  [PASS] Required Online-lockout & safety methods verified: {0}" -f (`$requiredMethods -join ', '))

`$assetsType = `$trainer.GetType('StoryModeTrainerMod.OnlineAssetsController')
if (`$null -eq `$assetsType) {
    throw 'Missing StoryModeTrainerMod.OnlineAssetsController type in StoryModeTrainer.dll'
}
`$requiredAssetMethods = @(
    'SetMpMapsMode',
    'PrepareZoneEnvironmentForCoords',
    'EnsureInteriorPinnedAt',
    'IsInteriorReadyAt',
    'SetCayoPericoEnabled',
    'SetNorthYanktonEnabled',
    'SetDiamondCasinoEnabled',
    'SetAircraftCarrierEnabled',
    'SetSuperyachtEnabled',
    'SetGunrunningBunkerEnabled',
    'SetDoomsdayFacilityEnabled',
    'SetCargoShipEnabled',
    'SetStoryInteriorsEnabled',
    'RevertAllToStoryWorld'
)
foreach (`$amName in `$requiredAssetMethods) {
    if (`$null -eq `$assetsType.GetMethod(`$amName, `$bindingFlags)) {
        throw "Missing required offline map/collision method on OnlineAssetsController: `$amName"
    }
}
Write-Host ("  [PASS] Required offline IPL & collision methods verified: {0}" -f (`$requiredAssetMethods -join ', '))

`$mod = `$trainer.ManifestModule
`$resolvedTypes = 0
for (`$i = 1; ; `$i++) {
    `$tok = 0x01000000 -bor `$i
    try { `$null = `$mod.ResolveType(`$tok); `$resolvedTypes++ } catch [System.ArgumentOutOfRangeException] { break }
}
`$resolvedMembers = 0
for (`$i = 1; ; `$i++) {
    `$tok = 0x0A000000 -bor `$i
    try { `$null = `$mod.ResolveMember(`$tok); `$resolvedMembers++ } catch [System.ArgumentOutOfRangeException] { break }
}
Write-Host ("  [PASS] Resolved {0} external TypeRef and {1} external MemberRef tokens cleanly" -f `$resolvedTypes, `$resolvedMembers)
`$dllBytes = [IO.File]::ReadAllBytes('$($trainerPath -replace "'","''")')
function Test-ByteSequence([byte[]]`$haystack, [byte[]]`$needle) {
    for (`$idx = 0; `$idx -le (`$haystack.Length - `$needle.Length); `$idx++) {
        if (`$haystack[`$idx] -eq `$needle[0]) {
            `$ok = `$true
            for (`$k = 1; `$k -lt `$needle.Length; `$k++) {
                if (`$haystack[`$idx + `$k] -ne `$needle[`$k]) { `$ok = `$false; break }
            }
            if (`$ok) { return `$true }
        }
    }
    return `$false
}
# 0x9BAE5AD2508DF078 (SET_INSTANCE_PRIORITY_MODE) must NEVER exist in compiled IL
if (Test-ByteSequence `$dllBytes ([BitConverter]::GetBytes([Convert]::ToUInt64('9BAE5AD2508DF078', 16)))) {
    throw 'Compiled StoryModeTrainer.dll contains forbidden native hash 0x9BAE5AD2508DF078!'
}
# Verify compiled IL embeds SET_ISLAND_ENABLED (0x9A9D1BA639675CF1), ON_ENTER_MP (0x0888C3502DBBEEF5), SET_INSTANCE_PRIORITY_HINT (0xC5F0A8EBD3F361CE), and EXTEND_WORLD_BOUNDARY_FOR_PLAYER (0x5006D96C995A5827)
foreach (`$reqHex in @('9A9D1BA639675CF1', '0888C3502DBBEEF5', 'C5F0A8EBD3F361CE', '5006D96C995A5827')) {
    if (-not (Test-ByteSequence `$dllBytes ([BitConverter]::GetBytes([Convert]::ToUInt64(`$reqHex, 16))))) {
        throw ("Compiled StoryModeTrainer.dll missing expected native hash 0x{0}" -f `$reqHex)
    }
}
Write-Host '  [PASS] Binary IL scan verified offline map/collision hashes & zero Online loading-mode native bytes'
"@
    $tmpInspectPs1 = Join-Path ([IO.Path]::GetTempPath()) ("smt_verify_" + [Guid]::NewGuid().ToString('N') + '.ps1')
    try {
        [IO.File]::WriteAllText($tmpInspectPs1, $inspectScript, [Text.Encoding]::UTF8)
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $tmpInspectPs1
        if ($LASTEXITCODE -ne 0) {
            throw "Managed assembly & IL token inspection failed with exit code $LASTEXITCODE"
        }
    } finally {
        if (Test-Path -LiteralPath $tmpInspectPs1) {
            Remove-Item -LiteralPath $tmpInspectPs1 -Force -ErrorAction SilentlyContinue
        }
    }

    Write-Host "=== 5. INI Config Sanity & Non-Conflicting Keybind Check ==="
    Add-Type -AssemblyName System.Windows.Forms

    $shvdnIni = Get-Content -LiteralPath (Join-Path $DistRoot 'root\ScriptHookVDotNet.ini') -Raw
    $trainerIni = Get-Content -LiteralPath (Join-Path $DistRoot 'scripts\StoryModeTrainer.ini') -Raw
    $shvdnConsoleKey  = Get-IniSettingValue -IniText $shvdnIni -KeyName 'ConsoleKeyBinding'
    $shvdnReloadKey   = Get-IniSettingValue -IniText $shvdnIni -KeyName 'ReloadKeyBinding'
    $trainerToggleKey = Get-IniSettingValue -IniText $trainerIni -KeyName 'MenuToggleKey'
    $noClipToggleKey  = Get-IniSettingValue -IniText $trainerIni -KeyName 'NoClipToggleKey'
    $noClipSpeedRaw   = Get-IniSettingValue -IniText $trainerIni -KeyName 'BaseSpeed'

    if ($shvdnConsoleKey -ine 'None') {
        throw "ScriptHookVDotNet.ini ConsoleKeyBinding must be 'None' (found '$shvdnConsoleKey') so Online-lockout cannot be aborted in-game."
    }
    if ($shvdnReloadKey -ine 'None') {
        throw "ScriptHookVDotNet.ini ReloadKeyBinding must be 'None' (found '$shvdnReloadKey')."
    }

    [System.Windows.Forms.Keys]$parsedMenuKey = [System.Windows.Forms.Keys]::None
    try {
        $parsedMenuKey = [System.Windows.Forms.Keys][Enum]::Parse([System.Windows.Forms.Keys], $trainerToggleKey, $true)
    } catch {
        $parsedMenuKey = [System.Windows.Forms.Keys]::None
    }
    if (-not $trainerToggleKey -or
        -not [Enum]::IsDefined([System.Windows.Forms.Keys], $parsedMenuKey) -or
        $parsedMenuKey -eq [System.Windows.Forms.Keys]::None) {
        throw "Invalid MenuToggleKey in StoryModeTrainer.ini: '$trainerToggleKey'"
    }

    [System.Windows.Forms.Keys]$parsedNoClipKey = [System.Windows.Forms.Keys]::None
    try {
        $parsedNoClipKey = [System.Windows.Forms.Keys][Enum]::Parse([System.Windows.Forms.Keys], $noClipToggleKey, $true)
    } catch {
        $parsedNoClipKey = [System.Windows.Forms.Keys]::None
    }
    if (-not $noClipToggleKey -or
        -not [Enum]::IsDefined([System.Windows.Forms.Keys], $parsedNoClipKey) -or
        $parsedNoClipKey -eq [System.Windows.Forms.Keys]::None) {
        throw "Invalid NoClipToggleKey in StoryModeTrainer.ini: '$noClipToggleKey'"
    }

    [double]$parsedSpeed = 0
    if (-not [double]::TryParse($noClipSpeedRaw, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$parsedSpeed) -or
        [double]::IsNaN($parsedSpeed) -or [double]::IsInfinity($parsedSpeed) -or
        $parsedSpeed -lt 2.0 -or $parsedSpeed -gt 200.0) {
        throw "Invalid NoClip BaseSpeed in StoryModeTrainer.ini: '$noClipSpeedRaw' (must be 2..200)"
    }

    if ($trainerToggleKey -ieq $shvdnConsoleKey) {
        throw "Key conflict: MenuToggleKey ($trainerToggleKey) matches SHVDN ConsoleKeyBinding ($shvdnConsoleKey)"
    }
    if ($noClipToggleKey -ieq $shvdnConsoleKey) {
        throw "Key conflict: NoClipToggleKey ($noClipToggleKey) matches SHVDN ConsoleKeyBinding ($shvdnConsoleKey)"
    }
    if ($parsedMenuKey -eq $parsedNoClipKey) {
        throw "Key conflict: MenuToggleKey ($trainerToggleKey) matches NoClipToggleKey ($noClipToggleKey)"
    }
    Write-Host "  [PASS] Keybinds valid: Menu=$parsedMenuKey, NoClip=$parsedNoClipKey, BaseSpeed=$parsedSpeed (no conflict with SHVDN Console=$shvdnConsoleKey)"

    Write-Host "=== 6. Staged dist\ Completeness ==="
    $stageEntries = Get-DistStageEntries
    foreach ($entry in $stageEntries) {
        if (-not (Test-Path -LiteralPath $entry.StagePath)) {
            throw "Missing dist file: $($entry.StagePath)"
        }
    }
    Write-Host "  [PASS] All $($stageEntries.Count) staged runtime files verified present."

    Write-Host "=== 7. Offline Compliance, Anti-Online Safeguard & Map Collision Audit ==="
    $argsTxtPath = Join-Path $DistRoot 'root\args.txt'
    $argsTxt = (Get-Content -LiteralPath $argsTxtPath -Raw).Trim()
    if ($argsTxt -notmatch '(?i)(?:^|\s)-nobattleye(?:\s|$)' -or $argsTxt -notmatch '(?i)(?:^|\s)-noBE(?:\s|$)') {
        throw "dist\root\args.txt is missing required -nobattleye / -noBE flags: '$argsTxt'"
    }
    Write-Host "  [PASS] dist\root\args.txt disables BattlEye cleanly: '$argsTxt'"

    $cmdlineTxtPath = Join-Path $DistRoot 'root\commandline.txt'
    $cmdlineTxt = (Get-Content -LiteralPath $cmdlineTxtPath -Raw).Trim()
    if ($cmdlineTxt -notmatch '(?im)(?:^|\s)-nobattleye(?:\s|$)') {
        throw "dist\root\commandline.txt is missing required -nobattleye flag: '$cmdlineTxt'"
    }
    if ($cmdlineTxt -match '(?im)(?:^|\s)-scOfflineOnly(?:\s|$)') {
        Write-Host "  [PASS] dist\root\commandline.txt enforces -scOfflineOnly & -nobattleye at GTA5.exe engine boot."
    } else {
        Write-Host "  [PASS] dist\root\commandline.txt enforces -nobattleye (-scOfflineOnly omitted for Rockstar Launcher verification; runtime Online lockout active)."
    }

    $csFiles = @(Get-ChildItem -LiteralPath $WorkspaceRoot -Filter '*.cs' -File)
    if ($csFiles.Count -eq 0) {
        throw "No C# source files found under $WorkspaceRoot"
    }
    $forbiddenPattern = 'SET_INSTANCE_PRIORITY_MODE|Game\.GetUserInput|NETWORK_DO_TRANSITION_|NETWORK_LAUNCH_TRANSITION|NETWORK_HOST_TRANSITION|NETWORK_SESSION_DO_|NETWORK_SESSION_JOIN_|MP[0-9]+_|MPPLY_|GIVE_ACHIEVEMENT_TO_PLAYER|SET_ACHIEVEMENT|SC_ACHIEVEMENT|STAT_SAVE|NETWORK_SET_SCRIPT_IS_SAFE_FOR_NETWORK_GAME|SET_MULTIPLAYER_.*CASH|NET_GAMESERVER|OPEN_COMMERCE_STORE|PLAYSTATS_|NETWORK_SESSION_HOST|NETWORK_JOIN_|System\.Net\.|Global_157|Global_262|Global_269|Global_185|\b157[456][0-9]{3}\b|\b269[0-9]{4}\b'
    $forbiddenMatches = @(Select-String -Path $csFiles.FullName -Pattern $forbiddenPattern)
    if ($forbiddenMatches.Count -gt 0) {
        $details = ($forbiddenMatches | ForEach-Object { "$($_.Filename):$($_.LineNumber): $($_.Line.Trim())" }) -join '; '
        throw "Forbidden Online/transition/blocking/telemetry/achievement/commerce/MP-global API pattern found in C# source: $details"
    }
    Write-Host "  [PASS] Source audit: $($csFiles.Count) C# files contain zero SET_INSTANCE_PRIORITY_MODE, blocking GetUserInput, MP*/MPPLY_ stats, MP globals, achievement unlocks, net-safe flags, commerce/telemetry, or network APIs."

    $onlineAssetsSource = Get-Content -LiteralPath (Join-Path $WorkspaceRoot 'OnlineAssetsController.cs') -Raw
    $trainerSource      = Get-Content -LiteralPath (Join-Path $WorkspaceRoot 'StoryModeTrainer.cs') -Raw
    foreach ($requiredToken in @(
        'SET_ISLAND_ENABLED',
        'SET_INSTANCE_PRIORITY_HINT',
        'ON_ENTER_MP',
        'ON_ENTER_SP',
        'CAP_INTERIOR',
        'EXTEND_WORLD_BOUNDARY_FOR_PLAYER',
        '"smboat"',
        '"vw_dlc_casino_apart"',
        '"prologue03_grv_cov"'
    )) {
        if ($onlineAssetsSource -notmatch [Regex]::Escape($requiredToken)) {
            throw "OnlineAssetsController.cs is missing required offline map/collision token: $requiredToken"
        }
    }
    foreach ($requiredTrainerToken in @(
        'SET_THIS_SCRIPT_CAN_BE_PAUSED',
        'DISPLAY_ONSCREEN_KEYBOARD',
        'World.Raycast'
    )) {
        if ($trainerSource -notmatch [Regex]::Escape($requiredTrainerToken)) {
            throw "StoryModeTrainer.cs is missing required safeguard/collision token: $requiredTrainerToken"
        }
    }
    Write-Host "  [PASS] Positive map-collision & pause-proof Online-lockout architecture tokens verified."

    $launchBatPath = Join-Path $WorkspaceRoot 'Launch-GTA5-Offline.bat'
    if (-not (Test-Path -LiteralPath $launchBatPath)) {
        throw "Missing Launch-GTA5-Offline.bat"
    }
    $launchBatText = Get-Content -LiteralPath $launchBatPath -Raw
    if ($launchBatText -notmatch '(?i)-WaitAndClean') {
        throw "Launch-GTA5-Offline.bat must pass -WaitAndClean for automatic cleanup on exit."
    }
    Write-Host "  [PASS] Launch-GTA5-Offline.bat enforces -WaitAndClean lifecycle."
}

function Invoke-LaunchOffline {
    param(
        [string]$ExplicitGamePath,
        [switch]$AutoClean
    )

    $gtaDir = Find-Gta5LegacyPath -ExplicitPath $ExplicitGamePath
    $null   = Assert-ValidGta5LegacyDir -Dir $gtaDir

    $isSteamCopy = ($gtaDir -match '\\steamapps\\') -or (Test-Path -LiteralPath (Join-Path $gtaDir 'steam_api64.dll'))
    $playGtaExe  = Join-Path $gtaDir 'PlayGTAV.exe'

    Invoke-InstallMod -ExplicitGamePath $gtaDir
    $cleanedUp = $false

    try {
        $steamRegPath = 'HKCU:\SOFTWARE\Valve\Steam\Apps\271590'
        $steamApp = Get-ItemProperty -LiteralPath $steamRegPath -ErrorAction SilentlyContinue
        if ($steamApp -and ($steamApp.Updating -eq 1)) {
            Write-Warning "Steam reports GTA 5 (AppID 271590) is currently updating. Launch may wait until Steam finishes updating."
        }

        Write-Host ""
        Write-Host "COMPLIANCE REMINDER:"
        Write-Host "  - Offline Story Mode ONLY (-scOfflineOnly -nobattleye via commandline.txt + staged args.txt '-nobattleye -noBE')."
        Write-Host "  - Do NOT join GTA Online while modded; BattlEye & Social Club Online are disabled for this session."
        Write-Host ""

        $steamArgs = if ($OmitScOfflineOnly) { '-nobattleye' } else { '-nobattleye -scOfflineOnly' }
        $playArgs  = if ($OmitScOfflineOnly) { @('-nobattleye', '-noBE') } else { @('-nobattleye', '-noBE', '-scOfflineOnly') }

        if ($isSteamCopy) {
            Write-Host "Launching GTA 5 Legacy via Steam with $steamArgs..."
            Start-Process "steam://rungameid/271590//$steamArgs"
        } elseif (Test-Path -LiteralPath $playGtaExe) {
            Write-Host ("Launching GTA 5 Legacy via PlayGTAV.exe with {0}..." -f ($playArgs -join ' '))
            Start-Process -FilePath $playGtaExe -ArgumentList $playArgs -WorkingDirectory $gtaDir
        } else {
            Write-Warning "Neither Steam marker nor PlayGTAV.exe found; attempting Steam AppID 271590 launch with $steamArgs..."
            Start-Process "steam://rungameid/271590//$steamArgs"
        }

        if (-not $AutoClean) {
            $cleanedUp = $true
            Write-Host "Run 2-Disable-And-CleanForOnline.bat (or Manage-StoryTrainer.ps1 -Action Remove) after exiting GTA 5."
            return
        }

        Write-Host ""
        Write-Host "[WaitAndClean] Monitoring GTA 5 session (auto-cleanup will run on exit)..."
        Write-Host "Keep this window open during your offline session (or run 2-Disable-And-CleanForOnline.bat manually later)."

        $gameSeen = $false
        $beWarned = $false
        $waitElapsedSec = 0
        $maxStartWaitSec = 180

        while ($waitElapsedSec -lt $maxStartWaitSec) {
            Start-Sleep -Seconds 2
            $waitElapsedSec += 2

            $beActive = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
                $_.ProcessName -in @('GTA5_BE', 'BEService_x64', 'BEService')
            })
            $beSvc = Get-Service -Name 'BEService' -ErrorAction SilentlyContinue
            if (-not $beWarned -and ($beActive.Count -gt 0 -or ($beSvc -and $beSvc.Status -eq 'Running'))) {
                $beWarned = $true
                try { [Console]::Beep(880, 400) } catch { }
                Write-Warning "[COMPLIANCE ALERT] BattlEye (GTA5_BE / BEService) started during launch! Exit GTA 5 immediately and avoid GTA Online while modded."
            }

            $gtaProc = Get-Process -Name 'GTA5' -ErrorAction SilentlyContinue
            if ($gtaProc) {
                $gameSeen = $true
                Write-Host "[WaitAndClean] GTA5.exe detected (PID $(($gtaProc | Select-Object -First 1).Id)). Waiting for session exit..."
                break
            }

            $steamNow = Get-ItemProperty -LiteralPath $steamRegPath -ErrorAction SilentlyContinue
            $launcherRunning = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -in @('PlayGTAV', 'GTAVLauncher') }
            if (($steamNow -and ($steamNow.Updating -eq 1 -or $steamNow.Running -eq 1)) -or $launcherRunning) {
                if ($waitElapsedSec -gt ($maxStartWaitSec - 20)) {
                    $waitElapsedSec = $maxStartWaitSec - 20
                }
            }
        }

        if ($gameSeen) {
            while ($true) {
                Start-Sleep -Seconds 2

                $beActive = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
                    $_.ProcessName -in @('GTA5_BE', 'BEService_x64', 'BEService')
                })
                $beSvc = Get-Service -Name 'BEService' -ErrorAction SilentlyContinue
                if (-not $beWarned -and ($beActive.Count -gt 0 -or ($beSvc -and $beSvc.Status -eq 'Running'))) {
                    $beWarned = $true
                    try { [Console]::Beep(880, 400) } catch { }
                    Write-Warning "[COMPLIANCE ALERT] BattlEye became active during this session! Exit GTA 5 immediately."
                }

                $stillRunning = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -in @('GTA5', 'GTA5_BE', 'PlayGTAV', 'GTAVLauncher') }
                if (-not $stillRunning) {
                    break
                }
            }

            Write-Host ""
            Write-Host "[WaitAndClean] GTA 5 exited. Waiting 3s for Rockstar Cloud Save / file handles to settle..."
            Start-Sleep -Seconds 3

            # Read-only grace wait if BEService happened to spin up and is winding down naturally
            for ($i = 0; $i -lt 7; $i++) {
                $beSvcNow = Get-Service -Name 'BEService' -ErrorAction SilentlyContinue
                if (-not $beSvcNow -or $beSvcNow.Status -eq 'Stopped') { break }
                Start-Sleep -Seconds 2
            }

            Invoke-RemoveMod -ExplicitGamePath $gtaDir
            $cleanedUp = $true
        } else {
            $launcherStillRunning = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -in @('GTA5', 'PlayGTAV', 'GTAVLauncher') }
            if ($launcherStillRunning) {
                $cleanedUp = $true
                Write-Warning "[WaitAndClean] GTA5.exe did not start within ${maxStartWaitSec}s, while launcher processes are still running. Skipping auto-removal; run 2-Disable-And-CleanForOnline.bat after closing."
            } else {
                Write-Warning "[WaitAndClean] GTA5.exe did not start within ${maxStartWaitSec}s (launch canceled?). Cleaning up staged mod files..."
                Invoke-RemoveMod -ExplicitGamePath $gtaDir
                $cleanedUp = $true
            }
        }
    } finally {
        if ($AutoClean -and (-not $cleanedUp)) {
            $activeGame = Get-Process -ErrorAction SilentlyContinue | Where-Object {
                $_.ProcessName -in @('GTA5', 'GTA5_BE', 'PlayGTAV', 'GTAVLauncher')
            }
            if (-not $activeGame) {
                Write-Warning "[WaitAndClean] Session monitor interrupted; cleaning up staged mod files..."
                try {
                    Invoke-RemoveMod -ExplicitGamePath $gtaDir
                } catch {
                    Write-Warning "Auto-cleanup failed: $($_.Exception.Message). Run 2-Disable-And-CleanForOnline.bat manually."
                }
            } else {
                Write-Warning "[WaitAndClean] Session monitor interrupted while GTA 5 is still running! Run 2-Disable-And-CleanForOnline.bat after closing GTA 5."
            }
        }
    }
}

switch ($Action) {
    'SyncDist'      { Invoke-SyncDist }
    'Install'       { Invoke-InstallMod -ExplicitGamePath $GamePath }
    'Remove'        { Invoke-RemoveMod -ExplicitGamePath $GamePath -RestorePreExisting:$RestorePreExistingBackup }
    'Status'        { Show-ModStatus -ExplicitGamePath $GamePath }
    'Verify'        { Invoke-VerifySetup -ExplicitGamePath $GamePath }
    'LaunchOffline' { Invoke-LaunchOffline -ExplicitGamePath $GamePath -AutoClean:$WaitAndClean }
}
