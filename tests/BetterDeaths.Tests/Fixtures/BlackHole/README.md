# Black Hole marker regressions

These are mechanic-only excerpts of completed local pulls 2649, 2671, 2673, 2674,
2675, 2681, and 2683. Each original reached Stomp-a-Mole after all ten Black
Hole rounds. Names and player keys are replaced. The fixture extractor preserves
the original pull times, including the boss turning before the first beam in set 4.

The tests have separate checks for the compass direction at appearance, recorded
beam geometry, holders and hits, and the clockwise DPS/support/Accretion order
these parties actually used. Only the tests know that order; the marker does not.
The beam hashes come from a before/after comparison with the previous analyzer.
One test deliberately passes a DPS tether to a healer.

The sibling `KefkaReplay` fixtures cover the same pulls through the last stomp.
They contain actor positions and mechanic events, without player records. The
Replay tests check the wall turns and footfalls against recorded times and
coordinates. Pull 2673 also checks that Replay can turn southeast while the
Analyzer still shows north for the fourth set.

Run the committed fixtures on any machine:

```sh
dotnet test tests/BetterDeaths.Tests --filter "FullyQualifiedName~BlackHoleMarkerRegressionTests|FullyQualifiedName~DmuKefkaReplayTests"
```

To also check the original captures on their owner's machine, set
`BETTERDEATHS_BLACK_HOLE_PULL_DIR` to the directory containing the original
`pull-*.json.gz` files and repeat. Do not commit the originals. To regenerate the
anonymized excerpts, run `node scripts/extract-black-hole-regressions.mjs <directory>`.
