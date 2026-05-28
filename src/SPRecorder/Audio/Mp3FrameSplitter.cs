using NAudio.Wave;

namespace SPRecorder.Audio;

public sealed class Mp3FrameSplitter : IMp3Splitter
{
    public IReadOnlyList<string> SplitBySize(string inputPath, long maxBytes)
    {
        if (new FileInfo(inputPath).Length <= maxBytes) return new[] { inputPath };
        return SplitFrames(inputPath, (size, _) => size > maxBytes);
    }

    public IReadOnlyList<string> SplitByTime(string inputPath, TimeSpan maxDuration)
    {
        using (var probe = new Mp3FileReader(inputPath))
            if (probe.TotalTime <= maxDuration) return new[] { inputPath };
        return SplitFrames(inputPath, (_, dur) => dur > maxDuration);
    }

    // shouldClose receives (currentChunkSize, currentChunkDuration) and decides
    // whether to close the current chunk before writing the next frame.
    private static IReadOnlyList<string> SplitFrames(
        string inputPath,
        Func<long, TimeSpan, bool> shouldClose)
    {
        var outputs = new List<string>();
        using var source = File.OpenRead(inputPath);

        FileStream? current = null;
        long currentSize = 0;
        TimeSpan currentDuration = TimeSpan.Zero;
        int index = 1;

        try
        {
            Mp3Frame? frame;
            while ((frame = Mp3Frame.LoadFromStream(source)) != null)
            {
                if (current == null || shouldClose(currentSize, currentDuration))
                {
                    current?.Dispose();
                    var path = ChunkPath(inputPath, index++);
                    outputs.Add(path);
                    current = File.Create(path);
                    currentSize = 0;
                    currentDuration = TimeSpan.Zero;
                }
                current.Write(frame.RawData, 0, frame.RawData.Length);
                currentSize += frame.RawData.Length;
                currentDuration += TimeSpan.FromSeconds(
                    (double)frame.SampleCount / frame.SampleRate);
            }
        }
        finally
        {
            current?.Dispose();
        }

        return outputs;
    }

    private static string ChunkPath(string inputPath, int index)
    {
        var dir  = Path.GetDirectoryName(inputPath) ?? "";
        var stem = Path.GetFileNameWithoutExtension(inputPath);
        var ext  = Path.GetExtension(inputPath);
        return Path.Combine(dir, $"{stem}_{index:D3}{ext}");
    }
}
