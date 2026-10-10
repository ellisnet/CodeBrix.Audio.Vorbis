using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Vorbis.Internal;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Vorbis;

/// <summary>
/// Writes <c>.ogg</c> (Ogg Vorbis) files through CodeBrix.Audio's <see cref="AudioFileWriterRegistry" />.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CodeBrixAudioVorbis.Register()" /> registers one of these, so an application making that call
/// can write .ogg by file name with no further change - including through CodeBrix.Audio's
/// <c>SoundFontRenderer.RenderToFile</c>, which picks its writer from the path's extension.
/// </para>
/// <para>
/// OGG IS WRITTEN STRICTLY FORWARDS, so <see cref="RequiresSeekableStream" /> is false and a .ogg file can be
/// written to a pipe or a network stream.
/// </para>
/// <para>
/// ONLY THE SAMPLE RATE AND CHANNEL COUNT OF A <see cref="WaveFormat" /> ARE USED. A Vorbis stream stores a
/// compressed payload, so the format's encoding and bit depth mean nothing here and are ignored rather than
/// refused. Encoder settings - quality or bitrate, comments - belong to the factory: register a factory built
/// with <see cref="VorbisEncoderOptions" /> of your own, after <see cref="CodeBrixAudioVorbis.Register()" />,
/// to write .ogg at other settings:
/// </para>
/// <code>
/// AudioFileWriterRegistry.Register(new VorbisAudioFileWriterFactory(new VorbisEncoderOptions { Quality = 0.7f }));
/// </code>
/// </remarks>
public sealed class VorbisAudioFileWriterFactory : IAudioFileWriterFactory
{
    private static readonly string[] OggExtensions = [".ogg"];

    private readonly VorbisEncoderOptions options;

    /// <summary>Creates the factory with the encoder defaults - quality mode at 0.4, no comments.</summary>
    public VorbisAudioFileWriterFactory() : this(null)
    {
    }

    /// <summary>Creates the factory with encoder settings of your own.</summary>
    /// <param name="options">The settings every writer this factory makes is built with, or null for the defaults.</param>
    /// <exception cref="ArgumentOutOfRangeException">An option is outside its permitted range.</exception>
    /// <exception cref="ArgumentException">A comment is not usable.</exception>
    public VorbisAudioFileWriterFactory(VorbisEncoderOptions options)
    {
        this.options = options ?? new VorbisEncoderOptions();
        this.options.Validate();
    }

    /// <summary>The encoder settings every writer this factory makes is built with.</summary>
    public VorbisEncoderOptions Options => options;

    /// <inheritdoc />
    public IReadOnlyList<string> Extensions => OggExtensions;

    /// <inheritdoc />
    /// <remarks>False: an Ogg stream is written from beginning to end and nothing in it is revisited.</remarks>
    public bool RequiresSeekableStream => false;

    /// <inheritdoc />
    /// <remarks>32-bit IEEE float at the requested rate and channel count - what the encoder takes.</remarks>
    public WaveFormat DefaultFormat(int sampleRate, int channels)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "The sample rate must be positive.");
        }

        if (channels < 1 || channels > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "A Vorbis stream holds 1 to 255 channels.");
        }

        return WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Only <see cref="WaveFormat.SampleRate" /> and <see cref="WaveFormat.Channels" /> are read from
    /// <paramref name="format" />. The stream is NOT closed by the writer and does not need to be seekable.
    /// </remarks>
    public IAudioFileWriter Create(Stream stream, WaveFormat format)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (format == null) throw new ArgumentNullException(nameof(format));

        if (!stream.CanWrite)
        {
            throw new ArgumentException("A .ogg file cannot be written to a stream that is not writable.", nameof(stream));
        }

        if (format.SampleRate <= 0)
        {
            throw new ArgumentException(
                $"The sample rate must be positive, and this format asks for {format.SampleRate}.", nameof(format));
        }

        if (format.Channels < 1 || format.Channels > 255)
        {
            throw new ArgumentException(
                $"A Vorbis stream holds 1 to 255 channels, and this format asks for {format.Channels}.", nameof(format));
        }

        return new VorbisStreamAudioFileWriter(stream, format, options);
    }
}
