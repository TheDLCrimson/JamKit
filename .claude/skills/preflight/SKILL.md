---
name: preflight
description: Run the JamKit preflight build-readiness gate for this Unity project against the live Editor via MCP - validate the project (JamKit > Validate Project), then build a desktop player, launch the produced player, and report one pass/fail verdict with the console/log tail. Use this whenever the user types /preflight, or wants to confirm the project is build-ready - that it validates clean, the desktop build compiles successfully, and the produced player can be launched - for example before a commit, before tagging a milestone, at the jam's Hour-16 build gate, after a risky or large change, or when they say things like "sanity-check the build", "make sure it still builds", or "is it shippable". Prefer this over running the validator or a build by itself whenever the intent is end-to-end build confidence, because it chains validate + build + launch and refuses to build a project that fails validation. Preflight verifies build-readiness (validation, a successful build, and that the player launches), not runtime health, gameplay progression, or full shipping certification.
---

# Preflight

Preflight is the build-readiness gate: it proves the project both validates clean and still produces a launchable desktop build.
It is the fastest way to catch the "it ran in the Editor but the build is broken" class of failure before it costs jam time.

Prerequisites: this is a Unity workflow driven through MCP for Unity, so it needs one live Unity Editor connected to the bridge.
Run every check against that single live Editor.
Headless batch mode is a fallback ONLY when no interactive Editor is connected (batch-mode runs can hang on domain reload).

Work the steps in order and stop at the first hard failure, because each step depends on the previous one holding: there is no point building a project that fails validation, and a pass must reflect what actually happened this run, never a stale result from a previous one.

## Step 0 - Confirm a live Editor is connected

Read the resource `mcpforunity://instances`.

- If exactly one instance is connected, use it (pin it with `set_active_instance` if the session is not already pinned).
- If none is connected, tell the user to open the Unity project in the Editor and stop. Do not spin up a batch-mode Editor unless the user explicitly asks for the headless fallback.
- If more than one is connected, ask the user which instance to preflight.

## Step 1 - Compile and reject a pre-existing broken state

- `refresh_unity` with `compile=request`, `mode=force`, `wait_for_ready=true`.
- `read_console` filtered to `error` types.
- If there are any compile errors, report them and STOP. Preflight cannot validate or build a project that does not compile.

## Step 2 - Run the validator (stale-result-resistant)

This step must reflect THIS run only. A `RESULT=PASS` line left in the console by an earlier run must never satisfy the current preflight.

1. `read_console` action `clear` to empty the console immediately before running the validator.
2. `execute_menu_item` with menu path `JamKit/Validate Project`.
3. `read_console` filtered with `filter_text` = `[JamKit Validate]` (get the fresh entries).
4. Require exactly one fresh line matching `[JamKit Validate] RESULT=`:
   - If zero fresh `RESULT=` lines appear, the validator did not run this pass. Report a preflight FAILURE (validator did not produce a fresh result) and STOP.
   - If a `RESULT=FAIL` line appears, report every `[JamKit Validate] ERROR:` line from the fresh console and STOP. Do not build.
   - If a single `RESULT=PASS` line appears, also surface any `[JamKit Validate] WARNING:` lines (advisory, not blocking) and continue.

## Step 3 - Build a desktop player

Only reached when the validator passed this run.

- `manage_build` with `action=build`, `target=windows64`, `development=true`, `output_path=Builds/preflight/JamKit_preflight.exe` (the `Builds/` folder is gitignored).
- Builds are asynchronous. Poll `manage_build` with `action=status` and the returned `job_id` until it finishes (use a sensible wait; a first build can take minutes). Treat only a fresh status for THIS `job_id` as authoritative - do not infer success from a prior build.
- Immediately after the build finishes, `read_console` filtered to `error` types for build errors.
- If the build fails or reports build errors, report the failure with the console/log tail and STOP.

## Step 4 - Launch the produced player

- Launch the produced player to confirm the build starts as a process - either pass the `auto_run` build option in Step 3, or start the produced executable (`Builds/preflight/JamKit_preflight.exe`) after the build.
- Give it a moment to open, then read only what tooling can actually see: whether the process is still alive after a few seconds, and the tail of the player log (`%USERPROFILE%/AppData/LocalLow/DefaultCompany/GMTK2026/Player.log`) alongside `read_console`.
- Be precise about what this proves. A launched process with an exception-free log tail shows the player *starts*; it does not prove runtime health, that gameplay works, or that Boot advances to Menu. Do not claim any of those unless you actually drive the player and observe it. When nothing exercises the running player, report "player launched" and "no startup exception observed in the log tail" as two separate, deliberately weak facts - never "the game runs".

## Step 5 - Report

Report a single, clear verdict, claiming only what was observed this run:

- **PASS** only if all of these held this run: the project compiled, the validator produced a fresh `RESULT=PASS`, the desktop build succeeded, and the produced player launched (the process started and no startup exception appeared in the log tail). State plainly that a preflight PASS certifies build-readiness only - a successful validation, a successful build, and a launchable player - and does not certify runtime health, gameplay progression, or shipping-readiness. Include the validator summary line, any warnings, the build output path, and the last few console/log lines.
- **FAIL** otherwise: name the first failing step (compile / validator / build / launch), and include the relevant error lines and log tail so the cause is actionable.

Audible audio, feel/juice, gameplay correctness, and final visual polish are not part of preflight - it is a build-readiness gate, not a quality or runtime pass. Flag anything that needs a human eyeball or an actual play-through separately.
