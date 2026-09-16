# Osculator — design specification

**An arithmetic-error observatory for orbital prediction.**

> **os·cu·lat·ing orbit** — the Keplerian orbit a body would follow if every perturbation
> stopped. From Latin *osculari*, to kiss: it touches the true trajectory at one instant,
> matching position and velocity exactly, and diverges immediately after. Agreement at
> epoch, divergence after — which is the whole subject of this document.

A desktop application that ingests NASA and NORAD orbital data, propagates satellites and
debris forward, compares the prediction against independent observations, and — this is the
part nobody else does — **decomposes the resulting error into the three things that caused
it**: the model, the data, and the arithmetic.

Status: proposed. Nothing is built yet.

---

## 1. Why this application exists

It is easy to write a satellite tracker. Dozens exist. What none of them tell you is *why*
their prediction is wrong.

Three independent error sources compose into the gap between a predicted position and a
measured one:

| | Source | Typical size at LEO, 7 days out |
|---|---|---|
| **Δ_model** | SGP4 is an analytical mean-element theory fitted to a truncated force model. It is not the physics; it is a curve fit to the physics. | **5–20 km** |
| **Δ_data** | The element set is quantized. A TLE carries mean motion to 8 decimals, eccentricity to 7, angles to 4. Those steps are not infinitely fine. | **0.3–3 km** |
| **Δ_arith** | Round-off in the propagator's own floating-point arithmetic. | **?** |

Everyone knows Δ_model. Some people know Δ_data. Almost nobody has ever *measured* Δ_arith,
because measuring it requires a reference computation whose own arithmetic error is
negligible — and if your only numeric type is `double`, you do not have one.

`ktsu.PreciseNumber` is that reference. It stores `significand × 10^exponent` over a
`BigInteger`, so at 50 significant digits its contribution to a kilometre-scale answer is
around 10⁻⁴⁴ m. Against that, the arithmetic error of every other storage type becomes
directly measurable.

### The honest claim

**The expected result is that Δ_arith(`double`) is negligible — roughly nine orders of
magnitude below Δ_data — and the application's job is to prove that rather than assert it.**

This matters. A showcase that concluded "use more digits and your satellite tracking gets
better" would be false, and anyone in the field would know it was false within a minute.
The interesting, defensible, demonstrable result is the *decomposition*: here is your
14 km of error, 12.1 km of it is the model, 1.9 km is the element set's last decimal place,
and 0.0000004 m is the arithmetic. Now stop blaming the arithmetic.

### Where the arithmetic *does* break — the cases worth building the app for

Four specific, reproducible places where Δ_arith stops being negligible. These are the
headline demonstrations.

**1. The Julian Date resolution wall.** The standard time variable in orbital mechanics is
the Julian Date, currently ≈ 2,461,000. A `double` holds ~15.95 significant decimal digits,
so one ulp of JD is about `2.46e6 × 2.22e-16 ≈ 5.5e-10` days ≈ **48 microseconds**. At LEO
orbital speed (7.7 km/s) that is 0.37 mm of along-track position — irrelevant for SGP4, and
*not* irrelevant when comparing against centimetre-accurate laser-ranging orbits. The entire
astrodynamics community works around this with the two-part Julian Date (integer day plus
fraction, carried as two `double`s), a hack that exists solely because one `double` cannot
hold the number.

In `PreciseNumber` the Julian Date is simply exact and the hack is unnecessary. The app
demonstrates the wall directly: sweep requested time continuously and plot propagated
along-track position. In `double`-JD mode the curve is a **staircase** with 48 µs treads. In
`PreciseNumber` it is a line. This is the single clearest, most physically real
demonstration in the application, and it is a genuine working practice, not a contrivance.

**2. `float` cannot represent the residual at all.** A LEO position magnitude is ~7,000,000 m.
In `float` (24-bit mantissa) one ulp at that magnitude is **0.5 m**. Comparing a prediction
against a 2 cm SP3 laser-ranging orbit in `float` is not inaccurate — it is *impossible*.
The residual quantizes to half-metre steps before you have computed anything. The residual
panel in FLOAT mode shows this as visible banding, and no amount of better modelling fixes it.

