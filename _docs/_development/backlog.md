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
- [ ] LSL frontend — remove LSL grammar, compiler frontend, `ll*()` function surface
  - Replaced by: C# scripting via Roslyn (Phase 3).
  - Breaks: every script event (`touch_start`, `state_entry`, …), every `ll*()` callsite in user content. No migration; new API.
- [ ] Legacy caps handlers — remove inherited caps endpoints (keep the dispatch shape for later)
  - Replaced by: typed RPC endpoints on the new host.
  - Breaks: inventory fetches, mesh upload, asset transfer, seed-cap handshake.
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
- [ ] BinaryFormatter — delete usages in `Eden/Framework/Util.cs`
  - Replaced by: explicit serialisation — `System.Text.Json` for interchange, MessagePack/protobuf where size/perf matters.
  - Breaks: any persisted state that used it. Audit call sites before deleting — if it's only in-memory clone helpers, no replacement needed.
- [ ] Thread.Abort / Thread.Suspend — delete from `Util.cs`, `DoubleDictionaryThreadAbortSafe.cs`
  - Replaced by: `CancellationToken`-based cooperative cancellation.
  - Breaks: anywhere scripts or long-running workers were aborted externally; all such call sites need a cooperative-shutdown rewrite.
- [ ] AppDomain.CurrentDomain — delete from `Eden/Region/Application/Application.cs`
  - Replaced by: nothing, or `AssemblyLoadContext` if we need runtime assembly isolation later (Phase 6 sandboxing may want this).
  - Breaks: assembly-resolve hooks, if used.
- [ ] XMLRPC and legacy grid protocols — delete with LLUDP
  - Replaced by: new RPC protocol; no grid-interop with OpenSim grids.
  - Breaks: any inter-grid message. Confirmed non-goal per plan.md.
- [ ] IAsyncResult / BeginInvoke async — mark for rewrite; delete any that were LLUDP-only
  - Replaced by: `Task`-based async / `await`.
  - Breaks: nothing functional — mechanical rewrite.
- [ ] Build still succeeds with demolition merged
- [ ] Surviving scene graph still loads and runs a smoke-test region
- [ ] Bump `<LangVersion>` in `Directory.Build.props` from 12 back to `latest` once YEngine is demolished (YEngine's `field` identifier collides with C# 14's `field` keyword)
- [ ] Decision landed on sandboxing (see Open Decisions)
- [ ] Decision landed on wire protocol (see Open Decisions)

---

## Phase 2 — Foundation

- [ ] Create `Eden.Shared` project (domain types: `Vector3`, `AvatarState`, `Prim`, `ItemId`, etc.)
- [ ] Move domain types from surviving `Eden/Framework` into `Eden.Shared`
- [ ] Stand up ASP.NET Core host (Kestrel) to replace removed HTTP server
- [ ] Implement wire protocol transport (server side)
- [ ] Replace config layer with `Microsoft.Extensions.Configuration`
- [ ] Replace logging with `Microsoft.Extensions.Logging` (Serilog provider)
- [ ] Migrate MySQL.Data to MySqlConnector
- [ ] Drop Prebuild tool; convert to native `.csproj` files + `Directory.Packages.props`
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

These gate Phase 2. See [plan.md](../_design/plan.md) for context.

- [ ] Script sandboxing model — trust / process isolation / WASM
- [ ] Wire protocol — WebSocket+MessagePack vs WebTransport/QUIC
- [ ] Content portability — fresh start vs import converters

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
