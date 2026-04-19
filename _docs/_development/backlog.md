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
- [x] Top-level `OpenSim/` → `Eden/` directory rename
- [x] `prebuild.xml` paths + Solution name updated
- [x] `LICENSE.md` updated to Eden + OpenSim attribution
- [ ] Verify a clean build succeeds locally after rename (`./runprebuild.sh && dotnet build`)
- [ ] Verify CI passes on first push
- [ ] Triage whether existing NUnit tests pass; mark broken ones for later
- [ ] Decide what to do with root `eden.sln` stub (collides with Prebuild-emitted `Eden.sln`)
- [ ] Add `.github/PULL_REQUEST_TEMPLATE.md`
- [ ] Add `.github/ISSUE_TEMPLATE/` with bug / feature / chore templates
- [ ] Add `CODEOWNERS` file
- [ ] Add `dependabot.yml` covering `nuget` and `github-actions` ecosystems
- [ ] Add `CHANGELOG.md` following Keep-a-Changelog conventions
- [ ] Decide commit message convention (Conventional Commits vs free-form) and document it
- [ ] Configure branch protection rules on default branch (document in a note; apply in GH UI)

---

## Phase 1 — Demolition

Remove the layers we're not keeping. Each item is "delete, then build to see
what stops compiling, then delete the fallout."

Every removal is paired with what replaces it in Phase 2+. Don't tick off a
demolition line until the replacement is either (a) scheduled or (b)
explicitly decided to be *no replacement*.

- [ ] LLUDP stack — remove `Eden/Region/ClientStack/Linden/*`
  - Replaced by: new wire protocol over Kestrel (WebSocket or WebTransport).
  - Breaks: client connection lifecycle, packet throttling, presence heartbeats, mesh/asset streaming pipe.
  - **Deferred to Phase 1→2 boundary.** LLClientView is the only `IClientAPI` implementation; every scene-graph callsite would need a null stub of a several-hundred-method interface to keep the build green after deletion. That stub *is* Phase 2 work. Leave the `Linden/` directory intact until Phase 2 provides the new-protocol `IClientAPI` equivalent, then delete in one move.
- [x] ~~LSL frontend — remove LSL grammar, compiler frontend, `ll*()` function surface~~ **Done.** Deleted: `ScriptEngine/YEngine/` (53 files, ~2.9 MB), `ScriptEngine/Shared/Api/Implementation/` (entire tree incl. plugins + AsyncCommandManager), `Shared/Api/Interface/`, `Shared/Api/Runtime/`, `Shared/Tests/`, `Shared/LSL_Types.cs`. Dropped 4 projects from `prebuild.xml`. Surviving `ScriptEngine/Shared/` keeps `Helpers.cs` (DetectParams, EventParams, exception types — generic, used by Scene events) with `LSL_Types.Vector3/Quaternion` substituted by `OpenMetaverse.Vector3/Quaternion`. `ScriptEngine/Interfaces/` (IScriptModule, IScriptEngine, …) kept as scaffolding for Phase 3. String references to "YEngine" in `Scene.cs` config defaults remain — harmless, Phase 3 will replace.
  - Still open: `bin/OpenSim.ini.example`, `bin/OpenSimDefaults.ini` config sections reference YEngine — rename / gut when we do the `bin/` config cleanup.
  - Note: the live `Thread.Abort()` in AsyncCommandManager died with this demolition.
- [ ] Legacy caps handlers — remove inherited caps endpoints (keep the dispatch shape for later)
  - Replaced by: typed RPC endpoints on the new host.
  - Breaks: inventory fetches, mesh upload, asset transfer, seed-cap handshake.
  - **Deferred to Phase 1→2 boundary.** Caps live inside `Linden/Caps/` and are tied to the Linden protocol; go with the LLUDP cut.
- [ ] Custom HTTP server — remove `OSHttpServer` and the custom `HttpListener.cs`
  - Replaced by: ASP.NET Core + Kestrel (Phase 2).
  - Breaks: all service endpoints, startup sequencing, middleware shape.
- [ ] Mono.Addins — remove plugin-loader wiring and `.addin.xml` files
  - Replaced by: lightweight plugin contract — interface + reflection discovery, or first-party-only with no plugin layer. Decide during Phase 2.
  - Breaks: how region modules get discovered and loaded.
- [ ] Nini config — remove Nini dependency and its `IConfigSource` usage
  - Replaced by: `Microsoft.Extensions.Configuration` (Phase 2).
  - Breaks: all `.ini` reads; config section names and hot-reload semantics change.
- [ ] log4net — remove `ILog`-based logging calls (placeholder for Phase 2 replacement)
  - Replaced by: `Microsoft.Extensions.Logging` with Serilog sink (Phase 2).
  - Breaks: log output format, appender config, any external log scraping.
