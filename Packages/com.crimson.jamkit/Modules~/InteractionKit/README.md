# InteractionKit

Wire interactive rooms by dragging components together, with no puzzle scripts.

An object holds one boolean state and pushes it to whatever it is wired to.
Buttons, doors, gates, sockets, trigger zones and relays are all the same idea with different behaviour attached.
All three rooms shipped in GMTK 2026 were authored this way, with zero per-room glue code.

The player half is here too: one `Interactor` on the rig owns the key, the hold window, the range and the prompt, so a world object states only its own label.

## Installation

Copy `Modules~/InteractionKit` into `Assets/`.
It references only `JamKit.Runtime`, `Unity.InputSystem` and `Unity.TextMeshPro`.
Delete the folder to remove it; nothing else in the project depends on it.

The `Demo/` folder is self-contained and can be deleted once you have read it.

## The 60-second version

Making a button that opens a door:

1. **GameObject > JamKit > Interaction > Switch.**
2. **GameObject > JamKit > Interaction > Door.**
3. On the Switch, press **`+ Wire to…`** and pick the door.

Two components, one link, nothing typed but the label.
Press the Toggle button in either inspector to test the chain without entering play mode.

To make it playable, add **Interactor**, **InteractionPrompt** and **HoverHighlight** to your player rig once, and bind the Interactor to `Gameplay/Interact`.

## Components

### Nodes (things with state)

| Component | What it is |
|---|---|
| `Switch` | Player-facing button, lever, breaker or terminal. `Toggle` / `Momentary` / `Once`. |
| `Door` | Moves itself when on. Slides, swings, or both. |
| `Gate` | Turns on when enough inputs are on. Threshold plus invert spans any / all / not. |
| `Socket` | Turns on when a matching `Carryable` is put in it. Optionally removable. |
| `SignalRelay` | Does nothing itself and hands its state to a `UnityEvent<bool>`. The escape hatch. |

Every node has `Outputs` (what it drives), an optional `Id`, and a `Blocked` flag.

### Not nodes

| Component | What it is |
|---|---|
| `TriggerZone` | A volume that sets a target node's state when something enters. |
| `BlockedBy` | Makes a node refuse interaction while another node says so. Points from the gated object to its source. |
| `Carryable` | Marks a prop as pick-up-able, plus an optional socket id. |
| `SignalVisual` | Tints a renderer by a node's state. Observes; add it to a switch and it just works. |
| `SignalSound` | Plays a clip on a node's state change. Observes, same as above. |

### Player rig

| Component | What it is |
|---|---|
| `Interactor` | The one input and arbitration authority. Owns key, hold seconds, range. |
| `InteractionPrompt` | The one world-space label. Shows interactables and carryables. |
| `HoverHighlight` | Inverted-hull outline on whatever is hovered. Needs an outline material. |

`Interactor` talks to world objects through `IInteractable` and never references a concrete node type, so the wiring half can be tested and demoed with no player in the scene.

## Carrying

`Socket` needs to take a prop out of the player's hands before seating it, so it asks for an `ICarrier`:

```csharp
public interface ICarrier
{
    bool      IsCarrying       { get; }
    Rigidbody Held             { get; }
    Rigidbody HoverTarget      { get; }
    string    CarryKeyDisplay  { get; }
    void      Drop();
}
```

The implementation stays game-side on purpose.
A carry is direct rigidbody physics against a specific character controller, and the shipping one (`Modules~/KCCController/Samples~/InteractionKitCarry/PlayerCarry.cs`, copied into `Assets/_Project` when you want it) calls into the KCC motor so a grabbed prop leaves its ground probes.
A module that referenced the KCC module would break the rule that modules never reference each other.

`Demo/DemoRig.cs` implements the whole interface in about forty lines if you want a reference, or a standalone rig.

## Recipes

| You want | Author it as |
|---|---|
| Button opens door | `Switch` → `Door`, Follow |
| Three breakers, all required | three `Switch` → `Gate` (threshold 3) → `Door` |
| Any one of several | `Gate` with threshold 1 |
| Something is true while NOT satisfied | `Gate` with Invert |
| Press once, stays open | `Switch` set to `Once`, or Pulse into the target |
| Momentary press | `Switch` set to `Momentary`, output Pulse |
| Needs power | `BlockedBy` on the gated object, pointing at the generator |
| Battery into a housing | `Carryable` (id) plus `Socket` (same id) |
| Key into a lock | the same thing. There is no lock component |
| Walk into a room and something happens | `TriggerZone` → any node |
| Drive a light, animator or game script | `SignalRelay` and its UnityEvent |
| See which breaker is on | `SignalVisual` on the switch |

