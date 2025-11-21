#nullable enable
using DNDGame.Services.Sync;

namespace DNDGame.Services.Dice;

public sealed record DiceRollResult(SyncEventRecord EventRecord, DiceRollBody Payload, DiceFormula Formula);
