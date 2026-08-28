# Reusable Quadruped Animation Pipeline

**Status: proposal.** This document defines the intended authoring and reuse model for
the upcoming quadruped creature pipeline. It does not claim that the current Blender
actions have already been converted into portable templates.

Companion doc: [3D → Sprite Bake Spec](bake-spec.md) for the current render, framing,
and fixed-rig contracts. This proposal generalizes the same script-driven principle to
Rigify Basic Quadruped rigs with different proportions and movement styles.

## Objective

Author a shared family of animation asks once—walk, idle, wince, attack, special
attack, and eventually defeat—then generate a character-specific Blender Action for
each compatible quadruped.

The system must be deterministic for the same inputs, but it must not make every
creature move identically. Anatomy, weight, gait, attack anatomy, and personality remain
intentional inputs.

## Core principle

Share **animation structure**, not raw world-space bone transforms.

A copied transform assumes the target has the same limb lengths, stance, ground height,
rest pose, and control orientation. That is safe for a duplicate rig and fragile for a
different creature. A reusable template instead records semantic phases, local
rotations, normalized translations, contacts, and tunable style parameters.

## Four-layer model

### 1. Motion structure

The structure defines the meaning and timing of an animation independently of species.
Frames are expressed as normalized time from `0.0` to `1.0`; the generated Action gets
the requested duration and frame rate.

Examples:

| Animation | Structural beats |
| --- | --- |
| Walk | contact → loading → passing → reach → opposite contact |
| Idle | neutral → inhale → exhale, with optional secondary gesture |
| Wince | impact → compression → recoil → recovery |
| Swipe | anticipation → paw lift → strike → follow-through → settle |
| Slam | crouch → load → apex → impact → compression → recovery |
| Defeat | destabilize → lose support → collapse → final rest |

The template also owns contact declarations and gameplay events such as `impact`,
`paw_down`, or `recovery_complete`.

### 2. Creature-family profile

The family profile supplies a movement vocabulary:

- feline;
- canine;
- ungulate;
- heavy or bear-like;
- reptilian or another exceptional quadruped family.

Profiles tune stride, spinal flex, weight transfer, paw or hoof articulation, head
stabilization, tail response, overlap, and recovery. A feline and an ungulate may share
the walk structure, but they should not share one literal gait.

### 3. Physical action archetype

`Basic attack` and `special attack` are gameplay slots, not animation definitions. The
reusable library names what the body actually does:

- right- or left-paw swipe;
- bite;
- headbutt;
- pounce;
- two-paw slam;
- horn charge;
- tail strike.

A character profile assigns an archetype to each gameplay slot. This avoids forcing a
horned creature, feline, and heavy quadruped through one generic attack clip.

### 4. Character adapter and overrides

Every character has a small profile containing:

- Rigify and Blender compatibility version;
- semantic control-to-bone mapping;
- forward and up axes;
- body length, shoulder and hip height, limb lengths, and stance width;
- rest-pose and ground-contact measurements;
- required IK/FK settings and custom properties;
- optional chains such as tail, ears, jaw, or horns;
- family, weight, intensity, and personality parameters;
- character-only pose corrections.

The generator produces a new Action. Artist polish is stored as a character override or
in the generated Action, never by mutating the shared template.

## Portability levels

| Target | Reuse strategy | Expected result |
| --- | --- | --- |
| Duplicate of the generated rig | Reuse the native Action | One-to-one |
| Same Rigify structure and similar family | Generate from template and family profile | Close starting point |
| Same Rigify structure, different anatomy | Generate with proportional adaptation | Requires a polish pass |
| Changed controls, chains, or Rigify generation | Add or update an adapter first | Reject until mapped |

Deterministic means that the same template, profile, rig signature, parameters, and
software version produce the same Action. It does not mean that one result is
automatically appealing on every anatomy.

## Rig contract and validation

The importer must fail clearly before writing animation when the target is incompatible.
Validation should cover:

