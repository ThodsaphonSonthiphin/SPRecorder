using NAudio.Wave;

namespace SPRecorder.Audio;

public sealed class Mp3FrameSplitter : IMp3Splitter
{
    public IReadOnlyList<string> SplitBySize(string inputPath, long maxBytes)
    {
        if (new FileInfo(inputPath).Length <= maxBytes) return new[] { inputPath };
        throw new NotImplementedException("size-based split not yet implemented");
    }

    public IReadOnlyList<string> SplitByTime(string inputPath, TimeSpan maxDuration)
    {
        using (var probe = new Mp3FileReader(inputPath))
            if (probe.TotalTime <= maxDuration) return new[] { inputPath };
        throw new NotImplementedException("time-based split not yet implemented");
    }
}