**3. Kepler's equation at high eccentricity.** Solving `E − e·sin E = M` by Newton iteration
near perigee of a Molniya (e ≈ 0.74) or GTO (e ≈ 0.73) orbit is the textbook
catastrophic-cancellation case; universal-variable formulations exist because of it. The app
runs the naive and the universal-variable solvers side by side across all four storage types
and shows where each loses digits.

**4. Accumulated round-off in long-arc numerical integration.** A 30-day Cowell integration at
1-second steps is 2.6 M steps. Round-off random-walks as roughly `√N · ε · r`, so in `double`
that is `1612 × 2.2e-16 × 7e6 m ≈ 2.5 mm` — small, real, measurable, and growing as `√t`.
In `float` the same arc accumulates ~1.3 km of pure arithmetic error, which is the same order
as the model error and therefore genuinely confusing if you do not know to look for it.

**5. Exact residual statistics.** RMS over 10⁶ squared metre-scale residuals loses digits to
naive `double` summation — Kahan summation exists for this reason. Accumulating in
`PreciseNumber` is exact, needs no compensation algorithm, and gives the other three storage
types something unimpeachable to be checked against.

---

## 2. What it showcases, per library

| Library | What the app proves about it |
|---|---|
| **ktsu.PreciseNumber** | That there is a real problem — not a synthetic benchmark — for which arbitrary-precision decimal arithmetic is the tool: being the reference against which every other storage type's error is measured, and dissolving the two-part Julian Date hack. |
| **ktsu.Semantics.Quantities** | That the same propagator source compiles and runs over four storage types unchanged, because every quantity is generic over `T : struct, INumber<T>`. That dimensional typing catches the errors that actually happen in this domain: km-vs-m, degrees-vs-radians, TEME-vs-J2000. And that the four storage-type alias packages are real, shipped, and interchangeable. |
| **ktsu.ImGui.App** | That the suite carries a dense, data-heavy, real-time technical application: a 30,000-row virtualized catalogue, a CPU-rasterized globe uploaded per frame, ImPlot time series, `PropertyGrid` element inspection, docking, and a PID-limited frame budget while background threads propagate. |

---

## 3. Repository and package layout

A new standalone repository, **`ktsu-dev/Osculator`**, consuming the three libraries as
published NuGet packages — not as project references. That is deliberate: it keeps a
network-fetching, 30k-object application out of three library repositories and their CI
matrices, and it proves the packages work *as published*.

Baseline versions: `ktsu.PreciseNumber` 2.0.4, `ktsu.Semantics.*` 5.3.1,
`ktsu.ImGui.*` 3.37.0.

```
Osculator/
├─ Osculator.Core/            net8.0;net9.0;net10.0 — generic over TStorage, no UI, no I/O
│   ├─ Time/                   PreciseInstant, time scales (UTC/UT1/TAI/TT), JD, TLE epochs
│   ├─ Elements/               OMM & TLE parse/emit, field quantization model
│   ├─ Propagation/            ISgp4<T>, Keplerian<T>, Cowell<T>, Sp3Interpolator<T>
│   ├─ Frames/                 TEME ↔ ITRF ↔ GCRF, precession/nutation, EOP
│   ├─ Forces/                 EGM96 harmonics, third-body, drag, SRP
│   └─ Residuals/              RIC/RSW decomposition, growth fits, exact statistics
├─ Osculator.Data/            CelesTrak, Space-Track, CDDIS/ILRS, JPL Horizons + disk cache
├─ Osculator.Math.Precise/    PreciseMath: sin, cos, atan2, sqrt, exp, log, π for PreciseNumber
├─ Osculator.Storage.Double/  one-line facade; references ktsu.Semantics.Quantities.Double
├─ Osculator.Storage.Float/   …Float
├─ Osculator.Storage.Decimal/ …Decimal
├─ Osculator.Storage.Precise/ …Precise  (+ ktsu.PreciseNumber, + Osculator.Math.Precise)
├─ Osculator.App/             net10.0 — ktsu.ImGui.App, ktsu.ImGui.Widgets, Hexa.NET.ImPlot
├─ Osculator.Tests/           MSTest; includes the Vallado SGP4 verification suite
└─ Osculator.Benchmarks/      BenchmarkDotNet: cost per propagation, per storage type
```

### A design tension worth stating plainly

