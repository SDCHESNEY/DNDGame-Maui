# Implementation Roadmap — .NET MAUI Serverless, P2P, OpenAI Default

This roadmap turns the MAUI-only, peer-to-peer, OpenAI-default design into a deliverable product. Each phase includes implementation scope, testing, and acceptance criteria. It aligns with the project guidelines in `.github/instructions.md` and the design in `docs/idea.md`.

Key constraints
- UI: Native .NET MAUI XAML (no Blazor Hybrid).
- Networking: Peer-to-peer only with end-to-end encryption; no centralized server.
- LLM: OpenAI is the default provider. API key stored encrypted in SecureStorage and never synced P2P.
- Data: Offline-first using encrypted SQLite with EF Core.
- Code: C# 12+, MVVM (CommunityToolkit.Mvvm), async-first, DI, strong logging.

---

## Phase 1 — Project Bootstrap & Quality Gates

Objectives
- Create the MAUI solution skeleton, core projects, and quality baseline.
- Establish DI, configuration, logging, and local persistence scaffolding.

Scope & Tasks
- Solution & Projects
  - Create MAUI app project with `App.xaml`, `AppShell.xaml`, `MainPage.xaml`.
  - Add `Core` folder (interfaces, models), `Services` (LLM, P2P, Sync, Crypto, Settings), `Data` (EF Core DbContext), `ViewModels`, `Views`.
- DI & Configuration
  - Wire DI in `MauiProgram.cs` for: `ISettingsService`, `ILlmService`, `ICryptoService`, `IP2PTransport`, `ISyncEngine`, `IRepository`.
  - Implement `AppSettings` via Preferences for non-sensitive, `SecureStorage` for secrets.
- Persistence
  - Add EF Core Sqlite provider and initial `DndGameContext` with local path + migrations.
  - Entities: `Player`, `Character`, `Session`, `Message`, `DiceRoll`, `SessionSettings`, `WorldFlags`.
- UI Frame
  - Shell routes for Settings, Session Lobby, Chat/Actions, Character Sheet, Dice Roller.
  - Base styles, color resources, and accessibility properties.
- Tooling & Quality
  - Add analyzers, nullable enable, warnings-as-errors for core projects.
  - Logging (Microsoft.Extensions.Logging) + redaction policy; no secret logging.

Testing
- Unit:
  - ViewModel instantiation and property change notifications (`INotifyPropertyChanged`).
  - DI registrations resolved without exceptions; constructor injection only.
  - Settings read/write roundtrip for Preferences (non-sensitive) and error handling for missing keys.
- Database:
  - In-memory Sqlite and device Sqlite: create database, apply initial migration, CRUD for `Character` and `Session`.
  - Migration idempotency (apply twice no-op), schema version check.
- Static Analysis:
  - Build succeeds with analyzers; nullable enabled across projects; no warnings in core code.
  - Verify `ConfigureAwait(false)` in library-like services where applicable.
- CI:
  - Pipeline executes restore/build/test on a clean machine.

Acceptance Criteria
- App builds and launches on at least one emulator/simulator (Android or iOS) and desktop (MacCatalyst/Windows where applicable).
- `Settings` page allows editing a non-sensitive value; value persists after full app restart.
- EF Core database file is created on first run; inserting and retrieving a `Character` and `Session` works deterministically.
- CI run is green: restore, build, and unit tests pass; analyzers produce zero errors and zero warnings in core code.

---

## Phase 2 — OpenAI Integration (Default) & Secure Key Handling ✅ **COMPLETE (Nov 2025)**

Objectives
- Make OpenAI the default LLM provider with encrypted API key storage.
- Provide a Settings UI for provider/model selection and API key entry.

Scope & Tasks
- LLM Service
  - Implement `ILlmService` and `OpenAiLlmService` with streaming support.
  - Add basic safety filters (client-side content moderation hooks) and Polly retries.
- Settings UI
  - Provider picker (`OpenAI`, `Localhost`, `OnDevice` — default `OpenAI`).
  - Model text box (default `gpt-4o-mini`).
  - Secure API key entry stored via `SecureStorage` (never logged or synced).
  - Optional biometric gate before use on shared devices.
- Sample Flow
  - "Test Connection" button performs a minimal completions call and shows result.

Testing
- Unit:
  - SecureStorage key save/get/delete happy/edge paths; deletion clears access.
  - Provider/model preference changes raise events and persist.
  - `OpenAiLlmService` builds requests with correct headers (`Authorization: Bearer`), base URL, and model field.
- Integration:
  - Mocked HTTP returns streaming and non-streaming responses; service parses incremental tokens.
  - Optional dev-only live call validates 200 OK and minimal completion (flagged to skip in CI).
- Security:
  - Verify no logs contain the API key (scan logs with test hook); redaction applied on exception.
  - Key not included in any exported bundle or crash report sample.
