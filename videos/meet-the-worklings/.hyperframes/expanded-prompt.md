# Meet the Worklings — expanded production prompt

## Title and style

An 18-second, 1080×1920 portrait social film introducing the three current Worklings as distinct companions with distinct elemental identities. The world is a living character catalogue: each creature enters a vertical arena-like portrait field, reveals an everyday emotion, then flashes its signature power before the next family takes over.

Use the existing Broadside frame system as brand truth:

- Ink-black `#0B1224` and ink-black-alt `#06292C` are the dark register.
- Fire-orange `#FF6B1A` is the sole designed accent and may become the full frame on declarative beats.
- Cream `#F3E4C8` carries primary text on dark.
- Teko 700 is the lowercase display face, Inter 400/700/900 is reading copy, and IBM Plex Mono 500 is uppercase tracked chrome.
- Flat planes, sharp edges, 1px hairlines, no shadows, no rounded cards, and no gradient ground.
- Cyan rune light, ember orange, and Wildkin green may appear only inside supplied character artwork and character-bound effects; they are not graphic-system accents.

The concept angle: **a collectible field guide suddenly comes alive—each catalogue entry breaks pose, shows affection, then reveals the force hidden inside the companion.**

## Rhythm

`HOOK — SNAP — SNAP — SNAP — HERO HOLD`

- Hook: 0.0–2.2s
- Wildkin: 2.2–6.2s
- Elemental: 6.2–10.2s
- Relicborn: 10.2–14.2s
- Ensemble: 14.2–18.0s

Energy rises across the three introductions, peaks on the ensemble smoke reveal, then gives the brand lockup at least 2.2 seconds of readable hold.

## Global production rules

- Build five modular HyperFrames sub-compositions with a thin 18-second host timeline.
- Keep load-bearing content within portrait social safe areas: 90px side inset, 150px top inset, and 260px bottom inset.
- Use a consistent catalogue system: family number, uppercase mono family label, species name, one short identity line, fine rules, crop marks, and a three-position progress marker.
- Each family scene has at least three depth layers: textured dark field, oversized cropped sprite/ghost silhouette, and foreground catalogue chrome.
- Sprite rendering stays crisp with `image-rendering: pixelated`; every crop is a 256×256 cell from the supplied 1024×1280 transparent sheets.
- Everyday pose animation uses deterministic frame swaps or short walk cycles. Signature poses are single authored cells enhanced only by CSS/GSAP scale, rotation, localized glow, particles, and camera motion.
- No narration or captions. Text is part of the designed frame.
- Primary transition: 0.28–0.34s velocity-matched vertical push/whip between family scenes.
- Accent transition: the repository’s eight-frame smoke-poof at the ensemble reveal.
- Never animate `display`, `visibility`, layout dimensions, or runtime clocks. No infinite repeats or unseeded randomness.
- Background music and every SFX asset are mounted as direct children of the host composition.

## Scene 1 — the catalogue opens

### Concept

The viewer arrives inside a bold creature catalogue just as it powers on. The orange register behaves like a printed cover torn open by a thin vertical dark aperture; the words “meet the worklings” feel like the title of a living field guide, not a generic social title card.

### Mood

Collectible game manual, protest poster, premium creature reveal. Immediate, tactile, and confident.

### Depth and elements

- BG: full fire-orange field with faint ink registration grid.
- BG: oversized ghost “01 / 03” catalogue numeral drifting upward.
- MG: lowercase Teko display “meet the” over “worklings”.
- MG: a narrow dark portal containing three tiny sprite silhouettes.
- FG: mono “COMPANION FIELD GUIDE” kicker.
- FG: top and bottom hairlines.
- FG: crop marks and “09:16 / ORIGIN INDEX” metadata.
- FG: three-position marker with the first position armed.

### Choreography

- Orange field CUTS on from black on the first audio transient.
- The dark portal DRAWS vertically from the middle.
- “meet the” SLIDES from the top while “worklings” STAMPS upward from below with a short overshoot.
- The three silhouettes STEP into their slots one beat apart.
- Catalogue chrome TYPES ON and rules GROW toward the edges.
- A slow camera PUSH continues throughout the beat.

### Audio

- 0.00s: compact magical catalogue-open impact.
- 1.55s: restrained rising air pull preparing the first transition.

### Transition

At 2.2s, a 0.32s upward velocity-matched push: the orange cover accelerates upward while the Wildkin’s dark field rises from below. Pair with the Wildkin leaf-rush SFX.

