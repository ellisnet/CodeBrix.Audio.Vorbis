#!/usr/bin/env bash
# ==============================================================================================
# build-host.sh - a quick, NOT-SHIPPED build of libcodebrix_vorbis.so with THIS machine's tools
# ==============================================================================================
#
#   ./build-host.sh
#
# Builds shim/ (+ the unmodified ogg/ and vorbis/ sources) with the host's own cmake and C
# compiler into output/host/libcodebrix_vorbis.so and runs smoke-test.c against it. It exists so
# the managed tests can run on a Linux dev box before (or without) a container build.
#
# The result is NEVER copied into src/.../runtimes: its glibc floor is whatever this machine has.
# Shipped Linux binaries come only from linux/build.sh (manylinux containers, --network none).
# Reads only this repository; installs nothing.
# ==============================================================================================
set -euo pipefail

TOOLS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRATCH="$TOOLS_DIR/build-scratch/host"
OUT="$TOOLS_DIR/output/host"

for t in cmake cc; do
    command -v "$t" > /dev/null 2>&1 || { echo "ERROR: $t is not installed (this script installs nothing)." >&2; exit 1; }
done

rm -rf "$SCRATCH"
mkdir -p "$SCRATCH" "$OUT"
cmake -S "$TOOLS_DIR/shim" -B "$SCRATCH" -DCMAKE_BUILD_TYPE=Release > "$SCRATCH/configure.txt"
cmake --build "$SCRATCH" -j "$(nproc)"
cp "$SCRATCH/libcodebrix_vorbis.so" "$OUT/libcodebrix_vorbis.so"
bash "$TOOLS_DIR/licenses/compose-license-vorbis.sh" --check
cp "$TOOLS_DIR/licenses/LICENSE-Vorbis.txt" "$OUT/LICENSE-Vorbis.txt"
cp "$TOOLS_DIR/ogg/COPYING" "$OUT/LICENSE-Ogg.txt"
cc -std=c11 -O1 -o "$OUT/smoke-test" "$TOOLS_DIR/smoke-test.c" -ldl -lm
"$OUT/smoke-test" "$OUT/libcodebrix_vorbis.so" "$OUT/smoke-test-440hz.ogg"
rm -rf "$SCRATCH"
echo
echo "host build: $OUT/libcodebrix_vorbis.so ($(stat -c %s "$OUT/libcodebrix_vorbis.so") bytes) - NOT for shipping"