The `ktsu.Semantics.Quantities.{Double,Float,Decimal,Precise}` packages inject
**global using aliases**, project-wide, binding `Position3D` to `Position3D<double>` (and so
on). Their own package description says it: *"Use one storage-type alias package per project."*

An application that wants four storage types **at once** therefore cannot use the alias
packages in its core. `Osculator.Core` must be written against the open generics
(`Position3D<T>`, `Length<T>`, `Duration<T>`) with `where T : struct, INumber<T>`, and so
must `Osculator.App`, which has to display all four side by side.

That is not a defect, it is the packages working as designed — and it produces exactly the
right showcase shape. The four `Osculator.Storage.*` projects are each one file long, each
reference one alias package, and each close the generic:

```csharp
// Osculator.Storage.Precise — the whole project
public sealed class PreciseHost : IPropagatorHost
{
    public string StorageName => "PreciseNumber";

    // `Position3D` here means Position3D<PreciseNumber>, supplied by the alias package's
    // global usings. Nothing in this file names the storage type.
    public StateVector Propagate(ElementSet elements, Instant at)
    {
        Position3D r = Sgp4<PreciseNumber>.Propagate(elements, at, PreciseMath.Instance);
        return StateVector.From(r);
    }
}
```

Four files, four storage types, one propagator. That is the demonstration, and it is more
convincing than the alias packages being used everywhere would be.

---

## 4. Data sources

All four, sequenced by how much they cost to integrate. Every client caches to disk, honours
the provider's rate guidance, and is usable offline from cache.

### 4.1 CelesTrak — the working catalogue *(no auth, M2)*

`https://celestrak.org/NORAD/elements/gp.php?GROUP=active&FORMAT=json` returns OMM-format
JSON. Verified live:

```json
[{"OBJECT_NAME":"ISS (ZARYA)","OBJECT_ID":"1998-067A",
  "EPOCH":"2026-09-15T08:51:14.158368","MEAN_MOTION":15.49122235,
  "ECCENTRICITY":0.00049264,"INCLINATION":51.6311,"RA_OF_ASC_NODE":213.7631,
  "ARG_OF_PERICENTER":142.7188,"MEAN_ANOMALY":217.4143,"BSTAR":0.00011249608,
  "MEAN_MOTION_DOT":5.779e-5,"MEAN_MOTION_DDOT":0,"NORAD_CAT_ID":25544}]
```

Note that the JSON does not *add* precision over the fixed-width TLE — `MEAN_MOTION` still
carries 8 decimals and `ECCENTRICITY` 7. The quantization is identical; the JSON merely stops
hiding it. The Δ_data analysis reads these fields directly.

`https://celestrak.org/pub/satcat.csv` supplies object metadata (name, type, launch,
RCS size, decay date). CelesTrak's usage guidelines ask for caching and no more than one
fetch per dataset per few hours — the client enforces this, it is not advisory.

### 4.2 Space-Track.org — the authoritative catalogue *(free account, M7)*

`POST https://www.space-track.org/ajaxauth/login` then
`/basicspacedata/query/class/gp/NORAD_CAT_ID/25544/orderby/EPOCH desc/format/json`.
Gives the full 18th Space Defense Squadron catalogue including objects CelesTrak does not
redistribute, plus historical element sets (essential — the TLE-vs-later-TLE loop needs an
element-set *history*, not just current state) and conjunction data messages. Rate limits are
hard: under 30 requests/minute and 300/hour. Credentials go to the OS credential store, never
to a config file in the repo.

### 4.3 NASA CDDIS / ILRS — centimetre truth *(Earthdata Login, M4)*

`https://cddis.nasa.gov/archive/slr/products/orbits/` — NASA Goddard's Crustal Dynamics Data
Information System, hosting ILRS satellite-laser-ranging precise orbits in SP3-c format for
the geodetic satellites: LAGEOS-1, LAGEOS-2, Etalon-1/2, Starlette, Stella, Ajisai, LARES.
These are **2–3 cm accurate** — five to six orders of magnitude better than SGP4.

This is the hero comparison. SGP4 on LAGEOS-1 against SLR truth is *kilometres* wrong, and
the truth is good to centimetres, so the residual is essentially pure model error with
nothing else in the way. It is also the case where `float`'s 0.5 m ulp makes the comparison
literally unrepresentable.

