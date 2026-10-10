using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Vorbis.Tests;

public class VorbisEncoderOptionsTests
{
    [Fact]
    public void defaults_are_quality_mode_at_point_four()
    {
        //Arrange
        VorbisEncoderOptions options = new VorbisEncoderOptions();

        //Assert
        options.Mode.Should().Be(VorbisEncoderMode.Quality);
        options.Quality.Should().Be(0.4f);
        options.NominalBitrate.Should().Be(128_000);
        options.MinBitrate.Should().BeNull();
        options.MaxBitrate.Should().BeNull();
        options.SerialNumber.Should().BeNull();
        options.Comments.Should().BeEmpty();
    }

    [Fact]
    public void Validate_accepts_the_defaults()
    {
        //Arrange
        Action validate = () => new VorbisEncoderOptions().Validate();

        //Assert
        validate.Should().NotThrow();
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(0f)]
    [InlineData(1.0f)]
    public void Validate_accepts_quality_at_the_limits(float quality)
    {
        //Arrange
        Action validate = () => new VorbisEncoderOptions { Quality = quality }.Validate();

        //Assert
        validate.Should().NotThrow();
    }

    [Theory]
    [InlineData(-0.2f)]
    [InlineData(1.01f)]
    [InlineData(float.NaN)]
    public void Validate_rejects_quality_out_of_range(float quality)
    {
        //Arrange
        Action validate = () => new VorbisEncoderOptions { Quality = quality }.Validate();

        //Assert
        validate.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Validate_ignores_quality_in_bitrate_mode()
    {
        //Arrange
        Action validate = () => new VorbisEncoderOptions { Mode = VorbisEncoderMode.Bitrate, Quality = 5f }.Validate();

        //Assert
        validate.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_rejects_a_non_positive_nominal_bitrate(int bitrate)
    {
        //Arrange
        Action validate = () => new VorbisEncoderOptions { Mode = VorbisEncoderMode.Bitrate, NominalBitrate = bitrate }.Validate();

        //Assert
        validate.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Validate_rejects_a_minimum_above_the_nominal_bitrate()
    {
        //Arrange
        Action validate = () => new VorbisEncoderOptions
        {
            Mode = VorbisEncoderMode.Bitrate,
            NominalBitrate = 128_000,
            MinBitrate = 160_000,
        }.Validate();

        //Assert
        validate.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Validate_rejects_a_maximum_below_the_nominal_bitrate()
    {
        //Arrange
        Action validate = () => new VorbisEncoderOptions
        {
            Mode = VorbisEncoderMode.Bitrate,
            NominalBitrate = 128_000,
            MaxBitrate = 96_000,
        }.Validate();

        //Assert
        validate.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Validate_rejects_an_undefined_mode()
    {
        //Arrange
        Action validate = () => new VorbisEncoderOptions { Mode = (VorbisEncoderMode)7 }.Validate();

        //Assert
        validate.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("TI=TLE")]
    [InlineData("TITLEé")]
    [InlineData("TAB\tTAG")]
    public void Validate_rejects_bad_comment_names(string name)
    {
        //Arrange
        VorbisEncoderOptions options = new VorbisEncoderOptions();
        options.Comments[name] = "value";
        Action validate = () => options.Validate();

        //Assert
        validate.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_rejects_a_null_comment_value()
    {
        //Arrange
        VorbisEncoderOptions options = new VorbisEncoderOptions();
        options.Comments["TITLE"] = null;
        Action validate = () => options.Validate();

        //Assert
        validate.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_rejects_a_comment_value_with_a_nul()
    {
        //Arrange
        VorbisEncoderOptions options = new VorbisEncoderOptions();
        options.Comments["TITLE"] = "a\0b";
        Action validate = () => options.Validate();

        //Assert
        validate.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_accepts_null_comments()
    {
        //Arrange
        Action validate = () => new VorbisEncoderOptions { Comments = null }.Validate();

        //Assert
        validate.Should().NotThrow();
    }
}
