# Changelog

All notable changes to JellySub are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [v1.0.8] – 2026-03-26

### Fixed
- Search UI now shows the resolved media title and year instead of the raw Jellyfin item ID.
- Added YIFY title-to-IMDb fallback so movie searches can work even when the item does not already carry an IMDb ID.

## [v1.0.7] – 2026-03-26

### Fixed
- Expanded Windows web-root detection and fixed web-client script downloads.

## [v1.0.6] – 2026-03-26

### Fixed
- Additional manual fixes and stability improvements to JellySub navigation, UI actions, and web-client integration.

## [v1.0.5] – 2026-03-26

### Added
- Added Linux, macOS, and Windows uninstall/revert scripts for the JellySub web-client patch.
- Added JellySub settings actions for downloading uninstall scripts and attempting automatic uninstall on default server paths.

## [v1.0.4] – 2026-03-26

### Added
- Added downloadable JellySub web-client install scripts for Linux, macOS, and Windows.
- Added plugin settings UI for downloading those scripts and attempting automatic installation into default Jellyfin web roots.
- Added embedded JellySub web-client plugin asset so the server plugin can patch local Jellyfin web roots directly.

### Improved
- Added web-client integration status reporting in the JellySub settings page.
- Improved instructions for default vs custom Jellyfin web installations.

## [v1.0.3] – 2026-03-26

### Fixed
- Reworked JellySub page navigation to use explicit client-side `Dashboard.navigate(...)` routing instead of relying on link handling.
- Fixed navigation from the settings page to Search, Batch, and Scan pages.
- Fixed navigation back from Search, Batch, and Scan pages to the main JellySub config page.

### Improved
- JellySub utility pages remain exposed through Jellyfin's plugin menu for faster access outside the plugin settings screen.

## [v1.0.2] – 2026-03-26

### Fixed
- Fixed Jellyfin plugin page links to use the client route format expected by the web app (`configurationpage?name=...`).
- Exposed JellySub search, batch, and scan pages directly in Jellyfin's plugin menu.
- Removed the incomplete client-side main-page injection attempt that was not actually loaded by Jellyfin.

## [v1.0.1] – 2026-03-26

### Fixed
- Fixed Jellyfin page routing for JellySub pages so subtitle actions no longer land on the wrong page.
- Fixed guided batch matching to use the actually selected anchor item instead of assuming the first list item.
- Fixed series / folder batch analysis to work with folder-style selections and non-episode media items.
- Added main Jellyfin UI action buttons for subtitle search, batch subtitle actions, and library-level scan access.

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
