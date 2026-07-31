namespace FluxCommerce.BuildingBlocks.Domain.Entities;

public abstract class Entity<TKey> : IEquatable<Entity<TKey>>
    where TKey : notnull
{
    public TKey Id { get; protected init; }

    protected Entity(TKey id) => Id = id;

    #pragma warning disable CS8618
    protected Entity() { }
    #pragma warning restore CS8618

    public override bool Equals(object? obj) =>
        obj is Entity<TKey> other && EqualityComparer<TKey>.Default.Equals(Id, other.Id);

    public bool Equals(Entity<TKey>? other) =>
        other is not null && EqualityComparer<TKey>.Default.Equals(Id, other.Id);

    public override int GetHashCode() => Id.GetHashCode();

    public static bool operator ==(Entity<TKey>? left, Entity<TKey>? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(Entity<TKey>? left, Entity<TKey>? right) =>
        !(left == right);
}
