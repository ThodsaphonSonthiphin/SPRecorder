using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SPRecorder.Recording;

/// <summary>HDR state of a single display, as far as recording colour fidelity cares.</summary>
public enum HdrState
{
    /// <summary>Display has no HDR/advanced-colour capability — nothing to worry about.</summary>
    NotSupported,
    /// <summary>HDR-capable but currently off — recordings will be correct.</summary>
    Off,
    /// <summary>HDR is ON — ScreenRecorderLib will squash it to SDR and colours come out wrong.</summary>
    On,
}

/// <summary>
/// Reads and toggles the HDR / Advanced-Colour state of a specific monitor via the
/// Win32 DisplayConfig API. ScreenRecorderLib 6.6.0 has no HDR pipeline: on an HDR
/// display it captures into 8-bit BT.709 with no tone-map, so video comes out
/// reddish/oversaturated. The only in-app remedy is to take the recorded monitor out
/// of HDR while recording. Every public method is best-effort and never throws — a
/// failure here must never block a recording.
/// </summary>
public sealed class HdrDisplay
{
    /// <summary>
    /// Pure mapping from the two facts we read off a display to an <see cref="HdrState"/>.
    /// <paramref name="active"/> is type-15 activeColorMode==HDR, or the legacy
    /// type-9 advancedColorEnabled bit. Unsupported always wins.
    /// </summary>
    internal static HdrState Decode(bool supported, bool active)
        => !supported ? HdrState.NotSupported
         : active     ? HdrState.On
         :              HdrState.Off;

    /// <summary>HDR state of the given monitor (empty = primary). Never throws.</summary>
    public HdrState GetState(string deviceName)
    {
        try
        {
            if (!TryFindTarget(deviceName, out var adapterId, out var id))
                return HdrState.NotSupported;
            return QueryState(adapterId, id);
        }
        catch { return HdrState.NotSupported; }
    }

    /// <summary>
    /// If the monitor is in HDR, turn it off and return true (caller should restore
    /// later via <see cref="TurnOn"/>). Returns false if not in HDR or on any failure.
    /// </summary>
    public bool TryTurnOff(string deviceName)
    {
        try
        {
            if (!TryFindTarget(deviceName, out var adapterId, out var id))
                return false;
            if (QueryState(adapterId, id) != HdrState.On)
                return false;
            return SetHdr(adapterId, id, enable: false);
        }
        catch { return false; }
    }

    /// <summary>Re-enable HDR on the monitor (used to restore after recording). Never throws.</summary>
    public void TurnOn(string deviceName)
    {
        try
        {
            if (TryFindTarget(deviceName, out var adapterId, out var id))
                SetHdr(adapterId, id, enable: true);
        }
        catch { /* best effort */ }
    }

    // ---- internals -----------------------------------------------------------

    private static HdrState QueryState(DisplayConfigNative.Luid adapterId, uint id)
    {
        // Prefer the 24H2+ type-15 call (its activeColorMode cleanly distinguishes
        // HDR from WCG); fall back to legacy type-9 on older builds.
        var info2 = new DisplayConfigNative.AdvancedColorInfo2
        {
            header = MakeHeader(DisplayConfigNative.GET_ADVANCED_COLOR_INFO_2,
                                Marshal.SizeOf<DisplayConfigNative.AdvancedColorInfo2>(), adapterId, id),
        };
        if (DisplayConfigNative.DisplayConfigGetDeviceInfo(ref info2) == 0)
        {
            bool supported = (info2.value & DisplayConfigNative.HDR_SUPPORTED_BIT) != 0;
            bool active    = info2.activeColorMode == DisplayConfigNative.ADVANCED_COLOR_MODE_HDR;
            return Decode(supported, active);
        }

        var info1 = new DisplayConfigNative.AdvancedColorInfo
        {
            header = MakeHeader(DisplayConfigNative.GET_ADVANCED_COLOR_INFO,
                                Marshal.SizeOf<DisplayConfigNative.AdvancedColorInfo>(), adapterId, id),
        };
        if (DisplayConfigNative.DisplayConfigGetDeviceInfo(ref info1) == 0)
        {
            bool supported = (info1.value & DisplayConfigNative.ADV_COLOR_SUPPORTED_BIT) != 0;
            bool active    = (info1.value & DisplayConfigNative.ADV_COLOR_ENABLED_BIT) != 0;
            return Decode(supported, active);
        }

        return HdrState.NotSupported;
    }

    private static bool SetHdr(DisplayConfigNative.Luid adapterId, uint id, bool enable)
    {
        // type-16 SET_HDR_STATE first (24H2+), then legacy type-10 SET_ADVANCED_COLOR_STATE.
        var set16 = new DisplayConfigNative.SetHdrStateInfo
        {
            header = MakeHeader(DisplayConfigNative.SET_HDR_STATE,
                                Marshal.SizeOf<DisplayConfigNative.SetHdrStateInfo>(), adapterId, id),
            value  = enable ? 1u : 0u,
        };
        if (DisplayConfigNative.DisplayConfigSetDeviceInfo(ref set16) == 0)
            return true;

        var set10 = new DisplayConfigNative.SetHdrStateInfo
        {
            header = MakeHeader(DisplayConfigNative.SET_ADVANCED_COLOR_STATE,
                                Marshal.SizeOf<DisplayConfigNative.SetHdrStateInfo>(), adapterId, id),
            value  = enable ? 1u : 0u,
        };
        return DisplayConfigNative.DisplayConfigSetDeviceInfo(ref set10) == 0;
    }

