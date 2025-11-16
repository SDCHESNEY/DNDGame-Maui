# LLM Dungeon Master (DNDGame) — .NET MAUI, Serverless & P2P

> **Trademark note**: "Dungeons & Dragons" is property of Wizards of the Coast. This project is a fan‑made, non‑commercial homage. Use only SRD 5.1 or original material; do not distribute proprietary D&D content.

## Overview

A fully self‑contained .NET MAUI application delivering a privacy‑first, LLM‑driven D&D‑style RPG experience across iOS, Android, macOS, and Windows—**without any centralized game server**. The UI is built with native MAUI XAML (no Blazor Hybrid). Players collaborate peer-to-peer over local networks or direct connections with end-to-end encryption, keeping gameplay private and resilient offline.

**Key Features:**
- **Serverless Architecture**: No backend infrastructure; all gameplay data stays on your devices
- **Peer-to-Peer Multiplayer**: Real-time collaboration via encrypted LAN/Nearby connections
- **Offline-First**: Full solo play capability; sessions sync when peers reconnect
- **Flexible LLM Integration**: OpenAI (default), localhost (Ollama/LM Studio), or on-device inference
- **Privacy by Default**: End-to-end encryption, local storage, no telemetry
- **Cross-Platform**: iOS, Android, macOS, and Windows with consistent native UI

## Architecture Overview

The application follows a layered architecture designed for testability, modularity, and cross-platform consistency. See `docs/idea.md` for complete technical details.

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

### Core Components

**UI Layer (MAUI XAML)**
- Native MAUI pages using `ContentPage`, `Shell`, and platform controls
- Key screens: Character Sheet, Dice Roller, Chat, Initiative Tracker, Session Lobby, Settings
- Cross-platform styling with `ResourceDictionary`, visual states, and accessibility support

**ViewModels (CommunityToolkit.Mvvm)**
- `ObservableObject`, `RelayCommand` for reactive bindings
- Async-first, platform-agnostic design
- Clear separation of concerns for testability

**LLM Service**
- Strategy pattern supporting multiple providers
- Streaming support with token-by-token rendering
- Safety features: content filters, rate limiting, retries (Polly)
- Client-side prompt summarization for context management

**P2P Transport**
- Modular transports: LAN (mDNS + TCP), Nearby (BLE + Wi-Fi Direct), optional WebRTC
- Device identity via Ed25519/X25519 keypairs
- Noise-protocol handshake for mutual authentication
- End-to-end encryption: ChaCha20-Poly1305 or AES-GCM

**Sync Engine**
- Append-only event log with content-addressed IDs
- Vector clocks for causality tracking
- CRDTs: RGA/Logoot (ordered chat), LWW-Element-Set (presence/flags)
- Gossip protocol for efficient replication with deduplication

**Persistence**
- SQLite with EF Core for structured data
- Encrypted storage for sensitive fields
- Platform SecureStorage (Keychain/Keystore) for API keys
- Export/import via signed JSON bundles

### Design Principles
- **Serverless**: No centralized infrastructure or always-on backend
- **Offline-First**: Full functionality without network; sync when available
- **Privacy-Preserving**: Data stays on devices; explicit sharing only
- **Security by Default**: E2E encryption, verifiable identities, minimal metadata
- **Pluggable Architecture**: Swap LLM providers, transport layers, or storage backends

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

## LLM Integration Modes

The application supports three LLM provider types, allowing users to choose based on privacy, cost, and connectivity needs:

### 1. OpenAI (Default) — Cloud API
- **Provider**: OpenAI via HTTPS
- **Configuration**: API key stored encrypted in platform `SecureStorage` (iOS Keychain/Android Keystore)
- **Default Model**: `gpt-4o-mini` (configurable)
- **Features**: 
  - Streaming token responses for real-time UI updates
  - Client-side prompt truncation and summarization
  - Retry logic with exponential backoff (Polly)
  - Optional biometric gate before API calls on shared devices
- **Security**: API key never logged, never synced P2P, never included in backups
- **Use Case**: Best quality responses; requires internet and API credits

### 2. Localhost — Private Local Inference
- **Providers**: Ollama, LM Studio, or any OpenAI-compatible local server
- **Configuration**: Base URL (e.g., `http://127.0.0.1:11434`)
- **Mobile Setup**: Connect to same-LAN host running the service
- **Use Case**: Privacy-focused; no API costs; requires local hardware

