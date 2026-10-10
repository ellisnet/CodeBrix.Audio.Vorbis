vorbis-native-tools/output/ - build results (NOT committed, except this README)
================================================================================
Every build script writes here, one folder per RID:

  output/<rid>/<library>          the stripped library, exactly as the package ships it
  output/<rid>/LICENSE-Vorbis.txt  verbatim copy of vorbis/COPYING (BSD-3)
  output/<rid>/LICENSE-Ogg.txt     verbatim copy of ogg/COPYING (BSD-3)
  output/<rid>/unstripped/        the pre-strip twin (Linux / Android / macOS)
  output/<rid>/BUILD-INFO.txt     toolchain, flags, sizes, sha256, gate results
  output/<rid>/smoke-test*        the gate's smoke-test binary

  output/host/libcodebrix_vorbis.so   a plain build with this machine's own cmake + cc, for
                                      running the managed tests on a Linux dev box. NEVER shipped
                                      (its glibc floor is whatever the dev box has).
                                      Rebuild:  ./build-host.sh

See ../README.txt.
