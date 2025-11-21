#nullable enable
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using DNDGame.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace DNDGame.Services.Dice;

public sealed class DiceService : IDiceService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private static readonly Regex FormulaRegex = new(
        pattern: "^\\s*(?<count>\\d+)\\s*d\\s*(?<sides>\\d+)(?:\\s*(?<sign>[+-])\\s*(?<mod>\\d+))?\\s*(?<mode>adv|dis)?\\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly ICryptoService _cryptoService;
    private readonly ISyncEngine _syncEngine;
    private readonly ILogger<DiceService> _logger;

    public DiceService(ICryptoService cryptoService, ISyncEngine syncEngine, ILogger<DiceService> logger)
    {
        _cryptoService = cryptoService;
        _syncEngine = syncEngine;
        _logger = logger;
    }

    public async Task<DiceRollResult> RollAsync(int sessionId, string formula, DiceRollMode? modeOverride = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(formula))
        {
            throw new ArgumentException("Formula is required", nameof(formula));
        }

        await _cryptoService.InitializeAsync(ct).ConfigureAwait(false);

        if (!TryParseFormula(formula, out var parsed))
        {
            throw new ArgumentException("Formula must match XdY+Z syntax (e.g. 2d6+3)", nameof(formula));
        }

        var appliedMode = modeOverride ?? parsed.Mode;
        var normalized = new DiceFormula(parsed.DiceCount, parsed.DiceSides, parsed.Modifier, appliedMode);
        ValidateFormula(normalized);

        var components = RollComponents(normalized);
        var total = components.Where(static c => c.Kept).Sum(static c => c.Value) + normalized.Modifier;
        var timestamp = DateTimeOffset.UtcNow;
        var identityKey = Convert.ToBase64String(_cryptoService.IdentityPublicKey.Span);
        var evidence = new DiceRollEvidence(
            Guid.NewGuid(),
            _cryptoService.Identity.PeerId,
            _cryptoService.Identity.DeviceName,
            identityKey,
            normalized.DiceCount,
            normalized.DiceSides,
            normalized.Modifier,
            normalized.Mode,
            components,
            total,
            normalized.Canonical,
            timestamp);

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(evidence, SerializerOptions);
        var signature = new byte[64];
        _cryptoService.Sign(payloadBytes, signature);

        var body = new DiceRollBody(evidence, Convert.ToBase64String(signature));
        var record = await _syncEngine.AppendLocalEventAsync(sessionId, body, ct).ConfigureAwait(false);
        _logger.LogInformation("Rolled {Formula} for session {SessionId}: {Total}", normalized.Canonical, sessionId, total);
        return new DiceRollResult(record, body, normalized);
    }

    public bool TryParseFormula(string formula, out DiceFormula parsed)
    {
        parsed = default!;
        if (string.IsNullOrWhiteSpace(formula))
        {
            return false;
        }

        var match = FormulaRegex.Match(formula);
        if (!match.Success)
        {
            return false;
        }

        var count = int.Parse(match.Groups["count"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var sides = int.Parse(match.Groups["sides"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var modifier = 0;
        if (match.Groups["sign"].Success && match.Groups["mod"].Success)
        {
            var modValue = int.Parse(match.Groups["mod"].Value, System.Globalization.CultureInfo.InvariantCulture);
            modifier = match.Groups["sign"].Value == "-" ? -modValue : modValue;
        }

        var mode = DiceRollMode.Normal;
        if (match.Groups["mode"].Success)
        {
            var token = match.Groups["mode"].Value;
            mode = string.Equals(token, "adv", StringComparison.OrdinalIgnoreCase)
                ? DiceRollMode.Advantage
                : DiceRollMode.Disadvantage;
        }

        parsed = new DiceFormula(count, sides, modifier, mode);
        return true;
    }

    private static IReadOnlyList<DiceRollComponent> RollComponents(DiceFormula formula)
    {
        var components = new List<DiceRollComponent>();
        if (formula.Mode == DiceRollMode.Normal)
        {
            for (var i = 0; i < formula.DiceCount; i++)
            {
                components.Add(new DiceRollComponent(RollSingle(formula.DiceSides), true));
            }

            return components;
        }

        if (formula.DiceCount != 1)
        {
            throw new InvalidOperationException("Advantage/disadvantage rolls must use 1dN formulas.");
        }

        var first = RollSingle(formula.DiceSides);
        var second = RollSingle(formula.DiceSides);
        var keepHigher = formula.Mode == DiceRollMode.Advantage;
        var kept = keepHigher ? Math.Max(first, second) : Math.Min(first, second);
        components.Add(new DiceRollComponent(first, first == kept));
        components.Add(new DiceRollComponent(second, second == kept));
        return components;
    }

    private static int RollSingle(int sides)
        => RandomNumberGenerator.GetInt32(1, checked(sides + 1));

    private static void ValidateFormula(DiceFormula formula)
    {
        if (formula.DiceCount is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(formula), "Dice count must be between 1 and 100.");
        }

        if (formula.DiceSides is < 2 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(formula), "Dice sides must be between 2 and 1000.");
        }

        if (formula.Modifier is < -1000 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(formula), "Modifier must be between -1000 and 1000.");
        }
    }
}
