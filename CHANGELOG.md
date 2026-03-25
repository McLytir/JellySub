# Changelog

All notable changes to JellySub are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [v1.0.0] – 2025-03-25

### Added
- Initial release
- Subtitle sources: OpenSubtitles.org, Subscene, YifySubtitles (no login required)
- Configurable source priority and enable/disable per source
- Assisted search — auto-search from Jellyfin item metadata, manual selection
- Manual search — free-text query with season/episode filter
- Series / folder batch download
  - Auto mode — silent best-match per episode
  - Guided mode — user picks ep 1 subtitle; plugin matches same uploader / release group for remaining episodes
- Player test — VLC command + XSPF playlist download before committing a series batch
- Manual re-sync trigger (ffsubsync or alass) after player test
- Library scan — on-demand or scheduled, with filterable results log
- Sync tool integration: install ffsubsync (pip) and alass (GitHub binary) from settings UI
- Auto-sync after download option
- Language mapping: 35 languages with BCP-47 ↔ ISO 639-2/B ↔ display name conversion
- Full Jellyfin plugin repository support (`manifest.json`)
