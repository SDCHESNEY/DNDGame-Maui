# LLM Dungeon Master — .NET MAUI Self‑Contained, P2P Design

> Trademark note: "Dungeons & Dragons" is property of Wizards of the Coast. This project is a fan‑made, non‑commercial homage. Use only content permitted by the SRD 5.1 or original material; do not distribute proprietary D&D content.

## Overview

A single, self‑contained .NET MAUI application that delivers a D&D‑style, LLM‑driven RPG experience on iOS/Android/macOS/Windows without any centralized game server. The UI is native MAUI (XAML) — no Blazor Hybrid. The only external interface is the LLM provider (OpenAI by default, or local/on‑device LLM as options). Players collaborate and exchange messages/state peer‑to‑peer (P2P) over local networks or direct connections, keeping gameplay privacy‑preserving and resilient offline.

Key principles:
- Self‑contained runtime (no always‑on backend).
- MAUI XAML UI only (no Blazor Hybrid).
- P2P messaging and state replication (no central database).
- Offline‑first with local persistence and deferred sync.
- Pluggable LLM modes: cloud API, localhost (Ollama/LM Studio), or on‑device (LLamaSharp).
- Security by default: end‑to‑end encryption, verifiable identities, minimal metadata.

---

## Goals & Non‑Goals

Goals
- Privacy: all gameplay data remains on devices unless players explicitly share.
- Serverless P2P: real‑time chat/actions across players without a centralized server.
- Offline‑first: full solo play offline; sessions sync/merge when peers meet.
- Cross‑platform UX: consistent UI via MAUI + BlazorWebView; MVVM for testability.
- SRD‑compatible mechanics and extensible rules modules.

Non‑Goals
- Operating a hosted matchmaking service (players discover peers directly or via QR/invite).
- Guaranteed global NAT traversal without any user assistance. (We provide LAN/nearby P2P by default and optional WAN techniques.)

---

## High‑Level Architecture

```
┌───────────────────────────────────────────────────────────────────┐
│                        .NET MAUI (Native UI)                      │
├───────────────────────────┬───────────────────────────────────────┤
│          UI Layer         │      ViewModels (MVVM Toolkit)        │
│        (MAUI XAML)        │  (State, Commands, Navigation)        │
├───────────────────────────┼───────────────────────────────────────┤
│    Application Services   │  LLM Service  |  P2P Transport        │
│  (Rules, Orchestration)   │  Sync Engine  |  Persistence          │
├───────────────────────────┼───────────────────────────────────────┤
│  Local Persistence        │  SQLite (Encrypted) + SecureStorage   │
├───────────────────────────┼───────────────────────────────────────┤
│ P2P Networking (Serverless)│ LAN mDNS + TCP | Nearby (BLE/Wi‑Fi)  │
│ (E2E encryption; CRDT log) │ Optional: WebRTC DataChannels         │
├───────────────────────────┼───────────────────────────────────────┤
│        LLM Interface      │ Cloud (OpenAI/Azure/Anthropic)        │
│ (only external dependency)│ Localhost (Ollama/LM Studio) | On‑device│
└───────────────────────────────────────────────────────────────────┘
```

---

## Core Components

- UI (MAUI XAML)
  - Native MAUI pages (`ContentPage`, `Shell`) and controls for Character Sheet, Dice Roller, Chat, Initiative Tracker.
  - Cross‑platform styling with `ResourceDictionary`, `Styles`, and visual states.
- ViewModels (CommunityToolkit.Mvvm)
  - `ObservableObject`, `RelayCommand`, async‑first; platform‑agnostic.
- LLM Service
  - Strategy pattern for providers: Cloud HTTP, Localhost HTTP (Ollama/LM Studio), On‑device (LLamaSharp/ONNX).
  - Safety/metering: content filters, token/rate limits, retries (Polly), streaming support.
- P2P Transport
  - Modular transports: LAN (mDNS + TCP), Nearby (Bluetooth LE + Wi‑Fi Direct), optional WebRTC DataChannels (for WAN when feasible).
  - Identity via device keypair; Noise‑style handshake; E2E encryption.
- Sync Engine (Conflict‑free)
  - Append‑only event log + vector clocks; CRDT for messages/session state (eventually consistent, mergeable offline).
  - Gossip protocol for replication; deduplication via content‑addressed IDs.
