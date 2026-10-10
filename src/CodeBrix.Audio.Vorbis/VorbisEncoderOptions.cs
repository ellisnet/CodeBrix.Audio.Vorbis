using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.Vorbis;

/// <summary>Settings for an <see cref="OggVorbisEncoder" />.</summary>
/// <remarks>
/// A null options argument means a fresh instance of this class: quality mode at 0.4 (roughly 128 kbit/s for
/// stereo music), no comments and a random serial number. The settings are read once, when the encoder is
/// created; changing them afterwards has no effect on that encoder.
/// </remarks>
public sealed class VorbisEncoderOptions
{
    /// <summary>The lowest <see cref="Quality" /> libvorbis accepts.</summary>
    public const float MinimumQuality = -0.1f;

    /// <summary>The highest <see cref="Quality" /> libvorbis accepts.</summary>
    public const float MaximumQuality = 1.0f;

    /// <summary>The default <see cref="Quality" />.</summary>
    public const float DefaultQuality = 0.4f;

    /// <summary>The default <see cref="NominalBitrate" />, in bits per second.</summary>
    public const int DefaultNominalBitrate = 128_000;

    /// <summary>Whether to encode by quality (the default) or around a bitrate.</summary>
    public VorbisEncoderMode Mode { get; set; } = VorbisEncoderMode.Quality;

    /// <summary>
    /// The quality target for <see cref="VorbisEncoderMode.Quality" />, from -0.1 (smallest) to 1.0 (best);
    /// default 0.4. Ignored in <see cref="VorbisEncoderMode.Bitrate" /> mode.
    /// </summary>
    public float Quality { get; set; } = DefaultQuality;

    /// <summary>
    /// The average bitrate, in bits per second, for <see cref="VorbisEncoderMode.Bitrate" />; default 128000.
    /// Ignored in <see cref="VorbisEncoderMode.Quality" /> mode.
    /// </summary>
    public int NominalBitrate { get; set; } = DefaultNominalBitrate;

    /// <summary>
    /// The lowest bitrate, in bits per second, <see cref="VorbisEncoderMode.Bitrate" /> mode may drop to, or
    /// null for no floor. Ignored in <see cref="VorbisEncoderMode.Quality" /> mode.
    /// </summary>
    public int? MinBitrate { get; set; }

    /// <summary>
    /// The highest bitrate, in bits per second, <see cref="VorbisEncoderMode.Bitrate" /> mode may rise to, or
    /// null for no ceiling. Ignored in <see cref="VorbisEncoderMode.Quality" /> mode.
    /// </summary>
    public int? MaxBitrate { get; set; }

    /// <summary>
    /// Vorbis comments (tags) written into the stream's comment header - for example <c>TITLE</c>,
    /// <c>ARTIST</c>, <c>ALBUM</c>. Names are ASCII 0x20 to 0x7D without <c>=</c>; values are any text without NUL
    /// characters and are written as UTF-8. The encoder's vendor string is written by the encoder itself.
    /// </summary>
    public IDictionary<string, string> Comments { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The Ogg logical-stream serial number, or null (the default) for a random one.</summary>
    public int? SerialNumber { get; set; }

    /// <summary>Checks every setting, so a bad one is reported where it was made.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A setting is outside its permitted range.</exception>
    /// <exception cref="ArgumentException">A comment name or value is not usable.</exception>
    internal void Validate()
    {
        if (Mode != VorbisEncoderMode.Quality && Mode != VorbisEncoderMode.Bitrate)
        {
            throw new ArgumentOutOfRangeException(nameof(Mode), Mode, "The mode must be Quality or Bitrate.");
        }

        if (Mode == VorbisEncoderMode.Quality)
        {
            if (float.IsNaN(Quality) || Quality < MinimumQuality || Quality > MaximumQuality)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(Quality), Quality, "The quality must be between -0.1 and 1.0.");
            }
        }
        else
        {
            if (NominalBitrate <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(NominalBitrate), NominalBitrate, "The nominal bitrate must be positive.");
            }

            if (MinBitrate.HasValue && (MinBitrate.Value <= 0 || MinBitrate.Value > NominalBitrate))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MinBitrate), MinBitrate, "The minimum bitrate must be positive and no more than the nominal bitrate.");
            }

            if (MaxBitrate.HasValue && (MaxBitrate.Value <= 0 || MaxBitrate.Value < NominalBitrate))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxBitrate), MaxBitrate, "The maximum bitrate must be positive and no less than the nominal bitrate.");
            }
        }

        if (Comments == null) return;

        foreach (KeyValuePair<string, string> comment in Comments)
        {
            if (string.IsNullOrEmpty(comment.Key))
            {
                throw new ArgumentException("A comment name must not be empty.", nameof(Comments));
            }

            foreach (char c in comment.Key)
            {
                if (c < 0x20 || c > 0x7D || c == '=')
                {
                    throw new ArgumentException(
                        $"The comment name '{comment.Key}' is not valid: names are ASCII 0x20 to 0x7D without '='.",
                        nameof(Comments));
                }
            }

            if (comment.Value == null)
            {
                throw new ArgumentException($"The comment '{comment.Key}' has no value.", nameof(Comments));
            }

            if (comment.Value.IndexOf('\0') >= 0)
            {
                throw new ArgumentException(
                    $"The value of comment '{comment.Key}' contains a NUL character, which a Vorbis comment cannot carry.",
                    nameof(Comments));
            }
        }
    }
}
