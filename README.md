# ktsu.Osculator

> A satellite tracker that tells you *why* its prediction is wrong — splitting the error into the model, the data, and the arithmetic.

[![License](https://img.shields.io/github/license/ktsu-dev/Osculator.svg?label=License&logo=nuget)](LICENSE.md)
[![GitHub commit activity](https://img.shields.io/github/commit-activity/m/ktsu-dev/Osculator?label=Commits&logo=github)](https://github.com/ktsu-dev/Osculator/commits/main)
[![GitHub contributors](https://img.shields.io/github/contributors/ktsu-dev/Osculator?label=Contributors&logo=github)](https://github.com/ktsu-dev/Osculator/graphs/contributors)

## Status

**Design only. No code exists yet.** The specification lives in [`docs/spec.md`](docs/spec.md);
every quantitative figure in it is a projection until milestone M5 replaces it with a measurement.

## Introduction

> **os·cu·lat·ing orbit** — the Keplerian orbit a body would follow if every perturbation stopped.
> From Latin *osculari*, to kiss: it touches the true trajectory at one instant, matching position
> and velocity exactly, and diverges immediately after.

Agreement at epoch, divergence after. That is what the name means and what the application measures.

It is easy to write a satellite tracker, and dozens exist. What none of them tell you is why their
prediction is wrong. Three independent sources compose into the gap between a predicted position and
a measured one:

| Term | Source | Typical size, LEO, 7 days out |
|---|---|---|
| **Δ_model** | SGP4 is an analytical mean-element theory fitted to a truncated force model. It is not the physics; it is a curve fit to the physics. | 5–20 km |
| **Δ_data** | The element set is quantized — a TLE carries mean motion to 8 decimals, eccentricity to 7, angles to 4. | 0.3–3 km |
| **Δ_arith** | Round-off in the propagator's own floating-point arithmetic. | measured, not assumed |

Everyone knows the first. Some people know the second. Almost nobody has *measured* the third,
because measuring it needs a reference computation whose own arithmetic error is negligible — and if
your only numeric type is `double`, you do not have one.

`ktsu.PreciseNumber` is that reference. Osculator compiles one propagator against four storage types
(`float`, `double`, `decimal`, `PreciseNumber`) through the `ktsu.Semantics.Quantities` alias
packages, holds everything but the storage type fixed, and differences the results.

**The expected answer is that `double`'s arithmetic error is negligible** — roughly nine orders of
magnitude below the data term — **and the point is to prove that rather than assert it.** A project
concluding "use more digits and your satellite tracking improves" would be false, and any
practitioner would know it within a minute. The defensible result is the decomposition itself.

## Where the arithmetic does break

Five specific, reproducible cases where Δ_arith stops being negligible, and the reason the
application is worth building:

- **The Julian Date resolution wall.** JD ≈ 2,461,000, and one ulp of that in `double` is ~48 µs. The
  two-part Julian Date — the hack the whole field uses — exists solely because one `double` cannot
  hold the number. Sweep time continuously and the propagated position is a *staircase*.
- **`float` cannot represent the residual at all.** One ulp at LEO radius is 0.5 m, so comparing
  against a 2 cm laser-ranging orbit is not inaccurate, it is impossible.
- **Kepler's equation at high eccentricity** — the textbook catastrophic-cancellation case.
- **Accumulated round-off over a long arc** — `float` reaches ~1.3 km of pure arithmetic error over
  a 30-day integration, the same order as the model error.
- **Exact residual statistics** — RMS over 10⁶ squared residuals, with no compensated summation.

## Data sources

NASA and NORAD, all cached to disk and usable offline:
[CelesTrak](https://celestrak.org) (OMM element sets, no auth),
[Space-Track.org](https://www.space-track.org) (the authoritative catalogue and element-set history),
[NASA CDDIS / ILRS](https://cddis.nasa.gov) (centimetre-accurate satellite-laser-ranging orbits in
SP3), and [JPL Horizons](https://ssd.jpl.nasa.gov/horizons/) (third bodies and spacecraft).

## What it showcases

| Library | What this application demonstrates about it |
|---|---|
| [`ktsu.PreciseNumber`](https://github.com/ktsu-dev/PreciseNumber) | A real problem — not a synthetic benchmark — for which arbitrary-precision decimal arithmetic is the right tool. |
| [`ktsu.Semantics.Quantities`](https://github.com/ktsu-dev/Semantics) | One propagator source over four storage types unchanged, and dimensional typing catching the errors this domain actually makes: km-vs-m, degrees-vs-radians, TEME-vs-J2000. |
| [`ktsu.ImGui.App`](https://github.com/ktsu-dev/ImGuiApp) | A dense real-time technical application: a 30,000-row catalogue, a globe, ImPlot time series, and a PID-limited frame budget while background threads propagate. |

## Upstream work this depends on

Speccing Osculator surfaced eleven gaps in those three libraries. Ten are filed with designs:

- PreciseNumber: [#78](https://github.com/ktsu-dev/PreciseNumber/issues/78) (tracking), [#79](https://github.com/ktsu-dev/PreciseNumber/issues/79), [#80](https://github.com/ktsu-dev/PreciseNumber/issues/80), [#81](https://github.com/ktsu-dev/PreciseNumber/issues/81), [#82](https://github.com/ktsu-dev/PreciseNumber/issues/82)
- Semantics: [#237](https://github.com/ktsu-dev/Semantics/issues/237), [#238](https://github.com/ktsu-dev/Semantics/issues/238), [#239](https://github.com/ktsu-dev/Semantics/issues/239), [#240](https://github.com/ktsu-dev/Semantics/issues/240)
- ImGuiApp: [#411](https://github.com/ktsu-dev/ImGuiApp/issues/411), [#413](https://github.com/ktsu-dev/ImGuiApp/issues/413) (3D rendering, with four sub-issues)

[PreciseNumber#78](https://github.com/ktsu-dev/PreciseNumber/issues/78) is the blocking one:
`PreciseNumber` implements `INumber<T>` and nothing else, so there is no `Sqrt`, `Sin`, `Cos` or
`Atan2` — and SGP4 is saturated with them.

## Roadmap

| | Deliverable | Gate |
|---|---|---|
| M0 | Repository, specification, CI | — |
| M1 | `Sgp4<T>` generic, `double` path | **Vallado verification suite passes** |
| M2 | CelesTrak, TLE-vs-later-TLE divergence, catalogue and globe | An end-to-end divergence number |
| M3 | `PreciseStorageMath`, the PreciseNumber path | **Performance gate** |
| M4 | Frames and EOP; CDDIS/ILRS SP3 | LAGEOS-1 against laser-ranging truth |
| M5 | Storage comparison, Monte-Carlo Δ_data, element inspector | **The decomposition works** |
| M6 | Cowell integrator, force model, time inspector | The JD staircase |
| M7 | Space-Track, JPL Horizons | Full catalogue and element-set history |
| M8 | Conjunction screening, polish | — |

See [`docs/spec.md`](docs/spec.md) for the full design.

## A note on the name

[OSCulator](https://osculator.net/) is an existing commercial Mac application — an OSC-to-MIDI
controller bridge, named for Open Sound Control rather than for osculation. Different domain and not
a trademark conflict, but the bare word's search results belong to it.

## Contributing

Contributions are welcome! Feel free to open issues or submit pull requests.

## License

This project is licensed under the MIT License. See the [LICENSE.md](LICENSE.md) file for details.