- UI/UX:
  - MAUI UITest: entering API key, saving, testing connection shows success; invalid key shows actionable error.
  - Biometric prompt path (if enabled) blocks use until authenticated.

Acceptance Criteria
- Settings screen shows provider=`OpenAI` by default; user can switch model text and save.
- Valid API key entry passes a test call within 10 seconds and displays a non-empty response; invalid key shows a clear error without exposing the key.
- API key persists across app restarts; delete removes it and test call is blocked until re-entered.
- Automated log scan in test confirms no plaintext API key; manual export confirms key absent.

Status Notes (Nov 2025)
- `OpenAiLlmService` now drives all completions with streamed updates, Polly retry policies, and `BasicLlmSafetyFilter` hooks before/after dispatch.
- Secure key entry flows are wired through `SettingsPage`/`SettingsViewModel`; secrets persist in `SecureStorage` behind DI-friendly abstractions used by new unit/integration tests.
- `Test Connection` exercises the live service using a mockable HTTP pipeline, and MAUI UITests verify both success and failure messaging without exposing secrets.

---

## Phase 3 — P2P Transport (LAN/Nearby) with E2E Encryption

Objectives
- Build serverless peer discovery and encrypted P2P channels for chat/actions.
- Establish device identity and mutual authentication.

Scope & Tasks
- Crypto & Identity
  - Implement `ICryptoService` with Ed25519 (signing) + X25519 (key exchange).
  - Generate persistent device keypair; display short peer ID (hash of public key).
  - Use Noise XX (or IK when trusted) handshake; session keys via HKDF.
  - Recommended libraries: Chaos.NaCl or NaCl.Core (Curve25519, Ed25519); AEAD: ChaCha20-Poly1305 or AES-GCM.
- Transport Abstractions
  - `IP2PTransport`: start/stop, discover, connect, send/receive framed messages, backpressure.
  - LAN transport: mDNS/Bonjour service announce + TCP encrypted channels.
  - Nearby transport (Phase 2b): BLE/Wi‑Fi Direct (platform-conditional).
- Message Envelope
  - Signed + encrypted envelope with monotonic counters and vector-clock metadata.
  - Message types: presence, chat, dice roll, event log shard, ack.
- UI & Flows
  - Session Lobby: create/join, QR invite (session ID + host pubkey), peers list, connection status.
  - Chat screen: encrypted messaging, typing indicator, delivery/ack status.

Testing
- Unit:
  - Noise XX handshake steps: correct DH operations, key derivation via HKDF, transcript hash progression.
  - Ed25519 signing and verification with RFC test vectors; X25519 ECDH test vectors.
  - Envelope framing: sequence numbers, vector-clock metadata, AEAD seal/open with nonce reuse prevention.
- Integration:
  - Two local app instances (simulator/emulator or loopback clients): discovery via mDNS, connect, mutual authentication, send/receive chat messages.
  - Intermittent connectivity simulation: drop packets, reorder; ensure retry/ack recovers without duplication.
- Security:
  - Tamper tests: modified ciphertext/signature is rejected; session terminated or error surfaced safely.
  - Replay tests: re-sent old frames rejected by nonce/counter or vector-clock logic.
- UI/UX:
  - Session Lobby lists peers with short verified IDs; QR invite shares session data; pairing confirmation visible.
  - Chat shows delivery status (sent/acked) and connection state changes.

Acceptance Criteria
- Two devices on the same LAN discover each other within 10 seconds and complete a mutual-auth handshake, displaying each peer’s verified short ID.
- Encrypted chat exchanges at least 20 back-and-forth messages with delivery acknowledgements; no plaintext captured in transport logs.
- Tampered/replayed frames are rejected and logged as security events without app crash.
- Device identity keys persist across restarts; previously trusted peers re-connect without re-pair unless keys changed.

---

## Phase 4 — Sync Engine (CRDT + Event Log) & Dice System

Objectives
- Provide offline-first, eventually-consistent state sync for sessions.
- Implement verifiable dice rolls with signed broadcasts.

Scope & Tasks
- Sync Engine
  - Append-only event log with content-addressed IDs (hash of payload + parents).
  - Causality: Lamport timestamp + optional vector clocks.
  - CRDTs: RGA/Logoot for ordered chat; LWW-Element-Set for presence/flags; map-merge for counters.
  - Gossip protocol for replication: range requests, deduplication, and backpressure.
- Storage
  - Persist event log and CRDT materialized views in EF Core Sqlite (encrypted columns for sensitive fields).
  - Import/export signed JSON bundles for out-of-band sync.
- Dice Service
  - `IDiceService` with secure RNG (`RandomNumberGenerator`).
  - Formula parser: `XdY+Z`, advantage/disadvantage; validated input.
  - Broadcast results with signatures; peers verify and append to log.

