#!/usr/bin/env python3
"""
update_manifest.py  –  called by the GitHub Actions release workflow.

Usage:
    python3 scripts/update_manifest.py \
        --version   1.0.0.0 \
        --checksum  <md5hex> \
        --source-url https://github.com/McLytir/JellySub/releases/download/v1.0.0/jellysub_1.0.0.0.zip \
        --changelog "Initial release"
"""

import argparse
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

MANIFEST = Path(__file__).parent.parent / "manifest.json"
TARGET_ABI = "10.10.0.0"   # minimum Jellyfin version


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--version",    required=True)
    ap.add_argument("--checksum",   required=True)
    ap.add_argument("--source-url", required=True)
    ap.add_argument("--changelog",  default="See GitHub release notes.")
    args = ap.parse_args()

    data = json.loads(MANIFEST.read_text(encoding="utf-8"))
    plugin = data[0]

    new_entry = {
        "version":   args.version,
        "changelog": args.changelog,
        "targetAbi": TARGET_ABI,
        "sourceUrl": args.source_url,
        "checksum":  args.checksum,
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    }

    # Remove existing entry for this version (idempotent re-runs)
    plugin["versions"] = [
        v for v in plugin.get("versions", [])
        if v["version"] != args.version
    ]

    # Insert at front (newest first)
    plugin["versions"].insert(0, new_entry)

    MANIFEST.write_text(
        json.dumps(data, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )

    print(f"manifest.json updated → version {args.version}")


if __name__ == "__main__":
    main()
