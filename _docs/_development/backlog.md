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

---

## Phase 1 — Demolition

Remove the layers we're not keeping. Each item is "delete, then build to see
what stops compiling, then delete the fallout."

- [ ] LLUDP stack — remove `Eden/Region/ClientStack/Linden/*`
- [ ] LSL frontend — remove LSL grammar, compiler frontend, `ll*()` function surface
- [ ] Legacy caps handlers — remove inherited caps endpoints (keep the dispatch shape for later)
- [ ] Custom HTTP server — remove `OSHttpServer` and the custom `HttpListener.cs`
- [ ] Mono.Addins — remove plugin-loader wiring and `.addin.xml` files
- [ ] Nini config — remove Nini dependency and its `IConfigSource` usage
- [ ] log4net — remove `ILog`-based logging calls (placeholder for Phase 2 replacement)
- [ ] BinaryFormatter — delete usages in `Eden/Framework/Util.cs`
- [ ] Thread.Abort / Thread.Suspend — delete from `Util.cs`, `DoubleDictionaryThreadAbortSafe.cs`
- [ ] AppDomain.CurrentDomain — delete from `Eden/Region/Application/Application.cs`
- [ ] XMLRPC and legacy grid protocols — delete with LLUDP
- [ ] IAsyncResult / BeginInvoke async — mark for rewrite; delete any that were LLUDP-only
- [ ] Build still succeeds with demolition merged
- [ ] Surviving scene graph still loads and runs a smoke-test region
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

---

## Phase 3 — Server rebuild

- [ ] Scene graph running against new transport
- [ ] C# scripting API surface (`async Task OnTouch(Avatar who)`, etc.)
- [ ] Roslyn-based script host; trust-model sandbox initially
- [ ] Physics re-integration against Bullet
- [ ] Asset / inventory / user services exposed over new wire protocol
- [ ] Integration tests that spin up a server and hit endpoints

---

## Phase 4 — Viewer

- [ ] `Eden.Viewer` — Godot 4 + C# project scaffolding
- [ ] Wire protocol client in viewer (consuming `Eden.Shared`)
- [ ] Basic scene rendering: avatars + prims + terrain
- [ ] Input and camera
- [ ] Minimal UI: chat, inventory, minimap, settings
- [ ] Asset streaming and caching

---

## Phase 5 — One-app UX

- [ ] Launcher menu: *Start a world* / *Join a world* / *Recent*
- [ ] In-process server for *Just me* mode
- [ ] Windows installer (code-signed)
- [ ] macOS `.dmg` (notarised)
- [ ] Linux AppImage
- [ ] Self-contained `dotnet publish` in CI

---

## Phase 6 — Hardening

- [ ] WASM sandbox for public scripts (Wasmtime)
- [ ] Performance pass: scene graph, physics tick, wire protocol
- [ ] Optional OAR/IAR import tool
- [ ] Public docs site
- [ ] Federation story (multi-region, multi-server)

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

## Chores (anytime)

- [ ] Delete `bin/Regions/`, `bin/ScriptEngines/`, `bin/*.db`, `bin/*.log` from the committed tree if any slipped in
- [ ] Audit `ThirdParty/` — which vendored libs are actually used after demolition?
- [ ] Move root `eden.sln` (Prebuild-only stub) under `Prebuild/` if keeping, else delete
- [ ] Scrub dead NAnt references (old `.nant/` folders, etc.) if any remain
