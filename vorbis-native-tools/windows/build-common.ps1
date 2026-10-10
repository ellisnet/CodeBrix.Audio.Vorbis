# =============================================================================================
# build-common.ps1 - shared machinery for the Windows codebrix_vorbis builds (win-x64, win-arm64)
# =============================================================================================
#
#   NEVER YET RUN. Written on Linux on 2026-10-10, modelled closely on CodeBrix.Sdl3's
#   sdl3-native-tools\windows\build-common.ps1 (which carries the first-run fixes of the
#   llama-native-tools and dav1d-native-tools Windows scripts: the dumpbin export parser, the cl
#   banner capture, choosing the Visual Studio instance by its own filesystem, the PATH trap,
#   the stderr-as-ErrorRecord trap of Windows PowerShell 5.1). Expect to fix something on the
#   first real run; fix it IN THE SCRIPT and record it in ..\BUILD-PROVENANCE.txt.
#
# Dot-source this from an architecture script; do not run it directly.
#
#     . "$PSScriptRoot\build-common.ps1"
#
# Everything it needs is in this repository: ..\ogg and ..\vorbis (the unmodified Xiph release
# trees), ..\shim (the codebrix_vorbis shim + its CMakeLists.txt), ..\patches (empty),
# ..\smoke-test.c, and ..\linux\pins.env (the version pins shared by every platform; only its
# platform-neutral keys are used here). Nothing is downloaded.
# =============================================================================================

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------------------------
# Pins: ..\linux\pins.env. Keys used: OGG_DIR, OGG_VERSION, VORBIS_DIR, VORBIS_VERSION,
# REQUIRED_SYMBOLS, LICENSE_VORBIS_NAME, LICENSE_OGG_NAME.
# ---------------------------------------------------------------------------------------------
function Read-Pins {
    param([Parameter(Mandatory)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "pins.env was not found at $Path. It is part of the repository - if it is missing after a clone, check the root .gitignore's blanket '*.env' rule (see ..\.gitignore)."
    }
    $pins = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
        $eq = $trimmed.IndexOf('=')
        if ($eq -lt 1) { continue }
        $key = $trimmed.Substring(0, $eq).Trim()
        $value = $trimmed.Substring($eq + 1).Trim()
        if ($value.Length -ge 2 -and $value.StartsWith('"') -and $value.EndsWith('"')) {
            $value = $value.Substring(1, $value.Length - 2)
        }
        $pins[$key] = $value
    }
    foreach ($required in @('OGG_DIR', 'OGG_VERSION', 'VORBIS_DIR', 'VORBIS_VERSION', 'REQUIRED_SYMBOLS', 'LICENSE_VORBIS_NAME', 'LICENSE_OGG_NAME')) {
        if (-not $pins.ContainsKey($required)) { throw "pins.env has no $required. The Windows scripts read it from ..\linux\pins.env." }
    }
    return $pins
}

# ---------------------------------------------------------------------------------------------
# The CMake options for Windows (MSVC through CMake's Visual Studio generator, -A x64 / -A ARM64):
#   CMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded   STATIC CRT: the DLL imports KERNEL32.dll only and
#                                              no user ever needs a Visual C++ Redistributable
#   /Z7 + /DEBUG ... /PDBALTPATH:%_PDB%        a .pdb for crash triage (Sdl3's recipe: /Z7 keeps
#                                              debug info in the objects, /DEBUG writes the pdb but
#                                              would switch /OPT:REF and /OPT:ICF off, so both are
#                                              restored; /PDBALTPATH records only the file name).
#                                              Code generation is unchanged.
# CMAKE_C_FLAGS_RELEASE is CMake's MSVC default ("/O2 /Ob2 /DNDEBUG") plus /Z7. The shim's
# CMakeLists adds /W4 for the shim and /w for the upstream sources. Exports come ONLY from the
# __declspec(dllexport) in codebrix_vorbis.h; no .def file is used, so libogg/libvorbis export
# nothing.
# ---------------------------------------------------------------------------------------------
function Get-VorbisWindowsCmakeOptions {
    return @(
        '-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded',
        '-DCMAKE_C_FLAGS_RELEASE=/O2 /Ob2 /DNDEBUG /Z7',
        '-DCMAKE_SHARED_LINKER_FLAGS_RELEASE=/INCREMENTAL:NO /DEBUG /OPT:REF /OPT:ICF /PDBALTPATH:%_PDB%'
    )
}

# Every DLL the shipped codebrix_vorbis.dll may import. With the static CRT, libogg + libvorbis +
# the shim need nothing but KERNEL32. VCRUNTIME140.dll / ucrtbase.dll / api-ms-win-crt-*.dll
# would mean the static CRT did not take. (Case-insensitive comparison: PE import names are.)
$script:AllowedDependents = @('KERNEL32.dll')

# ---------------------------------------------------------------------------------------------
# Native commands. Windows PowerShell 5.1 turns every STDERR line of a native command into an
# ErrorRecord when redirected with 2>&1, and with $ErrorActionPreference = 'Stop' the first one
# would abort the script. So native tools always run through this helper.
# ---------------------------------------------------------------------------------------------
function Invoke-Native {
    param(
        [Parameter(Mandatory)][string] $Command,
        [string[]] $Arguments = @()
    )
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $Command @Arguments 2>&1 | ForEach-Object { "$_" }
    }
    finally { $ErrorActionPreference = $saved }
    return $output
}

