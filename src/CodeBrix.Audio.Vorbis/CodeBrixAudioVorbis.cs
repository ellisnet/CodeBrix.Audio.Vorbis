using CodeBrix.Audio.Vorbis.Interop;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Vorbis;

/// <summary>
/// The one call that turns on Ogg Vorbis writing: <c>CodeBrixAudioVorbis.Register();</c>
/// </summary>
/// <remarks>
/// <para>
/// Call it once, early. It loads and checks the native encoder for the current platform - so a packaging
/// problem surfaces at start-up rather than at the first save - and registers a
/// <see cref="VorbisAudioFileWriterFactory" /> for ".ogg" with CodeBrix.Audio's
/// <see cref="AudioFileWriterRegistry" />, so .ogg files are written by file name wherever CodeBrix.Audio
/// writes audio by extension.
/// </para>
/// <para>
/// Reading and playing .ogg files needs nothing from this package: CodeBrix.Audio decodes Vorbis itself.
/// <see cref="OggVorbisEncoder" /> works without this call; it loads the native on first use.
/// </para>
/// <para>
/// There is deliberately NO module initializer doing this for you: a module initializer only runs once
/// something in the assembly is touched, which trimming and lazy assembly loading make unreliable. An explicit
/// call is the contract.
/// </para>
/// </remarks>
public static class CodeBrixAudioVorbis
{
    private static readonly object Gate = new object();

    // ONE factory instance. The registry keys on the extension, so a fresh factory per call would only replace
    // the previous one; a single instance keeps every .ogg written by name on the same settings.
    private static readonly VorbisAudioFileWriterFactory WriterFactory = new VorbisAudioFileWriterFactory();

    private static bool registered;

    /// <summary>
    /// Loads the native encoder and registers the ".ogg" writer with <see cref="AudioFileWriterRegistry" />.
    /// </summary>
    /// <remarks>
    /// Idempotent and safe to call from any thread; calling it more than once does nothing. Register a
    /// <see cref="VorbisAudioFileWriterFactory" /> of your own AFTERWARDS to write .ogg at other settings.
    /// </remarks>
    /// <exception cref="VorbisEncoderException">
    /// The native encoder for this platform could not be loaded; the message names the runtime identifier and
    /// every path that was tried.
    /// </exception>
    public static void Register()
    {
        lock (Gate)
        {
            if (registered) return;

            VorbisLibrary.EnsureLoaded();
            AudioFileWriterRegistry.Register(WriterFactory);

            registered = true;
        }
    }

    /// <summary>
    /// Loads the native encoder for the current platform if it is not loaded already, and checks that it speaks
    /// the contract this package was built against.
    /// </summary>
    /// <exception cref="VorbisEncoderException">
    /// The native library could not be loaded - the message names the runtime identifier and lists every path
    /// that was tried - or it reports a different contract version.
    /// </exception>
    public static void EnsureLoaded() => VorbisLibrary.EnsureLoaded();

    /// <summary>Whether <see cref="Register()" /> has run.</summary>
    public static bool IsRegistered
    {
        get { lock (Gate) { return registered; } }
    }

    /// <summary>
    /// The version string of the libvorbis inside the loaded native library, for diagnostics. Loads the native
    /// library if necessary.
    /// </summary>
    /// <exception cref="VorbisEncoderException">The native library could not be loaded.</exception>
    public static string NativeLibraryVersion => VorbisLibrary.Version;

    /// <summary>
    /// Where the native library was loaded from, for diagnostics, or null when it has not been loaded yet.
    /// </summary>
    public static string NativeLibraryPath => VorbisLibrary.LoadedPath;
}
