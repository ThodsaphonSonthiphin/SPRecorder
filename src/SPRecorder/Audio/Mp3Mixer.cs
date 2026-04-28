using NAudio.Lame;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SPRecorder.Audio;

public static class Mp3Mixer
{
    public static void MixToMono(string systemMp3, string micMp3, string outputMp3,
        int bitrateKbps, int sampleRate = 44100)
    {
        using var sysReader = new Mp3FileReader(systemMp3);
        using var micReader = new Mp3FileReader(micMp3);

        var sys = ToMonoResampled(sysReader.ToSampleProvider(), sampleRate);
        var mic = ToMonoResampled(micReader.ToSampleProvider(), sampleRate);

        // Halve each input so the sum can't clip when both peak together.
        var mixer = new MixingSampleProvider(new[]
        {
            new VolumeSampleProvider(sys) { Volume = 0.5f },
            new VolumeSampleProvider(mic) { Volume = 0.5f },
        });

        var pcm = new SampleToWaveProvider16(mixer);
        using var writer = new LameMP3FileWriter(outputMp3, pcm.WaveFormat, bitrateKbps);

        var buf = new byte[pcm.WaveFormat.AverageBytesPerSecond];
        int read;
        while ((read = pcm.Read(buf, 0, buf.Length)) > 0)
            writer.Write(buf, 0, read);
    }

    private static ISampleProvider ToMonoResampled(ISampleProvider src, int targetRate)
    {
        ISampleProvider mono = src.WaveFormat.Channels switch
        {
            1 => src,
            2 => src.ToMono(),
            _ => new MultiplexingSampleProvider(new[] { src }, 1),
        };
        return mono.WaveFormat.SampleRate == targetRate
            ? mono
            : new WdlResamplingSampleProvider(mono, targetRate);
    }
}
