# Format Support

## Tracker modules

- XM
- MOD and common ProTracker/SoundTracker variants
- S3M
- IT

Playback routes through native/module playback where available, with editable pattern views for direct tracker data.

## Chip formats

- SID
- NSF
- amChipper native module (`.amc`)

SID/NSF support uses internal player libraries. Exact export to tracker formats is reconstructed from playback/chip state and will keep improving as emulation and analysis improve.

## Exchange formats

- MIDI import/export for piano-roll note exchange.
- FL Studio Score (`.fsc`) import/export for piano-roll score exchange.
- WAV/MP3 rendering where audio render paths are configured.

## Source corpus policy

The repository ignores copied music corpora. Keep large SID/NSF/tracker libraries local unless every file is explicitly redistributable under compatible terms.
