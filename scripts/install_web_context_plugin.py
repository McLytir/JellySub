#!/usr/bin/env python3
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PLUGIN_SRC = ROOT / 'web-client' / 'jellysub-context-plugin.js'


def patch_index(index_path: Path):
    text = index_path.read_text(encoding='utf-8')
    marker = 'jellysub-context-plugin.js'
    if marker in text:
        return False

    script_tag = '    <script src="jellysub-context-plugin.js"></script>\n'
    if '</body>' in text:
        text = text.replace('</body>', script_tag + '</body>', 1)
    elif '</head>' in text:
        text = text.replace('</head>', script_tag + '</head>', 1)
    else:
        raise RuntimeError(f'Could not patch {index_path}: no </body> or </head> tag found')

    index_path.write_text(text, encoding='utf-8')
    return True


def patch_config(config_path: Path):
    data = json.loads(config_path.read_text(encoding='utf-8'))
    plugins = data.setdefault('plugins', [])
    if 'jellysubContext' not in plugins:
        plugins.append('jellysubContext')
        config_path.write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')
        return True
    return False


def main():
    if len(sys.argv) != 2:
        print('Usage: install_web_context_plugin.py <jellyfin-web-root>')
        sys.exit(1)

    webroot = Path(sys.argv[1]).expanduser().resolve()
    if not webroot.exists():
        print(f'Web root not found: {webroot}')
        sys.exit(1)

    index_path = webroot / 'index.html'
    config_path = webroot / 'config.json'
    plugin_dst = webroot / 'jellysub-context-plugin.js'

    for path in (index_path, config_path, PLUGIN_SRC):
        if not path.exists():
            print(f'Missing required file: {path}')
            sys.exit(1)

    plugin_dst.write_text(PLUGIN_SRC.read_text(encoding='utf-8'), encoding='utf-8')
    changed_index = patch_index(index_path)
    changed_config = patch_config(config_path)

    print(f'Installed plugin script to: {plugin_dst}')
    print(f'Patched index.html: {changed_index}')
    print(f'Patched config.json: {changed_config}')
    print('Done. Clear client cache / restart the client after updating web assets.')


if __name__ == '__main__':
    main()
