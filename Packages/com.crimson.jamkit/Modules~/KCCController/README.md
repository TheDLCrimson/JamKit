# KCCController

The canonical documentation for this module; installation is the section below.

## Purpose

A tuned first-person character controller: walk/crouch, a stand/crouch/slide state machine, air control, jump with coyote time and input buffering, and an overlap-checked uncrouch.
Extracted from the best first-person controller among several audited projects.

## Installation

Installing is a single folder copy. Budget about a minute.

### Requirements

- JamKit (`com.crimson.jamkit`) for `MathUtil`, and optionally `CameraSpring` / `CameraLean`.
- The Input System package, and a `Gameplay` action map (this repo ships `Assets/Settings/JamActions.inputactions`).
- KCC Core: **you must supply this yourself.** Kinematic Character Controller is a paid Unity Asset Store package and is not redistributed here.
- [Alchemy](https://github.com/annulusgames/Alchemy) (MIT), for the ability toggles' inspector layout. The template's `Packages/manifest.json` already installs it; in another project, add it through Package Manager by git URL.

### You supply KCC Core

[Kinematic Character Controller](https://assetstore.unity.com/packages/tools/physics/kinematic-character-controller-99131) is a paid Asset Store package. Its license does not permit redistribution, so it is **not** included in this repository and this module ships only JamKit's own code on top of it.

Buy and import KCC, then import **Core only** - the sample and tutorial content is not needed and is large. Core carries its own asmdefs, which is what `JamKit.KCCController` references by name.

If you do not own KCC, skip this module. Nothing else in JamKit depends on it.

### Step 1: copy the module in

Copy `KCCController/` out of `Packages/com.crimson.jamkit/Modules~/` into `Assets/`.
Unity ignores anything under `Modules~`, so the module costs zero import and compile time until you do this.

With KCC Core imported, the module compiles immediately after the copy: Core carries its own asmdef, so there is none to author.

### Step 2: add the input actions

The module ships no `.inputactions` asset of its own.
It binds to your existing shared asset through `InputActionReference`, so there is only ever one actions asset in the project.

Add these two to your **Gameplay** map if they are not already there (the template's `JamActions.inputactions` already has them):

| Action | Type | Suggested bindings |
|---|---|---|
| `Jump` | Button | `<Keyboard>/space`, `<Gamepad>/buttonSouth` |
| `Crouch` | Button | `<Keyboard>/leftCtrl`, `<Gamepad>/buttonEast` |

Then select `KccPlayer.prefab` and assign the four references on the `Player` component: **Move**, **Look**, **Jump**, **Crouch**.
They are already assigned in this repo.
Leaving any of them empty logs an error and disables the component rather than throwing a `NullReferenceException` every frame.

### Uninstall

Delete `Assets/KCCController/`; nothing outside the folder references it.
KCC Core is your own Asset Store import and is untouched by removing this module; with no module installed it is harmless dormant code, and whether to keep it is your call.

## Demo

Open `KCCController/Demo/Demo_KCC.unity` and press Play.

| Input | Result |
|---|---|
| WASD | Walk |
| Mouse | Look |
| Space | Jump (coyote time plus input buffering) |
| Left Ctrl | Toggle crouch; crouch while moving to slide |

The greybox course covers each behavior:

- **Flat ground**: walk, crouch, and crouch-while-moving to start a slide.
- **Slide ramp** (large, +Z): walk up, crouch at the top, slide down. Exercises slope gravity, slide friction, and steering.
- **Overhang** (1.2 m clearance): crouch under it, then try to stand. The overlap check vetoes the stand-up and re-crouches you.
- **Ledge platform**: walk off the edge and jump slightly late. Coyote time still gives you the jump.
- **Gap platforms**: jump the 4 m gap, then run off onto the descending landing slope while crouched to convert the fall into a slide.

The cursor locks on Play. Press Escape to free it in the Editor.

## Ability toggles

`PlayerCharacter` has one switch per ability, all on by default, so a game can ship without an ability rather than working around it.
They are inspector settings, and they take effect on the next frame, including while playing.

| Toggle | When off |
|---|---|
| `_walkEnabled` | Standing ground movement decays to a stop. |
| `_crouchEnabled` | Crouch requests are ignored; turning it off mid-crouch stands the character up through the normal uncrouch path. |
| `_airControlEnabled` | No steering in the air; the character keeps the momentum it left the ground with. |
| `_jumpEnabled` | Jump and jump-sustain requests are dropped at the input, so a buffered jump cannot fire later. |
| `_slideEnabled` | No slide. Slide also requires Crouch, because slides are entered from crouch; the toggle is greyed out while Crouch is off. |

While an ability is off, Alchemy hides its tuning fields, so the inspector only shows what is live.

## Tuning cheatsheet

All values are on the `PlayerCharacter` component. Shipped defaults in brackets.

- **Ground movement**: `_walkSpeed` [20], `_crouchSpeed` [7], `_walkResponse` [25], `_crouchResponse` [20].
  The response values are exponential-decay rates, not lerp factors: higher is snappier.
- **Air**: `_airSpeed` [15], `_airAcceleration` [70].
- **Jump**: `_jumpSpeed` [20], `_coyoteTime` [0.2], `_jumpSustainGravity` [0.4], `_gravity` [-90].
  `_coyoteTime` doubles as the jump-buffer window: a jump pressed up to that long before landing still fires on touchdown.
  `_jumpSustainGravity` scales gravity while rising and holding jump, so holding gives a higher jump.
- **Slide**: `_slideStartSpeed` [25], `_slideEndSpeed` [15], `_slideFriction` [0.8], `_slideSteerAcceleration` [5], `_slideGravity` [-90].
  Slides end and drop back to crouch once speed falls under `_slideEndSpeed`.
- **Stance**: `_standHeight` [2], `_crouchHeight` [1], `_crouchHeightResponse` [15], `_standCameraTargetHeight` [0.9], `_crouchCameraTargetHeight` [0.7].

The gravity values look extreme because they are: this controller is tuned for a fast, snappy movement shooter, not realistic physics.
Tune it with a controller in hand, not by reading numbers.

## Optional: stance-driven vignette

The donor drove a URP vignette from stance.
That pattern already ships in JamKit as `VolumeOverrideDriver`, generalized to any Volume override and correctly instantiating the profile rather than mutating the shared asset.
It is deliberately not part of this module, which would otherwise force a URP dependency on every consumer.

Add this to your own game code (`Assembly-CSharp` already references URP):

```csharp
using JamKit;
using JamKit.KCC;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class KccStanceVignette : MonoBehaviour
{
    [SerializeField] private PlayerCharacter _character;
    [SerializeField] private Volume _volume;
    [SerializeField] private VolumeOverrideDriver _driver;

    private void Start()
    {
        _driver.Initialize<Vignette>(_volume, v => v.intensity, 0.2f, 0.4f, 10f);
    }

    private void LateUpdate()
    {
        _driver.SetTarget01(_character.GetState().Stance == Stance.Stand ? 0f : 1f);
    }
}
```

## Optional: procedural camera feel, or Cinemachine

The demo rig already stacks JamKit's `CameraSpring` (landing thud) and `CameraLean` (roll into acceleration) under the look camera.
Both are optional on the `Player` component: leave them empty and the controller runs without them.

`Player._slideLeanMultiplier` [2.667] scales `CameraLean`'s base strength while sliding, so the camera rolls harder in a slide than at a walk.
It is a multiplier rather than a second strength value because `CameraLean` is toolkit code and deliberately knows nothing about stances.
With `CameraLean._strength` at its 0.075 default this yields 0.2 while sliding, which is what the controller is tuned around.
If you retune `CameraLean._strength`, the slide strength scales with it; set the multiplier to `desiredSlideStrength / _strength` to decouple them again.

`PlayerCamera` is a deliberately minimal look controller (accumulate euler, clamp pitch, follow the target).
To use Cinemachine instead, replace it with a vcam that follows `Character/CameraTarget`, and feed the vcam's rotation into `CharacterInput.Rotation`.
`PlayerCharacter` only needs a `Quaternion`; it flattens the pitch itself.

## Structure and rules

```
KCCController/
  README.md
  Runtime/
    JamKit.KCCController.asmdef   -> JamKit.Runtime, Unity.InputSystem, KinematicCharacterController
    CharacterTypes.cs             Stance, CrouchInput, CharacterState, CharacterInput, CameraInput
    PlayerCharacter.cs            the motor-driven controller
    Player.cs                     composition root: input -> character + camera
    PlayerCamera.cs               minimal look controller
  Demo/
    Demo_KCC.unity                greybox course (not in build settings)
    KccPlayer.prefab              the player rig
    Materials/
```

Everything lives in namespace `JamKit.KCC`.
The module references `JamKit.Runtime` and never another module, so modules can be copied in independently.

## Deviations from the donor

The movement and physics math is unchanged from the donor.
The extraction cleaned up leftover debug output, typo'd names, and the generated input wrapper, and swapped two smoothing calls for JamKit's `MathUtil.Decay` (a bit-identical substitution).
