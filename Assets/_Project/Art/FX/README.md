# AnalogFX

The analog/CRT full-screen look: scanlines, grain, chromatic offset, glitch.
Game-side (`Assets/_Project/Art/FX`), not toolkit code.
Built 2026-07-22 for the GMTK 2026 entry.

**The shader shipped here is deliberate programmer art.** Its job is to prove the pipeline and the parameter contract, not to look good.

## For Artist: what to replace, and what not to touch

Replace **only** the shader (and/or point `AnalogFX.mat` at a different shader or Shader Graph).
Everything else is designed to survive that swap untouched.

| File | Yours to change? |
|---|---|
| `AnalogFX_Placeholder.shader` | **Yes - replace this.** |
| `AnalogFX.mat` | Yes - repoint it at your shader. |
| `AnalogFX_*.asset` (4 presets) | Yes - retune freely, it is pure data. |
| `AnalogFXVolume.cs` | No. This is the gameplay contract. |
| `AnalogFXController.cs` | No. |
| Renderer features on `PC_Renderer` / `Mobile_Renderer` | No. |

### The contract: five global uniforms

Your shader must declare these names, and must **not** declare them in the `Properties` block.
They are set from C# with `Shader.SetGlobalFloat`, and a material property of the same name would shadow the global and silently break everything.

```hlsl
float _AnalogFX_Enabled;    // 0 = pass through untouched, 1 = apply. The kill switch. Honour it.
float _AnalogFX_Scanline;   // 0..1 horizontal CRT banding
float _AnalogFX_Grain;      // 0..1 noise
float _AnalogFX_Chromatic;  // 0..1 RGB channel separation
float _AnalogFX_Glitch;     // 0..1 blocky row tearing
```

Rules each parameter must obey, because gameplay and the presets assume them:

1. **0 is a no-op.** At 0 the parameter must make no visible difference.
2. **1 must not blow out.** Saturate your output; 1 means "heavy", not "unusable".
3. **`_AnalogFX_Enabled < 0.5` returns the source pixel unmodified**, with no arithmetic applied. This is the jam kill switch and it must stay exact.
4. **Respect the comfort budget below.** This one is not negotiable: it is a player-health constraint, not a taste preference.

### Comfort budget (read this before animating anything)

The first version of this shader re-randomised grain **every frame** and re-rolled glitch at 12 Hz.
It caused eye strain within seconds, and full-screen high-frequency flicker is a genuine photosensitivity hazard in a public build.

Anything animated must be **quantised to a low update rate**, not driven per frame:

```hlsl
#define ANALOGFX_GRAIN_HZ    10.0   // grain re-rolls 10x/sec, not 60
#define ANALOGFX_GLITCH_HZ    5.0   // tearing re-rolls 5x/sec
#define ANALOGFX_SCANLINES  200.0   // fixed band count, NOT tied to pixel height
```

Three specific traps, all of which the first version hit:

- **Per-frame noise.** Quantise the time input: `floor(_Time.y * RATE)`.
- **Scanlines derived from `_ScreenParams.y`.** That produces a 1-pixel comb which aliases and shimmers under any camera motion, and changes character with resolution. Use a fixed band count.
- **Bright flashes.** Brightness lifts on glitch are the worst offender for strain. Keep them small (currently 0.08).

Measured on the current shader, 60 frames of a static scene through the analog renderer: **12 of 60 frames changed at all** (the rest are pixel-identical), mean per-channel delta **2.35/255**, worst frame **11.85/255**.
Effect off, same scene: 0 of 60 frames changed.
If you replace the shader, re-run that check rather than trusting your eyes on a fresh look.

Sample the source with `_BlitTexture` (see the placeholder for the exact include and macro).

**WebGL matters here.** The submission target is WebGL2/GLES3: no compute, no unsized loops, keep instruction counts modest, avoid heavy dependent texture reads. Test in a browser, not only the Editor.

## Architecture

```
Volume Profile preset (.asset, data)
        v
AnalogFXVolume         gameplay contract: four 0..1 ClampedFloatParameters
        v              (URP blends profiles for free)
AnalogFXController     reads the blended stack -> Shader.SetGlobalFloat
        v
AnalogFX.mat -> AnalogFX_Placeholder.shader
        v
Full Screen Pass Renderer Feature ("AnalogFX") on PC_Renderer AND Mobile_Renderer
```

Values travel as **global uniforms, not material properties**.
Two reasons: nothing ever writes to the shared material asset (a bug seen in an audited project, where runtime edits to a shared VolumeProfile persisted into the asset, cannot happen here), and swapping the material or shader needs no code change.
The cost is that the material inspector's sliders do nothing - values come from the Volume stack.

## Where the effect appears (scoping)

The look is scoped on purpose, so it means "you are looking at a recording" rather than being a filter over everything.

| View | Renderer | Look |
|---|---|---|
| Watcher's own eyes (the room) | index **1**, `*_Renderer_Clean` | clean |
| Monitor wall / camera feeds | index **0** | analog |
| Possessed past self (first person) | index **0** | analog |

A URP full-screen pass applies per **renderer**, not per camera, so scoping is done by camera renderer index:

- Index **0** (`PC_Renderer` / `Mobile_Renderer`) carries the AnalogFX feature. This is the default, so any new camera is analog unless told otherwise.
- Index **1** (`PC_Renderer_Clean` / `Mobile_Renderer_Clean`) is the same renderer without the feature.
- Only `WatcherCamera` is set to index 1.

