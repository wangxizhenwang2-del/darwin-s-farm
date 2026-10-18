# 2026-10-18 local integration log

Target: local `Ye-Branch` (starting at `f936e0fd`). No remote push.
Integration branch: `codex/ye-local-integration-20261018`.

## Included

- Existing cross-habitat and cross-niche history through `00e8c21e` (fast-forward from Ye-Branch).
- Six categorized commits from the previously dirty primary worktree: animation/preview lab `3721293d`, bioweb data/import tools `927fc8c8`, Wwise state `5e0803b2`, scenes/UI `ac68a272`, simulation export/migration `00e6da3a`, handoff documentation `2be6efb4`.
- Removed 31 generated validation logs, traces, CSV and JSONL files in `8db77c82`. Kept source scenarios and implementation/manual documentation.

## Preserved, not directly overlaid

- `codex/v61-validation-local-20261018` at `f933d336`: generated font asset differed; editor settings normalized by Unity. Not overlaid on current UI.
- `codex/v61-safe-integration` at `e568cf2c`: 217 legacy animation metadata imports. Current animation layout has moved, so overlay would duplicate or break GUID mappings.
- `codex/darwin-mutation-v4-20261018` at `74bce395`: ecology/mutation changes collided with newer water/flight and V6.2 code; journal/save source also collided with newer UI and Wwise handling. No old version was substituted for current code.
- `codex/task12-ecology` at `d68c29ab`: economy patch was already equivalent in current tree; environment/ecology changes conflicted with newer rules and were not overlaid.
- Untracked `replays/` in mutation worktree were excluded as generated test data.

All pre-integration working tree contents were separately backed up under `G:\unity\darwin-integration-backup-20261018` (tracked binary patches, untracked ZIPs, source refs and manifests). The side branches above retain exact committed content.

## Checks

- Unity 6000.2.9f1 batch import and script compilation completed on the integrated worktree.
- `CrossHabitatV04SmokeChecks.RunBatch`: `CROSS_HABITAT_V04_SMOKE_PASS days=12`.
- No full gameplay or long ecological simulation was run. Unity's generated settings/meta edits from the validation run were restored.

## Follow-up

The preserved side branches need feature-by-feature review before any old ecology/environment/UI behavior can be ported. They are not represented as merged functionality in this integration.