- [x] ~~BinaryFormatter — delete usages in `Eden/Framework/Util.cs`~~ **Done.** `SerializeToFile` / `DeserializeFromFile` methods plus the `System.Runtime.Serialization.Formatters.Binary` using directive removed. Audit found zero callers in the codebase — pure dead code, no replacement needed.
- [x] ~~Thread.Abort / Thread.Suspend — delete from `Util.cs`, `DoubleDictionaryThreadAbortSafe.cs`~~ **Done.** Util.cs: the `Suspend/Resume` references were all inside a dead `/*…*/` block in `Util.GetStackTrace(Thread)`; method deleted, its one caller simplified. DoubleDictionaryThreadAbortSafe.cs: renamed to `DoubleDictionary`, file renamed, `Thread.Abort`-specific comments removed, callers (EntityManager, SceneManager) qualified to disambiguate from `OpenMetaverse.DoubleDictionary`.
  - Still open: live `Thread.Abort()` in `Eden/Region/ScriptEngine/Shared/Api/Implementation/AsyncCommandManager.cs:206` — dies with the LSL frontend demolition item.
- [x] ~~AppDomain.CurrentDomain — delete from `Eden/Region/Application/Application.cs`~~ **No change needed.** The one live usage is `AppDomain.CurrentDomain.UnhandledException += …`, which is still the canonical (and supported) way to catch unhandled exceptions in modern .NET. Only `AppDomain.CreateDomain` and sandboxing APIs were removed, none of which we use. Other hits in the tree are commented-out code in test files.
  - Optional follow-up: also hook `TaskScheduler.UnobservedTaskException` to catch observed-but-unhandled task exceptions (modern best practice).
- [ ] XMLRPC and legacy grid protocols — delete with LLUDP
  - Replaced by: new RPC protocol; no grid-interop with OpenSim grids.
  - Breaks: any inter-grid message. Confirmed non-goal per plan.md.
  - **Deferred to Phase 1→2 boundary.** The LLUDP research agent found XMLRPC code-wise independent from LLUDP, but protocol-wise it's the Linden login entry point. Dies with LLUDP when Phase 2's new protocol lands.
- [ ] IAsyncResult / BeginInvoke async — mark for rewrite; delete any that were LLUDP-only
  - Replaced by: `Task`-based async / `await`.
  - Breaks: nothing functional — mechanical rewrite.
  - **Deferred to Phase 2.** The LLUDP-only ones die with LLUDP. The rest (`WebUtil`, `RestObjectPoster*`, HTTP client callbacks) is a mechanical Task-based refactor that belongs with Phase 2 HTTP modernisation.
- [x] ~~Build still succeeds with demolition merged~~ 0 errors, 0 warnings across 3 demolition commits.
- [x] ~~Surviving scene graph still loads and runs a smoke-test region~~ Server boots, reads all configs, loads all modules without LSL/YEngine complaint, reaches interactive console init. Crashes there only because the smoke test uses non-tty stdin (captured as a separate chore). No demolition-caused regressions.
- [x] ~~Bump `<LangVersion>` in `Directory.Build.props` from 12 back to `latest` once YEngine is demolished~~ **Done.** YEngine gone, LangVersion restored to `latest`, build clean.
- [ ] Decision landed on sandboxing (see Open Decisions)
- [ ] Decision landed on wire protocol (see Open Decisions)

---

## Phase 2 — Foundation

- [x] ~~Create `Eden.Shared` project~~ Scaffolded at `Eden.Shared/` with `EdenVersion.cs` as anchor. Domain types will be added as wire-protocol work progresses.
- [ ] Populate `Eden.Shared` with domain types: `Vector3`, `AvatarState`, `Prim`, `ItemId`, event shapes, wire messages
- [ ] Move domain types from surviving `Eden/Framework` into `Eden.Shared`
- [ ] Stand up ASP.NET Core host (Kestrel) to replace removed HTTP server
- [ ] Implement wire protocol transport (server side) — WebTransport over QUIC via `System.Net.Quic`
- [ ] Replace config layer with `Microsoft.Extensions.Configuration`
- [ ] Replace logging with `Microsoft.Extensions.Logging` (Serilog provider)
- [ ] Migrate MySQL.Data to MySqlConnector
- [ ] Replace `System.Drawing.Common` + `libgdiplus` with `SkiaSharp` (or `ImageSharp`) — drops the only native-library install step on Linux/macOS
- [ ] Replace `Mono.Data.Sqlite` with `Microsoft.Data.Sqlite` (modern, maintained)
- [ ] Audit `Mono.Cecil` usage — likely only Mono.Addins internals; should die with the Mono.Addins removal
- [x] ~~Drop Prebuild tool; convert to native `.csproj` files + `Directory.Packages.props`~~ Prebuild removed; all csproj files tracked as SDK-style, solution `Eden.sln` tracked. Central package management (`Directory.Packages.props`) deferred until HintPath refs are converted to PackageReferences.
- [x] ~~Bump target framework `net8_0` → `net10_0`~~ Done across all csproj files; `global.json` pins SDK to 10.0.100+.
- [ ] Pin monorepo layout — where `Eden.Shared`, `Eden.Server`, `Eden.Viewer` live relative to the surviving `Eden/` tree
- [ ] Decide central package management (`Directory.Packages.props`) vs per-project `PackageReference`
- [ ] Pick DI container — default to `Microsoft.Extensions.DependencyInjection` unless reason not to
- [ ] Define wire protocol versioning scheme from day 1 (e.g. path-based `/v1/…`)
- [ ] Add health-check / liveness endpoint to the server host

