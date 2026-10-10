================================================================================
AGENT-README: CodeBrix.Audio.Vorbis
A Guide for AI Coding Agents - CONSUMING the
CodeBrix.Audio.Vorbis.BsdLicenseForever NuGet package
================================================================================

OVERVIEW
========
CodeBrix.Audio.Vorbis is an Ogg Vorbis ENCODER for .NET 10 or later. It turns
interleaved 32-bit float samples into a standard .ogg (Ogg Vorbis) stream
written to any writable System.IO.Stream, and it plugs an ".ogg" writer into
CodeBrix.Audio's AudioFileWriterRegistry so .ogg files can be written by file
name.

It is encode-only. Reading, decoding and playing .ogg files is already part of
CodeBrix.Audio.Core (OggVorbisFileReader, AudioFileReader, the playback
engine) and needs nothing from this package.

The encoder itself is native code: the package carries one native library per
platform under runtimes/<rid>/native/, built from the Xiph.Org reference
libvorbis and libogg sources. The application chooses nothing - natives for
every supported runtime identifier are in the package, and NuGet / the .NET
runtime pick the right one (on Android it is packed into the APK).


INSTALLATION
============
PackageId:  CodeBrix.Audio.Vorbis.BsdLicenseForever

    dotnet add package CodeBrix.Audio.Vorbis.BsdLicenseForever

IMPORTANT: the NuGet package id is CodeBrix.Audio.Vorbis.BsdLicenseForever (NOT
"CodeBrix.Audio.Vorbis"). The assembly and primary namespace are
CodeBrix.Audio.Vorbis.

NuGet dependencies (pulled in automatically, no version pinning needed in the
consuming project):
  - CodeBrix.Audio.Core.MitLicenseForever  (WaveFormat, IAudioFileWriter,
                                            AudioFileWriterRegistry, and the
                                            managed Vorbis DECODER)

License: BSD 3-Clause (SPDX: BSD-3-Clause). The native libraries are built
from the upstream code named in OVERVIEW, also BSD 3-Clause; its licence texts ship beside every
native binary as LICENSE-Vorbis.txt and LICENSE-Ogg.txt.

Requirements: .NET 10 or later, on one of the runtime identifiers the package
has a native for:
    win-x64, win-arm64, osx-x64, osx-arm64,
    linux-x64, linux-arm64, linux-riscv64,
    android-x64, android-arm64 (Android 13 / API 33 and later)
Anything else (32-bit processes, linux-musl, iOS, WebAssembly) throws a
VorbisEncoderException on first use, naming the platform.


KEY NAMESPACES / USINGS
=======================
    using CodeBrix.Audio.Vorbis;   // OggVorbisEncoder, VorbisEncoderOptions,
                                   // VorbisEncoderMode, VorbisEncoderException,
                                   // VorbisAudioFileWriterFactory,
                                   // CodeBrixAudioVorbis
    using CodeBrix.Audio.Wave;     // (from Core) AudioFileWriterRegistry,
                                   // IAudioFileWriter, WaveFormat,
                                   // OggVorbisFileReader (to read back)

Note: CodeBrix.Audio.Core also has types in the CodeBrix.Audio.Vorbis namespace
(its internal decoder). They are not this package's API; everything listed in
this file is.


CORE API REFERENCE
==================

