/*
 * codebrix_vorbis.c - implementation of the fixed-ABI contract in codebrix_vorbis.h.
 *
 * Wraps libogg + libvorbis + libvorbisenc (Xiph.Org, BSD-3, statically linked, unmodified) behind
 * opaque handles. Ogg pages are accumulated in a growable byte buffer owned by the handle and
 * drained by the caller with codebrix_vorbis_encoder_read_output.
 */
#include "codebrix_vorbis.h"

#include <stddef.h>
#include <stdlib.h>
#include <string.h>

#include <ogg/ogg.h>
#include <vorbis/codec.h>
#include <vorbis/vorbisenc.h>

/* Frames handed to vorbis_analysis_buffer per chunk; keeps libvorbis's internal PCM buffer bounded
 * no matter how large a single write_float call is. */
#define CBV_CHUNK_FRAMES 4096

struct codebrix_vorbis_encoder
{
    vorbis_info      info;
    vorbis_comment   comment;
    vorbis_dsp_state dsp;
    vorbis_block     block;
    ogg_stream_state stream;

    int32_t channels;
    int     headers_written;
    int     finished;
    int     faulted;          /* an internal failure left the stream unusable; writes refuse */

    uint8_t* out;             /* pending output bytes live in out[out_start .. out_end) */
    size_t   out_start;
    size_t   out_end;
    size_t   out_capacity;

    int64_t  granule;         /* granule position of the last completed audio page, -1 before one */
};

static int32_t map_vorbis_error(int rc)
{
    switch (rc)
    {
        case OV_EIMPL:  return CBV_E_VORBIS_UNSUPPORTED;
        case OV_EINVAL: return CBV_E_VORBIS_INVALID;
        default:        return CBV_E_VORBIS_FAULT;
    }
}

/* Appends bytes to the pending-output buffer, compacting or growing it as needed. */
static int32_t append_bytes(codebrix_vorbis_encoder* e, const unsigned char* data, long length)
{
    size_t len;
    size_t pending;

    if (length <= 0)
        return CBV_OK;
    len = (size_t)length;
    pending = e->out_end - e->out_start;

    if (e->out_capacity - e->out_end < len)
    {
        /* First try to reclaim the already-read prefix. */
        if (e->out_start > 0)
        {
            if (pending > 0)
                memmove(e->out, e->out + e->out_start, pending);
            e->out_start = 0;
            e->out_end = pending;
        }
        if (e->out_capacity - e->out_end < len)
        {
            size_t needed = e->out_end + len;
            size_t capacity = e->out_capacity ? e->out_capacity : 16384;
            uint8_t* grown;
            while (capacity < needed)
            {
                if (capacity > ((size_t)-1) / 2)
                {
                    capacity = needed;
                    break;
                }
                capacity *= 2;
            }
            grown = (uint8_t*)realloc(e->out, capacity);
            if (grown == NULL)
                return CBV_E_OUT_OF_MEMORY;
            e->out = grown;
            e->out_capacity = capacity;
        }
    }

    memcpy(e->out + e->out_end, data, len);
    e->out_end += len;
    return CBV_OK;
}

static int32_t append_page(codebrix_vorbis_encoder* e, const ogg_page* page, int is_audio)
{
    int32_t rc = append_bytes(e, page->header, page->header_len);
    if (rc != CBV_OK)
        return rc;
    rc = append_bytes(e, page->body, page->body_len);
    if (rc != CBV_OK)
        return rc;
    if (is_audio)
    {
        ogg_int64_t gp = ogg_page_granulepos(page);
        if (gp >= 0)
            e->granule = (int64_t)gp;
    }
    return CBV_OK;
}

/* Pulls every finished block out of the analysis pipeline, packetises it and appends every
 * completed page. With flush != 0 the remaining partial page is forced out too (end of stream). */
