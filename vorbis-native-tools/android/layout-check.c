/*
 * layout-check.c - compile-time checks of the codebrix_vorbis.h ABI the managed binding assumes.
 *
 * Cross-compiled (never executed) for EVERY Android ABI by build.sh, so the checks run on the
 * target's data model, not the build host's. codebrix_vorbis.h deliberately has no struct and no
 * C `long`; what the managed LibraryImport declarations rely on is the width of the scalar
 * types, pointers, and the numeric values of the result / mode constants. Any drift fails the
 * build here instead of corrupting a call on a device.
 */
#include <stddef.h>
#include <stdint.h>
#include "codebrix_vorbis.h"

#define SIZE(type, size) _Static_assert(sizeof(type) == (size), #type " size")
#define VALUE(name, value) _Static_assert((name) == (value), #name " value")

SIZE(int32_t, 4);
SIZE(int64_t, 8);
SIZE(float, 4);
SIZE(uint8_t, 1);
SIZE(void*, 8);                       /* both shipped Android ABIs are 64-bit */
SIZE(codebrix_vorbis_encoder*, 8);
SIZE(const char*, 8);

VALUE(CODEBRIX_VORBIS_API_VERSION, 1);
VALUE(CBV_OK, 0);
VALUE(CBV_E_INVALID_ARGUMENT, -1);
VALUE(CBV_E_OUT_OF_MEMORY, -2);
VALUE(CBV_E_INVALID_STATE, -3);
VALUE(CBV_E_VORBIS_UNSUPPORTED, -4);
VALUE(CBV_E_VORBIS_INVALID, -5);
VALUE(CBV_E_VORBIS_FAULT, -6);
VALUE(CBV_MODE_QUALITY, 0);
VALUE(CBV_MODE_BITRATE, 1);

/* The function types, exactly as the managed side declares them. A changed signature in the
 * header makes one of these initialisations a constraint violation (an error with -Werror). */
static int32_t     (*const p_api)(void) = codebrix_vorbis_api_version;
static const char* (*const p_libver)(void) = codebrix_vorbis_library_version;
static int32_t     (*const p_create)(int32_t, int32_t, int32_t, float, int32_t, int32_t, int32_t, int32_t,
                                     codebrix_vorbis_encoder**) = codebrix_vorbis_encoder_create;
static int32_t     (*const p_comment)(codebrix_vorbis_encoder*, const char*, const char*) = codebrix_vorbis_encoder_add_comment;
static int32_t     (*const p_headers)(codebrix_vorbis_encoder*) = codebrix_vorbis_encoder_write_headers;
static int32_t     (*const p_write)(codebrix_vorbis_encoder*, const float*, int32_t) = codebrix_vorbis_encoder_write_float;
static int32_t     (*const p_finish)(codebrix_vorbis_encoder*) = codebrix_vorbis_encoder_finish;
static int64_t     (*const p_pending)(codebrix_vorbis_encoder*) = codebrix_vorbis_encoder_pending_output;
static int32_t     (*const p_read)(codebrix_vorbis_encoder*, uint8_t*, int32_t) = codebrix_vorbis_encoder_read_output;
static int64_t     (*const p_granule)(codebrix_vorbis_encoder*) = codebrix_vorbis_encoder_granule_position;
static void        (*const p_destroy)(codebrix_vorbis_encoder*) = codebrix_vorbis_encoder_destroy;

/* Referenced so -Wunused does not complain; never called. */
const void* codebrix_vorbis_layout_check_refs[] = {
    (const void*)&p_api, (const void*)&p_libver, (const void*)&p_create, (const void*)&p_comment,
    (const void*)&p_headers, (const void*)&p_write, (const void*)&p_finish, (const void*)&p_pending,
    (const void*)&p_read, (const void*)&p_granule, (const void*)&p_destroy
};