OggVorbisEncoder : IDisposable   (sealed)
-----------------------------------------
    OggVorbisEncoder(Stream output, int channels, int sampleRate,
                     VorbisEncoderOptions options = null)
        Creates the native encoder and IMMEDIATELY writes the Ogg Vorbis
        header pages to output.
        output      writable stream; NOT owned (the caller closes it, after
                    disposing the encoder); need not be seekable.
        channels    1..255.
        sampleRate  > 0, e.g. 44100 or 48000.
        options     null = defaults (quality 0.4, no comments, random serial).
        Throws ArgumentNullException (output null), ArgumentException (stream
        not writable, bad comment), ArgumentOutOfRangeException (channels,
        sampleRate or an option out of range), VorbisEncoderException (native
        library missing, or the native encoder refuses the combination - e.g. a bitrate
        it has no mode for at that rate/channel count).

    int Encode(ReadOnlySpan<float> interleavedSamples)
        Interleaved samples (L, R, L, R, ...) in [-1, 1]; values beyond are
        clipped. Writes every Ogg page that completes. Returns the number of
        WHOLE FRAMES consumed (samples / Channels); a trailing partial frame is
        NOT consumed - pass it again at the start of the next call. Any length
        is fine (large inputs are split internally). A float[] or Span<float>
        converts implicitly.
        Throws ObjectDisposedException after Dispose, InvalidOperationException
        after Flush, VorbisEncoderException on a native failure.

    void Flush()
        ENDS THE STREAM: flushes the encoder, writes the final pages (the last
        one flagged end-of-stream) and flushes the output stream. Nothing can be
        encoded afterwards. Calling it again does nothing. (It is not a
        "flush and keep going" - there is no such operation in Vorbis.)

    void Dispose()
        Calls Flush() if it has not run, then releases the native encoder. The
        output stream stays open. Idempotent.

    int Channels { get; }
    int SampleRate { get; }
    VorbisEncoderMode Mode { get; }
    int SerialNumber { get; }        Ogg logical-stream serial in every page.
    long FramesWritten { get; }      frames accepted by Encode so far.
    TimeSpan Duration { get; }       FramesWritten / SampleRate.
    bool IsFinished { get; }         true after Flush or Dispose.
    bool IsDisposed { get; }

VorbisEncoderOptions   (sealed, settable properties)
-----------------------------------------------------
    VorbisEncoderMode Mode           default Quality
    float Quality                    -0.1 .. 1.0, default 0.4 (about
                                     128 kbit/s for stereo music). Quality mode
                                     only.
    int NominalBitrate               bits/s, > 0, default 128000. Bitrate mode
                                     only.
    int? MinBitrate                  bits/s, <= NominalBitrate, or null.
                                     Bitrate mode only.
    int? MaxBitrate                  bits/s, >= NominalBitrate, or null.
                                     Bitrate mode only.
    IDictionary<string, string> Comments
                                     Vorbis comments, e.g. ["TITLE"] = "...".
                                     Names: ASCII 0x20..0x7D, no '='. Values:
                                     any text without NUL, written as UTF-8.
                                     Default: empty (ordinal) dictionary. The
                                     vendor string is written automatically.
    int? SerialNumber                null (default) = random.
    Constants: MinimumQuality (-0.1f), MaximumQuality (1.0f),
               DefaultQuality (0.4f), DefaultNominalBitrate (128000).
    Options are validated when the encoder (or factory) is created and read
    once; later changes do not affect an existing encoder.

VorbisEncoderMode   (enum)
--------------------------
    Quality = 0    variable bitrate by quality target (recommended).
    Bitrate = 1    managed bitrate around NominalBitrate.

VorbisEncoderException : Exception
----------------------------------
    int NativeCode   the native result code, or 0 when the failure was not a
                     native call (e.g. the native library could not be loaded):
                     -1 argument out of range
                     -2 out of memory
                     -3 call not valid in the current state
                     -4 the encoder does not support this mode / rate /
                        channels / bitrate combination
                     -5 the encoder rejected the request as invalid
                     -6 any other native encoder / Ogg failure

CodeBrixAudioVorbis   (static)
------------------------------
    void Register()
        Loads and checks the native encoder, then registers a
        VorbisAudioFileWriterFactory for ".ogg" with AudioFileWriterRegistry.
        Idempotent and thread-safe. Throws VorbisEncoderException if the native
        library cannot be loaded (message names the RID and every path tried).
    void EnsureLoaded()
        Just the native load + contract-version check, without registering.
    bool IsRegistered { get; }
    string NativeLibraryVersion { get; }   native encoder version string
                                           (loads the native).
    string NativeLibraryPath { get; }      where the native was loaded from,
                                           or null before it has been.

VorbisAudioFileWriterFactory : IAudioFileWriterFactory   (sealed)
-----------------------------------------------------------------
    VorbisAudioFileWriterFactory()                         default options
    VorbisAudioFileWriterFactory(VorbisEncoderOptions options)
    VorbisEncoderOptions Options { get; }
    IReadOnlyList<string> Extensions      [".ogg"]
    bool RequiresSeekableStream           false
    WaveFormat DefaultFormat(int sampleRate, int channels)   IEEE float
    IAudioFileWriter Create(Stream stream, WaveFormat format)
        Only format.SampleRate and format.Channels are used; a 16-bit PCM
        format writes the same file as a float one. The stream is not closed by
        the writer. The writer accepts any sample count per Write (it carries a
        split frame over to the next call); IAudioFileWriter.Finish / Dispose
        ends the stream.


