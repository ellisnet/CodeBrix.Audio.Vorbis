/*
 * codebrix_vorbis.h - the fixed-ABI C surface of the codebrix_vorbis native library.
 *
 * This is the ONLY contract between the managed CodeBrix.Audio.Vorbis assembly and the native
 * library. It deliberately exposes no libogg / libvorbis struct, no C `long` and no callback:
 * every parameter is an opaque handle, an int32_t, an int64_t, a float, or a pointer to bytes,
 * so one managed DllImport set works on every RID (C `long` is 32-bit on Windows and 64-bit on
 * every other platform the family ships; libogg/libvorbis structs are full of them).
 *
 * The library statically links libogg + libvorbis + libvorbisenc (Xiph, BSD-3) and exports only
 * the functions below. Ogg pages are produced inside the shim and handed to the caller as raw
 * bytes; the caller writes them to its stream in order and the result is a valid Ogg Vorbis file.
 *
 * Threading: a handle may be used from one thread at a time. Different handles are independent.
 * Encoding: 32-bit float interleaved samples in [-1, 1]; libvorbis clips anything beyond.
 */
#ifndef CODEBRIX_VORBIS_H
#define CODEBRIX_VORBIS_H

#include <stdint.h>

#if defined(_WIN32)
#  define CBV_EXPORT __declspec(dllexport)
#else
#  define CBV_EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* ---- Versions ---------------------------------------------------------------------------- */

/* The version of THIS contract. The managed side refuses to run against a library whose value
 * differs from the one it was built for. Bump it whenever a function below changes shape. */
#define CODEBRIX_VORBIS_API_VERSION 1

/* Returns CODEBRIX_VORBIS_API_VERSION of the loaded library. */
CBV_EXPORT int32_t codebrix_vorbis_api_version(void);

/* Returns libvorbis's own version string (vorbis_version_string()), e.g. "Xiph.Org libVorbis 1.3.7".
 * The pointer is static; never free it. UTF-8 / ASCII. */
CBV_EXPORT const char* codebrix_vorbis_library_version(void);

/* ---- Result codes ------------------------------------------------------------------------ */

/* Every int32_t-returning function returns 0 (CBV_OK) or one of these negatives. The managed side
 * maps them to exceptions; keep the numbers stable (bump the API version if one must change). */
#define CBV_OK                      0
#define CBV_E_INVALID_ARGUMENT     -1   /* null handle/pointer, channels/rate/mode out of range */
#define CBV_E_OUT_OF_MEMORY        -2
#define CBV_E_INVALID_STATE        -3   /* e.g. add_comment after headers, write after finish */
#define CBV_E_VORBIS_UNSUPPORTED   -4   /* libvorbis OV_EIMPL: the mode/rate/bitrate combination is not supported */
#define CBV_E_VORBIS_INVALID       -5   /* libvorbis OV_EINVAL */
#define CBV_E_VORBIS_FAULT         -6   /* any other libvorbis / libogg failure */

/* ---- Encoder ----------------------------------------------------------------------------- */

typedef struct codebrix_vorbis_encoder codebrix_vorbis_encoder;

/* Encoding modes for codebrix_vorbis_encoder_create. */
#define CBV_MODE_QUALITY   0   /* VBR by quality: vorbis_encode_init_vbr(quality) */
#define CBV_MODE_BITRATE   1   /* managed bitrate: vorbis_encode_init(max, nominal, min) */

/*
 * Creates an encoder and writes nothing yet.
 *   channels          1..255 (libvorbis limit); 1 and 2 are the common cases.
 *   sample_rate_hz    e.g. 44100, 48000.
 *   mode              CBV_MODE_QUALITY or CBV_MODE_BITRATE.
 *   quality           CBV_MODE_QUALITY only: -0.1 .. 1.0 (libvorbis scale; 0.4 is roughly 128 kbit/s stereo).
 *   nominal_bitrate   CBV_MODE_BITRATE only: bits per second, > 0.
 *   min_bitrate       CBV_MODE_BITRATE only: bits per second, or -1 for unset.
 *   max_bitrate       CBV_MODE_BITRATE only: bits per second, or -1 for unset.
 *   serial_number     the Ogg logical-stream serial number; any int32_t (the caller picks, usually random).
 *   out_encoder       receives the handle on success, NULL on failure.
 * Returns CBV_OK or a CBV_E_* code. The vorbis_info / dsp / block state is fully initialised on success.
 */