The monitor wall needs no special handling: the feed cameras render through the analog renderer into their RenderTextures, so the wall shows analog footage even while the watcher's own view is clean.

**The two RP assets must list renderers in the same order**, because a camera's renderer index is resolved against whichever RP asset is active (PC on desktop, Mobile on WebGL). If you add a renderer, add it to both in the same slot.

### Registered on BOTH renderers, deliberately

WebGL defaults to the **Mobile** quality level (`Mobile_RPAsset` -> `Mobile_Renderer`); Standalone defaults to **PC**.
A feature added only to `PC_Renderer` works perfectly in the Editor and is **invisible in the browser**.
If you add or duplicate the feature, do it on both.

## Presets

| Preset | scanline | grain | chromatic | glitch |
|---|---|---|---|---|
| `AnalogFX_Off` | 0 | 0 | 0 | 0 |
| `AnalogFX_Clean` | 0.20 | 0.10 | 0.06 | 0 |
| `AnalogFX_Degrade` | 0.35 | 0.20 | 0.18 | 0.04 |
| `AnalogFX_Broken` | 0.60 | 0.45 | 0.45 | 0.35 |

`Degrade` is the everyday preset a player stares at for a whole session, so it is tuned for comfort over impact.
`Broken` is meant for short bursts, not sustained viewing.

`ProofRoom.unity` ships with `AnalogFX_Degrade`.
Swap by dragging a different profile onto the `AnalogFX` GameObject's Volume component; the preview updates live, no Play mode needed.

### Editing a preset

The four parameters live on an `AnalogFXVolume` override **inside** each profile asset.
If you create a new profile from scratch, add the override via `Add Override > JamKit > Analog FX`, and make sure each parameter's checkbox is ticked - an unticked parameter is not overridden and contributes nothing.

## Usage from gameplay

Only `glitch` is animated at runtime, because it is the only parameter gameplay modulates.
The other three are authored in presets; there is no driver for them on purpose.

```csharp
_analogFX.SetGlitch01(0.8f);   // adds on top of the preset's glitch, clamped to 1, smoothed
_analogFX.SetGlitch01(0f);     // eases back to the preset baseline
_analogFX.EffectEnabled = false;  // kill switch: pass-through, presets untouched
```

## Kill switch

`AnalogFXController.EffectEnabled = false` makes the pass a pass-through.
It is instant and safe, but the blit still costs a little GPU time.

For a **hard** disable with zero cost (the one to reach for if WebGL framerate is the problem), untick the `AnalogFX` feature on **both** `PC_Renderer` and `Mobile_Renderer`.

## Live preview in Edit mode

`AnalogFXController` is `[ExecuteAlways]` and pushes from `EditorApplication.update`, so the effect renders in the Scene and Game views without entering Play mode, and preset edits show up immediately.
Two things make that work, and both are load-bearing:

- Outside Play mode nothing drives URP's Volume stack, so the controller calls `VolumeManager.Update` itself before reading it. Without that the stack reads all zeros and the preview is blank.
- `Update()` on an `[ExecuteAlways]` component only ticks when the Editor repaints, so the push is driven from `EditorApplication.update` instead. Otherwise the preview goes stale whenever the Editor is idle.

## Verified 2026-07-22

Against the live Editor via MCP, in `ProofRoom.unity`, **re-verified after a domain reload** (see the note below on why that matters):

- Shader compiles with no messages; `isSupported = True`.
- Preset -> Volume stack -> globals confirmed end-to-end (`Degrade` produced `0.45 / 0.30 / 0.25 / 0.10` on the stack and the identical values on the shader globals).
- Renders visibly at `Degrade` and `Broken` (grain, row tearing, and RGB fringing all confirmed by screenshot, not property reads).
- Kill switch verified: `_AnalogFX_Enabled = 0` produces a clean image while the Volume stack still reports `glitch = 0.55`, proving the presets are untouched.
- Runtime glitch verified: adds on top of the preset baseline, smooths rather than snapping, clamps at 1, and eases back to the baseline on release.
- Feature registered and material assigned on both `PC_Renderer` and `Mobile_Renderer`.

- Renders in **Edit mode** with no Play mode, confirmed by screenshot; live preset swapping updates the preview.

**Not verified: how it looks or performs in a browser.** That rides with the WebGL smoke test.

### Gotcha that cost a rebuild: persisting a VolumeComponent

`VolumeProfile.Add<T>()` creates the override **in memory only**.
Writing it into the asset also needs `AssetDatabase.AddObjectToAsset(component, profile)`; without it, `SetDirty` plus `SaveAssets` appears to succeed, the values read back correctly, and the whole thing evaporates at the next domain reload, leaving `profile.components.Count == 0`.
The first version of these presets had exactly that bug, and the first round of verification passed against in-memory state that was never on disk.
Verify profile assets **after** a recompile, or by grepping the `.asset` file for the override, not just by reading values back in the same session.

Known side effect, currently free and probably desirable: the pass also applies to the SurveillanceKit feed cameras, so the monitor wall gets the analog look too.
It does mean the effect runs once per feed render plus once for the main camera, which is worth remembering if WebGL framerate becomes a problem.