### 3. On-Device — Embedded Models (Advanced)
- **Engine**: LLamaSharp or ONNX Runtime
- **Models**: Small quantized models (downloads managed by app)
- **Lifecycle**: Download, checksum verification, caching in app storage
- **Use Case**: Complete offline capability; no external dependencies; limited by device resources

### Safety & Moderation
- **System Prompts**: Enforce SRD-aligned rules, turn loop structure, and content boundaries
- **Content Filtering**: Optional client-side moderation pass (pre/post LLM)
- **Token Management**: Automatic summarization of long chat histories
- **Rate Limiting**: Configurable throttling to prevent excessive API usage

## Peer-to-Peer Networking & Sync

True serverless multiplayer with no central infrastructure. Players discover and connect directly with full end-to-end encryption.

### Transport Layers (Pluggable)
**LAN Transport** (Primary)
- Discovery: mDNS/Bonjour service records
- Connection: Encrypted TCP channels
- Range: Same local network (no internet required)
- Latency: Low (<50ms typical)

**Nearby Transport** (Phase 2b)
- Technologies: Bluetooth LE pairing, Wi-Fi Direct
- Range: Physical proximity (~30m)
- Use Case: Ad-hoc gaming without network infrastructure

**WebRTC DataChannels** (Optional, Future)
- Use Case: Remote play when LAN unavailable
- Note: Requires external STUN/TURN for NAT traversal (outside core scope)

### Discovery & Session Invites
- **QR Code/Deep Link**: Encodes session ID, host public key, transport hints
- **Auto-Discovery**: Peers on same LAN automatically find sessions via mDNS
- **Manual Join**: Enter session code or scan QR from another device

### Security Architecture
**Identity & Authentication**
- Each device generates a persistent Ed25519/X25519 keypair
- Peer IDs: Short hash of public key (user-verifiable, DID-style)
- Handshake: Noise XX protocol (mutual authentication) or IK (when pre-trusted)
- Forward Secrecy: Session keys rotated; compromise doesn't affect past sessions

**Encryption**
- Algorithm: ChaCha20-Poly1305 or AES-GCM (AEAD)
- Message Signing: Ed25519 signatures for authenticity
- Frame Format: Signed + encrypted envelope with monotonic counters
- Anti-Replay: Nonce/counter validation prevents replay attacks

**Trust Model**
- Device keys persist across restarts
- First connection: Key exchange + user verification (compare short peer IDs)
- Subsequent: Automatic reconnection with stored trusted keys
- Revocation: User can untrust/block peers in session settings

### Replication & Conflict Resolution
**Event Log Model**
- Append-only log of messages, actions, dice rolls
- Content-addressed IDs (hash of payload + parent references)
- Forms a DAG (Directed Acyclic Graph) for causality

**Causality Tracking**
- Lamport timestamps + per-peer logical clocks
- Vector clocks for precise merge ordering
- Topological sort ensures consistent playback across peers

**CRDTs (Conflict-Free Replicated Data Types)**
- **Chat Messages**: RGA (Replicated Growable Array) or Logoot for ordered sequences
- **Presence/Flags**: LWW-Element-Set (Last-Write-Wins) with timestamps
- **Counters/Stats**: Map-merge with conflict-free addition
- **Guarantee**: All peers converge to identical state after sync (eventual consistency)

**Gossip Protocol**
- Range requests: "Send me events 100-200"
- Deduplication: Content hashes prevent redundant transfers
- Backpressure: Bounded queues prevent memory exhaustion
- Resume: Checkpoint-based; survives app restart

**Reliability Features**
- Acknowledgement + retry for critical messages
- Ordered delivery within causal chains
- Detection of tampered or corrupted frames
- Graceful handling of intermittent connectivity

### Scope & Limitations
- **LAN/Nearby Only by Default**: No third-party relay servers or cloud infrastructure
- **WAN Support**: Out-of-band JSON bundles (QR/USB/File) for async sync across locations
- **NAT Traversal**: Not automatic; users may need same network or manual port forwarding for remote play

## Data Storage & Persistence

