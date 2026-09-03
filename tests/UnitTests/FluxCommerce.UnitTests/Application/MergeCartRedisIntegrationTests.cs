using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Cart.Application.Commands.AddAuthenticatedCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.AddGuestCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.CreateAuthenticatedCart;
using FluxCommerce.Modules.Cart.Application.Commands.CreateGuestCart;
using FluxCommerce.Modules.Cart.Application.Commands.MergeCart;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Enums;
using FluxCommerce.Modules.Cart.Application.Models;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

/// <summary>
/// Phase 3.6.2: Guest â†’ Authenticated merge against real Redis. Verifies source/target ownership
/// validation, add-missing / sum-duplicate / no-duplicate-item semantics, implicit target creation,
/// quantity invariants, atomic persistence AND Guest source consumption through
/// ICartStore.MergeAndConsumeAsync, logical idempotency (a consumed Guest Cart can never be
/// re-merged), and that a failed merge never consumes the source.
/// </summary>
[Collection(RedisCartTestCollection.Name)]
public class MergeCartRedisIntegrationTests
{
    private readonly RedisCartFixture _fixture;

    public MergeCartRedisIntegrationTests(RedisCartFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Merge_ExistingAuthenticatedCart_CombinesItems_AndConsumesGuestCart()
    {
        var store = _fixture.CreateStore();
        var guestCartId = await CreateGuestCartWithItemAsync(store);
        var userId = Guid.NewGuid();
        var currentUser = CreateUser(userId);
        var create = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);
        var add = new AddAuthenticatedCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser, FakeCatalogVariantCartLookup.Available());
        var created = await create.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        _fixture.Track(created.Value!);
        var targetCartId = created.Value!.CartId;
        var guestItem = (await store.GetAsync(guestCartId))!.Items[0];
        await add.HandleAsync(new AddAuthenticatedCartItemCommand(
            guestItem.VariantId.Value, guestItem.UnitPrice.Amount, guestItem.UnitPrice.Currency, 3),
            CancellationToken.None);

