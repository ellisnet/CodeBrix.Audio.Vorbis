================================================================================
VorbisDiagnostics - physical-device proof for CodeBrix.Audio.Vorbis on Android
================================================================================

A one-screen .NET for Android app (Android 13 / API 33 and later, arm64-v8a and
x86_64). One second after launch - or when "Run Vorbis diagnostics" is tapped -
it:

  1. calls CodeBrixAudioVorbis.Register() and logs NativeLibraryVersion and
     NativeLibraryPath;
  2. generates two tones in managed code (no recordings): 3 s of 440 Hz stereo
     at 48 kHz and 2 s of 660 Hz mono at 44.1 kHz, both -6 dBFS;
  3. encodes them with OggVorbisEncoder (the first in quality mode 0.4, the
     second in bitrate mode at 96000 bit/s) to the app's FilesDir, logging
     bytes, FramesWritten, Duration and elapsed time, and copies each file to
     the app's external files folder so adb can pull it;
  4. decodes each file back with CodeBrix.Audio.Core's OggVorbisFileReader and
     logs channels, sample rate, frame count, RMS and peak;
  5. plays the first file AUDIBLY through AudioFilePlayer on the shared output
     that CodeBrix.Audio.Android provides (volume 0.5 of a -6 dBFS tone - turn
     the device volume down first), logging start and end;
  6. ends with "VORBIS-DIAG RESULT: PASS" or "VORBIS-DIAG RESULT: FAIL: <reason>".

Every line is shown on screen and written to logcat with the tag VorbisDiag.


HOW THE NATIVE REACHES THE APK
==============================
The sample references the library as SOURCE (a ProjectReference to
src/CodeBrix.Audio.Vorbis). A ProjectReference does NOT carry the library's
runtimes/<rid>/native/ files into an Android build: they are pack-only items in
the library csproj, so they reach the .nupkg but not a referencing project's
output, and an APK built that way has no libcodebrix_vorbis.so (the first call
then fails to load the native). The sample csproj therefore names the two
Android natives itself as AndroidNativeLibrary items with Abi metadata
(arm64-v8a and x86_64), which packs them into lib/<abi>/ of the APK.

An application that consumes the PACKAGE needs none of this: a PackageReference
to CodeBrix.Audio.Vorbis.BsdLicenseForever delivers
runtimes/android-arm64/native and runtimes/android-x64/native into lib/<abi>/
of the APK automatically.

CodeBrix.Audio.Android.ApacheLicenseForever (from nuget.org) is the ONLY audio
natives package referenced - it supplies device output and the playback
decoders. CodeBrix.Audio.Core.MitLicenseForever arrives transitively.


BUILD, INSTALL, RUN
===================
Needs the .NET 10 Android workload, an Android SDK with the API 36 platform and
a JDK the workload accepts (the csproj defaults AndroidSdkDirectory to
$HOME/Android/Sdk and JavaSdkDirectory to the Debian JDK 21 path when those
exist, as CodeBrix.Audio.Android's diagnostics app does).

    dotnet build samples/VorbisDiagnostics/VorbisDiagnostics.csproj -c Release

One APK carries both ABIs:
    samples/VorbisDiagnostics/bin/Release/net10.0-android36.1/
        com.codebrix.audio.vorbis.diagnostics-Signed.apk

    adb -s <serial> install -r <that apk>
    adb -s <serial> logcat -c
    adb -s <serial> shell am start -n \
        com.codebrix.audio.vorbis.diagnostics/crc64ce2e49002291ce63.MainActivity
    (wait about 10 seconds)
    adb -s <serial> logcat -d -s VorbisDiag

The encoded files (the Release build is not debuggable, so run-as cannot read
FilesDir; the app copies them out):

    adb -s <serial> pull \
        /sdcard/Android/data/com.codebrix.audio.vorbis.diagnostics/files/ .
