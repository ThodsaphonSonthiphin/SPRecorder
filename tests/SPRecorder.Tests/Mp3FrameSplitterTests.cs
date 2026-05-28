using NAudio.Lame;
using NAudio.Wave;
using SPRecorder.Audio;

namespace SPRecorder.Tests;

public class Mp3FrameSplitterTests
{
    [Fact]
    public void SplitBySize_FileUnderThreshold_ReturnsInputUnchanged()
    {
        var path = TempPath("size-under");
        try
        {
            WriteSineMp3(path, TimeSpan.FromSeconds(2));
            var originalBytes = File.ReadAllBytes(path);

            var splitter = new Mp3FrameSplitter();
            var result = splitter.SplitBySize(path, 1_000_000); // 1 MB, way over file size

            Assert.Single(result);
            Assert.Equal(path, result[0]);
            Assert.Equal(originalBytes, File.ReadAllBytes(path)); // untouched
        }
        finally
        {
            DeleteAll(path);
        }
    }

    [Fact]
    public void SplitBySize_FileOverThreshold_ProducesMultipleChunks()
    {
        var path = TempPath("size-over");
        try
        {
            WriteSineMp3(path, TimeSpan.FromSeconds(60)); // ~720 KB at 96 kbps mono
            long threshold = 200 * 1024; // 200 KB

            var splitter = new Mp3FrameSplitter();
            var chunks = splitter.SplitBySize(path, threshold);

            Assert.True(chunks.Count >= 3, $"Expected >= 3 chunks, got {chunks.Count}");
            foreach (var c in chunks)
            {
                Assert.True(File.Exists(c), $"Chunk missing: {c}");
                Assert.True(new FileInfo(c).Length > 0, $"Chunk empty: {c}");
            }
        }
        finally
        {
            DeleteAll(path);
        }
    }

    [Fact]
    public void SplitBySize_ChunkFilenamesAreZeroPadded()
    {
        var path = TempPath("naming");
        try
        {
            WriteSineMp3(path, TimeSpan.FromSeconds(15));
            var chunks = new Mp3FrameSplitter().SplitBySize(path, 50 * 1024); // ~3-4 chunks

            Assert.True(chunks.Count >= 2);
            var stem = Path.GetFileNameWithoutExtension(path);
            for (int i = 0; i < chunks.Count; i++)
            {
                var expected = $"{stem}_{(i + 1):D3}.mp3";
                Assert.EndsWith(expected, chunks[i]);
            }
        }
        finally
        {
            DeleteAll(path);
        }
    }

    [Fact]
    public void SplitByTime_FileUnderDuration_ReturnsInputUnchanged()
    {
        var path = TempPath("time-under");
        try
        {
            WriteSineMp3(path, TimeSpan.FromSeconds(10));
            var result = new Mp3FrameSplitter().SplitByTime(path, TimeSpan.FromSeconds(60));

            Assert.Single(result);
            Assert.Equal(path, result[0]);
        }
        finally
        {
            DeleteAll(path);
        }
    }

    [Fact]
    public void SplitByTime_FileOverDuration_ProducesMultipleChunks()
    {
        var path = TempPath("time-over");
        try
        {
            WriteSineMp3(path, TimeSpan.FromSeconds(90));
            var chunks = new Mp3FrameSplitter().SplitByTime(path, TimeSpan.FromSeconds(30));

            Assert.True(chunks.Count >= 3, $"Expected >= 3 chunks, got {chunks.Count}");
            foreach (var c in chunks)
            {
                using var r = new Mp3FileReader(c);
                // each non-final chunk should be >= the threshold; final chunk can be shorter
                Assert.True(r.TotalTime.TotalSeconds > 0);
            }
        }
        finally
        {
            DeleteAll(path);
        }
    }

