# Eden — Backlog

Working list of concrete tasks. Ticking order is rough — phase gates are in
[../_design/plan.md](../_design/plan.md).

---

## Phase 0 — Groundwork

- [x] `.gitignore` at repo root
- [x] `.editorconfig` at repo root
- [x] `Directory.Build.props` with `<LangVersion>latest</LangVersion>`
- [x] GitHub Actions CI (Ubuntu + Windows matrix)
- [x] `TESTING.md` rewritten for `dotnet test`
- [x] `BUILDING.md` updated for Eden
- [x] `README.md` stripped to essentials
- [x] ~~Top-level `OpenSim/` → `Eden/` directory rename~~ obsoleted at the Phase 1→2 boundary — the whole `Eden/` tree was deleted.
- [x] ~~`prebuild.xml` paths + Solution name updated~~ obsoleted — Prebuild dropped, `prebuild.xml` gone.
- [x] `LICENSE.md` updated to Eden + OpenSim attribution
- [x] ~~Verify a clean build succeeds locally~~ `dotnet build eden.sln --configuration Release` → 0 warnings, 0 errors.
- [ ] Verify CI passes on first push
- [x] ~~Triage whether existing NUnit tests pass; mark broken ones for later~~ obsoleted — the NUnit suite died with the legacy tree; new-tree suite is xUnit under `tests/Eden.Tests/` (28 tests, 1 pre-existing flake tracked separately).
- [x] ~~Decide what to do with root `eden.sln` stub~~ `eden.sln` is now the live solution for the new tree (5 projects).
- [ ] Add `.github/PULL_REQUEST_TEMPLATE.md`
- [ ] Add `.github/ISSUE_TEMPLATE/` with bug / feature / chore templates
- [ ] Add `CODEOWNERS` file
- [ ] Add `dependabot.yml` covering `nuget` and `github-actions` ecosystems
- [ ] Add `CHANGELOG.md` following Keep-a-Changelog conventions
- [ ] Decide commit message convention (Conventional Commits vs free-form) and document it
- [ ] Configure branch protection rules on default branch (document in a note; apply in GH UI)

---

## Phase 1 — Demolition

The full legacy `Eden/` tree (~2659 files, 56 MB) plus `bin/`, `ThirdParty/`,
`ThirdPartyLicenses/`, `addon-modules/`, `share/` were deleted at the Phase
1→2 boundary in commits `1a89a60` → `cf33e95`. Every bullet below that
depended on that tree is now done by deletion.

- [x] ~~LLUDP stack — remove `Eden/Region/ClientStack/Linden/*`~~ **Done** at Phase 1→2 boundary. Replaced by QUIC transport (`Eden.Shared` wire protocol, `System.Net.Quic`).
- [x] ~~LSL frontend — remove LSL grammar, compiler frontend, `ll*()` function surface~~ **Done.** YEngine deleted before the boundary; the `ScriptEngine/Shared` + `ScriptEngine/Interfaces` remnants went with the boundary cut.
- [x] ~~Legacy caps handlers — remove inherited caps endpoints~~ **Done** at Phase 1→2 boundary. `Eden/Capabilities/` deleted. Will be replaced by typed RPC endpoints on the new host when that work lands in Phase 3.
- [x] ~~Custom HTTP server — remove `OSHttpServer` and the custom `HttpListener.cs`~~ **Done** at Phase 1→2 boundary. No HTTP in the new stack — QUIC direct via `System.Net.Quic`.
- [x] ~~Mono.Addins — remove plugin-loader wiring and `.addin.xml` files~~ **Done** at Phase 1→2 boundary. Plugin-loader model deferred; Phase 2+ will decide if there's any plugin layer at all.
- [x] ~~Nini config — remove Nini dependency and its `IConfigSource` usage~~ **Done** at Phase 1→2 boundary. `Microsoft.Extensions.Configuration` migration is a Phase 2 item.
- [x] ~~log4net — remove `ILog`-based logging calls~~ **Done** at Phase 1→2 boundary. `Microsoft.Extensions.Logging` + Serilog migration is a Phase 2 item.
- [x] ~~BinaryFormatter — delete usages in `Eden/Framework/Util.cs`~~ **Done.**
- [x] ~~Thread.Abort / Thread.Suspend — delete from `Util.cs`, `DoubleDictionaryThreadAbortSafe.cs`~~ **Done.**
- [x] ~~AppDomain.CurrentDomain — delete from `Eden/Region/Application/Application.cs`~~ **No change needed** (only surviving use was `UnhandledException`, still canonical in modern .NET).
  - Optional follow-up for the new-tree `Eden.Launcher`: hook `TaskScheduler.UnobservedTaskException` to catch observed-but-unhandled task exceptions.