Requires an Earthdata Login (the archive URL returns a 302 to the login flow, as verified).
Bearer token or `.netrc`; SP3-c parsing is straightforward fixed-column text. Position
interpolation uses the standard 10th-order Lagrange through the tabulated points.

### 4.4 JPL Horizons — third bodies and spacecraft *(no auth, M7)*

`https://ssd.jpl.nasa.gov/api/horizons.api?format=json&COMMAND='301'&EPHEM_TYPE=VECTORS&CENTER='500@399'&…`
Verified live. Two uses: Sun and Moon state vectors for the third-body terms of the numerical
force model (better than the usual analytic low-precision series), and NASA-published state
vectors for a handful of spacecraft as an additional truth source. Less useful for LEO debris,
which Horizons does not carry.

---

## 5. Propagation

All propagators are generic: `Sgp4<T> where T : struct, INumber<T>`, taking an
`IStorageMath<T>` for the transcendental functions the storage type does not supply itself.

### 5.1 SGP4/SDP4

The standard model, and the one the element sets are fitted to. Two rules that are easy to get
wrong and that the app must get right:

- **WGS-72, not WGS-84.** TLEs are generated with the WGS-72 constants
  (μ = 398600.8 km³/s², Rₑ = 6378.135 km, J2 = 0.001082616). Substituting the "better" WGS-84
  values makes results *worse*, because the model is a fit and the constants are part of the
  fit. A nice illustration in its own right that more accurate inputs are not always better
  inputs.
- **SGP4 outputs TEME, not J2000.** True Equator Mean Equinox is a distinct frame. Treating
  SGP4 output as ECI/J2000 is the single most common bug in amateur trackers and costs
  100 m to several km depending on epoch. The frame layer is not optional.

Constants are held in a `Wgs72<T>` type following the pattern `Semantics.Quantities` already
uses for conversion factors: a `double` constant plus a `Values<T>` holder whose properties
are filled once per closed generic by `StorageLiteral.Parse<T>`, so a `PreciseNumber` build
parses the literal exactly rather than converting a `double`.

### 5.2 Two-body Keplerian

Universal-variable (Stumpff function) formulation, so it does not degrade at high
eccentricity. Used as the analytic baseline and as the vehicle for demonstration 3 above,
where it runs alongside a deliberately naive Newton solver so the two can be compared.

### 5.3 Cowell numerical integration

Dormand–Prince RK8(7) with adaptive step, over a force model of: EGM96 spherical harmonics to
configurable degree/order, third-body Sun and Moon (from Horizons), exponential or NRLMSISE-00
atmospheric drag, and cannonball solar radiation pressure. This is the propagator that can be
run in any storage type over a long arc, which makes it the vehicle for demonstration 4.

### 5.4 SP3 interpolation

10th-order Lagrange through tabulated SP3 positions, the IGS/ILRS standard. Validated by
holding out epochs and interpolating them back.

---

## 6. Measuring divergence

### The frame

Residuals are reported in **RIC/RSW** — radial, in-track, cross-track — not in XYZ. Orbital
prediction error is overwhelmingly *along-track* (a timing error, essentially), and RIC makes
that legible at a glance where XYZ scrambles it across three axes that rotate with the orbit.

### The metrics

- Per-epoch RIC residual vector and total magnitude
- Error growth rate: a fit of |residual| against Δt, reported in km/day
- RMS and percentiles over an arc, **accumulated in `PreciseNumber`** so the statistic itself
  carries no round-off and the other storage types have something exact to be checked against

### The decomposition — the core of the application

For a chosen object and prediction horizon Δt:

| Term | How it is computed |
|---|---|
| **Δ_model** | `PreciseNumber` SGP4 from element set A, versus truth at epoch A+Δt. Truth is a later element set, an SP3 orbit, or a Horizons vector. |
| **Δ_data** | Monte Carlo: perturb each OMM field independently by ±½ its quantization step, re-propagate in `PreciseNumber`, take the spread of the ensemble. This isolates what the element set's *last decimal place* is worth. |
| **Δ_arith(T)** | `PreciseNumber` SGP4 versus `T` SGP4, **identical inputs, identical algorithm**. Everything but the storage type is held fixed, so the difference is arithmetic and nothing else. |

