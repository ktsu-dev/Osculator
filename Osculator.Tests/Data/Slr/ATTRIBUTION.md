# Laser-ranging test data

Used by `CpfFileTests` and by the end-to-end residual in `SlrResidualGateTests`. It is a
measurement product, not source code.

| File | What it is |
|---|---|
| `lageos2_cpf_160213_5441.sgf` | ILRS Consolidated Prediction Format file for LAGEOS-2 (NORAD 22195), issued by SGF (the NERC Space Geodesy Facility, Herstmonceux) for 2016-02-13: 288 ITRF positions at 5-minute spacing, UTC, CPF version 1. |

## Source

The copy used here is Orekit's test resource
`src/test/resources/orbit-determination/Lageos2/lageos2_cpf_160213_5441.sgf`
(<https://github.com/CS-SI/Orekit>, Apache 2.0, commit `0aa0f3bd57fc69e0ea29dd77e840418284fc3d82`),
because the ILRS archives at CDDIS require an Earthdata login and this environment had none. ILRS
data are freely available with acknowledgement of the ILRS. The file is byte-for-byte as Orekit
distributes it; nothing was edited.

The two other inputs to the LAGEOS-2 comparison are quoted in the test rather than committed as files:

- **The element set**, `1 22195U 92070B   16045.51027931 …`, is the public Space-Track element set
  that Orekit's `tle_od_test_Lageos2.in` (same directory) quotes as the source of its initial orbit.
  Both lines pass their checksums.
- **The Earth orientation** for 2016-02-13 and 2016-02-14 (MJD 57431 and 57432) is the IERS
  `finals2000A` Bulletin A columns, as they appear in Orekit's `src/test/resources/earth/finals2000A.all`.

## What it is good for, and what it is not

A CPF is a prediction: an analysis centre's orbit determination propagated forward so stations can
point their telescopes. For LAGEOS it is good to metres over its first day, which is three orders of
magnitude below the SGP4 residual the test measures and no tighter than that. An ILRS SP3 orbit is the
after-the-fact product, and it is what `LageosGate` compares against when it runs live.
