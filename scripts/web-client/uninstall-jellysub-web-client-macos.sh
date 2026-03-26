#!/usr/bin/env bash
set -euo pipefail

CANDIDATES=(
  "/Applications/Jellyfin.app/Contents/Resources/jellyfin-web"
  "/Applications/Jellyfin Desktop.app/Contents/Resources/jellyfin-web"
  "$HOME/Applications/Jellyfin.app/Contents/Resources/jellyfin-web"
  "$HOME/Applications/Jellyfin Desktop.app/Contents/Resources/jellyfin-web"
)

revert_root() {
  local root="$1"
  local plugin="$root/jellysub-context-plugin.js"
  local index="$root/index.html"
  local config="$root/config.json"

  [[ -f "$index" && -f "$config" ]] || return 1

  rm -f "$plugin"

  python3 - <<PY
from pathlib import Path
p = Path(r'''$index''')
text = p.read_text(encoding='utf-8')
text = text.replace('    <script src="jellysub-context-plugin.js"></script>\n', '')
text = text.replace('    <script src="jellysub-context-plugin.js"></script>\r\n', '')
p.write_text(text, encoding='utf-8')
PY

  python3 - <<PY
import json
from pathlib import Path
p = Path(r'''$config''')
data = json.loads(p.read_text(encoding='utf-8'))
plugins = data.get('plugins', [])
if isinstance(plugins, list):
    data['plugins'] = [p for p in plugins if p != 'jellysubContext']
p.write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')
PY

  echo "Reverted: $root"
  return 0
}

found=0
for root in "${CANDIDATES[@]}"; do
  if revert_root "$root"; then
    found=1
  fi
done

if [[ "$found" -eq 0 ]]; then
  echo "No default Jellyfin web root found. Edit CANDIDATES in this script for a custom install."
  exit 1
fi

echo "Done. Restart Jellyfin / Jellyfin Desktop and clear browser cache."