# ---------------------------------------------------------------------------------------------
# Visual Studio discovery. Component checks read the filesystem of the SELECTED installation,
# never `vswhere -requires` (which searches ALL instances). The selected instance is also handed
# to CMake (CMAKE_GENERATOR_INSTANCE), so the compiler CMake drives and the dumpbin/cl the gate
# uses come from the same installation.
# ---------------------------------------------------------------------------------------------
function Find-VisualStudio {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) {
        throw @"
vswhere.exe was not found at
    $vswhere
which means no Visual Studio 2017-or-newer installer is present. Install Visual Studio 2022 or
newer (or the Build Tools) with the "Desktop development with C++" workload - see ..\README.txt.
"@
    }
    $installPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1
    if (-not $installPath) {
        throw 'No Visual Studio installation with the C++ toolset was found. Install the "Desktop development with C++" workload.'
    }
    $version = & $vswhere -path $installPath -property installationVersion | Select-Object -First 1
    $display = & $vswhere -path $installPath -property catalog_productDisplayVersion | Select-Object -First 1
    $name    = & $vswhere -path $installPath -property displayName | Select-Object -First 1
    return [pscustomobject]@{
        Path           = $installPath
        Version        = $version
        Major          = [int](($version -split '\.')[0])
        DisplayVersion = $display
        DisplayName    = $name
    }
}

function Get-VcVarsAllPath {
    param([Parameter(Mandatory)][string] $VsInstallPath)
    $vcvarsall = Join-Path $VsInstallPath 'VC\Auxiliary\Build\vcvarsall.bat'
    if (-not (Test-Path -LiteralPath $vcvarsall)) {
        throw "vcvarsall.bat was not found in $VsInstallPath. The C++ workload is not installed in THIS instance."
    }
    return $vcvarsall
}

# Does the SELECTED installation actually have an ARM64-targeting cl.exe? Checked on disk.
function Test-Arm64ToolsPresent {
    param([Parameter(Mandatory)][string] $VsInstallPath)
    $msvcRoot = Join-Path $VsInstallPath 'VC\Tools\MSVC'
    if (-not (Test-Path -LiteralPath $msvcRoot)) { return $false }
    foreach ($toolset in Get-ChildItem -LiteralPath $msvcRoot -Directory) {
        foreach ($hostDir in @('Hostx64', 'Hostarm64')) {
            if (Test-Path -LiteralPath (Join-Path $toolset.FullName "bin\$hostDir\arm64\cl.exe")) { return $true }
        }
    }
    return $false
}

# The CMake "Visual Studio NN YYYY" generator for the selected instance, found by asking CMake
# which generators it offers (no name table to go stale). Visual Studio 2026 (major 18) needs a
# CMake new enough to know it.
function Get-VsGenerator {
    param([Parameter(Mandatory)][int] $VsMajor)
    $help = Invoke-Native 'cmake' @('--help')
    $generators = foreach ($line in $help) {
        if ($line -match '^\s*\*?\s*(Visual Studio (\d+) \d{4})(\s|=|$)') {
            [pscustomobject]@{ Name = $Matches[1].Trim(); Major = [int]$Matches[2] }
        }
    }
    $match = @($generators | Where-Object { $_.Major -eq $VsMajor } | Select-Object -First 1)
    if ($match.Count -eq 0) {
        $known = ($generators | ForEach-Object { $_.Name }) -join ', '
        throw "This cmake offers no generator for Visual Studio major version $VsMajor (it offers: $known). Install a newer cmake (winget install Kitware.CMake)."
    }
    return $match[0].Name
}

