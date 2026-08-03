using FluxCommerce.BuildingBlocks.Localization.Abstractions;

namespace FluxCommerce.UnitTests.TestInfrastructure;

public sealed class FakeLocalizer : ILocalizer
{
    public string this[string key] => key;

    public string Get(string key, params object[] arguments) => key;
}