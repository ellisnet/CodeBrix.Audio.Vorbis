================================================================================
vorbis-native-tools - how the codebrix_vorbis native libraries are built
================================================================================

WHAT IS BUILT
--------------------------------------------------------------------------------
ONE native library per runtime identifier, `codebrix_vorbis`
(libcodebrix_vorbis.so / codebrix_vorbis.dll / libcodebrix_vorbis.dylib), which
CodeBrix.Audio.Vorbis P/Invokes. It is the C shim in shim/codebrix_vorbis.c with
libogg 1.3.6 + libvorbis 1.3.7 + libvorbisenc statically linked in (libvorbisfile
is NOT built - decoding is the managed NVorbis port in CodeBrix.Audio.Core).

The shim implements shim/codebrix_vorbis.h - THE fixed-ABI contract with the
managed side: opaque handles, int32_t / int64_t / float / byte pointers only, no
libogg/libvorbis struct and no C `long` (32-bit on Windows), so one managed
import set works on every RID. Do not change the header without bumping
CODEBRIX_VORBIS_API_VERSION and changing the managed side in the same change.
Only the header's 11 functions are exported (every object is compiled with hidden
visibility); every gate checks the exact export set.

THE RULE: SCRIPTS NEVER REACH OUTSIDE THIS REPOSITORY
--------------------------------------------------------------------------------
Every build reads only what is committed here. Nothing is downloaded, cloned, or
installed at build time:
  * ogg/ and vorbis/ are the unmodified Xiph release tarballs, unpacked; the
    tarballs themselves are in upstream-downloads/ (UPSTREAM.txt has their
    sha256 and the no-network verification recipe). Never edit those two trees;
    a needed change goes in patches/*.patch, applied to a scratch copy.
  * Linux builds run in podman with --network none (and --pull=never). The
    derived images (linux/Containerfile.<arch>) install NOTHING - the pinned
    manylinux base images already carry gcc, cmake, make and binutils - so even
    building the derived image needs no network.
  * Tools on a build machine (podman, the NDK, Visual Studio, Xcode CLT, cmake)
    are prerequisites the scripts check for and NAME when missing; they never
    install them.

WHAT BUILDS WHERE
--------------------------------------------------------------------------------
  RID            script                          machine                 status
  linux-x64      linux/build.sh x64              any Linux + podman      built here
  linux-arm64    linux/build.sh arm64            ditto (+ qemu binfmt)   built here
  linux-riscv64  linux/build.sh riscv64          ditto (+ qemu binfmt)   built here
  android-arm64  android/build.sh arm64          Linux/macOS + NDK 30    built here
  android-x64    android/build.sh x64            Linux/macOS + NDK 30    built here
  win-x64        windows/build-win-x64.ps1       x64 Windows + VS C++    built (Jeremy's x64 box)
  win-arm64      windows/build-win-arm64.ps1     ARM64 Windows (or x64,  cross-built there;
                                                 cross; smoke test unrun) smoke test UNRUN
  osx-arm64      macos/build-osx-arm64.sh        Apple Silicon Mac       built (Jeremy's Apple Silicon Mac)
  osx-x64        macos/build-osx-x64.sh          Intel Mac, or Apple     built there (cross); smoke test
                                                 Silicon + Rosetta 2     UNRUN (no Rosetta 2), NOT adopted
  (host)         build-host.sh                   this Linux dev box      NOT shipped

The Windows scripts were written on Linux and first ran on 2026-10-10 on
Jeremy's x64 Windows 11 machine (Visual Studio 2026, cmake 4.4.3); what that run
found and changed is in BUILD-PROVENANCE.txt. win-arm64 was cross-built there
with -AdoptCrossBuild, so its smoke test is still UNRUN - finish it on an ARM64
Windows device (Surface Pro X) as build-win-arm64.ps1's header describes. The
macOS scripts were also written on Linux and first ran on 2026-10-10 on Jeremy's
Apple Silicon Mac (macOS 27.0.1, Xcode 27.0, cmake 4.4.3); both ran UNCHANGED.
osx-arm64 passed the complete gate and was adopted. osx-x64 was cross-built and
passed every static check, but its smoke test needs Rosetta 2, which that Mac
did not have, so it is NOT adopted yet: install Rosetta 2
(softwareupdate --install-rosetta --agree-to-license) and re-run
build-osx-x64.sh; BUILD-PROVENANCE.txt has the details. If a script breaks on
another machine, fix it in the script and record it there.

HOW TO REBUILD
--------------------------------------------------------------------------------
  cd vorbis-native-tools/linux   && ./build.sh            # all three Linux RIDs
  cd vorbis-native-tools/android && ./build.sh            # both Android ABIs
       DEVICE_ARM64=<adb serial> DEVICE_X64=<adb serial> ./build.sh   # + on-device smoke test
  cd vorbis-native-tools\windows ; .\build-win-x64.ps1 ; .\build-win-arm64.ps1
  cd vorbis-native-tools/macos   && ./build-osx-arm64.sh && ./build-osx-x64.sh
  cd vorbis-native-tools         && ./build-host.sh       # dev-box .so for the tests

Every script: copies ogg/ vorbis/ shim/ to scratch (patches applied there),
builds shim/CMakeLists.txt, keeps an unstripped twin, strips, writes the two
licence files beside the binary, runs THE GATE (architecture, exact exports,
system-only dependencies, glibc floor / 16 KB alignment / deployment target as
the platform needs, licence files, and ../smoke-test.c against the STAGED file),
writes output/<rid>/BUILD-INFO.txt, and - only when the gate passed - ADOPTS the
result: copies <binary> + LICENSE-Vorbis.txt + LICENSE-Ogg.txt into
../src/CodeBrix.Audio.Vorbis/runtimes/<rid>/native/ and the unstripped twin (or
.pdb) into unstripped/<rid>/. ADOPT=0 (bash) / -NoAdopt (PowerShell) skips that.
Then update that RID's block in BUILD-PROVENANCE.txt from BUILD-INFO.txt.

THE LICENCE FILES (identical on all nine RIDs)
--------------------------------------------------------------------------------
  LICENSE-Ogg.txt     ogg/COPYING, verbatim (BSD-3).
  LICENSE-Vorbis.txt  licenses/LICENSE-Vorbis.txt - ONE committed file, written by
                      licenses/compose-license-vorbis.sh --write: vorbis/COPYING
                      (BSD-3), a separator, then the notice PRESERVED in
                      vorbis/lib/lpc.c (Jutta Degener and Carsten Bormann,
                      Technische Universitaet Berlin, 1992-1994 - the block from
                      "/* Some of these routines" through "Carsten Bormann"). lpc.c
                      is compiled into every binary and that notice must not be
                      removed, so it travels with the binary. The bash scripts run
                      `compose-license-vorbis.sh --check` before copying and
                      compare the result again in the gate; the Windows gate
                      checks the copy equals the committed file and starts with
                      vorbis/COPYING. .gitattributes here marks the SOURCE texts
                      -text, and the repository-root .gitattributes marks the
                      ADOPTED copies (src/CodeBrix.Audio.Vorbis/runtimes/**)
                      -text, so no checkout can change their bytes - without the
                      root rule a Windows checkout (core.autocrlf=true) rewrote
                      the adopted copies with CRLF and a package built there
                      shipped different licence bytes per RID.

FILES
--------------------------------------------------------------------------------
  ogg/ vorbis/            unmodified Xiph trees (UPSTREAM.txt)
  upstream-downloads/     the release tarballs they came from
  shim/                   codebrix_vorbis.h (contract), codebrix_vorbis.c, CMakeLists.txt
  smoke-test.c            the gate's dlopen/LoadLibrary program (47 checks, no decode)
  licenses/               LICENSE-Vorbis.txt + its generator
  linux/                  build.sh, container-build.sh, Containerfile.<arch>, pins.env
                          (pins.env also feeds the Windows and macOS scripts)
  android/                build.sh, layout-check.c (ABI static asserts)
  windows/                build-common.ps1, build-win-x64.ps1, build-win-arm64.ps1
  macos/                  build-common.sh, build-osx-arm64.sh, build-osx-x64.sh
  build-host.sh           the not-shipped dev-box build
  patches/                empty (see its README)
  unstripped/             committed pre-strip twins for crash triage
  output/                 build results, git-ignored (except its README)
  BUILD-PROVENANCE.txt    what was built, when, with what, and the result