static int32_t drain(codebrix_vorbis_encoder* e, int flush)
{
    ogg_packet packet;
    ogg_page   page;
    int        rc;
    int32_t    arc;

    while ((rc = vorbis_analysis_blockout(&e->dsp, &e->block)) == 1)
    {
        rc = vorbis_analysis(&e->block, NULL);
        if (rc != 0)
            return map_vorbis_error(rc);
        rc = vorbis_bitrate_addblock(&e->block);
        if (rc != 0)
            return map_vorbis_error(rc);

        while ((rc = vorbis_bitrate_flushpacket(&e->dsp, &packet)) == 1)
        {
            if (ogg_stream_packetin(&e->stream, &packet) != 0)
                return CBV_E_VORBIS_FAULT;
            while (ogg_stream_pageout(&e->stream, &page) != 0)
            {
                arc = append_page(e, &page, 1);
                if (arc != CBV_OK)
                    return arc;
            }
        }
        if (rc < 0)
            return map_vorbis_error(rc);
    }
    if (rc < 0)
        return map_vorbis_error(rc);

    if (flush)
    {
        while (ogg_stream_flush(&e->stream, &page) != 0)
        {
            arc = append_page(e, &page, 1);
            if (arc != CBV_OK)
                return arc;
        }
    }
    if (ogg_stream_check(&e->stream) != 0)
        return CBV_E_VORBIS_FAULT;
    return CBV_OK;
}

CBV_EXPORT int32_t codebrix_vorbis_api_version(void)
{
    return CODEBRIX_VORBIS_API_VERSION;
}

CBV_EXPORT const char* codebrix_vorbis_library_version(void)
{
    return vorbis_version_string();
}

CBV_EXPORT int32_t codebrix_vorbis_encoder_create(
    int32_t channels,
    int32_t sample_rate_hz,
    int32_t mode,
    float   quality,
    int32_t nominal_bitrate,
    int32_t min_bitrate,
    int32_t max_bitrate,
    int32_t serial_number,
    codebrix_vorbis_encoder** out_encoder)
{
    codebrix_vorbis_encoder* e;
    int rc;

    if (out_encoder == NULL)
        return CBV_E_INVALID_ARGUMENT;
    *out_encoder = NULL;

    if (channels < 1 || channels > 255 || sample_rate_hz <= 0)
        return CBV_E_INVALID_ARGUMENT;
    if (mode == CBV_MODE_QUALITY)
    {
        /* NaN fails both comparisons and is rejected; a little float slack at the ends. */
        if (!(quality >= -0.1001f && quality <= 1.0001f))
            return CBV_E_INVALID_ARGUMENT;
    }
    else if (mode == CBV_MODE_BITRATE)
    {
        if (nominal_bitrate <= 0)
            return CBV_E_INVALID_ARGUMENT;
        if ((min_bitrate != -1 && min_bitrate <= 0) || (max_bitrate != -1 && max_bitrate <= 0))
            return CBV_E_INVALID_ARGUMENT;
        if (min_bitrate != -1 && min_bitrate > nominal_bitrate)
            return CBV_E_INVALID_ARGUMENT;
        if (max_bitrate != -1 && max_bitrate < nominal_bitrate)
            return CBV_E_INVALID_ARGUMENT;
    }
    else
    {
        return CBV_E_INVALID_ARGUMENT;
    }

    e = (codebrix_vorbis_encoder*)calloc(1, sizeof(*e));
    if (e == NULL)
        return CBV_E_OUT_OF_MEMORY;
    e->channels = channels;
    e->granule = -1;

    vorbis_info_init(&e->info);
    if (mode == CBV_MODE_QUALITY)
    {
        float q = quality < -0.1f ? -0.1f : (quality > 1.0f ? 1.0f : quality);
        rc = vorbis_encode_init_vbr(&e->info, channels, sample_rate_hz, q);
    }
    else
    {
        rc = vorbis_encode_init(&e->info, channels, sample_rate_hz,
                                (long)max_bitrate, (long)nominal_bitrate, (long)min_bitrate);
    }
    if (rc != 0)
    {
        vorbis_info_clear(&e->info);
        free(e);
        return map_vorbis_error(rc);
    }

    vorbis_comment_init(&e->comment);

    rc = vorbis_analysis_init(&e->dsp, &e->info);
    if (rc != 0)
    {
        vorbis_comment_clear(&e->comment);
        vorbis_info_clear(&e->info);
        free(e);
        return CBV_E_VORBIS_FAULT;
    }
    rc = vorbis_block_init(&e->dsp, &e->block);
    if (rc != 0)
    {
        vorbis_dsp_clear(&e->dsp);
        vorbis_comment_clear(&e->comment);
        vorbis_info_clear(&e->info);
        free(e);
        return CBV_E_VORBIS_FAULT;
    }
    rc = ogg_stream_init(&e->stream, (int)serial_number);
    if (rc != 0)
    {
        vorbis_block_clear(&e->block);
        vorbis_dsp_clear(&e->dsp);
        vorbis_comment_clear(&e->comment);
        vorbis_info_clear(&e->info);
        free(e);
        return CBV_E_OUT_OF_MEMORY;
    }

    *out_encoder = e;
    return CBV_OK;
}

