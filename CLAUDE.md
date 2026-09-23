# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Status

M1 complete for the `double` path. Read [`docs/spec.md`](docs/spec.md) before writing anything — it
settles architecture, data sources, propagators, the error decomposition, and the validation gates.

**Gate 1 passes over the whole published verification set**: 33 cases, 666 compared rows, near-earth
and deep-space, at 8.1e-9 km on position and 8.5e-10 km/s on velocity against a specified tolerance
of 1e-8 km. Both halves of the model are implemented — `Sgp4<T>` and `DeepSpace<T>` — and the suite
pins that the set exercises all three deep-space paths: no resonance (12 cases), the synchronous
resonance (7), and the half-day resonance (5).

**Two rows in that set are treated specially, both for stated reasons that are worth reading before
touching the tolerances.** One case is refused rather than compared, because its published row is a
stale buffer from the reference harness rather than a model output; see `ATTRIBUTION.md` beside the
data. And the file's one long arc — 3.5 years past epoch — is held to 2e-7 km rather than 1e-8,
because that is the round-off floor of `double` at that arc length and not a defect. Never loosen
either tolerance to make a change pass.

**The arithmetic error term is now measured, not projected.** `StorageComparisonTests` runs the
whole verification set in `float`, `double` and `PreciseNumber` at 30 significant digits, in about
five seconds, and reports:

| | digits | worst vs the published vectors | vs the 30-digit reference |
|---|---|---|---|
| `float` | 7 | **55.06 km**, and **0 refusals in 666 rows** | — |
| `double` | 16 | 8.1e-9 km (published arcs), 6.8e-8 km (long arc) | median **1.6e-10 km**, worst 7.0e-8 km |
| `decimal` | 28 | 4.2e-7 km | median **5.9e-11 km**, worst 4.0e-7 km |
| `PreciseNumber` | 30 | **7.3e-8 km** | — |

Four things to take from that table, all of which the tests assert:

1. **`double`'s arithmetic error is around eight and a half orders of magnitude below the data
   term.** A median of 1.6e-10 km is a sixth of a millimetre, against a measured **0.056 km** of
   element-set quantization. This is the repository's central claim and both sides of it are now
   numbers. It used to read "around ten orders" against "0.3 to 3 km of element-set quantization";
   that was a projection, the quantization has since been measured at a twentieth of it, and the
   claim survives comfortably either way. See below.
2. **`float` fails silently.** 55 km out and not one row reported an error: the model's error codes
   for an eccentricity or mean motion out of range are never tripped. It returns a confident wrong
   answer, which is the expensive failure mode.
3. **Thirty digits agrees with the published vectors *nine times worse* than `double` does.** Not a
   defect — the precise run is the more correct one. The published vectors were computed in
   `double`, so agreeing with them closely is a property of making the same rounding errors. If
   precision were the limiting factor, a 30-digit run would agree to 1e-30 km.
4. **`decimal`'s twelve extra digits buy a factor of 2.8, and cost a factor of 5.6.** Better than
   `double` at the median, worse at the extreme, and nowhere near the twelve orders of magnitude the
   digit counts suggest. See domain trap 11 for why.

**M2's data layer is in.** `Osculator.Data/CelesTrak/` holds a client, a response cache and a
snapshot store. The cache is the part with an obligation attached rather than a preference —
CelesTrak is free, is run by one person, and asks consumers to cache and refetch infrequently — so
its tests count requests through a stub transport against a clock the test moves by hand, and they
were **mutation-checked**: inverting the freshness comparison fails three of them, and removing the
age term fails one. A cache test never seen to fail is not evidence of a cache.

**Δ_data is measured, and it is much smaller than this file used to say.** `DataTermTests` runs
every usable case in the verification set, perturbs each element field by half its written step,
propagates, and combines the eight contributions in quadrature:

| | 1 day | 3 days | 7 days |
|---|---|---|---|
| median over 27 cases | **0.056 km** | — | **0.066 km** |
| range | 0.010 – 0.388 km | 0.010 – 4.49 km | 0.013 – 4.58 km |

