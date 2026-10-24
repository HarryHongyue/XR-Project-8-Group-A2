# ET And Cockpit Interaction Redesign

## Requirements And Sources

- Project 08: preserve propagation, transverse strain, pause/replay, parameters,
  viewpoints and prediction tasks. Label exaggerated deformation and slowed time.
- Track A: reveal detail progressively instead of placing all explanations in space.
- VRSUQ/SUS: predictable controls, visible feedback, recoverable actions, consistency.
- SSQ/Presence: readable text, low visual clutter and immediate pause access.
- ET reference: Design Report Update 2020, figure 1.8, triangular baseline:
  https://www.einsteintelescope-emr.eu/wp-content/uploads/2024/05/ET-0007B-20_ETDesignReportUpdate2020.pdf
- Official regional overview and illustration:
  https://www.einsteintelescope-emr.eu/en/einstein-telescope-in-brief/
- Official optics principle:
  https://www.einsteintelescope-emr.eu/en/optics-and-optical-metrology/
- Local image reference: `.tools/et-official-layout-reference.jpg`.
- This illustrates the published triangular ET concept, not a claim that the final
  observatory layout is fixed. Three 60-degree detectors, each with LF/HF instruments,
  must not be described as three perpendicular LIGO detectors.

## 1. Horizontal ET Observatory [Implementation Delegated]

1. Replace the upright L-shaped display with a horizontal equilateral triangle.
2. Use a 12-unit displayed side; actual concept side length is 10 km.
3. Local corners: (-6,0,-3.4641016), (6,0,-3.4641016), (0,0,6.9282032).
4. Observatory centre: (82,1.5,26); no rotation out of the horizontal XZ plane.
5. Build three corner stations. Each station has two arm axes toward its neighbours,
   separated by 60 degrees, with independently moving end test masses.
6. Model the optical response by projecting the transverse strain tensor onto each
   arm. Do not force all pairs to have equal/opposite changes at arbitrary incidence.
7. Retain split, round-trip and recombination paths; add near/end cavity mirrors.
8. Show LF/HF as distinct paired optical lanes, while explicitly stating that their
   thermal/noise differences and full recycling optics are not numerically simulated.
9. Default to one highlighted detector. D1/D2/D3 selection reveals its optical paths.
10. Put explanations, channel values and the short colour legend in the console.
11. Remove world-space prose and labels around the sheet, black holes and apparatus.
12. Raise the final observation position above the horizontal model for an oblique
    overview without forcing the player's physical head rotation.

## 2. Separate Subtitles From Controls [Implementation Delegated]

1. One subtitle presenter serves both story narration and AI answers.
2. White text, subtle black shadow, at most two lines per page. No Image background,
   glass rectangle, outline, buttons or pointer hit targets on the subtitle canvas.
3. Paginate long replies using language-aware boundaries; pace by unscaled time so
   explanations remain readable while the simulation is paused.
4. An AI answer temporarily interrupts narration and then restores the latest story
   page sequence; a new story event replaces old queued story text.
5. Mission start/continue/restart controls live in a separate action dock, not subtitles.
6. The Rick avatar opens only the question composer with ASK and rightmost MIC.
   Answers appear through the shared subtitles, not a permanent answer popup.

## 3. Unified Cockpit Layout [Implementation Delegated]

1. Left rail (-0.80, 1.60, 1.20): RICK · A2 title plus Controls and Mission/Help
   drawer buttons, mutually exclusive drawers.
2. GlobalPlayback (-0.80, 0.92, 1.20): persistent Play/Pause + clock outside
   every drawer; shows START only in the true parked Waiting state.
3. MissionActionDock (-0.80, 1.12, 1.20): Start/Continue/Restart as contextual
   actions sharing one rect - only the phase-appropriate one is active.
4. SubtitleBar (0, 0.72, 1.40): non-interactive two-line subtitles.
5. RickAvatar (0.80, 0.92, 1.20): glass chip + collapsed question composer.
6. Controls drawer (1120x1080) uses two tabs via `ConsoleTabs`: WAVE LAB
   (shape sliders, four slot cards 2x2, Save/Load or Confirm/Cancel in the same
   two positions, one transport row Replay Current / Reset / 0.25x / 0.5x / 1x,
   mission tools, status) and ET (station D1/D2/D3 selector, detail toggle,
   selected-station indicator, LF/HF channel readout box).