# Import a developer environment into THIS PowerShell process, for the gate's cl and dumpbin.
# $ArchArgument is what vcvarsall takes: x64, arm64, or x64_arm64 (cross from an x64 host).
function Import-DeveloperEnvironment {
    param(
        [Parameter(Mandatory)][string] $VcVarsAll,
        [Parameter(Mandatory)][string] $ArchArgument
    )
    Write-Host "  developer environment: vcvarsall.bat $ArchArgument"
    # This exact invocation is llama-native-tools' (run for real 2026-09-15); keep it.
    $output = & "$env:COMSPEC" /s /c "`"$VcVarsAll`" $ArchArgument >nul 2>&1 && set"
    if ($LASTEXITCODE -ne 0) {
        throw "vcvarsall.bat $ArchArgument failed (exit $LASTEXITCODE). The toolset for that target is probably not installed in this Visual Studio instance."
    }
    foreach ($line in $output) {
        $eq = $line.IndexOf('=')
        if ($eq -lt 1) { continue }
        Set-Item -Path "Env:$($line.Substring(0, $eq))" -Value $line.Substring($eq + 1)
    }
}

# cl.exe with no arguments prints its banner on STDERR and the usage line on STDOUT; pick the
# banner by content.
function Get-ClVersionLine {
    $line = Invoke-Native 'cl' | Where-Object { $_ -match 'Compiler Version' } | Select-Object -First 1
    if (-not $line) { return 'cl (version banner not recognised)' }
    return $line.Trim()
}

function Assert-OnPath {
    param(
        [Parameter(Mandatory)][string] $Tool,
        [Parameter(Mandatory)][string] $InstallHint
    )
    $found = Get-Command $Tool -ErrorAction SilentlyContinue
    if (-not $found) {
        throw @"
$Tool is not on PATH.

$InstallHint

This script does not install anything for you. See ..\README.txt.
"@
    }
    return $found.Source
}

# ---------------------------------------------------------------------------------------------
# Source: ogg\ vorbis\ shim\ copied to scratch and built there, so the vendored trees stay
# unmodified. Patches (none today - ..\patches\README.txt) apply to the copy, in name order.
# ---------------------------------------------------------------------------------------------
function Copy-SourceToScratch {
    param(
        [Parameter(Mandatory)][string]    $ToolsDir,
        [Parameter(Mandatory)][string]    $ScratchDir,
        [Parameter(Mandatory)][hashtable] $Pins
    )
    if (Test-Path -LiteralPath $ScratchDir) { Remove-Item -LiteralPath $ScratchDir -Recurse -Force }
    New-Item -ItemType Directory -Path $ScratchDir -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $ToolsDir $Pins['OGG_DIR'])    -Destination (Join-Path $ScratchDir 'ogg') -Recurse
    Copy-Item -LiteralPath (Join-Path $ToolsDir $Pins['VORBIS_DIR']) -Destination (Join-Path $ScratchDir 'vorbis') -Recurse
    Copy-Item -LiteralPath (Join-Path $ToolsDir 'shim')              -Destination (Join-Path $ScratchDir 'shim') -Recurse
    Write-Host "  copied ogg\ vorbis\ shim\ -> $ScratchDir"

    $applied = @()
    $patchDir = Join-Path $ToolsDir 'patches'
    if (Test-Path -LiteralPath $patchDir) {
        foreach ($patch in Get-ChildItem -LiteralPath $patchDir -Filter '*.patch' | Sort-Object Name) {
            $git = Get-Command git -ErrorAction SilentlyContinue
            if (-not $git) {
                throw "patches\$($patch.Name) needs applying and git is not available. Install Git for Windows (winget install Git.Git)."
            }
            Write-Host "  applying $($patch.Name)"
            Invoke-Native 'git' @('-C', $ScratchDir, 'apply', '-p1', $patch.FullName) | ForEach-Object { Write-Host "    $_" }
            if ($LASTEXITCODE -ne 0) {
                throw "patch $($patch.Name) did not apply. Fix it; patches are never applied best-effort."
            }
            $applied += $patch.Name
        }
    }
    if ($applied.Count -eq 0) { return 'none' }
    return ($applied -join ' ')
}

# ---------------------------------------------------------------------------------------------
# Configure + build with CMake's Visual Studio generator. Returns the built DLL's full path.
# ---------------------------------------------------------------------------------------------
function Invoke-VorbisBuild {
    param(
        [Parameter(Mandatory)][string]   $ScratchDir,
        [Parameter(Mandatory)][string]   $BuildDir,
        [Parameter(Mandatory)][string]   $Generator,
        [Parameter(Mandatory)][string]   $Platform,           # x64 | ARM64  (the -A value)
        [Parameter(Mandatory)][string]   $VsInstallPath,
        [Parameter(Mandatory)][string[]] $CmakeOptions
    )
    if (Test-Path -LiteralPath $BuildDir) { Remove-Item -LiteralPath $BuildDir -Recurse -Force }
    New-Item -ItemType Directory -Path $BuildDir -Force | Out-Null

    $arguments = @('-S', (Join-Path $ScratchDir 'shim'), '-B', $BuildDir, '-G', $Generator, '-A', $Platform,
                   "-DCMAKE_GENERATOR_INSTANCE=$VsInstallPath") + $CmakeOptions
    Write-Host "  cmake $($arguments -join ' ')"
    $configure = Invoke-Native 'cmake' $arguments
    $configureExit = $LASTEXITCODE
    $configure | Set-Content -LiteralPath (Join-Path $BuildDir 'configure.log') -Encoding UTF8
    if ($configureExit -ne 0) {
        $configure | Select-Object -Last 30 | ForEach-Object { Write-Host "    $_" }
        throw "cmake configure failed (exit $configureExit) - see $BuildDir\configure.log"
    }
    $configure | Where-Object { $_ -match 'CMake Warning|CMake Error' } | ForEach-Object { Write-Host "  $_" }

    Write-Host "  building Release (log: $BuildDir\build.log) ..."
    $build = Invoke-Native 'cmake' @('--build', $BuildDir, '--config', 'Release', '--parallel')
    $buildExit = $LASTEXITCODE
    $build | Set-Content -LiteralPath (Join-Path $BuildDir 'build.log') -Encoding UTF8
    if ($buildExit -ne 0) {
        $build | Select-String -Pattern 'error|FAILED' -Context 2, 8 | Select-Object -First 5 | ForEach-Object { Write-Host $_ }
        throw "build failed (exit $buildExit) - see $BuildDir\build.log"
    }
    $script:BuildWarnings = @($build | Where-Object { $_ -match ': warning [A-Z]+\d+' }).Count
    Write-Host "  built ($($script:BuildWarnings) compiler/linker warning lines in build.log)"

    $dll = Join-Path $BuildDir 'Release\codebrix_vorbis.dll'
    if (-not (Test-Path -LiteralPath $dll)) {
        $found = Get-ChildItem -LiteralPath $BuildDir -Filter 'codebrix_vorbis.dll' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $found) { throw "The build reported success but no codebrix_vorbis.dll exists under $BuildDir." }
        $dll = $found.FullName
        Write-Host "  [note] codebrix_vorbis.dll found at a non-default location: $dll"
    }
    return $dll
}

# ---------------------------------------------------------------------------------------------
# Helpers for the gate.
# ---------------------------------------------------------------------------------------------

# PE machine type, read straight from the header: 0x8664 x64, 0xAA64 ARM64.
function Get-PeMachine {
    param([Parameter(Mandatory)][string] $Path)
    $fs = [System.IO.File]::OpenRead($Path)
    try {
        $br = New-Object System.IO.BinaryReader($fs)
        $fs.Position = 0x3C
        $fs.Position = $br.ReadInt32()
        if ($br.ReadUInt32() -ne 0x00004550) { throw "$Path is not a PE image." }
        return [int]$br.ReadUInt16()
    }
    finally { $fs.Dispose() }
}

$script:GateFailures = New-Object System.Collections.Generic.List[string]
function Add-GatePass { param([string] $Message) Write-Host "  [ok] $Message" }
function Add-GateFail { param([string] $Message) Write-Host "  [FAIL] $Message"; $script:GateFailures.Add($Message) }

function Invoke-Dumpbin {
    param([Parameter(Mandatory)][string[]] $Arguments)
    $output = Invoke-Native 'dumpbin' $Arguments
    if ($LASTEXITCODE -ne 0) { throw "dumpbin $($Arguments -join ' ') failed (exit $LASTEXITCODE)." }
    return $output
}

# Compile ..\smoke-test.c with the cl of the imported developer environment (so for the target
# architecture), static CRT (/MT) so the .exe also runs on a clean machine.
function Build-SmokeTest {
    param(
        [Parameter(Mandatory)][string] $SourceFile,
        [Parameter(Mandatory)][string] $GateDir
    )
    if (Test-Path -LiteralPath $GateDir) { Remove-Item -LiteralPath $GateDir -Recurse -Force }
    New-Item -ItemType Directory -Path $GateDir -Force | Out-Null
    Push-Location $GateDir
    try {
        $out = Invoke-Native 'cl' @('/nologo', '/O1', '/MT', '/W3', $SourceFile, '/Fe:smoke-test.exe')
        $exit = $LASTEXITCODE
    }
    finally { Pop-Location }
    $out | Set-Content -LiteralPath (Join-Path $GateDir 'smoke-test-build.log') -Encoding UTF8
    if ($exit -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $GateDir 'smoke-test.exe'))) {
        $out | ForEach-Object { Write-Host "    $_" }
        throw "the smoke test did not compile (exit $exit) - see $GateDir\smoke-test-build.log"
    }
    return (Join-Path $GateDir 'smoke-test.exe')
}

# ---------------------------------------------------------------------------------------------
# THE GATE. The same checks as the Linux build, expressed with the tools Windows has.
# Returns the smoke test's output lines (for BUILD-INFO.txt).
# ---------------------------------------------------------------------------------------------
function Test-CodebrixVorbisDll {
    param(
        [Parameter(Mandatory)][string]    $DllPath,             # the STAGED dll (the file that ships)
        [Parameter(Mandatory)][int]       $ExpectedMachine,     # 0x8664 | 0xAA64
        [Parameter(Mandatory)][string]    $ExpectedMachineName, # x64 | ARM64 (as dumpbin prints it)
        [Parameter(Mandatory)][hashtable] $Pins,
        [Parameter(Mandatory)][string]    $ToolsDir,
        [Parameter(Mandatory)][string]    $GateDir,             # holds smoke-test.exe
        [bool] $CanRunTargetBinaries = $true
    )

    # --- 1. machine type, two ways ---------------------------------------------------------------
    $machine = Get-PeMachine $DllPath
    if ($machine -eq $ExpectedMachine) { Add-GatePass ('PE machine type 0x{0:X4} ({1})' -f $machine, $ExpectedMachineName) }
    else { Add-GateFail ('PE machine type is 0x{0:X4}, expected 0x{1:X4} ({2}) - the -A platform did not take effect' -f $machine, $ExpectedMachine, $ExpectedMachineName) }
    $machineLine = Invoke-Dumpbin @('/nologo', '/headers', $DllPath) | Where-Object { $_ -match 'machine \(' } | Select-Object -First 1
    if ($machineLine -and $machineLine -match [regex]::Escape($ExpectedMachineName)) { Add-GatePass "dumpbin agrees: $($machineLine.Trim())" }
    else { Add-GateFail "dumpbin /headers machine line is '$machineLine', expected $ExpectedMachineName" }

    # --- 2. exports: EXACTLY the 11 functions of codebrix_vorbis.h --------------------------------
    # dumpbin prints "ordinal hint RVA name"; with the .pdb beside the DLL it appends
    # " = name (undecorated)", so the name is the FIRST token after the RVA.
    $exports = @()
    foreach ($line in Invoke-Dumpbin @('/nologo', '/exports', $DllPath)) {
        if ($line -match '^\s*\d+\s+[0-9A-Fa-f]+\s+[0-9A-Fa-f]+\s+(\S+)') { $exports += $Matches[1] }
    }
    if ($exports.Count -eq 0) { Add-GateFail 'no exports could be parsed from dumpbin /exports - run it by hand and compare the line format with the parser in Test-CodebrixVorbisDll' }
    $expected = @($Pins['REQUIRED_SYMBOLS'] -split '\s+' | Where-Object { $_ -ne '' } | Sort-Object -Unique -CaseSensitive)
    $missing = @($expected | Where-Object { $exports -cnotcontains $_ })
    $extra   = @($exports  | Where-Object { $expected -cnotcontains $_ })
    if ($missing.Count -gt 0) { Add-GateFail "missing exports ($($missing.Count)): $($missing -join ' ')" }
    if ($extra.Count -gt 0)   { Add-GateFail "unexpected exports ($($extra.Count)) - libogg/libvorbis must stay hidden: $(($extra | Select-Object -First 10) -join ' ')" }
    if ($missing.Count -eq 0 -and $extra.Count -eq 0 -and $exports.Count -gt 0) {
        Add-GatePass "exports are exactly the $($expected.Count) codebrix_vorbis_* functions"
    }

    # --- 3. dependents: KERNEL32 only, no VC runtime ---------------------------------------------
    $dependents = @()
    $inList = $false
    foreach ($line in Invoke-Dumpbin @('/nologo', '/dependents', $DllPath)) {
        $text = $line.Trim()
        if ($text -like 'Image has the following dependencies*') { $inList = $true; continue }
        if ($text -like 'Image has the following delay load dependencies*') { $inList = $true; continue }
        if ($inList) {
            if ($text -eq '') { continue }
            if ($text -like 'Summary*') { break }
            $dependents += $text
        }
    }
    $crt = @($dependents | Where-Object { $_ -match '^(vcruntime|msvcp|msvcr|ucrtbase|api-ms-win-crt)' })
    $unexpected = @($dependents | Where-Object { $script:AllowedDependents -notcontains $_ })
    if ($crt.Count -gt 0) { Add-GateFail "the DLL imports the Visual C++ runtime ($($crt -join ' ')) - CMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded did not take" }
    if ($unexpected.Count -gt 0) { Add-GateFail "unexpected dependencies: $($unexpected -join ' ') (allowed: $($script:AllowedDependents -join ' '))" }
    if ($dependents.Count -eq 0) { Add-GateFail 'no dependents parsed from dumpbin /dependents - check the parser' }
    elseif ($crt.Count -eq 0 -and $unexpected.Count -eq 0) { Add-GatePass "dependencies: $($dependents -join ' ') (static CRT, no redistributable needed)" }

    # --- 4. licence files verbatim ----------------------------------------------------------------
    $outDir = Split-Path -Parent $DllPath
    # LICENSE-Vorbis.txt must be the committed composition and must BEGIN with vorbis\COPYING
    # byte for byte and carry the lpc.c notice (bash's --check is the full regeneration test).
    $vorbisOut  = [System.IO.File]::ReadAllBytes((Join-Path $outDir $Pins['LICENSE_VORBIS_NAME']))
    $copying    = [System.IO.File]::ReadAllBytes((Join-Path $ToolsDir "$($Pins['VORBIS_DIR'])\COPYING"))
    $committed  = (Get-FileHash -LiteralPath (Join-Path $ToolsDir "licenses\$($Pins['LICENSE_VORBIS_NAME'])")).Hash
    $prefixOk   = $vorbisOut.Length -gt $copying.Length
    if ($prefixOk) { for ($i = 0; $i -lt $copying.Length; $i++) { if ($vorbisOut[$i] -ne $copying[$i]) { $prefixOk = $false; break } } }
    $text       = [System.Text.Encoding]::ASCII.GetString($vorbisOut)
    $vorbisSame = $prefixOk -and $text.Contains('Jutta Degener') -and $text.Contains('Carsten Bormann') -and `
                  ((Get-FileHash -LiteralPath (Join-Path $outDir $Pins['LICENSE_VORBIS_NAME'])).Hash -eq $committed)
    $oggSame    = (Get-FileHash -LiteralPath (Join-Path $outDir $Pins['LICENSE_OGG_NAME'])).Hash    -eq (Get-FileHash -LiteralPath (Join-Path $ToolsDir "$($Pins['OGG_DIR'])\COPYING")).Hash
    if ($vorbisSame -and $oggSame) { Add-GatePass "$($Pins['LICENSE_VORBIS_NAME']) (COPYING + lpc.c notice) and $($Pins['LICENSE_OGG_NAME']) (COPYING) are exact" }
    else { Add-GateFail 'licence files differ from the upstream COPYING files' }

    if (-not $CanRunTargetBinaries) {
        Add-GateFail 'smoke test NOT RUN - this machine cannot execute the target architecture. (Reported as a failure on purpose: an unrun check is not a passed check.)'
        return @('  not run: this machine cannot execute the target architecture')
    }

    # --- 5. LoadLibrary smoke test ---------------------------------------------------------------
    # THE PATH TRAP: the staged DLL is copied beside smoke-test.exe and passed by FULL path, so the
    # exact bytes that ship are what loads - never a codebrix_vorbis.dll found elsewhere on PATH.
    $gateDll = Join-Path $GateDir 'codebrix_vorbis.dll'
    Copy-Item -LiteralPath $DllPath -Destination $gateDll -Force
    Write-Host '  --- LoadLibrary smoke test ---'
    Push-Location $GateDir
    try {
        $smoke = Invoke-Native (Join-Path $GateDir 'smoke-test.exe') @($gateDll, (Join-Path $GateDir 'smoke-test-440hz.ogg'))
        $smokeExit = $LASTEXITCODE
    }
    finally { Pop-Location }
    $smoke | ForEach-Object { Write-Host "    $_" }
    if ($smokeExit -eq 0) { Add-GatePass 'smoke test (api_version 1, 1 s 440 Hz stereo encode, OggS/BOS/EOS/granule page walk, error paths)' }
    else { Add-GateFail "smoke test exited $smokeExit" }
    return $smoke
}

