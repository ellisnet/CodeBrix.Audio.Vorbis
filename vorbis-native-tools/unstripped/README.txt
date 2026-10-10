================================================================================
vorbis-native-tools/unstripped - the durable home for pre-strip binaries
================================================================================

UNLIKE output/, THIS FOLDER IS COMMITTED. It keeps the unstripped mate of every
codebrix_vorbis binary that is shipped in the package, so crash dumps of a
stripped release binary can be symbolised after the build machine is gone.
Nothing here is shipped, and nothing here is an input to any build or pack step.

One folder per runtime identifier, mirroring
../../src/CodeBrix.Audio.Vorbis/runtimes/<rid>/native/:

  <rid>/libcodebrix_vorbis.so      pre-strip ELF with DWARF (Linux, Android)
  <rid>/libcodebrix_vorbis.dylib   pre-strip Mach-O (macOS; the .dSYM stays in output/)
  <rid>/codebrix_vorbis.pdb        the PDB of the shipped DLL (Windows)

The build scripts copy the twin here automatically when they adopt a binary
(ADOPT=1 / no -NoAdopt). Refresh SHA256SUMS after adding one:

    sha256sum */libcodebrix_vorbis.so > SHA256SUMS      # (and any .dylib/.pdb)

Verify a twin: its sha256 is the "SHA256 unstripped" line of that RID in
../BUILD-PROVENANCE.txt, and its build-id equals the shipped file's:

    readelf -n <rid>/libcodebrix_vorbis.so | grep 'Build ID'
    readelf -n ../../src/CodeBrix.Audio.Vorbis/runtimes/<rid>/native/libcodebrix_vorbis.so | grep 'Build ID'

STORED SO FAR (build-ids verified equal to the shipped twins by the gates, 2026-10-10):

  linux-x64      b1d12aff97f440459c7d2d15b35aff703efd17c6
  linux-arm64    f189624875e9c501c248e35e7e6188b43896dc66
  linux-riscv64  090824282064dd1ba6f33951fd8ba7a5623727a8
  android-arm64  6b897a3ad6eee637c242e9f331814a29a46ab9ba
  android-x64    ac0aa088ce47bfa6036f723156e487f87b3aa5a6
