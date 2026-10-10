using System;
using System.Runtime.InteropServices;

namespace CodeBrix.Audio.Vorbis.Interop;

/// <summary>
/// The codebrix_vorbis entry points, as source-generated <c>LibraryImport</c> declarations mirroring
/// <c>vorbis-native-tools/shim/codebrix_vorbis.h</c> one for one.
/// </summary>
/// <remarks>
/// Every parameter is an opaque handle, a 32- or 64-bit integer, a float or a byte pointer - the header keeps
/// C <c>long</c> and every libogg / libvorbis structure out of the contract - so one declaration set is right
/// on every platform. The static constructor installs the library resolver, so the first call through any of
/// these finds the native in the package's <c>runtimes/</c> layout.
/// </remarks>
internal static unsafe partial class VorbisNative
{
    static VorbisNative()
    {
        VorbisLibrary.EnsureResolverInstalled();
    }

    /// <summary>Returns the contract version of the loaded library.</summary>
    /// <returns><c>CODEBRIX_VORBIS_API_VERSION</c>.</returns>
    [LibraryImport(VorbisLibrary.LibraryName, EntryPoint = "codebrix_vorbis_api_version")]
    internal static partial int ApiVersion();

    /// <summary>Returns libvorbis's own version string. The pointer is static and owned by the library.</summary>
    /// <returns>A pointer to a NUL-terminated ASCII string.</returns>
    [LibraryImport(VorbisLibrary.LibraryName, EntryPoint = "codebrix_vorbis_library_version")]
    internal static partial IntPtr LibraryVersion();

    /// <summary>Creates an encoder.</summary>
    /// <param name="channels">Channel count.</param>
    /// <param name="sampleRateHz">Sample rate.</param>
    /// <param name="mode"><c>CBV_MODE_QUALITY</c> or <c>CBV_MODE_BITRATE</c>.</param>
    /// <param name="quality">Quality, quality mode only.</param>
    /// <param name="nominalBitrate">Nominal bitrate, bitrate mode only.</param>
    /// <param name="minBitrate">Minimum bitrate or -1, bitrate mode only.</param>
    /// <param name="maxBitrate">Maximum bitrate or -1, bitrate mode only.</param>
    /// <param name="serialNumber">The Ogg logical-stream serial number.</param>
    /// <param name="encoder">Receives the handle.</param>
    /// <returns>A <see cref="VorbisResult" /> code.</returns>
    [LibraryImport(VorbisLibrary.LibraryName, EntryPoint = "codebrix_vorbis_encoder_create")]
    internal static partial int EncoderCreate(
        int channels,
        int sampleRateHz,
        int mode,
        float quality,
        int nominalBitrate,
        int minBitrate,
        int maxBitrate,
        int serialNumber,
        IntPtr* encoder);

    /// <summary>Adds a comment to the comment header.</summary>
    /// <param name="encoder">The encoder.</param>
    /// <param name="tagUtf8">The NUL-terminated UTF-8 tag.</param>
    /// <param name="valueUtf8">The NUL-terminated UTF-8 value.</param>
    /// <returns>A <see cref="VorbisResult" /> code.</returns>
    [LibraryImport(VorbisLibrary.LibraryName, EntryPoint = "codebrix_vorbis_encoder_add_comment")]
    internal static partial int EncoderAddComment(IntPtr encoder, byte* tagUtf8, byte* valueUtf8);

    /// <summary>Emits the three header packets as Ogg pages into the output buffer.</summary>
    /// <param name="encoder">The encoder.</param>
    /// <returns>A <see cref="VorbisResult" /> code.</returns>
    [LibraryImport(VorbisLibrary.LibraryName, EntryPoint = "codebrix_vorbis_encoder_write_headers")]
    internal static partial int EncoderWriteHeaders(IntPtr encoder);

    /// <summary>Encodes interleaved float frames.</summary>
    /// <param name="encoder">The encoder.</param>
    /// <param name="interleavedSamples">The samples, <paramref name="frameCount" /> times the channel count.</param>
    /// <param name="frameCount">How many frames there are.</param>
    /// <returns>A <see cref="VorbisResult" /> code.</returns>
    [LibraryImport(VorbisLibrary.LibraryName, EntryPoint = "codebrix_vorbis_encoder_write_float")]
    internal static partial int EncoderWriteFloat(IntPtr encoder, float* interleavedSamples, int frameCount);

    /// <summary>Signals end of stream and flushes the final pages into the output buffer.</summary>
    /// <param name="encoder">The encoder.</param>
    /// <returns>A <see cref="VorbisResult" /> code.</returns>
    [LibraryImport(VorbisLibrary.LibraryName, EntryPoint = "codebrix_vorbis_encoder_finish")]
    internal static partial int EncoderFinish(IntPtr encoder);

    /// <summary>The number of output bytes waiting to be read.</summary>
    /// <param name="encoder">The encoder.</param>
    /// <returns>The byte count, or a negative <see cref="VorbisResult" /> code.</returns>
    [LibraryImport(VorbisLibrary.LibraryName, EntryPoint = "codebrix_vorbis_encoder_pending_output")]
    internal static partial long EncoderPendingOutput(IntPtr encoder);

    /// <summary>Copies pending output out and removes it from the buffer.</summary>
    /// <param name="encoder">The encoder.</param>
    /// <param name="destination">Where to copy to.</param>
    /// <param name="capacity">How many bytes fit.</param>
    /// <returns>The bytes copied, 0 when nothing is pending, or a negative <see cref="VorbisResult" /> code.</returns>
    [LibraryImport(VorbisLibrary.LibraryName, EntryPoint = "codebrix_vorbis_encoder_read_output")]
    internal static partial int EncoderReadOutput(IntPtr encoder, byte* destination, int capacity);

    /// <summary>The granule position of the last completed page.</summary>
    /// <param name="encoder">The encoder.</param>
    /// <returns>The granule position, or -1 before any audio page exists.</returns>
    [LibraryImport(VorbisLibrary.LibraryName, EntryPoint = "codebrix_vorbis_encoder_granule_position")]
    internal static partial long EncoderGranulePosition(IntPtr encoder);

    /// <summary>Releases an encoder. A null handle is ignored.</summary>
    /// <param name="encoder">The encoder.</param>
    [LibraryImport(VorbisLibrary.LibraryName, EntryPoint = "codebrix_vorbis_encoder_destroy")]
    internal static partial void EncoderDestroy(IntPtr encoder);
}
