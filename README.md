# LLM Dungeon Master (DNDGame) — .NET MAUI, Serverless & P2P

> Trademark note: "Dungeons & Dragons" is property of Wizards of the Coast. This project is a fan‑made, non‑commercial homage. Use only SRD 5.1 or original material; do not distribute proprietary D&D content.

Single, self‑contained .NET MAUI application for a privacy‑first, LLM‑assisted tabletop RPG experience. No centralized game server: peers connect directly (LAN / nearby transports) with end‑to‑end encryption. OpenAI is the default LLM; local/on‑device providers are planned. Offline play works solo; state sync merges when peers later meet.

## High‑Level Architecture
Layered design (see `docs/idea.md` for full details):

```
UI (MAUI XAML Shell Pages)
	↓ MVVM (CommunityToolkit.Mvvm ViewModels)
Services (LLM, Settings, Crypto, P2P Transport, Sync Engine, Dice)
	↓ Persistence (EF Core Sqlite + SecureStorage for secrets)
P2P Networking (mDNS LAN, future Nearby) | LLM Providers (OpenAI default)
```

Core principles: serverless, offline‑first, pluggable LLM, strong cryptography, eventual consistency via CRDT/event log (future phases).

## Goals & Non‑Goals
**Goals**
- Privacy: gameplay data stays on devices unless explicitly shared.
- Serverless P2P: realtime collaboration without a central server.
- Offline‑first: solo play works fully offline; merges later when peers meet.
- Cross‑platform UX: consistent MAUI XAML UI + MVVM testability.
- Extensibility: modular services (LLM, transport, sync, dice, crypto).

**Non‑Goals**
- Hosted matchmaking infrastructure.
- Automatic global NAT traversal / TURN relays out of scope for initial releases.
- Collection of analytics or behavioral telemetry by default.
- Centralized data retention of player content.

## LLM Integration Modes (Planned)
1. **OpenAI (Default)**: HTTPS completions/streaming; encrypted API key in SecureStorage.
2. **Localhost**: Ollama / LM Studio via LAN `http://<host>:11434` for private local inference.
3. **On‑Device**: Small models (LLamaSharp / ONNX) with cached, checksum‑verified downloads.

Safety & Performance: client‑side moderation hooks, prompt summarization, streaming token UI updates, retry/backoff (Polly) planned.

## P2P Messaging & Sync (Future Phases)
**Transports**: LAN (mDNS + TCP, encrypted), Nearby (BLE / Wi‑Fi Direct), optional future WebRTC.
**Security**: Device keypairs (X25519 + Ed25519), Noise XX/IK handshake, ChaCha20‑Poly1305 or AES‑GCM encryption.
**Replication**: Append‑only event log (content‑addressed IDs), Lamport + vector clocks, CRDTs (RGA/Logoot, LWW sets) for convergent state.
**Reliability**: Ack/retry with backpressure and tamper/replay rejection.

## Data & Persistence
SQLite (encrypted columns planned) for: Player, Character, Session, Message, DiceRoll, SessionSettings, WorldFlags.
Sensitive secrets: master key + API key in SecureStorage (never exported). Non‑sensitive preferences (provider/model) in Preferences.
Export/import bundles: signed JSON containing non‑secret state; out‑of‑band sync option.

## Dice & Rules
Secure RNG (`RandomNumberGenerator`), formula parsing (`XdY+Z`, advantage/disadvantage). Signed roll broadcasts so peers verify authenticity. Rules engine remains SRD‑compatible and extendable.

## Local LLM Quick Start (Ollama Example)
```bash
# 1. Install Ollama (see https://ollama.ai)
ollama pull llama3

# 2. In app Settings: set provider=Localhost and ensure model name matches.
# 3. Mobile device: connect to same LAN; adjust base URL if required.
```


## Project Structure
- `DNDGame.App` — MAUI app, DI bootstrap, XAML pages, `DndGameApplication`.
- `DNDGame.Core` — Domain entities & base interfaces.
- `DNDGame.Data` — EF Core Sqlite context (`DndGameContext`).
- `DNDGame.Services` — Settings, stub LLM, crypto, P2P, sync scaffolds.
- `DNDGame.Tests` — xUnit tests (DI + persistence).
- `docs/idea.md` — Detailed architecture & configuration.
- `docs/roadmap.md` — Full phased implementation plan.

## Phase Roadmap (Summary)
See `docs/roadmap.md` for full objectives, testing, acceptance.
- **Phase 1**: Bootstrap solution, DI, persistence, basic settings (current state complete).
- **Phase 2**: Real OpenAI integration, encrypted API key storage (SecureStorage), streaming responses.
- **Phase 3**: P2P transport (mDNS + encrypted channels), identity handshake (Noise), signed messages.
- **Phase 4**: Sync engine (event log + CRDT), dice system with signed broadcasts.
- **Phase 5**: Advanced LLM providers (Localhost, On‑device), moderation, performance & accessibility polish.

## Configuration (Current / Upcoming)
Non‑sensitive values in `Preferences`; sensitive secrets in platform `SecureStorage`.
Keys (planned/partial):
- `llm:provider` (Preferences) — `OpenAI` | `Localhost` | `OnDevice` (default: `OpenAI`).
- `llm:openai:model` (Preferences) — default `gpt-4o-mini`.
- `llm:openai:apiKey` (SecureStorage) — encrypted OpenAI API key (not yet implemented in Phase 1).

API key handling: never logged, never synced P2P, optional biometric prompt (future).

### Sample (Planned) API Key Storage Code
```csharp
public static class OpenAiKeyStore
{
	const string ApiKeyKey = "llm:openai:apiKey";
	public static async Task<bool> SaveApiKeyAsync(string apiKey)
	{
		if (string.IsNullOrWhiteSpace(apiKey)) return false;
		await SecureStorage.SetAsync(ApiKeyKey, apiKey); // platform encrypted
		return true;
	}
	public static Task<string?> GetApiKeyAsync() => SecureStorage.GetAsync(ApiKeyKey);
	public static void DeleteApiKey() => SecureStorage.Remove(ApiKeyKey);
}
```


## Build & Test Quick Start
```bash
dotnet restore
dotnet build -c Debug
dotnet test -c Debug
```

Build a specific target (example):
```bash
dotnet build src/DNDGame.App/DNDGame.App.csproj -c Debug -f net9.0-android
```
Run from IDE/emulator (Visual Studio / VS for Mac) for deployment.

## Development Notes
- C# 12, nullable enabled, MVVM Toolkit for bindings.
- SQLite DB file auto‑created; initial entities persisted (Phase 1 minimal set).
- `DndGameApplication` uses `CreateWindow` override (modern MAUI pattern).
- Compiled XAML bindings use `x:DataType` for performance.

## Security & Privacy Foundations
- End‑to‑end encryption planned (Phase 3) for all P2P frames.
- Secrets stay local; export bundles will exclude private keys and API keys.
- No telemetry by default; optional local diagnostics only.

## Next Active Phase
Proceed to **Phase 2**: Implement `OpenAiLlmService`, secure API key entry UI, test connection action, and log redaction safeguards.

## Contributing / Quality Gates
- Keep public APIs documented; avoid blocking UI thread.
- Use unit tests for persistence & DI; add integration tests as transports arrive.
- Maintain privacy and avoid introducing centralized dependencies.

## License & Content Use
Fan‑made, non‑commercial; ensure any game content follows SRD 5.1 or is original.

MIT License applies to source code (see `LICENSE`). Trademarked names/art belong to their respective owners and are not covered by this license.

---
For full details read `docs/idea.md` and `docs/roadmap.md`.