        var before = (await store.GetAsync(targetCartId))!;
        var merge = new MergeCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser, FakeCatalogVariantCartLookup.Available());
        var result = await merge.HandleAsync(new MergeCartCommand(guestCartId.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var merged = result.Value!;
        Assert.Equal(userId, merged.UserId);
        Assert.Equal(targetCartId, merged.CartId);
        Assert.Equal(before.Version + 1, merged.Version);
        var mergedItem = Assert.Single(merged.Items);
        Assert.Equal(guestItem.VariantId, mergedItem.VariantId);
        Assert.Equal(5, mergedItem.Quantity); // 3 + 2 â†’ Phase 3 duplicate rule.

        // The merge is persisted, not just returned.
        var persisted = (await store.GetAsync(targetCartId))!;
        Assert.Equal(merged.Version, persisted.Version);
        Assert.Equal(5, persisted.Items.Single().Quantity);

        // The Guest source is consumed atomically with the target persist.
        Assert.Null(await store.GetAsync(guestCartId));
    }

    [Fact]
    public async Task Merge_NoAuthenticatedCart_ImplicitlyCreatesTarget_WithGuestItems()
    {
        var store = _fixture.CreateStore();
        var guestCartId = await CreateGuestCartWithItemAsync(store);
        var userId = Guid.NewGuid();
        var currentUser = CreateUser(userId);
        var merge = new MergeCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser, FakeCatalogVariantCartLookup.Available());

        var result = await merge.HandleAsync(new MergeCartCommand(guestCartId.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var merged = result.Value!;
        Assert.Equal(userId, merged.UserId);
        Assert.Single(merged.Items);
        Assert.Equal(2, merged.Items[0].Quantity);
        Assert.Equal(2, merged.Version);

        // The new authenticated Cart is reachable through the user index and persisted.
        var viaIndex = (await store.GetByUserIdAsync(userId))!;
        Assert.NotNull(viaIndex);
        Assert.Equal(merged.CartId, viaIndex.CartId);
        Assert.Equal(merged.Version, viaIndex.Version);
        _fixture.Track(viaIndex);

        // The Guest source is consumed.
        Assert.Null(await store.GetAsync(guestCartId));
    }

    [Fact]
    public async Task Merge_MultipleItems_CombinesAllGuestItems_WithNoDuplicates()
    {
        var store = _fixture.CreateStore();
        var guestCartId = await CreateGuestCartWithItemAsync(store);
        var guestItemA = (await store.GetAsync(guestCartId))!.Items.Single();
        var addGuest = new AddGuestCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), FakeCatalogVariantCartLookup.Available());
        var second = await addGuest.HandleAsync(new AddGuestCartItemCommand(
            guestCartId.Value, Guid.NewGuid(), 79.99m, "USD", 1),
            CancellationToken.None);
        Assert.True(second.IsSuccess);
        var guestItems = (await store.GetAsync(guestCartId))!.Items;
        Assert.Equal(2, guestItems.Count);
        var guestItemB = guestItems.Single(i => i.VariantId != guestItemA.VariantId);

        var userId = Guid.NewGuid();
        var currentUser = CreateUser(userId);
        var create = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);
        var created = await create.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        _fixture.Track(created.Value!);
        var add = new AddAuthenticatedCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser, FakeCatalogVariantCartLookup.Available());

        // The user Cart already holds the same variant A (3) plus an unrelated variant C (4).
        await add.HandleAsync(new AddAuthenticatedCartItemCommand(
            guestItemA.VariantId.Value, guestItemA.UnitPrice.Amount, guestItemA.UnitPrice.Currency, 3),
            CancellationToken.None);
        var variantC = new CartItemState(
            new ProductId(Guid.NewGuid()),
            new VariantId(Guid.NewGuid()),
            new Sku("SKU-3003"),
            "USB-C Cable",
            new Money(9.99m, "USD"),
            4);
        await add.HandleAsync(new AddAuthenticatedCartItemCommand(
            variantC.VariantId.Value, variantC.UnitPrice.Amount, variantC.UnitPrice.Currency, variantC.Quantity),
            CancellationToken.None);

        var merge = new MergeCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser, FakeCatalogVariantCartLookup.Available());
        var result = await merge.HandleAsync(new MergeCartCommand(guestCartId.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var merged = result.Value!;
        Assert.Equal(3, merged.Items.Count);
        Assert.Equal(
            new[] { guestItemA.VariantId, guestItemB.VariantId, variantC.VariantId }.ToHashSet(),
            merged.Items.Select(i => i.VariantId).ToHashSet());
        // The duplicate variant A (2 + 3 = 5) must be a single Cart item, not two entries.
        Assert.Equal(1, merged.Items.Count(i => i.VariantId == guestItemA.VariantId));
        Assert.Equal(5, merged.Items.Single(i => i.VariantId == guestItemA.VariantId).Quantity);
        Assert.Equal(1, merged.Items.Single(i => i.VariantId == guestItemB.VariantId).Quantity);
        Assert.Equal(4, merged.Items.Single(i => i.VariantId == variantC.VariantId).Quantity);
        Assert.Null(await store.GetAsync(guestCartId));
    }

    [Fact]
    public async Task Merge_AlreadyConsumedGuestCart_ReturnsCartNotFound_AndDoesNotDuplicate()
    {
        var store = _fixture.CreateStore();
        var guestCartId = await CreateGuestCartWithItemAsync(store);
        var userId = Guid.NewGuid();
        var currentUser = CreateUser(userId);
        var merge = new MergeCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser, FakeCatalogVariantCartLookup.Available());

        var first = await merge.HandleAsync(new MergeCartCommand(guestCartId.Value), CancellationToken.None);
        Assert.True(first.IsSuccess);
        var targetAfterFirst = (await store.GetByUserIdAsync(userId))!;
        _fixture.Track(targetAfterFirst);
        Assert.Equal(2, targetAfterFirst.Items.Single().Quantity);

        // Retrying the same merge resolves the consumed Guest Cart as missing: logical idempotency.
        var retry = await merge.HandleAsync(new MergeCartCommand(guestCartId.Value), CancellationToken.None);

        Assert.True(retry.IsFailure);
        Assert.Equal(ErrorType.NotFound, retry.Error!.Type);
        Assert.Equal("Cart.CartNotFound", retry.Error.Code);
        var targetAfterRetry = (await store.GetByUserIdAsync(userId))!;
        Assert.Equal(2, targetAfterRetry.Items.Single().Quantity);
        Assert.Equal(targetAfterFirst.Version, targetAfterRetry.Version);
    }

    [Fact]
    public async Task Merge_AfterConsumption_NewGuestCartIsIndependent()
    {
        var store = _fixture.CreateStore();
        var firstGuestCartId = await CreateGuestCartWithItemAsync(store);
        var firstGuestItem = (await store.GetAsync(firstGuestCartId))!.Items.Single();
        var userId = Guid.NewGuid();
        var currentUser = CreateUser(userId);
        var merge = new MergeCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser, FakeCatalogVariantCartLookup.Available());

        var first = await merge.HandleAsync(new MergeCartCommand(firstGuestCartId.Value), CancellationToken.None);
        Assert.True(first.IsSuccess);
        var targetCartId = first.Value!.CartId;
        Assert.Null(await store.GetAsync(firstGuestCartId));

        // A brand new Guest Cart is created and merged independently of the consumed one.
        var secondGuestCartId = await CreateGuestCartWithItemAsync(store);
        var secondGuestItem = (await store.GetAsync(secondGuestCartId))!.Items.Single();
        Assert.NotEqual(firstGuestCartId, secondGuestCartId);
        Assert.NotEqual(firstGuestItem.VariantId, secondGuestItem.VariantId);
        var second = await merge.HandleAsync(new MergeCartCommand(secondGuestCartId.Value), CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(targetCartId, second.Value!.CartId);
        // The first merge's items are preserved and the second Guest Cart's item is added.
        Assert.Equal(2, second.Value!.Items.Count);
        Assert.Equal(2, second.Value!.Items.Single(i => i.VariantId == firstGuestItem.VariantId).Quantity);
        Assert.Equal(2, second.Value!.Items.Single(i => i.VariantId == secondGuestItem.VariantId).Quantity);
        Assert.Null(await store.GetAsync(firstGuestCartId));
        Assert.Null(await store.GetAsync(secondGuestCartId));
    }

    [Fact]
    public async Task Merge_MissingGuestCart_ReturnsCartNotFound_AndDoesNotCreateTarget()
    {
        var store = _fixture.CreateStore();
        var userId = Guid.NewGuid();
        var currentUser = CreateUser(userId);
        var merge = new MergeCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser, FakeCatalogVariantCartLookup.Available());

        var result = await merge.HandleAsync(new MergeCartCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("Cart.CartNotFound", result.Error.Code);
        Assert.Null(await store.GetByUserIdAsync(userId));
    }

    [Fact]
    public async Task Merge_AuthenticatedCartAsSource_ReturnsAccessDenied_AndDoesNothing()
    {
        var store = _fixture.CreateStore();
        var sourceUserId = Guid.NewGuid();
        var sourceUser = CreateUser(sourceUserId);
        var create = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), sourceUser);
        var add = new AddAuthenticatedCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), sourceUser, FakeCatalogVariantCartLookup.Available());
        var created = await create.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        _fixture.Track(created.Value!);
        var sourceCartId = created.Value!.CartId;
        await add.HandleAsync(new AddAuthenticatedCartItemCommand(
            Guid.NewGuid(), 10m, "USD", 1), CancellationToken.None);

        var mergeUserId = Guid.NewGuid();
        var merge = new MergeCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), CreateUser(mergeUserId), FakeCatalogVariantCartLookup.Available());
        var result = await merge.HandleAsync(new MergeCartCommand(sourceCartId.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartAccessDenied", result.Error!.Code);
        Assert.Null(await store.GetByUserIdAsync(mergeUserId));
        Assert.NotNull(await store.GetAsync(sourceCartId));
    }

    [Fact]
    public async Task Merge_EmptyGuestCart_ReturnsTargetUnchanged_AndConsumesEmptyGuestCart()
    {
        var store = _fixture.CreateStore();
        var createGuest = new CreateGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var guestCreated = await createGuest.HandleAsync(new CreateGuestCartCommand(), CancellationToken.None);
        Assert.True(guestCreated.IsSuccess);
        _fixture.Track(guestCreated.Value!);
        var guestCartId = guestCreated.Value!.CartId;

        var userId = Guid.NewGuid();
        var currentUser = CreateUser(userId);
        var create = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);
        var created = await create.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        _fixture.Track(created.Value!);
        var targetCartId = created.Value!.CartId;
        var before = (await store.GetAsync(targetCartId))!;

        var merge = new MergeCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser, FakeCatalogVariantCartLookup.Available());
        var result = await merge.HandleAsync(new MergeCartCommand(guestCartId.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        RedisCartTestData.AssertCartStateEqual(before, result.Value);
        Assert.Null(await store.GetAsync(guestCartId));
    }

    [Fact]
    public async Task Merge_QuantityOverflow_ReturnsQuantityLimitExceeded_AndDoesNotPersistOrConsume()
    {
        var store = _fixture.CreateStore();
        var guestCartId = await CreateGuestCartWithItemAsync(store);
        var userId = Guid.NewGuid();
        var currentUser = CreateUser(userId);
        var create = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);
        var created = await create.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        _fixture.Track(created.Value!);
        var targetBefore = created.Value!;
        var guestItem = (await store.GetAsync(guestCartId))!.Items[0];

        // Seed the target with a near-int.MaxValue quantity for the same variant directly through
        // the store, so the Domain Merge overflow invariant triggers (AddItem caps at 999).
        var seeded = new CartState(
            targetBefore.CartId,
            targetBefore.UserId,
            targetBefore.Version + 1,
            targetBefore.CreatedAtUtc,
            targetBefore.UpdatedAtUtc,
            [new CartItemState(
                guestItem.ProductId, guestItem.VariantId, guestItem.Sku, guestItem.ProductName,
                guestItem.UnitPrice, int.MaxValue - 1)]);
        var seed = await store.SaveAsync(seeded, expectedVersion: targetBefore.Version);
        Assert.Equal(CartSaveStatus.Success, seed.Status);

        var merge = new MergeCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser, FakeCatalogVariantCartLookup.Available());
        var result = await merge.HandleAsync(new MergeCartCommand(guestCartId.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("Cart.QuantityLimitExceeded", result.Error.Code);

        // A failed merge must not mutate the target nor consume the source.
        RedisCartTestData.AssertCartStateEqual(seeded, await store.GetAsync(targetBefore.CartId));
        Assert.NotNull(await store.GetAsync(guestCartId));
    }

    [Fact]
    public async Task Merge_SumAtIntMax_QuantitiesSucceed_AndGuestCartIsConsumed()
    {
        var store = _fixture.CreateStore();
        var guestCartId = await CreateGuestCartWithItemAsync(store);
        var userId = Guid.NewGuid();
        var currentUser = CreateUser(userId);
        var create = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);
        var created = await create.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        _fixture.Track(created.Value!);
        var targetBefore = created.Value!;
        var guestItem = (await store.GetAsync(guestCartId))!.Items[0];

        // Seed the target so the summed quantity is exactly int.MaxValue (the Domain Merge
        // boundary: the pre-check rejects only sums strictly greater than int.MaxValue).
        var seeded = new CartState(
            targetBefore.CartId,
            targetBefore.UserId,
            targetBefore.Version + 1,
            targetBefore.CreatedAtUtc,
            targetBefore.UpdatedAtUtc,
            [new CartItemState(
                guestItem.ProductId, guestItem.VariantId, guestItem.Sku, guestItem.ProductName,
                guestItem.UnitPrice, int.MaxValue - 2)]);
        var seed = await store.SaveAsync(seeded, expectedVersion: targetBefore.Version);
        Assert.Equal(CartSaveStatus.Success, seed.Status);

        var merge = new MergeCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser, FakeCatalogVariantCartLookup.Available());
        var result = await merge.HandleAsync(new MergeCartCommand(guestCartId.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(int.MaxValue, result.Value!.Items.Single().Quantity);
        Assert.Null(await store.GetAsync(guestCartId));
    }

    [Fact]
    public async Task MergeAndConsume_VersionConflict_DoesNotPersistTarget_AndDoesNotConsumeSource()
    {
        var store = _fixture.CreateStore();
        var guestCartId = await CreateGuestCartWithItemAsync(store);
        var userId = Guid.NewGuid();
        var currentUser = CreateUser(userId);
        var create = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);
        var created = await create.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        _fixture.Track(created.Value!);
        var targetBefore = (await store.GetByUserIdAsync(userId))!;

        // A stale expected version must fail the atomic merge-and-consume without writing the
        // target and without consuming the Guest source.
        var merge = await store.MergeAndConsumeAsync(
            targetBefore with { Version = targetBefore.Version + 1 },
            expectedVersion: targetBefore.Version + 5,
            expectedSourceVersion: 1,
            guestCartId,
            CancellationToken.None);

        Assert.Equal(CartSaveStatus.VersionConflict, merge.Status);
        RedisCartTestData.AssertCartStateEqual(targetBefore, await store.GetAsync(targetBefore.CartId));
        Assert.NotNull(await store.GetAsync(guestCartId));
    }

    [Fact]
    public async Task Merge_TargetBelongsToCurrentUser_NotAnotherUsersCart()
    {
        var store = _fixture.CreateStore();
        var guestCartId = await CreateGuestCartWithItemAsync(store);
        var otherUserId = Guid.NewGuid();
        var otherUser = CreateUser(otherUserId);
        var otherCreate = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), otherUser);
        var otherCreated = await otherCreate.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(otherCreated.IsSuccess);
        _fixture.Track(otherCreated.Value!);
        var otherCartId = otherCreated.Value!.CartId;
        var otherAdd = new AddAuthenticatedCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), otherUser, FakeCatalogVariantCartLookup.Available());
        var otherAdded = await otherAdd.HandleAsync(
            new AddAuthenticatedCartItemCommand(Guid.NewGuid(), 7m, "USD", 1),
            CancellationToken.None);
        Assert.True(otherAdded.IsSuccess);

        // A different authenticated user merges the Guest Cart.
        var mergeUserId = Guid.NewGuid();
        var merge = new MergeCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), CreateUser(mergeUserId), FakeCatalogVariantCartLookup.Available());
        var result = await merge.HandleAsync(new MergeCartCommand(guestCartId.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(mergeUserId, result.Value!.UserId);
        Assert.NotEqual(otherCartId, result.Value.CartId);

        // The other user's Cart is untouched and the Guest source is consumed by this merge.
        Assert.Single((await store.GetAsync(otherCartId))!.Items);
        Assert.Null(await store.GetAsync(guestCartId));
    }

    [Fact]
    public async Task Merge_ConcurrentMerges_SameGuestCart_GuestQuantitiesAppliedExactlyOnce()
    {
        // Regression for the concurrent double-apply race: the atomic merge script must verify the
        // Guest source is still present and unchanged at commit time, so a merge that read the
        // source before the winner consumed it can never commit against a target that already
        // contains the same Guest quantities (AÃ—3 target + 7 Ã— AÃ—2 guest would otherwise be AÃ—17).
        var store = _fixture.CreateStore();
        var guestCartId = await CreateGuestCartWithItemAsync(store);
        var guestItem = (await store.GetAsync(guestCartId))!.Items[0];
        var userId = Guid.NewGuid();
        var currentUser = CreateUser(userId);
        var create = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);
        var created = await create.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        _fixture.Track(created.Value!);
        var add = new AddAuthenticatedCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser, FakeCatalogVariantCartLookup.Available());
        var seeded = await add.HandleAsync(new AddAuthenticatedCartItemCommand(
            guestItem.VariantId.Value, guestItem.UnitPrice.Amount, guestItem.UnitPrice.Currency, 3),
            CancellationToken.None);
        Assert.True(seeded.IsSuccess);

        // 7 concurrent merges of the same Guest Cart against the same target. Exactly one can
        // commit: the atomic script re-verifies the target version AND the Guest source
        // presence/version, so the Guest quantities are applied exactly once.
        var writers = Enumerable
            .Range(0, 7)
            .Select(_ => new MergeCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser, FakeCatalogVariantCartLookup.Available()));
        var results = await Task.WhenAll(
            writers.Select(writer => writer.HandleAsync(new MergeCartCommand(guestCartId.Value), CancellationToken.None)));

        Assert.Equal(1, results.Count(r => r.IsSuccess));
        Assert.Equal(6, results.Count(r => r.IsFailure));
        Assert.All(
            results.Where(r => r.IsFailure),
            r => Assert.Contains(
                r.Error!.Code,
                new[] { "Cart.CartConcurrencyConflict", "Cart.CartNotFound" }));

        // The Guest quantities are applied exactly once: A(3) + A(2) = A(5), never A(3) + 7 Ã— A(2).
        var persisted = (await store.GetByUserIdAsync(userId))!;
        _fixture.Track(persisted);
        Assert.Single(persisted.Items);
        Assert.Equal(5, persisted.Items.Single().Quantity);
        Assert.Equal(3, persisted.Version);
        Assert.Null(await store.GetAsync(guestCartId));
    }

    [Fact]
    public async Task MergeAndConsume_StaleSourceVersion_Fails_WithoutPersistingOrConsuming()
    {
        var store = _fixture.CreateStore();
        var guestCartId = await CreateGuestCartWithItemAsync(store);
        var userId = Guid.NewGuid();
        var currentUser = CreateUser(userId);
        var create = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);
        var created = await create.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        _fixture.Track(created.Value!);
        var targetBefore = (await store.GetByUserIdAsync(userId))!;
        var guestBefore = (await store.GetAsync(guestCartId))!;

        // The Guest Cart is mutated AFTER the expected source version was captured, which is the
        // persistence-level shape of a concurrent guest mutation during the merge.
        var addGuest = new AddGuestCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), FakeCatalogVariantCartLookup.Available());
        var mutated = await addGuest.HandleAsync(new AddGuestCartItemCommand(
            guestCartId.Value, Guid.NewGuid(), 5m, "USD", 1),
            CancellationToken.None);
        Assert.True(mutated.IsSuccess);
        Assert.NotEqual(guestBefore.Version, (await store.GetAsync(guestCartId))!.Version);

        // A stale expected source version must fail the atomic merge-and-consume without writing the
        // target and without consuming the (now changed) Guest source.
        var merge = await store.MergeAndConsumeAsync(
            targetBefore with { Version = targetBefore.Version + 1 },
            expectedVersion: targetBefore.Version,
            expectedSourceVersion: guestBefore.Version,
            guestCartId,
            CancellationToken.None);

        Assert.Equal(CartSaveStatus.VersionConflict, merge.Status);
        RedisCartTestData.AssertCartStateEqual(targetBefore, await store.GetAsync(targetBefore.CartId));
        Assert.NotNull(await store.GetAsync(guestCartId));
    }

    [Fact]
    public async Task MergeAndConsume_ReplayAfterConsumption_FailsWithoutReapplyingGuestQuantities()
    {
        var store = _fixture.CreateStore();
        var guestCartId = await CreateGuestCartWithItemAsync(store);
        var userId = Guid.NewGuid();
        var currentUser = CreateUser(userId);
        var create = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);
        var created = await create.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        _fixture.Track(created.Value!);
        var target = (await store.GetByUserIdAsync(userId))!;
        var guestVersion = (await store.GetAsync(guestCartId))!.Version;

        var first = await store.MergeAndConsumeAsync(
            target with { Version = target.Version + 1 },
            expectedVersion: target.Version,
            expectedSourceVersion: guestVersion,
            guestCartId,
            CancellationToken.None);
        Assert.Equal(CartSaveStatus.Success, first.Status);
        var consumedTarget = (await store.GetByUserIdAsync(userId))!;

        // A concurrent attempt that read the source before consumption replays the atomic script
        // against the consumed source: it must fail without writing the target again.
        var replay = await store.MergeAndConsumeAsync(
            consumedTarget with { Version = consumedTarget.Version + 1 },
            expectedVersion: consumedTarget.Version,
            expectedSourceVersion: guestVersion,
            guestCartId,
            CancellationToken.None);

        Assert.Equal(CartSaveStatus.VersionConflict, replay.Status);
        RedisCartTestData.AssertCartStateEqual(consumedTarget, await store.GetByUserIdAsync(userId));
        Assert.Null(await store.GetAsync(guestCartId));
    }

    [Fact]
    public async Task Merge_Unauthenticated_ReturnsAuthenticationRequired_AndDoesNothing()
    {
        var store = _fixture.CreateStore();
        var guestCartId = await CreateGuestCartWithItemAsync(store);
        var merge = new MergeCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), CreateUser(null), FakeCatalogVariantCartLookup.Available());

        var result = await merge.HandleAsync(new MergeCartCommand(guestCartId.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error!.Type);
        Assert.Equal("Cart.AuthenticationRequired", result.Error.Code);
        Assert.NotNull(await store.GetAsync(guestCartId));
    }

    private async Task<CartId> CreateGuestCartWithItemAsync(ICartStore store)
    {
        var create = new CreateGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var created = await create.HandleAsync(new CreateGuestCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        _fixture.Track(created.Value!);
        var cartId = created.Value!.CartId;

        var add = new AddGuestCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), FakeCatalogVariantCartLookup.Available());
        var added = await add.HandleAsync(new AddGuestCartItemCommand(
            cartId.Value, Guid.NewGuid(), 24.99m, "USD", 2),
            CancellationToken.None);
        Assert.True(added.IsSuccess);

        return cartId;
    }

    private static FakeCurrentUser CreateUser(Guid? userId) =>
        new() { UserId = userId, IsAuthenticated = userId is not null };
}