7. Mission/Help drawer shares the same central bounds and carries the honest
   simplification list trilingually.
8. Every interactive panel face carries a transparent `PanelHitArea` ray
   blocker so blank glass regions do not pass pointer rays through to the
   world; all decorative layers stay raycast-free.
9. Main controls must remain available through mouse and existing XR ray modules.
   Do not replace the working controller rig or enable competing locomotion.

## 4. Slot State Machine [Implementation Delegated]

1. Exactly four independent optional snapshots; initially selected slot is 1.
2. Clicking any card only selects it. It never saves, loads, replays or changes time.
3. Save To Selected copies the current parameters into that slot only.
4. Occupied slot: first Save requests explicit inline overwrite confirmation;
   Confirm Overwrite performs it. Cancel, selection change or parameter change cancels.
5. Load Selected is enabled only for an occupied slot. It applies the snapshot and
   resets the wave preview paused at the current observation point. It does not
   overwrite, autoplay, move the ship or restart the mission.
6. Replay Current always uses the current slider values; it never silently loads a
   selected snapshot. To replay a slot: Select, Load, Replay.
7. Cards show stored amplitude/frequency/exaggeration, not live slider values.
8. Store detached snapshots, never pad missing slots with the same object reference.
9. Preserve legacy JSON data on migration; retain its original file. No inferred
   reconstruction of already duplicated legacy snapshots.
10. Test serialization in memory. Editor persistence stays within the workspace;
    standalone builds use the normal application persistence location.

## 5. Playback State Consistency [Implementation Delegated]

1. Play at initial Waiting starts the mission and simulation together.
2. Pause freezes simulation-driven travel, sliding, wave effects and detector markers.
3. Resume continues the same phase/time, without restarting.
4. Replay Current preserves the current viewpoint and parameters. At Earth or ET,
   start the preview clock one displayed second before the wave reaches that target.
   Suppress automatic mission travel during the preview; do not replay the journey.
5. Reset returns the wave preview to that same arrival-relative starting time paused.
   Restart Mission is a distinct contextual action that resets the journey and ship.
6. Continue is contextual, never a second ambiguously named Play button.
7. Button labels reflect the actual simulation state, even with drawers closed.
8. Replay/Reset/Load are offered only at the stable observation phases
   (Waiting, EarthPause, Done); during WatchingMerger and the ride/slide
   transits the buttons disable and the calls no-op - a preview must never
   freeze the journey without an escape.
9. START after a preview clears preview state first: parked pose, t=0, play.

## 6. Liquid-Glass Visual System [Implementation Delegated]

1. Restyle Welcome, Controls, Mission/Help, rail, mission dock, pause chip, Rick composer,
   slot cards, buttons, inputs and sliders using the same material roles and spacing.
2. Use rounded translucent smoke-glass surfaces, restrained cool highlights, top-edge
   sheen, soft shadows, recessed slider tracks and luminous thumb highlights.
3. Keep text neutral white/off-white with clear hierarchy; cyan only for active state.
4. Do not implement expensive full-screen blur/GrabPass on Quest or pretend a flat
   transparent fill is physical refraction. Use a lightweight procedural glass effect.
5. Minimum button height 48 canvas units, consistent gutters and no overlapping labels.
6. Decorative layers never receive rays. Interactive surfaces remain raycastable.

## 7. Verification And Delivery [Implementation Delegated; Review Lead-Owned]

1. Preserve recovery files, user edits and backups; no deletions or editor termination.
2. Extend additive editor regressions for ET geometry/arm projections, independent
   slot snapshots, explicit selection/save/load/confirm semantics and playback resets.
3. Assert subtitles have no background/buttons/raycast targets and floating prose is gone.
4. Assert persistent Pause is outside drawers and connected to the same playback state.
5. Compile and regenerate via the running editor's opt-in request runner only after
   code review. The existing successful result belongs to the previous revision.
6. Save fresh validation results and desktop visual evidence under workspace Logs.
7. Leave Main open. Report VR hardware/performance as unverified unless actually tested.
