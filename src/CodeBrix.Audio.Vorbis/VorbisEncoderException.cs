using System;

namespace CodeBrix.Audio.Vorbis;

/// <summary>
/// Thrown when the native Vorbis encoder cannot be loaded or reports a failure.
/// </summary>
/// <remarks>
/// <see cref="NativeCode" /> carries the native result code that caused the failure:
/// -1 an argument was out of range; -2 out of memory; -3 the call is not valid in the encoder's current state;
/// -4 libvorbis does not support the requested combination of mode, sample rate, channel count and bitrate;
/// -5 libvorbis rejected the request as invalid; -6 any other libvorbis / libogg failure. It is 0 when the
/// failure did not come from a native call - for example when the native library could not be loaded at all.
/// </remarks>
public class VorbisEncoderException : Exception
{
    /// <summary>Creates the exception with a message and no native code.</summary>
    /// <param name="message">What went wrong.</param>
    public VorbisEncoderException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the native result code that caused it.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="nativeCode">The native result code (a negative <c>CBV_E_*</c> value).</param>
    public VorbisEncoderException(string message, int nativeCode) : base(message)
    {
        NativeCode = nativeCode;
    }

    /// <summary>Creates the exception with a message and the exception that caused it.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The underlying failure.</param>
    public VorbisEncoderException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// The native result code that caused the failure (a negative <c>CBV_E_*</c> value), or 0 when the failure
    /// did not come from a native call.
    /// </summary>
    public int NativeCode { get; }
}
