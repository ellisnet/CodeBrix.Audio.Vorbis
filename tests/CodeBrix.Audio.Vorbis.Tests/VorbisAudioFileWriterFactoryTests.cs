using System;
using System.IO;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Vorbis.Tests;

public class VorbisAudioFileWriterFactoryTests
{
    [Fact]
    public void Extensions_is_ogg()
        => new VorbisAudioFileWriterFactory().Extensions.Should().ContainSingle().Which.Should().Be(".ogg");

    [Fact]
    public void RequiresSeekableStream_is_false()
        => new VorbisAudioFileWriterFactory().RequiresSeekableStream.Should().BeFalse();

    [Fact]
    public void DefaultFormat_is_ieee_float()
    {
        //Act
        WaveFormat format = new VorbisAudioFileWriterFactory().DefaultFormat(48000, 2);

        //Assert
        format.Encoding.Should().Be(WaveFormatEncoding.IeeeFloat);
        format.SampleRate.Should().Be(48000);
        format.Channels.Should().Be(2);
    }

    [Fact]
    public void DefaultFormat_rejects_bad_arguments()
    {
        //Arrange
        VorbisAudioFileWriterFactory factory = new VorbisAudioFileWriterFactory();
        Action badRate = () => factory.DefaultFormat(0, 2);
        Action badChannels = () => factory.DefaultFormat(44100, 0);

        //Assert
        badRate.Should().Throw<ArgumentOutOfRangeException>();
        badChannels.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void constructor_validates_options()
    {
        //Arrange
        Action create = () => new VorbisAudioFileWriterFactory(new VorbisEncoderOptions { Quality = 3f });

        //Assert
        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Options_defaults_when_null_is_passed()
        => new VorbisAudioFileWriterFactory(null).Options.Quality.Should().Be(0.4f);

    [Fact]
    public void Create_rejects_bad_arguments()
    {
        //Arrange
        VorbisAudioFileWriterFactory factory = new VorbisAudioFileWriterFactory();
        WaveFormat format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        using MemoryStream readOnly = new MemoryStream(new byte[4], writable: false);
        Action nullStream = () => factory.Create(null, format);
        Action nullFormat = () => factory.Create(new MemoryStream(), null);
        Action notWritable = () => factory.Create(readOnly, format);

        //Assert
        nullStream.Should().Throw<ArgumentNullException>();
        nullFormat.Should().Throw<ArgumentNullException>();
        notWritable.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_ignores_the_bit_depth_of_a_pcm_format()
    {
        //Arrange
        VorbisAudioFileWriterFactory factory = new VorbisAudioFileWriterFactory();
        using MemoryStream output = new MemoryStream();
        float[] tone = TestAudio.Sine(440, 0.5f, 44100, 2, 44100);

        //Act
        using (IAudioFileWriter writer = factory.Create(output, new WaveFormat(44100, 16, 2)))
        {
            writer.Write(tone);
        }

        //Assert
        TestAudio.Decoded decoded = TestAudio.Decode(output.ToArray());
        decoded.Channels.Should().Be(2);
        decoded.TotalFrames.Should().Be(44100);
    }

    [Fact]
    public void Create_writer_carries_the_factory_comments()
    {
        //Arrange
        VorbisEncoderOptions options = new VorbisEncoderOptions();
        options.Comments["ALBUM"] = "Factory settings";
        VorbisAudioFileWriterFactory factory = new VorbisAudioFileWriterFactory(options);
        using MemoryStream output = new MemoryStream();

        //Act
        using (IAudioFileWriter writer = factory.Create(output, factory.DefaultFormat(44100, 1)))
        {
            writer.Write(TestAudio.Sine(440, 0.5f, 44100, 1, 4410));
        }

        //Assert
        TestAudio.Decode(output.ToArray()).Tags["ALBUM"].Should().ContainSingle().Which.Should().Be("Factory settings");
    }
}
