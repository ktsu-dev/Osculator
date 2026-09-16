# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Status

**There is no code yet.** This repository currently holds a design specification,
[`docs/spec.md`](docs/spec.md), and nothing else. Read it before writing anything — it settles
architecture, data sources, propagators, the error decomposition, and the validation gates.

Every quantitative figure in the spec is a **projection**, not a measurement. Do not quote them as
results. Milestone M5 is where measurements replace them.

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

None yet. When projects exist they will follow the ktsu conventions used across the sibling repos:

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
| `Osculator.Math.Precise` | `PreciseMath`: sin, cos, atan2, sqrt, exp, log, π for `PreciseNumber` |
| `Osculator.Storage.{Double,Float,Decimal,Precise}` | One-file facades, each referencing one `ktsu.Semantics.Quantities.*` alias package |
| `Osculator.App` | `ktsu.ImGui.App` UI |
| `Osculator.Tests` | MSTest, including the Vallado SGP4 verification suite |
| `Osculator.Benchmarks` | BenchmarkDotNet, cost per propagation per storage type |

### Why the storage facades are separate projects

The `ktsu.Semantics.Quantities.{Double,Float,Decimal,Precise}` packages inject **global using
aliases, project-wide** — their own description says "use one storage-type alias package per
project". An application wanting four at once therefore cannot use them in its core. `Osculator.Core`
is written against the open generics (`Position3D<T>`, `Length<T>`, `Duration<T>`), and so is
`Osculator.App`, which displays all four side by side. The four facade projects are each one file
long and are where the alias packages are actually demonstrated.

## Domain traps

Five things that are easy to get wrong here and expensive to debug.

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

## Upstream dependencies

Speccing this application surfaced eleven gaps in the three libraries it consumes. Ten are filed with
full designs; see the README for the list. The blocking one is
**[ktsu-dev/PreciseNumber#78](https://github.com/ktsu-dev/PreciseNumber/issues/78)**: `PreciseNumber`
implements `INumber<T>` and nothing else, so there is no `Sqrt`, `Sin`, `Cos` or `Atan2`, and SGP4
cannot be written without them. `Osculator.Math.Precise` exists to supply them locally until that
lands.

Two related constraints worth knowing before starting M3:

- **[PreciseNumber#79](https://github.com/ktsu-dev/PreciseNumber/issues/79)** — `Pi` carries 26
  significant digits against a default division precision of 50. Argument reduction cannot beat the
  precision of π, so the precise path currently caps out around 21 usable digits.
- **[Semantics#239](https://github.com/ktsu-dev/Semantics/issues/239)** — `StorageMath.Sqrt<T>` is
  `internal`, so the type-generic Newton root cannot be reused and would have to be reimplemented.

## Validation gates

Non-negotiable, in order. Gate 1 comes before anything else in the repository means anything.

1. **SGP4 against Vallado's official verification suite** (`SGP4-VER.TLE` + `tcppver.out`), which
   specifies expected positions to 10⁻⁸ km.
2. The same suite in every storage type, tolerance scaled to the type. `float` will not meet
   10⁻⁸ km and is not expected to — recording *where* it fails is a result.
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

Not yet configured. Auto-generated files (`VERSION.md`, `CHANGELOG.md`, `LICENSE.md`) are produced by
the pipeline and should never be edited manually.
