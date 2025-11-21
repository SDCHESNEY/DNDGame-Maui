#nullable enable
namespace DNDGame.Services.Dice;

public sealed record DiceFormula(int DiceCount, int DiceSides, int Modifier, DiceRollMode Mode)
{
    public string Canonical => $"{DiceCount}d{DiceSides}{FormatModifier()}";

    public override string ToString() => Canonical;

    private string FormatModifier()
    {
        if (Modifier == 0)
        {
            return string.Empty;
        }

        return Modifier > 0 ? $"+{Modifier}" : Modifier.ToString();
    }
}
