#nullable enable
using System.Collections.ObjectModel;
using System.Text.Json;

namespace DNDGame.Services.Sync;

public sealed record SyncWireEvent(
    string EventId,
    int SessionId,
    SyncEventKind Kind,
    long LamportClock,
    DateTimeOffset Timestamp,
    string VectorClockJson,
    IReadOnlyList<string> Parents,
    string Payload)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static SyncWireEvent FromRecord(SyncEventRecord record)
        => new(
            record.EventId,
            record.SessionId,
            record.Kind,
            record.LamportClock,
            record.Timestamp,
            record.VectorClock.ToJson(),
            record.Parents,
            SyncEventSerializer.Serialize(record.Body));

    public SyncEventRecord ToRecord()
    {
        var body = SyncEventSerializer.Deserialize(Kind, Payload);
        var vectorClock = VectorClock.FromJson(VectorClockJson);
        return new SyncEventRecord(
            EventId,
            SessionId,
            Kind,
            LamportClock,
            Timestamp,
            Parents,
            vectorClock,
            body,
            true);
    }

    public static IReadOnlyList<SyncWireEvent> DeserializeMany(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<SyncWireEvent>();
        }

        var payload = JsonSerializer.Deserialize<List<SyncWireEvent>>(json, SerializerOptions) ?? new List<SyncWireEvent>();
        return new ReadOnlyCollection<SyncWireEvent>(payload);
    }

    public static string SerializeMany(IEnumerable<SyncWireEvent> events)
    {
        var list = events.ToList();
        if (list.Count == 0)
        {
            return string.Empty;
        }

        return JsonSerializer.Serialize(list, SerializerOptions);
    }
}
