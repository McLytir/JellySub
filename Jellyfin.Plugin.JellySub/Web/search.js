/* JellySub – search.js */
const API = '/JellySub';
let currentItemId = null;
let results = [];

export default function (view, params) {

    view.querySelector('#btnBackToConfig').addEventListener('click', () => navigateTo('configurationpage?name=jellysub'));

    // ── On show ───────────────────────────────────────────────────────────
    view.addEventListener('viewshow', () => {
        // Keep the resolved Jellyfin ID internal; show the title/year summary instead.
        const itemId = params.itemId || '';
        if (itemId) {
            view.querySelector('#itemIdInput').value = '';
            currentItemId = itemId;
        }

        // Set mode toggle state
        setMode(view, params.mode === 'manual' ? 'manual' : 'assisted');

        // Auto-search if itemId was provided
        if (itemId) runAssistedSearch(view);
    });

    // ── Mode toggle ───────────────────────────────────────────────────────
    view.querySelector('#btnAssisted').addEventListener('click', () => setMode(view, 'assisted'));
    view.querySelector('#btnManual').addEventListener('click',   () => setMode(view, 'manual'));

    // ── Searches ──────────────────────────────────────────────────────────
    view.querySelector('#btnAssistedSearch').addEventListener('click', () => runAssistedSearch(view));
    view.querySelector('#btnManualSearch').addEventListener('click',   () => runManualSearch(view));

    // Enter key in query boxes
    view.querySelector('#manualQuery').addEventListener('keydown', e => {
        if (e.key === 'Enter') runManualSearch(view);
    });
    view.querySelector('#itemIdInput').addEventListener('keydown', e => {
        if (e.key === 'Enter') runAssistedSearch(view);
    });

    // ── Select all ────────────────────────────────────────────────────────
    view.querySelector('#btnSelectAll').addEventListener('click', () => {
        view.querySelectorAll('.result-checkbox').forEach(cb => { cb.checked = true; });
        updateSelCount(view);
    });

    // ── Download selected ─────────────────────────────────────────────────
    view.querySelector('#btnDownloadSelected').addEventListener('click', () =>
        downloadSelected(view));
}

// ── Mode toggle ───────────────────────────────────────────────────────────────

function setMode(view, mode) {
    const isManual = mode === 'manual';
    view.querySelector('#assistedBar').style.display = isManual ? 'none'  : 'flex';
    view.querySelector('#manualBar').style.display   = isManual ? 'flex'  : 'none';
    view.querySelector('#btnAssisted').classList.toggle('button-alt', isManual);
    view.querySelector('#btnManual').classList.toggle('button-alt', !isManual);
}

// ── Search ────────────────────────────────────────────────────────────────────

async function runAssistedSearch(view) {
    const itemId = view.querySelector('#itemIdInput').value.trim() || currentItemId || '';
    const lang   = view.querySelector('#assistedLang').value.trim() || undefined;
    if (!itemId) { Dashboard.toast('Enter a Jellyfin item ID'); return; }

    currentItemId = itemId;
    await doSearch(view, ApiClient.getUrl(`${API}/search`, { itemId, ...(lang && { languages: lang }) }));
}

async function runManualSearch(view) {
    const query   = view.querySelector('#manualQuery').value.trim();
    const season  = view.querySelector('#manualSeason').value.trim() || undefined;
    const episode = view.querySelector('#manualEpisode').value.trim() || undefined;
    const lang    = view.querySelector('#manualLang').value.trim() || undefined;
    if (!query) { Dashboard.toast('Enter a title'); return; }

    const qs = { query, ...(lang && { languages: lang }), ...(season && { season }), ...(episode && { episode }) };
    await doSearch(view, ApiClient.getUrl(`${API}/search/manual`, qs));
}

async function doSearch(view, url) {
    Dashboard.showLoadingMsg();
    hideAll(view);
    try {
        const data = await ApiClient.ajax({ type: 'GET', url });
        if (data.error) {
            Dashboard.processErrorResponse({ statusText: data.error });
            return;
        }
        renderSearchInfo(view, data);
        results = data.results || [];
        renderResults(view, results);
    } catch (e) {
        Dashboard.processErrorResponse({ statusText: 'Search failed: ' + e });
    } finally {
        Dashboard.hideLoadingMsg();
    }
}

// ── Render ────────────────────────────────────────────────────────────────────

function renderSearchInfo(view, data) {
    const info = view.querySelector('#itemInfo');
    const title = (data.searchTitle || '').trim();
    const year  = data.searchYear ? ` (${data.searchYear})` : '';

    if (!title) {
        info.style.display = 'none';
        info.textContent = '';
        return;
    }

    info.textContent = `${title}${year}`;
    info.style.display = 'block';
}

