/*
 * smoke-test.c - the load-and-run verification program for the codebrix_vorbis native library
 *
 * Part of CodeBrix.Audio.Vorbis's vorbis-native-tools. One file, no build system needed: every
 * platform's gate compiles it with the plain C compiler and runs it against the STAGED (stripped,
 * about-to-be-shipped) library, so the bytes that are tested are the bytes that ship.
 *
 *     Linux / Android :  cc -std=c11 -O1 -o smoke-test smoke-test.c -ldl
 *     macOS           :  cc -std=c11 -O1 -o smoke-test smoke-test.c
 *     Windows (MSVC)  :  cl /nologo /O1 smoke-test.c
 *
 *     ./smoke-test <path-to-library> [output.ogg]
 *
 * It loads the library the way .NET's DllImport does (dlopen / LoadLibrary, by path), resolves
 * every entry point of codebrix_vorbis.h by name, and then:
 *   1. codebrix_vorbis_api_version() must be 1; prints codebrix_vorbis_library_version(),
 *   2. creates a stereo 44100 Hz quality-0.4 encoder, adds an ENCODER comment, writes headers,
 *   3. encodes 1 s of a 440 Hz sine (written in uneven chunks), finishes, drains the output,
 *   4. walks every Ogg page: "OggS" at offset 0, page structure consistent, EOS flag on the LAST
 *      page only, BOS on the first page only, one serial number, granule of the last page == 44100,
 *   5. checks the contract's error paths (null handle, write before headers, comment after headers,
 *      write after finish, bad arguments, headers-only stream ending in EOS),
 *   6. optionally writes the bytes to output.ogg (for listening / decoding elsewhere).
 * It never decodes. Exit code 0 means every step passed; anything else names the step that failed.
 *
 * Function types are declared by hand from codebrix_vorbis.h (int32_t / int64_t / float / pointers
 * only, default C calling convention on every platform).
 */

#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#if defined(_WIN32)
#  include <windows.h>
typedef HMODULE lib_handle;
static lib_handle lib_open(const char* path) { return LoadLibraryA(path); }
static void* lib_sym(lib_handle h, const char* name) { return (void*)GetProcAddress(h, name); }
#else
#  include <dlfcn.h>
typedef void* lib_handle;
static lib_handle lib_open(const char* path) { return dlopen(path, RTLD_NOW | RTLD_LOCAL); }
static void* lib_sym(lib_handle h, const char* name) { return dlsym(h, name); }
#endif

typedef struct enc enc;
typedef int32_t     (*fn_api_version)(void);
typedef const char* (*fn_library_version)(void);
typedef int32_t     (*fn_create)(int32_t, int32_t, int32_t, float, int32_t, int32_t, int32_t, int32_t, enc**);
typedef int32_t     (*fn_add_comment)(enc*, const char*, const char*);
typedef int32_t     (*fn_write_headers)(enc*);
typedef int32_t     (*fn_write_float)(enc*, const float*, int32_t);
typedef int32_t     (*fn_finish)(enc*);
typedef int64_t     (*fn_pending)(enc*);
typedef int32_t     (*fn_read)(enc*, uint8_t*, int32_t);
typedef int64_t     (*fn_granule)(enc*);
typedef void        (*fn_destroy)(enc*);

static fn_api_version     p_api_version;
static fn_library_version p_library_version;
static fn_create          p_create;
static fn_add_comment     p_add_comment;
static fn_write_headers   p_write_headers;
static fn_write_float     p_write_float;
static fn_finish          p_finish;
static fn_pending         p_pending;
static fn_read            p_read;
static fn_granule         p_granule;
static fn_destroy         p_destroy;

static int failures = 0;

#define CHECK(cond, what) do { if (!(cond)) { fprintf(stderr, "FAIL: %s\n", what); failures++; } \
                               else { printf("ok:   %s\n", what); } } while (0)

/* growable byte sink */
static uint8_t* g_bytes = NULL;
static size_t   g_len = 0, g_cap = 0;

