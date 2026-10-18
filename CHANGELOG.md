# Changelog

## 0.1.0 – pre-release

First public version. Tested with synthetic FIT files only (see Limits in the README).

- Merge two or more `*.fit` activity files into one valid FIT activity file.
- Preview of every file: sport, start/end, duration, distance, heart rate, calories, laps, pauses.
- Conflict analysis with resolution options: overlaps (prefer earlier file, prefer later file, keep all),
  gaps and pauses (optionally written as timer stop/start), duplicates, mixed sports.
- Device metadata (`file_id` and `device_info`) inherited from the first file, with `--device` / `--serial` overrides
  and a read-back verification of the written file.
- Windows 11 look through the built-in WPF Fluent theme; English and German UI.
- App icon.
