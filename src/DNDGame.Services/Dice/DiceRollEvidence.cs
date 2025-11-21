#nullable enable
namespace DNDGame.Services.Dice;

public sealed record DiceRollEvidence(
    Guid RollId,
    string RollerPeerId,
    string RollerDeviceName,
    string IdentityPublicKey,
    int DiceCount,
    int DiceSides,
    int Modifier,
    DiceRollMode Mode,
    IReadOnlyList<DiceRollComponent> Components,
    int Total,
    string Formula,
    DateTimeOffset Timestamp);