    /// <summary>Resolve a GDI device name (empty = primary) to its DisplayConfig target id.</summary>
    private static bool TryFindTarget(string deviceName, out DisplayConfigNative.Luid adapterId, out uint id)
    {
        adapterId = default;
        id = 0;

        var wanted = string.IsNullOrEmpty(deviceName)
            ? (Screen.PrimaryScreen?.DeviceName ?? "")
            : deviceName;
        if (string.IsNullOrEmpty(wanted))
            return false;

        if (DisplayConfigNative.GetDisplayConfigBufferSizes(
                DisplayConfigNative.QDC_ONLY_ACTIVE_PATHS, out uint numPaths, out uint numModes) != 0)
            return false;

        var paths = new DisplayConfigNative.PathInfo[numPaths];
        var modes = new DisplayConfigNative.ModeInfo[numModes];
        if (DisplayConfigNative.QueryDisplayConfig(
                DisplayConfigNative.QDC_ONLY_ACTIVE_PATHS,
                ref numPaths, paths, ref numModes, modes, IntPtr.Zero) != 0)
            return false;

        for (int i = 0; i < numPaths; i++)
        {
            var name = new DisplayConfigNative.SourceDeviceName
            {
                header = MakeHeader(DisplayConfigNative.GET_SOURCE_NAME,
                                    Marshal.SizeOf<DisplayConfigNative.SourceDeviceName>(),
                                    paths[i].src.adapterId, paths[i].src.id),
            };
            if (DisplayConfigNative.DisplayConfigGetDeviceInfo(ref name) != 0)
                continue;
            if (!string.Equals(name.gdiName, wanted, StringComparison.OrdinalIgnoreCase))
                continue;

            adapterId = paths[i].tgt.adapterId;
            id = paths[i].tgt.id;
            return true;
        }
        return false;
    }

    private static DisplayConfigNative.DeviceInfoHeader MakeHeader(
        uint type, int size, DisplayConfigNative.Luid adapterId, uint id)
        => new() { type = type, size = (uint)size, adapterId = adapterId, id = id };
}

/// <summary>
/// Raw Win32 DisplayConfig P/Invoke. Struct layouts are exact and load-bearing:
/// refreshRate is two UINT32s (DISPLAYCONFIG_RATIONAL), NOT a ulong — a ulong forces
/// 8-byte alignment that pads/mis-positions adapterId and yields ERROR_INVALID_PARAMETER.
/// Sizes are frozen by HdrDisplayTests.
/// </summary>
internal static class DisplayConfigNative
{
    public const uint QDC_ONLY_ACTIVE_PATHS = 2;

    public const uint GET_SOURCE_NAME             = 1;
    public const uint GET_ADVANCED_COLOR_INFO     = 9;   // legacy
    public const uint SET_ADVANCED_COLOR_STATE    = 10;  // legacy set
    public const uint GET_ADVANCED_COLOR_INFO_2   = 15;  // 24H2+
    public const uint SET_HDR_STATE               = 16;  // 24H2+

    public const uint ADVANCED_COLOR_MODE_HDR = 2;       // 0=SDR, 1=WCG, 2=HDR

    public const uint ADV_COLOR_SUPPORTED_BIT = 0x1;     // type-9 value bit0
    public const uint ADV_COLOR_ENABLED_BIT   = 0x2;     // type-9 value bit1
    public const uint HDR_SUPPORTED_BIT       = 0x10;    // type-15 value bit4 (highDynamicRangeSupported)

    [StructLayout(LayoutKind.Sequential)]
    public struct Luid { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceInfoHeader { public uint type; public uint size; public Luid adapterId; public uint id; }

    [StructLayout(LayoutKind.Sequential)]
    public struct SourceInfo { public Luid adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    public struct TargetInfo
    {
        public Luid adapterId; public uint id; public uint modeInfoIdx;
        public uint outputTechnology; public uint rotation; public uint scaling;
        public uint refreshNum; public uint refreshDen;   // DISPLAYCONFIG_RATIONAL — keep as TWO uints
        public uint scanLineOrdering; public int targetAvailable; public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PathInfo { public SourceInfo src; public TargetInfo tgt; public uint flags; }

    [StructLayout(LayoutKind.Sequential)]
    public struct ModeInfo { [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] data; }

    [StructLayout(LayoutKind.Sequential)]
    public struct AdvancedColorInfo   // type 9
    {
        public DeviceInfoHeader header; public uint value; public uint colorEncoding; public int bitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AdvancedColorInfo2  // type 15 — adds activeColorMode
    {
        public DeviceInfoHeader header; public uint value; public uint colorEncoding;
        public int bitsPerColorChannel; public uint activeColorMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SetHdrStateInfo { public DeviceInfoHeader header; public uint value; }  // type 16 / 10

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SourceDeviceName  // type 1
    {
        public DeviceInfoHeader header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string gdiName;
    }

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] PathInfo[] paths,
        ref uint numModes, [Out] ModeInfo[] modes, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref AdvancedColorInfo2 info);
    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref AdvancedColorInfo info);
    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref SourceDeviceName info);
    [DllImport("user32.dll")]
    public static extern int DisplayConfigSetDeviceInfo(ref SetHdrStateInfo info);
}