### Local Database (SQLite + EF Core)
**Core Entities**
- `Player`: User profiles and device identities
- `Character`: Stats, skills, inventory, conditions, backstory
- `Session`: Game metadata, settings, participant list
- `Message`: Chat history with timestamps and causality references
- `DiceRoll`: Roll history with formulas, results, and signatures
- `SessionSettings`: Per-session configuration (house rules, world flags)
- `WorldFlags`: Custom state tracking (quest progress, world events)

**Encryption Strategy**
- Master key stored in platform `SecureStorage` (OS-managed Keychain/Keystore)
- Selective column encryption for sensitive fields (character notes, private messages)
- Encrypted blobs for attachments (images, audio) with per-file nonces
- Database file itself can be encrypted at rest (platform-dependent)

**Performance Optimizations**
- `AsNoTracking` queries for read-heavy operations
- DTOs/projections for UI binding (avoid loading full entity graphs)
- Lazy loading disabled by default; explicit eager loading where needed
- Indexed columns for common queries (session lookup, message chronology)

### Configuration Storage
**Non-Sensitive Settings** (`Preferences` API)
- LLM provider selection (`OpenAI`, `Localhost`, `OnDevice`)
- Model name (e.g., `gpt-4o-mini`)
- UI preferences (theme, font size, notification settings)
- Transport preferences (auto-discover, default connection type)

**Sensitive Secrets** (`SecureStorage` API)
- OpenAI API key (encrypted by iOS Keychain/Android Keystore)
- Device private keys (Ed25519/X25519)
- Database master encryption key
- **Never logged, never synced P2P, never exported**

### Backup & Export
**Export Bundles** (Signed JSON)
- Contains: non-secret session state, messages, characters, dice history
- Excludes: API keys, device private keys, peer trust relationships
- Includes: integrity hashes, schema version, export timestamp
- Verification: Ed25519 signature for tamper detection

**Import Process**
1. Verify signature and schema compatibility
2. Merge with existing data using CRDT rules
3. Resolve conflicts (LWW for settings, causal merge for events)
4. Update local database and notify UI

**Out-of-Band Sync**
- Share bundles via QR code, file transfer, or cloud storage (user-initiated)
- Useful for WAN play without direct P2P connection
- Asynchronous; does not replace real-time P2P for local play

## Dice System & Game Rules

### Dice Service
**Random Number Generation**
- Uses cryptographically secure `RandomNumberGenerator` (not `Random`)
- Ensures fairness and unpredictability
- Serverless: no need to trust a central dice roller

**Formula Parsing**
- Standard notation: `XdY` (X dice of Y sides)
- Modifiers: `2d20+5`, `1d6-2`
- Advantage/Disadvantage: `adv(1d20)`, `dis(1d20)` rolls twice, takes higher/lower
- Input validation: prevents malformed/overflow expressions

**Signed Roll Broadcasts**
- Each roll signed with roller's Ed25519 key
- Broadcast to all peers in session
- Peers verify signature before accepting result
- Prevents cheating and post-hoc modification
- Duplicate/replayed rolls rejected by event log sequence

**Roll History**
- Persisted in SQLite with formula, result, timestamp
- Linked to session and character
- Audit trail for disputed rolls or game review

### Rules Engine (SRD 5.1 Compatible)
**Core Mechanics**
- Ability checks, saving throws, attack rolls
- Skill modifiers and proficiency bonuses
- Conditions (advantage, disadvantage, inspired, exhausted)
- Extensible for house rules or custom systems

**LLM Integration**
- System prompts enforce turn structure and rule adherence
- Prompts reference SRD for spell effects, monster stats, item properties
- DM agent adjudicates edge cases and improvises within boundaries

**Modular Design**
- Rules modules can be swapped or extended
- No hard-coded D&D-specific logic in core engine
- Supports future expansion to other systems (Pathfinder, custom rulesets)

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

## Implementation Roadmap

A phased approach ensures each milestone is fully tested and integrated before moving forward. See `docs/roadmap.md` for detailed scope, testing criteria, and acceptance conditions.

### Phase 1 — Project Bootstrap & Quality Gates ✅ **COMPLETE**
**Objectives**
- Establish solution structure and quality baseline
- DI container, configuration, logging scaffolding
- Local SQLite persistence with EF Core migrations

