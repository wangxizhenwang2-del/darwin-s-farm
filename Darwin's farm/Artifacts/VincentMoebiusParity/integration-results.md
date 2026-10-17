# Biome artwork integration

PASS: 21 integration groups; 27844 transition frames observed.

- 32 placed biome/rotation combinations: source materials, UV coverage, shared edge colors/heights, collision ownership and build previews.
- Desert: Real terrain movement checks: One representative / 120 simulated seconds: 213.0m, reached 28/29 targets, longest stop 0.04s, max sideways visual drift 0.000000m.
- Desert: live movement 7.31m / 5 seconds, identity/count preserved.
- Grassland: Real terrain movement checks: One representative / 120 simulated seconds: 211.6m, reached 45/46 targets, longest stop 0.04s, max sideways visual drift 0.000000m.
- Grassland: live movement 7.28m / 5 seconds, identity/count preserved.
- Forest: Real terrain movement checks: One representative / 120 simulated seconds: 211.4m, reached 46/47 targets, longest stop 0.04s, max sideways visual drift 0.000000m.
- Forest: live movement 1.04m / 5 seconds, identity/count preserved.
- Rainforest: Real terrain movement checks: One representative / 120 simulated seconds: 212.2m, reached 40/41 targets, longest stop 0.04s, max sideways visual drift 0.000000m.
- Rainforest: live movement 8.06m / 5 seconds, identity/count preserved.
- Tundra: Real terrain movement checks: One representative / 120 simulated seconds: 211.5m, reached 43/44 targets, longest stop 0.04s, max sideways visual drift 0.000000m.
- Tundra: live movement 9.10m / 5 seconds, identity/count preserved.
- Highland: Real terrain movement checks: One representative / 120 simulated seconds: 211.7m, reached 40/41 targets, longest stop 0.04s, max sideways visual drift 0.000000m.
- Highland: live movement 3.93m / 5 seconds, identity/count preserved.
- SnowMountain: Real terrain movement checks: One representative / 120 simulated seconds: 212.5m, reached 36/37 targets, longest stop 0.04s, max sideways visual drift 0.000000m.
- SnowMountain: live movement 7.29m / 5 seconds, identity/count preserved.
- Volcano: Real terrain movement checks: One representative / 120 simulated seconds: 212.5m, reached 35/36 targets, longest stop 0.04s, max sideways visual drift 0.000000m.
- Volcano: live movement 5.14m / 5 seconds, identity/count preserved.
- All 56 ordered biome pairs (112 forward/return changes): retained outgoing material, faded material swaps, final cleanup, stable navigation after settlement.
- 16 interrupted biome changes: no retired visuals/probes, stable identity and recovery.
- Water coverage retains its existing surface/selection behavior and no land decorations.
- Exactly eight cached runtime ground materials after all changes; no per-transition material allocation.