    [Fact]
    public void SplitByTime_HandlesShortFinalChunk()
    {
        var path = TempPath("time-tail");
        try
        {
            WriteSineMp3(path, TimeSpan.FromSeconds(70));
            var chunks = new Mp3FrameSplitter().SplitByTime(path, TimeSpan.FromSeconds(30));

            Assert.Equal(3, chunks.Count);
            using var last = new Mp3FileReader(chunks[^1]);
            // last chunk should be the leftover (~10 s), not a full 30 s slice
            Assert.True(last.TotalTime.TotalSeconds < 25,
                $"Final chunk unexpectedly long: {last.TotalTime.TotalSeconds}s");
        }
        finally
        {
            DeleteAll(path);
        }
    }

    [Fact]
    public void Chunks_AreValidMp3_ReadableByNAudio()
    {
        var path = TempPath("validity");
        try
        {
            WriteSineMp3(path, TimeSpan.FromSeconds(20));
            var chunks = new Mp3FrameSplitter().SplitBySize(path, 100 * 1024);

            Assert.True(chunks.Count >= 2);
            foreach (var c in chunks)
            {
                using var r = new Mp3FileReader(c);
                Assert.Equal(1, r.WaveFormat.Channels);
                Assert.True(r.TotalTime.TotalSeconds > 0);

                // exercise the decoder to confirm no frame corruption
                var pcm = new byte[r.WaveFormat.AverageBytesPerSecond];
                int totalRead = 0, read;
                while ((read = r.Read(pcm, 0, pcm.Length)) > 0) totalRead += read;
                Assert.True(totalRead > 0, $"Chunk decoded to 0 bytes: {c}");
            }
        }
        finally
        {
            DeleteAll(path);
        }
    }

    [Fact]
    public void Chunks_ConcatenatedFrames_EqualInputBytes()
    {
        var path = TempPath("concat");
        try
        {
            WriteSineMp3(path, TimeSpan.FromSeconds(15));
            var originalBytes = File.ReadAllBytes(path);

            var chunks = new Mp3FrameSplitter().SplitBySize(path, 80 * 1024);
            Assert.True(chunks.Count >= 2);

            // Concatenate all chunk bytes
            using var concatenated = new MemoryStream();
            foreach (var c in chunks)
            {
                using var fs = File.OpenRead(c);
                fs.CopyTo(concatenated);
            }
            var combined = concatenated.ToArray();

            // The input may have a leading LAME info frame that our frame loop also
            // preserves verbatim in chunk 1 — so the concatenation of all frame
            // payloads should equal the input MP3 byte-for-byte.
            Assert.Equal(originalBytes.Length, combined.Length);
            Assert.Equal(originalBytes, combined);
        }
        finally
        {
            DeleteAll(path);
        }
    }

    // --- helpers ---

    private static string TempPath(string label) =>
        Path.Combine(Path.GetTempPath(), $"sprec_split_{label}_{Guid.NewGuid():N}.mp3");

    private static void WriteSineMp3(string path, TimeSpan duration, int sampleRate = 44100, int bitrateKbps = 96)
    {
        var fmt = new WaveFormat(sampleRate, 16, 1);
        using var writer = new LameMP3FileWriter(path, fmt, bitrateKbps);
        int totalSamples = (int)(sampleRate * duration.TotalSeconds);
        var buf = new byte[totalSamples * 2];
        for (int i = 0; i < totalSamples; i++)
        {
            short v = (short)(MathF.Sin(2 * MathF.PI * 440 * i / sampleRate) * 16000);
            buf[i * 2]     = (byte)(v & 0xFF);
            buf[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        writer.Write(buf, 0, buf.Length);
    }

    private static void DeleteAll(params string[] paths)
    {
        foreach (var p in paths)
            if (File.Exists(p)) File.Delete(p);
        // also clean any numbered chunks next to each input
        foreach (var p in paths)
        {
            var dir = Path.GetDirectoryName(p) ?? "";
            var stem = Path.GetFileNameWithoutExtension(p);
            foreach (var f in Directory.GetFiles(dir, $"{stem}_*.mp3"))
                File.Delete(f);
        }
    }
}