function Get-Sha256 {
    param([Parameter(Mandatory)][string] $Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

# ---------------------------------------------------------------------------------------------
# THE WHOLE BUILD, for one RID. The two architecture scripts decide the route and call this.
#   -VcVarsArch   x64 | arm64 | x64_arm64     (vcvarsall argument, for the gate's cl/dumpbin)
#   -Platform     x64 | ARM64                 (CMake -A value)
#   -CanRun       can this host execute the target's code (false only for the arm64 cross route)
#   -NoAdopt      build + gate only; do not copy into src\...\runtimes
#   -AdoptCrossBuild  adopt a cross-built win-arm64 whose static checks passed but whose smoke
#                 test could not run (recorded as such in BUILD-INFO.txt). Use only when no ARM64
#                 Windows machine is available; prefer finishing the gate on one.
# Exit code: 0 = every check ran and passed (or -AdoptCrossBuild was given and only the smoke test
#            was unrun). 1 = a check failed OR could not run.
# ---------------------------------------------------------------------------------------------
function Invoke-WindowsVorbisBuild {
    param(
        [Parameter(Mandatory)][string] $Rid,
        [Parameter(Mandatory)][string] $Platform,
        [Parameter(Mandatory)][int]    $ExpectedMachine,
        [Parameter(Mandatory)][string] $VcVarsArch,
        [Parameter(Mandatory)][string] $Route,
        [Parameter(Mandatory)][bool]   $CanRun,
        [Parameter(Mandatory)][string] $BuildRoot,
        [Parameter(Mandatory)][string] $ScriptName,
        [switch] $NoAdopt,
        [switch] $AdoptCrossBuild
    )

    $toolsDir   = Split-Path -Parent $PSScriptRoot          # vorbis-native-tools\
    $repoDir    = Split-Path -Parent $toolsDir
    $pins       = Read-Pins (Join-Path $toolsDir 'linux\pins.env')
    $smokeSrc   = Join-Path $toolsDir 'smoke-test.c'
    $scratchDir = Join-Path $BuildRoot 'src'
    $buildDir   = Join-Path $BuildRoot 'build'
    $gateDir    = Join-Path $BuildRoot 'gate'
    $outDir     = Join-Path $toolsDir "output\$Rid"
    $startedAt  = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss') + ' UTC'
    $stopwatch  = [System.Diagnostics.Stopwatch]::StartNew()
    $osCaption  = (Get-CimInstance Win32_OperatingSystem).Caption
    $osVersion  = [System.Environment]::OSVersion.Version.ToString()

    Write-Host '=============================================================================='
    Write-Host " codebrix_vorbis (libogg $($pins['OGG_VERSION']) + libvorbis $($pins['VORBIS_VERSION'])) - $Rid"
    Write-Host '=============================================================================='
    Write-Host "  started  : $startedAt"
    Write-Host "  host     : $env:PROCESSOR_ARCHITECTURE, $osCaption $osVersion"
    Write-Host "  route    : $Route"
    Write-Host "  source   : $toolsDir\{ogg,vorbis,shim}  (vendored - nothing is downloaded)"
    Write-Host ''

    # --- 1. prerequisites, all of them, before anything is compiled ------------------------------
    Write-Host '--- prerequisites ---'
    foreach ($required in @((Join-Path $toolsDir "$($pins['OGG_DIR'])\src\framing.c"), (Join-Path $toolsDir "$($pins['OGG_DIR'])\COPYING"),
                            (Join-Path $toolsDir "$($pins['VORBIS_DIR'])\lib\vorbisenc.c"), (Join-Path $toolsDir "$($pins['VORBIS_DIR'])\COPYING"),
                            (Join-Path $toolsDir 'shim\CMakeLists.txt'), (Join-Path $toolsDir 'shim\codebrix_vorbis.c'), $smokeSrc,
                            (Join-Path $toolsDir "licenses\$($pins['LICENSE_VORBIS_NAME'])"))) {
        if (-not (Test-Path -LiteralPath $required)) { throw "$required is missing. It is part of the repository; restore it from git." }
    }
    $vs = Find-VisualStudio
    Write-Host "  Visual Studio: $($vs.DisplayName) $($vs.DisplayVersion) [$($vs.Version)] at $($vs.Path)"
    $vcvarsall = Get-VcVarsAllPath $vs.Path
    if ($Platform -eq 'ARM64' -and -not (Test-Arm64ToolsPresent $vs.Path)) {
        throw @"
The ARM64 C++ build tools are not installed in THIS Visual Studio instance ($($vs.Path)).
Add the component "MSVC v143 - VS 2022 C++ ARM64/ARM64EC build tools (Latest)" - or the
equivalent "MSVC Build Tools for ARM64/ARM64EC" in newer Visual Studio - to that instance.
(The check reads this instance's filesystem on purpose.)
"@
    }
    Import-DeveloperEnvironment -VcVarsAll $vcvarsall -ArchArgument $VcVarsArch

    $clPath    = Assert-OnPath 'cl'      'cl ships with the "Desktop development with C++" workload; it should be on PATH inside the developer environment.'
    $dumpbin   = Assert-OnPath 'dumpbin' 'dumpbin ships with the C++ toolset; it should be on PATH inside the developer environment.'
    $cmakePath = Assert-OnPath 'cmake'   'Install it: winget install Kitware.CMake   (>= 3.16; Visual Studio 2026 needs a cmake that knows the "Visual Studio 18 2026" generator)'
    $cmakeVersion = (Invoke-Native 'cmake' @('--version') | Select-Object -First 1)
    $generator = Get-VsGenerator -VsMajor $vs.Major
    $clVersion = Get-ClVersionLine
    $sdkVersion = if ($env:WindowsSDKVersion) { $env:WindowsSDKVersion.TrimEnd('\') } else { 'unknown' }

    Write-Host "  cl       : $clVersion  [$clPath]"
    Write-Host "  dumpbin  : $dumpbin"
    Write-Host "  cmake    : $cmakeVersion  [$cmakePath]"
    Write-Host "  generator: $generator -A $Platform"
    Write-Host "  Win SDK  : $sdkVersion"
    if ($CanRun) { Write-Host '  execute  : this host runs the target architecture - the full gate will be applied' }
    else { Write-Host '  execute  : this host CANNOT run the target architecture - the smoke test cannot run and will be reported as a failure' }
    Write-Host ''

    # --- 2. source + build -------------------------------------------------------------------------
    Write-Host '--- source ---'
    $patchesApplied = Copy-SourceToScratch -ToolsDir $toolsDir -ScratchDir $scratchDir -Pins $pins
    Write-Host "  patches applied: $patchesApplied"
    Write-Host ''

    Write-Host '--- building ---'
    $options = Get-VorbisWindowsCmakeOptions
    $builtDll = Invoke-VorbisBuild -ScratchDir $scratchDir -BuildDir $buildDir -Generator $generator -Platform $Platform `
                                   -VsInstallPath $vs.Path -CmakeOptions $options
    Write-Host ''

    Write-Host '--- smoke test program ---'
    $smokeExe = Build-SmokeTest -SourceFile $smokeSrc -GateDir $gateDir
    $smokeMachine = Get-PeMachine $smokeExe
    if ($smokeMachine -ne $ExpectedMachine) {
        throw ('smoke-test.exe was compiled for machine 0x{0:X4}, not 0x{1:X4}: the developer environment ({2}) is not the target''s.' -f $smokeMachine, $ExpectedMachine, $VcVarsArch)
    }
    Write-Host "  $smokeExe (machine 0x$('{0:X4}' -f $smokeMachine))"
    Write-Host ''

    # --- 3. collect ---------------------------------------------------------------------------------
    Write-Host '--- collecting ---'
    if (Test-Path -LiteralPath $outDir) { Remove-Item -LiteralPath $outDir -Recurse -Force }
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    $dllPath = Join-Path $outDir 'codebrix_vorbis.dll'
    Copy-Item -LiteralPath $builtDll -Destination $dllPath
    $builtPdb = [System.IO.Path]::ChangeExtension($builtDll, '.pdb')
    $pdbPath = Join-Path $outDir 'codebrix_vorbis.pdb'
    if (Test-Path -LiteralPath $builtPdb) {
        Copy-Item -LiteralPath $builtPdb -Destination $pdbPath
        Write-Host '  codebrix_vorbis.pdb kept beside the DLL (crash triage; NOT shipped - goes to ..\unstripped\<rid>\ on adoption)'
    }
    else { Write-Host '  [warn] no codebrix_vorbis.pdb was produced - the options ask for /Z7 and /DEBUG; check build.log.' }
    # LICENSE-Vorbis.txt comes from the ONE committed composition (vorbis\COPYING + the notice
    # preserved in vorbis\lib\lpc.c), written by ..\licenses\compose-license-vorbis.sh.
    Copy-Item -LiteralPath (Join-Path $toolsDir "licenses\$($pins['LICENSE_VORBIS_NAME'])") -Destination (Join-Path $outDir $pins['LICENSE_VORBIS_NAME'])
    Copy-Item -LiteralPath (Join-Path $toolsDir "$($pins['OGG_DIR'])\COPYING")    -Destination (Join-Path $outDir $pins['LICENSE_OGG_NAME'])
    Copy-Item -LiteralPath (Join-Path $buildDir 'build.log') -Destination (Join-Path $outDir 'build.log')
    Write-Host "  $($pins['LICENSE_VORBIS_NAME']) = licenses\LICENSE-Vorbis.txt (vorbis\COPYING + lpc.c notice), $($pins['LICENSE_OGG_NAME']) = ogg\COPYING"
    Write-Host ''

    # --- 4. the gate --------------------------------------------------------------------------------
    Write-Host '--- verifying ---'
    $machineName = if ($ExpectedMachine -eq 0xAA64) { 'ARM64' } else { 'x64' }
    $smokeOutput = Test-CodebrixVorbisDll -DllPath $dllPath -ExpectedMachine $ExpectedMachine -ExpectedMachineName $machineName `
                                          -Pins $pins -ToolsDir $toolsDir -GateDir $gateDir -CanRunTargetBinaries $CanRun

    $complete = ($script:GateFailures.Count -eq 0)
    $staticFailures = @($script:GateFailures | Where-Object { $_ -notlike 'smoke test NOT RUN*' })
    if ($staticFailures.Count -gt 0) {
        Write-Host ''
        Write-Host "VERIFICATION FAILED for $Rid. $outDir is left for inspection, but this build must not be adopted."
        exit 1
    }
    $gateStatus = if ($complete) { 'COMPLETE - every check ran and passed' }
                  else { 'INCOMPLETE - cross-built; the static checks passed, the smoke test is UNRUN (this host cannot execute ARM64).' }

    # --- 5. record ----------------------------------------------------------------------------------
    $sha = Get-Sha256 $dllPath
    $size = (Get-Item -LiteralPath $dllPath).Length
    $pdbLine = if (Test-Path -LiteralPath $pdbPath) { "codebrix_vorbis.pdb, $((Get-Item -LiteralPath $pdbPath).Length) bytes, sha256 $(Get-Sha256 $pdbPath) (not shipped)" } else { 'none produced' }
    $sumLines = foreach ($name in @('codebrix_vorbis.dll', 'codebrix_vorbis.pdb', $pins['LICENSE_VORBIS_NAME'], $pins['LICENSE_OGG_NAME'])) {
        $p = Join-Path $outDir $name
        if (Test-Path -LiteralPath $p) { "$(Get-Sha256 $p)  $name" }
    }
    # LF line endings and no BOM, so `sha256sum -c` on Linux or macOS can check the file as is.
    [System.IO.File]::WriteAllText((Join-Path $outDir 'SHA256SUMS.txt'), (($sumLines -join "`n") + "`n"))
    $stopwatch.Stop()

    $buildInfoText = @"
codebrix_vorbis native library - build information
==============================================================================
RID              : $Rid
Gate status      : $gateStatus
Built            : $startedAt
Build duration   : $([int]$stopwatch.Elapsed.TotalSeconds)s
Built by         : vorbis-native-tools\windows\$ScriptName  (route: $Route)

Build machine
------------------------------------------------------------------------------
OS               : $osCaption $osVersion ($env:PROCESSOR_ARCHITECTURE)
Visual Studio    : $($vs.DisplayName) $($vs.DisplayVersion) [$($vs.Version)]
                   $($vs.Path)
cl               : $clVersion
Windows SDK      : $sdkVersion
cmake            : $cmakeVersion
generator        : $generator -A $Platform (CMAKE_GENERATOR_INSTANCE = the instance above)

Source (vendored in-repo; nothing fetched at build time)
------------------------------------------------------------------------------
libogg           : $($pins['OGG_VERSION']) (ogg\)     libvorbis: $($pins['VORBIS_VERSION']) (vorbis\)
Shim             : shim\codebrix_vorbis.c + codebrix_vorbis.h (API version 1)
Patches applied  : $patchesApplied

Configuration
------------------------------------------------------------------------------
cmake options    : $($options -join "`n                   ")
CRT              : static (MultiThreaded) - no Visual C++ Redistributable needed
Build warnings   : $($script:BuildWarnings) (upstream sources compiled with /w; these are the shim's or the linker's)

Result
------------------------------------------------------------------------------
File             : codebrix_vorbis.dll  (DllImport("codebrix_vorbis") probes this name)
Size             : $size bytes
SHA256           : $sha
Debug symbols    : $pdbLine
Licences beside  : $($pins['LICENSE_VORBIS_NAME']) (vorbis\COPYING + lpc.c preserved notice), $($pins['LICENSE_OGG_NAME']) (ogg\COPYING) - BSD-3

Smoke test ($(if ($CanRun) { 'ran on this machine' } else { 'NOT RUN - cross build' }))
------------------------------------------------------------------------------
$(($smokeOutput | ForEach-Object { "  $_" }) -join "`n")
"@
    Set-Content -LiteralPath (Join-Path $outDir 'BUILD-INFO.txt') -Value $buildInfoText -Encoding UTF8

    Write-Host ''
    Write-Host '--- done ---'
    Write-Host "  $dllPath"
    Write-Host "  sha256 $sha"
    Write-Host "  $([int]$stopwatch.Elapsed.TotalSeconds)s"
    Write-Host ''

    # --- 6. adopt -----------------------------------------------------------------------------------
    $adopt = (-not $NoAdopt) -and ($complete -or $AdoptCrossBuild)
    if ($adopt) {
        $dest = Join-Path $repoDir "src\CodeBrix.Audio.Vorbis\runtimes\$Rid\native"
        New-Item -ItemType Directory -Path $dest -Force | Out-Null
        foreach ($name in @('codebrix_vorbis.dll', $pins['LICENSE_VORBIS_NAME'], $pins['LICENSE_OGG_NAME'])) {
            Copy-Item -LiteralPath (Join-Path $outDir $name) -Destination (Join-Path $dest $name) -Force
        }
        Write-Host "adopted -> src\CodeBrix.Audio.Vorbis\runtimes\$Rid\native\ (codebrix_vorbis.dll + both licences)"
        if (Test-Path -LiteralPath $pdbPath) {
            $unstripped = Join-Path $toolsDir "unstripped\$Rid"
            New-Item -ItemType Directory -Path $unstripped -Force | Out-Null
            Copy-Item -LiteralPath $pdbPath -Destination (Join-Path $unstripped 'codebrix_vorbis.pdb') -Force
            Write-Host "        -> vorbis-native-tools\unstripped\$Rid\codebrix_vorbis.pdb"
        }
        if (-not $complete) { Write-Host '  [note] adopted with -AdoptCrossBuild: the smoke test has NOT run on ARM64. Say so in ..\BUILD-PROVENANCE.txt.' }
        Write-Host "Record the build in ..\BUILD-PROVENANCE.txt (replace the NOT YET BUILT block for $Rid) from"
        Write-Host "output\$Rid\BUILD-INFO.txt, then commit the adopted files."
    }
    elseif ($NoAdopt) { Write-Host "-NoAdopt: not copied into the package. Result stays in output\$Rid." }

    if (-not $complete -and -not $AdoptCrossBuild) {
        Write-Host 'GATE INCOMPLETE: this DLL was cross-built and has NOT been executed, so it was NOT adopted.'
        Write-Host 'Run build-win-arm64.ps1 on an ARM64 Windows machine, or re-run here with -AdoptCrossBuild.'
        exit 1
    }
    exit 0
}
