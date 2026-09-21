# Modules~

Genre modules live here.
The trailing `~` makes Unity ignore this folder entirely - it is never imported, compiled, or scanned.
This keeps unused modules at zero cost: no import time, no compile time, no accidental references.

Each module gets its own subfolder with its own asmdef (referencing `JamKit.Runtime` at most, never another module), a demo scene, and **one canonical `README.md`** - the single source of truth for that module, installation section included.
To use a module, copy its subfolder out of `Modules~/` into the project (see the module README's installation section).

| Module | Status | Docs |
|---|---|---|
| KCCController | Shipped (M7, 2026-07-17) - first-person kinematic character controller | `KCCController/README.md` |
| InteractionKit | Shipped (M9, 2026-09-20) - signal-graph wiring (switches, doors, gates, sockets) plus the first-person interaction layer | `InteractionKit/README.md` |

## Removed modules

`Interactables` was superseded by `InteractionKit` and removed; the InteractionKit README carries the v1 -> v2 mapping table for porting an old scene.
