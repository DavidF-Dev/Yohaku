using Xunit;
using Yohaku;
using static Yohaku.NativeMethods;

namespace Yohaku.Tests;

public class StripGeometryTests
{
    private static readonly RECT Monitor = new() { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };

    // ---- Scale ---------------------------------------------------------

    [Theory]
    [InlineData(12, 1.0, 12)]   // 100%
    [InlineData(12, 1.5, 18)]   // 150%
    [InlineData(12, 1.25, 15)]  // 125%
    [InlineData(10, 2.0, 20)]   // 200%
    [InlineData(0, 1.5, 0)]
    public void Scale_scales_and_rounds(int logical, double dpi, int expected) =>
        Assert.Equal(expected, StripGeometry.Scale(logical, dpi));

    [Fact]
    public void Scale_clamps_negative_dpi_to_zero() =>
        Assert.Equal(0, StripGeometry.Scale(12, -1.0));

    // ---- DesiredRect ---------------------------------------------------

    [Fact]
    public void DesiredRect_top_is_full_width_strip_at_top()
    {
        var r = StripGeometry.DesiredRect(ABE_TOP, Monitor, 12);
        Assert.Equal((0, 0, 1920, 12), (r.Left, r.Top, r.Right, r.Bottom));
    }

    [Fact]
    public void DesiredRect_bottom_is_full_width_strip_at_bottom()
    {
        var r = StripGeometry.DesiredRect(ABE_BOTTOM, Monitor, 12);
        Assert.Equal((0, 1068, 1920, 1080), (r.Left, r.Top, r.Right, r.Bottom));
    }

    [Fact]
    public void DesiredRect_left_is_full_height_strip_at_left()
    {
        var r = StripGeometry.DesiredRect(ABE_LEFT, Monitor, 12);
        Assert.Equal((0, 0, 12, 1080), (r.Left, r.Top, r.Right, r.Bottom));
    }

    [Fact]
    public void DesiredRect_right_is_full_height_strip_at_right()
    {
        var r = StripGeometry.DesiredRect(ABE_RIGHT, Monitor, 12);
        Assert.Equal((1908, 0, 1920, 1080), (r.Left, r.Top, r.Right, r.Bottom));
    }

    [Fact]
    public void DesiredRect_respects_non_zero_monitor_origin()
    {
        var secondary = new RECT { Left = -1920, Top = 0, Right = 0, Bottom = 1080 };
        var r = StripGeometry.DesiredRect(ABE_LEFT, secondary, 20);
        Assert.Equal((-1920, 0, -1900, 1080), (r.Left, r.Top, r.Right, r.Bottom));
    }

    // ---- PinThickness --------------------------------------------------
    // After ABM_QUERYPOS moves the strip only the outer edge is authoritative; PinThickness restores our exact thickness against it.

    [Fact]
    public void PinThickness_top_pins_bottom_to_top_plus_thickness()
    {
        var approved = new RECT { Left = 0, Top = 5, Right = 1920, Bottom = 999 };
        var r = StripGeometry.PinThickness(ABE_TOP, approved, 12);
        Assert.Equal(17, r.Bottom);
        Assert.Equal(5, r.Top);
    }

    [Fact]
    public void PinThickness_bottom_pins_top_to_bottom_minus_thickness()
    {
        var approved = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        var r = StripGeometry.PinThickness(ABE_BOTTOM, approved, 12);
        Assert.Equal(1068, r.Top);
        Assert.Equal(1080, r.Bottom);
    }

    [Fact]
    public void PinThickness_left_pins_right_to_left_plus_thickness()
    {
        var approved = new RECT { Left = 0, Top = 0, Right = 999, Bottom = 1080 };
        var r = StripGeometry.PinThickness(ABE_LEFT, approved, 12);
        Assert.Equal(12, r.Right);
    }

    [Fact]
    public void PinThickness_right_pins_left_to_right_minus_thickness()
    {
        var approved = new RECT { Left = 1, Top = 0, Right = 1920, Bottom = 1080 };
        var r = StripGeometry.PinThickness(ABE_RIGHT, approved, 12);
        Assert.Equal(1908, r.Left);
    }
}
