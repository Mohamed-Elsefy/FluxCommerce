using FluxCommerce.BuildingBlocks.Infrastructure.Caching;
using Xunit;

namespace FluxCommerce.UnitTests.Infrastructure;

public class RedisConnectionProviderTests
{
    [Fact]
    public void CreateConfigurationOptions_SetsAbortOnConnectFailFalse()
    {
        var options = RedisConnectionProvider.CreateConfigurationOptions("localhost:6379");

        Assert.False(options.AbortOnConnectFail);
    }

    [Fact]
    public void CreateConfigurationOptions_PreservesProvidedConnectionSettings()
    {
        var options = RedisConnectionProvider.CreateConfigurationOptions(
            "localhost:6380,password=secret,ssl=true,defaultDatabase=2,abortConnect=true");

        Assert.False(options.AbortOnConnectFail);
        Assert.Equal("secret", options.Password);
        Assert.True(options.Ssl);
        Assert.Equal(2, options.DefaultDatabase);
        Assert.Single(options.EndPoints);
        Assert.Contains("6380", options.EndPoints[0].ToString());
    }

    [Fact]
    public void GetConnection_DoesNotThrow_WhenRedisIsUnavailable()
    {
        using var provider = new RedisConnectionProvider("127.0.0.1:6399,connectTimeout=200");

        var connection = provider.GetConnection();

        Assert.NotNull(connection);
    }
}
