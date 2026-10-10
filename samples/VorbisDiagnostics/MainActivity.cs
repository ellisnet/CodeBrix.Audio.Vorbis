using System;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.Android;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Wave;
using global::Android.App;
using global::Android.OS;
using global::Android.Widget;
using AndroidBuild = global::Android.OS.Build;
using AndroidLog = global::Android.Util.Log;

namespace CodeBrix.Audio.Vorbis.Diagnostics;

/// <summary>
/// One-screen device proof for CodeBrix.Audio.Vorbis: loads the native encoder, encodes two generated tones to
/// .ogg files, decodes them back with CodeBrix.Audio.Core, plays the first one through CodeBrix.Audio.Android,
/// and ends with a "VORBIS-DIAG RESULT:" line. Every line also goes to logcat under the tag VorbisDiag.
/// </summary>
[Activity(Label = "CodeBrix Vorbis Diagnostics", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    private const string LogTag = "VorbisDiag";

    private TextView _log = null;
    private ScrollView _scroll = null;
    private Button _run = null;
    private int _running;
    private AndroidAudioFocus _focus = null;
    private AudioFilePlayer _player = null;

    /// <summary>A test tone and what is expected of it after the round trip.</summary>
    private sealed class ToneCase
    {
        public string Name { get; init; }
        public double Frequency { get; init; }
        public int SampleRate { get; init; }
        public int Channels { get; init; }
        public double Seconds { get; init; }
        public VorbisEncoderOptions Options { get; init; }
        public string Path { get; set; }
    }

    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        CodeBrixAndroidAudio.Initialize(this);
        _focus = new AndroidAudioFocus(this);

        var panel = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Vertical };
        _run = new Button(this) { Text = "Run Vorbis diagnostics" };
        _run.Click += (_, _) => StartRun();
        _log = new TextView(this) { Text = "" };
        _log.SetTextIsSelectable(true);
        _scroll = new ScrollView(this);
        _scroll.AddView(_log);
        panel.AddView(_run);
        panel.AddView(_scroll, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MatchParent, 0, 1f));
        SetContentView(panel);

        // Auto-run one second after launch so adb can drive the app without taps.
        new Handler(Looper.MainLooper).PostDelayed(StartRun, 1000);
    }

    private void StartRun()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        _run.Enabled = false;
        Task.Run(() =>
        {
            try { RunDiagnostics(); }
            catch (Exception ex) { Log("VORBIS-DIAG RESULT: FAIL: unexpected " + ex.GetType().Name + ": " + ex.Message); }
            finally
            {
                Volatile.Write(ref _running, 0);
                RunOnUiThread(() => _run.Enabled = true);
            }
        });
    }

    private void Log(string line)
    {
        AndroidLog.Info(LogTag, line);
        RunOnUiThread(() =>
        {
            _log.Append(line + "\n");
            _scroll.Post(() => _scroll.FullScroll(global::Android.Views.FocusSearchDirection.Down));
        });
    }

    private void RunDiagnostics()
    {
        string failure = null;
        void Fail(string reason) { failure ??= reason; Log("CHECK FAILED: " + reason); }

        Log("=== CodeBrix.Audio.Vorbis device diagnostics ===");
        Log($"Device {AndroidBuild.Manufacturer} {AndroidBuild.Model}, ABI {AndroidBuild.SupportedAbis?[0]}, "
            + $"API {(int)AndroidBuild.VERSION.SdkInt}, process {RuntimeInformation.ProcessArchitecture}, "
            + $"runtime {RuntimeInformation.FrameworkDescription}");

        // (1) Load and register.
        try
        {
            CodeBrixAudioVorbis.Register();
            Log("Step 1 Register: OK, IsRegistered=" + CodeBrixAudioVorbis.IsRegistered);
            Log("Step 1 NativeLibraryVersion: " + CodeBrixAudioVorbis.NativeLibraryVersion);
            Log("Step 1 NativeLibraryPath: " + (CodeBrixAudioVorbis.NativeLibraryPath ?? "(null)"));
        }
        catch (Exception ex)
        {
            Log("Step 1 Register FAILED: " + ex);
            Log("VORBIS-DIAG RESULT: FAIL: native load: " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]);
            return;
        }

        var cases = new[]
        {
            new ToneCase
            {
                Name = "tone-440hz-stereo-48k-q04", Frequency = 440, SampleRate = 48000, Channels = 2, Seconds = 3,
                Options = new VorbisEncoderOptions { Mode = VorbisEncoderMode.Quality, Quality = 0.4f },
            },
            new ToneCase
            {
                Name = "tone-660hz-mono-44k1-96kbps", Frequency = 660, SampleRate = 44100, Channels = 1, Seconds = 2,
                Options = new VorbisEncoderOptions { Mode = VorbisEncoderMode.Bitrate, NominalBitrate = 96000 },
            },
        };

        const float amplitude = 0.5011872f; // -6 dBFS peak
        double expectedRms = amplitude / Math.Sqrt(2);
        string externalDir = GetExternalFilesDir(null)?.AbsolutePath;

        foreach (ToneCase tone in cases)
        {
            // (2) Generate.
            int frames = (int)Math.Round(tone.SampleRate * tone.Seconds);
            float[] pcm = new float[frames * tone.Channels];
            for (int i = 0; i < frames; i++)
            {
                float v = (float)(amplitude * Math.Sin(2 * Math.PI * tone.Frequency * i / tone.SampleRate));
                for (int c = 0; c < tone.Channels; c++) pcm[(i * tone.Channels) + c] = v;
            }

            Log($"Step 2 {tone.Name}: generated {frames} frames, {tone.Channels} ch @ {tone.SampleRate} Hz, "
                + $"{tone.Frequency} Hz sine, -6 dBFS");

            // (3) Encode.
            tone.Path = Path.Combine(FilesDir.AbsolutePath, tone.Name + ".ogg");
            try
            {
                tone.Options.Comments["TITLE"] = tone.Name;
                tone.Options.Comments["ENCODER_TEST"] = "CodeBrix VorbisDiagnostics";
                var watch = Stopwatch.StartNew();
                long framesWritten;
                TimeSpan duration;
                using (FileStream file = File.Create(tone.Path))
                {
                    using (var encoder = new OggVorbisEncoder(file, tone.Channels, tone.SampleRate, tone.Options))
                    {
                        // Feed in blocks, as a real producer would.
                        int block = 4096 * tone.Channels;
                        for (int offset = 0; offset < pcm.Length; offset += block)
                        {
                            encoder.Encode(pcm.AsSpan(offset, Math.Min(block, pcm.Length - offset)));
                        }

                        encoder.Flush();
                        framesWritten = encoder.FramesWritten;
                        duration = encoder.Duration;
                    }
                }

                watch.Stop();
                long bytes = new FileInfo(tone.Path).Length;
                Log($"Step 3 {tone.Name}: mode {tone.Options.Mode}, {bytes} bytes, FramesWritten {framesWritten}, "
                    + $"Duration {duration.TotalSeconds:F3} s, {watch.Elapsed.TotalMilliseconds:F1} ms, path {tone.Path}");
                if (framesWritten != frames) Fail($"{tone.Name}: FramesWritten {framesWritten} != {frames}");
                if (bytes < 1000) Fail($"{tone.Name}: only {bytes} bytes written");

                if (externalDir != null)
                {
                    string copy = Path.Combine(externalDir, tone.Name + ".ogg");
                    File.Copy(tone.Path, copy, true);
                    Log($"Step 3 {tone.Name}: copied for adb pull to {copy}");
                }
            }
            catch (Exception ex)
            {
                Log($"Step 3 {tone.Name} encode FAILED: {ex}");
                Fail($"{tone.Name}: encode threw {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            // (4) Decode back with CodeBrix.Audio.Core.
            try
            {
                var watch = Stopwatch.StartNew();
                using var reader = new OggVorbisFileReader(tone.Path);
                int channels = reader.WaveFormat.Channels;
                int rate = reader.WaveFormat.SampleRate;
                long reportedFrames = reader.Length / reader.WaveFormat.BlockAlign;
                byte[] buffer = new byte[4096 * channels * sizeof(float)];
                long decodedFrames = 0;
                double sum = 0;
                long counted = 0;
                double peak = 0;
                int edge = 4096; // skip the encoder/decoder edge frames
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ReadOnlySpan<float> floats = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, read));
                    for (int i = 0; i < floats.Length; i++)
                    {
                        long frame = decodedFrames + (i / channels);
                        float s = floats[i];
                        peak = Math.Max(peak, Math.Abs(s));
                        if (frame >= edge && frame < frames - edge) { sum += (double)s * s; counted++; }
                    }

                    decodedFrames += floats.Length / channels;
                }

                watch.Stop();
                double rms = counted == 0 ? 0 : Math.Sqrt(sum / counted);
                Log($"Step 4 {tone.Name}: decoded channels {channels}, rate {rate}, reported frames {reportedFrames}, "
                    + $"decoded frames {decodedFrames}, RMS {rms:F4} (expected {expectedRms:F4}), peak {peak:F4}, "
                    + $"vendor '{reader.EncoderVendor}', {watch.Elapsed.TotalMilliseconds:F1} ms");
                if (channels != tone.Channels) Fail($"{tone.Name}: decoded {channels} channels, expected {tone.Channels}");
                if (rate != tone.SampleRate) Fail($"{tone.Name}: decoded rate {rate}, expected {tone.SampleRate}");
                if (Math.Abs(decodedFrames - frames) > 2048) Fail($"{tone.Name}: decoded {decodedFrames} frames, expected {frames}");
                if (Math.Abs(rms - expectedRms) > expectedRms * 0.1) Fail($"{tone.Name}: RMS {rms:F4} not within 10% of {expectedRms:F4}");
            }
            catch (Exception ex)
            {
                Log($"Step 4 {tone.Name} decode FAILED: {ex}");
                Fail($"{tone.Name}: decode threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        // (5) Play the first file audibly through CodeBrix.Audio.Android (the shared output).
        try
        {
            if (cases[0].Path == null || !File.Exists(cases[0].Path)) throw new FileNotFoundException("first file was not written");
            bool focus = _focus.Request();
            Log("Step 5 audio focus granted: " + focus);
            using var ended = new ManualResetEventSlim(false);
            _player = new AudioFilePlayer();
            _player.PlaybackEnded += (_, _) => ended.Set();
            _player.Load(cases[0].Path);
            _player.Volume = 0.5f;
            Log($"Step 5 playback START: {cases[0].Name}, player Duration {_player.Duration.TotalSeconds:F3} s");
            var watch = Stopwatch.StartNew();
            _player.Play();
            bool natural = ended.Wait(_player.Duration + TimeSpan.FromSeconds(5));
            watch.Stop();
            Log($"Step 5 playback END: natural end {natural}, wall {watch.Elapsed.TotalSeconds:F3} s, "
                + $"state {_player.PlaybackState}, position {_player.Position.TotalSeconds:F3} s");
            if (!natural) Fail("playback did not reach its natural end within duration + 5 s");
            else if (watch.Elapsed < _player.Duration - TimeSpan.FromSeconds(0.5)) Fail($"playback ended after only {watch.Elapsed.TotalSeconds:F3} s");
            _player.Dispose();
            _player = null;
            _focus.Abandon();
        }
        catch (Exception ex)
        {
            Log("Step 5 playback FAILED: " + ex);
            Fail("playback threw " + ex.GetType().Name + ": " + ex.Message);
        }

        // (6) Verdict.
        Log(failure == null ? "VORBIS-DIAG RESULT: PASS" : "VORBIS-DIAG RESULT: FAIL: " + failure);
    }

    protected override void OnDestroy()
    {
        _player?.Dispose();
        _focus?.Dispose();
        SharedAudioOutput.Shutdown();
        base.OnDestroy();
    }
}