- Persistence
  - SQLite for data; per‑record encryption for sensitive blobs; keys in `SecureStorage`.
  - Export/backup as JSON; import with verification.

---

## LLM Integration Modes

1) OpenAI (default)
- Provider: OpenAI via HTTPS.
- API Key configuration stored encrypted via platform `SecureStorage` (Keychain/Keystore) and never replicated P2P or included in backups. Optional biometric gate before use.
- Streaming tokens to UI; client‑side truncation/summarization for long histories.

2) Localhost HTTP
- Ollama or LM Studio running locally (`http://127.0.0.1:11434`/provider default).
- Mobile: connect to same‑LAN host or device‑local (where supported).

3) On‑Device Inference (advanced)
- LLamaSharp or ONNX Runtime for smaller models; offline and private.
- Model lifecycle: download, verify checksum, cache in app storage; optional quantized formats.

Prompting & Safety
- System prompt enforces SRD‑aligned rules, turn loop, dice policy, content boundaries.
- Optional moderation pass pre/post LLM (on‑device heuristic + provider filters).
- Caching of prompt summaries to control context length.

---

## Serverless P2P Messaging & Sync

Transports (pluggable, P2P only)
- LAN: mDNS/Bonjour discovery; encrypted TCP channels; no internet required.
- Nearby: Bluetooth LE pairing and/or Wi‑Fi Direct for ad‑hoc groups.

Discovery & Invites
- QR code or deep link encodes session ID, host’s public key, and transport hints.
- On LAN, peers auto‑discover sessions via mDNS service records.

Security
- Identity: X25519 keypair; DID‑style peer IDs (hash of public key).
- Handshake: Noise XX (or IK once trusted); mutual authentication; forward secrecy.
- Encryption: ChaCha20‑Poly1305 or AES‑GCM; message signing with Ed25519.

Replication Model
- Event Log: append‑only messages, actions, dice rolls; content‑addressed IDs (hash).
- Causality: Lamport timestamps + per‑peer clock; vector clocks for merge.
- CRDTs: RGA/Logoot for ordered chat; LWW‑Element‑Set for presence/state flags.
- Reliability: ack/retry and small bounded queues; back‑pressure when offline.

Scope Without Servers
- Guaranteed serverless for LAN/nearby play (no internet, no servers).
- Remote/WAN play is not provided by default to preserve strict P2P without third‑party infrastructure. Players may exchange asynchronous update bundles out‑of‑band (e.g., QR/USB) if needed.

---

## Data & Persistence

Local Storage (SQLite)
- Entities: Player, Character, Session, Message, DiceRoll, SessionSettings, WorldFlags.
- Read‑heavy queries use `AsNoTracking` patterns; projections to DTOs for UI.

Encryption
- Master key in platform `SecureStorage`; data key wraps selected tables/columns.
- Attachments (images/audio) encrypted as blobs with per‑file nonces.

Backup/Portability
- Export/import as signed JSON bundle (includes integrity hashes and versioning).
- Optional P2P delta sync using event log ranges.

---

## Rules & Dice

- Dice Service: secure RNG via `RandomNumberGenerator` (server‑agnostic).
- Validated formulas (e.g., `2d20+5`, advantage/disadvantage) with clear provenance.
- Dice rolls broadcast P2P with signature; all peers can verify.

---

## UI/UX (MAUI XAML)

- Screens
  - Session Lobby: create/join, QR invite, transport status, peers list.
  - Chat + Actions: message stream, LLM responses, quick actions.
  - Character Sheet: stats, skills, inventory, conditions.
  - Dice Roller: presets, custom formulas, signed results.
  - Settings: LLM provider (OpenAI default; Local/On‑device optional), privacy, backups, encrypted API key entry.

- Performance
  - Virtualized lists, streaming rendering for LLM output, fine‑grained state updates.
  - Background tasks for sync/LLM calls with cancellation and retries.

---

## Security & Privacy

- E2E encryption for all P2P traffic.
- Minimal metadata; anonymized peer IDs unless user shares profile.
- Configurable data retention and redaction for logs.
- No analytics by default; optional local telemetry for debugging only.

---

## Developer Notes (Alignment with Guidelines)