Testing
- Unit:
  - CRDT invariants: add/remove/merge maintain order and idempotency; LWW resolution matches timestamps.
  - Event log DAG ordering validates parent links; topological sort matches expectations; hash IDs stable.
  - Dice parser: valid cases (e.g., 1d20, 2d6+3, adv/dis) and invalid inputs (overflow, malformed) rejected.
- Property-based:
  - Random operation sequences across peers converge to identical state (chat, presence, flags) after sync.
- Integration:
  - Three peers with disconnections: produce diverging histories; after reconnection, logs merge without manual conflict resolution.
  - Gossip range requests transfer only missing entries; bandwidth bounded; resume from checkpoints after app restart.
- Security:
  - Dice results signed by the roller; all peers verify signature; rejection path for invalid signatures.
  - Duplicate and replayed dice events suppressed.

Acceptance Criteria
- After partition and reconnection, all peers reach identical chat and session state within 15 seconds of link restoration without user action.
- Event log inspector (dev view) shows a consistent topological order and no orphaned nodes.
- Dice rolls display identical totals and components on all peers; signature verification passes; invalidly signed rolls show an error and are excluded.

---

## Phase 5 — Advanced LLM & Product Polish

Objectives
- Add local/on-device LLM options, content moderation, and performance tuning.
- Improve UX, accessibility, localization, and battery/performance characteristics.

Scope & Tasks
- LLM Options
  - `Localhost` provider (Ollama/LM Studio) with base URL config.
  - `OnDevice` provider via LLamaSharp/ONNX for small models (download + cache lifecycle).
  - Prompt templates for SRD rules, turn loop, and summarization.
- Moderation & Safety
  - Optional client-side moderation pass; configurable boundaries.
  - Summarization of long histories to control context length and latency.
- UX & Perf
  - Virtualized lists, streaming rendering of LLM output, cancellation tokens.
  - Accessibility labels, dynamic fonts, contrast, and keyboard navigation.
  - Localization scaffolding and resource files.
- Packaging
  - App icons/splash; app store prep; versioning and release notes.

Testing
- Unit:
  - Provider switching preserves settings; OpenAI remains functional after toggling back.
  - On-device model lifecycle: download, checksum verify, load/unload, and fallback on failure.
  - Prompt template substitution correctness; summarization boundaries enforced.
- Integration:
  - Localhost provider (Ollama/LM Studio) E2E prompt-response on LAN dev setup.
  - On-device model smoke test generates a short response within target time budget on at least one platform.
- UI/UX:
  - MAUI UITest coverage for core flows (session create/join, chat, dice, settings) across target devices.
  - Accessibility: labels, focus order, dynamic text sizing verified.
- Performance:
  - Load test synthetic session histories; memory usage stays within target limits; battery impact measured on a physical device where feasible.

Acceptance Criteria
- Provider picker supports `OpenAI`, `Localhost`, and `OnDevice`; switching providers does not crash; OpenAI remains default on fresh install.
- Moderation settings alter responses as configured; long histories remain responsive (token streaming visible, UI remains interactive).
- Accessibility checks pass: screen reader announces key controls, minimum contrast met, dynamic text sizes render correctly.
- Performance targets met: cold start under agreed threshold, scrolling chat remains >45 FPS on target device, no memory leaks detected in a 30‑minute session.

---

## Cross-Cutting Quality Gates

- Code Style: C# 12+, nullable on, expression-bodied where appropriate, `nameof`, XML docs for public APIs.
- Async: Prefer `async/await`; cancelable operations; no blocking calls on UI thread.
- Security: No secrets in logs; PII minimization; encrypted storage for sensitive data.
- Telemetry: Local-only diagnostics; opt-in; redaction applied.
- Documentation: README quick-start, settings help, security notes, and architecture diagrams.

---

## Deliverables Checklist (Per Phase)

- Source code merged to `develop` with passing CI (build + unit tests).
- Updated docs in `docs/` (user-facing changes + developer notes).
- Demo script or short screen capture for the acceptance scenarios.

---

## Quick Commands

Build & run (example — adjust target for your platform):

```bash
# Restore & build
dotnet restore
dotnet build -c Debug

# Run tests
dotnet test -c Debug
```

For device/emulator runs, use Visual Studio or specify a platform target (e.g., `net8.0-android`, `net8.0-ios`) according to your local setup.

---

## Risks & Mitigations

- NAT/Firewall Constraints: Scope is LAN/Nearby P2P only by default to avoid third-party infra; provide out-of-band export/import bundles if WAN is required.
- Crypto API Portability: Use well-supported libs (Chaos.NaCl/NaCl.Core); validate with test vectors and property tests.
- Battery/Performance: Stream processing, cancellation, and CRDT compaction; monitor with profiling tools.
- App Store Policies: Ensure no forbidden background networking; provide clear privacy disclosures.
