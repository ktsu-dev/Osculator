# Verification data

These two files are the standard SGP4 verification set. They are test vectors — inputs and the
positions a correct implementation produces — not source code, and Osculator's propagator is written
from the published algorithm rather than ported from any reference implementation.

| File | What it is |
|---|---|
| `SGP4-VER.TLE` | 34 element sets exercising near-earth and deep-space regimes, including deliberately pathological cases: decayed orbits, near-zero eccentricity, eccentricity above 0.99, an exactly-resonant period, and the Lyddane choice boundary. Each line 2 carries three extra fields — start, stop and step in minutes since epoch. |
| `sgp4-ver-expected.out` | Expected TEME position (km) and velocity (km/s) at each step. |

## Source

Distributed by CelesTrak with *Revisiting Spacetrack Report #3* (AIAA 2006-6753) by David Vallado,
Paul Crawford, Richard Hujsak and T.S. Kelso:

<https://celestrak.org/publications/AIAA/2006-6753/>

The underlying model is from Spacetrack Report #3 (1980), a United States government publication.
`sgp4-ver-expected.out` is the output of the package's Java implementation; the Fortran, C++ and
Pascal implementations in the same package agree with it to within about 1e-8 km, which is the
tolerance the tests assert.

## Note on the reference implementations

The AIAA package also contains reference implementations in several languages. They carry no licence
statement, so nothing in this repository is derived from them. They were read to confirm the
algorithm's published form, which is the same role the paper serves.
