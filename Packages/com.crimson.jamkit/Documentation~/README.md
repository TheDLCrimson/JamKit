# JamKit

Reusable Unity Game Jam Toolkit: core services, UI/feel layer, and editor tooling extracted from prior jam projects.
This guide covers what has shipped through milestone M6 (Core Services, Validator/Preflight, UI + Feel Layer, Data Pipeline, AI skills, and the Lockdown shakedown sample).
It is a practical, fast-scanning catalog for a jam developer under time pressure, not a devlog or architecture essay.

Package: `com.crimson.jamkit`, namespace `JamKit` (runtime), `JamKit.Editor` (editor-only).

---

## 1. Quick start / Bootstrap contract

Four services - `GameState`, `AudioService`, `SaveService`, `UIScreenEffects` - are the "blessed" singletons.
They live as children of one `Bootstrap` prefab at `Assets/_Project/Resources/Bootstrap.prefab`, wired by serialized reference on `Bootstrap` itself.

- **Init order** (fixed, in `Bootstrap.Awake()`): `SaveService` → `AudioService` → `GameState` → `UIScreenEffects`. Later services can assume earlier ones already ran `Initialize()`.
- **Fallback spawn**: a `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` hook auto-instantiates the prefab from `Resources/Bootstrap` if no `Bootstrap` already exists in the loaded scene. This is what lets you press Play directly in *any* scene - Menu, Game, a demo/test scene not even in Build Settings - and still get all four services wired, not just Boot.
- **The one rule that matters**: because the fallback spawns *after* every scene object's `Awake`/`OnEnable` but *before* any `Start`, **never read a JamKit service from your own `Awake()`**. Read from `Start()` or later. `Singleton<T>.Instance` does not fabricate a stand-in if the real service hasn't run yet (see below) - reading too early gives you a loud `null`, not a silently broken instance.
- **Setup**: nothing to do for a normal scene in the M0 template (Boot already has Bootstrap placed). For a new standalone scene, the fallback handles it automatically as long as `Bootstrap.prefab` still exists at that Resources path.

---

## 2. Core utilities and services

### Singleton\<T\>

Abstract MonoBehaviour base (`Runtime/Core/Singleton.cs`): `public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour`.