static int drain(enc* e)
{
    uint8_t chunk[3000]; /* deliberately not a page multiple: partial reads must work */
    int32_t n;
    while ((n = p_read(e, chunk, (int32_t)sizeof(chunk))) > 0)
    {
        if (g_len + (size_t)n > g_cap)
        {
            size_t cap = g_cap ? g_cap * 2 : 65536;
            while (cap < g_len + (size_t)n) cap *= 2;
            g_bytes = (uint8_t*)realloc(g_bytes, cap);
            if (!g_bytes) { fprintf(stderr, "FAIL: out of memory\n"); exit(90); }
            g_cap = cap;
        }
        memcpy(g_bytes + g_len, chunk, (size_t)n);
        g_len += (size_t)n;
    }
    return n; /* 0, or a negative error code */
}

typedef struct
{
    int pages, bos_pages, eos_pages, eos_on_last, bos_on_first, serial_consistent, structure_ok;
    int64_t last_granule;
} page_report;

static uint32_t rd32(const uint8_t* p) { return (uint32_t)p[0] | ((uint32_t)p[1] << 8) | ((uint32_t)p[2] << 16) | ((uint32_t)p[3] << 24); }

static page_report walk_pages(const uint8_t* b, size_t len)
{
    page_report r;
    size_t off = 0;
    uint32_t serial = 0;
    memset(&r, 0, sizeof(r));
    r.serial_consistent = 1;
    r.structure_ok = 1;
    r.last_granule = -1;
    while (off < len)
    {
        size_t nseg, body = 0, i, header_len;
        uint8_t flags;
        if (len - off < 27 || memcmp(b + off, "OggS", 4) != 0 || b[off + 4] != 0) { r.structure_ok = 0; break; }
        flags = b[off + 5];
        nseg = b[off + 26];
        header_len = 27 + nseg;
        if (len - off < header_len) { r.structure_ok = 0; break; }
        for (i = 0; i < nseg; i++) body += b[off + 27 + i];
        if (len - off < header_len + body) { r.structure_ok = 0; break; }
        if (r.pages == 0) serial = rd32(b + off + 14);
        else if (rd32(b + off + 14) != serial) r.serial_consistent = 0;
        if (flags & 0x02) { r.bos_pages++; if (r.pages == 0) r.bos_on_first = 1; }
        r.eos_on_last = (flags & 0x04) ? 1 : 0;
        if (flags & 0x04) r.eos_pages++;
        r.last_granule = (int64_t)((uint64_t)rd32(b + off + 6) | ((uint64_t)rd32(b + off + 10) << 32));
        r.pages++;
        off += header_len + body;
    }
    return r;
}

#define RESOLVE(var, type, name) do { var = (type)lib_sym(lib, name); \
    if (!var) { fprintf(stderr, "FAIL: symbol %s not exported\n", name); return 3; } } while (0)

