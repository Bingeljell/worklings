# Pet Interaction Model

How you read and care for a Workling. Information reveals progressively — the pet itself, then hover, then the care card, then the menu bar — so Pixel feels like a companion, not a monitoring dashboard.

Everything below describes implemented behavior unless marked deferred. Values are **Fullness-first**: higher always means better, matching the [Pet Brain](pet_brain.md).

## 1. The pet is the primary UI

- Urgent hunger, exhaustion, sadness, or low trust must be visible on the pet itself.
- Care reactions briefly override the underlying expression.
- Happy and content states stay quiet — no thought-bubble spam.
- No condition may rely on colour alone.

All three families share one pose contract, so every mood and reaction reads the same whichever Workling is active. An eight-frame smoke effect covers launch, wake, tuck-away, and family swaps.

## 2. Hover summary

Hover for ~500ms and a small read-only summary appears: natural language ("Pixel is hungry and getting tired"), at most two conditions, no numbers. It dismisses when the pointer leaves, never steals focus or blocks dragging, and stays inside the display. The same summary is exposed to accessibility tools without hovering.

## 3. Care card

Click opens it; drag moves the pet and never opens it. One card at a time; Escape or an outside click closes it; opening it never moves the pet; care actions update it live and leave it open.

```text
┌────────────────────────────┐
│ Pixel              Hungry  │
│                            │
│ Fullness   ██░░░░░░░░  18  │
│ Energy     ████░░░░░░  38  │
│ Happiness  ██████░░░░  64  │
│ Trust      ███████░░░  71  │
│                            │
│ [ Feed ] [ Play ] [ Pet ]  │
│          [ Sleep ]         │
│                            │
│ ♥ Berries · ♥ Puzzle       │
└────────────────────────────┘
```

Every meter is a wellbeing measure: longer bar, better pet. Natural language may still call the Workling "hungry" — that's flavour, not a different scale.

## 4. Menu bar

The reliable fallback: wake, tuck away, persistence warnings, quit, the checked Choose Workling selector, the persistent roaming toggle, and (for now) duplicate care actions. Both surfaces call the same `PetSession` actions and show the same state. Family swaps happen under the dense smoke frame and preserve name, needs, favourites, and progression time.

## Idle roaming

An explicit opt-in. Pixel occasionally wanders within the current display — needs and relationship state never change from movement.

- Stays fully inside the visible frame, reflects inward off edges, never crosses displays.
- Pauses instantly for hover, the care card, dragging, tuck-away, or Reduce Motion.
- Walks with the walking frames, faces its direction of travel, and never flips text.
- A drag that ends off-frame is clamped back in; roaming then resumes.

Mood, personality, and activity context will drive movement later; today's pattern is deterministic and modest.

## Click versus drag

Movement, not timing, tells them apart: within click tolerance → card on release; beyond it → drag, click suppressed. AppKit owns the tracking; SwiftUI only presents content.

## Urgency

| Need | Notice | Urgent | Critical |
| --- | ---: | ---: | ---: |
| Fullness | `<= 45` | `<= 25` | `<= 10` |
| Energy | `<= 45` | `<= 20` | `<= 10` |
| Happiness | `<= 45` | `<= 30` | `<= 15` |
| Trust | `<= 35` | `<= 20` | `<= 10` |

Priority when conditions compete: critical physical needs, then other criticals, then urgent physical needs, then trust and happiness; notice-level conditions only appear unopposed. Hover shows at most two. Ambient bubbles show one urgent-or-worse condition with a cooldown of at least fifteen minutes unless things get worse.

## Action availability

Derived from the Pet Brain, never duplicated in the UI: Feed disabled at Fullness 100, Play below 15 Energy, Sleep at 100 Energy, Pet always on. Disabled actions explain themselves through accessible help text. Favourites are marked identically on the card and in the menu.

## Feedback

Reactions run ~3 seconds as text plus an expression change, then need state resumes. The card always shows current exact values, even mid-reaction.

## Accessibility

Name, mood, and the hover summary live in the pet's accessibility label. Every card action is keyboard-reachable, meters carry names and values, colour is never the only signal, Reduce Motion is respected everywhere, and larger text must not hide actions.

## Boundaries

`CompanionCore` owns every testable decision: urgency, summaries, availability, roaming plans, smoke midpoints, screen targets. The app target owns timing, tracking, placement, focus, and animation. `PetSession` is the single source of live state. No provider-specific logic enters this layer.

## Verification

`swift run CompanionCoreChecks` covers the domain rules above. Manual macOS review covers hover, click-versus-drag, card focus and dismissal, menu/card consistency, VoiceOver, Reduce Motion, family swaps under smoke, and roaming interruptions.

## Deferred

- Richer action animation states.
- Mood-driven movement, obstacles, multi-display travel.
- Activity adapters and reactions from the [progression design](progression.md).
- Adoption, naming, personality selection.
- A full settings and save-recovery interface.
