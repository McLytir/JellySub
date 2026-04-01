# Changelog

All notable changes to JellySub are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [v1.0.19] – 2026-04-01

### Fixed
- OpenSubtitles REST requests now use the exact minimal header set that works in direct PowerShell testing, and automatic redirects are disabled. This avoids following the malformed redirect target that was surfacing in Jellyfin as a second connection attempt to `_ :443`.

## [v1.0.18] – 2026-04-01

### Changed
- Added explicit OpenSubtitles direct-connect diagnostics so the running Jellyfin logs now show the exact request URI, request host, and direct target host/port used by the source. This is to isolate the remaining `_ :443` networking failure on affected Windows installs.

## [v1.0.17] – 2026-04-01

### Fixed
- OpenSubtitles requests now use a dedicated direct HttpClient inside the source itself, bypassing the factory pipeline entirely. This is an extra hardening step for systems where the ambient/runtime HTTP stack was still trying to resolve an invalid proxy host such as `_`.

## [v1.0.16] – 2026-04-01

### Fixed
- Bypassed broken ambient/system proxy resolution for JellySub outbound HTTP requests. This fixes search failures where the running Jellyfin process tried to connect to an invalid proxy host such as `_` instead of the subtitle provider.
- Keeps the restored OpenSubtitles REST search/download flow and the manual-search item-context fix.

## [v1.0.15] – 2026-04-01

### Fixed
- Restored real OpenSubtitles search and download functionality by switching back to the working REST API flow; the newer HTML-scraping path was being blocked by the site's anti-bot challenge and returned 401 challenge pages instead of search results.
- Manual subtitle search continues to preserve the selected Jellyfin item context so search results can be downloaded directly from the search page.

## [v1.0.14] – 2026-04-01

### Fixed
- Manual subtitle search now preserves the selected Jellyfin item context, so search results can be downloaded directly from the search page instead of failing with a missing item ID.

## [v1.0.13] – 2026-04-01

### Fixed
- Restored OpenSubtitles.org search after the source implementation changes and fixed the follow-up build issues in the release pipeline.
- Fixed episode request construction/scoping issues in the JellySub controller.

### Improved
- Added missing XML documentation across public DTOs, services, and controller APIs to reduce release/build noise.

## [v1.0.12] – 2026-03-29

### Fixed
- Episode searches now inherit the parent series IMDb ID when the episode itself does not have one, which restores subtitle results for series like Blossoms Shanghai.
- OpenSubtitles REST now normalizes IMDb IDs to the numeric form it expects, restoring subtitle results for series like Blossoms Shanghai.

## [v1.0.11] – 2026-03-29

### Fixed
- OpenSubtitles REST now normalizes IMDb IDs to the numeric form it expects, restoring subtitle results for series like Blossoms Shanghai.
- OpenSubtitles search now uses the public REST API instead of scraping the classic site, which avoids the anti-bot block.
- Search UI now shows the resolved media title and year instead of the raw Jellyfin item ID.
- Added YIFY title-to-IMDb fallback so movie searches can work even when the item does not already carry an IMDb ID.

## [v1.0.10] – 2026-03-29

### Fixed
- OpenSubtitles search now uses the public REST API instead of scraping the classic site, which avoids the anti-bot block.
- Search UI now shows the resolved media title and year instead of the raw Jellyfin item ID.
- Added YIFY title-to-IMDb fallback so movie searches can work even when the item does not already carry an IMDb ID.

## [v1.0.9] – 2026-03-29

### Fixed
- OpenSubtitles search now uses the public REST API instead of scraping the classic site, which avoids the anti-bot block.
- Search UI now shows the resolved media title and year instead of the raw Jellyfin item ID.
- Added YIFY title-to-IMDb fallback so movie searches can work even when the item does not already carry an IMDb ID.

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
