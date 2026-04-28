using NAudio.Lame;
using NAudio.Wave;

namespace SPRecorder.Audio;

public sealed class Mp3StreamWriter : IDisposable
{
    private readonly LameMP3FileWriter _writer;
    private readonly bool _isFloat;
    private readonly object _lock = new();
    private bool _disposed;

    public Mp3StreamWriter(string path, WaveFormat sourceFormat, int bitrateKbps)
    {
        SourceFormat = sourceFormat;
        _isFloat = sourceFormat.Encoding == WaveFormatEncoding.IeeeFloat;
        TargetFormat = new WaveFormat(sourceFormat.SampleRate, 16, sourceFormat.Channels);
        _writer = new LameMP3FileWriter(path, TargetFormat, bitrateKbps);
    }

    public WaveFormat SourceFormat { get; }
    public WaveFormat TargetFormat { get; }

    public void Write(byte[] buffer, int offset, int count)
    {
        if (_disposed || count <= 0) return;

        var pcm = _isFloat ? FloatToPcm16(buffer, offset, count) : Slice(buffer, offset, count);

        lock (_lock)
        {
            if (_disposed) return;
            _writer.Write(pcm, 0, pcm.Length);
        }
    }

    private static byte[] Slice(byte[] buf, int offset, int count)
    {
        var copy = new byte[count];
        Buffer.BlockCopy(buf, offset, copy, 0, count);
        return copy;
    }

    private static byte[] FloatToPcm16(byte[] buf, int offset, int count)
    {
        int sampleCount = count / 4;
        var result = new byte[sampleCount * 2];
        for (int i = 0; i < sampleCount; i++)
        {
            float sample = BitConverter.ToSingle(buf, offset + i * 4);
            sample = Math.Clamp(sample, -1f, 1f);
            short pcm = (short)(sample * short.MaxValue);
            result[i * 2]     = (byte)(pcm & 0xFF);
            result[i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
        }
        return result;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _writer.Dispose();
        }
    }
}
