# Biome artwork integration

PASS: 21 integration groups; 24133 transition frames observed.

- 32 placed biome/rotation combinations: source materials, UV coverage, shared edge colors/heights, collision ownership and build previews.
- Desert: Real terrain movement checks: One representative / 120 simulated seconds: 213.2m, reached 27/28 targets, longest stop 0.04s, max sideways visual drift 0.000000m.
- Desert: live movement 9.46m / 5 seconds, identity/count preserved.
- Grassland: Real terrain movement checks: One representative / 120 simulated seconds: 213.4m, reached 21/22 targets, longest stop 0.04s, max sideways visual drift 0.000000m.
- Grassland: live movement 8.13m / 5 seconds, identity/count preserved.
- Forest: Real terrain movement checks: One representative / 120 simulated seconds: 213.0m, reached 17/18 targets, longest stop 0.04s, max sideways visual drift 0.000000m.
- Forest: live movement 9.32m / 5 seconds, identity/count preserved.
- Rainforest: Real terrain movement checks: One representative / 120 simulated seconds: 213.3m, reached 26/27 targets, longest stop 0.04s, max sideways visual drift 0.000000m.
- Rainforest: live movement 7.56m / 5 seconds, identity/count preserved.
- Tundra: Real terrain movement checks: One representative / 120 simulated seconds: 213.4m, reached 19/20 targets, longest stop 0.04s, max sideways visual drift 0.000000m.
- Tundra: live movement 4.98m / 5 seconds, identity/count preserved.
- Highland: Real terrain movement checks: One representative / 120 simulated seconds: 212.9m, reached 23/24 targets, longest stop 0.04s, max sideways visual drift 0.000000m.
- Highland: live movement 9.37m / 5 seconds, identity/count preserved.
- SnowMountain: Real terrain movement checks: One representative / 120 simulated seconds: 213.4m, reached 26/27 targets, longest stop 0.04s, max sideways visual drift 0.000000m.
- SnowMountain: live movement 9.38m / 5 seconds, identity/count preserved.
- Volcano: Real terrain movement checks: One representative / 120 simulated seconds: 213.3m, reached 26/27 targets, longest stop 0.04s, max sideways visual drift 0.000000m.
- Volcano: live movement 5.03m / 5 seconds, identity/count preserved.
- All 56 ordered biome pairs (112 forward/return changes): retained outgoing material, faded material swaps, final cleanup, stable navigation after settlement.
- 16 interrupted biome changes: no retired visuals/probes, stable identity and recovery.
- Water coverage retains its existing surface/selection behavior and no land decorations.
- Exactly eight cached runtime ground materials after all changes; no per-transition material allocation.