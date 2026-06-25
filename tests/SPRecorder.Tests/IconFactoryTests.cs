using System.Drawing;
using SPRecorder.Tray;

namespace SPRecorder.Tests;

public class IconFactoryTests
{
    [Fact]
    public void CreateCircleWithBadge_ReturnsUsableIcon()
    {
        using var icon = IconFactory.CreateCircleWithBadge(Color.Gray, Color.FromArgb(255, 193, 7));
        Assert.NotNull(icon);
        Assert.True(icon.Width > 0 && icon.Height > 0);
    }
}
