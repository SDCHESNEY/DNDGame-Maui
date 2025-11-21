#nullable enable
using DNDGame.Services.Sync;

namespace DNDGame.Services.Dice;

public sealed record DiceRollBody(DiceRollEvidence Evidence, string Signature) : ISyncEventBody
{
    public SyncEventKind Kind => SyncEventKind.DiceRoll;
}
