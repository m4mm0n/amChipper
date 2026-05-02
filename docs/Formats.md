# Format Support

## Tracker modules

- XM
- MOD and common ProTracker/SoundTracker variants
- S3M
- IT
- Broad libopenmpt-backed tracker family coverage such as 669, ABC, AHX, AMF, AMS, DBM, DMF, DSM, FAR, GDM, HVL, MED, MTM, OKT, PTM, STM, ULT, UMX, and related variants listed in the in-app Format Support tab.

Playback routes through native/module playback where available, with editable pattern views for direct tracker data.

## Chip formats

- SID
- NSF
- amChipper native module (`.amc`)

SID/NSF support uses internal player libraries. Exact export to tracker formats is reconstructed from playback/chip state and will keep improving as emulation and analysis improve.

## AMC native module goals

The `.amc` container is intended to be the amChipper-native project/chip module format:

- Compressed storage for metadata, normalized song data, and embedded exact source bytes.
- 64-channel project headroom beyond classic MOD/XM-era limits.
- Preservation of tracker rows including note, instrument, volume column, effect command, and effect parameter data where the source exposes them.
- Room for amChipper-only data such as mixer state, analyzer preferences, future chip/instrument envelopes, and workflow metadata.
- Exact playback fallback through the embedded source when the imported source player is still the most faithful playback path.

## Exchange formats

- MIDI import/export for piano-roll note exchange.
- FL Studio Score (`.fsc`) import/export for piano-roll score exchange.
- WAV/MP3 rendering where audio render paths are configured.

## Source corpus policy

The repository ignores copied music corpora. Keep large SID/NSF/tracker libraries local unless every file is explicitly redistributable under compatible terms.
