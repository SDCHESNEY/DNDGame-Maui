#nullable enable
using System.Text;
using DNDGame.Services.Crypto;
using DNDGame.Services.Interfaces;
using DNDGame.Services.P2P;
using DNDGame.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace DNDGame.Tests;

public sealed class PhaseThreeTests : IAsyncLifetime
{
    private readonly List<IAsyncDisposable> _disposables = new();

    [Fact]
    public async Task CryptoService_PersistsIdentityAcrossInstances()
    {
        ISecureStorageProvider store = new InMemorySecureStorageProvider();
        var first = new CryptoService(store, NullLogger<CryptoService>.Instance);
        await first.InitializeAsync();
        var peerId = first.Identity.PeerId;

        var second = new CryptoService(store, NullLogger<CryptoService>.Instance);
        await second.InitializeAsync();

        second.Identity.PeerId.Should().Be(peerId);
    }

    [Fact]
    public async Task LanTransport_ExchangesEncryptedMessages()
    {
        var options = new LanP2PTransportOptions
        {
            AckTimeout = TimeSpan.FromSeconds(2)
        };

        var (transportA, cryptoA) = CreateTransport(options);
        var (transportB, cryptoB) = CreateTransport(options);

        await transportA.StartAsync();
        await transportB.StartAsync();

        await WaitUntilAsync(() =>
            transportA.GetKnownPeers().Any(p => p.PeerId == cryptoB.Identity.PeerId) &&
            transportB.GetKnownPeers().Any(p => p.PeerId == cryptoA.Identity.PeerId),
            TimeSpan.FromSeconds(5));

        var peer = transportA.GetKnownPeers().First(p => p.PeerId == cryptoB.Identity.PeerId);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        transportB.MessageReceived += (_, args) =>
        {
            var text = Encoding.UTF8.GetString(args.Payload.Span);
            received.TrySetResult(text);
        };

        await transportA.ConnectAsync(peer);
        await transportA.SendAsync(peer.PeerId, Encoding.UTF8.GetBytes("hello"));

        (await received.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be("hello");
    }

    [Fact]
    public async Task CryptoService_ComputesMatchingSharedSecrets()
    {
        var storeA = new InMemorySecureStorageProvider();
        var storeB = new InMemorySecureStorageProvider();
        var cryptoA = new CryptoService(storeA, NullLogger<CryptoService>.Instance);
        var cryptoB = new CryptoService(storeB, NullLogger<CryptoService>.Instance);
        await cryptoA.InitializeAsync();
        await cryptoB.InitializeAsync();

        var privA = new byte[32];
        var pubA = new byte[32];
        cryptoA.GenerateEphemeralKeyPair(privA, pubA);

        var privB = new byte[32];
        var pubB = new byte[32];
        cryptoB.GenerateEphemeralKeyPair(privB, pubB);

        var secretA = new byte[32];
        var secretB = new byte[32];
        cryptoA.ComputeSharedSecret(privA, pubB, secretA);
        cryptoB.ComputeSharedSecret(privB, pubA, secretB);

        secretA.Should().Equal(secretB);
    }

    [Fact]
    public async Task CryptoService_ComputesMatchingStaticSharedSecrets()
    {
        var storeA = new InMemorySecureStorageProvider();
        var storeB = new InMemorySecureStorageProvider();
        var cryptoA = new CryptoService(storeA, NullLogger<CryptoService>.Instance);
        var cryptoB = new CryptoService(storeB, NullLogger<CryptoService>.Instance);
        await cryptoA.InitializeAsync();
        await cryptoB.InitializeAsync();

        var secretA = new byte[32];
        var secretB = new byte[32];
        cryptoA.ComputeStaticSharedSecret(cryptoB.KeyExchangePublicKey.Span, secretA);
        cryptoB.ComputeStaticSharedSecret(cryptoA.KeyExchangePublicKey.Span, secretB);

        secretA.Should().Equal(secretB);
    }

    private (LanP2PTransport Transport, ICryptoService Crypto) CreateTransport(LanP2PTransportOptions options)
    {
        var storage = new InMemorySecureStorageProvider();
        var crypto = new CryptoService(storage, NullLogger<CryptoService>.Instance);
        var transport = new LanP2PTransport(crypto, NullLogger<LanP2PTransport>.Instance, options);
        _disposables.Add(transport);
        return (transport, crypto);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var start = DateTimeOffset.UtcNow;
        while (!predicate())
        {
            if (DateTimeOffset.UtcNow - start > timeout)
            {
                throw new TimeoutException("Condition not satisfied within timeout");
            }

            await Task.Delay(100);
        }
    }

    public async Task InitializeAsync() => await Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var disposable in _disposables)
        {
            await disposable.DisposeAsync();
        }
    }
}
