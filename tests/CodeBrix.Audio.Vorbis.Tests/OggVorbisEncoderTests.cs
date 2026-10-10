using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Vorbis.Tests;

public class OggVorbisEncoderTests
{
    // libvorbis's longest block at 44.1/48 kHz is 2048 frames; durations are asserted to within one of them.
    private const int OneBlock = 2048;

    [Theory]
    [InlineData(1, 44100)]
    [InlineData(2, 44100)]
    [InlineData(1, 48000)]
    [InlineData(2, 48000)]
    public void Encode_round_trips_a_sine_in_quality_mode(int channels, int sampleRate)
    {
        //Arrange
        int frames = sampleRate * 2;
        float[] tone = TestAudio.Sine(440, 0.5f, sampleRate, channels, frames);

        //Act
        byte[] ogg = TestAudio.Encode(tone, channels, sampleRate, null);
        TestAudio.Decoded decoded = TestAudio.Decode(ogg);

        //Assert
        decoded.Channels.Should().Be(channels);
        decoded.SampleRate.Should().Be(sampleRate);
        Math.Abs(decoded.TotalFrames - frames).Should().BeLessThanOrEqualTo(OneBlock);
        Math.Abs((decoded.Samples.Length / channels) - frames).Should().BeLessThanOrEqualTo(OneBlock);
        for (int c = 0; c < channels; c++)
        {
            TestAudio.Rms(decoded.Samples, channels, c, OneBlock).Should().BeApproximately(0.5 / Math.Sqrt(2), 0.02);
        }

        TestAudio.Peak(decoded.Samples).Should().BeInRange(0.45, 0.6);
    }

    [Fact]
    public void Encode_preserves_the_tone_frequency()
    {
        //Arrange
        float[] tone = TestAudio.Sine(440, 0.5f, 48000, 1, 48000);

        //Act
        TestAudio.Decoded decoded = TestAudio.Decode(TestAudio.Encode(tone, 1, 48000, null));

        //Assert
        TestAudio.UpwardZeroCrossings(decoded.Samples, 1, 0).Should().BeInRange(436, 444);
    }

    [Fact]
    public void Encode_reports_the_exact_frame_count_in_the_final_granule()
    {
        //Arrange
        const int frames = 44100 + 123;
        float[] tone = TestAudio.Sine(440, 0.5f, 44100, 2, frames);

        //Act
        byte[] ogg = TestAudio.Encode(tone, 2, 44100, null);
        List<OggPages.Page> pages = OggPages.Parse(ogg);

        //Assert
        pages[^1].GranulePosition.Should().Be(frames);
    }

    [Fact]
    public void Encode_round_trips_in_bitrate_mode()
    {
        //Arrange
        float[] tone = TestAudio.Sine(440, 0.5f, 44100, 2, 44100 * 3);
        VorbisEncoderOptions options = new VorbisEncoderOptions { Mode = VorbisEncoderMode.Bitrate, NominalBitrate = 128_000 };

        //Act
        byte[] ogg = TestAudio.Encode(tone, 2, 44100, options);
        TestAudio.Decoded decoded = TestAudio.Decode(ogg);

        //Assert
        decoded.NominalBitrate.Should().Be(128_000);
        decoded.Channels.Should().Be(2);
        TestAudio.Rms(decoded.Samples, 2, 0, OneBlock).Should().BeApproximately(0.5 / Math.Sqrt(2), 0.02);
        ogg.Length.Should().BeLessThan(3 * 128_000 / 8 * 2);
    }