CBV_EXPORT int32_t codebrix_vorbis_encoder_create(
    int32_t channels,
    int32_t sample_rate_hz,
    int32_t mode,
    float   quality,
    int32_t nominal_bitrate,
    int32_t min_bitrate,
    int32_t max_bitrate,
    int32_t serial_number,
    codebrix_vorbis_encoder** out_encoder);

/*
 * Adds a Vorbis comment (tag=value, both NUL-terminated UTF-8) to the comment header.
 * Only valid BEFORE codebrix_vorbis_encoder_write_headers; afterwards returns CBV_E_INVALID_STATE.
 * The shim always sets the vendor string itself (libvorbis does); tags are e.g. "TITLE", "ARTIST", "ENCODER".
 */
CBV_EXPORT int32_t codebrix_vorbis_encoder_add_comment(
    codebrix_vorbis_encoder* encoder,
    const char* tag_utf8,
    const char* value_utf8);

/*
 * Emits the three Vorbis header packets (identification, comments, codebooks) as Ogg pages into the
 * output buffer, flushing so the headers end on a page boundary (as every Ogg Vorbis file must).
 * Must be called exactly once, before the first write. Drain with codebrix_vorbis_encoder_read_output.
 */
CBV_EXPORT int32_t codebrix_vorbis_encoder_write_headers(codebrix_vorbis_encoder* encoder);

/*
 * Encodes `frame_count` frames of interleaved float samples (frame = one sample per channel, so the
 * buffer holds frame_count * channels floats). Completed Ogg pages are appended to the output buffer.
 * frame_count may be 0 (no-op). Returns CBV_OK or a CBV_E_* code.
 */
CBV_EXPORT int32_t codebrix_vorbis_encoder_write_float(
    codebrix_vorbis_encoder* encoder,
    const float* interleaved_samples,
    int32_t frame_count);

/*
 * Signals end of stream: flushes the analysis pipeline, emits the final packet with the EOS flag and
 * flushes the last page(s) into the output buffer. After this, write_float / finish return
 * CBV_E_INVALID_STATE; read_output still drains what is left. Calling finish on a never-written
 * encoder (headers only) is valid and produces an empty-but-valid Ogg Vorbis stream.
 */
CBV_EXPORT int32_t codebrix_vorbis_encoder_finish(codebrix_vorbis_encoder* encoder);

/*
 * Number of output bytes currently waiting to be read (completed Ogg pages). 0 when nothing is pending.
 * Returns a negative CBV_E_* code only for a null handle.
 */
CBV_EXPORT int64_t codebrix_vorbis_encoder_pending_output(codebrix_vorbis_encoder* encoder);

/*
 * Copies up to `capacity` bytes of pending output into `destination` and removes them from the
 * buffer. Returns the number of bytes copied (0 when nothing is pending) or a negative CBV_E_* code.
 * Call in a loop until it returns 0. Partial reads are fine: output is a byte stream.
 */
CBV_EXPORT int32_t codebrix_vorbis_encoder_read_output(
    codebrix_vorbis_encoder* encoder,
    uint8_t* destination,
    int32_t capacity);

/*
 * The granule position (total PCM frames) of the last page that was completed, i.e. how far the
 * emitted stream reaches. -1 before any audio page exists. Informational (progress reporting).
 */
CBV_EXPORT int64_t codebrix_vorbis_encoder_granule_position(codebrix_vorbis_encoder* encoder);

/* Releases everything. NULL is ignored. The handle is invalid afterwards. */
CBV_EXPORT void codebrix_vorbis_encoder_destroy(codebrix_vorbis_encoder* encoder);

#ifdef __cplusplus
}
#endif

#endif /* CODEBRIX_VORBIS_H */
