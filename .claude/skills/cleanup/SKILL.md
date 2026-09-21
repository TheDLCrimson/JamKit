---
name: cleanup
description: Sweep the jam game code for debug artifacts and dead weight, report grouped findings, and remove only what the human approves. Use this whenever the user types /cleanup, or asks to "clean up the project", "remove debug logs / dead code", "do a polish sweep", "hunt for leftover TODOs", or "prep for submission" - especially the jam's hour-88 cleanup pass. It scans Assets/_Project for ungated Debug.Log calls, dead or unused serialized fields, unused assets, and stray TODO/HACK comments, then presents grouped findings for review and applies removals only on approval. It never touches the frozen JamKit package (Packages/com.crimson.jamkit) and never edits .unity scenes autonomously. Use it in the polish window to catch the debug artifacts every audited project shipped and never swept.
---

# /cleanup

The polish-window sweep every audited project needed and never got: find debug artifacts and dead weight, report them, remove on approval.
The target is a submission with a debug-artifact count of zero - no OnGUI/log spam, no dead systems left "in place".
This skill only sweeps `Assets/_Project` (and other jam-owned game assets). It never modifies `Packages/com.crimson.jamkit` (frozen after `v1.0`) and never edits scenes on its own.

## Step 0 - Scope the sweep

Default scope: `Assets/_Project/` (game code and assets). Confirm or narrow with the user if they name a subset.
Explicitly out of scope: `Packages/com.crimson.jamkit` (frozen), `Assets/Plugins`, `Assets/Settings`, `ProjectSettings/`. Do not report or touch those.

## Step 1 - Scan for each artifact class

Search (read-only) and collect findings under these headings:

- **Ungated `Debug.Log`** - any `Debug.Log`/`LogWarning`/`LogError` in `Assets/_Project` not inside a `#if UNITY_EDITOR || DEVELOPMENT_BUILD` (or `#if UNITY_EDITOR`) block. These are the CarGoesAround failure mode. (The DebugOverlay is the sanctioned channel; its calls are already guarded.)
- **Dead / unused serialized fields** - `[SerializeField]` fields never read in the class, and obvious dead private fields. Flag rather than assume; some are read via inspector-wired UnityEvents.
- **Unused assets** - assets in `Assets/_Project` with no references (scripts not on any prefab/scene, orphan sprites/prefabs). Treat as candidates to confirm, not certainties - reference-checking is imperfect: an asset loaded by string key or `Resources.Load` (e.g. from a folder named `Resources`) has no serialized reference yet is still live, so a "no references" result is a prompt to ask the human, never proof the asset is dead.
- **Stray `TODO` / `HACK` / `FIXME` comments** - leftover markers in game code.
- **Debug leftovers** - any `OnGUI` in shipping code paths, commented-out code blocks, obvious scratch/test scripts.

Also worth surfacing if seen in passing: `Debug.Log` guarded correctly (fine, note count only), and anything that looks like an abandoned system (the Tongue-Tied Obi-rope pattern).

## Step 2 - Report grouped findings

Present findings grouped by class, each with `file:line` and a one-line note.
Give a total count per class and a headline debug-artifact number.
Separate the confident removals (ungated `Debug.Log`, stray TODOs) from the judgement calls (possibly-unused fields/assets that need human confirmation).
Do not delete anything yet.

## Step 3 - Apply only approved removals

After the human approves specific items:

- Remove ungated `Debug.Log` lines, approved dead fields, approved TODO comments, and approved unused assets.
- For anything that requires a scene edit, or deleting an asset a human should eyeball first, describe it and let the human do it - do not edit `.unity` files or delete ambiguous assets autonomously.
- Never touch `Packages/com.crimson.jamkit` or `ProjectSettings/`.
- If a live Editor is connected, `refresh_unity` + `read_console` after code removals to confirm nothing broke.

## Step 4 - Report the resulting artifact count

State the debug-artifact count after the approved sweep, and list anything deliberately left (with the reason) so the number is honest.
The goal state is zero shipped debug artifacts; if any remain, say why.
