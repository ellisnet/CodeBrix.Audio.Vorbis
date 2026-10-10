namespace CodeBrix.Audio.Vorbis.Interop;

/// <summary>
/// The result codes and mode values of the native contract (<c>CBV_*</c> in <c>codebrix_vorbis.h</c>), and
/// the text the managed side reports for each.
/// </summary>
internal static class VorbisResult
{
    /// <summary><c>CBV_OK</c>.</summary>
    public const int Ok = 0;

    /// <summary><c>CBV_E_INVALID_ARGUMENT</c>.</summary>
    public const int InvalidArgument = -1;

    /// <summary><c>CBV_E_OUT_OF_MEMORY</c>.</summary>
    public const int OutOfMemory = -2;

    /// <summary><c>CBV_E_INVALID_STATE</c>.</summary>
    public const int InvalidState = -3;

    /// <summary><c>CBV_E_VORBIS_UNSUPPORTED</c>.</summary>
    public const int VorbisUnsupported = -4;

    /// <summary><c>CBV_E_VORBIS_INVALID</c>.</summary>
    public const int VorbisInvalid = -5;

    /// <summary><c>CBV_E_VORBIS_FAULT</c>.</summary>
    public const int VorbisFault = -6;

    /// <summary><c>CBV_MODE_QUALITY</c>.</summary>
    public const int ModeQuality = 0;

    /// <summary><c>CBV_MODE_BITRATE</c>.</summary>
    public const int ModeBitrate = 1;

    /// <summary>Describes a result code in words.</summary>
    /// <param name="code">The code.</param>
    /// <returns>A short description.</returns>
    public static string Describe(int code) =>
        code switch
        {
            Ok => "success",
            InvalidArgument => "an argument was out of range (CBV_E_INVALID_ARGUMENT)",
            OutOfMemory => "the native encoder ran out of memory (CBV_E_OUT_OF_MEMORY)",
            InvalidState => "the call is not valid in the encoder's current state (CBV_E_INVALID_STATE)",
            VorbisUnsupported =>
                "libvorbis does not support this combination of mode, sample rate, channels and bitrate "
                + "(CBV_E_VORBIS_UNSUPPORTED)",
            VorbisInvalid => "libvorbis rejected the request as invalid (CBV_E_VORBIS_INVALID)",
            VorbisFault => "libvorbis or libogg failed (CBV_E_VORBIS_FAULT)",
            _ => $"unknown native result code {code}",
        };

    /// <summary>Throws a <see cref="VorbisEncoderException" /> when <paramref name="code" /> is not OK.</summary>
    /// <param name="code">The code a native call returned.</param>
    /// <param name="operation">What was being done, for the message.</param>
    public static void ThrowIfFailed(int code, string operation)
    {
        if (code >= Ok) return;
        throw new VorbisEncoderException($"Vorbis encoding failed while {operation}: {Describe(code)}.", code);
    }
}