Four things to take from that, all of which the tests assert:

1. **It is around 0.06 km, not the 0.3–3 km the spec projects.** The projection was out by a factor
   of five to fifty. Note carefully what is and is not measured here: this is the band of element
   sets that *would have been written identically*, which is pure quantization of the digits on the
   page. It is **not** how well the orbit is actually known — that includes the fit residual and the
   deliberate degradation of public element sets, and it cannot be measured from an element set
   alone. Both get loosely called Δ_data; only the first one is what this number is.
2. **Mean motion never leads, in any of the 27 cases.** Written expecting it to dominate at a week,
   since its error is a rate and therefore integrates. It does integrate and it still loses: half a
   step is 5e-9 rev/day, about 1.5 m of phase after seven days, while half a step of an angle is
   already 3 m at t=0. The rate has a week to catch up and does not manage it.
3. **The term barely grows with the arc for most cases, and explodes for a few.** The median moves
   from 0.056 to 0.066 km over a week, while 11801 goes 0.099 → 4.49 km and 16925 goes 0.187 →
   3.09 km. Both of those are eccentricity-led, which is where a half-step in the seventh decimal
   moves perigee enough to change the drag the orbit sees.
4. **`MeanMotionDot` contributes exactly zero, and so does `MeanMotionDdot`.** Not small — zero.
   `Sgp4.cs` never reads either field; all of SGP4's drag is B*. They are in every TLE because SGP,
   the model this one replaced, used them. The perturbation is kept in the table rather than
   dropped, because a contribution of exactly zero is the evidence.

**The residual layer is in.** `Osculator.Core/Residuals/` resolves the difference between two
states into the reference state's RIC/RSW frame. Nothing in the dimensions catches a sign or an
axis order — a cross product and its negation have identical dimensions — so all three conventions
are pinned by construction against a state whose answer is obvious by inspection. Writing it turned
up trap 13 below, which is the first Δ_model term this repository has measured rather than quoted.

Figures elsewhere in the spec are still **projections**. Do not quote those as results.

## What this application is

Osculator tracks satellites and orbital debris from NASA and NORAD data, and decomposes the gap
between its prediction and independent observation into three terms:

- **Δ_model** — SGP4 is a curve fit to the physics, not the physics. 5–20 km at LEO, 7 days out.
- **Δ_data** — the element set is quantized. 0.3–3 km over the same arc.
- **Δ_arith** — round-off in the propagator's own arithmetic. Measured, not assumed.

The third is the one nobody measures, because measuring it needs a reference computation whose own
arithmetic error is negligible. `ktsu.PreciseNumber` is that reference.

**The expected result is that `double`'s arithmetic error is negligible**, around nine orders of
magnitude below the data term. The application's job is to *prove* that, not to argue the opposite.
Any change that starts implying "more digits means better tracking" is going the wrong way — that
claim is false and the spec explains why at length.

## Build commands

```bash
dotnet restore
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName~TestMethodName"
dotnet build -c Release
```

## Planned project layout

From the spec. `Osculator.Core` is generic over the storage type and contains no UI and no I/O.

| Project | Responsibility |
|---|---|
| `Osculator.Core` | `Time/`, `Elements/`, `Propagation/`, `Frames/`, `Forces/`, `Residuals/` — all generic over `TStorage` |
| `Osculator.Data` | CelesTrak, Space-Track, CDDIS/ILRS SP3, JPL Horizons clients plus the disk cache |
| `Osculator.Math.Precise` | `PreciseStorageMath`: `IStorageMath<PreciseNumber>` at a chosen working precision |
| `Osculator.Core/Numerics` | `DecimalMath`: sqrt, sin, cos, atan2, exp, log and pow for `decimal`, which the base library has none of |
| `Osculator.Storage.{Double,Float,Decimal,Precise}` | One-file facades, each referencing one `ktsu.Semantics.Quantities.*` alias package |
| `Osculator.App` | `ktsu.ImGui.App` UI |
| `Osculator.Tests` | MSTest, including the Vallado SGP4 verification suite |
| `Osculator.Benchmarks` | BenchmarkDotNet, cost per propagation per storage type |