int main(int argc, char** argv)
{
    const int rate = 44100, channels = 2;
    const double pi = 3.14159265358979323846;
    lib_handle lib;
    enc* e = NULL;
    float* pcm;
    int i, written, rc;
    page_report r;
    const int chunks[] = { 1, 999, 4096, 10000, 7, 28997 }; /* sums to 44100 */

    if (argc < 2)
    {
        fprintf(stderr, "usage: %s <path-to-codebrix_vorbis-library> [output.ogg]\n", argv[0]);
        return 2;
    }
    lib = lib_open(argv[1]);
    if (!lib)
    {
#if defined(_WIN32)
        fprintf(stderr, "FAIL: LoadLibrary(%s) error %lu\n", argv[1], (unsigned long)GetLastError());
#else
        fprintf(stderr, "FAIL: dlopen(%s): %s\n", argv[1], dlerror());
#endif
        return 3;
    }
    RESOLVE(p_api_version,     fn_api_version,     "codebrix_vorbis_api_version");
    RESOLVE(p_library_version, fn_library_version, "codebrix_vorbis_library_version");
    RESOLVE(p_create,          fn_create,          "codebrix_vorbis_encoder_create");
    RESOLVE(p_add_comment,     fn_add_comment,     "codebrix_vorbis_encoder_add_comment");
    RESOLVE(p_write_headers,   fn_write_headers,   "codebrix_vorbis_encoder_write_headers");
    RESOLVE(p_write_float,     fn_write_float,     "codebrix_vorbis_encoder_write_float");
    RESOLVE(p_finish,          fn_finish,          "codebrix_vorbis_encoder_finish");
    RESOLVE(p_pending,         fn_pending,         "codebrix_vorbis_encoder_pending_output");
    RESOLVE(p_read,            fn_read,            "codebrix_vorbis_encoder_read_output");
    RESOLVE(p_granule,         fn_granule,         "codebrix_vorbis_encoder_granule_position");
    RESOLVE(p_destroy,         fn_destroy,         "codebrix_vorbis_encoder_destroy");
    printf("ok:   all 11 entry points resolved\n");

    CHECK(p_api_version() == 1, "api_version == 1");
    printf("      library_version = \"%s\"\n", p_library_version());
    CHECK(strstr(p_library_version(), "1.3.7") != NULL, "library_version names libVorbis 1.3.7");

    /* ---- the 1 s 440 Hz stereo sine ---- */
    rc = p_create(channels, rate, 0, 0.4f, 0, -1, -1, 0x0C0DEB1C, &e);
    CHECK(rc == 0 && e != NULL, "create stereo 44100 Hz quality 0.4");
    if (!e) return 4;
    CHECK(p_granule(e) == -1, "granule_position == -1 before audio");
    CHECK(p_add_comment(e, "ENCODER", "codebrix_vorbis smoke-test") == 0, "add_comment before headers");
    CHECK(p_write_float(e, NULL, 0) == -3, "write_float before headers -> CBV_E_INVALID_STATE");
    CHECK(p_write_headers(e) == 0, "write_headers");
    CHECK(p_pending(e) > 0, "headers produced pending output");
    CHECK(p_write_headers(e) == -3, "second write_headers -> CBV_E_INVALID_STATE");
    CHECK(p_add_comment(e, "TITLE", "late") == -3, "add_comment after headers -> CBV_E_INVALID_STATE");
    CHECK(drain(e) == 0, "drain headers");

    pcm = (float*)malloc(sizeof(float) * (size_t)rate * (size_t)channels);
    for (i = 0; i < rate; i++)
    {
        float s = (float)(0.5 * sin(2.0 * pi * 440.0 * (double)i / (double)rate));
        pcm[i * 2] = s;
        pcm[i * 2 + 1] = s;
    }
    written = 0;
    rc = 0;
    for (i = 0; i < (int)(sizeof(chunks) / sizeof(chunks[0])); i++)
    {
        rc |= p_write_float(e, pcm + (size_t)written * channels, chunks[i]);
        written += chunks[i];
        if (drain(e) < 0) rc = -1;
    }
    CHECK(rc == 0 && written == rate, "write_float 44100 frames in 6 uneven chunks");
    CHECK(p_write_float(e, NULL, 0) == 0, "write_float 0 frames is a no-op");
    CHECK(p_write_float(e, NULL, 5) == -1, "write_float NULL samples -> CBV_E_INVALID_ARGUMENT");
    CHECK(p_finish(e) == 0, "finish");
    CHECK(p_finish(e) == -3, "second finish -> CBV_E_INVALID_STATE");
    CHECK(p_write_float(e, pcm, 1) == -3, "write_float after finish -> CBV_E_INVALID_STATE");
    CHECK(drain(e) == 0 && p_pending(e) == 0, "drain to empty after finish");
    CHECK(p_granule(e) == rate, "granule_position == 44100 after finish");
    p_destroy(e);
    p_destroy(NULL);
    free(pcm);

    printf("      encoded %lu bytes\n", (unsigned long)g_len);
    CHECK(g_len > 4 && memcmp(g_bytes, "OggS", 4) == 0, "\"OggS\" at offset 0");
    r = walk_pages(g_bytes, g_len);
    printf("      %d Ogg pages, last granule %lld\n", r.pages, (long long)r.last_granule);
    CHECK(r.structure_ok, "every page well-formed and contiguous");
    CHECK(r.pages >= 3, "at least 3 pages");
    CHECK(r.bos_on_first && r.bos_pages == 1, "BOS on the first page only");
    CHECK(r.eos_on_last && r.eos_pages == 1, "EOS on the last page only");
    CHECK(r.serial_consistent, "one serial number throughout");
    CHECK(r.last_granule == rate, "last page granule == 44100");
    CHECK(g_len > 2000 && g_len < 60000, "plausible size for 1 s at q0.4");

    if (argc >= 3)
    {
        FILE* f = fopen(argv[2], "wb");
        CHECK(f && fwrite(g_bytes, 1, g_len, f) == g_len && fclose(f) == 0, "wrote output file");
    }

    /* ---- contract error paths ---- */
    e = (enc*)0x1;
    CHECK(p_create(0, rate, 0, 0.4f, 0, -1, -1, 1, &e) == -1 && e == NULL, "create channels 0 -> CBV_E_INVALID_ARGUMENT, handle NULL");
    CHECK(p_create(2, rate, 7, 0.4f, 0, -1, -1, 1, &e) == -1, "create bad mode -> CBV_E_INVALID_ARGUMENT");
    CHECK(p_create(2, rate, 0, 1.5f, 0, -1, -1, 1, &e) == -1, "create quality 1.5 -> CBV_E_INVALID_ARGUMENT");
    CHECK(p_create(2, rate, 1, 0.0f, 0, -1, -1, 1, &e) == -1, "create bitrate 0 -> CBV_E_INVALID_ARGUMENT");
    CHECK(p_create(2, rate, 0, 0.4f, 0, -1, -1, 1, NULL) == -1, "create NULL out -> CBV_E_INVALID_ARGUMENT");
    rc = p_create(2, 1000000, 0, 0.4f, 0, -1, -1, 1, &e);
    CHECK(rc == -4 || rc == -5, "create 1 MHz -> a libvorbis refusal code");
    if (rc == 0) p_destroy(e);
    CHECK(p_write_headers(NULL) == -1 && p_finish(NULL) == -1 && p_pending(NULL) < 0
          && p_read(NULL, NULL, 0) < 0 && p_add_comment(NULL, "A", "B") == -1, "NULL handle -> CBV_E_INVALID_ARGUMENT");

    /* bitrate mode creates */
    e = NULL;
    rc = p_create(2, 48000, 1, 0.0f, 128000, -1, -1, 7, &e);
    CHECK(rc == 0 && e, "create bitrate mode 128 kbit/s 48 kHz");
    if (e) p_destroy(e);

    /* headers-only stream must still end in EOS */
    e = NULL;
    rc = p_create(1, 48000, 0, 0.4f, 0, -1, -1, 99, &e);
    CHECK(rc == 0 && e, "create mono 48 kHz");
    if (e)
    {
        CHECK(p_finish(e) == -3, "finish before headers -> CBV_E_INVALID_STATE");
        g_len = 0;
        CHECK(p_write_headers(e) == 0 && p_finish(e) == 0 && drain(e) == 0, "headers-only finish");
        r = walk_pages(g_bytes, g_len);
        printf("      headers-only stream: %lu bytes, %d pages\n", (unsigned long)g_len, r.pages);
        CHECK(r.structure_ok && r.eos_on_last && r.eos_pages == 1, "headers-only stream ends with EOS");
        p_destroy(e);
    }

    free(g_bytes);
    if (failures)
    {
        fprintf(stderr, "SMOKE TEST FAILED: %d check(s)\n", failures);
        return 1;
    }
    printf("SMOKE TEST PASSED\n");
    return 0;
}
