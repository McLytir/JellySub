/* JellySub – config.js */
const API = '/JellySub';

const ALL_SOURCES = [
    { id: 'OpenSubtitlesOrg', name: 'OpenSubtitles.org' },
    { id: 'Subscene',         name: 'Subscene' },
    { id: 'YifySubtitles',    name: 'YifySubtitles' },
];

export default function (view) {
    let config = {};

    view.querySelector('#navSearch').addEventListener('click', () => navigateTo('configurationpage?name=jellysubsearchv4&op=single'));
    view.querySelector('#navSeries').addEventListener('click', () => navigateTo('configurationpage?name=jellysubsearchv4&op=batch'));
    view.querySelector('#navScan').addEventListener('click', () => navigateTo('configurationpage?name=jellysubsearchv4&op=scan'));

    view.querySelector('#btnWebClientInstall').addEventListener('click', () => installWebClient(view));
    view.querySelector('#btnWebClientUninstall').addEventListener('click', () => uninstallWebClient(view));
    view.querySelector('#btnDownloadLinux').addEventListener('click', () => downloadScript('linux', 'install'));
    view.querySelector('#btnDownloadMac').addEventListener('click', () => downloadScript('macos', 'install'));
    view.querySelector('#btnDownloadWindows').addEventListener('click', () => downloadScript('windows', 'install'));
    view.querySelector('#btnDownloadLinuxUninstall').addEventListener('click', () => downloadScript('linux', 'uninstall'));
    view.querySelector('#btnDownloadMacUninstall').addEventListener('click', () => downloadScript('macos', 'uninstall'));
    view.querySelector('#btnDownloadWindowsUninstall').addEventListener('click', () => downloadScript('windows', 'uninstall'));

    // ── Load ──────────────────────────────────────────────────────────────
    view.addEventListener('viewshow', async () => {
        Dashboard.showLoadingMsg();
        try {
            const res = await ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl(`${API}/config`) });
            config = res;
            populateForm(view, config);
            await loadSyncTools(view);
            await loadWebClientStatus(view);
        } catch (e) {
            Dashboard.processErrorResponse({ statusText: 'Failed to load config: ' + e });
        } finally {
            Dashboard.hideLoadingMsg();
        }
    });

    // ── Save ──────────────────────────────────────────────────────────────
    view.querySelector('#jellySubConfigForm').addEventListener('submit', async (e) => {
        e.preventDefault();
        Dashboard.showLoadingMsg();
        try {
            const updated = readForm(view, config);
            const saved = await ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl(`${API}/config`),
                data: JSON.stringify(updated),
                contentType: 'application/json',
            });
            config = saved || updated;
            const status = view.querySelector('#saveStatus');
            status.style.display = 'block';
            setTimeout(() => { status.style.display = 'none'; }, 3000);
        } catch (e) {
            Dashboard.processErrorResponse({ statusText: 'Failed to save: ' + e });
        } finally {
            Dashboard.hideLoadingMsg();
        }
        return false;
    });

    // ── Add language ──────────────────────────────────────────────────────
    view.querySelector('#addLangBtn').addEventListener('click', () => {
        const input = view.querySelector('#newLang');
        const code  = input.value.trim().toLowerCase();
        if (!code) return;
        addLangChip(view, code);
        input.value = '';
    });

    // ── Install sync tool buttons (delegated) ─────────────────────────────
    view.querySelector('#syncToolsSection').addEventListener('click', async (e) => {
        const btn = e.target.closest('[data-install-tool]');
        if (!btn) return;
        const toolId = btn.dataset.installTool;
        btn.disabled = true;
        btn.textContent = 'Installing…';
        try {
            const res = await ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl(`${API}/sync/tools/install`),
                data: JSON.stringify({ toolId }),
                contentType: 'application/json',
            });
            if (res.success) {
                Dashboard.alert('Installed successfully!\n' + res.output);
            } else {
                Dashboard.alert('Install failed:\n' + res.output);
            }
        } catch (ex) {
            Dashboard.alert('Install error: ' + ex);
        }
        await loadSyncTools(view);
    });
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function populateForm(view, cfg) {
    // Jellyfin's JSON serializer outputs camelCase; support both camelCase and PascalCase
    const get = (camel, Pascal) => cfg[camel] !== undefined ? cfg[camel] : cfg[Pascal];

    // Sources
    const container = view.querySelector('#sourceList');
    container.innerHTML = '';
    const ordered = get('enabledSources', 'EnabledSources') || ALL_SOURCES.map(s => s.id);
    // Show enabled first in order, then disabled
    const allIds = [...new Set([...ordered, ...ALL_SOURCES.map(s => s.id)])];
    allIds.forEach(id => {
        const src = ALL_SOURCES.find(s => s.id === id);
        if (!src) return;
        const enabled = ordered.includes(id);
        container.appendChild(makeSourceRow(src, enabled));
    });

    // Languages
    const langList = view.querySelector('#langList');
    langList.innerHTML = '';
    (get('preferredLanguages', 'PreferredLanguages') || ['en']).forEach(l => addLangChip(view, l));

    // Checkboxes / selects
    view.querySelector('#fallbackLang').checked      = get('fallbackToAnyLanguage', 'FallbackToAnyLanguage') || false;
    view.querySelector('#defaultMode').value         = get('defaultItemMode', 'DefaultItemMode')             || 'Assisted';
    view.querySelector('#overwriteExisting').checked = get('overwriteExisting', 'OverwriteExisting')         || false;
    view.querySelector('#minDownloads').value        = get('minimumDownloadCount', 'MinimumDownloadCount')   ?? 0;
    view.querySelector('#scanSchedule').value        = get('batchScanSchedule', 'BatchScanSchedule')         || 'Manual';
    view.querySelector('#autoSync').value            = get('autoSyncAfterDownload', 'AutoSyncAfterDownload') || 'Off';
    view.querySelector('#syncKeepOriginal').checked  = get('syncKeepOriginal', 'SyncKeepOriginal') !== false;
    view.querySelector('#ffsubsyncPath').value       = get('ffsubsyncPath', 'FfsubsyncPath')                 || '';
    view.querySelector('#alassPath').value           = get('alassPath', 'AlassPath')                         || '';
}