- [x] ~~XMLRPC and legacy grid protocols~~ **Done** at Phase 1→2 boundary.
- [x] ~~IAsyncResult / BeginInvoke async rewrite~~ **Done** by deletion — all Begin/End-style callsites lived in the legacy tree and went with it.
- [x] ~~Build still succeeds with demolition merged~~ 0 errors, 0 warnings, 27/27 tests green across the 4 boundary commits.
- [x] ~~Surviving scene graph still loads and runs a smoke-test region~~ obsoleted — there is no "surviving scene graph" anymore; Phase 3 will build a new one against the QUIC transport.
- [x] ~~Bump `<LangVersion>` in `Directory.Build.props`~~ **Done** — `latest`.
- [x] Decision landed on sandboxing — **trust** for MVP (see Open Decisions).
- [x] Decision landed on wire protocol — **QUIC** (see Open Decisions).

---

## Phase 2 — Foundation

- [x] ~~Create `Eden.Shared` project~~ Scaffolded at `Eden.Shared/` with `EdenVersion.cs` as anchor.
- [x] ~~Populate `Eden.Shared` with domain types~~ Math (`Vector3`, `Quaternion`, `Color`, `Transform`), tagged IDs (`EdenId<TTag>`), entities (`AvatarState`, `PrimState`), wire envelope + `ClientHello`/`ServerHello`/`Ping`/`Pong`/`AvatarUpdate`/`AvatarLeft`/`PrimUpdate`/`ChatMessage`. 6 round-trip tests green.
- [x] ~~Move domain types from surviving `Eden/Framework` into `Eden.Shared`~~ obsoleted at the Phase 1→2 boundary — `Eden/Framework` is gone, `Eden.Shared` is the authoritative model.
- [x] ~~Stand up ASP.NET Core host (Kestrel) to replace removed HTTP server~~ Went direct QUIC via `System.Net.Quic` (`EdenLauncher.StartHostAsync`). No HTTP layer in the runtime.
- [x] ~~Implement wire protocol transport (server side) — QUIC via `System.Net.Quic`~~ `QuicTransport` + `InMemoryTransport` both implement `ITransport`. Multi-client server (`EdenServer.HandleClientAsync`) with avatar registry + broadcast. `QuicHostIntegrationTests` exercises real QUIC end-to-end.
- [x] ~~Register custom MessagePack formatters for the `Eden.Shared` domain types~~ `EdenResolver` + per-type array-keyed formatters (`Eden.Shared/Wire/Formatters/`). Measured: AvatarState 96 B (minimal) / 116 B (realistic), PrimState 117 B, Vector3 16 B, Transform 38 B. Records unchanged — no `[Key]` attributes.
- [ ] Introduce `Microsoft.Extensions.Configuration` — deferred until something in the new tree actually needs configuring (no `appsettings.json` consumers yet; port / world ID / bind address are all method args). Re-evaluate when the Phase 5 launcher menu or Phase 3 services land.
- [x] ~~Replace logging with `Microsoft.Extensions.Logging`~~ `Microsoft.Extensions.Logging.Abstractions` wired into `EdenServer`, `ViewerClient`, `EdenLauncher`. Callers pass an `ILoggerFactory` or get `NullLogger<T>.Instance` by default. Concrete provider (Serilog, Console, etc.) chosen at the composition root — not a library concern.
- [ ] Migrate MySQL.Data to MySqlConnector — deferred until a database layer actually lands in the new tree
- [ ] Replace `System.Drawing.Common` + `libgdiplus` with `SkiaSharp` (or `ImageSharp`) — deferred until image-handling actually lands in the new tree (the legacy callsites went with the Phase 1→2 cut)
- [ ] Replace `Mono.Data.Sqlite` with `Microsoft.Data.Sqlite` — deferred until SQLite callsites appear in the new tree
- [x] ~~Audit `Mono.Cecil` usage~~ died with Mono.Addins / legacy tree.
- [x] ~~Drop Prebuild tool; convert to native `.csproj` files + `Directory.Packages.props`~~ Prebuild removed; all csproj files tracked as SDK-style, solution `eden.sln` tracked. Central package management deferred (own bullet below).
- [x] ~~Bump target framework `net8_0` → `net10_0`~~ Done across all csproj files; `global.json` pins SDK to 10.0.100+.
- [x] ~~Pin monorepo layout~~ `src/` + `tests/` split. Product under `src/` (`Eden.Shared`, `Eden.Server`, `Eden.Client`, `Eden.Launcher`, `Eden.Logging`, `Eden.Scripting` with nested `Host/`, `Eden.Viewer`); `tests/Eden.Tests/` covers everything.
- [x] ~~Decide central package management~~ Adopted `Directory.Packages.props` at repo root. `CentralPackageTransitivePinningEnabled=true`. All 11 package versions live in one file; csproj files just `<PackageReference Include="…" />`.
- [x] ~~Pick DI container~~ `Microsoft.Extensions.DependencyInjection`. `Eden.Logging.AddEdenLogging(this IServiceCollection, ...)` extension wires Serilog through the standard `ILoggerFactory`/`ILogger<T>` graph. Composition roots (viewer, future CLI, tests) build a `ServiceProvider` and resolve normally.
- [x] ~~Define wire protocol versioning scheme from day 1~~ `EdenVersion.WireProtocol` constant shipped in `ClientHello`/`ServerHello`; server rejects mismatched versions.
- [x] ~~Add health-check / liveness endpoint to the server host~~ Pre-handshake `MessageKind.Healthcheck` probe. `HealthcheckReply` carries product/release/wire protocol/uptime/session count/world id. Probe via `EdenLauncher.CheckHealthAsync(host, port, timeout)` — opens a throwaway QUIC connection, sends frame, returns reply. No Hello required.