function renderResults(view, res) {
    const container = view.querySelector('#resultsList');
    container.innerHTML = '';

    if (!res.length) {
        view.querySelector('#noResults').style.display = 'block';
        return;
    }

    view.querySelector('#resultsContainer').style.display = 'block';
    view.querySelector('#resultCount').textContent = `${res.length} result(s) found`;

    res.forEach((r, i) => {
        const row = document.createElement('div');
        row.style.cssText = 'display:flex;align-items:center;gap:10px;padding:8px 12px;' +
            `background:${i % 2 === 0 ? 'rgba(255,255,255,.04)' : 'rgba(255,255,255,.02)'}`;

        const hashBadge  = r.isHashMatch         ? '<span class="label" style="background:#4CAF50;color:#000;font-size:10px;border-radius:3px;padding:1px 5px">HASH</span> ' : '';
        const sdBadge    = r.isHearingImpaired    ? '<span class="label" style="background:#2196F3;color:#fff;font-size:10px;border-radius:3px;padding:1px 5px">SDH</span> '  : '';
        const mtBadge    = r.isMachineTranslated  ? '<span class="label" style="background:#FF9800;color:#000;font-size:10px;border-radius:3px;padding:1px 5px">MT</span> '   : '';
        const srcBadge   = `<span style="font-size:11px;color:#888;border:1px solid rgba(255,255,255,.2);border-radius:3px;padding:1px 5px">${r.sourceName}</span>`;
        const dlCount    = r.downloadCount > 0 ? `<span style="font-size:11px;color:#aaa">⬇ ${r.downloadCount.toLocaleString()}</span>` : '';
        const uploader   = r.uploader ? `<span style="font-size:11px;color:#aaa">👤 ${r.uploader}</span>` : '';
        const dateStr    = r.uploadDate ? new Date(r.uploadDate).toLocaleDateString() : '';

        row.innerHTML = `
            <input type="checkbox" class="result-checkbox" data-idx="${i}"
                   style="width:16px;height:16px;flex-shrink:0" />
            <div style="flex:1;min-width:0">
              <div style="white-space:nowrap;overflow:hidden;text-overflow:ellipsis;font-size:14px">
                ${hashBadge}${sdBadge}${mtBadge}
                <strong>${escHtml(r.releaseName || r.id)}</strong>
              </div>
              <div style="display:flex;gap:8px;flex-wrap:wrap;margin-top:3px">
                ${srcBadge}
                <span style="font-size:11px;background:rgba(255,255,255,.1);border-radius:3px;padding:1px 5px">${r.languageName}</span>
                ${dlCount}${uploader}
                ${dateStr ? `<span style="font-size:11px;color:#aaa">${dateStr}</span>` : ''}
              </div>
            </div>`;

        row.querySelector('.result-checkbox').addEventListener('change', () => updateSelCount(view));
        container.appendChild(row);
    });

    updateSelCount(view);
}

// ── Download ──────────────────────────────────────────────────────────────────

async function downloadSelected(view) {
    const selected = [...view.querySelectorAll('.result-checkbox:checked')]
        .map(cb => results[parseInt(cb.dataset.idx, 10)]);

    if (!selected.length) return;
    if (!currentItemId) { Dashboard.toast('No item ID — use Assisted mode or set an item ID'); return; }

    const progress = view.querySelector('#downloadProgress');
    const statusEl = view.querySelector('#dlStatus');
    progress.style.display = 'block';

    for (const r of selected) {
        statusEl.textContent = `Downloading: ${r.releaseName} (${r.languageName})…`;
        try {
            const res = await ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl(`${API}/download`),
                data: JSON.stringify({
                    itemId:       currentItemId,
                    sourceId:     r.sourceId,
                    subtitleId:   r.id,
                    downloadUrl:  r.downloadUrl,
                    language:     r.language,
                    releaseName:  r.releaseName,
                    uploader:     r.uploader,
                    releaseGroup: r.releaseGroup,
                }),
                contentType: 'application/json',
            });
            if (res.success) {
                statusEl.textContent = `✓ Saved: ${res.savedPath}`;
            } else {
                statusEl.textContent = `✗ Failed: ${res.error}`;
            }
        } catch (ex) {
            statusEl.textContent = `✗ Error: ${ex}`;
        }
        await sleep(400);
    }

    statusEl.textContent = `Done. ${selected.length} subtitle(s) processed.`;
}

// ── Utilities ─────────────────────────────────────────────────────────────────

function hideAll(view) {
    view.querySelector('#resultsContainer').style.display = 'none';
    view.querySelector('#noResults').style.display = 'none';
    view.querySelector('#downloadProgress').style.display = 'none';
    view.querySelector('#itemInfo').style.display = 'none';
    view.querySelector('#resultsList').innerHTML = '';
}

function updateSelCount(view) {
    const n   = view.querySelectorAll('.result-checkbox:checked').length;
    const btn = view.querySelector('#btnDownloadSelected');
    view.querySelector('#selCount').textContent = n;
    btn.disabled = n === 0;
}

function navigateTo(url) {
    if (Dashboard.navigate) {
        Dashboard.navigate(url);
        return;
    }

    window.location.href = '/' + url;
}

const escHtml = s => s.replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const sleep   = ms => new Promise(r => setTimeout(r, ms));