function readForm(view, existing) {
    // Sources — collect checked ones in DOM order
    const enabledSources = [...view.querySelectorAll('.source-checkbox:checked')]
        .map(cb => cb.dataset.sourceId);

    // Languages — collect chip values
    const langs = [...view.querySelectorAll('.lang-chip-code')].map(s => s.textContent);

    return {
        ...existing,
        enabledSources,
        preferredLanguages:   langs,
        fallbackToAnyLanguage: view.querySelector('#fallbackLang').checked,
        defaultItemMode:       view.querySelector('#defaultMode').value,
        overwriteExisting:     view.querySelector('#overwriteExisting').checked,
        minimumDownloadCount:  parseInt(view.querySelector('#minDownloads').value, 10) || 0,
        batchScanSchedule:     view.querySelector('#scanSchedule').value,
        autoSyncAfterDownload: view.querySelector('#autoSync').value,
        syncKeepOriginal:      view.querySelector('#syncKeepOriginal').checked,
        ffsubsyncPath:         view.querySelector('#ffsubsyncPath').value.trim(),
        alassPath:             view.querySelector('#alassPath').value.trim(),
    };
}

function makeSourceRow(src, enabled) {
    const row = document.createElement('div');
    row.style.cssText = 'display:flex;align-items:center;gap:10px;padding:6px 0;border-bottom:1px solid rgba(255,255,255,.08)';
    row.innerHTML = `
        <span style="cursor:grab;font-size:18px;color:#999">⠿</span>
        <label style="display:flex;align-items:center;gap:8px;flex:1;cursor:pointer">
          <input type="checkbox" is="emby-checkbox" class="source-checkbox"
                 data-source-id="${src.id}" ${enabled ? 'checked' : ''} />
          <span>${src.name}</span>
        </label>`;
    return row;
}

function addLangChip(view, code) {
    const list = view.querySelector('#langList');
    const chip = document.createElement('span');
    chip.style.cssText = 'display:inline-flex;align-items:center;gap:4px;background:rgba(255,255,255,.1);border-radius:16px;padding:2px 10px;margin:3px;font-size:13px';
    chip.innerHTML = `<span class="lang-chip-code">${code}</span>
        <button type="button" style="background:none;border:none;cursor:pointer;color:#ccc;font-size:14px;line-height:1;padding:0 0 0 4px">×</button>`;
    chip.querySelector('button').onclick = () => chip.remove();
    list.appendChild(chip);
}

function navigateTo(url) {
    if (Dashboard.navigate) {
        Dashboard.navigate(url);
        return;
    }

    window.location.href = '/' + url;
}

