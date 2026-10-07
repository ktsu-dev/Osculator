# SP3 test data

Two excerpts of real precise orbit files, used by `Sp3ParserTests` and by the gate-4 held-out-epoch
test in `Sp3InterpolatorTests`. They are measurement products, not source code.

| File | What it is |
|---|---|
| `igs16295-excerpt.sp3` | IGS final GPS orbit for 2011-04-01 (GPS week 1629, day 5): 96 epochs at 15 minutes, GPS time, IGS05 frame, positions and satellite clocks. |
| `lageos1-excerpt.sp3` | ILRS LAGEOS-1 orbit for 2021-12-30 23:00 to 2021-12-31 00:00 UTC: 31 epochs at 2 minutes, ITRF2014, positions and velocities, no clock field. |

## Sources

**IGS.** The final orbit `igs16295.sp3` is published by the International GNSS Service, whose
products are freely available for any use with acknowledgement of the IGS. The copy used here is the
one distributed in RTKLIB's sample data
(<https://github.com/tomojitakasu/RTKLIB/tree/master/util/data>), because the IGS archives at CDDIS
require an Earthdata login and this environment had none.

**ILRS.** The LAGEOS-1 orbit is an ILRS analysis-centre product (the header names JCET; the comment
lines name the `ilrsb` combination it was cut from). The copy used here is Orekit's test resource
`src/test/resources/sp3/issue1014-days-increment.sp3` (<https://github.com/CS-SI/Orekit>, Apache 2.0),
for the same reason. ILRS data are freely available with acknowledgement of the ILRS.

## How the excerpts differ from the originals

The point of each edit is to keep the files small and well-formed; no position, velocity or clock
value was changed.

- **IGS:** only satellites G01, G02 and G05 are kept, of the original 32, and the `+` satellite-list
  and `++` accuracy lines were rewritten to declare those three. G01 is kept deliberately: its clock
  is the bad-value marker `999999.999999` at every epoch, which is the case the parser must read as
  "no clock". Every epoch and the whole remaining header are as published.
- **LAGEOS-1:** the original continues for 31 more epochs past midnight, and those epochs are written
  as day 0 of January 2022 (`* 2022  1  0  0  2`), which is not a date — that defect is what the
  Orekit resource exists to test. The excerpt stops at the last well-formed epoch, 2021-12-31 00:00,
  and the header's epoch count was changed from 62 to 31 to match. `Sp3ParserTests` reproduces the
  defect on purpose and asserts it is refused.

Two further irregularities of the LAGEOS-1 file were left as they are, because a real ILRS file has
them and the parser should cope: its epoch lines start the year one column earlier than the SP3-c
specification says, and its header writes the six-character frame name `ITRF14` into a five-column
field. Its `##` line also gives a GPS week and day that do not match the start time on its first
line; nothing reads them.
