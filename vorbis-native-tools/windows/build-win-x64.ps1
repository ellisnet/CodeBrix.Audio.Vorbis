# =============================================================================================
# build-win-x64.ps1 - build codebrix_vorbis.dll for the win-x64 runtime identifier
# =============================================================================================
#
#   Written on Linux on 2026-10-10. FIRST RUN the same day on Jeremy's x64 Windows 11 machine
#   (Visual Studio Professional 2026 18.10.3, cl 19.51, cmake 4.4.3): built, passed the whole
#   gate including the smoke test, and was adopted. The one first-run fix (the /PDBALTPATH
#   value) is in build-common.ps1; see ..\BUILD-PROVENANCE.txt. If something breaks on another
#   machine, fix it IN THE SCRIPT (or build-common.ps1) and record it there.
#
# USAGE (from any PowerShell prompt - the script sets up the compiler environment itself):
#
#     cd vorbis-native-tools\windows
#     .\build-win-x64.ps1              # build, gate, and (gate COMPLETE) adopt into src\
#     .\build-win-x64.ps1 -NoAdopt     # build + gate only
#
# MSVC (cl) through CMake's Visual Studio generator, -A x64, static CRT. Output:
# ..\output\win-x64\ ; adopted into ..\..\src\CodeBrix.Audio.Vorbis\runtimes\win-x64\native\
# (codebrix_vorbis.dll + LICENSE-Vorbis.txt + LICENSE-Ogg.txt), the .pdb into ..\unstripped\win-x64\.
# It installs nothing. Anything missing is named, with the command that installs it.
# Prerequisites: Visual Studio 2022+ (or Build Tools) with "Desktop development with C++",
# cmake >= 3.16 on PATH.
# =============================================================================================

[CmdletBinding()]
param(
    # Where cmake builds and the gate runs. Kept out of the repository.
    [string] $BuildRoot = (Join-Path $env:TEMP 'codebrix-vorbis-build-win-x64'),
    [switch] $NoAdopt
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot\build-common.ps1"

if ($env:PROCESSOR_ARCHITECTURE -ne 'AMD64') {
    throw "build-win-x64.ps1 runs on an x64 Windows machine; this host is $env:PROCESSOR_ARCHITECTURE."
}

Invoke-WindowsVorbisBuild -Rid 'win-x64' -Platform 'x64' -ExpectedMachine 0x8664 -VcVarsArch 'x64' `
                          -Route 'native (x64 host)' -CanRun $true -BuildRoot $BuildRoot `
                          -ScriptName 'build-win-x64.ps1' -NoAdopt:$NoAdopt