**Scope**
- MAUI app project with Shell navigation
- Core projects: `DNDGame.Core`, `DNDGame.Services`, `DNDGame.Tests`
- DI registration for all services
- Initial entities: `Player`, `Character`, `Session`, `Message`, `DiceRoll`
- Settings page with non-sensitive `Preferences` storage
- CI pipeline: build, test, static analysis

**Acceptance Criteria**
- App launches on at least one emulator/simulator and desktop
- Settings persist across app restarts
- CRUD operations work for `Character` and `Session`
- CI runs green with zero warnings in core code

---

### Phase 2 — OpenAI Integration (Default) & Secure Key Handling 🔄 **NEXT**
**Objectives**
- Make OpenAI the default LLM provider
- Secure API key entry and storage via `SecureStorage`
- Settings UI for provider/model selection

**Scope**
- `OpenAiLlmService` with streaming support
- Safety filters and Polly retry logic
- Settings UI: provider picker (default `OpenAI`), model text box, secure API key entry
- "Test Connection" button validates credentials
- Optional biometric gate for shared devices

**Testing**
- SecureStorage key save/retrieve/delete roundtrip
- Mocked HTTP streaming responses
- Log scanning to verify no API key leakage
- UI tests for key entry and test connection flow

**Acceptance Criteria**
- Valid API key passes test call within 10 seconds
- Invalid key shows clear error without exposing key
- API key persists across restarts and is absent from logs/exports
- Settings show provider=`OpenAI` by default

---

### Phase 3 — P2P Transport (LAN/Nearby) with E2E Encryption
**Objectives**
- Serverless peer discovery and encrypted P2P channels
- Device identity and mutual authentication

**Scope**
- `ICryptoService` with Ed25519 (signing) + X25519 (key exchange)
- Persistent device keypair and short peer ID display
- Noise XX handshake (or IK for trusted peers)
- LAN transport: mDNS/Bonjour discovery + encrypted TCP
- Message envelope: signed + encrypted with monotonic counters
- Session Lobby UI: create/join, QR invite, peers list, connection status
- Chat screen with encrypted messaging and delivery acknowledgements

**Testing**
- Noise handshake unit tests with RFC test vectors
- Two-device integration: discovery, mutual auth, encrypted chat
- Tamper/replay detection tests
- Connection status UI validation

**Acceptance Criteria**
- Two devices on same LAN discover each other within 10 seconds
- Complete mutual-auth handshake with verified peer IDs
- 20+ back-and-forth encrypted messages with delivery acks
- Tampered/replayed frames rejected and logged
- Device identity persists across restarts

---

### Phase 4 — Sync Engine (CRDT + Event Log) & Dice System
**Objectives**
- Offline-first, eventually-consistent state sync
- Verifiable dice rolls with signed broadcasts

**Scope**
- Append-only event log with content-addressed IDs
- Causality: Lamport timestamps + vector clocks
- CRDTs: RGA/Logoot (chat), LWW-Element-Set (presence/flags)
- Gossip protocol for replication with deduplication
- Encrypted SQLite columns for sensitive event data
- Export/import signed JSON bundles
- `IDiceService` with secure RNG and formula parser
- Signed dice broadcasts with peer verification

**Testing**
- CRDT invariant tests (add/remove/merge idempotency)
- Event log DAG ordering and topological sort
- Property-based testing: random ops converge across peers
- Three-peer partition/reconnection scenarios
- Dice signature verification and replay rejection

**Acceptance Criteria**
- After partition + reconnect, all peers reach identical state within 15 seconds
- Event log shows consistent ordering without orphaned nodes
- Dice rolls display identically on all peers with valid signatures
- Invalid signatures rejected and logged

---

### Phase 5 — Advanced LLM & Product Polish
**Objectives**
- Add local/on-device LLM options
- Content moderation and performance tuning
- UX, accessibility, localization, and packaging

**Scope**
- `Localhost` provider (Ollama/LM Studio) with base URL config
- `OnDevice` provider via LLamaSharp/ONNX (download + cache lifecycle)
- Prompt templates for SRD rules, turn loop, summarization
- Client-side content moderation (configurable)
- Virtualized lists, streaming LLM rendering, cancellation tokens
- Accessibility: labels, dynamic fonts, contrast, keyboard navigation
- Localization scaffolding
- App icons, splash screens, app store preparation

