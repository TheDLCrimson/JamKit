# Jam Prompt Library

Paste-ready prompts for driving Claude against this repo's toolkit seams during the 96 hours.
Each template names the actual JamKit seam it expects Claude to use and states its stop condition or human-owned decision.
These are operator prompts, not AI advice: fill the angle-bracket blanks and paste.
Where a slash command already covers the job, the template defers to it rather than duplicating instructions.

Ground rules baked into every template (from CLAUDE.md, do not restate them in your paste unless useful):
game code lives in `Assets/_Project`, plain Assembly-CSharp, no namespace;
`[SerializeField] private` + `_camelCase`, no public mutable fields;
events in `GameEvents.cs` only, constants in `GameConstants.cs` only;
no ungated `Debug.Log`; smoothing via `MathUtil.Decay()`;
never edit `Packages/com.crimson.jamkit` (frozen at `v1.0`) or a `.unity` scene without a human.

---

## 1. Mechanic scaffolds

Scaffold it against the toolkit seams, then stop for human wiring - no scene edits, no feel tuning.

```
Scaffold a new mechanic: <one-line description, e.g. "dash: short burst of speed on button press with a cooldown">
```

The skill scaffolds one MonoBehaviour in `Assets/_Project/Scripts/`, adds the event struct(s) to `GameEvents.cs`, registers a guarded `JamKit.DebugOverlay.AddButton` cheat, then STOPS with a human wiring checklist.
Wiring the component into a scene, binding input in `JamActions.inputactions`, and tuning feel are human-owned and happen after the scaffold.

If you need the mechanic to also react to an existing event, add one line to the paste:

```
It should listen for <ExistingEvent> and <do X>; subscribe in OnEnable, unsubscribe in OnDisable.
```

---

## 2. Effect / item authoring

New composable item behaviours are pure toolkit-seam boilerplate: delegate freely.

New effect subclass:

```
Add a new ItemEffect subclass `<Name>Effect` in Assets/_Project/Scripts/ (game code, no namespace, `using JamKit;`).
It extends `ItemEffect` and implements `ApplyEffect(GameObject caster = null, GameObject target = null)` to <describe the effect>.
Serialize any tunables as `[SerializeField] private _camelCase` (remember the `_chance`-class trap: a `[Range]` field with no `[SerializeField]` never serializes).
Do not author any asset or edit a scene; stop after the script compiles so I can compose it in the inspector.
```

New item asset composed from existing effects:

```
Author an `ItemDefinition` asset named `<Item>` under Assets/_Project/Data/ that composes these effects: <list, e.g. "a RandomChanceEffect at 25% wrapping a HealEffect">.
Use the polymorphic `[SerializeReference]` effect list; `ItemDefinition.ApplyEffects(caster, target)` is the trigger seam.
At least one item must be authored by a human through the real inspector to judge the drawer UX - if this is that item, stop and hand it to me instead.
```

Bulk catalog from prefabs:

```
Run the DataAssetGenerator (Editor window + static `Generate(...)`) over prefab folder <path> to produce `<SOType>` catalog assets in <output path>, linking icons from <icon path>.
Confirm each generated asset is named and icon-linked correctly and that a re-run updates in place rather than duplicating.
Report the count; do not commit.
```

---

## 3. UI screens

Menu and pause screens go through the UIBlocker stack seam, not bespoke canvas code.

```
Add a `<Screen>` menu screen as a `UIBlockerBase` subclass on the UIBlockerStack.
Follow the existing blocker pattern: inject the pause callback via GameState rather than calling it directly, use the Input System UI map for Escape, and use `UIPanelAnimator.Show()/Hide()` for the appearance.
Build the canvas/prefab is a human step (scene/prefab writes stay human, and MCP-built canvases need `renderMode = 0` plus a screenshot check - see CLAUDE.md); scaffold the script and stop with a wiring checklist.
```

For juice on an existing object, name the JamKit Feel components you want applied and ask for a proposal before any code.

---

## 4. Tuning sweeps

Generating parameter variations is safe to delegate; choosing the winner is not.

```
Produce <N> tuning variants of <component/asset> varying <fields, e.g. "dash speed 12/16/20 and cooldown 0.4/0.6/0.8">.
Output them as <LevelDataSO assets / a table of inspector values / a small config> so I can flip between them in play mode.
Do not pick a "best" set and do not bake final values - I tune feel with a controller in hand.
```

---

## 5. Bug reproduction first

Reproduce before fixing - this is the standing rule and the class of bug the audit shows AI diagnoses well only when forced to reproduce.

```
Bug: <symptom, e.g. "the dash sometimes fires twice on one press">.
First reproduce it end-to-end the way a player hits it (drive the real flow via MCP: `manage_editor` play mode, `execute_code` to trigger, `read_console` to observe) and show me the reproduction and the root cause before touching any code.
Suspect the audit's usual culprits: execution/frame order, serialization surprises (unserialized fields), and stale EventBus subscribers across scene loads.
Only after I confirm the cause, propose the smallest fix.
```

---

## 6. Submission assets

Copy and asset-selection are delegable; the upload and capture themselves are human-performed.

itch.io page copy:

```
Draft itch.io page copy for <game>: a one-line hook, a 2-3 sentence description, a controls list, and a "made with" credits block.
Base it only on what the game actually does right now - <paste the core loop / mechanic list>.
Do not invent features; this is a draft for me to edit, and I do the upload.
```

GIF / screenshot checklist:

```
Give me a capture checklist for the itch page: which 3-5 moments best show the core loop, the ideal clip length, and the Recorder settings to use.
Recording with Unity Recorder and uploading are manual steps I perform at hour 90 - just produce the shot list and settings, do not attempt to capture or publish anything.
```

Never let a submission prompt imply automated publishing: builds, GIF capture, and the itch upload are always human-run, tested from the itch page itself and not the editor.