`Δ_arith(PreciseNumber) ≡ 0` is the trivial invariant that validates the harness.

### A trap this domain walks straight into

`Semantics.Quantities` decided (deliberately, and it is documented) that **`V0 − V0` returns
`T.Abs(a − b)`** — magnitude subtraction stays non-negative. So `Speed − Speed` silently
yields the *absolute* difference, and `Length − Length` likewise.

A residual is signed by definition. Every residual computation in this application must
therefore use the V1/V3 signed forms (`Velocity1D`, `Velocity3D`, `Displacement3D`), never the
V0 magnitude forms. This is correct library behaviour that will nonetheless bite here
specifically, and it belongs in `Osculator.Core`'s own contributing notes.

---

## 7. User interface

`ktsu.ImGui.App` with `EnableDocking = true`, panels as `DockedWindow`s under
`ImGuiWidgets.DrawDeferredDocked()` — and *not* also `DrawDeferred()`, which would draw every
dialog twice.

| Panel | Built from |
|---|---|
| **Catalogue** | ~30,000 objects. `ImGuiWidgets.SearchBoxRanked` over names and IDs, filters by orbit class and object type. Needs virtualization — see gap 12. |
| **Globe** | CPU-rasterized into an RGBA buffer and uploaded with `ImGuiApp.CreateTexture`/`UpdateTexture` each frame; `ImGuiWidgets.ImageCanvas` + `ImageCanvasState` supply pan and zoom. Earth, ground tracks, orbit ribbons, objects coloured by residual magnitude through `ktsu.ImGui.Color` and `Palette.Semantic`. |
| **Residual plots** | ImPlot. RIC components against time; |residual| against Δt on log axes with all four storage types overplotted. `ImGuiExtensionManager` auto-detects and initializes ImPlot by reflection, so adding the `Hexa.NET.ImPlot` package reference is the entire integration. |
| **Storage comparison** | A table: for the selected object and Δt, |Δ_arith| in float / double / decimal / PreciseNumber, **and wall-clock per propagation alongside it.** The cost of precision is reported as prominently as the benefit. A showcase that hid a 500× slowdown would not be a showcase, it would be an advertisement. |
| **Element inspector** | `ImGuiWidgets.PropertyGrid`, one row per OMM field, each showing the value, its quantization step, and the sensitivity ∂r/∂field in metres per ulp at the current horizon. This is the panel that makes Δ_data tangible. |
| **Time inspector** | The Julian Date staircase. A continuous time sweep against propagated along-track position, per storage type. |
| **Conjunction screening** *(stretch)* | All-against-all with a smart pre-filter; miss distances land in the metre-to-km range, where the *differencing* of two large nearly-equal state vectors is exactly the cancellation case the storage types disagree on. |

Propagation runs on background threads; results marshal back through `ImGuiApp.Invoker`.
`ImGuiAppPerformanceSettings` throttles unfocused and idle frame rates so a long background
sweep is not competing with a 60 Hz redraw of a static screen.

---

## 8. Library gaps found

Real findings from reading the three repositories, in the order they will block work. Each is
a candidate contribution back upstream.

### ktsu.PreciseNumber

**G1 — No transcendental functions at all. Blocking.**
`PreciseNumber` implements `INumber<PreciseNumber>` and nothing else: no
`ITrigonometricFunctions`, `IRootFunctions`, `ILogarithmicFunctions`, or
`IExponentialFunctions`. SGP4 is saturated with `sin`, `cos`, `atan2`, `sqrt` and `fmod`.
`Osculator.Math.Precise` must supply them (Taylor series with argument reduction for sin/cos,
Newton for sqrt, `atanh` series or AGM for log). *Upstream: implement
`IRootFunctions<PreciseNumber>` and `ITrigonometricFunctions<PreciseNumber>`.*

**G2 — π is carried to 26 significant digits while `MinimumDivisionPrecision` is 50.**
`Pi = 3.1415926535897932384626433` (26 digits, and **truncated rather than rounded** — the next
true digit is 8, so a correctly rounded 26-digit π would end `…434`). `Tau` likewise carries 25.