**Testing**
- Provider switching preserves settings
- On-device model lifecycle (download, verify, load/unload)
- Localhost E2E with Ollama/LM Studio
- UI test coverage for all core flows
- Accessibility checks (screen reader, contrast, dynamic text)
- Performance: memory usage, battery impact, scroll FPS

**Acceptance Criteria**
- All three providers (`OpenAI`, `Localhost`, `OnDevice`) functional
- Moderation settings alter responses as configured
- Long chat histories remain responsive (streaming visible, UI interactive)
- Accessibility checks pass (labels, contrast, dynamic text)
- Performance targets met (cold start time, smooth scrolling, no memory leaks)

---

## Current Status
**Active Phase**: Phase 1 Complete ✅  
**Next Milestone**: Phase 2 — OpenAI Integration

All phases include comprehensive unit, integration, and security testing with defined acceptance criteria. Quality gates enforce code style, nullable reference types, async patterns, and zero warnings in core projects.

## Configuration & Settings

### Storage Strategy
**Non-Sensitive Settings** → `Preferences` API
- Persisted as key-value pairs in platform storage
- Readable/writable without authentication
- Examples: UI preferences, provider selection, model names

**Sensitive Secrets** → `SecureStorage` API
- Encrypted by OS (iOS Keychain, Android Keystore, Windows Credential Locker)
- Requires device unlock or biometric authentication (platform-dependent)
- Examples: API keys, device private keys, encryption master keys

### Configuration Keys

| Key | Storage | Type | Default | Description |
|-----|---------|------|---------|-------------|
| `llm:provider` | Preferences | string | `OpenAI` | LLM provider (`OpenAI`, `Localhost`, `OnDevice`) |
| `llm:openai:model` | Preferences | string | `gpt-4o-mini` | OpenAI model identifier |
| `llm:openai:apiKey` | SecureStorage | string | (none) | OpenAI API key (encrypted) |
| `llm:localhost:baseUrl` | Preferences | string | `http://127.0.0.1:11434` | Ollama/LM Studio endpoint |
| `p2p:autoDiscover` | Preferences | bool | `true` | Auto-discover peers on LAN |
| `ui:theme` | Preferences | string | `System` | Theme (`Light`, `Dark`, `System`) |
| `security:requireBiometric` | Preferences | bool | `false` | Gate LLM usage with biometric auth |

### API Key Security
**Best Practices**
- **Never logged**: Redacted from all log output and exception messages
- **Never synced**: Excluded from P2P replication and export bundles
- **Never cached**: Not stored in memory longer than needed
- **Optional biometric gate**: Prompt before each LLM call on shared devices

**Implementation Example** (Phase 2)
```csharp
// AppSettings.cs - Non-sensitive preferences
public static class AppSettings
{
    private const string ProviderKey = "llm:provider";
    private const string ModelKey = "llm:openai:model";

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

// OpenAiKeyStore.cs - Sensitive secret storage
public static class OpenAiKeyStore
{
    private const string ApiKeyKey = "llm:openai:apiKey";

    public static async Task<bool> SaveApiKeyAsync(string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return false;

        // Optional: Biometric authentication before saving
        // if (!await BiometricAuth.TryAuthenticateAsync("Confirm to save OpenAI key")) 
        //     return false;

        await SecureStorage.SetAsync(ApiKeyKey, apiKey);
        return true;
    }

    public static async Task<string?> GetApiKeyAsync(CancellationToken ct = default)
    {
        try 
        { 
            return await SecureStorage.GetAsync(ApiKeyKey); 
        }
        catch 
        { 
            return null; // Key not found or access denied
        }
    }

    public static void DeleteApiKey()
    {
        SecureStorage.Remove(ApiKeyKey);
    }
}

// LlmClientFactory.cs - Using the configuration
public static class LlmClientFactory
{
    public static async Task<ILlmService?> CreateOpenAiAsync(HttpClient http)
    {
        var apiKey = await OpenAiKeyStore.GetApiKeyAsync();
        if (string.IsNullOrWhiteSpace(apiKey)) 
            return null; // No key configured

        var model = AppSettings.Model;
        return new OpenAiLlmService(http, apiKey, model);
    }
}
```