---

## Phase 3 — Server rebuild

- [ ] Scene graph running against new transport — partial: server has a prim registry (`EdenServer._prims`), `SpawnPrimWithBehaviorAsync` attaches a behavior, `ClientTouchPrim` wire frame dispatches `[OnTouch]` onto the prim's behavior via `ServerSelfContext`. Full scene graph (children, parenting, visibility, prim broadcasts) is still TODO.
- [x] ~~C# scripting API surface~~ `Eden.Scripting` — `EdenBehavior` + event/capability attributes + context interfaces. Samples in `tests/Eden.Tests/ScriptingSamplesCompileTest.cs` are a compile-check.
- [x] ~~Roslyn-based script host; trust-model sandbox initially~~ `Eden.Scripting.Host.BehaviorHost` — pre-compiled assembly loading (Roslyn-from-source deferred), reflection-based handler dispatch, lifecycle, error isolation. 10 host tests + DoorScript end-to-end integration. Trust model only; sandboxing deferred to Phase 6. `[OnChat(Channel=…)]` filter dispatch shipped — `RaiseChatAsync(…, channel)` convenience.
- [ ] Physics integration against **Jolt** (via `JoltPhysicsSharp`) — **slice 1 shipped**: `PhysicsWorld` wrapper, 60 Hz server tick loop, box bodies sized by prim scale, static vs dynamic via `PrimFlags.Physical`, dynamic-body pose synced back to `ServerPrim` and broadcast as `PrimUpdate`. Gravity test green. Still TODO: collision event dispatch → `[OnCollisionStart/End]`, wire `IPhysicsApi.Raycast` to Jolt, non-box shapes, avatar character controllers.
- [ ] Asset / inventory / user services exposed over new wire protocol
- [x] ~~Integration tests that spin up a server and hit endpoints~~ `Eden.Tests` covers handshake, multi-client avatar registry, broadcast + spoof-guard, QUIC end-to-end, ViewerClient mirror. 28 tests (1 pre-existing flake tracked separately).
- [x] ~~Lock the scripting API shape with a worked sample in `_docs/_design/`~~ Design doc at `_docs/_design/scripting-model.md`; scaffolded `Eden.Scripting/` project with `EdenBehavior` + event/capability attributes + `ISelfContext`/`IWorldContext`. Sample behaviors (`DoorScript`, `VendingMachine`, `SerialDemo`) in `tests/Eden.Tests/ScriptingSamplesCompileTest.cs` are a compile-check of the API — if the shape drifts, the build breaks.
- [ ] Enumerate the script event surface (touch, collision, timer, money, sensor, link_message, …)
- [ ] Choose region persistence format (JSON, custom binary, SQLite rows)

