using System.Runtime.InteropServices;
using SPRecorder.Recording;

namespace SPRecorder.Tests;

public class HdrDisplayTests
{
    // ---- Decode: map (hdrSupported, hdrActive) -> HdrState -------------------
    // hdrActive comes from type-15 activeColorMode==HDR, or type-9 advancedColorEnabled.

    [Fact]
    public void Decode_NotSupported_WhenDisplayLacksHdr()
        => Assert.Equal(HdrState.NotSupported, HdrDisplay.Decode(supported: false, active: false));

    [Fact]
    public void Decode_NotSupported_EvenIfActiveBitSet_WhenUnsupported()
        => Assert.Equal(HdrState.NotSupported, HdrDisplay.Decode(supported: false, active: true));

    [Fact]
    public void Decode_On_WhenSupportedAndActive()
        => Assert.Equal(HdrState.On, HdrDisplay.Decode(supported: true, active: true));

    [Fact]
    public void Decode_Off_WhenSupportedButInactive()
        => Assert.Equal(HdrState.Off, HdrDisplay.Decode(supported: true, active: false));

    // ---- Struct-layout regression --------------------------------------------
    // These sizes were empirically verified to make DisplayConfigGetDeviceInfo
    // succeed on Windows 11 build 26200. The most likely regression is declaring
    // the DISPLAYCONFIG_RATIONAL refreshRate as a single ulong, which forces
    // 8-byte alignment and pads/mis-positions adapterId -> ERROR_INVALID_PARAMETER.
    // Freezing the sizes here catches exactly that.

    [Fact]
    public void Header_Is20Bytes()
        => Assert.Equal(20, Marshal.SizeOf<DisplayConfigNative.DeviceInfoHeader>());

    [Fact]
    public void PathInfo_Is72Bytes()
        => Assert.Equal(72, Marshal.SizeOf<DisplayConfigNative.PathInfo>());

    [Fact]
    public void ModeInfo_Is64Bytes()
        => Assert.Equal(64, Marshal.SizeOf<DisplayConfigNative.ModeInfo>());

    [Fact]
    public void AdvancedColorInfo2_Is36Bytes()   // type 15, GET_ADVANCED_COLOR_INFO_2
        => Assert.Equal(36, Marshal.SizeOf<DisplayConfigNative.AdvancedColorInfo2>());

    [Fact]
    public void AdvancedColorInfo_Is32Bytes()    // type 9, legacy GET_ADVANCED_COLOR_INFO
        => Assert.Equal(32, Marshal.SizeOf<DisplayConfigNative.AdvancedColorInfo>());

    [Fact]
    public void SetHdrState_Is24Bytes()          // type 16, SET_HDR_STATE
        => Assert.Equal(24, Marshal.SizeOf<DisplayConfigNative.SetHdrStateInfo>());

    [Fact]
    public void SourceDeviceName_Is84Bytes()     // type 1, GET_SOURCE_NAME
        => Assert.Equal(84, Marshal.SizeOf<DisplayConfigNative.SourceDeviceName>());
}
