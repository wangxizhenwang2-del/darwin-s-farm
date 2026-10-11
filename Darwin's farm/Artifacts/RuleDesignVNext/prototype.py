"""Disposable numerical sanity check for the proposed ecology rules.

This is not the Unity simulator. It tests resource conservation, branch ratios,
and whether the proposed two-lane food contest permits coexistence.
"""

from dataclasses import dataclass
from math import ceil


@dataclass
class Pop:
    name: str
    n: float
    size: float
    fertility: float
    movement: float = 20
    fitness: float = 1

    @property
    def lane(self):
        return "small" if self.size < 30 else "large"

    @property
    def b0(self):
        return self.n * 0.05 * self.fertility / 100 * self.fitness

    @property
    def need(self):
        adult = self.n * (self.size + 0.001 * self.movement**2)
        youth = self.b0 * (2 * self.size + self.size**2 / 10)
        return adult + youth


def waterfill(available, demand, weight):
    received = [0.0] * len(demand)
    active = {i for i, d in enumerate(demand) if d > 0 and weight[i] > 0}
    remaining = available
    while active and remaining > 1e-9:
        total_weight = sum(weight[i] for i in active)
        saturated = [i for i in active if remaining * weight[i] / total_weight >= demand[i] - received[i]]
        if not saturated:
            for i in active:
                received[i] += remaining * weight[i] / total_weight
            break
        for i in saturated:
            gain = demand[i] - received[i]
            received[i] += gain
            remaining -= gain
            active.remove(i)
    return received


def one_day(pops, stock, recovery=100_000, max_stock=1_000_000, gamma=1.5):
    stock = min(max_stock, stock + min(recovery, max_stock - stock))
    demands = [p.need for p in pops]
    lane_biomass = {lane: sum(p.n * p.size for p in pops if p.lane == lane)
                    for lane in ("small", "large")}
    weights = [p.n * p.size * p.fitness * (0.75 + 0.004 * p.movement) /
               (1 + gamma * lane_biomass[p.lane] / max(1, recovery))
               for p in pops]
    received = waterfill(stock, demands, weights)
    for p, d, food in zip(pops, demands, received):
        satisfaction = min(1, food / d) if d else 0
        births = p.b0 * satisfaction
        food_deaths = 0.6 * p.n * (1 - satisfaction)
        p.n = max(0, p.n + births - food_deaths)
    stock -= sum(received)
    return stock, [min(1, food / d) if d else 0 for food, d in zip(received, demands)]


def run():
    for future_recovery in (100_000, 150_000):
        one = [Pop("chicken", 100, 8, 75)]
        stock = 100_000
        for _ in range(125):
            stock, _ = one_day(one, stock)
        n = one[0].n
        large = n * 10 / (38 + 10)
        small = n - large
        two = [Pop("large", large, 38, 45), Pop("small", small, 10, 90)]
        print("branch", future_recovery, round(n), round(stock), round(large),
              round(small), round(one[0].need), round(sum(p.need for p in two)))
        for day in range(1, 241):
            stock, sat = one_day(two, stock, future_recovery)
            if day in (1, 7, 14, 30, 60, 240):
                print("split", future_recovery, day, *[round(p.n) for p in two],
                      round(stock), *[round(x, 3) for x in sat])

    n, original_size, large_size, small_size = 1000, 8, 38, 10
    n_large = n * small_size / (large_size + small_size)
    n_small = n - n_large
    print("mass-check", round(n_large, 3), round(n_small, 3),
          round(n * original_size), round(n_large * large_size + n_small * small_size))

    # Reachability counts assume every chosen +5/-5 step is actually selected.
    targets = {
        "deer": (40, 25, 75, 60, 30),
        "fierce_deer": (22, 25, 65, 70, 30),
        "civet": (60, 70, 50, 20, 60),
        "sparrow_lion": (70, 50, 70, 80, 20),
    }
    routes = (
        ("chicken_large", (61, 50, 20, 38, 45), "deer"),
        ("deer", targets["deer"], "fierce_deer"),
        ("deer_small", (40, 25, 75, 25, 60), "civet"),
        ("civet_large", (60, 70, 50, 50, 30), "sparrow_lion"),
    )
    for source_name, source, target_name in routes:
        distance = [abs(a - b) for a, b in zip(source, targets[target_name])]
        checks = sum(max(0, ceil((d - 5) / 5)) for d in distance)
        print("reachability", source_name, target_name, distance, checks)

    def capture(pm, ps, em, es):
        speed = max(0.5, min(1.5, 1 + (pm - em) / 40))
        body = max(0.25, min(1.5, ((ps + 10) / (es + 10)) ** 2))
        return min(1, 0.5 * speed * body)

    for label, traits in (
        ("civet-chicken", (50, 20, 20, 10)),
        ("civet-deer", (50, 20, 75, 60)),
        ("lion-deer", (70, 80, 75, 60)),
    ):
        print("capture", label, round(capture(*traits), 3))

    attraction = 0.82
    for movement, elevation_step in ((20, 0), (35, 0), (35, 1), (65, 1)):
        threshold = 30 if elevation_step == 0 else 60
        chance = attraction * movement / 100 if movement > threshold else 0
        print("migration", movement, elevation_step, round(chance, 3))

    # Static deer conversion stress test after the R=150k branch equilibrium.
    # It does not simulate the trait path or the stochastic conversion event.
    for chicken_fitness in (1.0, 1 - (21 + 25) / 90):
        paired = [Pop("deer", 1785, 60, 30, 75, 1),
                  Pop("chicken", 6897, 10, 90, 20, chicken_fitness)]
        stock = 0
        for day in range(1, 501):
            stock, _ = one_day(paired, stock, 150_000)
            if day in (30, 120, 500):
                print("conversion-stress", round(chicken_fitness, 3), day,
                      *[round(p.n, 2) for p in paired])


if __name__ == "__main__":
    run()
