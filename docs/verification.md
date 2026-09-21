# JamKit Verification Procedure

How to verify a milestone (or any change) against the live Unity Editor via MCP.
This is the standing procedure: it says *how* to verify, so results are reliable and repeatable.

Two layers: EditMode tests (automated, pure logic) and runtime Play Mode checks (lifecycle, wiring, integration).
Never use headless batch mode for routine verification (batch-mode runs can hang on domain reload).
Keep one interactive Editor open on the project and drive it through MCP.

## Setup gotcha: MCP connects in one Claude Code client but not another (Windows)

Hit more than once when cloning this template onto a new project/machine.
`claude mcp add` writes the UnityMCP server entry keyed by the project's folder path as a literal string in `~/.claude.json`.
On Windows, different Claude Code clients can normalize the same folder to differently-cased drive letters - for example a terminal session resolves to `D:/Code/...`, while the VSCode extension resolves to `d:/Code/...`.
Each casing is a distinct dictionary key, so this produces two separate project entries: one with `UnityMCP` configured, one an empty stub with no servers.

Symptom: one client's `/mcp` shows UnityMCP connected with tools; another client's `/mcp`, opened on the exact same folder, shows "No MCP servers configured."
This is not a Unity-side problem - the bridge and port are fine.

Fix: open `~/.claude.json`, find both drive-letter-casing variants of the project's path (search for the project folder name), and copy the working `mcpServers.UnityMCP` block into whichever entry is missing it.
Then reconnect (`/mcp`) in the client that was missing it.

## Step 0: compile and read the console before anything else

After editing scripts, force a compile and wait for the Editor to settle before running tests or entering Play mode.

- `refresh_unity` with `compile=request`, `mode=force`, `wait_for_ready=true`.
- Then `read_console` filtered to errors.
- Any error means fix before proceeding; do not run tests against a project that failed to compile.
- Note: `Assembly for Assembly Definition File 'Packages/com.crimson.jamkit/Editor/JamKit.Editor.asmdef' will not be compiled, because it has no scripts associated with it.` was a known-benign warning **before M2 only**, while `Editor/` was still empty.
  M2 populated `Editor/`, so this warning should no longer appear. If you see it now, the `Editor/` scripts are not being picked up - investigate it as a real failure rather than ignoring it.

## Step 1: EditMode tests

Pure-logic services only (EventBus, SaveService, InventoryModel in M4).
Lifecycle code (SceneLoader, Bootstrap, AudioService) is verified in Play Mode, not here.

- `run_tests` is asynchronous: it returns a `job_id` immediately.
  Poll with `get_test_job` using `wait_timeout` of 30-60s; do not busy-loop.
- EventBus is static: clear it in `[TearDown]` or bindings leak between tests and cause order-dependent failures.
- Tests that touch `PlayerPrefs` or `persistentDataPath` must namespace keys with a `JamKitTests_` prefix and delete them in `[TearDown]`, so they never pollute real save data.
- A test that deliberately logs an error must `LogAssert.Expect` it, or NUnit fails the test on the unexpected log.
- If a test depends on a Unity API's edge behavior, probe it first with `execute_code` to pick a deterministic input.
  Example from M1: `JsonUtility.FromJson` is lenient with some malformed strings and throws on others; the corrupt-file test uses `"not-json-at-all"` because it was confirmed to throw reliably.
  Guessing the input risks a flaky test.

## Step 2: runtime verification (Play Mode)

For scene flow, singleton lifecycle, prefab wiring, and anything that only exists at runtime.

- Clear the console, load the entry scene, then `manage_editor play`.
- Assert with `execute_code`:
  - reflection to read private fields (e.g. `SceneLoader._isTransitioning`, `AudioService._sfxSources`),
  - `FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length` for instance counts (idempotency: exactly one Bootstrap and one of each persistent service).
- `execute_code` runs as a method body compiled with CodeDom (C# 6) by default: no inline `class`/`using` declarations, no top-level statements; use fully qualified type names (`UnityEngine.Object`, `System.Reflection.BindingFlags`).
  An inline `[Serializable] class` will fail to compile; use an existing serializable type when probing.
- Scene loads are asynchronous: issue the load in one `execute_code` call, then check `SceneManager.GetActiveScene().name` in the next call.
- Always `manage_editor stop` when done, and delete any assets generated only for verification (screenshots, temp files, scratch scripts).
- Verify adversarial and failure paths, not only the happy path (e.g. load an unregistered scene name and confirm navigation still recovers).
- Exercise the real wired path: invoke a `Button`'s `onClick`, not the controller method directly, so the UnityEvent wiring is part of what is tested.

## Play Mode gotcha: the player loop can freeze while the Editor is unfocused

Hit during M8 RecorderKit verification (2026-07-19).
`manage_editor play` succeeded and `isPlaying` read true, but `Time.frameCount` sat at 3 and `timeSinceLevelLoad` at 0.04s across 30+ real seconds: the player loop only advanced one frame per incoming MCP request, because the unfocused Editor was not pumping it.
OS-focusing the Editor window (PowerShell `AppActivate`) did not reliably resume it either.

Reliable fix: drive the loop programmatically with `UnityEditor.EditorApplication.Step()` from `execute_code`.
Each `Step()` advances exactly one frame at the fixed timestep (60 steps = ~1.2s of game time at 0.02s), which makes timed runtime checks *more* deterministic than wall-clock waiting.
Pattern: enter play, `Step()` in bursts of 100-300 until the target game time, assert state via `execute_code`, read the console, then `manage_editor stop` (stepping leaves the Editor paused; stop clears it).
Prefer this over `Start-Sleep` polling for any Play Mode check with a timing component.

## Step 3: verify behavior, not just properties (MCP-authored content)

MCP writes can report success while the result is wrong.
Several M1 defects passed a property read but failed in reality; always confirm the actual behavior.

- A `Canvas` added via `manage_components`/`manage_gameobject` defaults to World Space render mode, not Screen Space - Overlay.
  Set `renderMode = 0` and confirm the UI with a `manage_camera` screenshot, because a correct-looking `sizeDelta` can coexist with a wrong render mode.
- `AudioSource`s added via MCP have `outputAudioMixerGroup = null`, so mixer volume controls silently do nothing.
  Assign the group and verify routing at runtime (read each source's `outputAudioMixerGroup.name`).
- A component-property write can return `success: true` yet mis-target: an M1 `Button.onClick` set through the generic property setter bound to the GameObject instead of the controller component.
  Read the write back (`GetPersistentTarget`/`GetPersistentMethodName`) or wire it through `UnityEditor.Events.UnityEventTools.AddPersistentListener` and verify.

## Step 4: what cannot be verified headlessly

Flag these for a human in the milestone report; do not claim they passed.

- Audible audio: `AudioSource.isPlaying == true` and correct mixer routing do not prove the clip sounds right (no clicks/pops, correct volume balance).
- Juice and feel quality: camera spring, lean, tween easing, and transition timing need a human eyeball pass.
- Visual layout correctness beyond "it renders": a screenshot catches gross errors, but final polish is a human judgment.
