# JamKit - Unity Game Jam Toolkit

## Session context

This repo is the reusable Unity Game Jam Toolkit ("JamKit") plus a small sample game that exercises it.
Built for 96-hour jams and small teams working with AI agents, on Unity 6000.3.19f1.

## Context loading / documentation routing

Default at session start: read this file, then read only what the current task needs.

- `Packages/com.crimson.jamkit/Documentation~/README.md` - the authoritative catalog of what JamKit provides: setup, usage, and current behavioral contracts. This is the default answer to "what tool exists / how do I use it".
- Module READMEs (`Packages/com.crimson.jamkit/Modules~/<Module>/README.md`) - the **canonical source of truth for each module** (API, usage, contracts, verification, installation). Read one before installing or coding against that module.
- `docs/verification.md` - the live-Editor verification procedure. Read before any work that needs Unity verification.
- `docs/prompts.md` - paste-ready prompts for driving Claude against the toolkit seams during a jam. Read when starting a delegable jam task (mechanic scaffold, effect/item authoring, UI screen, tuning sweep, bug reproduction, submission assets).

## Working rules

- Complete only the task explicitly requested. Do not invent generalized infrastructure (registries, extra abstraction layers, unplanned services) to "future-proof" it - this repo rejects generalization beyond the observed use case.
- Never mark work done based only on code inspection when it can be tested - run the test.
- When a change ships a new toolkit tool or changes an API, setup step, lifecycle rule, dependency, or behavioral contract, update the JamKit guide (`Packages/com.crimson.jamkit/Documentation~/README.md`) or the module README in the same change. Verify every API name and example against current source. The guide is a fast-scanning catalog for a time-pressured jam developer, not a changelog.
- When reporting finished work, give the user a manual Unity Editor checklist for what tooling cannot see (Package Manager state, visual correctness, popups/dialogs, menu registration, actually pressing Play).

## Non-negotiable decisions (do not relitigate)

- Toolkit code lives in `Packages/com.crimson.jamkit`, namespace `JamKit`, split into `JamKit.Runtime` / `JamKit.Editor` asmdefs.
- Jam game code lives in `Assets/_Project`, plain Assembly-CSharp, **no namespaces** (intentional asymmetry).
- Tweening: **PrimeTween**, installed from the npm scoped registry in `Packages/manifest.json` (its license forbids redistributing the tarball). Never DOTween.
- Input: **new Input System only**; Active Input Handling must stay InputSystem-only, never "Both".
- Packages: Cinemachine 3.x and Recorder yes; Addressables, Visual Scripting, Timeline, Odin, UniTask, Post Processing v2 **no**; AI Navigation only if the theme needs it.
- Events: one static type-keyed EventBus for cross-system flow; all event structs in `Assets/_Project/Scripts/GameEvents.cs`; `EventBus.Clear()` on scene transitions. No ScriptableObject event channels.
- Exactly four singletons (`JamKit.Singleton<T>`): GameState, AudioService, SaveService, UIScreenEffects. Everything else is inspector references.
- ScriptableObjects are immutable config/content only, never runtime state; persistence goes through SaveService only.
- Scenes: Boot / Menu / Game, all in build settings from day zero; levels are LevelDataSO data, not scenes.
- Genre modules (KCCController, InteractionKit) live in `Modules~` (Unity-ignored) until copied in; each has its own asmdef; modules never reference each other.
- The toolkit package is stable: never modify `Packages/com.crimson.jamkit` unless the user explicitly approves the change.

## Where this came from

JamKit was extracted from five finished Unity projects, keeping only patterns that had already survived a shipped build.
The extraction sources are private, so they are not referenced here; what survived the audit is in `Packages/com.crimson.jamkit`.

## Coding conventions

- `[SerializeField] private` fields, `_camelCase` for privates; no public mutable fields.
- All tags, layers, PlayerPrefs keys, and scene names as constants in `Assets/_Project/Scripts/GameConstants.cs`.
- `[FormerlySerializedAs]` on any serialized-field rename.
- No `Debug.Log` outside `#if UNITY_EDITOR` guards or the DebugOverlay; no OnGUI in shipping code paths.
- Framerate-independent smoothing via `MathUtil.Decay()` (the `1 - exp(-k*dt)` idiom), not raw `Lerp(a, b, k)`.
- Toolkit code gets XML docs on public APIs; game code in `_Project` does not need them.
- Markdown files: no em dashes; one sentence per line in prose.

