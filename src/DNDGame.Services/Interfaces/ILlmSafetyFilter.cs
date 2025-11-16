#nullable enable
namespace DNDGame.Services.Interfaces;

public interface ILlmSafetyFilter
{
    void EnsureAllowed(string prompt);
}