COMPLETE EXAMPLES
=================

1. Encode a generated tone to a file
------------------------------------
    using System;
    using System.IO;
    using CodeBrix.Audio.Vorbis;

    const int rate = 48000, channels = 2;
    float[] samples = new float[rate * channels];          // one second
    for (int i = 0; i < rate; i++)
    {
        float v = 0.5f * MathF.Sin(2 * MathF.PI * 440 * i / rate);
        samples[i * 2] = v;
        samples[i * 2 + 1] = v;
    }

    using FileStream file = File.Create("tone.ogg");
    using (var encoder = new OggVorbisEncoder(file, channels, rate))
    {
        encoder.Encode(samples);
    }                                   // Dispose writes the end of stream

2. Streaming encode in blocks, with comments and bitrate mode
-------------------------------------------------------------
    var options = new VorbisEncoderOptions
    {
        Mode = VorbisEncoderMode.Bitrate,
        NominalBitrate = 160_000,
        MaxBitrate = 192_000,
    };
    options.Comments["TITLE"] = "Session 12";
    options.Comments["ARTIST"] = "Studio B";

    using var output = new MemoryStream();
    using (var encoder = new OggVorbisEncoder(output, 2, 44100, options))
    {
        float[] block = new float[1024 * 2];
        while (FillNextBlock(block, out int samplesInBlock))   // your source
        {
            encoder.Encode(block.AsSpan(0, samplesInBlock));
        }
        encoder.Flush();                // explicit end; Dispose would do it too
    }
    byte[] ogg = output.ToArray();

3. Write .ogg by file name through CodeBrix.Audio
-------------------------------------------------
    using System.IO;
    using CodeBrix.Audio.Vorbis;
    using CodeBrix.Audio.Wave;

    CodeBrixAudioVorbis.Register();     // once, at start-up

    using var stream = File.Create("mix.ogg");
    using (IAudioFileWriter writer =
           AudioFileWriterRegistry.Create("mix.ogg", stream, 44100, 2))
    {
        writer.Write(samples);          // float[] or ReadOnlySpan<float>
    }

    // Different settings for every .ogg written by name: register your own
    // factory AFTER Register().
    AudioFileWriterRegistry.Register(
        new VorbisAudioFileWriterFactory(new VorbisEncoderOptions { Quality = 0.8f }));

4. Read it back (Core, no extra package)
----------------------------------------
    using CodeBrix.Audio.Wave;

    using var reader = new OggVorbisFileReader("tone.ogg");
    // reader.WaveFormat: IEEE float, file's rate/channels
    // reader.Tags["TITLE"], reader.EncoderVendor

5. Diagnose a missing native
----------------------------
    try
    {
        CodeBrixAudioVorbis.EnsureLoaded();
        Console.WriteLine(CodeBrixAudioVorbis.NativeLibraryVersion);
        Console.WriteLine(CodeBrixAudioVorbis.NativeLibraryPath);
    }
    catch (VorbisEncoderException ex)
    {
        Console.WriteLine(ex.Message);  // names the RID and every path tried
    }


MINIMUM VIABLE PROJECT TEMPLATE
===============================
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference Include="CodeBrix.Audio.Vorbis.BsdLicenseForever" Version="*" />
      </ItemGroup>
    </Project>

    // Program.cs
    using System;
    using System.IO;
    using CodeBrix.Audio.Vorbis;

    float[] tone = new float[44100];
    for (int i = 0; i < tone.Length; i++)
        tone[i] = 0.4f * MathF.Sin(2 * MathF.PI * 440 * i / 44100f);

    using FileStream file = File.Create("a440.ogg");
    using var encoder = new OggVorbisEncoder(file, 1, 44100);
    encoder.Encode(tone);

(Pin a real version instead of "*" in a real project.)


PERFORMANCE TIPS
================
- Feed blocks of a few thousand frames or more; per-call overhead is small but
  not zero. Very large single calls are fine - they are split internally.
- Quality mode gives the best sound for the size; use Bitrate mode only when a
  bitrate target matters (streaming).
- Encoding is CPU-bound and single-threaded per encoder. Encode several files
  in parallel with one encoder per thread; never share one encoder between
  threads.
