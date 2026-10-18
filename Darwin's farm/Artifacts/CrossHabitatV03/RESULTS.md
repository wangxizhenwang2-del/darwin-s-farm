# CrossHabitat v0.3 agent result

## Scope and baseline

- Isolated worktree: `G:\xh03\Darwin's farm`; branch `codex/cross-habitat-v03`, based on `codex/water-land-v02` (`99ca32f4`). The original checkout and previous round checkout were clean at task start; this task did not modify either. The original checkout now has separate concurrent changes, which were left untouched.
- Unity: `6000.2.9f1`. Reference package: `Darwin_CrossHabitat_v03_AgentPackage.zip`.
- Offline: `python -m unittest discover -v` passed 53/53; `python research_driver.py` completed 4,500 water days, max material residual `1.862645149230957e-09`. See `offline-*.txt`.
- New water ecology remains OFF by default. Economy and random events were off in the water mechanism fixtures. Formal cross niche evolution and flight switches were not enabled.

## Source changes

- Each water cell now owns its physical plant stock and regrowth. Basin rotation/splitting no longer averages/duplicates physical stock. Legacy `algaeBiomass/algaeRecovery` are no longer part of active food/material accounting.
- A connected water basin is settled once per `SimulateDay`, reusing `LandLifeRules` allocation, hunting, paid births/deaths and the existing `FoodWeb` prey whitelist. Population objects remain in their single home cell.
- The mass ledger includes water body, reserve, plant growth, detritus and sink when water ecology is on.
- Water migration and water/land route preflight require the population's actual local source cell. No old random conversion was enabled.
- The V048 replay fixture can enable water ecology explicitly and exports real daily state/hash traces.

## Test matrix

`PASS` means only the stated scenario passed. `PARTIAL` does not satisfy the full package case.

| ID | Result | Evidence / gap |
|---|---|---|
| W01 | PASS L2 for fixed topology | Real 100 day V048 `SimulateDay`, 3 water cells / 3 populations; one basin budget, separate cell stock. Max physical stock residual 0.0625, world material residual 0.0625 float units. |
| W02 | PASS L2 for day 1 counterfactual | Real 5 day paired V048 runs. Lower shrimp group (5,000→1,000) increases deer allocated food on day 1 from 8,392.7578125 to 61,459.8046875. |
| W03 | PASS L2 for tested food chain | Real shark asset prey whitelist includes shrimp, excludes deer. In 100 day V048 trace shrimp has 818 predation deaths; deer has 0. No full species web coverage. |
| W04 | PASS L2 editor fixture | Real `SimulateDay`: R=0 spends old S on day 4; S=R=0 spends no plant and herbivores decline from 622 to 0 by day 14. |
| W05 | PARTIAL L2 | Split day 2/reconnect day 3 in editor fixture conserves stock and keeps shrimp in w3. No 100 day rotating topology trace. |
| W06 | PASS L1 | 25 editor boundary checks cover mutual openings, heights 0/1/2, diagonal and one sided links. |
| W07 | PASS L1, FAIL L2 requirement | w3→far A rejected, w3→near B route accepted, movement to w1 resets qualification and permits near A route. No formal evolution transaction. |
| W08 | NOT RUN / BLOCKED | No real cross niche formal evolution scheduler or paid transaction. |
| W09 | NOT RUN / BLOCKED | Cross niche payment rejection and atomic transaction unavailable. |
| W10 | PARTIAL L1 | Local slot/source position checked; target basin food and formal transfer untested. |
| W11 | PARTIAL L2 | Fixed topology 100 day double replay has identical 101 model hashes; required 200 days with topology edits/migration not run. |
| A01–A09 | NOT RUN / BLOCKED | Flight lifecycle, paid 50 founder conversion, migration branch semantics and reverse conversion not integrated. Undecided 25%, branch size and probability remain offline only. |
| R01 | PASS L2 | Original land five species seed 240, days 0..100: all 101 model hashes equal v0.2 trace. |
| R02 | NOT RUN | Old A–D operation fixtures not replayed. |
| R03 | EXISTING FAIL day 1 | Same `V43LandRulesAudit` reverse edge assertion fails in v0.2 and original baseline; see `R03-existing-baseline.txt`. |
| R04 | PARTIAL | R01 confirms water switch OFF has no hash difference in that land scenario; all economy/disaster/air combinations not tested. |

## Logs and interpretation

- `water-100.trace.txt` and `water-100-repeat.trace.txt`: actual Unity V048 day 0..100 states and hashes. `land-five-species-100.trace.txt`: land regression. `unity-logs/`: Unity invocation, success markers and editor fixture events.
- `daily_water_ledger.csv`, `pop_day.csv`, `*-day_hashes.jsonl`, `topology_events.csv`, `run_config.json`, `trace-audit.json`: derived from actual Unity traces/logs; `export_evidence.py` regenerates the derived files. The topology editor fixture does not export a hash, so that CSV says so explicitly.
- `cross_niche_events.csv` has a header only because cross niche events did not occur. It is not evidence of a passing cross niche test.
- The first failure day for newly executed passing tests: none. R03 has the inherited day 1 failure. W08/W09 and A01–A09 are untested, not failed runs.

## Blockers and acceptance

The current formal evolution gate and commit assume source and target share one land block. Connecting water↔land requires a destination food forecast over the correct local tile/basin, physical payment from the correct stock, a cross block split, and atomic rejection. Activating the current route preflight as a commit would bypass those requirements, so cross niche formal evolution remains disconnected from the daily scheduler. Flight has analogous unresolved real lifecycle and transaction work. No ecology formulas or default production parameters were invented.

Highest achieved level: **L2 for the tested water daily loop and land regression**. Cross habitat evolution and flight remain **L1 route only / below L2**. **L3 was not attempted**.
