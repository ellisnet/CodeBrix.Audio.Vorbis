using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CodeBrix.Audio.Vorbis.Interop;

namespace CodeBrix.Audio.Vorbis;

/// <summary>
/// Encodes 32-bit float samples into an Ogg Vorbis stream written to a <see cref="Stream" />.
/// </summary>
/// <remarks>
/// <para>
/// The three Vorbis header pages are written as soon as the encoder is created. Each
/// <see cref="Encode(ReadOnlySpan{float})" /> call hands interleaved samples to the native encoder and writes
/// whatever Ogg pages that completes; <see cref="Flush" /> (or <see cref="Dispose" />) ends the stream - it
/// writes the final pages, the last one flagged end-of-stream - and the result is a standard <c>.ogg</c> file.
/// </para>
/// <para>
/// THE STREAM IS NOT OWNED. The caller opened it and the caller closes it, after disposing the encoder. It is
/// written strictly forwards, so it does not need to be seekable: a pipe or a network stream works.
/// </para>
/// <para>
/// An instance is not thread-safe; use it from one thread at a time. Separate instances are independent.
/// </para>
/// </remarks>
public sealed class OggVorbisEncoder : IDisposable
{
    // Frames handed to the native encoder per call. Large inputs are split so the native output buffer and
    // the copy buffer below stay small whatever the caller passes.
    private const int MaxFramesPerNativeCall = 4096;

    private readonly Stream output;
    private readonly byte[] copyBuffer = new byte[16 * 1024];

    private VorbisEncoderHandle encoder;
    private long framesWritten;
    private bool finished;

