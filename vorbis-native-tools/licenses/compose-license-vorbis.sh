#!/usr/bin/env bash
# ==============================================================================================
# compose-license-vorbis.sh - THE generator of licenses/LICENSE-Vorbis.txt (one source of truth)
# ==============================================================================================
#
#   ./compose-license-vorbis.sh            print the composed text to stdout
#   ./compose-license-vorbis.sh --write    (re)write licenses/LICENSE-Vorbis.txt
#   ./compose-license-vorbis.sh --check    exit 1 unless licenses/LICENSE-Vorbis.txt is current
#
# LICENSE-Vorbis.txt, shipped beside every codebrix_vorbis binary on all nine RIDs, is:
#   1. vorbis/COPYING verbatim (libvorbis's BSD-3 licence), then
#   2. a separator line, then
#   3. the third-party notice PRESERVED in vorbis/lib/lpc.c, verbatim: the comment block from
#      "/* Some of these routines" through the line "Carsten Bormann" (Jutta Degener and Carsten
#      Bormann, Technische Universitaet Berlin, 1992-1994). lpc.c is compiled into every binary,
#      and that notice's condition is that it is not removed - so it travels with the binary.
#
# Every build script (linux, android, macos, build-host.sh) runs --check before copying
# licenses/LICENSE-Vorbis.txt beside the binary; the Windows scripts copy the committed file
# (and check it begins with vorbis/COPYING). The block is found by its text, not by line
# numbers, and the script fails if either marker is missing. Reads only this repository.
# ==============================================================================================
set -euo pipefail

TOOLS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COPYING="$TOOLS_DIR/vorbis/COPYING"
LPC="$TOOLS_DIR/vorbis/lib/lpc.c"
TARGET="$TOOLS_DIR/licenses/LICENSE-Vorbis.txt"

compose() {
    [ -f "$COPYING" ] && [ -f "$LPC" ] || { echo "ERROR: vorbis/COPYING or vorbis/lib/lpc.c missing" >&2; exit 1; }
    grep -q '^/\* Some of these routines' "$LPC" && grep -q '^Carsten Bormann$' "$LPC" \
        || { echo "ERROR: the preserved-notice markers were not found in vorbis/lib/lpc.c" >&2; exit 1; }
    cat "$COPYING"
    printf '\n'
    printf '%s\n' '----------------------------------------------------------------------------------------------'
    printf '%s\n' 'The following notice is preserved verbatim from vorbis/lib/lpc.c (libvorbis 1.3.7), which is'
    printf '%s\n' 'compiled into this library:'
    printf '%s\n' '----------------------------------------------------------------------------------------------'
    printf '\n'
    sed -n '/^\/\* Some of these routines/,/^Carsten Bormann$/p' "$LPC"
}

case "${1:-}" in
    "")      compose ;;
    --write) compose > "$TARGET"; echo "wrote $TARGET" ;;
    --check)
        tmp="$(mktemp)"; compose > "$tmp"
        if cmp -s "$tmp" "$TARGET"; then rm -f "$tmp"; echo "licenses/LICENSE-Vorbis.txt is current"
        else rm -f "$tmp"; echo "ERROR: licenses/LICENSE-Vorbis.txt is stale or missing - run licenses/compose-license-vorbis.sh --write" >&2; exit 1; fi ;;
    *) echo "usage: $0 [--write|--check]" >&2; exit 2 ;;
esac
