#nullable enable
using System.Collections.ObjectModel;
using System.Text.Json;

namespace DNDGame.Services.Sync;

public sealed class VectorClock : IEquatable<VectorClock>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly IReadOnlyDictionary<string, long> _entries;

    private VectorClock(IReadOnlyDictionary<string, long> entries)
    {
        _entries = entries;
    }

    public static VectorClock Empty { get; } = new(new ReadOnlyDictionary<string, long>(new Dictionary<string, long>(StringComparer.Ordinal)));

    public IReadOnlyDictionary<string, long> Entries => _entries;

    public long this[string peerId] => _entries.TryGetValue(peerId, out var value) ? value : 0;

    public VectorClock Increment(string peerId)
    {
        var map = new Dictionary<string, long>(_entries, StringComparer.Ordinal);
        map[peerId] = this[peerId] + 1;
        return new VectorClock(new ReadOnlyDictionary<string, long>(map));
    }

    public VectorClock Merge(VectorClock other)
    {
        var map = new Dictionary<string, long>(_entries, StringComparer.Ordinal);
        foreach (var kvp in other._entries)
        {
            if (!map.TryGetValue(kvp.Key, out var existing) || kvp.Value > existing)
            {
                map[kvp.Key] = kvp.Value;
            }
        }

        return new VectorClock(new ReadOnlyDictionary<string, long>(map));
    }

    public static VectorClock FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        var map = JsonSerializer.Deserialize<Dictionary<string, long>>(json, SerializerOptions) ?? new Dictionary<string, long>(StringComparer.Ordinal);
        return new VectorClock(new ReadOnlyDictionary<string, long>(map));
    }

    public string ToJson() => JsonSerializer.Serialize(_entries, SerializerOptions);

    public string ToDeterministicString()
        => string.Join('|', _entries.OrderBy(static kvp => kvp.Key, StringComparer.Ordinal)
            .Select(static kvp => $"{kvp.Key}:{kvp.Value}"));

    public bool Equals(VectorClock? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (_entries.Count != other._entries.Count)
        {
            return false;
        }

        foreach (var kvp in _entries)
        {
            if (!other._entries.TryGetValue(kvp.Key, out var value) || value != kvp.Value)
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is VectorClock clock && Equals(clock);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var kvp in _entries.OrderBy(static kvp => kvp.Key, StringComparer.Ordinal))
        {
            hash.Add(kvp.Key, StringComparer.Ordinal);
            hash.Add(kvp.Value);
        }

        return hash.ToHashCode();
    }
}
