#nullable enable
namespace DNDGame.Services.Sync;

public enum SyncEventKind
{
    ChatMessage = 0,
    Presence = 1,
    FlagUpdate = 2,
    DiceRoll = 3
}
