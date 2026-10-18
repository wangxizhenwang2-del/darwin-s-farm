# Biome artwork integration

PASS: 21 integration groups; 19435 transition frames observed.

- 32 placed biome/rotation combinations: source materials, UV coverage, shared edge colors/heights, collision ownership and build previews.
- Desert: Real terrain movement checks: One representative / 120 simulated seconds: 213.1m, reached 29/30 targets, longest stop 0.04s, max sideways visual drift 0.000000m.
- Desert: live movement 5.10m / 5 seconds, identity/count preserved.
- Grassland: Real terrain movement checks: One representative / 120 simulated seconds: 213.5m, reached 23/24 targets, longest stop 0.04s, max sideways visual drift 0.000000m.
- Grassland: live movement 5.52m / 5 seconds, identity/count preserved.
- Forest: Real terrain movement checks: One representative / 120 simulated seconds: 211.7m, reached 43/44 targets, longest stop 0.04s, max sideways visual drift 0.000000m.
- Forest: live movement 5.57m / 5 seconds, identity/count preserved.
- Rainforest: Real terrain movement checks: One representative / 120 simulated seconds: 213.2m, reached 28/29 targets, longest stop 0.04s, max sideways visual drift 0.000000m.
- Rainforest: live movement 7.24m / 5 seconds, identity/count preserved.
- Tundra: Real terrain movement checks: One representative / 120 simulated seconds: 213.3m, reached 20/21 targets, longest stop 0.04s, max sideways visual drift 0.000000m.
- Tundra: live movement 7.29m / 5 seconds, identity/count preserved.
- Highland: Real terrain movement checks: One representative / 120 simulated seconds: 212.9m, reached 23/23 targets, longest stop 0.04s, max sideways visual drift 0.000000m.
- Highland: live movement 7.27m / 5 seconds, identity/count preserved.
- SnowMountain: Real terrain movement checks: One representative / 120 simulated seconds: 213.5m, reached 26/27 targets, longest stop 0.04s, max sideways visual drift 0.000000m.
- SnowMountain: live movement 4.93m / 5 seconds, identity/count preserved.
- Volcano: Real terrain movement checks: One representative / 120 simulated seconds: 213.2m, reached 27/28 targets, longest stop 0.04s, max sideways visual drift 0.000000m.
- Volcano: live movement 8.66m / 5 seconds, identity/count preserved.
- All 56 ordered biome pairs (112 forward/return changes): retained outgoing material, faded material swaps, final cleanup, stable navigation after settlement.
- 16 interrupted biome changes: no retired visuals/probes, stable identity and recovery.
- Water coverage retains its existing surface/selection behavior and no land decorations.
- Exactly eight cached runtime ground materials after all changes; no per-transition material allocation.