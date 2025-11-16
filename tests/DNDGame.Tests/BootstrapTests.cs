#nullable enable
using DNDGame.Data;
using DNDGame.Services.Crypto;
using DNDGame.Services.Interfaces;
using DNDGame.Services.Llm;
using DNDGame.Services.P2P;
using DNDGame.Services.Settings;
using DNDGame.Services.Sync;
// Global usings moved to GlobalUsings.cs to satisfy IDE0005

namespace DNDGame.Tests;

public class BootstrapTests
{
    [Fact]
    public void DIContainer_ResolvesCoreServices()
    {
        var sc = new ServiceCollection();
        sc.AddDbContext<DndGameContext>(o => o.UseSqlite("Data Source=:memory:"));
        sc.AddSingleton<ISettingsService, SettingsService>();
        sc.AddSingleton<ILlmService, StubLlmService>();
        sc.AddSingleton<ICryptoService, CryptoService>();
        sc.AddSingleton<IP2PTransport, DummyP2PTransport>();
        sc.AddSingleton<ISyncEngine, SyncEngine>();
        var sp = sc.BuildServiceProvider();

        sp.GetRequiredService<ISettingsService>().Should().NotBeNull();
        sp.GetRequiredService<ILlmService>().Should().NotBeNull();
        sp.GetRequiredService<ICryptoService>().Should().NotBeNull();
        sp.GetRequiredService<IP2PTransport>().Should().NotBeNull();
        sp.GetRequiredService<ISyncEngine>().Should().NotBeNull();
    }

    [Fact]
    public async Task Database_CanCreateAndPersistCharacter()
    {
        var sc = new ServiceCollection();
        sc.AddDbContext<DndGameContext>(o => o.UseSqlite("Data Source=:memory:"));
        var sp = sc.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<DndGameContext>();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();

        ctx.Characters.Add(new Core.Entities.Character { Name = "Test", Level = 1 });
        await ctx.SaveChangesAsync();

        var count = await ctx.Characters.CountAsync();
        count.Should().Be(1);
    }
}
