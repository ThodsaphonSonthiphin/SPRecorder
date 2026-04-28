using NAudio.Lame;
using NAudio.Wave;
using SPRecorder.Audio;

namespace SPRecorder.Tests;

public class Mp3MixerTests
{
    [Fact]
    public void MixToMono_ProducesValidMp3_FromTwoMonoSources()
    {
        var sysPath   = TempPath("sys");
        var micPath   = TempPath("mic");
        var mixedPath = TempPath("mixed");

        try
        {
            WriteSineMp3(sysPath, freq: 440f, sampleRate: 48000, channels: 2);
            WriteSineMp3(micPath, freq: 880f, sampleRate: 44100, channels: 1);

            Mp3Mixer.MixToMono(sysPath, micPath, mixedPath, bitrateKbps: 96, sampleRate: 44100);

            var fi = new FileInfo(mixedPath);
            Assert.True(fi.Exists);
            Assert.True(fi.Length > 1000, $"Mixed MP3 unexpectedly small: {fi.Length} bytes");

            using var reader = new Mp3FileReader(mixedPath);
            Assert.Equal(1, reader.WaveFormat.Channels);          // mono
            Assert.Equal(44100, reader.WaveFormat.SampleRate);    // resampled to common rate
            Assert.True(reader.TotalTime.TotalSeconds > 0.5,
                $"Mixed file is too short: {reader.TotalTime.TotalSeconds}s");
        }
        finally
        {
            foreach (var p in new[] { sysPath, micPath, mixedPath })
                if (File.Exists(p)) File.Delete(p);
        }
    }

    private static string TempPath(string label) =>
        Path.Combine(Path.GetTempPath(), $"sprecorder_mix_{label}_{Guid.NewGuid():N}.mp3");

    [Fact]
    public void MixToStereo_ProducesStereoMp3()
    {
        var sysPath   = TempPath("sys");
        var micPath   = TempPath("mic");
        var mixedPath = TempPath("mixed");

        try
        {
            WriteSineMp3(sysPath, freq: 440f, sampleRate: 44100, channels: 1);
            WriteSineMp3(micPath, freq: 880f, sampleRate: 44100, channels: 1);

            // Use 128 kbps so LAME doesn't auto-downsample for stereo
            Mp3Mixer.MixToStereo(sysPath, micPath, mixedPath, bitrateKbps: 128, sampleRate: 44100);

            using var reader = new Mp3FileReader(mixedPath);
            Assert.Equal(2, reader.WaveFormat.Channels);
            Assert.Equal(44100, reader.WaveFormat.SampleRate);
            Assert.True(reader.TotalTime.TotalSeconds > 0.5);
        }
        finally
        {
            foreach (var p in new[] { sysPath, micPath, mixedPath })
                if (File.Exists(p)) File.Delete(p);
        }
    }

    private static void WriteSineMp3(string path, float freq, int sampleRate, int channels)
    {
        var fmt = new WaveFormat(sampleRate, 16, channels);
        using var writer = new LameMP3FileWriter(path, fmt, 96);

        int samples = sampleRate; // 1 second
        var buf = new byte[samples * channels * 2];
        for (int i = 0; i < samples; i++)
        {
            short v = (short)(MathF.Sin(2 * MathF.PI * freq * i / sampleRate) * 16000);
            for (int c = 0; c < channels; c++)
            {
                int idx = (i * channels + c) * 2;
                buf[idx]     = (byte)(v & 0xFF);
                buf[idx + 1] = (byte)((v >> 8) & 0xFF);
            }
        }
        writer.Write(buf, 0, buf.Length);
    }
}
