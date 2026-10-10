vorbis-native-tools/patches/
================================================================================
EMPTY ON PURPOSE. As of 2026-10-10 libogg 1.3.6 and libvorbis 1.3.7 are built
exactly as released.

If a change to the Xiph source is ever unavoidable, it goes here as a unified
diff named NNNN-<ogg|vorbis>-<what>.patch, made relative to vorbis-native-tools/
(so paths start with ogg/ or vorbis/). Every build script copies ogg/, vorbis/
and shim/ into a scratch folder and applies every *.patch here with
`patch -p1 --forward --batch` (in name order) to THAT copy; a patch that does not
apply fails the build. ogg/ and vorbis/ themselves are never edited.
Record any patch in UPSTREAM.txt and BUILD-PROVENANCE.txt.
