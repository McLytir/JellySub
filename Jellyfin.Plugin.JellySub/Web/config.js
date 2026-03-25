/* JellySub – config.js */
const API = '/JellySub';

const ALL_SOURCES = [
    { id: 'OpenSubtitlesOrg', name: 'OpenSubtitles.org' },
    { id: 'Subscene',         name: 'Subscene' },
    { id: 'YifySubtitles',    name: 'YifySubtitles' },
];

export default function (view) {
    let config = {};

    // ── Load ──────────────────────────────────────────────────────────────
    view.addEventListener('viewshow', async () => {
        Dashboard.showLoadingMsg();
        try {
            const res = await ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl(`${API}/config`) });
            config = res;
            populateForm(view, config);
            await loadSyncTools(view);
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
            await ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl(`${API}/config`),
                data: JSON.stringify(updated),
                contentType: 'application/json',
            });
            config = updated;
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
                data: JSON.stringify({ ToolId: toolId }),
                contentType: 'application/json',
            });
            if (res.Success) {
                Dashboard.alert('Installed successfully!\n' + res.Output);
            } else {
                Dashboard.alert('Install failed:\n' + res.Output);
            }
        } catch (ex) {
            Dashboard.alert('Install error: ' + ex);
        }
        await loadSyncTools(view);
    });
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function populateForm(view, cfg) {
    // Sources
    const container = view.querySelector('#sourceList');
    container.innerHTML = '';
    const ordered = (cfg.EnabledSources || ALL_SOURCES.map(s => s.id));
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
    (cfg.PreferredLanguages || ['en']).forEach(l => addLangChip(view, l));

    // Checkboxes / selects
    view.querySelector('#fallbackLang').checked      = cfg.FallbackToAnyLanguage || false;
    view.querySelector('#defaultMode').value         = cfg.DefaultItemMode        || 'Assisted';
    view.querySelector('#overwriteExisting').checked = cfg.OverwriteExisting      || false;
    view.querySelector('#minDownloads').value        = cfg.MinimumDownloadCount   ?? 0;
    view.querySelector('#scanSchedule').value        = cfg.BatchScanSchedule      || 'Manual';
    view.querySelector('#autoSync').value            = cfg.AutoSyncAfterDownload  || 'Off';
    view.querySelector('#syncKeepOriginal').checked  = cfg.SyncKeepOriginal !== false;
    view.querySelector('#ffsubsyncPath').value       = cfg.FfsubsyncPath          || '';
    view.querySelector('#alassPath').value           = cfg.AlassPath              || '';
}

function readForm(view, existing) {
    // Sources — collect checked ones in DOM order
    const enabledSources = [...view.querySelectorAll('.source-checkbox:checked')]
        .map(cb => cb.dataset.sourceId);

    // Languages — collect chip values
    const langs = [...view.querySelectorAll('.lang-chip-code')].map(s => s.textContent);

    return {
        ...existing,
        EnabledSources:       enabledSources,
        PreferredLanguages:   langs,
        FallbackToAnyLanguage: view.querySelector('#fallbackLang').checked,
        DefaultItemMode:       view.querySelector('#defaultMode').value,
        OverwriteExisting:     view.querySelector('#overwriteExisting').checked,
        MinimumDownloadCount:  parseInt(view.querySelector('#minDownloads').value, 10) || 0,
        BatchScanSchedule:     view.querySelector('#scanSchedule').value,
        AutoSyncAfterDownload: view.querySelector('#autoSync').value,
        SyncKeepOriginal:      view.querySelector('#syncKeepOriginal').checked,
        FfsubsyncPath:         view.querySelector('#ffsubsyncPath').value.trim(),
        AlassPath:             view.querySelector('#alassPath').value.trim(),
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

async function loadSyncTools(view) {
    const section = view.querySelector('#syncToolsSection');
    try {
        const res = await ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl(`${API}/sync/tools`) });
        section.innerHTML = '';
        (res.Tools || []).forEach(t => {
            const row = document.createElement('div');
            row.style.cssText = 'display:flex;align-items:center;justify-content:space-between;padding:8px 0;border-bottom:1px solid rgba(255,255,255,.08);max-width:640px';
            row.innerHTML = `
                <div>
                  <strong>${t.DisplayName}</strong>
                  <span style="margin-left:8px;font-size:12px;color:${t.IsInstalled ? '#4CAF50' : '#f44336'}">
                    ${t.IsInstalled ? '● Installed' + (t.Version ? ' v' + t.Version : '') : '● Not installed'}
                  </span>
                  <div style="font-size:12px;color:#aaa;margin-top:2px">${t.Description}</div>
                </div>
                ${!t.IsInstalled
                    ? `<button is="emby-button" type="button" class="raised button-alt"
                              data-install-tool="${t.ToolId}" style="white-space:nowrap">
                         Install
                       </button>`
                    : ''}`;
            section.appendChild(row);
        });
    } catch {
        section.innerHTML = '<p style="color:#f44336">Could not load sync tool status.</p>';
    }
}