    [Fact]
    public void Encode_honours_min_and_max_bitrate()
    {
        //Arrange
        float[] tone = TestAudio.Sine(440, 0.5f, 44100, 2, 44100);
        VorbisEncoderOptions options = new VorbisEncoderOptions
        {
            Mode = VorbisEncoderMode.Bitrate,
            NominalBitrate = 128_000,
            MinBitrate = 96_000,
            MaxBitrate = 160_000,
        };

        //Act
        TestAudio.Decoded decoded = TestAudio.Decode(TestAudio.Encode(tone, 2, 44100, options));

        //Assert
        decoded.Channels.Should().Be(2);
        decoded.Samples.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Encode_higher_quality_makes_a_larger_file()
    {
        //Arrange
        float[] noiseish = TestAudio.Sine(440, 0.3f, 44100, 2, 44100 * 2);
        float[] second = TestAudio.Sine(3517, 0.3f, 44100, 2, 44100 * 2);
        for (int i = 0; i < noiseish.Length; i++) noiseish[i] += second[i];

        //Act
        byte[] low = TestAudio.Encode(noiseish, 2, 44100, new VorbisEncoderOptions { Quality = -0.1f });
        byte[] high = TestAudio.Encode(noiseish, 2, 44100, new VorbisEncoderOptions { Quality = 1.0f });

        //Assert
        high.Length.Should().BeGreaterThan(low.Length);
    }

    [Fact]
    public void Encode_round_trips_comments()
    {
        //Arrange
        float[] tone = TestAudio.Sine(440, 0.5f, 44100, 1, 44100);
        VorbisEncoderOptions options = new VorbisEncoderOptions();
        options.Comments["TITLE"] = "Round-trip test tone";
        options.Comments["ARTIST"] = "Café Ünïcode ✓";

        //Act
        TestAudio.Decoded decoded = TestAudio.Decode(TestAudio.Encode(tone, 1, 44100, options));

        //Assert
        decoded.Tags["TITLE"].Should().ContainSingle().Which.Should().Be("Round-trip test tone");
        decoded.Tags["ARTIST"].Should().ContainSingle().Which.Should().Be("Café Ünïcode ✓");
        decoded.Vendor.Should().Contain("Xiph.Org");
    }

    [Fact]
    public void Encode_writes_well_formed_ogg_pages_with_bos_and_eos()
    {
        //Arrange
        float[] tone = TestAudio.Sine(440, 0.5f, 48000, 2, 48000);

        //Act
        byte[] ogg = TestAudio.Encode(tone, 2, 48000, new VorbisEncoderOptions { SerialNumber = 0x1234567 });
        List<OggPages.Page> pages = OggPages.Parse(ogg);

        //Assert
        ogg.AsSpan(0, 4).SequenceEqual("OggS"u8).Should().BeTrue();
        pages.Count.Should().BeGreaterThanOrEqualTo(3);
        pages[0].IsBeginningOfStream.Should().BeTrue();
        pages.Skip(1).Any(p => p.IsBeginningOfStream).Should().BeFalse();
        pages[^1].IsEndOfStream.Should().BeTrue();
        pages.Take(pages.Count - 1).Any(p => p.IsEndOfStream).Should().BeFalse();
        pages.All(p => p.SerialNumber == 0x1234567).Should().BeTrue();
        pages.Select(p => p.SequenceNumber).Should().ContainInOrder(Enumerable.Range(0, pages.Count));
    }

    [Fact]
    public void Encode_returns_whole_frames_consumed()
    {
        //Arrange
        using MemoryStream output = new MemoryStream();
        using OggVorbisEncoder encoder = new OggVorbisEncoder(output, 2, 44100);

        //Act
        int consumed = encoder.Encode(new float[1001]);

        //Assert
        consumed.Should().Be(500);
        encoder.FramesWritten.Should().Be(500);
    }

    [Fact]
    public void Encode_accepts_input_far_larger_than_one_native_call()
    {
        //Arrange
        float[] tone = TestAudio.Sine(440, 0.5f, 44100, 2, 44100 * 5);
        using MemoryStream output = new MemoryStream();

        //Act
        using (OggVorbisEncoder encoder = new OggVorbisEncoder(output, 2, 44100))
        {
            encoder.Encode(tone).Should().Be(44100 * 5);
        }

        //Assert
        TestAudio.Decode(output.ToArray()).TotalFrames.Should().Be(44100 * 5);
    }

    [Fact]
    public void Encode_of_many_small_calls_matches_the_frame_count()
    {
        //Arrange
        float[] tone = TestAudio.Sine(440, 0.5f, 44100, 1, 44100);
        using MemoryStream output = new MemoryStream();

        //Act
        using (OggVorbisEncoder encoder = new OggVorbisEncoder(output, 1, 44100))
        {
            for (int offset = 0; offset < tone.Length; offset += 100)
            {
                encoder.Encode(tone.AsSpan(offset, Math.Min(100, tone.Length - offset)));
            }
        }

        //Assert
        TestAudio.Decode(output.ToArray()).TotalFrames.Should().Be(44100);
    }

    [Fact]
    public void Encode_of_an_empty_span_returns_zero()
    {
        //Arrange
        using MemoryStream output = new MemoryStream();
        using OggVorbisEncoder encoder = new OggVorbisEncoder(output, 1, 44100);

        //Act
        int consumed = encoder.Encode(ReadOnlySpan<float>.Empty);

        //Assert
        consumed.Should().Be(0);
        encoder.FramesWritten.Should().Be(0);
    }

    [Fact]
    public void Constructor_writes_the_headers_immediately()
    {
        //Arrange
        using MemoryStream output = new MemoryStream();

        //Act
        using OggVorbisEncoder encoder = new OggVorbisEncoder(output, 2, 44100);

        //Assert
        output.Length.Should().BeGreaterThan(0);
        OggPages.Parse(output.ToArray()).Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Flush_with_no_audio_produces_a_valid_empty_stream()
    {
        //Arrange
        using MemoryStream output = new MemoryStream();

        //Act
        using (OggVorbisEncoder encoder = new OggVorbisEncoder(output, 2, 44100))
        {
            encoder.Flush();
        }

        //Assert
        OggPages.Parse(output.ToArray())[^1].IsEndOfStream.Should().BeTrue();
        TestAudio.Decoded decoded = TestAudio.Decode(output.ToArray());
        decoded.Channels.Should().Be(2);
        decoded.Samples.Length.Should().Be(0);
    }

    [Fact]
    public void Encode_writes_to_a_stream_that_cannot_seek()
    {
        //Arrange
        ForwardOnlyStream output = new ForwardOnlyStream();
        float[] tone = TestAudio.Sine(440, 0.5f, 44100, 2, 44100);

        //Act
        using (OggVorbisEncoder encoder = new OggVorbisEncoder(output, 2, 44100))
        {
            encoder.Encode(tone);
        }

        //Assert
        TestAudio.Decode(output.ToArray()).TotalFrames.Should().Be(44100);
    }

    [Fact]
    public void Duration_follows_frames_written()
    {
        //Arrange
        using MemoryStream output = new MemoryStream();
        using OggVorbisEncoder encoder = new OggVorbisEncoder(output, 1, 48000);

        //Act
        encoder.Encode(new float[24000]);

        //Assert
        encoder.Duration.Should().Be(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void properties_reflect_the_constructor_arguments()
    {
        //Arrange
        using MemoryStream output = new MemoryStream();
        VorbisEncoderOptions options = new VorbisEncoderOptions { Mode = VorbisEncoderMode.Bitrate, SerialNumber = 42 };

        //Act
        using OggVorbisEncoder encoder = new OggVorbisEncoder(output, 2, 48000, options);

        //Assert
        encoder.Channels.Should().Be(2);
        encoder.SampleRate.Should().Be(48000);
        encoder.Mode.Should().Be(VorbisEncoderMode.Bitrate);
        encoder.SerialNumber.Should().Be(42);
        encoder.IsFinished.Should().BeFalse();
        encoder.IsDisposed.Should().BeFalse();
    }

    [Fact]
    public void Constructor_rejects_a_null_stream()
    {
        //Arrange
        Action create = () => new OggVorbisEncoder(null, 2, 44100);

        //Assert
        create.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_a_read_only_stream()
    {
        //Arrange
        using MemoryStream readOnly = new MemoryStream(new byte[16], writable: false);
        Action create = () => new OggVorbisEncoder(readOnly, 2, 44100);

        //Assert
        create.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(256)]
    public void Constructor_rejects_a_bad_channel_count(int channels)
    {
        //Arrange
        using MemoryStream output = new MemoryStream();
        Action create = () => new OggVorbisEncoder(output, channels, 44100);

        //Assert
        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-44100)]
    public void Constructor_rejects_a_bad_sample_rate(int sampleRate)
    {
        //Arrange
        using MemoryStream output = new MemoryStream();
        Action create = () => new OggVorbisEncoder(output, 2, sampleRate);

        //Assert
        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_rejects_bad_options()
    {
        //Arrange
        using MemoryStream output = new MemoryStream();
        Action create = () => new OggVorbisEncoder(output, 2, 44100, new VorbisEncoderOptions { Quality = 2f });

        //Assert
        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_reports_an_unsupported_bitrate_with_its_native_code()
    {
        //Arrange
        using MemoryStream output = new MemoryStream();
        VorbisEncoderOptions options = new VorbisEncoderOptions { Mode = VorbisEncoderMode.Bitrate, NominalBitrate = 1_000 };
        Action create = () => new OggVorbisEncoder(output, 2, 44100, options);

        //Act
        VorbisEncoderException thrown = create.Should().Throw<VorbisEncoderException>().Which;

        //Assert
        thrown.NativeCode.Should().BeInRange(-5, -4);
        thrown.Message.Should().Contain("44100");
    }

    [Fact]
    public void Encode_after_Flush_throws()
    {
        //Arrange
        using MemoryStream output = new MemoryStream();
        using OggVorbisEncoder encoder = new OggVorbisEncoder(output, 1, 44100);
        encoder.Flush();

        //Act
        Action encode = () => encoder.Encode(new float[10]);

        //Assert
        encode.Should().Throw<InvalidOperationException>();
        encoder.IsFinished.Should().BeTrue();
    }

    [Fact]
    public void Encode_after_Dispose_throws()
    {
        //Arrange
        using MemoryStream output = new MemoryStream();
        OggVorbisEncoder encoder = new OggVorbisEncoder(output, 1, 44100);
        encoder.Dispose();

        //Act
        Action encode = () => encoder.Encode(new float[10]);

        //Assert
        encode.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_twice_is_harmless_and_writes_the_end_once()
    {
        //Arrange
        using MemoryStream output = new MemoryStream();
        OggVorbisEncoder encoder = new OggVorbisEncoder(output, 1, 44100);
        encoder.Encode(TestAudio.Sine(440, 0.5f, 44100, 1, 4410));

        //Act
        encoder.Dispose();
        long afterFirst = output.Length;
        encoder.Dispose();

        //Assert
        output.Length.Should().Be(afterFirst);
        encoder.IsDisposed.Should().BeTrue();
        OggPages.Parse(output.ToArray()).Count(p => p.IsEndOfStream).Should().Be(1);
    }

    [Fact]
    public void Flush_twice_is_harmless()
    {
        //Arrange
        using MemoryStream output = new MemoryStream();
        using OggVorbisEncoder encoder = new OggVorbisEncoder(output, 1, 44100);
        encoder.Flush();
        long afterFirst = output.Length;

        //Act
        encoder.Flush();

        //Assert
        output.Length.Should().Be(afterFirst);
    }

    [Fact]
    public void Dispose_leaves_the_stream_open()
    {
        //Arrange
        MemoryStream output = new MemoryStream();
        OggVorbisEncoder encoder = new OggVorbisEncoder(output, 1, 44100);

        //Act
        encoder.Dispose();

        //Assert
        output.CanWrite.Should().BeTrue();
        output.Dispose();
    }

    [Fact]
    public void NativeGranulePosition_advances_once_audio_pages_complete()
    {
        //Arrange
        using MemoryStream output = new MemoryStream();
        using OggVorbisEncoder encoder = new OggVorbisEncoder(output, 1, 44100);

        // Noise, not a sine: a pure tone compresses so well that two seconds may not fill one Ogg page.
        float[] noise = TestAudio.Noise(0.5f, 1, 44100 * 2, seed: 7);

        //Act
        long before = encoder.NativeGranulePosition();
        encoder.Encode(noise);
        long after = encoder.NativeGranulePosition();

        //Assert
        before.Should().BeLessThanOrEqualTo(0);
        after.Should().BeGreaterThan(0);
    }
}