CBV_EXPORT int32_t codebrix_vorbis_encoder_add_comment(
    codebrix_vorbis_encoder* encoder,
    const char* tag_utf8,
    const char* value_utf8)
{
    if (encoder == NULL || tag_utf8 == NULL || value_utf8 == NULL || tag_utf8[0] == '\0')
        return CBV_E_INVALID_ARGUMENT;
    if (encoder->headers_written || encoder->finished || encoder->faulted)
        return CBV_E_INVALID_STATE;
    /* libvorbis takes non-const pointers but only reads them. */
    vorbis_comment_add_tag(&encoder->comment, (char*)tag_utf8, (char*)value_utf8);
    return CBV_OK;
}

CBV_EXPORT int32_t codebrix_vorbis_encoder_write_headers(codebrix_vorbis_encoder* encoder)
{
    ogg_packet header;
    ogg_packet header_comment;
    ogg_packet header_code;
    ogg_page   page;
    int        rc;
    int32_t    arc;

    if (encoder == NULL)
        return CBV_E_INVALID_ARGUMENT;
    if (encoder->headers_written || encoder->finished || encoder->faulted)
        return CBV_E_INVALID_STATE;

    rc = vorbis_analysis_headerout(&encoder->dsp, &encoder->comment,
                                   &header, &header_comment, &header_code);
    if (rc != 0)
    {
        encoder->faulted = 1;
        return map_vorbis_error(rc);
    }
    if (ogg_stream_packetin(&encoder->stream, &header) != 0
        || ogg_stream_packetin(&encoder->stream, &header_comment) != 0
        || ogg_stream_packetin(&encoder->stream, &header_code) != 0)
    {
        encoder->faulted = 1;
        return CBV_E_VORBIS_FAULT;
    }
    /* The headers must end on a page boundary so audio starts on a fresh page. */
    while (ogg_stream_flush(&encoder->stream, &page) != 0)
    {
        arc = append_page(encoder, &page, 0);
        if (arc != CBV_OK)
        {
            encoder->faulted = 1;
            return arc;
        }
    }
    encoder->headers_written = 1;
    return CBV_OK;
}

