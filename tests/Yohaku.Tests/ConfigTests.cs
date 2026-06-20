using Xunit;
using Yohaku;

namespace Yohaku.Tests;

public class ConfigTests
{
    [Fact]
    public void Defaults_are_twelve_on_every_edge()
    {
        var cfg = new Config();
        Assert.Equal(12, cfg.InsetTop);
        Assert.Equal(12, cfg.InsetRight);
        Assert.Equal(12, cfg.InsetBottom);
        Assert.Equal(12, cfg.InsetLeft);
    }

    [Fact]
    public void Round_trips_through_json()
    {
        var original = new Config { InsetTop = 5, InsetRight = 10, InsetBottom = 15, InsetLeft = 20 };
        var restored = Config.Deserialize(Config.Serialize(original));

        Assert.NotNull(restored);
        Assert.Equal(5, restored!.InsetTop);
        Assert.Equal(10, restored.InsetRight);
        Assert.Equal(15, restored.InsetBottom);
        Assert.Equal(20, restored.InsetLeft);
    }

    [Fact]
    public void Missing_fields_fall_back_to_defaults()
    {
        var cfg = Config.Deserialize("{}");
        Assert.NotNull(cfg);
        Assert.Equal(12, cfg!.InsetTop);
        Assert.Equal(12, cfg.InsetLeft);
    }

    [Fact]
    public void Reads_explicit_values()
    {
        var cfg = Config.Deserialize("{\"InsetTop\":30,\"InsetRight\":30,\"InsetBottom\":30,\"InsetLeft\":30}");
        Assert.NotNull(cfg);
        Assert.Equal(30, cfg!.InsetTop);
        Assert.Equal(30, cfg.InsetRight);
        Assert.Equal(30, cfg.InsetBottom);
        Assert.Equal(30, cfg.InsetLeft);
    }
}
