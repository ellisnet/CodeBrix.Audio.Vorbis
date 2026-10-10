# CodeBrix.Audio.Vorbis

Ogg Vorbis encoding for [CodeBrix.Audio](https://github.com/ellisnet/CodeBrix.Audio): float samples in, a standard `.ogg` file out, over a native Vorbis encoder that ships inside the package for every supported platform.
CodeBrix.Audio.Vorbis is provided as a .NET 10 library and associated `CodeBrix.Audio.Vorbis.BsdLicenseForever` NuGet package.

CodeBrix.Audio.Vorbis supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## Installation

```
dotnet add package CodeBrix.Audio.Vorbis.BsdLicenseForever
```

Note that the NuGet package ID and the namespace are different - there is no package named plain `CodeBrix.Audio.Vorbis`:

* NuGet package ID: `CodeBrix.Audio.Vorbis.BsdLicenseForever`
* Assembly and primary namespace: `CodeBrix.Audio.Vorbis` - i.e. `using CodeBrix.Audio.Vorbis;`

XML documentation (IntelliSense) ships alongside the assembly.

The package pulls in the following automatically; no version pinning is needed in the consuming project:

* `CodeBrix.Audio.Core.MitLicenseForever` - the shared audio library whose file-writer registry this package adds `.ogg` to. Reading and playing `.ogg` files is already part of Core; this package adds writing.

## CodeBrix.Audio.Vorbis supports:

* Encoding interleaved 32-bit float samples to Ogg Vorbis, written strictly forwards to any writable `Stream` - a file, a `MemoryStream`, a pipe
* Quality-based variable bitrate (the default, quality -0.1 to 1.0) and managed bitrate (nominal, with optional minimum and maximum)
* Any channel count from 1 to 255 and any sample rate the Vorbis encoder supports - 44.1 kHz and 48 kHz mono and stereo are the everyday cases
* Vorbis comments (`TITLE`, `ARTIST`, `ALBUM`, ...) written as UTF-8
* Writing `.ogg` by file name wherever CodeBrix.Audio writes audio by extension, after one `CodeBrixAudioVorbis.Register()` call
* Native encoders in the package for Windows x64 and ARM64, macOS Intel and Apple Silicon, Linux x64, ARM64 and RISC-V 64, and Android 13+ ARM64 and x64 - the application chooses nothing

## Native assets

The package carries one native library per platform in the standard NuGet `runtimes/<rid>/native/` layout, each beside the licence texts of the code it was built from. NuGet and the .NET runtime pick the right one; on Android it is packed into the APK. If the native cannot be found at run time, the exception names the runtime identifier and every path that was tried.

## Sample Code

### Encode a tone to an .ogg file

```csharp
using System;
using System.IO;
using CodeBrix.Audio.Vorbis;

const int sampleRate = 48000;
const int channels = 2;

float[] samples = new float[sampleRate * channels * 2];   // two seconds
for (int i = 0; i < sampleRate * 2; i++)
{
    float value = 0.5f * MathF.Sin(2 * MathF.PI * 440 * i / sampleRate);
    samples[i * 2] = value;
    samples[i * 2 + 1] = value;
}

var options = new VorbisEncoderOptions { Quality = 0.5f };
options.Comments["TITLE"] = "A440";

using FileStream file = File.Create("tone.ogg");
using (var encoder = new OggVorbisEncoder(file, channels, sampleRate, options))
{
    encoder.Encode(samples);
}   // Dispose ends the stream
```

### Write .ogg by file name

```csharp
using System.IO;
using CodeBrix.Audio.Vorbis;
using CodeBrix.Audio.Wave;

CodeBrixAudioVorbis.Register();   // once, at application start-up

float[] samples = RenderSomething();   // interleaved stereo float samples at 44.1 kHz

using var stream = File.Create("render.ogg");
using (IAudioFileWriter writer = AudioFileWriterRegistry.Create("render.ogg", stream, 44100, 2))
{
    writer.Write(samples);
}
```

## Documentation

The NuGet package includes `AGENT-README.txt`, a complete API reference and usage guide written for AI coding agents - point your agent at that file when it is writing code against this library.

Additional sample code and usage examples are available in the `CodeBrix.Audio.Vorbis.Tests` project:
https://github.com/ellisnet/CodeBrix.Audio.Vorbis/tree/main/tests/CodeBrix.Audio.Vorbis.Tests

## License

CodeBrix.Audio.Vorbis is licensed under the BSD 3-Clause License - see the
[LICENSE](https://github.com/ellisnet/CodeBrix.Audio.Vorbis/blob/main/LICENSE) file.

For licensing and provenance information about the open source code included in
this package, see [THIRD-PARTY-NOTICES.txt](https://github.com/ellisnet/CodeBrix.Audio.Vorbis/blob/main/THIRD-PARTY-NOTICES.txt).