- **Use when**: you need one well-known access point for a MonoBehaviour service. The four Bootstrap-owned services derive from it; so does `UIBlockerStack` (which is scene-local, not Bootstrap-owned - see UI section).
- **Contract - never fabricates.** `Instance` resolves an already-present, wired instance via `FindFirstObjectByType<T>()`, or returns `null`. It does **not** `AddComponent` a bare stand-in. Read `Instance` from `Start()` or later, never `Awake()` (see Quick Start above).
- A duplicate instance destroys itself in `Awake()`.
- `[SerializeField] private bool _dontDestroyOnLoad` - leave **off** for a component parented under Bootstrap (Bootstrap's own `DontDestroyOnLoad` already carries its whole child hierarchy). Turn it **on** only for a standalone singleton used outside Bootstrap (a demo scene, a module).

### EventBus

Static, type-keyed pub/sub (`Runtime/Core/EventBus.cs`). Event types are plain structs implementing the marker interface `IGameEvent`.

```csharp
public struct GameWonEvent : IGameEvent { }

EventBus.Subscribe<GameWonEvent>(OnGameWon);
EventBus.Fire(new GameWonEvent());
EventBus.Unsubscribe<GameWonEvent>(OnGameWon); // do this in OnDisable/OnDestroy
```

- `Subscribe<T>`, `Unsubscribe<T>`, `Fire<T>`, `HasSubscribers<T>()`, `GetSubscriberCount<T>()`.
- `Clear()` / `Clear<T>()` - `Clear()` (all types) is called automatically by `SceneLoader` on every scene transition, so static subscribers from a torn-down scene never leak forward. Don't call it yourself unless you specifically mean to wipe every subscriber.
- `Fire` is exception-isolated: one subscriber throwing is logged and does not stop the rest from receiving the event.
- `ActiveEventTypeCount`, `TotalSubscriberCount()` - read-only introspection, used by `DebugOverlay`.
- **Gotcha**: forgetting to `Unsubscribe` leaks a dead reference until the next scene's automatic `Clear()`.

### GameState

One of the four blessed singletons (`GameState.Instance`).

- `GamePhase CurrentPhase` - enum `Menu | Playing | Paused`.
- `Pause()` / `Resume()` - idempotent (no-op if already in that state); set `Time.timeScale` (0/1) and fire `GamePausedEvent`/`GameResumedEvent`. `Resume()` restores whatever `CurrentPhase` was active immediately before `Pause()` (e.g. pausing from `Menu` resumes to `Menu`, not hardcoded to `Playing`).
- `SetPhase(GamePhase)` - fires `GamePhaseChangedEvent`.
- `Restart()` - resets `Time.timeScale` to 1 and reloads the active scene through `SceneLoader`.
- **Use when**: you need pause/resume plumbing or a coarse phase flag. `UIBlockerStack`'s default pause callbacks already call `GameState.Pause`/`Resume` for you - you often don't need to touch this directly.

### AudioService

One of the four blessed singletons (`AudioService.Instance`).

- `PlaySfx(AudioClip clip, float volume = 1f)` - plays over a pooled round-robin ring of SFX `AudioSource`s.
- `PlayMusic(AudioClip clip, bool loop = true)` - crossfades over two dedicated A/B sources (~1s); safe to call again mid-crossfade, it cancels prior tweens first.
- `SetMusicVolume(float linear01)` / `GetMusicVolume()`, `SetSfxVolume(float linear01)` / `GetSfxVolume()` - mixer volume, persisted via `SaveService` automatically.
- **Setup**: needs an `AudioMixer` with exposed float params (default names `MusicVolume`/`SfxVolume`), a pooled SFX `AudioSource[]`, and two dedicated music `AudioSource`s - all wired on the Bootstrap prefab's `AudioService` child. If `_mixer` is left unassigned, `Initialize()` logs one clear diagnostic error naming the fix and degrades gracefully (volume calls skip the mixer but still persist to `SaveService`) instead of throwing - this is deliberate, so a missing mixer can never silently abort `GameState`/`UIScreenEffects` initialization later in `Bootstrap`'s fixed init order.
- **Gotcha**: SFX and music never share an `AudioSource` by design (a crossfade fighting a one-shot would sound wrong) - don't repurpose one ring for the other.

### SaveService

One of the four blessed singletons (`SaveService.Instance`). Every `Set*` writes through immediately (`PlayerPrefs.Save()` / `File.WriteAllText`), never deferred to quit.

- Typed PlayerPrefs helpers: `SetInt/GetInt`, `SetFloat/GetFloat`, `SetString/GetString`, `SetBool/GetBool`.
- `SaveJson<T>(string fileName, T data)` / `LoadJson<T>(string fileName, T defaultValue)` - `JsonUtility`-based blob at `persistentDataPath/{fileName}.json`. A missing or corrupt file returns `defaultValue`, never throws.
- **Gotcha**: `JsonUtility` only serializes public/`[SerializeField]` fields of a `[Serializable]` type - no properties, no `Dictionary`, and a `[SerializeReference]` polymorphic field serializes as its *declared* type, not the runtime type. Shape save data as a flat serializable class with fields only; flatten dictionaries into a list of entries.

### SceneLoader

Static (`Runtime/Services/SceneLoader.cs`): `SceneLoader.LoadScene(string sceneName)`.

- Fixed lifecycle per call: reset `Time.timeScale = 1` → (if a cover-hook owner is set) cover the screen to completion → `EventBus.Clear()` → `SceneManager.LoadSceneAsync` → `AfterSceneLoad` once the new scene has finished loading.
- **`BeforeSceneLoad` is completion-aware and single-owner.** It's an `Action<Action>`, not a plain event: `SceneLoader` invokes it with a continuation, and the owner must call that continuation once the screen is fully covered before the load actually begins. `UIScreenEffects` owns this (fades to opaque, then begins the load) - don't assign it yourself unless you are replacing that ownership.
- `AfterSceneLoad` is a plain multicast `event Action` - subscribe freely (fade back in, refresh UI, etc.).
- A `LoadScene` call while already transitioning is **rejected** (logged, no-op), never queued.
- A scene not registered in Build Settings is rejected up front, before any transition side effect runs.
- **Gotcha**: only Build-Settings scenes are valid `LoadScene` targets. A reference/demo scene like `Demo_Feel` cannot be loaded this way.

### MathUtil

Static (`Runtime/Util/MathUtil.cs`) - the `1 - exp(-k*dt)` framerate-independent decay idiom.

- `Decay(float k, float dt)` - the raw Lerp factor.
- `Decay(current, target, k, dt)` - float / `Vector2` / `Vector3` overloads that apply it directly.
- **Use when**: smoothing any value toward a target every frame. Prefer this over a raw `Lerp(a, b, k)`, which is framerate-dependent.

```csharp
_smoothed = MathUtil.Decay(_smoothed, target, responseRate, Time.deltaTime);
```

### Pools

Static (`Runtime/Util/Pools.cs`) - thin prefab-keyed wrapper over `UnityEngine.Pool.ObjectPool<GameObject>`. One pool is created lazily per distinct prefab reference, the first time it's requested.

- `Pools.Get(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)`.
- `Pools.Release(GameObject prefab, GameObject instance)`.
- **Use when**: spawning/despawning the same prefab repeatedly (bullets, VFX, pickups) without repeated `Instantiate`/`Destroy` cost.

---

## 3. Validation / preflight tooling

### JamKit project validator

Menu: **`JamKit > Validate Project`** (or call `JamKit.Editor.ProjectValidator.Validate()` directly for a `ValidationReport` with `Errors`/`Warnings`/`Passed`).

Checks: build-scene list non-empty with `Boot` first; no missing-script references in build scenes or their prefab dependency closure; no ungated `UnityEditor` references reachable from a player build; Active Input Handling is Input System Package only; warns (non-blocking) on known bloat folders (TMP Examples & Extras, PrimeTween Demo, imported package Samples).

The menu run logs a stable, greppable line: `[JamKit Validate] RESULT=PASS|FAIL errors=N warnings=N`.

### /preflight workflow

Run the `/preflight` skill for the full build-readiness gate (validate → build → launch → one pass/fail verdict). The authoritative step-by-step procedure lives in `.claude/skills/preflight/SKILL.md` - it is not duplicated here. General MCP-based verification methodology (EditMode tests, Play Mode runtime checks, what can't be verified headlessly) lives in `docs/verification.md`.

---

## 4. UI

### UIScreenEffects

One of the four blessed singletons (`UIScreenEffects.Instance`), **Bootstrap-owned** - it lives under `Bootstrap(Clone)/ScreenEffects/Overlay` at runtime (a Screen Space - Overlay Canvas + `CanvasGroup` + `Image` wired on the prefab). Full-screen fade/flash overlay; runs on unscaled time so it keeps animating while the game is paused.

- `FadeTo(targetAlpha, duration, color?, effectStartDelay, effectEndDelay, restoreColor, onComplete)`; helpers `FadeOut(duration, ...)`, `FadeIn(duration, ...)`, `FadeOutTo(color, duration, ...)`.
- `Flash(flashCount, flashDuration, maxAlpha, color?, ...)`; presets `QuickFlash`, `DamageFlash`, `HealFlash`, `WarningFlash`, `CriticalFlash`, `StunFlash`.
- `SetColor(Color)` / `GetColor()`, `Stop(bool resetAlpha = true)`.
- **Scene-transition ownership contract.** `UIScreenEffects` owns `SceneLoader`'s cover/uncover fade. **While a transition is covering the screen, every ad-hoc call above (`FadeTo`, `Flash` and its presets, `Stop`) is a documented no-op**: it logs a warning, invokes any `onComplete` you passed *immediately*, and does not touch the overlay - the transition exclusively owns the shared `CanvasGroup`/`Image` for that window. This is deliberate, not a bug to route around: it's the fix for a real defect where an ad-hoc effect firing mid-transition could silently strand a scene load forever. Don't call a flash/fade during a scene transition expecting it to visibly run; it will complete its callback but do nothing visible.
- **Setup**: none - use the Bootstrap-provided instance. Don't create a second `UIScreenEffects` in a scene.

### UIBlockerStack / UIBlockerBase

`UIBlockerStack` is a **scene-local** singleton (`Singleton<UIBlockerStack>`) - **not** one of the four Bootstrap-owned services. Place one per scene that needs a modal stack (e.g. a pause menu).

- `OpenBlocker(UIBlockerBase blocker)` - instantiates the prefab under `_blockerContainer` and pushes it. `CloseTopBlocker()`, `CloseBlocker(UIBlockerBase blocker)` (closes it and everything above it), `CloseAllBlockers()`. `GetBlockerCount()`, `IsBlockerActive(blocker)`, `IsBlockerTop(blocker)`.
- **Setup**: assign `_cancelAction` (an `InputActionReference` to the `UI/Cancel` action in the shared `Assets/Settings/JamActions.inputactions` asset); optionally `_escapeBlocker` (opened when Cancel is pressed with an empty stack - typically your pause menu); optionally `_hudLayer` (auto shown/hidden based on the stack's blockers).
- `OnPauseGameplay` / `OnResumeGameplay` (`Action` properties) default to `GameState.Instance.Pause()` / `Resume()`. Reassign only if pause needs to go somewhere else.
- `UIBlockerBase` is the abstract base for a blocker prefab's root component:
  - `PauseGameplay`, `HideHUD`, `RespondToEscape` - read-only properties backed by serialized fields, editable per-prefab in the Inspector.
  - Lifecycle callbacks: `OnOpen()` (first becomes top of stack), `OnPause()` (another blocker pushed above this one), **`OnResume()`** (becomes top again after the one above it closed), `OnClose()`, `OnUpdate()` (every frame while topmost).
  - **`OnResume` is distinct from `OnOpen` on purpose** - a revealed-again blocker gets `OnResume`, never a second `OnOpen`. Don't assume `OnOpen` fired just because a blocker is visible; check which callback you actually got.
  - `RequestClose()` - convenience for a close button; calls `UIBlockerStack.Instance.CloseTopBlocker()`.
- **Minimal usage**: subclass `UIBlockerBase` (an empty subclass is enough for default pause/HUD-hide/escape behavior - see `DemoPauseBlocker`), make it a prefab, then either assign it as `_escapeBlocker` or call `UIBlockerStack.Instance.OpenBlocker(yourPrefab)`.

### UIPanelAnimator

Reusable PrimeTween fade + scale show/hide for one UI panel.

- `Show()`, `Hide()`, `Toggle()` - each is a no-op if already in that state (guards toggle-spam); any in-flight sequence is stopped before a new one starts.
- `IsOpen` - read-only, the single authoritative flag, set synchronously at the start of each transition. It never desyncs from what's actually on screen, even under rapid toggling.
- **Setup**: put it on the panel's root with a `CanvasGroup` (auto-resolved via `GetComponent` if `_canvasGroup` is unassigned) and a `RectTransform` (`_panel`, defaults to its own transform). Tune `_duration`, `_hiddenScale`, `_useUnscaledTime`, `_deactivateOnHide`, `_startHidden` in the Inspector.

---

## 5. Feel

### SpringMath / CameraSpring

- `SpringMath.Spring(ref Vector3 current, ref Vector3 velocity, Vector3 target, float halfLife, float frequency, float timeStep)` - stateless, implicit-Euler damped harmonic spring step; unconditionally stable at any timestep. Reusable for camera, UI, recoil, follow-cams - not camera-specific.
- `CameraSpring` (MonoBehaviour) - positional-lag + pitch-kick camera bob (landing thud / impact feel). Call `Initialize()` once, then `UpdateSpring(deltaTime, up)` every frame.
- **Setup**: put it on a camera rig child - the parent transform moves normally, and `CameraSpring` resets its own `localPosition` to zero each frame and applies the spring offset on top. Tune `_halfLife`, `_frequency`, `_angularDisplacement`, `_linearDisplacement`.

### CameraLean

Acceleration-driven camera roll ("lean into the turn"). Call `Initialize()` once, then `UpdateLean(deltaTime, acceleration, up, strengthMultiplier = 1f)` every frame.

- Takes a plain `Vector3 acceleration` - not a character-controller type, so it works with any mover. Pass a larger `strengthMultiplier` for e.g. sliding/boosting.
- **Setup**: same rig-placement pattern as `CameraSpring` - chain them (rig → `CameraLean` → `CameraSpring` → Main Camera), as `Demo_Feel` does.

### FloatingCursor

Bobbing (optionally spinning/pulsing) marker driven by PrimeTween.

- `StartAnimation()` to begin. `SetBobbingIntensity(float)`, `SetBobbingSpeed(float)`, `UpdateCursorPosition(Vector3)` (smooth move, preserves the bob), `UpdateCursorPositionInstant(Vector3)`, `StopAllAnimations()`.
- **Setup**: enable `_enableRotation` / `_enableScalePulse` in the Inspector if wanted, then call `StartAnimation()` once after positioning the object.

### YAxisBillboard

Rotates to face the main camera on the XZ plane only (stays upright). No setup, no dependencies - add the component and it works. Uses `Camera.main`; no-ops if there isn't one.

### VolumeOverrideDriver

Drives one float-valued Volume override parameter between a min/max from a 0..1 target, framerate-independently (`MathUtil.Decay` under the hood).

- **Scope contract: float parameters only.** It drives a single `VolumeParameter<float>` (e.g. `Vignette.intensity`) via a typed selector delegate you supply - it is **not** a general multi-parameter or non-float Volume adapter, and uses no reflection.
- `Initialize<T>(Volume volume, Func<T, VolumeParameter<float>> selector, float min, float max, float response)` where `T : VolumeComponent, new()` - **instantiates the Volume's profile at runtime** and adds/finds the `T` override on that runtime copy. The shared profile *asset* is never mutated.
- `SetTarget01(float target01)` (clamped 0..1) each time the driving value changes. `IsInitialized` to check readiness.

```csharp
_volumeDriver.Initialize<Vignette>(_volume, v => v.intensity, 0.15f, 0.5f, 8f);
// ...
_volumeDriver.SetTarget01(Mathf.Clamp01(someValue));
```

- **Gotcha**: `Initialize` replaces `volume.profile` with a runtime instance - don't assign the Volume's `sharedProfile` afterward expecting it to still be the live one.

### PartExploder

Breaks the object's child hierarchy into loose rigidbody parts with an explosion impulse - a universal "prop shatters" effect.

- `Explode()` - outward, light preset. `Crash()` - heavier, adds colliders. `HasExploded` - read-only; a breakup only ever runs once per instance.
- **Setup**: tune `_explodeProfile` / `_crashProfile` (a `BreakupProfile`: force, radius, upwardsModifier, partMass, torqueRange, partLifetime, addCollidersIfMissing) in the Inspector; optional `_effectPrefab` / `_sound`. Every child transform becomes a loose part (each gets a `Rigidbody` if it doesn't already have one).
- **Gotcha**: no EventBus or gameplay coupling by design - if you need a "something broke" signal for other systems, fire your own event from the caller.

---

## 6. Data pipeline

Designer-authored content: polymorphic effects, item definitions, a slot inventory, level data, and two editor generators for bulk-creating content assets.

### SerializedPolymorphic + PolymorphicListDrawer

`SerializedPolymorphic` (`Runtime/Data/SerializedPolymorphic.cs`) is an empty `[Serializable] abstract` marker base. Derive your own polymorphic base from it, mark a field `[SerializeReference]`, and JamKit's `PolymorphicListDrawer` (`Editor/`) draws it: a dropdown of every concrete subclass (assembly-scanned, in any assembly), a per-element delete button in lists, an open-script shortcut, and the selected instance's own fields inline.

- **Use when**: you want designer-editable, composable, nestable data (abilities, effects, upgrades, AI actions) without a custom editor.
- The drawer derives the concrete base from the field type (unwrapping `List<T>`/array), so **one drawer serves every `SerializedPolymorphic` hierarchy** - you never write a per-type `[CustomPropertyDrawer]`.
- **Gotcha - `[SerializeReference]` nesting**: a nested polymorphic list (an effect that itself holds effect lists) round-trips only because each element is a `[SerializeReference]`. A plain `[SerializeField]` field of the base type would serialize as the *declared* type and lose the subclass.

### ItemEffect / IConditionalEffect / RandomChanceEffect

`Runtime/Data/` - the one effect hierarchy the toolkit ships (concrete leaf effects are game-specific and live in game code).

- `abstract class ItemEffect : SerializedPolymorphic` - `ApplyEffect(GameObject caster = null, GameObject target = null)` (abstract), `OnObtained()`/`OnRemoved()` (virtual).
- `interface IConditionalEffect { bool ConditionMet(); }` - implement on an effect that should auto-trigger.
- `RandomChanceEffect : ItemEffect` - composite: rolls `_chance`, runs its `_successEffects` or `_failureEffects` list (both `[SerializeReference]`, so effects nest arbitrarily).
- **Trap this fixes (`[Range]` without `[SerializeField]`)**: an inspector attribute like `[Range]`/`[Tooltip]`/`[Header]` does **not** serialize a field - only `public` or `[SerializeField] private` does. A `[Range(0,1)] private float _chance` (no `[SerializeField]`) never serializes, never shows in the inspector, and silently stays at its default (the donor's bug - every roll was 50/50). `RandomChanceEffect._chance` is `[SerializeField, Range(0,1)]`. Watch for this on any `[Range]`d private field.

### ItemDefinition

Immutable content SO (`Runtime/Data/ItemDefinition.cs`, menu **Create > JamKit > Item Definition**): `DisplayName`, `Description`, `Icon`, `MaxStackSize` (>= 1), and `Effects` (a `[SerializeReference]` effect list, drawn by the polymorphic drawer). `ApplyEffects(caster, target)` runs them all.

- Config/content only - **no prefab field** (an item definition is not a spawnable). It holds no runtime state; runtime counts live in `InventoryModel`.

### InventoryModel / InventoryView / InventorySlotView

A slot inventory where each slot holds a counted stack of one `ItemDefinition` (counted stacks inside positional, swappable slots).

- `InventoryModel` (`Runtime/Data/`, **plain C#, no MonoBehaviour**) - `new InventoryModel(slotCount)`. `AddItem(item, qty=1)` stacks into a matching non-full slot then the first empty slot, clamps to `MaxStackSize`, and **returns the leftover** that didn't fit. `RemoveItem(index, qty=1)`, `SwapSlots(a, b)`, `GetSlot(i)` (returns an `InventorySlot { Item, Count, IsEmpty }`), `Clear()`, `IsFull` (true only when every slot is occupied and every stack maxed), `SlotCount`. C# events `SlotChanged(int index)` and `InventoryChanged`. Being POCO, it is unit-testable in EditMode with no scene.
- `InventoryView` (`Runtime/UI/`) - MonoBehaviour binding a model to an assigned `InventorySlotView[]`. `Bind(model)` refreshes and subscribes (auto-creates a model sized to the slots if none bound by `Start`); raises `SlotClicked(int)`; drag-drop between slots calls `model.SwapSlots`. Plain MonoBehaviour with no pause/HUD coupling - works as a HUD or inside a `UIBlockerBase` panel.
  - **Preconfigured view count**: the view only ever displays the `InventorySlotView[]` you assign in the inspector - it never creates or destroys slot widgets. Size the model to match the assigned views. If you `Bind` a model with *more* slots than views, the surplus model slots are simply not shown; with *fewer*, the surplus views render empty. Either way the model itself is untouched, and a count mismatch logs a warning so the misconfiguration is visible.
- `InventorySlotView` (`Runtime/UI/`) - one slot widget: an icon `Image`, a `TMP_Text` count badge (hidden at count <= 1), a `CanvasGroup` for drag alpha. Canvas-scale-aware drag. Wire `_iconImage`/`_countText` per prefab; the owning `InventoryView` calls `Initialize`/`SetSlot`. During a drag the icon is lifted onto the root canvas and drawn last, so it always renders on top of every slot regardless of drag direction or `LayoutGroup` order, then restored to its slot (parent, position, tint, raycast) on release - you don't manage draw order yourself. Needs a parent `Canvas` for the lift; without one it falls back to in-slot movement.
- **Setup**: build a slot prefab (root: `Image` + `CanvasGroup` + `InventorySlotView`, child icon `Image`, child count `TMP_Text`), place N of them under an `InventoryView`, assign the array, then `Bind` your model. Needs an `EventSystem` with `InputSystemUIInputModule` in the scene for drag/click (Input-System-only project).

### LevelDataSO

`Runtime/Data/LevelDataSO.cs` (menu **Create > JamKit > Level Data**) - the "one scene, many levels" pattern without the grid dependency: a `List<LevelData>` where each `LevelData` has `LevelNumber`, `Subtitle`, and optional `StartingItems` (`ItemDefinition[]`). `GetLevel(index)`, `Levels`, `Count`. Deliberately generic - add game-specific level fields in game code, not here. A thin game-side loader (e.g. the sample `LevelLoader` in `Assets/_Project/Scripts/`) holds the SO + a current index and re-parameterizes the reused Game scene per level.

### DataAssetGenerator (editor)

Menu **`JamKit > Data Asset Generator`** (an `EditorWindow`) - bulk-generates one SO content asset per source prefab in a folder. Configure prefab (source) folder, icon folder, output folder, and target SO type; **Generate** creates/updates `{PrefabName}_Data.asset` for each prefab.

- Load-or-create, so re-running **updates existing assets, never duplicates** them.
- Assigns by convention only where the chosen SO type has the field: `displayName` (from the prefab name, `_` -> space) and `icon` (from `{iconFolder}/{PrefabName}.png`) always; a `prefab` field only if one exists (`ItemDefinition` has none, so it's skipped).
- Also callable headlessly: `DataAssetGenerator.Generate(prefabFolder, iconFolder, outputFolder, soType)` returns `{ Created, Updated }`.
- **Limitation**: prefab-as-source is retained from the donor - it enumerates prefabs to drive names. For SOs not backed by a prefab folder you'd want a different source; not generalized here.

### SpriteImportFixer (editor)

Menu **`JamKit > Fix Sprite Import Settings`** - pick a folder; every Texture2D under it is forced to `Sprite` / `SpriteImportMode.Single`, reimporting only those that changed. Also callable as `SpriteImportFixer.FixFolder(folder)` (returns the count changed). Run it on an icon folder before `DataAssetGenerator` so icons load as sprites.

---

## 7. Debug

### DebugOverlay

Toggleable on-screen IMGUI debug overlay: FPS, current `GameState.CurrentPhase`, EventBus subscriber counts (`EventBus.TotalSubscriberCount()` / `ActiveEventTypeCount`), and one-line cheat buttons.

- Toggle key: `_toggleKey` (default `Key.F1`, new Input System `Keyboard.current` - legacy `Input.GetKeyDown` would throw under Input-System-only handling).
- `DebugOverlay.AddButton(string label, Action action)` (static) registers a cheat button; `ClearButtons()` clears all.
- Add one via the Editor menu **`JamKit > Add Debug Overlay`** (drops a `DebugOverlay` GameObject into the open scene), or add the component yourself.

### Release-build exclusion contract

The entire `DebugOverlay` class body is compiled only under `#if UNITY_EDITOR || DEVELOPMENT_BUILD`. **It does not exist as a type in a release (non-development) build.** Every call site that references `JamKit.DebugOverlay` - including `AddButton` calls - must be wrapped in the same `#if UNITY_EDITOR || DEVELOPMENT_BUILD` guard, or that code will fail to compile in a release build.

**Gotcha**: `UnityEngine.Rendering` also defines a type named `DebugOverlay`. If a file has `using UnityEngine.Rendering;`, fully-qualify calls as `JamKit.DebugOverlay` to disambiguate (see `DemoFeelController` for the pattern).

---

## 8. Sample and reference scenes

### Game (Lockdown sample)

`Assets/_Project/Scenes/Game.unity` - **in Build Settings**, reached via Boot -> Menu -> Play. The canonical end-to-end sample: a complete, playable 3-level game ("Lockdown") built entirely on the template + toolkit, demonstrating the full intended workflow - boot, gameplay, win/lose, restart/next level - not an isolated feature exercise like Demo_Feel/Demo_Data below.

The loop: collect the key (`KeyPickup`, new Input System Interact action) to apply `UnlockDoorEffect` (a game-specific `ItemEffect` subclass) to the door (`LockdownDoor`); reach the door before a per-level countdown expires; see the result on a `UIBlockerStack` screen (`WinLoseBlocker`, reads/writes via `SaveService`); Continue to advance (`LevelLoader` + a 3-entry `LevelDataSO`) or retry the same level.

Exercises, in one working game: `EventBus` (game-specific events in `GameEvents.cs`), `GameState`/`SaveService`/`UIScreenEffects` together, a new `UIBlockerBase` screen, a new `ItemEffect` leaf, `LevelDataSO` with 3 levels, a new Input System action, and `DebugOverlay` cheat buttons ("Lockdown: Force Win"/"Force Lose").

`PlayerMove` / `CameraFollow` (game-side, not toolkit) are deliberately minimal, not-feel-tuned scaffolding - enough to play the sample, not a movement/camera solution. Replace them with your own controller or a genre module (see `Modules~`) for real gameplay feel.

Driven by `LockdownController` and friends (`Assets/_Project/Scripts/`) - plain Assembly-CSharp, no namespace (game-side sample code; not part of the package).

### Demo_Feel

`Assets/_Project/Scenes/Demo_Feel.unity` - **not in Build Settings** (a reference/exercise scene, not part of the shipped game flow; it cannot be a `SceneLoader.LoadScene` target).

Exercises every M3 UI/Feel component in one place:

- A camera rig (`CameraLean` → `CameraSpring` → Main Camera) oscillated to show spring + lean together.
- A `Volume` + `VolumeOverrideDriver` pulsing a Vignette.
- A `PartExploder` breakable (click to explode).
- A `FloatingCursor` and a `YAxisBillboard`.
- A `UIBlockerStack` pause menu (Esc, via `PauseBlocker.prefab` / `DemoPauseBlocker`).
- A `UIPanelAnimator` info panel.
- A `DebugOverlay` (F1) with cheat buttons wired to several of the above.

Driven by `DemoFeelController` / `DemoPauseBlocker` - plain Assembly-CSharp, no namespace (game-side demo code; not part of the package).

Open the scene and press Play directly - Bootstrap's `AfterSceneLoad` fallback spawns all four services even though the scene isn't in Build Settings - to see everything running.

### Demo_Data

`Assets/_Project/Scenes/Demo_Data.unity` - **not in Build Settings** - exercises the M4 data pipeline: an `InventoryView` of four `InventorySlotView`s bound to an `InventoryModel` seeded with authored `ItemDefinition` assets (drag-drop to swap slots), plus a button (or Space) that applies a composed item's effects, including a nested `RandomChanceEffect`, observable in the console. Backed by the sample content in `Assets/_Project/_DataTest/` and driven by `DemoDataController` (game-side, no namespace). Needs the scene's `EventSystem` + `InputSystemUIInputModule` for UI input.

---

## 9. Wiring a genre module into your scene

Genre modules (`Modules~/KCCController`, `InteractionKit`) never reference each other and never reference `GameEvents.cs` - each module exposes only plain C# events, so it stays portable across projects. Wiring one into this game is always the same three-step recipe.

### Step 1: install and place

Copy the module folder out of `Modules~/` into `Assets/` (see that module's own README for its installation section), then add its components to scene objects via the Inspector like any other component. No code yet.

### Step 2: write one scene-owned glue script per module

A glue script subscribes to the module's C# events and translates each one into an `EventBus.Fire<T>()` call using an `IGameEvent` struct declared in `GameEvents.cs`. It must live on a normal scene GameObject, **never on one of the four blessed singletons** - a singleton is `DontDestroyOnLoad`, so a delegate it held into a torn-down scene's module instance would keep that dead scene reachable after a load. Unsubscribe in `OnDisable` (subscribe in `OnEnable`), the same pattern as any other `EventBus` consumer.

```csharp
using UnityEngine;
using JamKit.Surveillance;

public class SurveillanceGlue : MonoBehaviour
{
    [SerializeField] private SurveillanceDirector _director;

    private void OnEnable()  => _director.FeedFocused += OnFeedFocused;
    private void OnDisable() => _director.FeedFocused -= OnFeedFocused;

    private void OnFeedFocused(SurveillanceCamera cam)
        => EventBus.Fire(new FeedFocusedEvent { FeedId = cam.Id });
}
```

Drop the script on a scene object (e.g. a `RoomGlue` GameObject) and drag the module component into the serialized field. Every shipped module's README repeats this same rule under its own Usage section - this is the one place it's written out with the code.

### Step 3: bridge two modules through a game-side adapter, never directly

When two modules need to cooperate, write one small adapter class in `Assembly-CSharp` that implements the consuming module's interface. Neither module ends up referencing the other.

The shipped example is InteractionKit's `Socket`, which has to take a prop out of the player's hands before seating it. It does not reference any carry implementation; it declares what it needs and the game supplies it:

```csharp
using UnityEngine;
using JamKit.InteractionKit;
using JamKit.KCC;

public class PlayerCarry : MonoBehaviour, ICarrier
{
    [SerializeField] private PlayerCharacter _character;   // KCCController's motor

    public bool      IsCarrying      => _held != null;
    public Rigidbody Held            => _held;
    public Rigidbody HoverTarget     { get; private set; }
    public string    CarryKeyDisplay { get; private set; }

    public void Drop() { /* restore physics, un-ignore the motor's queries */ }
}
```

`PlayerCarry` is free to reference the KCC module because it is game code, not a module. Doing the same thing inside InteractionKit would make one module depend on another. The real, shipped version is `Modules~/KCCController/Samples~/InteractionKitCarry/PlayerCarry.cs`; copy it into `Assets/_Project` once both modules are installed.

Not every module event needs to reach `GameEvents.cs` - only fire one when another system genuinely needs to react to it. A component that drives its own module directly from serialized references does not need the EventBus to talk to itself; cross into `GameEvents.cs` only for the beats another system cares about.

---

## 10. InteractionKit (interactive rooms without puzzle scripts)

`Modules~/InteractionKit` is the module to reach for whenever the player presses, opens, carries, or combines something. Its own README is canonical; this is the pointer.

An object holds one boolean state and pushes it to whatever it is wired to, so a wired room is authored in the Inspector rather than scripted. All three rooms shipped in GMTK 2026 were built this way with zero per-room puzzle code.

**Button that opens a door:** `GameObject > JamKit > Interaction > Switch`, then `> Door`, then press `+ Wire to…` on the Switch and pick the door. Two components, one link, nothing typed but the label.

**World side:** `Switch`, `Door`, `Gate` (threshold plus invert spans any/all/not), `Socket`, `SignalRelay` (UnityEvent escape hatch), plus `TriggerZone`, `BlockedBy`, `Carryable`, `SignalVisual` and `SignalSound`.

**Player side:** add `Interactor`, `InteractionPrompt` and `HoverHighlight` to the rig once. The Interactor owns the key, hold duration, range and arbitration, so a world object carries only its own label. Bind it to `Gameplay/Interact`; `Gameplay/Carry` drives the carry.

Two lifecycle rules worth knowing before you wire anything:

- **`Gate` pulls, everything else pushes.** A scene-level Gate reads nodes inside room prefabs, because a reference pointing out of a prefab does not survive Apply. Do not convert it to a push.
- **A `Door`'s authored transform is its CLOSED pose.** Author doors closed and let the open offset describe the travel.

Selecting a node draws its wiring in the scene view; `JamKit > Interaction > Show Wiring` draws every node's wiring at once (off by default).

Run `JamKit > Validate Interaction Wiring` on any scene using it. The checks ship with the module, not with the project validator, so they arrive and leave with the folder. The headline check catches a `Gate` with an empty input row, which is silently unsatisfiable at runtime and cost GMTK 2026 four unfinishable rooms for two days.

`Socket` needs to take a prop out of the player's hands, which it does through the `ICarrier` interface. The implementation stays game-side (the KCC module ships one under `Samples~/InteractionKitCarry`, to copy into `Assets/_Project`) because a carry talks to a specific character controller, and modules never reference each other. `Demo/DemoRig.cs` implements the same interface in about forty lines.


---

## See also

- `docs/verification.md` (repo root) - how to verify changes against the live Editor via MCP.
- `docs/prompts.md` (repo root) - paste-ready prompts for driving Claude against the toolkit during a jam.
- `Modules~/README.md` (package root) - optional genre modules, copied in on demand. Shipped: KCCController (first-person kinematic controller) and InteractionKit (signal-graph wiring plus the first-person interaction layer). Each module's own README is its canonical documentation.
