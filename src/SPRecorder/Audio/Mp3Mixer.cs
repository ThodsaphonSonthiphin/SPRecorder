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

        var mixer = new MixingSampleProvider(new[]
        {
            new VolumeSampleProvider(sys) { Volume = 0.5f },
            new VolumeSampleProvider(mic) { Volume = 0.5f },
        });

        WriteMp3(mixer, outputMp3, bitrateKbps);
    }

    public static void MixToStereo(string systemMp3, string micMp3, string outputMp3,
        int bitrateKbps, int sampleRate = 44100)
    {
        using var sysReader = new Mp3FileReader(systemMp3);
        using var micReader = new Mp3FileReader(micMp3);

        var sys = ToMonoResampled(sysReader.ToSampleProvider(), sampleRate);
        var mic = ToMonoResampled(micReader.ToSampleProvider(), sampleRate);

        var stereo = new MultiplexingSampleProvider(new ISampleProvider[] { sys, mic }, 2);

        WriteMp3(stereo, outputMp3, bitrateKbps);
    }

    private static void WriteMp3(ISampleProvider source, string path, int bitrateKbps)
    {
        var pcm = new SampleToWaveProvider16(source);
        using var writer = new LameMP3FileWriter(path, pcm.WaveFormat, bitrateKbps);

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