- Pass a buffered stream (FileStream is buffered by default). The encoder
  writes each completed Ogg page as it appears.


COMMON PITFALLS TO AVOID
========================
- Forgetting to Dispose (or Flush). Without it the final pages and the
  end-of-stream flag are never written and the file is truncated. Use a using
  block; the stream must still be open when the encoder is disposed - dispose
  the encoder FIRST, then the stream (nest the usings that way).
- Treating Flush() as a mid-stream flush. It ENDS the stream; Encode afterwards
  throws InvalidOperationException.
- Passing non-interleaved (planar) audio. Samples must be frame-interleaved:
  L0 R0 L1 R1 ...
- Ignoring Encode's return value with odd-length buffers. Only whole frames are
  consumed; resubmit any trailing partial frame. (The registry writer does this
  for you.)
- Expecting 16-bit PCM input. Encode takes floats in [-1, 1]; convert shorts by
  dividing by 32768f.
- Bitrate mode with a bitrate the encoder has no mode for (e.g. 8 kbit/s stereo
  at 44.1 kHz) throws VorbisEncoderException with NativeCode -4 or -5. Use
  quality mode or a realistic bitrate.
- Calling CodeBrixAudioVorbis.Register() and expecting playback changes. Reading
  and playing .ogg is Core's job and works without this package; Register only
  adds WRITING by file name.
- Trimmed or single-file publishes that drop runtimes/: the native must still
  reach the output. If the loader cannot find it, the exception lists every
  path it tried.


WHAT THIS PACKAGE DOES NOT DO
=============================
- Decode or play Ogg Vorbis (CodeBrix.Audio.Core does that).
- Encode Opus, FLAC or MP3 (Opus: CodeBrix.Audio.Opus).
- Chain several logical streams in one file, or multiplex Vorbis with video.
- Resample or remix: the stream is written at the rate and channel count given.
- Edit comments of an existing file without re-encoding.


WORKING EXAMPLES ON GITHUB
==========================
Test project:
https://github.com/ellisnet/CodeBrix.Audio.Vorbis/tree/main/tests/CodeBrix.Audio.Vorbis.Tests

  OggVorbisEncoderTests.cs
      Round trips (mono/stereo, 44.1/48 kHz, quality and bitrate modes),
      comments, Ogg page structure (BOS/EOS, serial, sequence), partial frames,
      non-seekable streams, argument validation and dispose semantics.
  VorbisEncoderOptionsTests.cs
      Every option's range and comment-name rules.
  VorbisAudioFileWriterFactoryTests.cs
      The ".ogg" factory: default format, PCM formats, factory comments.
  CodeBrixAudioVorbisTests.cs
      Register, writing .ogg by file name through AudioFileWriterRegistry,
      native load diagnostics.
  TestAudio.cs
      Generating test tones and decoding them back with Core's
      OggVorbisFileReader.


QUICK REFERENCE CARD
====================
--- Encode ---
Create:     var enc = new OggVorbisEncoder(stream, channels, sampleRate, options);
Feed:       int frames = enc.Encode(interleavedFloats);
End:        enc.Flush();   // or Dispose()
Progress:   enc.FramesWritten, enc.Duration

--- Options ---
Quality:    new VorbisEncoderOptions { Quality = 0.6f }            // -0.1..1.0
Bitrate:    new VorbisEncoderOptions { Mode = VorbisEncoderMode.Bitrate,
                                       NominalBitrate = 128_000,
                                       MinBitrate = null, MaxBitrate = null }
Tags:       options.Comments["TITLE"] = "...";
Serial:     options.SerialNumber = 1234;

--- Write by file name ---
Register:   CodeBrixAudioVorbis.Register();
Write:      AudioFileWriterRegistry.Create("x.ogg", stream, rate, channels)
Custom:     AudioFileWriterRegistry.Register(new VorbisAudioFileWriterFactory(options));

--- Diagnostics ---
Load check: CodeBrixAudioVorbis.EnsureLoaded();
Versions:   CodeBrixAudioVorbis.NativeLibraryVersion / NativeLibraryPath
Errors:     VorbisEncoderException.NativeCode (-1..-6, 0 = not native)

Target: .NET 10 or later
License: BSD 3-Clause


================================================================================
END OF AGENT-README
================================================================================