Run one verification case with
`dotnet test --filter "FullyQualifiedName~Sgp4Verification"`. The suite prints its worst position
and velocity error, but the runner only shows stdout for tests it renders a block for — i.e.
failing ones — so to see the figures on a pass, run the test executable directly with
`--show-stdout All`.

### Why the storage facades are separate projects

The `ktsu.Semantics.Quantities.{Double,Float,Decimal,Precise}` packages inject **global using
aliases, project-wide** — their own description says "use one storage-type alias package per
project". An application wanting four at once therefore cannot use them in its core. `Osculator.Core`
is written against the open generics (`Position3D<T>`, `Length<T>`, `Duration<T>`), and so is
`Osculator.App`, which displays all four side by side. The four facade projects are each one file
long and are where the alias packages are actually demonstrated.

## Domain traps

Thirteen things that are easy to get wrong here and expensive to debug.

1. **`V0 − V0` returns `T.Abs(a − b)`.** `ktsu.Semantics.Quantities` decided this deliberately and
   documents it: magnitude subtraction stays non-negative. A residual is signed by definition, so
   **every residual computation must use the signed V1/V3 forms** (`Velocity1D`, `Velocity3D`,
   `Displacement3D`) and never the V0 magnitude forms. `Speed - Speed` silently gives the absolute
   difference.
2. **SGP4 uses WGS-72, not WGS-84.** TLEs are generated with the WGS-72 constants
   (μ = 398600.8 km³/s², Rₑ = 6378.135 km, J₂ = 0.001082616). Substituting the "better" WGS-84 values
   makes results *worse*, because the model is a fit and the constants are part of the fit.
3. **SGP4 outputs TEME, not J2000.** True Equator Mean Equinox is a distinct frame. Treating SGP4
   output as ECI/J2000 is the most common bug in amateur trackers and costs 100 m to several km.
4. **One `double` cannot hold a Julian Date at useful resolution.** JD ≈ 2,461,000, so one ulp is
   ~48 µs. That is why the two-part Julian Date exists. In `PreciseNumber` it is exact.
5. **Residuals belong in RIC/RSW**, not XYZ. Orbital error is overwhelmingly along-track — essentially
   a timing error — and XYZ scrambles that across three axes rotating with the orbit.
6. **The OMM JSON carries more precision than the two-line text for some fields.** The JSON is
   generated from the originating values rather than by re-reading the text, so eccentricity gains a
   digit and the drag term gains three (five significant digits in the text, eight in the JSON).
   Mean motion and its first derivative are identical. So Δ_data depends on which representation was
   ingested, and mixing the two compares element sets of different precision. `TleParserTests`
   pins this with the same ISS element set committed in both forms.
7. **SGP4's velocity unit is not its position unit.** Position converts by the Earth's radius;
   velocity by `radius * xke / 60`. Dropping `xke` leaves position perfect and velocity wrong by a
   factor of 13.45 — which is precisely the defect the verification suite caught during M1, and the
   reason position and velocity are asserted separately.
8. **The deep-space model reads the epoch, not just the time since it.** The near-earth model only
   ever sees minutes since epoch; the deep-space model places the sun and the moon at the instant the
   elements were fitted. `ElementSet` therefore carries a two-part `JulianDate` alongside its
   `DateTime`, converted exactly from the fractional day of year, and the propagator reads that one.
   Going through `DateTime` instead costs up to 99 nanoseconds — it truncates to its tick, measured,
   not the millisecond it is sometimes said to round to — which is below what the model can notice
   only because the sidereal angle cancels out of the resonance terms. `JulianDate` records that
   reasoning; do not re-derive it from the class name.
9. **An arbitrary-precision type needs the propagator to throw precision away.** `PreciseNumber`'s
   multiplication is *exact*, so the product of two n-digit values has 2n digits of which n are
   meaningful, and the growth is linear in the length of the chain. Measured before
   `IStorageMath<T>.ToWorkingPrecision` existed: one `Initialize` took **18.8 seconds** and left
   `T5cof` holding **167,104 significant digits**; a single propagation did not finish in fifteen
   minutes. After it: 47 ms and 30 digits. Division was never the problem — a quotient is truncated
   on the way out. Anything added to the propagator that stores or accumulates a value needs to go
   through `ToWorkingPrecision`, and the fixed-width types get the identity.
