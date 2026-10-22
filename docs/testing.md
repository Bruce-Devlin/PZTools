# Mod tests and isolated playtests

Use **Set up game tests** in Test Explorer for a disposable single-player or local dedicated-server profile. Existing advanced profiles, support adapters, steps, remote calls, actor helpers and cleanup hooks remain available. Test sources under `.pztests` and development metadata under `.agents`, `.codex` and `.pztools` are excluded from runtime deployment.

## Failure containment

`t:equal`, `t:truthy`, `t:fail` and timed-out `t:waitUntil` record a failure and yield to the harness. Code after the failing check does not execute. Every case has one finalization path: `afterEach` hooks run in registration order, then registered cleanup runs in reverse order, exactly once. Cleanup errors are appended to the original failure message. JSON case results include `FailureOrigin` (`assertion`, `native_phase`, `harness` or `cleanup`).

Use a synchronous protected phase for risky native calls:

```lua
test("native action", function(t)
    local item = t:phase("create item", function()
        return InventoryItemFactory.CreateItem("Base.Axe")
    end)
    t:truthy(item, "Expected an axe")
    t:waitUntil(function() return item:getCondition() > 0 end, 5)
end)
```

Phase functions, wait predicates, `t:throws` callbacks, teardown and cleanup must be synchronous. Do not call `coroutine.yield`, wait, remote operations or other yielding helpers from these callbacks. `waitUntil` rejects an attempt to yield inside a protected phase. A yielding test body must never be wrapped in `pcall`.

An unprotected native exception can corrupt Kahlua's coroutine state. The harness detects an interrupted resume on the next callback, finalizes the case, emits one infrastructure error and stops that endpoint. It does not resume a damaged coroutine or claim safe continuation. Some game builds instead log exposed Java exceptions and return success to `pcall`. Where the game exposes its Lua debugger error counter, the companion observes counter changes around protected callbacks and coroutine resumes: a logged error fails a protected phase, while an unhandled increase ends the endpoint. This prevents a logged native exception from silently producing a passing case. It cannot prevent code inside an unprotected callback from running before the callback returns. If the VM stops invoking callbacks entirely, the host timeout remains the final bound.

Remove old project-local replacements of `PZT.context` or `PZT.tick` when adopting this harness. Existing support files are preserved; they are not automatically rewritten.

## Runnable endpoint diagnostics

Test Explorer and `pztools_get_run_status` expose per-endpoint phase, heartbeat timestamp, last observed simulation progress, tick count, pause state, living-player presence, support-registration state, current case and blocking reason. Final reports also retain these states in `results.json` and `endpoints.json`, including on timeout or cancellation.

The companion uses `OnTickEvenPaused` when available for a heartbeat and watchdog, independently of normal `OnTick` simulation progress. It also emits heartbeats from normal ticks for runtimes without that hook. Missing pre-world callbacks remain an unknown screen state, never an inferred Click to Start screen. Pause is nullable when the runtime cannot expose speed controls.

The barrier opens only after every endpoint has registered support, observed at least two simulation ticks, reported a living character (or a server endpoint), and has fresh heartbeat and simulation observations within five seconds. The companion includes its event timestamp so old buffered events cannot masquerade as fresh readiness. One endpoint waiting on a screen, paused or stalled holds the barrier closed. Existing single-player startup-click assistance remains available. No pause dismissal, guide closing, resurrection or fixture co-location is added; intentional pauses and deaths during a case are reported without repair. Case timeouts still use wall time.

## Runtime snapshots

**Preview payload** in Playtest Profiles lists included project files and pruned excluded directories. At launch, PZTools captures the project and all included local dependencies once before deploying any endpoint. It holds read locks across the capture to prevent mixed file contents, rejects an active writer with its path, and checks for added or removed runtime files before accepting the snapshot. Excluded research folders are pruned without enumeration. Legitimate `.java` and `.class` files outside excluded directories remain eligible.

Every endpoint receives those captured payloads, with a `SourceSnapshotId` in its deployment manifest. The workspace and game-test report expose the snapshot ID and `PayloadManifestPath`. `snapshot.json` contains source paths, inclusion previews, payload manifest references, byte count and any excluded Workshop guarantees. Changes to original sources after capture do not affect the run. Snapshot copies are verified against their captured manifest before replacing an endpoint deployment; client/server parity checks remain in place after companion injection. Test companion files are separate and never become production payloads.

Steam-managed Workshop dependencies remain outside the local snapshot guarantee and are explicitly identified. The snapshot needs an additional copy of the filtered local payload alongside endpoint copies and transient deployment staging. It follows workspace retention: retained sessions keep their snapshots; ordinary disposable workspaces are cleaned by the existing session lifecycle. A failed capture never launches a game.

## Verification

```powershell
dotnet test Tests/PZTools.Testing.Tests/PZTools.Testing.Tests.csproj -m:1 -nodeReuse:false -p:UseSharedCompilation=false
Tests/PZTools.KahluaSmoke/Run.ps1 -GameRoot 'path-to-installed-game' -Javac 'path-to-javac.exe'
```

The Kahlua smoke runner compiles a small Java host and executes the installed game's original Kahlua classes, bundled Java and `stdlib.lua`. It prints SHA-256 fingerprints. It verifies assertions, timed-out waits, a synchronous Java exception, cleanup failure, subsequent passing cases and one unrecoverable unprotected exception. It does not start a game, extract or ship game binaries, or prove native world/event behavior. The expected unrecoverable fixture prints one host exception and then passes only if the harness finalizes once and stops.

On 22 October 2026, both fixtures passed against installed `projectzomboid.jar` SHA-256 `E1A69EB743EDE60B213A0FE7F8B83D4FCAB773036D256CC4543A336F3B058A33`, with `stdlib.lua` SHA-256 `583C294996AE65310FB1191B9A70584B59C163A0F2D257AF53F06728BA3931EB` and bundled Java 25.0.1. This identifies the binaries tested without inferring their version from the older reference-source cache.

Live acceptance still requires the supported game build: a Survival Guide pause, no playable character, two clients with only one in-world, then both admitted; deliberate in-case death/pause; and the failure suite under actual engine callbacks. Automated Lua/event fixtures and the direct Kahlua host are separate evidence from those live journeys.
