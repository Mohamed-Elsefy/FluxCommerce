using System.Text.Json.Serialization;

namespace FluxCommerce.BuildingBlocks.Messaging.Versioning;

[JsonConverter(typeof(EventVersionJsonConverter))]
public readonly record struct EventVersion : IComparable<EventVersion>
{
    public int Major { get; }

    public int Minor { get; }

    public EventVersion(int major, int minor)
    {
        if (major < 0)
            throw new ArgumentOutOfRangeException(nameof(major), "Major version must be non-negative.");

        if (minor < 0)
            throw new ArgumentOutOfRangeException(nameof(minor), "Minor version must be non-negative.");

        Major = major;
        Minor = minor;
    }

    public static EventVersion V1 => new(1, 0);

    public static EventVersion Parse(string value)
    {
        if (!TryParse(value, out var version))
            throw new FormatException($"'{value}' is not a valid event version. Expected format 'major.minor'.");

        return version;
    }

    public static bool TryParse(string? value, out EventVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return false;

        if (!int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor))
            return false;

        if (major < 0 || minor < 0)
            return false;

        version = new EventVersion(major, minor);
        return true;
    }

    public int CompareTo(EventVersion other)
    {
        var majorComparison = Major.CompareTo(other.Major);
        return majorComparison != 0 ? majorComparison : Minor.CompareTo(other.Minor);
    }

    public override string ToString() => $"{Major}.{Minor}";
}
