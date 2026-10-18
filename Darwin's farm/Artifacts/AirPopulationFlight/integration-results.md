# Air population flight

PASS: 9 air population integration groups.

- 鹿飞 renders with its own illustration/count; one air representative per tile.
- Land, rotated forest and water: 12 patrol/rotation combinations, 36,000 bounded low-flight samples; 12,000 origin samples and count labels remain in the unmodified game camera view.
- Live frames advance and air keeps flying without NavMesh; ecological identity/count unchanged.
- Natural migration, manual migration and community edits reject a second flying species atomically.
- Daily natural migration splits 40/40, visibly flies across the boundary, then resumes target patrol.
- Manual same-species arrival merges into the existing sky resident; total count remains 80.
- Whole manual migration to water retains population ID/count and removes the old patrol.
- Terrain elevation updates airspace; tile removal cleans flight objects and transitions.
- Competing natural migrations reserve one sky slot; the other species remains home and all 160 individuals are conserved.
