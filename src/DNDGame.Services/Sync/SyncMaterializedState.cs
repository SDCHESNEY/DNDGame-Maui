#nullable enable
using DNDGame.Services.Dice;

namespace DNDGame.Services.Sync;

public sealed record SyncMaterializedState(
    IReadOnlyList<ChatMessageState> Chat,
    IReadOnlyDictionary<string, PresenceState> Presence,
    IReadOnlyDictionary<string, FlagState> Flags,
    IReadOnlyList<DiceRollState> DiceHistory);

public sealed record ChatMessageState(
    Guid MessageId,
    string PeerId,
    string DeviceName,
    string Content,
    DateTimeOffset Timestamp,
    string EventId);

public sealed record PresenceState(
    string PeerId,
    bool IsOnline,
    long Version,
    DateTimeOffset UpdatedAt,
    string DeviceName,
    string? Status,
    string EventId);

public sealed record FlagState(
    string Key,
    string? Value,
    long Version,
    DateTimeOffset UpdatedAt,
    string EventId);

public sealed record DiceRollState(
    Guid RollId,
    string RollerPeerId,
    string RollerDeviceName,
    string Formula,
    DiceRollMode Mode,
    int Modifier,
    int Total,
    IReadOnlyList<DiceRollComponent> Components,
    bool SignatureValid,
    DateTimeOffset Timestamp,
    string EventId);