## AI working rules for this repo

- Never modify `ProjectSettings/` or `Packages/com.crimson.jamkit` without explicit approval.
- Never commit or push unless explicitly asked.
- One writer per scene file, ever; prefer prefab edits over scene edits.
- Unity MCP reads: always allowed (`read_console`, resources, scene inspection).
- Unity MCP writes, before the jam: Claude may create and wire scenes, GameObjects, prefabs, and ScriptableObjects via the MCP editor-API tools (`manage_scene`, `manage_gameobject`, `manage_components`, `manage_prefabs`, `manage_scriptable_object`), with the human reviewing in the open Editor. Never edit scene/prefab YAML as raw text.
- Unity MCP writes, during the jam: prefabs and ScriptableObjects only, never open scenes - more than one writer (human or AI agent) on a scene file is exactly the merge-conflict class the one-writer rule exists for.
- Verification runs against the live Editor via MCP: `run_tests` for EditMode suites, `manage_editor` play mode + `execute_code` + `read_console` for runtime checks, `manage_build` for builds. Do not launch headless batch-mode Editor processes for routine verification (they can hang on domain reload); batch mode is a fallback only when no interactive Editor is available. Follow `docs/verification.md` for the full procedure and the MCP-authored-content gotchas (Canvas render mode, AudioSource mixer routing, verifying property writes) that produce false passes.
- Building UI Canvases via `manage_gameobject`/`manage_components` instead of Unity's `GameObject > UI > ...` menu: `AddComponent<Canvas>()` defaults `renderMode` to World Space (2), not Screen Space - Overlay (0). Always explicitly set `renderMode = 0` right after adding the Canvas, or the whole UI renders as a giant, perspective-distorted plane floating in the scene instead of a flat 2D overlay. Confirm any MCP-built UI visually with a `manage_camera` screenshot before reporting it done - reading component properties back is not enough to catch this class of bug.
- UI/Canvas work is an error-prone hot spot beyond render mode too (e.g. a fresh `RectTransform`'s default 100x100 centered anchors, not stretch-to-fill, silently shrinking a container): never trust UI defaults, always set stretch/anchors explicitly and confirm any new or edited UI visually before calling it done.
- During the jam: reproduce bugs before fixing them (scope and mechanic-feel ownership is spelled out in the delegation policy below).

## AI delegation and parallel-agent policy

Delegation policy:

- **Always delegate** (AI is faster and quality-sufficient): boilerplate against known toolkit seams (new `IGameEvent` structs plus Fire/Subscribe wiring, new `ItemEffect` subclasses, new catalog SO types for `DataAssetGenerator`, UIBlocker-stack menu screens); content (LevelDataSO assets, tuning sweeps, placeholder-art lists, itch copy); defect archaeology (reproduce first, explain the cause, then propose the fix); chores (validate/build runs via `execute_menu_item` + `manage_build`, `read_console` compile-fix loops, git hygiene, the `/cleanup` sweep).
- **Never delegate**: core mechanic *feel* (slide friction, jump curves, camera spring frequency - controller-in-hand work only); scope and theme-interpretation decisions (AI enumerates options, humans cut); anything touching physics timing or execution order without a human replaying it.
- **Mandatory human review**: scene/prefab changes made via Unity MCP (YAML diffs corrupt easily and are hard to eyeball); any change inside `Packages/com.crimson.jamkit` during the jam; the pre-submission build.

Parallel agents:

- **Valuable** for independent, seam-separated tasks (one agent on the menu flow while another writes item effects - the UIBlocker and EventBus seams keep them from colliding) and for overnight review passes.
- **One agent per asmdef/folder**, and stricter still: shared single-file coordination surfaces (`GameEvents.cs`, `GameConstants.cs`, this `CLAUDE.md`) and any `.unity` scene get **exactly one writer at a time**, even within a single folder - two agents appending to `GameEvents.cs` collide the same way two writers on one scene do.
- **Budget**: parallel bursts are for pre-jam work and the hour 0-12 scaffolding window. From jam hour 12 on, default to a single interactive session; spawn a background agent only for a task with over 30 minutes of autonomous work.