- C# 12+, MVVM Toolkit, async‑first services, DI via `MauiProgram`.
- Clear separation: UI (MAUI XAML) vs. ViewModels vs. Services vs. Transport.
- Testability: xUnit + FluentAssertions; transport fakes; CRDT merge tests.
- Packaging: multi‑target MAUI; conditional compilation for platform transports.

---

## Implementation Roadmap

Phase 1 — Foundation
- MAUI scaffold (XAML pages, Shell); DI wiring; SQLite + SecureStorage.
- Local single‑player loop with OpenAI (default) and dice service; encrypted API key entry.

Phase 2 — P2P (LAN/Nearby)
- mDNS discovery + encrypted TCP; session invites via QR; chat sync (CRDT).
- Signed dice rolls; basic presence; reliability & reconnection.

Phase 3 — Advanced LLM & State
- On‑device models via LLamaSharp; content moderation; prompt templates & memory.
- Session state CRDTs for initiative/conditions/inventory.

Phase 4 — Polish
- Export/import bundles; performance and battery tuning; accessibility; localization.

---

## Configuration (OpenAI Default)

- Provider: `OpenAI` (default). Optional: `Localhost`, `OnDevice`.
- Sensitive secrets (API keys) are stored only in platform `SecureStorage` and never replicated P2P or included in exports.
- Non‑sensitive toggles (provider selection, model name) use `Preferences`.

Configuration Keys
- `llm:provider` (Preferences): `OpenAI` | `Localhost` | `OnDevice` (default: `OpenAI`).
- `llm:openai:model` (Preferences): e.g., `gpt-4o-mini`.
- `llm:openai:apiKey` (SecureStorage): OpenAI API key (encrypted at rest by the OS).

Example UX
- Settings screen provides: provider picker, model text box, and a secure API key entry field (masked). The app validates the key format client‑side and performs a safe test request on save.

Sample Code (C# — MAUI)

```csharp
// Non-sensitive settings
public static class AppSettings
{
  const string ProviderKey = "llm:provider";
  const string ModelKey = "llm:openai:model";

  public static string Provider
  {
    get => Preferences.Get(ProviderKey, "OpenAI");
    set => Preferences.Set(ProviderKey, value);
  }

  public static string Model
  {
    get => Preferences.Get(ModelKey, "gpt-4o-mini");
    set => Preferences.Set(ModelKey, value);
  }
}

// Sensitive secret (encrypted by OS keychain/keystore)
public static class OpenAiKeyStore
{
  const string ApiKeyKey = "llm:openai:apiKey";

  public static async Task<bool> SaveApiKeyAsync(string apiKey, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(apiKey)) return false;

    // Optional biometric gate (pseudo-code):
    // if (!await BiometricAuth.TryAuthenticateAsync("Confirm to save OpenAI key")) return false;

    await SecureStorage.SetAsync(ApiKeyKey, apiKey);
    return true;
  }

  public static async Task<string?> GetApiKeyAsync(CancellationToken ct = default)
  {
    try { return await SecureStorage.GetAsync(ApiKeyKey); }
    catch { return null; }
  }

  public static void DeleteApiKey()
  {
    SecureStorage.Remove(ApiKeyKey);
  }
}

// Using the configuration in an OpenAI client factory
public static class LlmClientFactory
{
  public static async Task<IOpenAiClient?> CreateOpenAiAsync(HttpClient http)
  {
    var apiKey = await OpenAiKeyStore.GetApiKeyAsync();
    if (string.IsNullOrWhiteSpace(apiKey)) return null;

    var model = AppSettings.Model;
    return new OpenAiClient(http, apiKey, model);
  }
}
```

Notes
- Keys are never logged. Redact secrets in diagnostics.
- Consider adding a "Require biometric before use" toggle that prompts before each LLM call on shared devices.
- For `Localhost`/`OnDevice` modes, no API key is required; the same settings page can conditionally hide the key field.

---

## Local LLM Quick Start (Optional)

- Ollama on desktop:
  1. Install Ollama and pull a model (e.g., `llama3`).
  2. Set provider to `Localhost` in Settings with base URL `http://127.0.0.1:11434`.
  3. Ensure mobile device is on same LAN (if connecting from phone to desktop).

This document defines a privacy‑first, serverless architecture: the MAUI app runs fully on the user’s devices, only contacting an LLM endpoint (cloud or local). Multiplayer uses encrypted P2P to exchange messages and game state without any centralized server.