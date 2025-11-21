#nullable enable
using System.Text.Json;
using DNDGame.Services.Dice;

namespace DNDGame.Services.Sync;

internal static class SyncEventSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static string Serialize(ISyncEventBody body) => body.Kind switch
    {
        SyncEventKind.ChatMessage => JsonSerializer.Serialize((ChatMessageBody)body, Options),
        SyncEventKind.Presence => JsonSerializer.Serialize((PresenceBody)body, Options),
        SyncEventKind.FlagUpdate => JsonSerializer.Serialize((FlagUpdateBody)body, Options),
        SyncEventKind.DiceRoll => JsonSerializer.Serialize((DiceRollBody)body, Options),
        _ => throw new NotSupportedException($"Unsupported event kind {body.Kind}")
    };

    public static ISyncEventBody Deserialize(SyncEventKind kind, string payload) => kind switch
    {
        SyncEventKind.ChatMessage => Deserialize<ChatMessageBody>(payload),
        SyncEventKind.Presence => Deserialize<PresenceBody>(payload),
        SyncEventKind.FlagUpdate => Deserialize<FlagUpdateBody>(payload),
        SyncEventKind.DiceRoll => Deserialize<DiceRollBody>(payload),
        _ => throw new NotSupportedException($"Unsupported event kind {kind}")
    };

    private static T Deserialize<T>(string payload) where T : ISyncEventBody
    {
        var result = JsonSerializer.Deserialize<T>(payload, Options);
        return result ?? throw new InvalidOperationException($"Unable to deserialize payload for {typeof(T).Name}");
    }
}
