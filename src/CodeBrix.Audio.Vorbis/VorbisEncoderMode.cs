namespace CodeBrix.Audio.Vorbis;

/// <summary>How an <see cref="OggVorbisEncoder" /> decides how many bits to spend.</summary>
public enum VorbisEncoderMode
{
    /// <summary>
    /// Variable bitrate chosen by a quality target (<see cref="VorbisEncoderOptions.Quality" />). The default,
    /// and the mode that gives the best sound for the size.
    /// </summary>
    Quality = 0,

    /// <summary>
    /// Managed bitrate around <see cref="VorbisEncoderOptions.NominalBitrate" />, optionally held between
    /// <see cref="VorbisEncoderOptions.MinBitrate" /> and <see cref="VorbisEncoderOptions.MaxBitrate" />.
    /// </summary>
    Bitrate = 1,
}