1. generated Rigify rig and expected quadruped controls exist;
2. required control names or semantic mappings resolve uniquely;
3. rest-pose axes and scale are known;
4. IK/FK and stretch properties are supported and initialized;
5. required limb and spine chains have compatible structure;
6. optional chains degrade intentionally rather than silently;
7. Blender, Rigify, template, and adapter versions are recorded.

Rigify-generated names are an implementation detail behind the semantic mapping. The
template should ask for `front_paw_ik.right`, not hard-code a particular generated bone
name throughout every animation recipe.

## Template data

Each template should contain:

- identity, version, family compatibility, duration, and frame rate;
- normalized phase markers;
- local rotations relative to the target rest pose;
- translations normalized by body or limb measurements;
- interpolation and hold rules;
- ground and support contacts;
- IK/FK and stretch state;
- gameplay events;
- exposed parameters such as intensity, stride, lift, recoil, and tail response;
- a list of optional and required controls.

The representation may be JSON plus a Blender Python applicator, or a Python recipe
with a stable data schema. The data contract matters more than the initial file format.

## Generation workflow

1. Validate and fingerprint the target rig.
2. Measure the character in its rest pose.
3. Load the motion structure, family profile, action archetype, and character profile.
4. Convert normalized rotations and translations into target-control space.
5. Enforce declared ground contacts and support feet.
6. Create a new, named Blender Action without touching existing Actions.
7. Run mechanical checks for foot drift, penetration, joint flips, missing controls,
   start/end closure, and contact timing.
8. Apply or author character-specific polish.
9. Bake a standalone final Action for the game-export path.

Native Blender Action libraries remain the preferred fast path for truly identical
rigs. The generator exists for different proportions, families, and controlled variants.

## Data that is not animation

The reuse system must not silently bundle model-specific repair work. These remain
separate concerns:

- vertex weights and skinning defects;
- corrective shape keys;
- mesh intersections caused by unique anatomy;
- horn, armor, accessory, or fur collisions;
- camera and render framing;
- physics or engine-side hit reactions.

For example, repairing an unweighted toe is a character-rig fix. The corresponding paw
lift and contact timing belong to the animation template.

## Naming and versioning

Templates describe structure or physical action:

```text
Q4_Idle_Breathe_v1
Q4_Wince_Backward_v1
Q4_Walk_Feline_v1
Q4_Attack_PawSwipe_R_v1
Q4_Attack_DoublePawSlam_v1
```

Generated Actions include the character name and remain ordinary editable Actions:

```text
ForestFlicker_Idle_Breathe_v1
ForestFlicker_Wince_Backward_v1
ForestFlicker_Walk_Feline_v1
```

A final game-facing alias can map an archetype to `idle`, `walk`, `basic_attack`,
`special_attack`, `wince`, or `defeat` without weakening the authoring taxonomy.

## Initial proof of concept

Start with three templates:

1. **Idle/breathing** — validates broad pose transfer and optional secondary motion.
2. **Backward wince** — validates impact timing, spinal compression, and recovery.
3. **Feline walk** — validates family styling, four-foot contacts, proportional stride,
   and loop closure.

Apply them to a second, differently proportioned Rigify Basic Quadruped. The proof is
successful when:

- generation is repeatable from a clean file;
- the source Actions remain unchanged;
- feet contact the calculated ground without visible drift;
- start and end poses close where required;
- missing optional controls produce explicit warnings;
- the generated Action can be polished and exported normally;
- applying it again produces the same unpolished result.

Only after that proof should the library expand into attack archetypes and defeat
variants. Death and defeat should have multiple structures—side collapse, forward
collapse, exhausted kneel—because anatomy and final silhouette dominate those motions.

## Open decisions

- in-place versus root-motion delivery for the game;
- authoritative frame rate and engine event format;
- whether generated Actions retain procedural metadata after baking;
- how character overrides are stored and reviewed;
- which Blender and Rigify versions define the first supported contract;
- whether the reusable tool ships as scripts, a Blender add-on, or both.
