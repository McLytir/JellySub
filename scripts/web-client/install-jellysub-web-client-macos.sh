#!/usr/bin/env bash
set -euo pipefail

PLUGIN_URL="https://raw.githubusercontent.com/McLytir/JellySub/main/web-client/jellysub-context-plugin.js"
CANDIDATES=(
  "/Applications/Jellyfin.app/Contents/Resources/jellyfin-web"
  "/Applications/Jellyfin Desktop.app/Contents/Resources/jellyfin-web"
  "$HOME/Applications/Jellyfin.app/Contents/Resources/jellyfin-web"
  "$HOME/Applications/Jellyfin Desktop.app/Contents/Resources/jellyfin-web"
)

patch_root() {
  local root="$1"
  local plugin="$root/jellysub-context-plugin.js"
  local index="$root/index.html"
  local config="$root/config.json"

  [[ -f "$index" && -f "$config" ]] || return 1

  curl -fsSL "$PLUGIN_URL" -o "$plugin"

  grep -q 'jellysub-context-plugin.js' "$index" || \
    python3 - <<PY
from pathlib import Path
p = Path(r'''$index''')
text = p.read_text(encoding='utf-8')
text = text.replace('</body>', '    <script src="jellysub-context-plugin.js"></script>\n</body>', 1) if '</body>' in text else text.replace('</head>', '    <script src="jellysub-context-plugin.js"></script>\n</head>', 1)
p.write_text(text, encoding='utf-8')
PY

  python3 - <<PY
import json
from pathlib import Path
p = Path(r'''$config''')
data = json.loads(p.read_text(encoding='utf-8'))
plugins = data.setdefault('plugins', [])
if 'jellysubContext' not in plugins:
    plugins.append('jellysubContext')
p.write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')
PY

  echo "Patched: $root"
  return 0
}

found=0
for root in "${CANDIDATES[@]}"; do
  if patch_root "$root"; then
    found=1
  fi
done

if [[ "$found" -eq 0 ]]; then
  echo "No default Jellyfin web root found. Edit CANDIDATES in this script for a custom install."
  exit 1
fi

echo "Done. Restart Jellyfin / Jellyfin Desktop and clear browser cache."
