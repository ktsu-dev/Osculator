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

The deep-space coefficients were checked against Spacetrack Report #3 itself, whose `DPINIT`
listing gives them in full:

<https://celestrak.org/NORAD/documentation/spacetrk.pdf>

That listing settled four eccentricity polynomials this implementation first had wrong, and one
branch it did not know existed: `G520` splits again at an eccentricity of 0.715, inside the branch
that already splits at 0.65. That third range is why the verification file distinguishes "ecc in
0.7 to 0.715" from "ecc > 0.715" when no other coefficient splits between them.

## Note on the reference implementations

The AIAA package also contains reference implementations in several languages. They carry no licence
statement, so nothing in this repository is derived from them. They were read to confirm the
algorithm's published form, which is the same role the paper serves.

## One row in the expected output is not a model output

Object 33334's block carries a single row, at zero minutes, and that row is byte-for-byte identical
to the last row of object 33333's block above it.

33334 is a constructed case — object 26975's elements with the mean motion replaced by 0.00001
revolutions a day — and the verification file's own comment says it was an attempt to provoke a
particular error code. At that mean motion the lunar-solar periodic coefficients, which scale as the
reciprocal of it, are some five orders of magnitude too large: the perturbed eccentricity comes out
at −122 and the model refuses to return a state. What the reference harness printed is a position
buffer that the failed call never wrote to, still holding the previous object's last state.

`Sgp4VerificationTests` therefore compares nothing against that row, and asserts the refusal
instead. It is the only row in the file that is skipped, and this is why.
