#nullable enable
namespace DNDGame.Services.Interfaces;

public interface ISettingsService
{
    string Provider { get; set; }
    string Model { get; set; }
}
