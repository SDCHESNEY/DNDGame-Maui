#nullable enable
using DNDGame.Services.Dice;

namespace DNDGame.Services.Interfaces;

public interface IDiceService
{
    Task<DiceRollResult> RollAsync(int sessionId, string formula, DiceRollMode? modeOverride = null, CancellationToken ct = default);
    bool TryParseFormula(string formula, out DiceFormula parsed);
}
