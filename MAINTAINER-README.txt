================================================================================
MAINTAINER-README: CodeBrix.Audio.Vorbis
Notes for people and agents MAINTAINING this repository - not for package
consumers
================================================================================

If you are CONSUMING the NuGet package, stop reading and open AGENT-README.txt
instead. Everything below is about the repository itself: how it is laid out,
how it builds, how it is tested, how it is packaged, how the native libraries
are produced, and the conventions the source follows.


PURPOSE AND SCOPE
=================
PackageId:      CodeBrix.Audio.Vorbis.BsdLicenseForever
Assembly:       CodeBrix.Audio.Vorbis (net10.0)
Project:        src/CodeBrix.Audio.Vorbis/CodeBrix.Audio.Vorbis.csproj
License:        BSD-3-Clause (LICENSE); vendored Xiph code also BSD-3-Clause
Consumer doc:   AGENT-README.txt

An Ogg Vorbis ENCODER: one managed assembly that P/Invokes one native library
per runtime identifier, named codebrix_vorbis (libcodebrix_vorbis.so,
libcodebrix_vorbis.dylib, codebrix_vorbis.dll). Decoding is NOT here - it is
the managed decoder in CodeBrix.Audio.Core.

The native contract is vorbis-native-tools/shim/codebrix_vorbis.h. It is the
single source of truth for the managed/native boundary: fixed-ABI (no C long,
no libogg/libvorbis structs, no callbacks), versioned by
CODEBRIX_VORBIS_API_VERSION. The managed side (Interop/VorbisLibrary.cs,
ExpectedApiVersion) refuses a library reporting any other version. Change the
header only together with the shim, every native build, Interop/VorbisNative.cs
and the version constant on both sides.


REPOSITORY LAYOUT
=================
src/CodeBrix.Audio.Vorbis/
    CodeBrixAudioVorbis.cs           Register / EnsureLoaded / diagnostics
    OggVorbisEncoder.cs              the encoder
    VorbisEncoderOptions.cs          settings + validation
    VorbisEncoderMode.cs
    VorbisEncoderException.cs
    VorbisAudioFileWriterFactory.cs  the ".ogg" IAudioFileWriterFactory
    Internal/VorbisStreamAudioFileWriter.cs   IAudioFileWriter adapter
    Interop/VorbisLibrary.cs         native loader + DllImportResolver
    Interop/VorbisNative.cs          LibraryImport declarations (the header)
    Interop/VorbisEncoderHandle.cs   SafeHandle over the native encoder
    Interop/VorbisResult.cs          CBV_* codes and messages
    InternalsVisibleTo.cs            -> CodeBrix.Audio.Vorbis.Tests
    runtimes/<rid>/native/           COMMITTED native binaries, each beside
                                     LICENSE-Vorbis.txt and LICENSE-Ogg.txt
tests/CodeBrix.Audio.Vorbis.Tests/   xUnit v3 test project
samples/VorbisDiagnostics/           Android device-proof app (EXTRAS-README.txt);
                                     in the solution's Samples folder
vorbis-native-tools/                 everything that builds the natives (see
                                     NATIVE LIBRARIES below and its README.txt)

Root files: CodeBrix.Audio.Vorbis.slnx, global.json, LICENSE, README.md,
AGENT-README.txt, MAINTAINER-README.txt, EXTRAS-README.txt, README-INDEX.txt,
THIRD-PARTY-NOTICES.txt, icon-codebrix-128.png, .gitignore, and the 8 AI-agent
pointer stubs (AGENTS.md, CLAUDE.md, .clinerules, .cursorrules, .windsurfrules,
.cursor/rules/agent-readme.mdc, .github/copilot-instructions.md,
.junie/guidelines.md - byte-identical to the family canonical copies).

The .slnx "Solution Items" folder lists the canonical ten files: .gitignore,
AGENT-README.txt, EXTRAS-README.txt, global.json, icon-codebrix-128.png,
LICENSE, MAINTAINER-README.txt, README-INDEX.txt, README.md,
THIRD-PARTY-NOTICES.txt. Its "Tests" folder holds the test project.

global.json selects the Microsoft.Testing.Platform test runner; it pins no SDK.


BUILDING
========
    dotnet build --configuration Release CodeBrix.Audio.Vorbis.slnx

Expect 0 warnings and 0 errors (Debug and Release). GenerateDocumentationFile
is on, so CS1591 is fixed by writing the doc comment, never with NoWarn.
AllowUnsafeBlocks is on for the source-generated LibraryImport interop.

The managed build does NOT build natives; it packs and copies whatever is
committed under src/CodeBrix.Audio.Vorbis/runtimes/.


TESTING
=======
xUnit v3 + SilverAssertions. The test runner IS Microsoft.Testing.Platform,
selected by the root global.json - do not delete it; without it `dotnet test`
falls back to the VSTest bridge, which fails on the .NET 10 SDK.

    dotnet test --solution CodeBrix.Audio.Vorbis.slnx
    dotnet test --solution CodeBrix.Audio.Vorbis.slnx -c Release

If `dotnet test --solution` reports zero tests (a known SDK 10.0.4xx quirk),
run the test assembly directly:

    dotnet tests/CodeBrix.Audio.Vorbis.Tests/bin/Debug/net10.0/CodeBrix.Audio.Vorbis.Tests.dll

