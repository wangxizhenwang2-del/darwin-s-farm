# Species unlock card plan

Goal: present each newly unlocked or starting species with a reusable UGUI card; release its ecology news toast at the reveal frame.

1. Add the four supplied images as Unity sprite assets. Slice the star and question sheets in the Editor importer; keep species portraits from `SpeciesData.Portrait`.
2. Add a scene-authored `SpeciesUnlockUI` under `JournalCanvas/SafeArea`, with all images, TMP labels, and continue button serialized. Use the current Canvas scaler and safe-area dimensions.
3. Add `SpeciesUnlockPresenter`: queue species requests, reset the card on every play, animate with unscaled coroutine time, emit `OnRevealStarted` exactly once at the silhouette-to-portrait transition, and wait for Continue.
4. Add a journal game-entry event so new games scan living starting species after an Inspector-configurable delay. Loaded games skip this opening sequence. Subscribe to the existing `SimulationController.OnSpeciesUnlocked` for later species.
5. Extend the existing ecology news HUD so presenter-owned NewSpecies messages are shown from the reveal callback and the detector's later duplicate is suppressed.
6. Add an Editor smoke test for timing, single and queued cards, startup delay, HUD timing, and responsive bounds. Run a Unity batch scene check and compile the project.

No ecological model, evolution, unlock decision, or numerical rule is modified.