    /// <summary>Creates an encoder and writes the Ogg Vorbis header pages to <paramref name="output" />.</summary>
    /// <param name="output">The stream to write to. It must be writable; it is not owned by the encoder.</param>
    /// <param name="channels">The number of interleaved channels, 1 to 255.</param>
    /// <param name="sampleRate">The sample rate in hertz, for example 44100 or 48000.</param>
    /// <param name="options">The encoder settings, or null for the defaults (quality 0.4, no comments).</param>
    /// <exception cref="ArgumentNullException"><paramref name="output" /> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="output" /> is not writable, or a comment is not usable.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="channels" />, <paramref name="sampleRate" /> or an option is out of range.
    /// </exception>
    /// <exception cref="VorbisEncoderException">
    /// The native library could not be loaded, or libvorbis refused the combination of settings - for example
    /// a bitrate it has no mode for at this sample rate and channel count.
    /// </exception>
    public OggVorbisEncoder(Stream output, int channels, int sampleRate, VorbisEncoderOptions options = null)
    {
        if (output == null) throw new ArgumentNullException(nameof(output));
        if (!output.CanWrite)
        {
            throw new ArgumentException("An Ogg Vorbis stream cannot be written to a stream that is not writable.", nameof(output));
        }

        if (channels < 1 || channels > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "A Vorbis stream holds 1 to 255 channels.");
        }

        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "The sample rate must be positive.");
        }

        options ??= new VorbisEncoderOptions();
        options.Validate();

        VorbisLibrary.EnsureLoaded();

        this.output = output;
        Channels = channels;
        SampleRate = sampleRate;
        Mode = options.Mode;

        int serialNumber = options.SerialNumber ?? Random.Shared.Next(int.MinValue, int.MaxValue);

        IntPtr created = CreateNative(options, channels, sampleRate, serialNumber);
        encoder = new VorbisEncoderHandle(created);
        SerialNumber = serialNumber;

        try
        {
            if (options.Comments != null)
            {
                foreach (KeyValuePair<string, string> comment in options.Comments)
                {
                    AddComment(comment.Key, comment.Value);
                }
            }

            VorbisResult.ThrowIfFailed(VorbisNative.EncoderWriteHeaders(encoder.DangerousGetHandle()), "writing the header pages");
            Drain();
        }
        catch
        {
            encoder.Dispose();
            encoder = null;
            throw;
        }
    }

    /// <summary>The number of interleaved channels the encoder was created with.</summary>
    public int Channels { get; }

    /// <summary>The sample rate, in hertz, the encoder was created with.</summary>
    public int SampleRate { get; }

    /// <summary>The bitrate mode the encoder was created with.</summary>
    public VorbisEncoderMode Mode { get; }

    /// <summary>The Ogg logical-stream serial number written into every page.</summary>
    public int SerialNumber { get; }

    /// <summary>The number of frames (one sample per channel) accepted by <see cref="Encode(ReadOnlySpan{float})" /> so far.</summary>
    public long FramesWritten => framesWritten;

    /// <summary>The duration of the audio accepted so far: <see cref="FramesWritten" /> at <see cref="SampleRate" />.</summary>
    public TimeSpan Duration => TimeSpan.FromSeconds((double)framesWritten / SampleRate);

    /// <summary>True once <see cref="Flush" /> or <see cref="Dispose" /> has ended the stream.</summary>
    public bool IsFinished => finished;

    /// <summary>True once <see cref="Dispose" /> has run.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Encodes interleaved samples (channel 0, channel 1, ... for frame 0, then frame 1, ...) in [-1, 1] and
    /// writes every Ogg page that completes.
    /// </summary>
    /// <param name="interleavedSamples">The samples. Values beyond [-1, 1] are clipped by the encoder.</param>
    /// <returns>
    /// The number of whole frames consumed: the sample count divided by <see cref="Channels" />. A trailing
    /// partial frame is not consumed; pass it again at the start of the next call.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The encoder has been disposed.</exception>
    /// <exception cref="InvalidOperationException"><see cref="Flush" /> has already ended the stream.</exception>
    /// <exception cref="VorbisEncoderException">The native encoder failed.</exception>
    public unsafe int Encode(ReadOnlySpan<float> interleavedSamples)
    {
        CheckWritable();

        int frames = interleavedSamples.Length / Channels;
        if (frames == 0) return 0;

        fixed (float* start = interleavedSamples)
        {
            int done = 0;
            while (done < frames)
            {
                int chunk = Math.Min(MaxFramesPerNativeCall, frames - done);
                int code = VorbisNative.EncoderWriteFloat(encoder.DangerousGetHandle(), start + ((long)done * Channels), chunk);
                VorbisResult.ThrowIfFailed(code, "encoding samples");

                done += chunk;
                framesWritten += chunk;
                Drain();
            }
        }

        return frames;
    }

    /// <summary>
    /// Ends the stream: flushes the encoder, writes the final Ogg pages (the last flagged end-of-stream) and
    /// flushes the output stream. Nothing can be encoded afterwards. Calling it again does nothing.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The encoder has been disposed.</exception>
    /// <exception cref="VorbisEncoderException">The native encoder failed.</exception>
    public void Flush()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(OggVorbisEncoder));
        if (finished) return;

        finished = true;
        VorbisResult.ThrowIfFailed(VorbisNative.EncoderFinish(encoder.DangerousGetHandle()), "ending the stream");
        Drain();
        output.Flush();
    }

    /// <summary>
    /// Ends the stream if <see cref="Flush" /> has not already, then releases the native encoder. The output
    /// stream is left open. Calling it more than once does nothing.
    /// </summary>
    public void Dispose()
    {
        if (IsDisposed) return;

        try
        {
            if (!finished && encoder != null) Flush();
        }
        finally
        {
            IsDisposed = true;
            encoder?.Dispose();
            encoder = null;
        }
    }

    /// <summary>The granule position (total frames) of the last completed page, or -1 before any audio page.</summary>
    /// <returns>The granule position.</returns>
    internal long NativeGranulePosition()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(OggVorbisEncoder));
        return VorbisNative.EncoderGranulePosition(encoder.DangerousGetHandle());
    }

    private static unsafe IntPtr CreateNative(VorbisEncoderOptions options, int channels, int sampleRate, int serialNumber)
    {
        bool bitrate = options.Mode == VorbisEncoderMode.Bitrate;
        IntPtr created = IntPtr.Zero;

        int code = VorbisNative.EncoderCreate(
            channels,
            sampleRate,
            bitrate ? VorbisResult.ModeBitrate : VorbisResult.ModeQuality,
            options.Quality,
            bitrate ? options.NominalBitrate : 0,
            bitrate && options.MinBitrate.HasValue ? options.MinBitrate.Value : -1,
            bitrate && options.MaxBitrate.HasValue ? options.MaxBitrate.Value : -1,
            serialNumber,
            &created);

        if (code != VorbisResult.Ok)
        {
            if (created != IntPtr.Zero) VorbisNative.EncoderDestroy(created);

            string settings = bitrate
                ? $"bitrate mode, nominal {options.NominalBitrate} bit/s"
                : $"quality mode, quality {options.Quality}";
            throw new VorbisEncoderException(
                $"The Vorbis encoder could not be created for {channels} channel(s) at {sampleRate} Hz ({settings}): "
                + $"{VorbisResult.Describe(code)}.",
                code);
        }

        if (created == IntPtr.Zero)
        {
            throw new VorbisEncoderException(
                "The Vorbis encoder reported success but returned no encoder.", VorbisResult.VorbisFault);
        }

        return created;
    }

    private unsafe void AddComment(string tag, string value)
    {
        byte[] tagBytes = NulTerminatedUtf8(tag);
        byte[] valueBytes = NulTerminatedUtf8(value);

        fixed (byte* t = tagBytes)
        fixed (byte* v = valueBytes)
        {
            VorbisResult.ThrowIfFailed(
                VorbisNative.EncoderAddComment(encoder.DangerousGetHandle(), t, v), $"adding the comment '{tag}'");
        }
    }

    private static byte[] NulTerminatedUtf8(string text)
    {
        int length = Encoding.UTF8.GetByteCount(text);
        byte[] bytes = new byte[length + 1];
        Encoding.UTF8.GetBytes(text, 0, text.Length, bytes, 0);
        return bytes;
    }

    private unsafe void Drain()
    {
        IntPtr native = encoder.DangerousGetHandle();

        fixed (byte* destination = copyBuffer)
        {
            while (true)
            {
                int copied = VorbisNative.EncoderReadOutput(native, destination, copyBuffer.Length);
                VorbisResult.ThrowIfFailed(copied, "reading encoded pages");
                if (copied == 0) return;

                output.Write(copyBuffer, 0, copied);
            }
        }
    }

    private void CheckWritable()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(OggVorbisEncoder));
        if (finished)
        {
            throw new InvalidOperationException("The stream has been ended by Flush; nothing more can be encoded.");
        }
    }
}
