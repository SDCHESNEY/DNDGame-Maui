#nullable enable
using System.Text.RegularExpressions;
using DNDGame.Services.Interfaces;

namespace DNDGame.Services.Llm;

public sealed partial class BasicLlmSafetyFilter : ILlmSafetyFilter
{
    private static readonly string[] BannedKeywords =
    {
        "sudo rm -rf",
        "drop table",
        "child abuse",
        "terrorist"
    };

    private static readonly Regex ExcessWhitespaceRegex = ExcessWhitespaceRegexFactory();

    public void EnsureAllowed(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("Prompt cannot be empty", nameof(prompt));
        }

        var normalized = ExcessWhitespaceRegex.Replace(prompt.ToLowerInvariant(), " ").Trim();
        if (normalized.Length > 4000)
        {
            throw new InvalidOperationException("Prompt exceeds maximum length of 4000 characters.");
        }

        if (BannedKeywords.Any(keyword => normalized.Contains(keyword)))
        {
            throw new InvalidOperationException("Prompt contains disallowed content. Please revise your request.");
        }
    }

    [GeneratedRegex(@"\s+")] 
    private static partial Regex ExcessWhitespaceRegexFactory();
}
