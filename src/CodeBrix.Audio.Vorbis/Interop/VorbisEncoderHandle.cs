using System;
using System.Runtime.InteropServices;

namespace CodeBrix.Audio.Vorbis.Interop;

/// <summary>
/// Owns one native <c>codebrix_vorbis_encoder</c>, so an encoder that is never disposed still releases its
/// native memory when it is collected.
/// </summary>
internal sealed class VorbisEncoderHandle : SafeHandle
{
    /// <summary>Wraps a handle returned by <c>codebrix_vorbis_encoder_create</c>.</summary>
    /// <param name="existing">The native handle.</param>
    public VorbisEncoderHandle(IntPtr existing) : base(IntPtr.Zero, ownsHandle: true)
    {
        SetHandle(existing);
    }

    /// <inheritdoc />
    public override bool IsInvalid => handle == IntPtr.Zero;

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        VorbisNative.EncoderDestroy(handle);
        return true;
    }
}
