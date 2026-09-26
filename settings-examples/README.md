# Example settings files

Copy any of these next to `GymClock.exe` and rename it to `gymclock.settings.txt`.
A file with that name sitting beside the executable takes priority over
`%AppData%\GymClock\settings.txt`, which makes it easy to keep a USB-stick copy or
several shortcuts configured differently.

`station-builder.txt` shows the legacy/simple station format that still works
unchanged - per-station work/rest instructions, the on-screen table, and move
time between stations, with no program built. This is what you get from the
**Program** tab's on-screen checkboxes alone.

`circuit.program.txt` is a real **program**, in the plain-text script grammar
the Program Builder reads and writes - a three-station Sequential circuit with
a shared Move block between every pair of stations. Load it with **Import
Program...** on the Program tab (it is not a `gymclock.settings.txt` file
itself). See [Building a program](../README.md#building-a-program) in the main
README for the full explanation.

Three more real-world programmes, each built from an actual PE department
timing sheet, showing both execution modes in practice:

- `45-15-interval-cardio-circuit.program.txt` and `pyramid-cardio-circuit.program.txt`
  are **rotating circuits** - several groups on different stations at once, all
  following the same Shared Timing countdown, so the station panel is
  informational only (nothing is highlighted, since every station is in use
  simultaneously). Both wrap up with a set number of rotations through all the
  stations, with a Move block before each next rotation but not after the
  final one.
- `whole-group-hiit.program.txt` is a **single group** moving through 8
  stations one at a time - Sequential Stations, so the panel highlights
  whichever station is currently active as the group moves through it.
