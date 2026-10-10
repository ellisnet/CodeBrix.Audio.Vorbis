using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Vorbis.Tests;

/// <summary>
/// Generates the test signals (no recordings - every tone is synthesized here) and decodes encoded output
/// back with CodeBrix.Audio's managed Vorbis decoder.
/// </summary>
internal static class TestAudio
{
    /// <summary>One decoded Ogg Vorbis stream.</summary>
    internal sealed class Decoded
    {
        /// <summary>The channel count from the identification header.</summary>
        public int Channels { get; init; }

        /// <summary>The sample rate from the identification header.</summary>
        public int SampleRate { get; init; }

        /// <summary>The total frame count the stream reports (its final granule position).</summary>
        public long TotalFrames { get; init; }

        /// <summary>The nominal bitrate from the identification header (read straight from the first page).</summary>
        public int NominalBitrate { get; init; }

        /// <summary>The vendor string from the comment header.</summary>
        public string Vendor { get; init; }

        /// <summary>Every comment, as TAG=value strings.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Tags { get; init; }

        /// <summary>The decoded interleaved samples.</summary>
        public float[] Samples { get; init; }
    }

    /// <summary>A sine tone, the same on every channel.</summary>
    /// <param name="frequency">Frequency in hertz.</param>
    /// <param name="amplitude">Peak amplitude.</param>
    /// <param name="sampleRate">Sample rate.</param>
    /// <param name="channels">Channels.</param>
    /// <param name="frames">Frames.</param>
    /// <returns>Interleaved samples.</returns>
    public static float[] Sine(double frequency, float amplitude, int sampleRate, int channels, int frames)
    {
        float[] samples = new float[frames * channels];
        for (int i = 0; i < frames; i++)
        {
            float value = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * i / sampleRate));
            for (int c = 0; c < channels; c++) samples[(i * channels) + c] = value;
        }

        return samples;
    }

    /// <summary>Seeded white noise, the same on every channel.</summary>
    /// <param name="amplitude">Peak amplitude.</param>
    /// <param name="channels">Channels.</param>
    /// <param name="frames">Frames.</param>
    /// <param name="seed">The random seed, so every run encodes the same signal.</param>
    /// <returns>Interleaved samples.</returns>
    public static float[] Noise(float amplitude, int channels, int frames, int seed)
    {
        Random random = new Random(seed);
        float[] samples = new float[frames * channels];
        for (int i = 0; i < frames; i++)
        {
            float value = (float)(amplitude * ((random.NextDouble() * 2) - 1));
            for (int c = 0; c < channels; c++) samples[(i * channels) + c] = value;
        }

        return samples;
    }

    /// <summary>Decodes an Ogg Vorbis stream completely.</summary>
    /// <param name="ogg">The encoded bytes.</param>
    /// <returns>What was decoded.</returns>
    public static Decoded Decode(byte[] ogg)
    {
        using OggVorbisFileReader reader = new OggVorbisFileReader(new MemoryStream(ogg));

        int channels = reader.WaveFormat.Channels;
        byte[] bytes = new byte[4096 * channels * sizeof(float)];
        List<float> all = new List<float>();
        int read;
        while ((read = reader.Read(bytes, 0, bytes.Length)) > 0)
        {
            ReadOnlySpan<float> floats = MemoryMarshal.Cast<byte, float>(bytes.AsSpan(0, read));
            foreach (float f in floats) all.Add(f);
        }

        return new Decoded
        {
            Channels = channels,
            SampleRate = reader.WaveFormat.SampleRate,
            TotalFrames = reader.Length / reader.WaveFormat.BlockAlign,
            NominalBitrate = OggPages.NominalBitrate(ogg),
            Vendor = reader.EncoderVendor,
            Tags = reader.Tags,
            Samples = all.ToArray(),
        };
    }

    /// <summary>The RMS of one channel, skipping the first and last <paramref name="edgeFrames" /> frames.</summary>
    /// <param name="samples">Interleaved samples.</param>
    /// <param name="channels">Channels.</param>
    /// <param name="channel">The channel to measure.</param>
    /// <param name="edgeFrames">Frames to ignore at each end.</param>
    /// <returns>The RMS.</returns>
    public static double Rms(float[] samples, int channels, int channel, int edgeFrames)
    {
        int frames = samples.Length / channels;
        double sum = 0;
        int count = 0;
        for (int i = edgeFrames; i < frames - edgeFrames; i++)
        {
            double v = samples[(i * channels) + channel];
            sum += v * v;
            count++;
        }

        return count == 0 ? 0 : Math.Sqrt(sum / count);
    }

    /// <summary>The largest absolute sample on any channel.</summary>
    /// <param name="samples">Interleaved samples.</param>
    /// <returns>The peak.</returns>
    public static double Peak(float[] samples)
    {
        double peak = 0;
        foreach (float s in samples) peak = Math.Max(peak, Math.Abs(s));
        return peak;
    }

    /// <summary>Counts upward zero crossings on one channel - a frequency estimate that needs no FFT.</summary>
    /// <param name="samples">Interleaved samples.</param>
    /// <param name="channels">Channels.</param>
    /// <param name="channel">The channel.</param>
    /// <returns>The crossing count.</returns>
    public static int UpwardZeroCrossings(float[] samples, int channels, int channel)
    {
        int frames = samples.Length / channels;
        int crossings = 0;
        for (int i = 1; i < frames; i++)
        {
            if (samples[((i - 1) * channels) + channel] < 0 && samples[(i * channels) + channel] >= 0) crossings++;
        }

        return crossings;
    }

    /// <summary>Encodes a buffer in one call with the given settings.</summary>
    /// <param name="samples">Interleaved samples.</param>
    /// <param name="channels">Channels.</param>
    /// <param name="sampleRate">Sample rate.</param>
    /// <param name="options">Options, or null.</param>
    /// <returns>The encoded bytes.</returns>
    public static byte[] Encode(float[] samples, int channels, int sampleRate, VorbisEncoderOptions options)
    {
        using MemoryStream output = new MemoryStream();
        using (OggVorbisEncoder encoder = new OggVorbisEncoder(output, channels, sampleRate, options))
        {
            encoder.Encode(samples);
        }

        return output.ToArray();
    }
}