10. **`decimal`'s precision is absolute, not relative, and SGP4 lives below the crossover.** It is a
   96-bit integer with a scale capped at 28 decimal places, so significant digits run out as values
   get smaller: 28 at unit magnitude, 19 at 1e-9, **16 at 1e-12 — the same as `double`** — and 8 at
   1e-20. SGP4's drag expansion is exactly there: `Cc1` is around 1e-12 for a typical object and
   `D2`, `D3`, `D4`, `T4cof` and `T5cof` are powers of it. So the coefficients deciding how an orbit
   decays are computed in *fewer* digits than `double` would give them. Measured in
   `StorageComparisonTests`; do not assume a type with more digits has more digits everywhere.
11. **The deep-space eccentricity polynomials branch three ways, not two.** `G520` splits again at
   0.715 inside the branch that already splits at 0.65, and the verification file has a case in each
   of the resulting ranges precisely because of it. Getting this wrong is not subtle once measured —
   it put 10 to 19 km on four Molniya cases — but it is invisible in any element set below 0.65.
12. **CelesTrak's `gp.php` sends no `Last-Modified`, `ETag` or `Cache-Control`** — measured against
   the live service, not assumed. So there is no conditional request to make and no server-stated
   freshness to honour: the entire refetch policy is ours, which is exactly why it is tested rather
   than documented. It also answers an unknown catalogue number with a **200 and the sentence
   `No GP data found`**, so a successful request is not on its own a successful lookup; left
   unchecked that reaches the JSON reader as a parse failure, which says nothing about what went
   wrong. `CelesTrakClient` detects it, raises `CelesTrakException`, and does **not** cache it —
   otherwise one typo would keep failing for the whole window.
13. **SGP4's reported velocity is not the exact time derivative of its reported position.** Measured
   at about **1.2e-3 km/s** on the first verification case: differencing two propagated positions
   gives a cross-track rate of 9.6e-4 km/s, and the cross-track axis is built from `r × v`, so the
   stated velocity has no cross-track component by construction. It is linear in the offset — the
   ratio held across 1, 0.5, 0.25 and 0.125 seconds — so it is a velocity discrepancy and not an
   acceleration. The cause is that the periodic corrections' own time derivatives are only partly
   carried into the model's velocity formulas. **It is around a metre per second of Δ_model, seven
   orders of magnitude above `double`'s arithmetic error**, so a residual built by differencing
   positions and one built from the stated velocity are different measurements. Which one is used
   has to be a decision rather than an accident. `RswResidualTests` pins it.

## Upstream dependencies

Speccing this application surfaced eleven gaps in the three libraries it consumes; ten were filed
with full designs. **Seven have since landed and are published**, which unblocked M3 — the precise
path is no longer waiting on anything.

Verified against `ktsu.PreciseNumber` 2.5.0 and `ktsu.Semantics.Quantities` 5.5.1:

