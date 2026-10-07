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
| `double` | 16 | 8.1e-9 km (published arcs), 6.8e-8 km (long arc) | median **4.7e-11 km**, worst 3.1e-8 km |
| `decimal` | 28 | 4.2e-7 km | median **4.8e-11 km**, worst 4.0e-7 km |
| `PreciseNumber` | 30 | **3.3e-8 km** | — |

**The deep-space epoch sidereal time is a shared input, not part of Δ_arith** (#65). It is evaluated
once in `double`, exactly as the published model evaluates it, and only the finished angle is
converted to the storage type, as the element set's own `double` fields are. Both other choices were
measured and both are wrong. Evaluating it in `T` quantized the epoch before the propagator ran —
consecutive floats near JD 2.46e6 are six hours apart, and the angle does not cancel out of the
resonance terms — which put up to 20.5 km on `float`'s half-day-resonant cases (0.09 km now) and was
70% of `double`'s median against the 30-digit reference (1.6e-10 km before, 4.7e-11 now). Evaluating
it from the two-part date, the more accurate number, moved `double` itself off the published vectors
by up to 3.6e-8 km on object 8195: the vectors carry the single-date rounding of that polynomial.
`float`'s 55.06 km headline is on a non-resonant case and did not move.

Four things to take from that table, all of which the tests assert:

1. **`double`'s arithmetic error is around nine orders of magnitude below the data term.** A median
   of 4.7e-11 km is a twentieth of a millimetre, against a measured **0.056 km** of element-set
   quantization. This is the repository's central claim and both sides of it are now numbers. It
   used to read "around ten orders" against "0.3 to 3 km of element-set quantization"; that was a
   projection, the quantization has since been measured at a twentieth of it, and the claim survives
   comfortably either way. See below.
2. **`float` fails silently.** 55 km out and not one row reported an error: the model's error codes
   for an eccentricity or mean motion out of range are never tripped. It returns a confident wrong
   answer, which is the expensive failure mode.
3. **Thirty digits agrees with the published vectors *four times worse* than `double` does.** Not a
   defect — the precise run is the more correct one. The published vectors were computed in
   `double`, so agreeing with them closely is a property of making the same rounding errors. If
   precision were the limiting factor, a 30-digit run would agree to 1e-30 km.
4. **`decimal`'s twelve extra digits buy nothing, and cost a factor of 12.6.** Level with `double`
   at the median, an order of magnitude worse at the extreme, and nowhere near the twelve orders of
   magnitude the digit counts suggest. It read "a factor of 2.8 better at the median" until the
   epoch sidereal time was shared; that advantage was `double`'s own single-date evaluation of one
   angle, which `decimal` did not share. See domain trap 10 for why.

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

**The frame layer reaches the ITRF now, and gate 3's sign conventions are checked rather than
recalled.** `Osculator.Data/Iers/` reads the IERS `finals2000A.all.csv` series — open, no account,
unlike the laser-ranging archives — and supplies the two things the transform could not previously
be given: UT1 − UTC and the pole coordinates. `EarthFixedFrame` now takes an `EarthOrientation`
rather than a bare `double`, which closes a hole the previous version shipped: it *required* a
UT1 − UTC argument that nothing in the repository could produce.

Three things worth knowing before touching any of it:

- **The polar-motion sign convention is pinned by the published definition, not by this code's own
  arithmetic.** IERS TN36 eq 5.3 gives `r_TIRS = W·r_ITRS` with `W = R3(−s′)·R2(xₚ)·R1(yₚ)`, so
  the way this code goes is `r_ITRS = R1(−yₚ)·R2(−xₚ)·r_TIRS`; `s′` is under a microarcsecond and
  is dropped. The test applies it to the PEF z axis and asserts the result is ITRS `(xₚ, −yₚ, 1)`,
  which *is* what the pole coordinates mean. Flipping either sign fails it, and each failure names
  the convention it broke.
- **A rotation's inverse is its transpose, and that is the whole of the inversion.** `ItrfToPef`
  was first written negating the angles *as well*, which flips the sines twice and undoes it. It
  read 10.9 m of round-trip error, then 18.8 m after a partial fix; the transpose was simply
  written out wrong in its third row. Neither would have been visible without the round-trip test.
- **Rows with no values are dropped at parse time.** The file's last ~50 rows carry a date and
  nothing else, because the IERS publishes the date grid further ahead than it forecasts for it. A
  parser that reads an empty field as zero produces a legal-looking UT1 − UTC of 0, which is
  exactly the 415 m error the required argument exists to prevent. An instant past the last real
  row is refused by name rather than extrapolated.

Measured against the real 20,040-line file: 19,990 usable rows, covering MJD 41684–61673.
2000-01-01 reads UT1 − UTC = 0.3554779 s, which is the known J2000 value — an independent check
that the column mapping is right. Today's values come back flagged `IsPrediction`, correctly: the
IERS finalises about a week in arrears, and a forecast's stated uncertainty on UT1 − UTC runs from
four times the final one to a thousand times it a year out.

**Those uncertainties are carried now, and measured in metres as part of Δ_data.**
`EarthOrientation` holds the file's sigma columns for the pole and UT1 − UTC; between rows the
larger neighbour's sigma is reported, and a row with values but no sigma reads NaN rather than zero.
`Residuals/EarthOrientationTerm` rotates a TEME state to the ITRF with each parameter at ±σ in turn
and combines them in quadrature, refusing an unknown sigma. At LEO a final row is worth about a
centimetre, led by UT1 − UTC; a forecast a year out is metres. Without this, once residuals are
taken in the ITRF, that share would be counted as Δ_model (spec §11).

**The frame layer exists now, and it is honest about where it stops.** `Osculator.Core/Frames/`
rotates TEME into the Earth-fixed frame and converts that to geodetic latitude, longitude and
altitude. `PefState<T>` remains the named intermediate — TEME → PEF is the sidereal rotation, PEF →
ITRF is polar motion — so a caller that only wants a map can stop at PEF and one that wants a
residual cannot reach `Geodetic` without going all the way, because that overload takes
`ItrfState<T>`. Polar motion is about 12 m at the surface for present-day pole coordinates, not the
9 m first estimated here.

Gate 3 now has a published reference (see the gate list below). The physics checks stay, and
for this transform they are stronger than they sound. A geostationary satellite has to stay over
one longitude, and essentially nothing can be wrong in the rotation sense, the sidereal rate or the
rotating-frame velocity while that still holds. Mutation-checked, not merely watched to pass:

| mutation | what the test read |
|---|---|
| rotation sense flipped | the satellite wandered **179.5°** of longitude in a day, against a 1° bound |
| the ω × r term dropped | rotating-frame speed **3.075 km/s**, exactly the inertial speed, against a 0.01 bound |

Three numbers from that layer worth keeping in proportion, all measured by its tests:

1. **Geodetic latitude is not geocentric latitude — 0.192° at 45°, which is 21 km on the ground.**
   The largest error available anywhere in this layer, and the one most often shipped, because the
   geocentric form is what a one-line `asin(z / |r|)` gives.
2. **UT1 − UTC is worth up to 415 m at the equator**, forty times the polar motion the layer
   deliberately omits. It is a required argument on the transform rather than an optional
   refinement for exactly that reason: passing zero is a choice, not a default.
3. **The ellipsoid is WGS-84 while the propagator is WGS-72, and both are right.** The WGS-72
   constants are part of SGP4's curve fit; the ellipsoid is the surface a latitude is measured
   against and has nothing to do with the fit. Using WGS-72's would shift altitudes by ~2 m and
   report against a surface nobody else uses.

**The residual layer is in.** `Osculator.Core/Residuals/` resolves the difference between two
states into the reference state's RIC/RSW frame. Nothing in the dimensions catches a sign or an
axis order — a cross product and its negation have identical dimensions — so all three conventions
are pinned by construction against a state whose answer is obvious by inspection. Writing it turned
up trap 13 below, which is the first Δ_model term this repository has measured rather than quoted.

**Residual statistics are accumulated in the storage type, with no compensation.**
`ResidualStatistics<T>` (RMS overall and per RIC axis, nearest-rank percentiles) and
`ErrorGrowthFit<T>` (km/day, intercept fitted) are one generic each, so spec §1 demo 5 is the same
class run twice. Over a million metre-scale residuals with a closed-form answer of exactly
333,833.5 km², `PreciseNumber` returns every digit and naive `double` summation returns
333,833.49999995285: a relative error of **1.4e-13, about three digits lost**. The sums are
deliberately not passed through `ToWorkingPrecision` — exact addition grows only by the exponent span
and the count, not per term — and `ThePreciseSumsAreNotReducedToTheWorkingPrecision` fails if they are.

**Conjunction screening is in the core; its panel is not.** `Osculator.Core/Conjunction/` rejects
pairs whose perigee–apogee shells cannot meet, scans the rest for sign changes of `Δr · Δv`, and
bisects each to a nanominute, with the time, both states and the miss distance all in `T`. On a
constructed 13.24 m crossing at 2.09 km/s, against 30 digits: `float` is off by **0.82 m**, `double`
by **1.6e-12 km**, `decimal` by 3e-26 km. The subtraction itself is exact; what it does is leave the
states' own absolute rounding as the whole answer, which is the spec's cancellation case.
`ClosestApproach<T>` keeps the states so storage types can be compared in `T`; never compare two
`PropagatedState`s for this.

**Gate 5 passes, and it is the one the headline number rests on.** `ArithmeticErrorGateTests`
checks the harness rather than the result. Two claims:

| claim | measured |
|---|---|
| the 30-digit reference is converged | 30 vs 40 digits differ by **4.765e-21 km** worst over 666 rows |
| the harness reproduces itself exactly | two 30-digit runs agree to **every digit**, not nearly |

The first is what makes every other number in the comparison mean anything. A thirty-digit run is
not exact arithmetic, it is thirty-digit arithmetic; if its own error were anywhere near the
4.7e-11 km it is used to measure, the central claim would be measuring the reference rather than
`double`. It is **ten orders below** that, so it is not.

**The gate has a guard against passing vacuously, and that guard was added because the first
version would have.** A harness whose precision argument never reached the arithmetic — so that
the probe was secretly the reference — reports a difference of exactly zero, which sailed through
an upper bound of 1e-15. The test now requires the difference to be **non-zero** first. Verified by
clamping `PreciseStorageMath.SignificantDigits` to 30: the sweep reads 0.000e+000 and the guard
fires.

**Neither half of the gate can see the reference computing in `double`**, because a conversion
through `double` is deterministic and both runs share it — routing four functions through it made
the convergence sweep read *better* (#25). `PreciseStorageMathTests` closes that: every function is
checked against digits computed by mpmath, at 30 and at 100 digits, and routing any one of `Sqrt`,
`Sin`, `Cos`, `Atan2` or `Pow` through `double` fails it. Mutation-checked, one function at a time.
`Pow` computes its fractional path itself, because PreciseNumber's two-argument `Pow` stops at fifty
digits for short operands (#67), and `IStorageMath<T>.Divide` is the seam for literal-over-literal
quotients, which the `/` operator rounds to fifty digits whatever precision was asked for (#42).

It costs about twelve seconds, because it sweeps the whole verification set twice in
`PreciseNumber`. That is most of the test suite's runtime and it is the right trade for the one
check that validates the repository's central claim.

**The cost of precision is measured too, and M3's performance gate has a budget.**
`Osculator.Benchmarks/Sgp4Benchmarks.cs` times one initialization plus a one-day propagation in each
storage type, on a near-earth case (06251) and a half-day-resonant Molniya (21897) from the
verification set. BenchmarkDotNet, short job, one 2.8 GHz Xeon core:

| | near-earth | deep-space | vs `double` | allocated |
|---|---|---|---|---|
| `float` | 0.66 µs | 1.8 µs | **0.6x** | 0.5x |
| `double` | 1.1 µs | 3.0 µs | 1x | 672 B / 920 B |
| `decimal` | 91 µs | 344 µs | **80–114x** | 2x |
| `PreciseNumber` (30 digits) | **9.2 ms** | **21 ms** | **7,100–8,100x** | 1.2 MB / 3.7 MB |

Read it beside the Δ_arith table above. Thirty digits costs four orders of magnitude to move the
answer by 1.6e-10 km, which is the right price for a reference computed once for the object a user
selected and the wrong one for anything run over the catalogue. `decimal` costs two orders of
magnitude and, per trap 10, does not buy the digits it advertises.

**The budget is 100 ms for one `PreciseNumber` initialization plus propagation** — the point past
which an on-demand reference stops feeling immediate. `PropagationBudget` (run as
`dotnet run --project Osculator.Benchmarks -c Release -- --budget`) times it on both orbits, fastest
of five after a warm-up, and exits non-zero above ten times the budget; CI runs it on every push. It
is a stopwatch rather than BenchmarkDotNet on purpose: the regression it exists for is trap 9, which
is not a few percent but a propagation that never finishes, and a factor of ten separates that from
runner noise in both directions. Every run is bounded by the ceiling, warm-up included, so that
regression fails the step in seconds instead of hanging it. **Mutation-checked**: with
`ToWorkingPrecision` made the identity, both orbits report "did not finish within 1000 ms" and the
step fails in about three seconds.

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
| `Osculator.Numerics.Precise` | `PreciseStorageMath`: `IStorageMath<PreciseNumber>` at a chosen working precision |
| `Osculator.Core/Numerics` | `DecimalMath`: sqrt, sin, cos, atan2, exp, log and pow for `decimal`, which the base library has none of |
| `Osculator.Storage.{Double,Float,Decimal,Precise}` | One-file facades, each referencing one `ktsu.Semantics.Quantities.*` alias package. Each is an `IPropagatorHost`: `Propagate(ElementSet, minutesSinceEpoch)` runs SGP4 in its storage type and returns a non-generic `PropagatedState` (TEME km and km/s as `double`, plus wall-clock). That is the seam every panel calls; never difference two of those states to measure Δ_arith, since both are already rounded to `double` |
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

### The application shell

`Osculator.App/Shell/` hosts every panel. `AppShell.BuildConfig()` is what `Program` starts and what
`AppShellTests` drives headlessly, so the tests exercise the real configuration: docking on,
`ImGuiWidgets.DrawDeferredDocked()` as the only pump (never `DrawDeferred()` as well, which draws
every dialog twice), and frame rates throttled when unfocused (5), idle for 30 s (10) or hidden (2).

- **Adding a panel is one line.** Derive from `Panel`, override `Title` and `Draw`, and add
  `registry.Register<YourPanel>();` to `Shell/Panels.cs`. The title is the docked window's identity,
  so a duplicate is refused at registration.
- **Long work goes through `Panel.Work`**, a `BackgroundWork` keyed by job: `Run(key, work,
  onResult)` runs `work` on a worker and delivers `onResult` on the UI thread through
  `ImGuiApp.Invoker`. Starting a job under a key that is already running cancels the old one, and
  its result is dropped even if it finishes anyway, which is what a selection change wants.
- **`DockedWindow` closes a panel whenever `ImGui.Begin` returns false**, and ImGui returns false
  for a collapsed window and for a docked tab behind another, not only for one the user closed.
  Tabbing two panels together used to lose the hidden one for good. `Panel` therefore keeps its own
  `IsOpen`, the registry re-shows every open panel each frame, and a panel counts as closed only
  when its window was begun, is not skipping items, and still did not draw, which only the close
  button produces. Remove the guard once [ImGuiApp#600](https://github.com/ktsu-dev/ImGuiApp/issues/600) is fixed.

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
4. **One `double` cannot hold a Julian Date at useful resolution.** JD ≈ 2,461,000 lies between 2²¹
   and 2²², so one ulp is 2⁻³¹ days: **40.2 µs**, measured. The 48 µs usually quoted (this file
   said it too) is machine epsilon times the date, a bound that overstates the spacing by JD / 2²¹.
   That is why the two-part Julian Date exists. In `PreciseNumber` it is exact.
   `JulianDateStaircase` sweeps it and `JulianDateStaircaseTests` pins the tread and the 31 cm
   risers it puts on along-track position at ISS speed.
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
| [Semantics#244](https://github.com/ktsu-dev/Semantics/issues/244) | alias-package composability — the `PrivateAssets="all"` guard is gone from the four facades |

Consequences for this repository, all now acted on:

- **`Osculator.Math.Precise` lost its reason to exist and became `Osculator.Numerics.Precise`.**
  It was a placeholder for the transcendentals PreciseNumber lacked; `PreciseMath` is deleted and
  the project now holds `PreciseStorageMath` alone, named to match `Osculator.Core/Numerics` where
  `DecimalMath` lives.
- **The square root `DecimalMath` was writing itself now comes from `StorageMath`.** Of the seven
  functions that module supplies, six had to be written because nothing computes them for a
  `decimal`; the root was the seventh and Semantics already had it. Measured identical in every bit
  across seven magnitudes from 1e-10 to 1e20 before the swap. The negative guard stays, because
  `StorageMath.Sqrt` answers a negative radicand with an `OverflowException`, which is not what
  went wrong.
- **The `PrivateAssets="all"` guard is removed from the four facades**, verified rather than
  assumed: with it gone, `Osculator.Tests` references all four and still sees no alias at all.
- **`IStorageMath<T>` is a thin delegation for every storage type** that has transcendentals. It is
  still the right seam — it keeps the propagator free of an `ITrigonometricFunctions<T>` constraint
  — and it now also carries `ToWorkingPrecision`, which is not a delegation at all. See the domain
  traps.

An earlier version of this section claimed **`StorageProbe` reimplements a Newton root**. It does
not, and never did: it contains a halving search and a power of ten, and no root of any kind. The
line is recorded here as removed rather than silently deleted, because acting on it would have been
work in the wrong file.

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
3. Frame transforms against a published reference vector. **Passing.** `FrameCorrectnessTests`
   runs the worked example in Appendix C of Vallado et al. 2006 (AIAA 2006-6753 Rev 2) through
   TEME → PEF and PEF → ITRF separately, so a failure says whether rotation or polar motion broke.
   Positions agree to 5.7e-8 km against the paper's 1e-7 km last digit; velocity to 1.0e-8 km/s,
   which is the paper's length-of-day correction to ω, not carried here. **The paper's own instant
   is 14.7 µs off the one it names**: its program forms UT1 as one `double`, which is the
   Julian-date staircase of trap 4. From the exact two-part instant the PEF vector lands 8.5 mm
   away, and the test asserts that whole gap is that rotation. So `SiderealAngle` is matched to
   the paper by feeding it the paper's rounded instant, not by rounding its own: it evaluates
   GMST from the two parts without ever summing them, rising at every microsecond and within
   8.2e-14 rad of a 50-digit evaluation, where the single-`double` sum was off by up to 1.4e-9 rad.
4. SP3 interpolation by held-out epochs.
5. `Δ_arith(PreciseNumber) ≡ 0` — the invariant proving the harness holds everything but the storage
   type fixed.
6. Benchmarks in CI, per storage type per propagator. **Met for SGP4** as a budget gate on the
   `PreciseNumber` path rather than a timed comparison, because a shared runner measures itself; the
   per-type figures come from `Sgp4Benchmarks` run locally.

## Data source etiquette

Every client caches to disk and works offline from cache. This is enforced, not advisory:

- **CelesTrak** asks for caching and infrequent refetch in its usage guidelines.
- **Space-Track** limits are hard — under 30 requests/minute and 300/hour. `SpaceTrackRateLimiter`
  enforces 29 and 299 over sliding windows, counts the login, and refuses rather than queues; the
  ceilings can be lowered but not raised. `SpaceTrackClient` also refuses a cache shorter than an
  hour, because Space-Track asks that `gp` be queried no more often than that. The password is read
  from the OS credential store by `OsCredentialStore` (Credential Manager, Keychain, Secret Service),
  whose remarks give the one command per platform that stores it. The client's tests run against a
  fixture in Space-Track's response shape that was built, not captured: no account is in the repo.
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
