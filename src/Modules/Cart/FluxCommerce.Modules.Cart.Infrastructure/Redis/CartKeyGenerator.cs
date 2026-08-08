using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.Modules.Cart.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace FluxCommerce.Modules.Cart.Infrastructure.Redis;

public sealed class CartKeyGenerator
{
    private const string UserKeyPart = "user";

    private readonly string _keyNamespace;

    public CartKeyGenerator(IOptions<CartOptions> options)
    {
        _keyNamespace = options.Value.KeyNamespace;
    }

    public string ForCart(CartId cartId) => $"{_keyNamespace}:{cartId.Value:D}";

    public string ForUser(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId must not be an empty GUID.", nameof(userId));

        return $"{_keyNamespace}:{UserKeyPart}:{userId:D}";
    }
}