---

## Phase 3 — Server rebuild

- [ ] Scene graph running against new transport
- [ ] C# scripting API surface (`async Task OnTouch(Avatar who)`, etc.)
- [ ] Roslyn-based script host; trust-model sandbox initially
- [ ] Physics re-integration against Bullet
- [ ] Asset / inventory / user services exposed over new wire protocol
- [ ] Integration tests that spin up a server and hit endpoints
- [ ] Lock the scripting API shape with a worked sample in `_docs/_design/` (`async Task OnTouch(Avatar who)`, etc.)
- [ ] Enumerate the script event surface (touch, collision, timer, money, sensor, link_message, …)
- [ ] Choose region persistence format (JSON, custom binary, SQLite rows)

---

## Phase 4 — Viewer

- [ ] `Eden.Viewer` — Godot 4 + C# project scaffolding
- [ ] Wire protocol client in viewer (consuming `Eden.Shared`)
- [ ] Basic scene rendering: avatars + prims + terrain
- [ ] Input and camera
- [ ] Minimal UI: chat, inventory, minimap, settings
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

## Deferred from earlier passes

Items explicitly punted during Phase 0 / Option-C rename. Execute
post-demolition.

- [ ] Namespace rename — `namespace OpenSim.*` to `namespace Eden.*` across all `.cs` files
- [ ] Using rename — `using OpenSim.*;` to `using Eden.*;`
- [ ] Project / assembly names in `prebuild.xml` — `<Project name="OpenSim.X">` to `<Project name="Eden.X">` (plus all `<Reference name="OpenSim.X"/>`)
- [ ] Binary output names — `OpenSim.exe` to `Eden.exe`, `OpenSim.ConsoleClient.exe` to `Eden.ConsoleClient.exe`
- [ ] Runtime config files — `bin/OpenSim.ini.example`, `bin/OpenSim.exe.config`, `bin/OpenSimDefaults.ini`, etc.
- [ ] Launcher scripts — `bin/opensim.sh` to `bin/eden.sh`
- [ ] Source-file copyright headers — audit for correctness; keep OpenSim attributions per BSD terms, add Eden notice to new files
- [ ] Warning suppressions in `prebuild.xml` — `CA1416`, `SYSLIB0011`, `SYSLIB0014`, `SYSLIB0039` should be removable once the dangerous APIs are gone

---

## Open decisions

*All closed 2026-04-19. See [plan.md](../_design/plan.md) decision log for outcomes.*

- [x] Script sandboxing model — **trust** for MVP; keep host abstraction WASM-compatible
- [x] Wire protocol — **WebTransport over QUIC** (`System.Net.Quic`), MessagePack payloads
- [x] Content portability — **fresh start**; no OpenSim migration tooling baked in

---

## Cross-cutting / tooling

- [ ] Release workflow: tag → build → upload per-OS artefacts
- [ ] Nightly build pipeline
- [ ] Container image + `docker-compose.yml` for quick local runs
- [ ] Performance baseline / benchmark harness
- [ ] Load test rig (simulated avatar bot pool)

---

## Product / brand

- [ ] Domain name
- [ ] Logo
- [ ] App icon
- [ ] Website (at minimum a one-pager)
- [ ] Community channel — Discord / Matrix / forum

---

## Chores (anytime)

- [ ] Delete `bin/Regions/`, `bin/ScriptEngines/`, `bin/*.db`, `bin/*.log` from the committed tree if any slipped in
- [ ] Audit `ThirdParty/` — which vendored libs are actually used after demolition?
- [ ] Move root `eden.sln` (Prebuild-only stub) under `Prebuild/` if keeping, else delete
- [ ] Scrub dead NAnt references (old `.nant/` folders, etc.) if any remain
- [ ] Rename `Watchdog.AbortThread` (in `Eden/Framework/Monitoring/Watchdog.cs`) — it no longer aborts, just untracks. Convert callers to cooperative cancellation at the same time.
- [ ] Fix silent cert-missing failure in `Eden/Server/Base/HttpServerBase.cs` — prints "server can't start" then keeps going. Should `Environment.Exit(1)` or throw.
- [ ] Make `LocalConsole` tolerate non-tty stdin — currently crashes at `Console.TreatControlCAsInput = true` when launched without an interactive terminal (scripts, Docker, CI). Try/catch or detect `Console.IsInputRedirected` first.
