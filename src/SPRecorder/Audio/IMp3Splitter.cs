namespace SPRecorder.Audio;

public interface IMp3Splitter
{
    // Returns paths of generated chunks.
    // If input <= threshold, returns [inputPath] and writes nothing.
    IReadOnlyList<string> SplitByTime(string inputPath, TimeSpan maxDuration);
    IReadOnlyList<string> SplitBySize(string inputPath, long maxBytes);
}