async function downloadScript(platform, mode) {
    try {
        const url = ApiClient.getUrl(`${API}/webclient/script`, { platform, mode });
        const scriptText = await ApiClient.ajax({ type: 'GET', url, dataType: 'text' });
        const blob = new Blob([scriptText], { type: 'text/plain;charset=utf-8' });
        const objectUrl = URL.createObjectURL(blob);
        const fileName = scriptFileName(platform, mode);

        const a = document.createElement('a');
        a.href = objectUrl;
        a.download = fileName;
        a.rel = 'noopener';
        document.body.appendChild(a);
        a.click();
        a.remove();

        setTimeout(() => URL.revokeObjectURL(objectUrl), 1000);
    } catch (e) {
        Dashboard.processErrorResponse({ statusText: 'Failed to download script: ' + e });
    }
}

function scriptFileName(platform, mode) {
    const uninstall = mode === 'uninstall';
    switch (platform) {
        case 'linux':
            return uninstall ? 'uninstall-jellysub-web-client-linux.sh' : 'install-jellysub-web-client-linux.sh';
        case 'macos':
            return uninstall ? 'uninstall-jellysub-web-client-macos.sh' : 'install-jellysub-web-client-macos.sh';
        case 'windows':
            return uninstall ? 'uninstall-jellysub-web-client-windows.ps1' : 'install-jellysub-web-client-windows.ps1';
        default:
            return uninstall ? 'uninstall-jellysub-web-client.sh' : 'install-jellysub-web-client.sh';
    }
}

async function loadWebClientStatus(view) {
    const el = view.querySelector('#webClientStatus');
    try {
        const data = await ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl(`${API}/webclient/status`) });
        const installed = data.patchedRoots?.length || 0;
        const candidates = data.candidateRoots?.length || 0;
        const roots = (data.candidateRoots || []).map(r => `<li><code>${escHtml(r)}</code></li>`).join('');
        el.innerHTML = `
            <div><strong>Detected OS:</strong> ${escHtml(data.platform || 'unknown')}</div>
            <div style="margin-top:4px"><strong>Default web roots checked:</strong> ${candidates}</div>
            <div style="margin-top:4px"><strong>Already patched:</strong> ${installed}</div>
            ${roots ? `<details style="margin-top:8px"><summary>Show checked paths</summary><ul style="margin:8px 0 0 18px">${roots}</ul></details>` : ''}
        `;
    } catch (e) {
        el.textContent = 'Could not load web-client integration status: ' + e;
    }
}

async function installWebClient(view) {
    await runWebClientAction(view, 'install-defaults', 'install');
}

async function uninstallWebClient(view) {
    await runWebClientAction(view, 'uninstall-defaults', 'uninstall');
}

async function runWebClientAction(view, endpoint, label) {
    Dashboard.showLoadingMsg();
    try {
        const res = await ApiClient.ajax({
            type: 'POST',
            url: ApiClient.getUrl(`${API}/webclient/${endpoint}`),
        });
        const ok = res.success ? '✓' : '✗';
        const details = (res.results || []).map(r => `${r.path}: ${r.status}${r.message ? ' — ' + r.message : ''}`).join('\n');
        Dashboard.alert(`${ok} ${res.message}${details ? `\n\n${details}` : ''}`);
        await loadWebClientStatus(view);
    } catch (e) {
        Dashboard.alert(`Web-client ${label} failed: ` + e);
    } finally {
        Dashboard.hideLoadingMsg();
    }
}

const escHtml = s => String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));

async function loadSyncTools(view) {
    const section = view.querySelector('#syncToolsSection');
    try {
        const res = await ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl(`${API}/sync/tools`) });
        section.innerHTML = '';
        (res.tools || []).forEach(t => {
            const row = document.createElement('div');
            row.style.cssText = 'display:flex;align-items:center;justify-content:space-between;padding:8px 0;border-bottom:1px solid rgba(255,255,255,.08);max-width:640px';
            row.innerHTML = `
                <div>
                  <strong>${t.displayName}</strong>
                  <span style="margin-left:8px;font-size:12px;color:${t.isInstalled ? '#4CAF50' : '#f44336'}">
                    ${t.isInstalled ? '● Installed' + (t.version ? ' v' + t.version : '') : '● Not installed'}
                  </span>
                  <div style="font-size:12px;color:#aaa;margin-top:2px">${t.description}</div>
                </div>
                ${!t.isInstalled
                    ? `<button is="emby-button" type="button" class="raised button-alt"
                              data-install-tool="${t.toolId}" style="white-space:nowrap">
                         Install
                       </button>`
                    : ''}`;
            section.appendChild(row);
        });
    } catch {
        section.innerHTML = '<p style="color:#f44336">Could not load sync tool status.</p>';
    }
}