---

## Phase 4 — Viewer

- [x] ~~`Eden.Viewer` — Godot 4 + C# project scaffolding~~ Godot 4.6.1 .NET project, references `Eden.Shared` + `Eden.Client` + `Eden.Launcher`. Not in the main .sln (needs Godot editor for first build).
- [x] ~~Wire protocol client in viewer (consuming `Eden.Shared`)~~ `Eden.Client.ViewerClient` — handshake, state mirror, events. 4 tests.
- [x] ~~Basic scene rendering: avatars + prims + terrain~~ Partial — floor + cube avatars + name labels. Prims not yet.
- [x] ~~Input and camera~~ Third-person rig with yaw/pitch mouse-look, WASD camera-relative, Esc captures/releases.
- [ ] Minimal UI: chat, inventory, minimap, settings — HUD only so far (mode / name / coords / peer count). Chat + rest pending.
- [ ] Asset streaming and caching
- [ ] Choose asset formats — glTF for meshes, Opus for audio, format for avatars
- [ ] Input rebinding UI and gamepad support
- [ ] Accessibility pass — keyboard-only navigation, screen-reader hooks, high-contrast theme

---

## Phase 5 — One-app UX

- [ ] Launcher menu: *Start a world* / *Join a world* / *Recent*
- [ ] In-process server for *Just me* mode
- [ ] Windows installer (code-signed)
- [ ] macOS `.dmg` (notarised)
- [ ] Linux AppImage
- [ ] Self-contained `dotnet publish` in CI
- [ ] Procure Windows code-signing certificate (EV, ~£200/yr)
- [ ] Enrol in Apple Developer Program ($99/yr) for macOS notarisation
- [ ] Auto-update mechanism — pick between Sparkle / Velopack / custom
- [ ] Crash reporting pipeline (self-hosted Sentry or file-based dumps)
- [ ] Telemetry policy — opt-in, decide what's collected and document in app

---

## Phase 6 — Hardening

- [ ] WASM sandbox for public scripts (Wasmtime)
- [ ] Performance pass: scene graph, physics tick, wire protocol
- [ ] Optional OAR/IAR import tool
- [ ] Public docs site
- [ ] Federation story (multi-region, multi-server)
- [ ] Write `SECURITY.md` with disclosure policy
- [ ] Rate limiting on server endpoints
- [ ] Content moderation tooling (report, mute, eject, land ban)
- [ ] Data-protection notes (GDPR basics if hosting publicly)

---

## Open decisions

*All closed 2026-04-19. See [plan.md](../_design/plan.md) decision log for outcomes.*

- [x] Script sandboxing model — **trust** for MVP; keep host abstraction WASM-compatible
- [x] Wire protocol — **QUIC** (`System.Net.Quic`), MessagePack payloads. WebTransport framing dropped — desktop-only, no browser client planned.
- [x] Content portability — **fresh start**; no OpenSim migration tooling baked in

---

## Cross-cutting / tooling

- [ ] Release workflow: tag → build → upload per-OS artefacts
- [ ] Nightly build pipeline
- [ ] Container image + `docker-compose.yml` for quick local runs
- [ ] Performance baseline / benchmark harness
- [ ] Load test rig (simulated avatar bot pool)
- [x] ~~Fix pre-existing flake `ViewerClientTests.Remote_Movement_Populates_RemoteAvatars`~~ Race in the test itself — TCS fired on Bob's initial (X=0) broadcast racing Bob's explicit X=7 update. Fixed by matching the expected position in the subscription. Also removed `continue-on-error: true` from CI test step.

---

## Product / brand

- [ ] Domain name
- [ ] Logo
- [ ] App icon
- [ ] Website (at minimum a one-pager)
- [ ] Community channel — Discord / Matrix / forum

---

## Chores (anytime)

- [ ] Audit new-tree `Eden.*` source files for correct copyright headers (BSD attribution for derivative content; plain Eden header on net-new files)
- [ ] Remove `CA1416` suppression in `Directory.Build.props` once the QUIC cross-platform story is verified on all three OSes
