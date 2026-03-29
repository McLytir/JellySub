# JellySub

> **No-login subtitle downloader for Jellyfin.**  
> Uses the OpenSubtitles API, Subscene, and YifySubtitles — no account needed.

[![Build](https://github.com/McLytir/JellySub/actions/workflows/build.yml/badge.svg)](https://github.com/McLytir/JellySub/actions/workflows/build.yml)
[![Latest Release](https://img.shields.io/github/v/release/McLytir/JellySub)](https://github.com/McLytir/JellySub/releases/latest)
[![License: GPL-3.0](https://img.shields.io/github/license/McLytir/JellySub)](LICENSE)

---

## Table of Contents

1. [Features](#features)
2. [Installation](#installation)
   - [Method A — Jellyfin Plugin Repository (recommended)](#method-a--jellyfin-plugin-repository-recommended)
   - [Method B — Manual zip install](#method-b--manual-zip-install)
   - [Jellyfin Tray / Desktop (Windows)](#jellyfin-tray--desktop-windows)
   - [Jellyfin Server — Linux](#jellyfin-server--linux)
   - [Jellyfin Server — Docker](#jellyfin-server--docker)
   - [Jellyfin Server — Windows](#jellyfin-server--windows)
   - [Jellyfin Server — macOS](#jellyfin-server--macos)
3. [First-time Setup](#first-time-setup)
4. [Usage](#usage)
   - [Assisted Search](#assisted-search)
   - [Manual Search](#manual-search)
   - [Series / Folder Download](#series--folder-download)
   - [Library Scan](#library-scan)
   - [Sync Tools (ffsubsync & alass)](#sync-tools-ffsubsync--alass)
5. [Subtitle Sources](#subtitle-sources)
6. [Configuration Reference](#configuration-reference)
7. [Building from Source](#building-from-source)
8. [Contributing](#contributing)
9. [Licence](#licence)

---

## Features

| Feature | Description |
|---|---|
| **Assisted search** | Click a Jellyfin item → auto-search by its metadata → you choose which subtitle(s) to download |
| **Manual search** | Free-text query with optional season/episode filter |
| **Series batch download** | Download subtitles for a whole series or season at once |
| **Guided series mode** | You pick ep 1's subtitle; the plugin automatically matches the same uploader / release group for all remaining episodes |
| **Player test** | Before committing a batch, download ep 1's subtitle and get a VLC command / XSPF playlist to verify sync in your local player |
| **Manual re-sync** | After the playtest, trigger ffsubsync or alass on the subtitle from inside Jellyfin |
| **Library scan** | Scheduled or on-demand — finds every media file missing a subtitle for your configured language(s) and silently downloads the best match |
| **Multi-source** | OpenSubtitles.org (public API) · Subscene · YifySubtitles. Enable, disable and re-order by priority in settings |
| **Sync tool integration** | Download and invoke ffsubsync (audio-based) or alass (reference-subtitle) directly from the Jellyfin UI |
| **No login required** | Every scraper works anonymously |

---

## Installation

### Method A — Jellyfin Plugin Repository (recommended)

This is the easiest method. Jellyfin downloads and installs the plugin for you, and you will be notified of updates automatically.

**Step 1 — Add the JellySub repository**

1. Open your Jellyfin web interface and sign in as an administrator.
2. Go to **Dashboard** (the ⚙ gear icon, top-right).
3. Navigate to **Plugins → Repositories**.
4. Click **＋ Add**.
5. Fill in the form:
   - **Repository name:** `JellySub`
   - **Repository URL:**  
     ```
     https://raw.githubusercontent.com/McLytir/JellySub/main/manifest.json
     ```
6. Click **Save**.

**Step 2 — Install the plugin**

1. Go to **Plugins → Catalogue**.
2. Find **JellySub** in the *Subtitles* category (use the search bar if needed).
3. Click **Install**.
4. Restart Jellyfin when prompted.

> After restarting, JellySub appears under **Plugins → My Plugins** and is ready to configure.

---

### Method B — Manual zip install

Use this if you can't reach GitHub from your Jellyfin server.

1. Download the latest `jellysub_X.Y.Z.0.zip` from the  
   [**Releases page**](https://github.com/McLytir/JellySub/releases/latest).
2. Extract the zip — it contains two `.dll` files:
   - `Jellyfin.Plugin.JellySub.dll`
   - `HtmlAgilityPack.dll`
3. Copy **both** files into the JellySub plugin folder  
   (see the per-platform paths below).
4. Restart Jellyfin.

---

### Jellyfin Tray / Desktop (Windows)

The **Jellyfin Tray** app bundles a full Jellyfin server and exposes its web UI at  
`http://localhost:8096` by default. Installation is identical to any other Jellyfin server:

- **Via repository (recommended):** follow [Method A](#method-a--jellyfin-plugin-repository-recommended) — just open `http://localhost:8096` in your browser.
- **Via manual install:** place the DLL files in:
  ```
  %APPDATA%\Jellyfin\plugins\JellySub\
  ```
  Typical full path:
  ```
  C:\Users\<YourName>\AppData\Roaming\Jellyfin\plugins\JellySub\
  ```
  Then right-click the tray icon → **Restart Jellyfin**.

---

### Jellyfin Server — Linux

**Plugin folder** (choose whichever matches your install):

| Install type | Plugin folder |
|---|---|
| Debian / Ubuntu package | `/var/lib/jellyfin/plugins/JellySub/` |
| Portable `.tar.gz` | `~/.local/share/jellyfin/plugins/JellySub/` |
| Snap | `/var/snap/jellyfin/current/config/jellyfin/plugins/JellySub/` |

**Using the repository (recommended):**
```bash
# Simply follow Method A from the web UI — no CLI needed.
# The web UI is at http://<server-ip>:8096
```

**Manual install:**
```bash
# Adjust the path to match your install type from the table above
PLUGIN_DIR="/var/lib/jellyfin/plugins/JellySub"
sudo mkdir -p "$PLUGIN_DIR"

# Download the latest release zip
curl -L -o /tmp/jellysub.zip \
  "$(curl -s https://api.github.com/repos/McLytir/JellySub/releases/latest \
     | grep browser_download_url | grep '.zip' | cut -d'"' -f4)"

# Extract the DLLs
sudo unzip -o /tmp/jellysub.zip '*.dll' -d "$PLUGIN_DIR"

# Fix ownership
sudo chown -R jellyfin:jellyfin "$PLUGIN_DIR"

# Restart
sudo systemctl restart jellyfin
```

---

### Jellyfin Server — Docker

The plugin folder is inside the Jellyfin **config volume**.  
If you mounted your config volume at `/config`, the path is:

```
/config/plugins/JellySub/
```

**Using the repository (recommended):**
```bash
# Open the Jellyfin web UI (port 8096 by default) and follow Method A.
# No container restart needed until you click "Install" — Jellyfin handles it.
```

**Manual install:**
```bash
# Replace /config with your actual config volume host path
PLUGIN_DIR="/config/plugins/JellySub"
mkdir -p "$PLUGIN_DIR"

# Download and extract
curl -L -o /tmp/jellysub.zip \
  "$(curl -s https://api.github.com/repos/McLytir/JellySub/releases/latest \
     | grep browser_download_url | grep '.zip' | cut -d'"' -f4)"

unzip -o /tmp/jellysub.zip '*.dll' -d "$PLUGIN_DIR"

# Restart the container
docker restart jellyfin        # or: docker compose restart jellyfin
```

**docker-compose tip:** if you use a named volume, find the volume path first:
```bash
docker volume inspect jellyfin_config | grep Mountpoint
```

---

### Jellyfin Server — Windows

**Plugin folder:**
```
%PROGRAMDATA%\Jellyfin\Server\plugins\JellySub\
```
Typical full path:
```
C:\ProgramData\Jellyfin\Server\plugins\JellySub\
```

**Using the repository (recommended):**  
Open `http://localhost:8096`, follow [Method A](#method-a--jellyfin-plugin-repository-recommended).

**Manual install:**
1. Download `jellysub_X.Y.Z.0.zip` from [Releases](https://github.com/McLytir/JellySub/releases/latest).
2. Create the folder `C:\ProgramData\Jellyfin\Server\plugins\JellySub\` if it doesn't exist.
3. Extract both `.dll` files into that folder.
4. Restart the Jellyfin service:
   ```powershell
   Restart-Service JellyfinServer
   ```
   Or use **Services** (`services.msc`) → **Jellyfin Server** → Restart.

---

### Jellyfin Server — macOS

**Plugin folder:**
```
~/.local/share/jellyfin/plugins/JellySub/
```

**Using the repository (recommended):**  
Open `http://localhost:8096`, follow [Method A](#method-a--jellyfin-plugin-repository-recommended).

**Manual install:**
```bash
PLUGIN_DIR="$HOME/.local/share/jellyfin/plugins/JellySub"
mkdir -p "$PLUGIN_DIR"

curl -L -o /tmp/jellysub.zip \
  "$(curl -s https://api.github.com/repos/McLytir/JellySub/releases/latest \
     | grep browser_download_url | grep '.zip' | cut -d'"' -f4)"

unzip -o /tmp/jellysub.zip '*.dll' -d "$PLUGIN_DIR"

# Restart (adjust if you installed via Homebrew / different method)
brew services restart jellyfin
```

---

## First-time Setup

After installation and restart:

1. Go to **Dashboard → Plugins → My Plugins → JellySub → Settings**.
2. Read the built-in overview at the top of the page. It explains the three main workflows:
   - **Single item** subtitle search
   - **Folder / series** batch download
   - **Whole-library** subtitle scan
3. Configure the settings section:
   - **Sources** — enable/disable and drag to set priority order.
   - **Preferred Languages** — add language codes in priority order  
     (e.g. `en` for English, `fr` for French). Use [BCP-47](https://r12a.github.io/app-subtags/) codes.
   - **Default mode** — *Assisted* (you pick) or *Auto* (silent best match).
4. Click **Save**.
5. *(Optional)* Install sync tools — see [Sync Tools](#sync-tools-ffsubsync--alass).

---

## Usage

All JellySub pages are accessible from the settings page or directly via  
**Dashboard → Plugins → My Plugins → JellySub**.

---

### Assisted Search

> Auto-search using a media item's metadata, then you choose what to download.

1. Navigate to **JellySub → Search Subtitles**.
2. Make sure **Assisted** mode is selected (top-right toggle).
3. Paste the **Jellyfin Item ID** of the movie or episode.
   - To find an item's ID: open the item in Jellyfin → the URL contains  
     `/details?id=<UUID>` — copy the UUID.
4. Optionally override the language(s) (comma-separated, e.g. `en,fr`).
5. Click **Search** — results from all enabled sources appear, ranked by quality.
6. Tick the checkboxes next to the subtitles you want.
7. Click **Download selected** — files are saved next to the media file as  
   `<MediaFileName>.<lang>.srt`.

---

### Manual Search

> Free-text search across all sources when you don't have an item ID.

1. Navigate to **JellySub → Search Subtitles**.
2. Switch to **Manual** mode (top-right toggle).
3. Enter a title, optional season/episode numbers, and language(s).
4. Click **Search**, then select and download as above.

---

### Series / Folder Download

> Batch download for an entire series or season.

**Auto mode** — fully silent:

1. Navigate to **JellySub → Series Download**.
2. Select **Auto** mode.
3. Paste the **Jellyfin Item ID** of the series or season.
4. Enter the target language.
5. Click **Analyse** — the plugin lists every episode with its current subtitle status.
6. Click **Download all** — each episode gets the highest-ranked available subtitle.

**Guided mode** — you set the template from episode 1:

1. Select **Guided** mode and fill in item ID + language.
2. Click **Analyse** — the plugin fetches subtitle candidates for episode 1 and shows them.
3. Pick the subtitle for episode 1 (results are sorted: hash match first, then by downloads).
4. The plugin matches the **same uploader** (or the same release group as a fallback) for every other episode, and previews the matches in the episode list.
5. *(Optional)* Click **Test episode 1** — the subtitle is downloaded to a temp file and you receive:
   - A **VLC command** to copy and run: `vlc "/path/video.mkv" --sub-file="/tmp/jellysub_test_….srt"`
   - A **downloadable XSPF playlist** you can open directly in VLC.
6. Watch the first few minutes. If the sync is off, click **Re-sync after playtest** and choose a sync tool (see below).
7. Once happy, click **Download all** — the batch runs with a live progress bar.

---

### Library Scan

> Finds all media files missing a subtitle for your configured language(s) and auto-downloads.

1. Navigate to **JellySub → Library Scan**.
2. Click **▶ Start Scan**.
3. The status page polls every 3 seconds and shows:
   - A summary bar: Downloaded / Not Found / Failed / Skipped.
   - A filterable log of every processed item.
4. To schedule automatic scans, go to **Settings → Library Scan Schedule**.

---

### Sync Tools (ffsubsync & alass)

JellySub integrates two subtitle-sync tools. Neither is mandatory — the plugin works without them. Once installed they continue to work normally from the command line; JellySub just adds UI buttons to invoke them.

| Tool | Method | Speed | Best when |
|---|---|---|---|
| **ffsubsync** | Analyses the video's audio track | 1–5 min per episode | No reference subtitle available |
| **alass** | Aligns against a reference subtitle in another language you already have | Seconds | You have another language sub that is already in sync |

**Installing from the plugin UI (recommended):**

1. Go to **Settings → Sync Tools**.
2. Click **Install** next to the tool you want.
   - *ffsubsync* is installed via `pip` — Python 3.7+ must be present on the server.
   - *alass* downloads a pre-built binary from GitHub Releases — no dependencies.
3. The status badge updates to show the installed version.

**Installing manually on the server:**

```bash
# ffsubsync  (requires Python 3.7+)
pip install ffsubsync

# alass  (Linux x86_64 example — adjust for your OS/arch)
curl -L -o /usr/local/bin/alass \
  "$(curl -s https://api.github.com/repos/kaegi/alass/releases/latest \
     | grep browser_download_url | grep linux | grep x86_64 | cut -d'"' -f4)"
chmod +x /usr/local/bin/alass
```

**Using sync from the UI:**

- After any subtitle download a **Sync** button appears alongside the file path.
- In Guided series mode the **Re-sync after playtest** button is available between the player test and the batch confirm.
- Choose **ffsubsync** (no reference needed) or **alass** (provide the path to a reference `.srt` in another language).
- The synced file overwrites the original, or sits alongside it as `.synced.srt` — controlled by **Settings → Keep original alongside synced version**.

---

## Subtitle Sources

| Source | Best for | Notes |
|---|---|---|
| **OpenSubtitles.org** | Movies + TV, all languages | Uses the public REST API, which avoids the classic site’s anti-bot challenge. Downloads are ZIP or GZip archives. |
| **Subscene** | Movies + TV | May be blocked by Cloudflare bot protection on some server IPs. Disable in settings if it fails consistently. |
| **YifySubtitles** | Movies | Indexed by IMDb ID, so it only works for content that has an IMDb ID in Jellyfin. Very reliable for movies. |

Search results from all enabled sources are merged, deduped, and ranked:  
**hash match → download count → upload date → not machine-translated**.

---

## Configuration Reference

| Setting | Default | Description |
|---|---|---|
| **Enabled Sources** | All three, in order | Sources to use and their priority. Sources run concurrently; the order affects which result appears first when scores are tied. |
| **Preferred Languages** | `en` | BCP-47 codes in priority order. The search runs once per language. |
| **Fallback to any language** | Off | If no subtitle exists in a preferred language, accept any. |
| **Default mode** | Assisted | What happens when you click a media item: *Assisted* = show results, *Auto* = silent download. |
| **Overwrite existing** | Off | Whether to replace subtitle files that already exist on disk. |
| **Minimum download count** | 0 | Filter out subtitles with fewer downloads than this value. |
| **Batch scan schedule** | Manual | *Manual*, *After library refresh*, *Daily*, or *Weekly*. |
| **ffsubsync path** | *(empty = use PATH)* | Override the location of the ffsubsync executable. |
| **alass path** | *(empty = use PATH)* | Override the location of the alass executable. |
| **Auto-sync after download** | Off | Automatically run a sync tool after every subtitle download. |
| **Keep original on sync** | On | Saves the synced output as a separate file rather than overwriting. |

---

## Building from Source

**Requirements:** [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```bash
# Clone
git clone https://github.com/McLytir/JellySub.git
cd JellySub

# Restore and build
dotnet restore JellySub.sln
dotnet build   JellySub.sln --configuration Release

# Publish (produces DLLs in ./artifacts/)
dotnet publish Jellyfin.Plugin.JellySub/Jellyfin.Plugin.JellySub.csproj \
    --configuration Release \
    --output ./artifacts
```

Copy `artifacts/Jellyfin.Plugin.JellySub.dll` and `artifacts/HtmlAgilityPack.dll`  
to your Jellyfin plugins folder (see paths above).

**Releasing a new version:**

```bash
# Bump version in Directory.Build.props, commit, then:
git tag v1.2.3
git push origin v1.2.3
# GitHub Actions builds the zip, updates manifest.json, and creates the release.
```

---

## Contributing

Bug reports and pull requests are welcome.

- **Scraper broken?** HTML structures change. Open an issue with the source name and describe what stopped working. A PR updating the XPath selectors in the relevant `*Source.cs` file is the fastest fix.
- **New source?** Implement `ISubtitleSource`, add a constant to `SourceIds.cs`, register it in `PluginServiceRegistrator.cs`, and add it to the default `EnabledSources` list in `PluginConfiguration.cs`.
- **Translations?** Language mappings live in `Sources/LanguageMap.cs`.

---

## Licence

[GPL-3.0](LICENSE) © McLytir  
This project is not affiliated with Jellyfin, OpenSubtitles, Subscene, or YifySubtitles.