CBV_EXPORT int32_t codebrix_vorbis_encoder_write_float(
    codebrix_vorbis_encoder* encoder,
    const float* interleaved_samples,
    int32_t frame_count)
{
    const float* src;
    int32_t remaining;
    int32_t channels;

    if (encoder == NULL || frame_count < 0 || (frame_count > 0 && interleaved_samples == NULL))
        return CBV_E_INVALID_ARGUMENT;
    if (!encoder->headers_written || encoder->finished || encoder->faulted)
        return CBV_E_INVALID_STATE;
    /* vorbis_analysis_wrote(0) would mean end-of-stream, so 0 frames must stay a no-op. */
    if (frame_count == 0)
        return CBV_OK;

    channels = encoder->channels;
    src = interleaved_samples;
    remaining = frame_count;
    while (remaining > 0)
    {
        int32_t frames = remaining < CBV_CHUNK_FRAMES ? remaining : CBV_CHUNK_FRAMES;
        float** buffer = vorbis_analysis_buffer(&encoder->dsp, frames);
        int32_t f;
        int32_t c;
        int32_t rc;

        if (buffer == NULL)
        {
            encoder->faulted = 1;
            return CBV_E_OUT_OF_MEMORY;
        }
        for (c = 0; c < channels; c++)
        {
            float* dst = buffer[c];
            const float* s = src + c;
            for (f = 0; f < frames; f++)
            {
                dst[f] = *s;
                s += channels;
            }
        }
        if (vorbis_analysis_wrote(&encoder->dsp, frames) != 0)
        {
            encoder->faulted = 1;
            return CBV_E_VORBIS_FAULT;
        }
        rc = drain(encoder, 0);
        if (rc != CBV_OK)
        {
            encoder->faulted = 1;
            return rc;
        }
        src += (size_t)frames * (size_t)channels;
        remaining -= frames;
    }
    return CBV_OK;
}

CBV_EXPORT int32_t codebrix_vorbis_encoder_finish(codebrix_vorbis_encoder* encoder)
{
    int32_t rc;

    if (encoder == NULL)
        return CBV_E_INVALID_ARGUMENT;
    if (!encoder->headers_written || encoder->finished || encoder->faulted)
        return CBV_E_INVALID_STATE;

    encoder->finished = 1;
    if (vorbis_analysis_wrote(&encoder->dsp, 0) != 0)
    {
        encoder->faulted = 1;
        return CBV_E_VORBIS_FAULT;
    }
    rc = drain(encoder, 1);
    if (rc != CBV_OK)
        encoder->faulted = 1;
    return rc;
}

CBV_EXPORT int64_t codebrix_vorbis_encoder_pending_output(codebrix_vorbis_encoder* encoder)
{
    if (encoder == NULL)
        return CBV_E_INVALID_ARGUMENT;
    return (int64_t)(encoder->out_end - encoder->out_start);
}

CBV_EXPORT int32_t codebrix_vorbis_encoder_read_output(
    codebrix_vorbis_encoder* encoder,
    uint8_t* destination,
    int32_t capacity)
{
    size_t pending;
    size_t count;

    if (encoder == NULL || capacity < 0 || (capacity > 0 && destination == NULL))
        return CBV_E_INVALID_ARGUMENT;

    pending = encoder->out_end - encoder->out_start;
    count = pending < (size_t)capacity ? pending : (size_t)capacity;
    if (count == 0)
        return 0;

    memcpy(destination, encoder->out + encoder->out_start, count);
    encoder->out_start += count;
    if (encoder->out_start == encoder->out_end)
    {
        encoder->out_start = 0;
        encoder->out_end = 0;
    }
    return (int32_t)count;
}

CBV_EXPORT int64_t codebrix_vorbis_encoder_granule_position(codebrix_vorbis_encoder* encoder)
{
    if (encoder == NULL)
        return CBV_E_INVALID_ARGUMENT;
    return encoder->granule;
}

CBV_EXPORT void codebrix_vorbis_encoder_destroy(codebrix_vorbis_encoder* encoder)
{
    if (encoder == NULL)
        return;
    ogg_stream_clear(&encoder->stream);
    vorbis_block_clear(&encoder->block);
    vorbis_dsp_clear(&encoder->dsp);
    vorbis_comment_clear(&encoder->comment);
    vorbis_info_clear(&encoder->info);
    free(encoder->out);
    free(encoder);
}