This caps the whole precise path. Reducing an angle mod 2π cannot be more accurate than π
itself, and SGP4's mean anomaly accumulates thousands of revolutions — reducing ~10⁴ rad mod 2π
costs 4–5 digits, leaving ~21 usable digits no matter what `Divide` is asked for. *Upstream:
carry π, τ and e to at least `MinimumDivisionPrecision` digits, correctly rounded, and expose
`Pi(int significantDigits)`.* Note that `Semantics` already computes π by Machin's formula in
`Semantics.Test/Quantities/PiLiteralTests.cs` — the algorithm is in the family already.

**G3 — `Exp` and non-integer `Pow` route through `double`.**
`PreciseNumber.Pow` falls back to `Math.Exp(Math.Log(To<double>()) * power.To<double>())` for
any non-integer exponent, so `x^0.5` is a 15-digit answer wearing a 50-digit type. Exponential
atmosphere models in the drag force hit this directly. *Upstream: series-based `Exp` and `Log`.*

**G4 — Performance is an open risk, not a known quantity.**
`Divide` defaults to 50 significant digits. One SGP4 call is roughly 500 operations; the full
catalogue at 100 steps is ~1.5 × 10⁹ `PreciseNumber` operations. The precise path is a
*reference*, computed on demand for one object, never for the catalogue. M3 carries a hard
gate: if one `PreciseNumber` SGP4 call exceeds a stated budget, the design is revisited before
anything is built on it. `PreciseNumber.Benchmarks` already exists and is the place to measure.

### ktsu.Semantics.Quantities

**G5 — Vector forms have no unit factories.**
Scalars get `Length<T>.FromKilometer(x)`. Vectors do not: `Position3D<T>` is constructed
`new() { X = …, Y = …, Z = … }` with raw `T` in SI base units. Orbital work is natively in
kilometres, so every construction site has to remember a ×1000 by hand — precisely the class
of error the library exists to prevent. *Upstream: emit `From{Unit}` factories on vector forms
too, plus an `In(unit)` reader.*

**G6 — `IVectorN.Length()` returns bare `T`, dropping the dimension.**
The C++ projection's `magnitude()` answers with the V0 form of the same dimension. The C#
`Position3D<T>.Length()` returns `T` — so the type information is discarded at exactly the
point you wanted `Distance<T>`. *Upstream: add `Magnitude()` returning the V0 quantity
alongside the existing `Length()`.*

**G7 — `StorageMath.Sqrt` is `internal`.**
The type-generic Newton square root (seeded through `double`, refined in `T`'s own arithmetic,
scaled by powers of four for values `double` cannot hold) is exactly what an application doing
its own vector math over quantities needs, and it is unreachable. `Osculator` would otherwise
reimplement it verbatim. *Upstream: make it public, or expose a public `Quantities.StorageMath`.*

**G8 — No gravitational parameter dimension.**
μ (L³T⁻²) is the most-used constant in orbital mechanics and `dimensions.json` has no entry
with those exponents — it is currently expressible only as a bare `T`. *Upstream: add
`GravitationalParameter` with `MeterCubedPerSecondSquared` and
`KilometerCubedPerSecondSquared`.*

Two companions are more interesting, because they are **name collisions on existing exponent
vectors** — exactly the 72-dimensions-over-63-vectors situation the nominal layer exists for:

- `SpecificAngularMomentum` (L²T⁻¹) would be a second name on `KinematicViscosity`'s vector.
- `SpecificOrbitalEnergy` (L²T⁻²) would be a **third** name on the vector already shared by
  `AbsorbedDose` and `EquivalentDose` — specific orbital energy and absorbed radiation dose
  are dimensionally identical (both are J/kg), which is a genuinely good illustration for the
  repository's own documentation of why exponents alone cannot carry meaning.

### ktsu.ImGui.App

**G9 — No GL context is exposed to the application.**
Not a defect, but it decides the globe's implementation: CPU rasterization into
`CreateTexture`/`UpdateTexture`, at a cost of ~3.7 MB of upload per frame at 1280×720. Worth
investigating whether `BeginExternalFrameSession(IRendererBackend)` permits interleaved custom
GL before committing to the CPU path.

**G10 — No virtualized table widget.**
30,000 rows needs `ImGuiListClipper` used directly. *Upstream candidate: a `VirtualTable`
widget in `ktsu.ImGui.Widgets`*, with the isolation UI test suite the repository requires.