The test csproj copies src/CodeBrix.Audio.Vorbis/runtimes/** into its output
under runtimes/<rid>/native/ (None Remove/Include glob), so the tests load
exactly the committed binary through the library's own resolver - nothing is
installed on the machine. The tests therefore run on any platform whose native
is committed; on others they fail with the loader's "could not be loaded"
message, which names every path tried.

Round-trip tests decode with CodeBrix.Audio.Core's OggVorbisFileReader (pinned
in the test csproj at the same version as the library's Core reference -
raise both together). All signals are generated in code; there are no audio
assets and none may be added without an identified licence.

No coverage collector is referenced (the family dropped coverlet.collector).


PACKAGING / PUBLISHING
======================
The csproj carries the family's canonical date-stamped version block
(1.<years since 2026>.<day of year>.<minute of day>, UTC); never hardcode a
<Version>. GeneratePackageOnBuild is on, so every build drops a fresh .nupkg in
src/CodeBrix.Audio.Vorbis/bin/<Configuration>/.

The nupkg contains: lib/net10.0/CodeBrix.Audio.Vorbis.dll + .xml, README.md,
AGENT-README.txt, THIRD-PARTY-NOTICES.txt, LICENSE, icon-codebrix-128.png, and
runtimes/<rid>/native/{binary, LICENSE-Vorbis.txt, LICENSE-Ogg.txt} for every
RID committed. The runtimes are packed with PackagePath="runtimes\" (BARE - NuGet
appends the recursive directory itself; spelling %(RecursiveDir) out doubles the
path). After any csproj change, unzip -l the Release nupkg and check every
runtimes/<rid>/native/ path appears once and correctly.

Do not publish until all nine RIDs are committed (see the matrix below) -
a package missing a RID fails at run time on that platform. Jeremy publishes.


NATIVE LIBRARIES
================
All native build tooling lives in vorbis-native-tools/ - read
vorbis-native-tools/README.txt before touching it. Per-RID build records
(toolchain, container digest, hashes, date) are in
vorbis-native-tools/BUILD-PROVENANCE.txt; upstream identity and tarball hashes
are in vorbis-native-tools/UPSTREAM.txt.

Build matrix - which machine builds which RID:

    RID              Built where                     How
    ---------------  ------------------------------  ---------------------------
    linux-x64        this Linux dev machine          manylinux container
    linux-arm64      this Linux dev machine          manylinux container
    linux-riscv64    this Linux dev machine          manylinux container
    android-arm64    this Linux dev machine          Android NDK
    android-x64      this Linux dev machine          Android NDK
    win-x64          Jeremy, on Windows              MSVC script
    win-arm64        Jeremy, on Windows              MSVC script (cross from x64)
    osx-arm64        Jeremy, on a Mac                Xcode/clang script
    osx-x64          Jeremy, on a Mac                Xcode/clang script

Each build writes the binary plus LICENSE-Vorbis.txt and LICENSE-Ogg.txt (the
two Xiph COPYING files, verbatim) and is copied into
src/CodeBrix.Audio.Vorbis/runtimes/<rid>/native/. A host build made for local
testing (vorbis-native-tools/output/host/) is never committed into runtimes/.

THE NEVER-REACH-OUTSIDE RULE. Every build script reads only from this
repository: the vendored ogg/ and vorbis/ trees, the shim, the scripts. No
downloads, no git clones, no package installs at build time; containers run
with networking disabled. The repository must be able to rebuild every native
years from now with nothing but the toolchains named in BUILD-PROVENANCE.txt.
vorbis-native-tools/ogg/ and vorbis-native-tools/vorbis/ are never modified;
a needed source change goes in vorbis-native-tools/patches/ and is applied to a
scratch copy by the scripts.


CODING CONVENTIONS
==================
- File layout: usings at the top (System.* first, alphabetical), one blank line,
  file-scoped namespace, one blank line, the type. No global usings, no
  ImplicitUsings, no #nullable.
- Folder = namespace (Interop/ -> CodeBrix.Audio.Vorbis.Interop, Internal/ ->
  CodeBrix.Audio.Vorbis.Internal). The public types live at the root namespace.
  Do not add a folder/namespace called "Encoding" (it would shadow
  System.Text.Encoding inside this assembly) or "Ogg" / "Contracts" (Core
  already has CodeBrix.Audio.Vorbis.Ogg and .Contracts namespaces).
- NRT is off: no `?` on reference types, no `!`. Value-type nullables are fine.
- XML doc comments on every public and protected member; no NoWarn.
- Native signatures stay blittable (pointers, ints, floats) so LibraryImport
  generates no marshalling.
- Tests: <Class>Tests.cs, snake_case method names (Member_does_thing or
  plain_snake_case), //Arrange //Act //Assert in multi-statement bodies,
  expression-bodied single-statement tests, SilverAssertions fluent style,
  TestContext.Current.CancellationToken on any cancellable call.


PROVENANCE / VENDORED SOURCES
=============================
vorbis-native-tools/ogg/ (libogg) and vorbis-native-tools/vorbis/ (libvorbis)
are unmodified Xiph.Org release tarballs; the tarballs are in
vorbis-native-tools/upstream-downloads/. Details and full licence texts:
THIRD-PARTY-NOTICES.txt and vorbis-native-tools/UPSTREAM.txt. The managed code
and the shim are original to this repository.


NOTES
=====
- OggVorbisEncoder.Flush() ENDS the stream (Vorbis has no mid-stream flush);
  Encode after Flush throws InvalidOperationException, after Dispose
  ObjectDisposedException.
- A pure sine compresses so well that a second or two of it may fit in a single
  Ogg page; tests that need several audio pages use seeded noise.
- The registry writer carries a partial frame between Write calls; a partial
  frame still held at Finish is dropped.
- CodeBrix.Audio.Core is pinned in BOTH csprojs (library and tests); raise them
  together.


================================================================================
END OF MAINTAINER-README
================================================================================