## Scene 2 — Wildkin / moss-fox

### Concept

The Wildkin entry begins as a living woodland specimen: leafy tail filling the vertical frame, small fox alert at its center. It walks into position, softens into a delighted expression, then releases a coiled nature signature that turns the catalogue’s rules into wind-swept arcs.

### Mood

Forest familiar meets sports-card hero portrait. Nimble, warm, spirited.

### Depth and elements

- BG: ink-black `#0B1224` field with subtle fern-like arcs traced in fire-orange hairlines.
- BG: huge, low-opacity cropped tail silhouette from the idle sprite.
- BG: three short catalogue ticks breathing near the left edge.
- MG: Wildkin sprite large and slightly below center, moving through walk-contact and happy poses.
- MG: a thin vertical orange selection lane behind the sprite.
- FG: “01 / WILDKIN” mono label.
- FG: lowercase “moss-fox” display name.
- FG: identity line “wild at heart.”
- FG: progress marker with position one active.
- FG: bottom-right pose metadata “NATURE / SIGNATURE”.

### Choreography

- The selection lane WIPES upward.
- The fox WALKS in through four deterministic cells, landing on happy.
- “moss-fox” ASSEMBLES from a tight vertical crop.
- The identity line LOCKS IN under a hairline.
- At 5.25s global time, the happy pose SCALE-SWAPS into the signature cell while the camera PUNCHES forward and leaf/rune arcs ORBIT once.
- The signature settles without an element-by-element exit.

### Audio

- 2.20s: leafy air rush with a light twig/foliage texture.
- 5.25s: compact nature-energy bloom and low soft impact.

### Transition

At 6.2s, a 0.30s upward whip: Wildkin accelerates out, Elemental rises with a slight heat blur. Pair with an ember ignition whoosh.

## Scene 3 — Elemental / ember-newt

### Concept

The Elemental entry is a furnace specimen under observation. Its tail ember becomes the scene’s moving beacon; the creature blinks, brightens, then snaps into a fire-ring signature that appears to heat the catalogue ink itself.

### Mood

Arcade boss-introduction energy filtered through the same disciplined field-guide system. Curious first, explosive second.

### Depth and elements

- BG: ink-black-alt `#06292C` field with sparse orange thermal contour lines.
- BG: oversized ember orb cropped near the upper-right.
- BG: vertical heat ticks that pulse once.
- MG: Elemental sprite large at center, starting idle/blink and resolving happy.
- MG: narrow orange temperature rail aligned to the sprite’s tail.
- FG: “02 / ELEMENTAL” mono label.
- FG: lowercase “ember-newt” display name.
- FG: identity line “fire within.”
- FG: progress marker with position two active.
- FG: bottom-right pose metadata “EMBER / SIGNATURE”.

### Choreography

- Heat contours DRAW outward from the tail orb.
- The newt BOBS in on a spring and BLINKS once through deterministic cell changes.
- Display type SLAMS in from alternating sides, then holds.
- The temperature rail FILLS exactly once.
- At 9.25s global time, the sprite SCALE-SWAPS into the signature cell; an orange ring EXPANDS, the frame heats briefly, and the camera SHAKES by a controlled few pixels.
- The signature remains visible through the handoff.

### Audio

- 6.20s: tight ember ignition and fast flame whoosh.
- 9.25s: contained fire-ring burst with warm bass body, no explosion tail.

### Transition

At 10.2s, a 0.30s upward whip that resolves from heat blur into precise mechanical focus. Pair with a clockwork ratchet and rune chime.

## Scene 4 — Relicborn / keyback pangolin

### Concept

The Relicborn entry feels like an ancient mechanism accepting a key. The pangolin arrives with deliberate weight, braces under its plated silhouette, and releases a circular rune signature that turns the catalogue into a calibrated relic interface.

### Mood

Ancient machine, precision watch, defensive hero. Deliberate and powerful rather than slow.

### Depth and elements

- BG: ink-black field with concentric hairline rune circles and four sharp calibration marks.
- BG: oversized cropped wind-up key silhouette rotating a few degrees.
- BG: faint scale pattern built from offset arcs, never a repeated tile grid.
- MG: Relicborn sprite large at center, moving idle → cared-for → brace → signature.
- MG: orange mechanical axis crossing behind the key.
- FG: “03 / RELICBORN” mono label.
- FG: lowercase “keyback” over “pangolin”.
- FG: identity line “built to endure.”
- FG: progress marker with position three active.
- FG: bottom-right pose metadata “RUNE / SIGNATURE”.

