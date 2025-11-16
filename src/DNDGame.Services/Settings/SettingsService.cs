#nullable enable
using DNDGame.Services.Interfaces;
using Microsoft.Maui.Storage;

namespace DNDGame.Services.Settings;

public class SettingsService : ISettingsService
{
    private const string ProviderKey = "llm:provider";
    private const string ModelKey = "llm:openai:model";

    public string Provider
    {
        get => Preferences.Get(ProviderKey, "OpenAI");
        set => Preferences.Set(ProviderKey, value);
    }

    public string Model
    {
        get => Preferences.Get(ModelKey, "gpt-4o-mini");
        set => Preferences.Set(ModelKey, value);
    }
}
