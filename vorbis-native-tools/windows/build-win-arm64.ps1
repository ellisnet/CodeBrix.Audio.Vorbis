# =============================================================================================
# build-win-arm64.ps1 - build codebrix_vorbis.dll for the win-arm64 runtime identifier
# =============================================================================================
#
#   NEVER YET RUN (either route). Written on Linux on 2026-10-10. Expect to fix something on the
#   first real run; fix it IN THE SCRIPT (or build-common.ps1) and record it in
#   ..\BUILD-PROVENANCE.txt.
#
# USAGE (from any PowerShell prompt - the script sets up the compiler environment itself):
#
#     cd vorbis-native-tools\windows
#     .\build-win-arm64.ps1                          # route chosen from the host (see below)
#     .\build-win-arm64.ps1 -Route CrossFromX64      # force the cross route on an x64 machine
#     .\build-win-arm64.ps1 -AdoptCrossBuild         # x64 host: adopt even though the smoke test
#                                                    # could not run (static checks must pass)
#     .\build-win-arm64.ps1 -NoAdopt                 # build + gate only
#
# THE COMPILER IS MSVC (cl) with -A ARM64. libogg/libvorbis are plain C with no intrinsics or
# assembly, so no clang-cl is needed. The two routes differ only in the HOST:
#   Native (an ARM64 Windows machine): vcvarsall arm64; the whole gate runs, smoke test included.
#   CrossFromX64 (an x64 Windows machine): vcvarsall x64_arm64. Builds the DLL and an ARM64
#       smoke-test.exe and runs every static check (machine type, exact exports, dependents,
#       licences), but CANNOT run the smoke test. That is reported as a FAILURE and the script
#       exits 1 without adopting - unless -AdoptCrossBuild is given, in which case it adopts and
#       BUILD-INFO.txt says "Gate status: INCOMPLETE". The gate can be finished later by copying
#       output\win-arm64\codebrix_vorbis.dll and the gate's smoke-test.exe to an ARM64 machine:
#           smoke-test.exe <full path to codebrix_vorbis.dll>
#   -Route Auto (default) picks Native on ARM64 hosts and CrossFromX64 on x64 hosts.
#
# Output: ..\output\win-arm64\
# =============================================================================================

[CmdletBinding()]
param(
    [ValidateSet('Auto', 'Native', 'CrossFromX64')]
    [string] $Route = 'Auto',
    [string] $BuildRoot = (Join-Path $env:TEMP 'codebrix-vorbis-build-win-arm64'),
    [switch] $NoAdopt,
    [switch] $AdoptCrossBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot\build-common.ps1"

$hostIsArm64 = ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64')
if ($Route -eq 'Auto') { $Route = if ($hostIsArm64) { 'Native' } else { 'CrossFromX64' } }
if ($Route -eq 'Native' -and -not $hostIsArm64) {
    throw "-Route Native must run on an ARM64 Windows machine; this host is $env:PROCESSOR_ARCHITECTURE. Use -Route CrossFromX64 here, or run on the ARM64 machine."
}
if ($Route -eq 'CrossFromX64' -and $env:PROCESSOR_ARCHITECTURE -ne 'AMD64') {
    throw "-Route CrossFromX64 runs on an x64 Windows machine; this host is $env:PROCESSOR_ARCHITECTURE. On ARM64 use -Route Native."
}

$vcvarsArch = if ($Route -eq 'Native') { 'arm64' } else { 'x64_arm64' }
$routeText  = if ($Route -eq 'Native') { 'Native (ARM64 host)' } else { 'CrossFromX64 (x64 host, -A ARM64)' }

Invoke-WindowsVorbisBuild -Rid 'win-arm64' -Platform 'ARM64' -ExpectedMachine 0xAA64 -VcVarsArch $vcvarsArch `
                          -Route $routeText -CanRun $hostIsArm64 -BuildRoot $BuildRoot `
                          -ScriptName 'build-win-arm64.ps1' -NoAdopt:$NoAdopt -AdoptCrossBuild:$AdoptCrossBuild