## Verification

- **EditMode tests:** 22 cases in `Tests/`. Run from Test Runner or `JamKit.InteractionKit.Tests`.
- **Self-test:** press Play on the showcase scene. `InteractionKitSelfTest` builds all 12 recipes in code and logs one PASS/FAIL line each.
- **Wiring validator:** `JamKit > Validate Interaction Wiring` checks the open scene. It ships with the module rather than living in JamKit's frozen `ProjectValidator`, so copying the module in brings its checks along.
- **Showcase:** `Demo/InteractionKit_Showcase.unity`, eight hand-authored stations. WASD plus mouse, hold E to interact, F to carry, Escape to release the cursor.

Selecting any node draws its wiring in the scene view. `JamKit > Interaction > Show Wiring` additionally draws every node's wiring without selecting anything, which is how you audit a whole room at a glance. It is off by default.

## Gotchas

These are the decisions that look wrong until you know why. Changing them re-breaks something that was already fixed.

1. **`Gate` pulls; everything else pushes.** A scene-level Gate reads nodes inside room prefabs. A reference pointing *out* of a prefab does not survive Apply; reading inward does. Do not "fix" this into a push.
2. **A `Door`'s authored transform is the CLOSED pose.** Ticking State on snaps it open on Awake rather than baking a wrong closed pose. Author doors closed.
3. **`Door`'s swing is pre-multiplied**, so a leaf imported with a baked rotation still swings about the parent's axes.
4. **`SetState` no-ops on an unchanged value.** That is what stops a wiring cycle recursing forever.
5. **`SignalNode.OnValidate` re-push is guarded.** OnValidate also fires on every Play entry and for edits to *any* field. Without the guard every node in the scene re-fires the instant Play starts, before services finish initialising.
6. **`Socket.Seat` hides the prop itself** rather than through a relay, for that same OnValidate reason.
7. **`Socket` has a 0.6s post-eject cooldown**, or the still-overlapping prop snaps straight back in and can never be pulled out.
8. **`Socket` resolves its carrier on demand, never at Start.** Rooms are separate scenes and Unity refuses to serialise a cross-scene reference.
9. **`BlockedBy` points from the gated object to its source.** Twenty gated devices is twenty components; the source holds no list.
10. **`Switch` and `Socket` have no `[RequireComponent(typeof(Collider))]`.** `Collider` is abstract, so the attribute throws instead of helping. `Reset()` adds a sensible one and the validator reports any that end up missing.
11. **`HoverHighlight` with no outline material draws nothing**, rather than rendering every shell with Unity's magenta error shader.
12. **A `TriggerZone` needs the player to be a physics body.** Unity raises `OnTriggerEnter` only when both parties have a Collider and at least one has a Rigidbody, so a rig built as a bare transform walks through every trigger volume and nothing fires, with no error anywhere. A `CharacterController` or the KCC module already satisfies this; a hand-built rig may not. The validator checks it.
13. **Leaves override `DrawNodeGizmos`, never `OnDrawGizmosSelected`.** Unity dispatches gizmo messages to the most-derived declaration only, so a leaf declaring its own would silently hide `SignalNode`'s wiring gizmo and stop drawing wiring entirely.
14. **`SignalVisual` and `SignalSound` observe a node instead of being one.** Making them nodes would mean wiring a switch's Outputs to a component on that same switch, which is the friction this kit exists to remove.

## Migrating from the v1 Interactables module

| v1 | v2 |
|---|---|
| `Interactable` (base) | merged into `SignalNode`; `Id` and `Blocked` moved up |
| `SwitchInteractable` + `Switchable` | **`Switch`** (one component) |
| `DoorInteractable` + `Openable` | **`Door`** (one component) |
| `PoweredInteractable` | deleted; use `BlockedBy` |
| `InteractableRegistry` | deleted; nothing consumed it once replay was cut |
| `Receptacle` | **`Socket`** |
| `SignalTrigger` | **`TriggerZone`** |
| `SignalIndicator` (demo tier) | **`SignalVisual`** (runtime) |
| `HoldToInteract` (15 fields, per object) | **`Interactor`** (player rig) plus `Switch.Label` |
| `CarryPrompt` + `HoldToInteract`'s prompt | **`InteractionPrompt`** (player rig) |

`Activate(actionId, source)` is gone. It existed so a replayed tape could re-fire a switch idempotently; replay was cut, and `SwitchMode` spans everything the shipped rooms used.

There is no automated upgrader. Writing one to swap components across scenes while preserving references is multi-day work for a handful of consumer files, so port by hand using the table above.
