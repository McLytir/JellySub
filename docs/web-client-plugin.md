# JellySub web-client context actions

This repository now includes a **Jellyfin web-client plugin** that adds JellySub actions into the main client UI.

It is separate from the server plugin because Jellyfin server plugins (`IHasWebPages`) can expose config pages, but they **cannot directly extend the main item context menu in Jellyfin Web/Desktop** on their own.

## What this adds

When installed into a Jellyfin web client, it adds:

- **JellySub: Search subtitles** in item context flows
- **JellySub: Batch subtitles** for folder / series / season / video contexts
- **Subtitle scan** button on library/detail-style pages
- Header action buttons on supported pages for faster access

## Files

- Web-client plugin source: `web-client/jellysub-context-plugin.js`
- Installer helper: `scripts/install_web_context_plugin.py`
- Platform install scripts: `scripts/web-client/install-jellysub-web-client-*`
- Platform uninstall scripts: `scripts/web-client/uninstall-jellysub-web-client-*`

## How it works

Jellyfin Web loads client plugins from `config.json`.
This plugin uses the `window` plugin-loading path supported by Jellyfin Web's `pluginManager`:

1. `index.html` loads `jellysub-context-plugin.js`
2. the script defines `window.jellysubContext`
3. `config.json.plugins` includes `"jellysubContext"`
4. Jellyfin loads it at startup in both browser and desktop clients that use that web root

## Install into a Jellyfin web root

```bash
python3 scripts/install_web_context_plugin.py /path/to/jellyfin-web-root
```

This will:

- copy `jellysub-context-plugin.js` into the web root
- patch `index.html` to load it
- append `jellysubContext` to `config.json.plugins`

## Typical targets

### Jellyfin Server web UI

Often one of:

- `/usr/share/jellyfin/web`
- `/var/lib/jellyfin/web`
- your container-mounted Jellyfin web directory

### Jellyfin Desktop

Patch the desktop app's bundled web root as well if it does **not** use the server-hosted web UI.
The exact location depends on platform/package.

## Uninstall / revert

Use the matching uninstall script for your platform, or the uninstall button from JellySub settings when the server can access the target web root.

## Caveats

- This is a **web-client patch/plugin**, not a pure server-plugin feature.
- After installing, clear browser cache or restart Jellyfin Desktop.
- Client updates may overwrite the patched web root, requiring re-installation.