**G11 — ImPlot needs no integration code**, being loaded by assembly name through reflection
in `ImGuiExtensionManager.InitializeImPlot`. Recorded here because it is a pleasant surprise
worth not re-deriving.

---

## 9. Validation

Non-negotiable gates, in order:

1. **SGP4 against Vallado's official verification suite** (`SGP4-VER.TLE` + `tcppver.out`),
   which specifies expected positions to 10⁻⁸ km. Until this passes in `double`, nothing else
   in the application means anything.
2. **The same suite in every storage type**, with the tolerance scaled to the type. `float`
   will not meet 10⁻⁸ km and is not expected to — recording *where* it fails is a result.
3. **Frame transforms against IERS test vectors.**
4. **SP3 interpolation by held-out epochs.**
5. **`Δ_arith(PreciseNumber) ≡ 0`** — the trivial invariant that proves the comparison harness
   is actually holding everything but the storage type fixed.
6. **Benchmarks in CI**, per storage type per propagator, so a precision regression and a
   performance regression are both visible.

---

## 10. Milestones

| | Deliverable | Gate |
|---|---|---|
| **M0** | Repository skeleton, this specification, CI | — |
| **M1** | `Sgp4<T>` generic, `double` path | **Vallado suite passes** |
| **M2** | CelesTrak client + cache; TLE-vs-later-TLE divergence; catalogue and globe panels | End-to-end divergence number for the ISS |
| **M3** | `PreciseMath`, `PreciseNumber` path, high-precision π | **Performance gate (G4)**; Vallado suite passes in `PreciseNumber` |
| **M4** | Frames (TEME↔ITRF↔GCRF), EOP; CDDIS/ILRS SP3 | LAGEOS-1 against SLR truth |
| **M5** | Storage comparison panel; Monte-Carlo Δ_data; element inspector | **The decomposition works — the headline result** |
| **M6** | Cowell integrator, force model; time inspector | JD staircase and long-arc round-off demos |
| **M7** | Space-Track; JPL Horizons third bodies | Full catalogue, historical element sets |
| **M8** | Conjunction screening, polish, documentation | — |

M5 is the milestone that justifies the project. Everything before it is infrastructure;
everything after it is elaboration.

---

## 11. Risks

- **G4, PreciseNumber performance**, is the one that could invalidate the design. It is gated
  at M3, before anything is built on top of the precise path.
- **G2, π precision**, caps the reference's own accuracy at ~21 digits after argument
  reduction. Either fixed upstream or worked around locally with a computed π; either way it
  must be resolved before M5's numbers can be trusted.
- **Four data sources is a lot of integration surface**, three of them with credentials or
  usage limits. They are deliberately sequenced M2 → M4 → M7 so that the headline result at M5
  depends on only the first two.
- **Earth Orientation Parameters** are a further network dependency and a further quantization
  source that itself belongs in the Δ_data term — easy to forget, and it would silently
  contaminate Δ_model.
- **Being wrong in public.** This application makes quantitative claims about error budgets in
  a field with expert practitioners. The Vallado verification gate and the published
  methodology are the defence; they are not optional.

---

## 12. Open questions

1. **The name is settled, with one thing on the record.**
   [OSCulator](https://osculator.net/) is an existing commercial Mac application — an
   OSC-to-MIDI controller bridge, named for Open Sound Control rather than for osculation.
   Different domain and not a trademark conflict, but the bare word's search results belong
   to it, so discoverability comes from `ktsu.Osculator` and the repository rather than from
   the name alone.
2. **Does `BeginExternalFrameSession(IRendererBackend)` allow custom GL** interleaved with
   ImGui rendering? If so the globe is a real 3D scene rather than a CPU raster (G9).
3. **Should `PreciseMath` live in `Osculator` or go straight upstream** into
   `ktsu.PreciseNumber` as `IRootFunctions`/`ITrigonometricFunctions` implementations (G1)?
   Upstream is better for everyone and slower to land.
4. **How much of the force model is worth building** versus using an existing .NET
   astrodynamics library for the truth-side numerical propagator? Building it serves the
   showcase; borrowing it serves the schedule.
