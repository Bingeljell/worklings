# Dungeon polish backlog

Things Nikhil raised while play-testing the Godot dungeon that were deliberately
deferred rather than dropped. Each entry says what was asked for, why it is
worth doing, and where the seam is — so picking one up does not start with
re-deriving the problem.

Ordered roughly by how much the fight gains per hour spent.

## Whose turn is it

**Asked for 2026-09-14.** Some indicator that it is the player's turn — "an
arrow bobbing above their head pointing at them, or a slow circular glow below
them; solid border, subtle glow under them".

Right now the only cue is the command bar going from dim to lit, which is at the
bottom of the frame while the player is looking at their Workling in the middle
of it. The foe already declares its intent over its head (`IntentBadge`), so the
Workling having nothing is also an asymmetry the eye notices.

**Seam:** the phase is already explicit — `Phase.Choosing` is exactly "it is your
turn" — and `IntentBadge` is a worked example of a screen-space overlay tracking
a creature in 3D. A ground ring wants to be world-space instead, and
`VfxPrimitives` already builds ground marks for the signature layer.

## Per-attack and per-signature sound effects

**Asked for twice, 2026-09-13 and 2026-09-14.** "Individual sound effects for
the spells — lightning, fireball, whiplash, etc., to make it more fun."

Every blow currently plays from one shared set in `CombatAudio` (`Hit`, `Crit`,
`Unleash`, `Slam`, …), so a Ram's headbutt, a Snag's whip and a Flicker's swipe
are the same noise and all four signatures share one sound. The *visual*
signature layer already gives each creature an identity; the audio is the one
system that says nothing about it.

**Seam:** `AbilitySignature.For(modelName)` already picks the visual. The sound
should key off the same value rather than a generic `CombatSound.Unleash`, so
adding a creature stays one roster entry. Per-creature basic attacks want the
same treatment through the roster, not a switch at the call site.

## Camera framing and combatant spacing

**Asked for 2026-09-14.** "The camera needs to be pulled out a little and the
Workling and foe need to be moved apart more."

The framing was locked for a 1v1 study against a Scamp. It does not hold for the
Monolith: at 7.50 units the mini-boss nearly touches the Workling, and there is a
lot of dead floor bottom-right in every frame. Party formations are unproven for
the same reason.

**Seam:** `StageSet` owns the camera and the marks, and the capture tool builds
the same arena, so a change is reviewable without launching the game. Note that
the current angle is a deliberate locked decision — changing it is a real call,
not a tweak.

## The loadout screen

**Asked for 2026-09-14.** "The UI for the loadout needs a complete overhaul.
It's all text and unintuitive. I'd like it similar to an MMO or RPG or something
like PoE where I can see what all my character has as icons around my character
based on their slots available."

`LoadoutPanel` is a text grid — a row per slot, cycled with the arrow keys. The
target is the standard paper-doll: the Workling in the middle, slot frames
arranged around it, items as icons, hover for detail.

**Seam:** the panel already owns the whole interaction and hands back a
`PetState`, and equipping already validates ownership through `PetState`, so the
rewrite is presentation only. It needs item icons, which do not exist yet — the
placeholder rule applies: build the real layout with placeholder art rather than
letting missing icons justify another text wall.

## Pacing between beats

Not reported, observed. A kill still costs a few seconds of dead air before the
bank-or-push choice appears. `BeatSeconds`, `ActionSeconds`, `ReadSeconds` and
`CardSeconds` are all exported on `CacheWarrenScene` and `WORKLINGS_FAST=1`
collapses them, so this is a tuning pass rather than a build.
