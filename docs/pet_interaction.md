# Pet Interaction Model

## Purpose

This document defines how a user understands and cares for a Workling. It covers implemented behavioral surfaces and accessibility while keeping future autonomous movement and final art direction separate.

Pixel is the current fixed-name test Workling and still uses code-drawn placeholder visuals. The Wildkin, Elemental, and Relicborn repository assets are concept art and are not yet runtime states.

The Workling must communicate important needs without requiring the user to inspect the menu bar. Interaction should remain lightweight enough that Pixel feels like a companion instead of a monitoring dashboard.

## Implementation status

Ambient placeholder states, delayed hover, click-versus-drag handling, the pet-anchored care card, shared menu actions, positive wellbeing meters, favourite markers, reaction feedback, and basic accessibility labels are implemented. Autonomous movement, final sprite states, adoption, family selection, and a complete settings experience remain deferred.

## Interaction hierarchy

Information is revealed progressively through four surfaces.

### 1. Ambient pet state

The pet itself is the primary signal. The current placeholder uses face, colour, and occasional thought bubbles; future sprites should use pose and animation to communicate the same semantic states.

- Urgent hunger, exhaustion, sadness, or low trust must have a visible state.
- Reactions to care actions may temporarily override the underlying expression.
- Persistent happy or content thought bubbles should not create visual noise.
- Important conditions must not rely on colour alone.

### 2. Hover summary

Hovering over the pet for approximately 500 milliseconds reveals a small non-interactive summary.

- Use natural language, such as “Pixel is hungry and getting tired.”
- Show at most two relevant conditions.
- Do not show exact numeric values in the hover summary.
- Dismiss when the pointer leaves without taking focus.
- Do not intercept dragging or clicks.
- Keep the summary inside the active display's visible frame.

The same summary must be available to accessibility technologies without requiring hover.

### 3. Click care card

A click opens an interactive care card anchored near the pet. A drag moves the pet and must not open the card.

- Only one care card may be open.
- Clicking outside the card or pressing Escape closes it.
- The card may become key so its controls support keyboard and accessibility interaction.
- Opening or closing the card must not move the pet.
- Performing a care action updates the card immediately and leaves it open for feedback.

Current card structure:

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

All exact-value meters are positive wellbeing measures: a higher value and longer bar always mean the Workling is doing better. The interface displays **Fullness** as the inverse of the Pet Brain's internal hunger value. Natural-language conditions may still describe the Workling as hungry.

The current implementation uses SwiftUI shapes and system materials. Approved concept art establishes character direction, but runtime sprite extraction, state variants, animation, and asset licensing metadata remain separate work.

### 4. Menu bar

The menu bar remains a reliable fallback and application-control surface.

- Keep wake, tuck away, persistence warnings, and quit controls.
- Care actions may remain duplicated during the experiment.
- Both surfaces must call the same session actions and display the same state.
- If the care card proves successful, detailed care can later move out of the menu bar.

## Pointer behavior

The pet window distinguishes a click from a drag using movement rather than timing alone.

- Pointer movement within the normal click tolerance opens the card on release.
- Movement beyond the tolerance begins dragging and suppresses the click action.
- A click no longer performs the pet action directly; Pet is an explicit card/menu action.
- Hover content disappears while dragging or while the care card is open.

Native AppKit tracking and window-drag behavior should own these distinctions. SwiftUI presents content but should not independently compete for pointer gestures.

## Urgency model

Every internal need maps to an urgency level used by ambient presentation and hover summaries. Hunger thresholds below use the Pet Brain's internal value; the exact-value interface shows the inverse as Fullness.

| Need | Notice | Urgent | Critical |
| --- | ---: | ---: | ---: |
| Hunger | `>= 55` | `>= 75` | `>= 90` |
| Energy | `<= 45` | `<= 20` | `<= 10` |
| Happiness | `<= 45` | `<= 30` | `<= 15` |
| Trust | `<= 35` | `<= 20` | `<= 10` |

When several conditions compete:

1. Critical physical needs take priority.
2. Other critical needs follow.
3. Urgent physical needs follow.
4. Trust and happiness follow.
5. Notice-level conditions appear only when no urgent condition displaces them.

The hover summary reports at most two conditions in that order. Ambient thought bubbles report only one urgent or critical condition and use a cooldown to avoid nagging.

## Care action availability

The interface derives availability from the Pet Brain rather than duplicating rules in SwiftUI or AppKit.

- Feed is disabled when hunger is already zero.
- Play is disabled below 15 energy.
- Sleep is disabled at 100 energy.
- Pet remains available.
- Disabled actions explain why through accessible help text.

Favourite food and play choices are marked consistently in both care surfaces. Preference bonuses remain domain rules, not interface rules.

## Feedback policy

- Care reactions override need content for approximately three seconds.
- Reaction feedback uses text plus an expression change.
- After the reaction expires, the current need state resumes.
- Happy/content ambient bubbles are suppressed by default.
- Urgent need bubbles should use a cooldown of at least fifteen minutes unless urgency increases.
- The care card always reflects the latest exact values, even while a reaction is showing.

## Accessibility

- Expose pet name, mood, and hover summary in the pet's accessibility label or description.
- Make every care action keyboard reachable when the card is open.
- Label progress indicators with names and numeric values.
- Never use colour as the only indication of state or disabled behavior.
- Continue respecting Reduce Motion.
- Ensure card and hover placement work with larger text without hiding actions.

## Implementation boundaries

- `CompanionCore` owns urgency, summaries, action availability, and other testable presentation decisions.
- The application target owns hover timing, AppKit tracking, card placement, focus, and dismissal.
- `PetSession` remains the single source of live state and actions.
- Menu-bar and pet-anchored controls reuse the same domain models.
- No Codex-specific logic or movement behavior enters this slice.

## Verification

Automated checks should cover:

- decoded need values remain within `0...100`;
- urgency thresholds and priority ordering;
- two-condition summary limits and wording;
- care action availability and explanations;
- reaction precedence over ambient need content;
- existing simulation, persistence, presentation, and placement behavior.

Manual macOS review should cover:

- hover delay, dismissal, and display-edge placement;
- click versus drag discrimination;
- card focus, outside-click dismissal, and Escape;
- live updates after every care action;
- menu/card consistency;
- VoiceOver labels, keyboard navigation, and Reduce Motion.

## Deferred work

- Integrating approved character concepts into licensed runtime sprites and animation states.
- Autonomous movement.
- Codex and other activity adapters.
- Adoption, naming, and personality-selection flows.
- A complete settings and save-recovery interface.