### Settings UI (Phase 2+)
- **Provider Picker**: Dropdown with `OpenAI` (default), `Localhost`, `OnDevice`
- **Model Field**: Text input for model identifier (contextual per provider)
- **API Key Entry**: Masked password field (only for cloud providers)
- **Test Connection**: Button validates credentials with minimal test request
- **Biometric Toggle**: Enable/disable biometric gate for LLM calls
- **Base URL**: Text input (visible only for `Localhost` provider)


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

## User Experience & UI Design

### Screens & Navigation (MAUI Shell)
**Session Lobby**
- Create new session or join existing
- QR invite generation and scanning
- List of discovered peers with connection status
- Transport selector (LAN/Nearby)

**Chat & Actions**
- Real-time message stream with LLM responses
- Quick action buttons (attack, cast spell, use item)
- Typing indicators for connected peers
- Delivery/read receipts

**Character Sheet**
- Stats, skills, proficiencies
- Inventory management with drag-and-drop
- Conditions and effects tracker
- Notes and backstory editor

**Dice Roller**
- Quick presets (d20, d6, advantage/disadvantage)
- Custom formula builder with validation
- Roll history with expandable details
- Signed verification badge for P2P rolls

**Initiative Tracker**
- Turn order with drag-to-reorder
- HP tracking for all combatants
- Condition icons and duration timers
- Automatic advancement with sound/haptic feedback

**Settings**
- LLM provider configuration
- Security options (biometric gate, trusted peers)
- Backup/restore functionality
- Theme and accessibility preferences

### Accessibility Features
- Screen reader support with semantic labels
- Dynamic type scaling (respects system font size)
- High contrast mode support
- Keyboard navigation for desktop targets
- Haptic feedback for important actions
- Reduced motion mode (respects system setting)

### Performance Targets
- **Cold Start**: <3 seconds on mid-range devices
- **Chat Scrolling**: >45 FPS with 1000+ messages (virtualized)
- **LLM Streaming**: Token latency <100ms (network permitting)
- **Memory**: <200MB baseline, <500MB during active session
- **Battery**: <5% drain per hour on idle session (background sync)

---

## Development Guidelines

### Code Standards
- **Language**: C# 12+ with nullable reference types enabled
- **Pattern**: MVVM with `CommunityToolkit.Mvvm` (`ObservableObject`, `RelayCommand`)
- **Async**: All I/O operations async; `ConfigureAwait(false)` in libraries
- **DI**: Constructor injection only; no service locator pattern
- **Logging**: `Microsoft.Extensions.Logging` with PII redaction
- **Documentation**: XML docs for all public APIs

### Project Organization
```
DNDGame.Core/         # Domain entities, interfaces, no dependencies
DNDGame.Services/     # Business logic, LLM, P2P, crypto, sync
DNDGame.Data/         # EF Core DbContext, migrations, repositories
DNDGame.App/          # MAUI UI, ViewModels, platform-specific code
DNDGame.Tests/        # Unit, integration, and property-based tests
```

### Testing Strategy
**Unit Tests** (xUnit + FluentAssertions)
- ViewModels: property change notifications, command execution
- Services: business logic, input validation, error handling
- CRDTs: merge operations, idempotency, commutativity

**Integration Tests**
- Database: migrations, CRUD, concurrency
- P2P: handshake, encryption, message exchange
- LLM: mocked HTTP responses, streaming, error handling

**Property-Based Tests** (FsCheck)
- CRDT convergence with random operation sequences
- Event log ordering invariants
- Encryption/decryption roundtrip

**UI Tests** (MAUI UI Testing)
- Key flows: session create/join, chat, dice roll, character edit
- Accessibility: labels, focus order, contrast
- Platform-specific: iOS, Android, macOS, Windows

### Quality Gates (CI/CD)
- ✅ Restore and build succeed
- ✅ All tests pass (unit + integration)
- ✅ Static analysis (StyleCop, Roslyn analyzers)
- ✅ Zero warnings in core projects
- ✅ Code coverage >80% for services
- ✅ Log scanning for secret leakage
- ✅ Dependency vulnerability scan

---

## Security & Privacy by Design

### Security Principles
1. **Encryption by Default**: All P2P traffic encrypted; sensitive DB fields encrypted
2. **Minimal Metadata**: No tracking, analytics, or behavioral profiling
3. **Verifiable Identity**: Peer keys signed; user confirms on first connection
4. **Defense in Depth**: Input validation, output encoding, rate limiting
5. **Fail Secure**: Errors default to denying access, not granting

