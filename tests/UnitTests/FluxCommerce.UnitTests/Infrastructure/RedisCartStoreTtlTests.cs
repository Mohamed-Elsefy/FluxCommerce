using FluxCommerce.Modules.Cart.Infrastructure.Configuration;
using FluxCommerce.Modules.Cart.Infrastructure.Redis;
using Xunit;

namespace FluxCommerce.UnitTests.Infrastructure;

public class RedisCartStoreTtlTests
{
    [Fact]
    public void GetExpiry_ReturnsConfiguredTtl()
    {
        var options = new CartOptions { KeyNamespace = "cart", DefaultTtlMinutes = 30 };

        var expiry = RedisCartStore.GetExpiry(options);

        Assert.Equal(TimeSpan.FromMinutes(30), expiry);
    }

    [Fact]
    public void GetExpiry_ReturnsDefaultTtl()
    {
        var options = new CartOptions();

        var expiry = RedisCartStore.GetExpiry(options);

        Assert.Equal(TimeSpan.FromMinutes(7 * 24 * 60), expiry);
    }

    [Fact]
    public void GetExpiry_OneMinute_IsValid()
    {
        var options = new CartOptions { KeyNamespace = "cart", DefaultTtlMinutes = 1 };

        var expiry = RedisCartStore.GetExpiry(options);

        Assert.Equal(TimeSpan.FromMinutes(1), expiry);
    }

    [Fact]
    public void GetExpiry_ZeroTtl_Throws()
    {
        var options = new CartOptions { KeyNamespace = "cart", DefaultTtlMinutes = 0 };

        Assert.Throws<ArgumentException>(() => RedisCartStore.GetExpiry(options));
    }

    [Fact]
    public void GetExpiry_NegativeTtl_Throws()
    {
        var options = new CartOptions { KeyNamespace = "cart", DefaultTtlMinutes = -1 };

        Assert.Throws<ArgumentException>(() => RedisCartStore.GetExpiry(options));
    }

    [Fact]
    public void GetExpiry_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RedisCartStore.GetExpiry(null!));
    }
}
