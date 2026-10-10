using System.IO;
using CodeBrix.Audio.Vorbis.Interop;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Vorbis.Tests;

// Registration is process-wide and permanent, so these assert what is true after registering themselves.
public class CodeBrixAudioVorbisTests
{
    [Fact]
    public void Register_makes_ogg_writable_by_file_name()
    {
        //Act
        CodeBrixAudioVorbis.Register();

        //Assert
        CodeBrixAudioVorbis.IsRegistered.Should().BeTrue();
        AudioFileWriterRegistry.Supports("tune.ogg").Should().BeTrue();
        AudioFileWriterRegistry.Resolve(".ogg").Should().BeOfType<VorbisAudioFileWriterFactory>();
    }

    [Fact]
    public void Register_is_idempotent()
    {
        //Arrange
        CodeBrixAudioVorbis.Register();
        IAudioFileWriterFactory first = AudioFileWriterRegistry.Resolve(".ogg");

        //Act
        CodeBrixAudioVorbis.Register();
        CodeBrixAudioVorbis.Register();

        //Assert
        AudioFileWriterRegistry.Resolve(".ogg").Should().BeSameAs(first);
    }

    [Fact]
    public void Register_leaves_the_built_in_writers_in_place()
    {
        //Act
        CodeBrixAudioVorbis.Register();

        //Assert
        AudioFileWriterRegistry.Supports(".wav").Should().BeTrue();
        AudioFileWriterRegistry.Supports(".aiff").Should().BeTrue();
    }

    [Fact]
    public void registry_writes_a_decodable_ogg_by_file_name()
    {
        //Arrange
        CodeBrixAudioVorbis.Register();
        using MemoryStream output = new MemoryStream();
        float[] tone = TestAudio.Sine(440, 0.5f, 48000, 2, 48000);

        //Act
        using (IAudioFileWriter writer = AudioFileWriterRegistry.Create("render.ogg", output, 48000, 2))
        {
            writer.Write(tone);
        }

        //Assert
        TestAudio.Decoded decoded = TestAudio.Decode(output.ToArray());
        decoded.SampleRate.Should().Be(48000);
        decoded.TotalFrames.Should().Be(48000);
    }

    [Fact]
    public void EnsureLoaded_accepts_the_shipped_contract_version()
    {
        //Act
        CodeBrixAudioVorbis.EnsureLoaded();

        //Assert
        VorbisNative.ApiVersion().Should().Be(VorbisLibrary.ExpectedApiVersion);
        VorbisLibrary.IsLoaded.Should().BeTrue();
    }

    [Fact]
    public void NativeLibraryVersion_names_libvorbis()
        => CodeBrixAudioVorbis.NativeLibraryVersion.Should().Contain("libVorbis");

    [Fact]
    public void NativeLibraryPath_is_known_once_loaded()
    {
        //Act
        CodeBrixAudioVorbis.EnsureLoaded();

        //Assert
        CodeBrixAudioVorbis.NativeLibraryPath.Should().Contain("codebrix_vorbis");
    }
}
