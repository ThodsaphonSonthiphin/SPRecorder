using NAudio.Wave;
using SPRecorder.Audio;

namespace SPRecorder.Tests;

public class Mp3StreamWriterTests
{
    [Fact]
    public void WritesValidMp3_FromFloat32Source()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"sprecorder_test_{Guid.NewGuid():N}.mp3");
        try
        {
            var fmt = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
            using (var w = new Mp3StreamWriter(temp, fmt, 96))
            {
                int samples = 44100; // 1 second
                var buf = new byte[samples * 4];
                for (int i = 0; i < samples; i++)
                {
                    float v = MathF.Sin(2 * MathF.PI * 440 * i / 44100f) * 0.5f;
                    var bytes = BitConverter.GetBytes(v);
                    Buffer.BlockCopy(bytes, 0, buf, i * 4, 4);
                }
                w.Write(buf, 0, buf.Length);
            }

            var fi = new FileInfo(temp);
            Assert.True(fi.Exists);
            Assert.True(fi.Length > 1000, $"Expected non-trivial MP3, got {fi.Length} bytes.");

            var head = File.ReadAllBytes(temp).AsSpan(0, 3);
            bool isMp3 = (head[0] == 0xFF && (head[1] & 0xE0) == 0xE0)
                      || (head[0] == 0x49 && head[1] == 0x44 && head[2] == 0x33); // ID3
            Assert.True(isMp3, "File does not start with an MP3 frame or ID3 tag.");
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    [Fact]
    public void WritesValidMp3_FromPcm16Source()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"sprecorder_test_{Guid.NewGuid():N}.mp3");
        try
        {
            var fmt = new WaveFormat(44100, 16, 1);
            using (var w = new Mp3StreamWriter(temp, fmt, 96))
            {
                int samples = 44100;
                var buf = new byte[samples * 2];
                for (int i = 0; i < samples; i++)
                {
                    short v = (short)(MathF.Sin(2 * MathF.PI * 440 * i / 44100f) * 16000);
                    buf[i * 2]     = (byte)(v & 0xFF);
                    buf[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
                }
                w.Write(buf, 0, buf.Length);
            }

            var fi = new FileInfo(temp);
            Assert.True(fi.Exists);
            Assert.True(fi.Length > 1000);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