### Choreography

- Calibration marks SNAP into the four frame edges.
- The key TURNS with a small recoil.
- The pangolin DROPS into its grounded position, then shifts through cared-for and brace poses.
- The two-line name LOCKS IN like stamped metal type.
- At 13.25s global time, the brace SCALE-SWAPS into the signature cell and the rune circle DRAWS clockwise while the camera PUSHES in.
- Motion suspends for a fraction before the smoke reveal.

### Audio

- 10.20s: brass clockwork ratchet, key turn, and clean rune ping.
- 13.25s: low mechanical lock plus radiant rune swell.

### Transition

At 14.2s, the eight-frame repository smoke sprite expands over the character. A bespoke poof impact masks the cut; smoke disperses to reveal all three Worklings together.

## Scene 5 — three families, one companion

### Concept

The catalogue pages collapse into one final team portrait. Wildkin, Elemental, and Relicborn form a triangular hero grouping on the dark register; the piece ends on affection and collectibility, not combat.

### Mood

Hero roster lockup, warm triumph, clean brand memory.

### Depth and elements

- BG: ink-black field with a broad orange vertical panel occupying roughly one third of the frame.
- BG: three oversized family numerals at controlled low opacity.
- BG: faint diagonal energy paths connecting the characters.
- MG: all three transparent sprites in happy/victory poses, overlapping in a stable triangle.
- MG: a single shared ground line and subtle contact shadows made from flat translucent ellipses.
- FG: lowercase “three families.” display line.
- FG: orange plate with “one companion.” in dark ink.
- FG: mono “WORKLINGS / ORIGIN SET” kicker.
- FG: completed three-position marker.
- FG: small “meet yours.” closing prompt above the social safe-area floor.

### Choreography

- Smoke EXPANDS through frames 0–4 and DISPERSES through frames 5–7.
- The three characters POP outward from the smoke with staggered spring settles.
- “three families.” SLIDES into its final edge anchor.
- The orange “one companion.” plate GROWS horizontally and the words STAMP into it.
- Energy paths DRAW once, then stop.
- Characters share one subtle synchronized breath while the camera makes a final 2% push.
- Final 0.6s remains visually stable; a restrained fade to ink-black is optional only after the readable hold.

### Audio

- 14.20s: magical smoke poof with soft low impact and sparkling dispersal.
- 15.10s: short three-note creature-collection flourish.
- 17.35s: music resolves cleanly; no long reverb beyond the video.

## Recurring motifs

- Sequential mono family numbering: 01, 02, 03.
- A three-position progress indicator advancing once per character.
- One vertical orange selection lane that changes function per family: path, heat rail, mechanical axis.
- Hairline arcs derived from each creature: fern curl, ember orbit, rune circle.
- Camera always advances slightly; nothing drifts like a screensaver.

## Audio Lab asset plan

- `meet-worklings-bgm`: preferred-quality instrumental music, immediate hook, approximately 132 BPM, warm percussion, plucked fantasy motif, compact brass/strings, three escalating character sections, clean heroic ending, no vocals.
- `catalogue-open`: compact magical printed-field-guide opening impact.
- `wildkin-leaf-rush`: isolated leafy air rush with one soft nature-energy bloom.
- `elemental-ember-whip`: fast flame ignition and contained ember burst.
- `relicborn-clockwork-rune`: brass ratchet/key turn resolving into a clean rune ping.
- `ensemble-smoke-poof`: magical soft smoke poof with low body and sparkling dispersal.
- `collection-flourish`: deterministic short three-note tonal victory/collection cue.

Use explicit Audio Lab engines, fixed seeds, 48 kHz WAV masters, crop-before-fade where applicable, peak normalization, delivery exports, and `audio-lab verify` provenance checks.

## Negative prompt

- No generic fantasy trailer montage; the existing teaser already occupies that territory.
- No fake gameplay UI, fabricated stats, invented abilities, health bars, or unshipped claims.
- No narration, subtitles, rounded cards, shadows, gradients, bokeh, generic particles, or extra graphic accent colors.
- No tiny web typography, centered floating stacks, static slides, random idle drift, infinite animation, or independent elements moving without a shared camera intention.
- No smoothing of the pixel art and no AI-generated replacement character imagery.
- No combat staging in this first film; signature poses communicate latent power without turning the introduction into the combat video.
