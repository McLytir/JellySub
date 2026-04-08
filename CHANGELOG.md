# Changelog

All notable changes to JellySub are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [v1.0.36] – 2026-04-06

### Fixed
- Fixed a compilation error in the API controller related to metadata provider ambiguity.

## [v1.0.35] – 2026-04-06

### Improved
- Finalized search coverage improvements and metadata fallback logic.
- Synchronized manifest and build metadata for release stability.

## [v1.0.34] – 2026-04-06

### Fixed
- Fixed an "undefined" popup message when running the web-client auto-installer from the plugin settings page.

## [v1.0.32] – 2026-04-06

### Removed
- Removed the live subtitle font and size controls from the web-client plugin as it caused UI persistence issues and was unnecessary for some users.

## [v1.0.31] – 2026-04-03

### Added
- Added restyle actions for existing subtitles in JellySub Operations, including series/season scope and whole-library scope.
- Added best-effort live subtitle font/size controls to the JellySub web-client patch for Jellyfin Web/Desktop playback pages.

### Changed
- Existing subtitle restyling now converts SRT sidecars to styled ASS output using the configured JellySub font settings, and can optionally delete the original SRT files afterward.

## [v1.0.30] – 2026-04-03

### Added
- Added configurable subtitle font family and font size settings in JellySub.
- Added optional ASS subtitle output mode so newly downloaded subtitles can be saved with the configured styling.

### Changed
- JellySub now saves either `.srt` or `.ass` depending on the configured subtitle output format.

## [v1.0.29] – 2026-04-03

### Fixed
- Restored batch analysis and library scan on Jellyfin 10.11.x by removing the plugin's runtime dependency on `ILibraryManager.GetItemList(InternalItemsQuery)`.
- Jellyfin changed that interface method's return type between 10.10 and 10.11, which caused `MissingMethodException` in live installs even though the plugin still built successfully.
- Batch and scan item enumeration now use recursive folder traversal instead, which is compatible with the affected Jellyfin versions.

## [v1.0.28] – 2026-04-03

### Fixed
- Moved JellySub Operations onto a fresh `jellysubsearchv4` page/controller route so Jellyfin/browser cache cannot keep serving the stale operations page bundle.
- Added a legacy `jellysubsearchv3` redirect shim so older context-menu/web-client launchers are forwarded into the fresh route instead of opening the stale page.
- Updated all current plugin-page and web-client navigation to target the new route directly.

## [v1.0.27] – 2026-04-03

### Changed
- Unified single-item search, series/folder batch download, and library scan under one JellySub Operations page with in-page section switching.
- Kept the old batch/scan routes as compatibility shims so stale links still land on the new unified page.

### Fixed
- Context-menu and settings-page navigation now open the correct unified JellySub flow (`single`, `batch`, or `scan`) instead of scattering across separate pages.
- Assisted search once again preserves and visibly fills the Jellyfin item ID when launched from the web-client action, so the page opens with the proper context and auto-search can run.

## [v1.0.26] – 2026-04-02

### Fixed
- The ellipsis/context-menu "Search subtitles" action is now shown only for playable media items (Movie/Episode/Video), not folder-like items such as Series or Season. This prevents assisted search from auto-launching against folder metadata like "Season 1 2023".
- The context-menu search route now explicitly opens in assisted mode.

## [v1.0.25] – 2026-04-02

### Fixed
- Search page now explicitly requests JSON and parses string responses defensively. This fixes the case where the backend returned valid search results but Jellyfin's web client handed the page a raw JSON string, causing the UI to treat the result list as empty.

## [v1.0.24] – 2026-04-01

### Fixed
- Forced the subtitle search UI onto a brand-new page/controller route to defeat stubborn Jellyfin/browser cache reuse.
- Added a backend fallback so if a stale UI still accidentally sends `itemId=Inception` to the assisted-search endpoint, JellySub treats it as a title search instead of throwing a GUID parse error.

## [v1.0.23] – 2026-04-01

### Fixed
- Forced a cache bust for the subtitle search page controller script. If Jellyfin/browser was still serving an older cached search.js bundle, this ensures the updated result-normalization logic is actually loaded.

## [v1.0.22] – 2026-04-01

### Fixed
- Search results page now tolerates both camelCase and PascalCase API payloads. This fixes the case where the backend was returning valid subtitle results but the web UI rendered nothing because it only looked for lowercase JSON property names.

## [v1.0.21] – 2026-04-01

### Changed
- Added search-pipeline result-count logging for OpenSubtitles, the aggregator, and the API endpoints. This makes it explicit whether results are being returned by the provider, filtered out in JellySub, or lost later in the UI.

## [v1.0.20] – 2026-04-01

### Fixed
- OpenSubtitles title searches now avoid the provider's broken canonical redirect behavior. Query titles are normalized to lowercase before request, and malformed redirects such as `https://_/search/...` are repaired and retried against the original host.

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
