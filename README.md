# JamKit

A Unity template for 96-hour game jams. Services, UI, feel and a data pipeline that already work, so hour one is spent on the game instead of on a save system.

Unity **6000.3.19f1**, URP, new Input System.

Everything here was extracted from finished Unity projects rather than written speculatively, and then proven by building a small game with it. What survived that is in `Packages/com.crimson.jamkit`.

## What you get

**Core services.** `EventBus` (static, type-keyed), `Singleton<T>`, `GameState`, `SaveService`, `AudioService` (pooled SFX ring plus crossfaded music), `SceneLoader`, and a `Bootstrap` that wakes them in a fixed order. Exactly four singletons, by design; everything else is inspector references.

**UI.** `UIBlockerStack` for pause menus and dialogs that nest correctly, `UIScreenEffects` for fades, `UIPanelAnimator` for tweened panels.

**Feel.** `CameraSpring`, `CameraLean`, `SpringMath`, `FloatingCursor`, `PartExploder`, `VolumeOverrideDriver`, `YAxisBillboard`, and a `DebugOverlay` with cheat buttons that strips out of release builds.

**Data pipeline.** Polymorphic `ItemEffect`s with a working inspector drawer, a unified inventory model/view, `LevelDataSO`, plus editor tools to bulk-generate ScriptableObjects and fix sprite import settings.

**A validator.** `JamKit > Validate Project` checks the things that kill jam builds: missing script references, editor APIs leaking into runtime code, a permissive Active Input Handling mode, build-settings drift.

**Modules**, in `Packages/com.crimson.jamkit/Modules~/`, copied in only when you need them:

- **InteractionKit** - wire interactive rooms by dragging components together. An object holds one bool and pushes it to whatever it is wired to, so buttons, doors, gates, sockets and trigger zones are all the same idea. Includes the first-person interaction layer: one `Interactor` on the player rig owns the key, hold window and arbitration.
- **KCCController** - a first-person kinematic character controller with walk, crouch, slide, jump and air control, each switchable per character. Requires the [Kinematic Character Controller](https://assetstore.unity.com/packages/tools/physics/kinematic-character-controller-99131) asset, which you must buy and import yourself; see that module's README.

## Getting started

```bash
git clone https://github.com/TheDLCrimson/JamKit.git
```

Open in Unity 6000.3.19f1 and press Play on `Assets/_Project/Scenes/Boot.unity`.

Dependencies resolve automatically through Package Manager, including PrimeTween from the npm scoped registry already configured in `Packages/manifest.json`.

Then read `Packages/com.crimson.jamkit/Documentation~/README.md`. It is the practical catalog: what exists, how to use it, and the lifecycle rules that bite.

### Try the modules

`InteractionKit` is already installed. Open `Assets/InteractionKit/Demo/InteractionKit_Showcase.unity` and press Play - an eight-station showcase covering every recipe the kit supports - button opens door, three breakers into an AND gate, momentary pulse latch, one-way commit, power gating, socket and carryable, trigger zone, inverted gate. WASD and mouse, hold **E** to interact, **F** to carry.

Other modules install by copying their folder out of `Modules~` into `Assets/`; each module's README covers its own setup.

## Sample scenes

| Scene | In build | What it shows |
|---|---|---|
| `Boot` | yes | Service bootstrap and the handoff to Menu |
| `Menu` | yes | Main menu on the blocker stack |
| `Game` | yes | A small key-and-door game built toolkit-only |
| `Demo_Feel` | no | Every UI and Feel component in one place |
| `Demo_Data` | no | Inventory, drag-and-drop, composed item effects |

## Conventions

`CLAUDE.md` carries the conventions the toolkit assumes: `[SerializeField] private` with `_camelCase`, tags and scene names as constants, framerate-independent smoothing through `MathUtil.Decay` rather than raw `Lerp`, no `Debug.Log` outside editor guards. It is written for an AI assistant but reads fine as a style guide.

`docs/verification.md` describes verifying changes against a live Unity Editor. `docs/prompts.md` is a library of paste-ready prompts for common jam tasks.

## What is deliberately not here

No Addressables, no Visual Scripting, no Timeline, no UniTask. A 96-hour jam does not pay back the setup cost.

Tweening is PrimeTween, never DOTween. Input is the new Input System only - Active Input Handling must stay InputSystem-only, never "Both".

Levels are data (`LevelDataSO`), not scenes. Scenes are Boot, Menu and Game.

## License

MIT, see `LICENSE`.

The toolkit and sample game are MIT. Third-party assets are **not** bundled: the KCCController module requires the Kinematic Character Controller asset from the Unity Asset Store, and PrimeTween installs from npm under its own license.