### Privacy Commitments
- **No Server**: No central database or cloud storage of gameplay data
- **No Telemetry**: Diagnostic logging is local-only and opt-in
- **No Tracking**: No user IDs, session IDs, or persistent identifiers beyond device keys
- **User Control**: Export/delete all data; revoke peer trust; unlink devices

### Threat Model
**In Scope**
- Malicious peer attempting MITM, replay, or tampering
- Physical device access (mitigated by OS-level encryption)
- Network eavesdropping on LAN traffic

**Out of Scope**
- Compromised device OS or kernel
- Social engineering (user sharing API keys)
- Supply chain attacks on dependencies (mitigated by checksum verification)

---

## Troubleshooting & FAQ

### Common Issues

**Q: App won't launch on iOS simulator**  
A: Ensure Xcode Command Line Tools are installed. Check `dotnet workload list` includes `maui-ios`.

**Q: API key not persisting across restarts**  
A: SecureStorage may fail on some emulators. Test on physical device or check platform logs.

**Q: Peers not discovering on same network**  
A: Check firewall allows mDNS (UDP 5353) and TCP on ephemeral ports. Some corporate networks block local discovery.

**Q: LLM responses very slow**  
A: Check network latency to OpenAI. Consider switching to `Localhost` provider with Ollama for faster local inference.

**Q: Database migration failed**  
A: Delete app data and reinstall (dev only). Production: implement migration rollback and schema versioning.

### Debugging Tips
- Enable verbose logging: Set `Logging:LogLevel:Default` to `Debug` in `appsettings.json`
- Inspect P2P traffic: Use network proxy (mitmproxy) on LAN; note traffic is encrypted
- SQLite browser: Copy database from device via platform tools; open with DB Browser for SQLite
- XAML Hot Reload: Ctrl+S (Windows) or Cmd+S (macOS) to reload UI changes without restart

---

## Contributing

This is currently a solo project. Contributions, feedback, and PRs are welcome once the core architecture stabilizes (post-Phase 3).

### Future Contributions
- LLM provider adapters (Anthropic, Cohere, Google Gemini)
- Transport layers (WebRTC, Tor, I2P)
- Rules modules (Pathfinder, Fate, OSR systems)
- Localization (i18n for non-English languages)
- Accessibility improvements (voice control, switch access)

### Code of Conduct
Be respectful, inclusive, and constructive. Focus on technical merit. No harassment, discrimination, or gatekeeping.

## Related Documentation

- **`docs/idea.md`**: Complete technical design, architecture deep-dive, configuration examples
- **`docs/roadmap.md`**: Detailed phase breakdown with objectives, testing criteria, acceptance conditions
- **`LICENSE`**: MIT License for source code

---

## License & Legal

### Source Code License
MIT License applies to all source code in this repository. See `LICENSE` file for full text.

### Content & Trademarks
- "Dungeons & Dragons" and "D&D" are registered trademarks of Wizards of the Coast LLC
- This project is a **fan-made, non-commercial homage** with no affiliation to Wizards of the Coast
- Game mechanics use content compatible with the **System Reference Document (SRD) 5.1**
- Do not include copyrighted/trademarked content from official D&D publications
- Original content contributions are welcome and must not infringe third-party IP

### Privacy & Data Collection
This application:
- ✅ Stores data **only on your devices**
- ✅ Uses **no centralized servers** for gameplay
- ✅ Collects **no analytics or telemetry** by default
- ✅ Requires **explicit user action** to share data with peers
- ⚠️ LLM provider (OpenAI, etc.) may collect data per their privacy policy—review before use

---

## Acknowledgments

**Technologies**
- .NET MAUI for cross-platform UI
- Entity Framework Core for data persistence
- CommunityToolkit.Mvvm for MVVM infrastructure
- OpenAI API for LLM capabilities

**Inspiration**
- The vibrant D&D and TTRPG community
- Privacy-focused P2P protocols (Secure Scuttlebutt, Matrix)
- Local-first software movement

**Special Thanks**
- Contributors to open-source cryptography libraries (NaCl, Noise Protocol)
- Maintainers of Ollama and LM Studio for local LLM inference
- The .NET community for excellent tooling and support

---

**Last Updated**: November 2025  
**Status**: Phase 1 Complete ✅ | Phase 2 In Progress 🔄
