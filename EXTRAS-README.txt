================================================================================
EXTRAS-README: CodeBrix.Audio.Vorbis
Samples, tools and other content in this repository that is not part of a NuGet
package
================================================================================

Exactly one project is packable - src/CodeBrix.Audio.Vorbis - and everything
else listed below exists only to build, test, prove or document it. None of it is
included in the CodeBrix.Audio.Vorbis.BsdLicenseForever package (the package
carries only the native BINARIES that vorbis-native-tools produces, under
runtimes/<rid>/native/).

For runnable, compilable usage of the library, read the test project: the
"WORKING EXAMPLES ON GITHUB" section of AGENT-README.txt maps each feature area
to the test file that exercises it.


VORBIS-NATIVE-TOOLS
===================
    vorbis-native-tools/

Everything needed to build the native codebrix_vorbis library for every
runtime identifier, from this repository alone:
  - ogg/ and vorbis/: the vendored, unmodified Xiph.Org libogg and libvorbis
    source trees (upstream-downloads/ holds the release tarballs;
    UPSTREAM.txt records their identity and hashes);
  - shim/: codebrix_vorbis.h (the fixed-ABI contract with the managed
    assembly) and the C shim + CMake build that links libogg + libvorbis +
    libvorbisenc into one shared library;
  - the per-platform build scripts (Linux manylinux containers, Android NDK,
    Windows MSVC, macOS) and the smoke test;
  - patches/ (for any future build-time source change - never applied to the
    vendored trees themselves);
  - BUILD-PROVENANCE.txt: one record per RID actually built.

Start with vorbis-native-tools/README.txt. MAINTAINER-README.txt (NATIVE
LIBRARIES) has the build matrix: which machine builds which RID.


TEST PROJECT
============
    tests/CodeBrix.Audio.Vorbis.Tests/

xUnit v3; run it as described in MAINTAINER-README.txt (TESTING). It copies the
committed natives into its output and round-trips generated tones through the
encoder and CodeBrix.Audio.Core's decoder.

ANDROID DIAGNOSTICS SAMPLE
==========================
    samples/VorbisDiagnostics/

A small .NET for Android app (API 33 and later) that proves the package on a
real device: it registers the encoder, encodes generated tones in both modes,
decodes them back with CodeBrix.Audio.Core, plays one audibly through
CodeBrix.Audio.Android, and prints "VORBIS-DIAG RESULT: PASS" (also to logcat,
tag VorbisDiag). It references the library by ProjectReference, which does NOT
carry the runtimes natives into an APK, so its csproj adds explicit
AndroidNativeLibrary items for android-arm64 and android-x64; an application
that references the published package needs nothing of the kind. Its README.txt
has the adb install / launch / log-collection commands. Build it in Release
with the android workload installed; it is in the solution's Samples folder.
