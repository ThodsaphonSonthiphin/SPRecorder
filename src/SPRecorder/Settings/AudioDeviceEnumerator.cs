using NAudio.CoreAudioApi;

namespace SPRecorder.Settings;

public sealed record AudioDeviceInfo(string Id, string FriendlyName);

public static class AudioDeviceEnumerator
{
    public static IReadOnlyList<AudioDeviceInfo> GetCaptureDevices() =>
        Enumerate(DataFlow.Capture);

    public static IReadOnlyList<AudioDeviceInfo> GetRenderDevices() =>
        Enumerate(DataFlow.Render);

    public static AudioDeviceInfo? GetDefault(DataFlow flow)
    {
        using var enumerator = new MMDeviceEnumerator();
        try
        {
            using var device = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            return new AudioDeviceInfo(device.ID, device.FriendlyName);
        }
        catch { return null; }
    }

    private static IReadOnlyList<AudioDeviceInfo> Enumerate(DataFlow flow)
    {
        using var enumerator = new MMDeviceEnumerator();
        var collection = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
        var list = new List<AudioDeviceInfo>(collection.Count);
        foreach (var device in collection)
        {
            list.Add(new AudioDeviceInfo(device.ID, device.FriendlyName));
            device.Dispose();
        }
        return list;
    }
}
