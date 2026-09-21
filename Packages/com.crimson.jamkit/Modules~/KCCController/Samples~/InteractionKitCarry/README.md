# PlayerCarry (KCC + InteractionKit integration)

The shipping carry for a KCC player: Portal-style velocity-follow pick up, hold and drop, implementing InteractionKit's `ICarrier` so a `Socket` can take a prop out of the player's hands.

It needs both modules, so it cannot live inside either one: modules never reference each other.
That is why it sits in `Samples~`, which Unity ignores even after the module is copied in.

## Install

1. Install both `KCCController` (with KCC Core imported) and `InteractionKit`.
2. Copy `PlayerCarry.cs` into `Assets/_Project/Scripts/`.
3. Add it to the KCC player root, next to the InteractionKit `Interactor`.

In `Assets/_Project` it compiles as game code, which may reference any module.

It carries five fixes found by hand-testing: the carry elevator (it removes the held prop from the KCC motor's ground probes through `PlayerCharacter.SetColliderIgnored`), the fast-turn drop, grab assist for small props, lowering the held prop for low sockets, and restoring the rigidbody exactly as found on drop.
