#nullable enable
namespace DNDGame.Services.Sync;

public sealed record ChatMessageBody(
    Guid MessageId,
    string PeerId,
    string DeviceName,
    string Content,
    DateTimeOffset CreatedAt,
    string? AfterEventId = null) : ISyncEventBody
{
    public SyncEventKind Kind => SyncEventKind.ChatMessage;
}