| Was blocking | Now |
|---|---|
| [PreciseNumber#80](https://github.com/ktsu-dev/PreciseNumber/issues/80) roots | `Sqrt`, `Cbrt`, `RootN`, `Hypot` — `IRootFunctions` implemented |
| [PreciseNumber#81](https://github.com/ktsu-dev/PreciseNumber/issues/81) exp/log/pow | `Exp`, `Log`, `Log2`, `Log10`, `Pow` — the three interfaces implemented, no `double` fallback |
| [PreciseNumber#82](https://github.com/ktsu-dev/PreciseNumber/issues/82) trig | `Sin`, `Cos`, `Tan`, `Asin`, `Acos`, `Atan`, `SinCos`, and a bespoke `Atan2` |
| [PreciseNumber#79](https://github.com/ktsu-dev/PreciseNumber/issues/79) constants | π, τ and e now carry **150 significant digits, correctly rounded** (π ends `…940813`; the old truncation gave `…940812`) |
| [Semantics#239](https://github.com/ktsu-dev/Semantics/issues/239) | `StorageMath` is **public** |
| [Semantics#238](https://github.com/ktsu-dev/Semantics/issues/238) | `Position3D<T>` has `Magnitude()` and `DistanceTo()` returning the V0 quantity |
| [Semantics#240](https://github.com/ktsu-dev/Semantics/issues/240) | `GravitationalParameter<T>` exists |
| [Semantics#244](https://github.com/ktsu-dev/Semantics/issues/244) | alias-package composability — the `PrivateAssets="all"` workaround in the four facades can be revisited |

Consequences for this repository, none of them yet acted on:

- **`Osculator.Math.Precise` has lost its reason to exist.** It was a placeholder for the
  transcendentals PreciseNumber lacked. Delete it, or keep it only for anything genuinely bespoke.
- **`StorageProbe` reimplements a Newton root** that `StorageMath` now exposes publicly.
- **`IStorageMath<T>` is now a thin delegation for every storage type** that has transcendentals,
  including `PreciseNumber`. It is still the right seam — it keeps the propagator free of a
  `ITrigonometricFunctions<T>` constraint — and it now also carries `ToWorkingPrecision`, which is
  not a delegation at all. See the domain traps.

Still open upstream:

- **[Semantics#237](https://github.com/ktsu-dev/Semantics/issues/237)** — vector forms have no
  `From{Unit}` factories, so a `Position3D` is still built in raw metres.
- **[ImGuiApp#411](https://github.com/ktsu-dev/ImGuiApp/issues/411)** (virtualized table) and
  **[#413](https://github.com/ktsu-dev/ImGuiApp/issues/413)** (backend-agnostic 3D, four sub-issues)
  — no movement; the globe is still a CPU raster and the catalogue still needs `ImGuiListClipper`.

## Validation gates

Non-negotiable, in order. Gate 1 comes before anything else in the repository means anything.

1. **SGP4 against Vallado's official verification suite** (`SGP4-VER.TLE` + `tcppver.out`), which
   specifies expected positions to 10⁻⁸ km. **Passing**, over both the near-earth and the
   deep-space model.
2. The same suite in every storage type, tolerance scaled to the type. **Passing for all four.**
   `float` was never expected to meet 10⁻⁸ km — recording where it fails, and that it does so
   without saying so, is the result.
3. Frame transforms against IERS test vectors.
4. SP3 interpolation by held-out epochs.
5. `Δ_arith(PreciseNumber) ≡ 0` — the invariant proving the harness holds everything but the storage
   type fixed.
6. Benchmarks in CI, per storage type per propagator.

## Data source etiquette

Every client caches to disk and works offline from cache. This is enforced, not advisory:

- **CelesTrak** asks for caching and infrequent refetch in its usage guidelines.
- **Space-Track** limits are hard — under 30 requests/minute and 300/hour.
- **CDDIS** needs an Earthdata Login; credentials go to the OS credential store, **never** to a file
  in this repository.

## Code standards

Follows the sibling ktsu repositories:

- Tabs for indentation, file-scoped namespaces, explicit types (no `var`), no `this.` qualifier,
  always braces.
- File header: `// Copyright (c) 2026 ktsu-dev contributors` (the text comes from `COPYRIGHT.md`;
  ktsu.Sdk syncs `.editorconfig`'s `file_header_template` from it on every build).
- Explicit types in test bodies.

## Code quality

Do not add global suppressions for warnings. Use explicit suppression attributes with justifications
when needed, with preprocessor defines only as fallback. Make the smallest, most targeted
suppressions possible.

## CI/CD

`.github/workflows/dotnet.yml` restores, builds Release, tests, and checks the benchmark host
resolves, on Linux. It is deliberately **not** the full ktsu release pipeline the sibling
repositories run: there is no package to version, tag or publish, and that pipeline would fail
trying. Auto-generated files (`VERSION.md`, `CHANGELOG.md`, `LICENSE.md`) are produced by a release
pipeline and should never be edited manually.
