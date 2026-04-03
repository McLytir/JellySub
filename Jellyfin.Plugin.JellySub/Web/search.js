/* JellySub – unified operations page */
const API = '/JellySub';

let currentItemId = null;
let singleResults = [];

let batchCurrentMode = 'auto';
let batchAnalysisData = null;
let batchEp1Anchor = null;
let batchMatchedEpisodes = null;
let batchTestSubPath = null;
let batchTestVideoPath = null;

let scanPollTimer = null;
let scanFullLog = [];

export default function (view, params) {
    let routeParams = resolveParams(params);

    view.querySelector('#btnBackToConfig').addEventListener('click', () => navigateTo('configurationpage?name=jellysub'));

    view.querySelector('#btnOpSingle').addEventListener('click', () => selectOperation(view, 'single'));
    view.querySelector('#btnOpBatch').addEventListener('click',  () => selectOperation(view, 'batch'));
    view.querySelector('#btnOpScan').addEventListener('click',   () => selectOperation(view, 'scan'));

    view.querySelector('#btnAssisted').addEventListener('click', () => setSingleMode(view, 'assisted'));
    view.querySelector('#btnManual').addEventListener('click',   () => setSingleMode(view, 'manual'));
    view.querySelector('#btnAssistedSearch').addEventListener('click', () => runAssistedSearch(view));
    view.querySelector('#btnManualSearch').addEventListener('click',   () => runManualSearch(view));
    view.querySelector('#manualQuery').addEventListener('keydown', e => {
        if (e.key === 'Enter') runManualSearch(view);
    });
    view.querySelector('#itemIdInput').addEventListener('keydown', e => {
        if (e.key === 'Enter') runAssistedSearch(view);
    });
    view.querySelector('#btnSelectAll').addEventListener('click', () => {
        view.querySelectorAll('.result-checkbox').forEach(cb => { cb.checked = true; });
        updateSingleSelCount(view);
    });
    view.querySelector('#btnDownloadSelected').addEventListener('click', () => downloadSelected(view));

    view.querySelector('#btnBatchModeAuto').addEventListener('click',   () => setBatchMode(view, 'auto'));
    view.querySelector('#btnBatchModeGuided').addEventListener('click', () => setBatchMode(view, 'guided'));
    view.querySelector('#btnBatchAnalyze').addEventListener('click', () => analyzeBatch(view));
    view.querySelector('#btnConfirmDownload').addEventListener('click', () => confirmBatchDownload(view));
    view.querySelector('#btnCopyCmd').addEventListener('click', () => {
        navigator.clipboard.writeText(view.querySelector('#vlcCmd').textContent)
            .then(() => Dashboard.toast('Copied to clipboard'));
    });
    view.querySelector('#btnSyncAfterTest').addEventListener('click', () => {
        view.querySelector('#syncPanel').style.display = 'block';
    });
    view.querySelector('#syncTool').addEventListener('change', e => {
        view.querySelector('#refSubRow').style.display = e.target.value === 'alass' ? 'block' : 'none';
    });
    view.querySelector('#btnRunSync').addEventListener('click', () => runBatchSync(view));

    view.querySelector('#btnStartScan').addEventListener('click', async () => {
        try {
            await ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl(`${API}/scan/trigger`) });
            Dashboard.toast('Library scan started');
            startScanPolling(view);
        } catch (e) {
            Dashboard.processErrorResponse({ statusText: 'Failed to start scan: ' + e });
        }
    });
    view.querySelector('#btnRefreshStatus').addEventListener('click', () => refreshScanStatus(view));
    view.querySelector('#logFilter').addEventListener('change', () => renderScanLog(view));

    view.addEventListener('viewshow', () => {
        routeParams = resolveParams(params);
        hydrateRouteState(view, routeParams);
    });

    view.addEventListener('viewhide', () => {
        stopScanPolling();
    });
}

function hydrateRouteState(view, routeParams) {
    const itemId = routeParams.itemId || '';
    if (itemId) {
        view.querySelector('#itemIdInput').value = itemId;
        view.querySelector('#batchItemId').value = itemId;
        currentItemId = itemId;
    }

    const scanHint = view.querySelector('#scanContextHint');
    if (routeParams.libraryId) {
        scanHint.textContent = `Opened from library ${routeParams.libraryId}. Scan currently runs across JellySub's configured scope.`;
        scanHint.style.display = 'block';
    } else {
        scanHint.textContent = '';
        scanHint.style.display = 'none';
    }

    setSingleMode(view, routeParams.mode === 'manual' ? 'manual' : 'assisted');
    setBatchMode(view, routeParams.batchMode === 'guided' ? 'guided' : 'auto');

    const initialOperation = normalizeOperation(routeParams.op || routeParams.operation || inferOperation(routeParams));
    selectOperation(view, initialOperation, false);

    if (initialOperation === 'single' && itemId) {
        runAssistedSearch(view);
    }

    if (initialOperation === 'scan') {
        refreshScanStatus(view);
    }
}

function inferOperation(routeParams) {
    const pageName = String(routeParams.name || '').toLowerCase();
    if (pageName === 'jellysubseries') return 'batch';
    if (pageName === 'jellysubscan') return 'scan';
    return 'single';
}

function normalizeOperation(op) {
    return ['single', 'batch', 'scan'].includes(op) ? op : 'single';
}

function selectOperation(view, operation, updateUrl = true) {
    const op = normalizeOperation(operation);
    view.querySelector('#singlePanel').style.display = op === 'single' ? 'block' : 'none';
    view.querySelector('#batchPanel').style.display  = op === 'batch' ? 'block' : 'none';
    view.querySelector('#scanPanel').style.display   = op === 'scan' ? 'block' : 'none';

    view.querySelector('#btnOpSingle').classList.toggle('button-alt', op !== 'single');
    view.querySelector('#btnOpBatch').classList.toggle('button-alt', op !== 'batch');
    view.querySelector('#btnOpScan').classList.toggle('button-alt', op !== 'scan');

    if (op === 'scan') {
        refreshScanStatus(view);
    } else {
        stopScanPolling();
    }

    if (updateUrl && window.history?.replaceState) {
        const nextUrl = buildUnifiedUrl(op, view);
        window.history.replaceState(window.history.state, '', nextUrl);
    }
}

function buildUnifiedUrl(operation, view) {
    const params = new URLSearchParams();
    params.set('page', 'configurationpage');
    params.set('name', 'jellysubsearchv4');
    params.set('op', operation);

    if (operation === 'single') {
        const mode = view.querySelector('#manualBar').style.display === 'flex' ? 'manual' : 'assisted';
        params.set('mode', mode);
        const itemId = view.querySelector('#itemIdInput').value.trim() || currentItemId || '';
        if (itemId) params.set('itemId', itemId);
    }

    if (operation === 'batch') {
        const itemId = view.querySelector('#batchItemId').value.trim();
        if (itemId) params.set('itemId', itemId);
        params.set('batchMode', batchCurrentMode);
    }

    return `${window.location.pathname}#!/${params.get('page')}?${params.toString().replace(/^page=configurationpage&/, '')}`;
}

function setSingleMode(view, mode) {
    const isManual = mode === 'manual';
    view.querySelector('#assistedBar').style.display = isManual ? 'none' : 'flex';
    view.querySelector('#manualBar').style.display   = isManual ? 'flex' : 'none';
    view.querySelector('#btnAssisted').classList.toggle('button-alt', isManual);
    view.querySelector('#btnManual').classList.toggle('button-alt', !isManual);
}

async function runAssistedSearch(view) {
    const itemId = view.querySelector('#itemIdInput').value.trim() || currentItemId || '';
    const lang = view.querySelector('#assistedLang').value.trim() || undefined;
    if (!itemId) {
        Dashboard.toast('Enter a Jellyfin item ID');
        return;
    }

    currentItemId = itemId;
    view.querySelector('#itemIdInput').value = itemId;
    await doSearch(view, ApiClient.getUrl(`${API}/search`, { itemId, ...(lang && { languages: lang }) }));
}

async function runManualSearch(view) {
    const query = view.querySelector('#manualQuery').value.trim();
    const season = view.querySelector('#manualSeason').value.trim() || undefined;
    const episode = view.querySelector('#manualEpisode').value.trim() || undefined;
    const lang = view.querySelector('#manualLang').value.trim() || undefined;
    if (!query) {
        Dashboard.toast('Enter a title');
        return;
    }

    const qs = { query, ...(lang && { languages: lang }), ...(season && { season }), ...(episode && { episode }) };
    await doSearch(view, ApiClient.getUrl(`${API}/search/manual`, qs));
}

async function doSearch(view, url) {
    Dashboard.showLoadingMsg();
    hideSingleSearchState(view);
    try {
        let data = await ApiClient.ajax({ type: 'GET', url, dataType: 'json' });
        if (typeof data === 'string') {
            data = JSON.parse(data);
        }
        if (data.error || data.Error) {
            Dashboard.processErrorResponse({ statusText: data.error || data.Error });
            return;
        }
        renderSearchInfo(view, data);
        const rawResults = data.results || data.Results || [];
        singleResults = Array.isArray(rawResults) ? rawResults.map(normalizeResult) : [];
        renderResults(view, singleResults);
    } catch (e) {
        Dashboard.processErrorResponse({ statusText: 'Search failed: ' + e });
    } finally {
        Dashboard.hideLoadingMsg();
    }
}

function renderSearchInfo(view, data) {
    const info = view.querySelector('#itemInfo');
    const title = String(data.searchTitle || data.SearchTitle || '').trim();
    const yearValue = data.searchYear ?? data.SearchYear;
    const year = yearValue ? ` (${yearValue})` : '';

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

        const hashBadge = r.isHashMatch ? '<span class="label" style="background:#4CAF50;color:#000;font-size:10px;border-radius:3px;padding:1px 5px">HASH</span> ' : '';
        const sdBadge = r.isHearingImpaired ? '<span class="label" style="background:#2196F3;color:#fff;font-size:10px;border-radius:3px;padding:1px 5px">SDH</span> ' : '';
        const mtBadge = r.isMachineTranslated ? '<span class="label" style="background:#FF9800;color:#000;font-size:10px;border-radius:3px;padding:1px 5px">MT</span> ' : '';
        const srcBadge = `<span style="font-size:11px;color:#888;border:1px solid rgba(255,255,255,.2);border-radius:3px;padding:1px 5px">${r.sourceName}</span>`;
        const dlCount = r.downloadCount > 0 ? `<span style="font-size:11px;color:#aaa">⬇ ${r.downloadCount.toLocaleString()}</span>` : '';
        const uploader = r.uploader ? `<span style="font-size:11px;color:#aaa">👤 ${r.uploader}</span>` : '';
        const dateStr = r.uploadDate ? new Date(r.uploadDate).toLocaleDateString() : '';

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

        row.querySelector('.result-checkbox').addEventListener('change', () => updateSingleSelCount(view));
        container.appendChild(row);
    });

    updateSingleSelCount(view);
}

async function downloadSelected(view) {
    const selected = [...view.querySelectorAll('.result-checkbox:checked')]
        .map(cb => singleResults[parseInt(cb.dataset.idx, 10)]);

    if (!selected.length) return;
    if (!currentItemId) {
        Dashboard.toast('No item ID — use Assisted mode or set an item ID');
        return;
    }

    const progress = view.querySelector('#downloadProgress');
    const statusEl = view.querySelector('#dlStatus');
    progress.style.display = 'block';

    for (const result of selected) {
        statusEl.textContent = `Downloading: ${result.releaseName} (${result.languageName})…`;
        try {
            const res = await ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl(`${API}/download`),
                data: JSON.stringify({
                    itemId: currentItemId,
                    sourceId: result.sourceId,
                    subtitleId: result.id,
                    downloadUrl: result.downloadUrl,
                    language: result.language,
                    releaseName: result.releaseName,
                    uploader: result.uploader,
                    releaseGroup: result.releaseGroup,
                }),
                contentType: 'application/json',
            });
            statusEl.textContent = res.success
                ? `✓ Saved: ${res.savedPath}`
                : `✗ Failed: ${res.error}`;
        } catch (ex) {
            statusEl.textContent = `✗ Error: ${ex}`;
        }
        await sleep(400);
    }

    statusEl.textContent = `Done. ${selected.length} subtitle(s) processed.`;
}

function hideSingleSearchState(view) {
    view.querySelector('#resultsContainer').style.display = 'none';
    view.querySelector('#noResults').style.display = 'none';
    view.querySelector('#downloadProgress').style.display = 'none';
    view.querySelector('#itemInfo').style.display = 'none';
    view.querySelector('#resultsList').innerHTML = '';
}

function updateSingleSelCount(view) {
    const selectedCount = view.querySelectorAll('.result-checkbox:checked').length;
    const btn = view.querySelector('#btnDownloadSelected');
    view.querySelector('#selCount').textContent = selectedCount;
    btn.disabled = selectedCount === 0;
}

function setBatchMode(view, mode) {
    batchCurrentMode = mode;
    view.querySelector('#btnBatchModeAuto').classList.toggle('button-alt', mode !== 'auto');
    view.querySelector('#btnBatchModeGuided').classList.toggle('button-alt', mode !== 'guided');
    const hint = view.querySelector('#batchModeHint');
    hint.style.display = 'block';
    hint.textContent = mode === 'auto'
        ? 'Auto mode: best subtitle is downloaded silently for every episode.'
        : 'Guided mode: you pick the subtitle for episode 1; the plugin matches the same uploader / release for the rest.';
}

async function analyzeBatch(view) {
    const itemId = view.querySelector('#batchItemId').value.trim();
    const lang = view.querySelector('#batchLang').value.trim() || 'en';
    if (!itemId) {
        Dashboard.toast('Enter a series item ID');
        return;
    }

    Dashboard.showLoadingMsg();
    try {
        const data = await ApiClient.ajax({
            type: 'POST',
            url: ApiClient.getUrl(`${API}/series/analyze`),
            data: JSON.stringify({ itemId, language: lang }),
            contentType: 'application/json',
        });
        batchAnalysisData = data;
        batchMatchedEpisodes = null;
        batchEp1Anchor = null;
        batchTestSubPath = null;
        batchTestVideoPath = null;
        resetBatchProgress(view);
        renderEpisodeList(view, data);

        if (batchCurrentMode === 'auto') {
            await runBatchAutoMatch(view, lang);
        } else {
            await loadBatchEp1Candidates(view, data, lang);
        }
    } catch (e) {
        Dashboard.processErrorResponse({ statusText: 'Analyse failed: ' + e });
    } finally {
        Dashboard.hideLoadingMsg();
    }
}

function resetBatchProgress(view) {
    view.querySelector('#ep1Pane').style.display = 'none';
    view.querySelector('#testSection').style.display = 'none';
    view.querySelector('#syncPanel').style.display = 'none';
    view.querySelector('#confirmSection').style.display = 'none';
    view.querySelector('#batchProgress').style.display = 'none';
    view.querySelector('#progressBar').style.width = '0';
    view.querySelector('#batchStatus').textContent = '';
    view.querySelector('#matchSummary').textContent = '';
    view.querySelector('#syncStatus').textContent = '';
}

async function runBatchAutoMatch(view, lang) {
    view.querySelector('#confirmSection').style.display = 'flex';
    view.querySelector('#matchSummary').textContent =
        `${batchAnalysisData.episodes.filter(e => !e.hasSubtitle).length} episode(s) need subtitles`;
    view.querySelector('#btnConfirmDownload').dataset.mode = 'auto';
    view.querySelector('#btnConfirmDownload').dataset.lang = lang;
}

async function loadBatchEp1Candidates(view, data, lang) {
    const ep1 = data.episodes.find(e => !e.hasSubtitle);
    if (!ep1) {
        view.querySelector('#ep1Pane').style.display = 'none';
        view.querySelector('#confirmSection').style.display = 'flex';
        view.querySelector('#matchSummary').textContent = 'All episodes already have subtitles.';
        return;
    }

    view.querySelector('#ep1Pane').style.display = 'block';
    view.querySelector('#ep1Searching').style.display = 'block';
    view.querySelector('#ep1Results').innerHTML = '';

    const url = ApiClient.getUrl(`${API}/search`, { itemId: ep1.itemId, languages: lang });
    const res = await ApiClient.ajax({ type: 'GET', url });
    view.querySelector('#ep1Searching').style.display = 'none';

    const results = (res.results || res.Results || []).map(normalizeResult);
    renderBatchEp1Candidates(view, results, ep1, lang);
}

function renderBatchEp1Candidates(view, results, ep1, lang) {
    const container = view.querySelector('#ep1Results');
    if (!results.length) {
        container.innerHTML = '<p style="color:#f44336">No subtitles found for episode 1.</p>';
        return;
    }

    results.slice(0, 15).forEach((result, i) => {
        const row = document.createElement('div');
        row.style.cssText = 'display:flex;align-items:center;gap:10px;padding:6px 0;' +
            `border-bottom:1px solid rgba(255,255,255,.06);cursor:pointer;${result.isHashMatch ? 'background:rgba(76,175,80,.05)' : ''}`;
        const badges = [
            result.isHashMatch ? '<span style="background:#4CAF50;color:#000;font-size:10px;border-radius:3px;padding:1px 4px">HASH</span>' : '',
            result.isHearingImpaired ? '<span style="background:#2196F3;color:#fff;font-size:10px;border-radius:3px;padding:1px 4px">SDH</span>' : '',
        ].join(' ');
        row.innerHTML = `
            <input type="radio" name="ep1choice" value="${i}" style="flex-shrink:0" />
            <div style="flex:1;min-width:0">
              <div style="white-space:nowrap;overflow:hidden;text-overflow:ellipsis">${badges} ${escHtml(result.releaseName)}</div>
              <div style="font-size:11px;color:#888">${result.sourceName} · ${result.languageName}
                ${result.downloadCount ? '· ⬇' + result.downloadCount.toLocaleString() : ''}
                ${result.uploader ? '· 👤' + escHtml(result.uploader) : ''}</div>
            </div>`;
        row.addEventListener('click', () => {
            row.querySelector('input').checked = true;
            batchEp1Anchor = result;
            runBatchGuidedMatch(view, ep1, lang);
        });
        container.appendChild(row);
    });
}

async function runBatchGuidedMatch(view, ep1, lang) {
    if (!batchEp1Anchor) return;
    Dashboard.showLoadingMsg();
    try {
        const seriesItemId = view.querySelector('#batchItemId').value.trim();
        const data = await ApiClient.ajax({
            type: 'POST',
            url: ApiClient.getUrl(`${API}/series/match`),
            data: JSON.stringify({ seriesItemId, anchorItemId: ep1.itemId, language: lang, anchor: batchEp1Anchor }),
            contentType: 'application/json',
        });
        batchMatchedEpisodes = data;
        updateEpisodeListWithMatches(view, data);
        await prepareBatchPlayerTest(view, ep1);
        view.querySelector('#testSection').style.display = 'block';
        view.querySelector('#confirmSection').style.display = 'flex';
        const found = data.episodes.filter(e => e.chosenSubtitle).length;
        view.querySelector('#matchSummary').textContent = `${found} / ${data.episodes.length} episodes matched`;
        view.querySelector('#btnConfirmDownload').dataset.mode = 'guided';
        view.querySelector('#btnConfirmDownload').dataset.lang = lang;
    } catch (e) {
        Dashboard.processErrorResponse({ statusText: 'Matching failed: ' + e });
    } finally {
        Dashboard.hideLoadingMsg();
    }
}

async function prepareBatchPlayerTest(view, ep1) {
    if (!batchEp1Anchor) return;
    try {
        const res = await ApiClient.ajax({
            type: 'POST',
            url: ApiClient.getUrl(`${API}/player/test`),
            data: JSON.stringify({
                itemId: ep1.itemId,
                sourceId: batchEp1Anchor.sourceId,
                subtitleId: batchEp1Anchor.id,
                downloadUrl: batchEp1Anchor.downloadUrl,
                language: batchEp1Anchor.language,
                releaseName: batchEp1Anchor.releaseName,
            }),
            contentType: 'application/json',
        });
        batchTestVideoPath = res.videoPath;
        batchTestSubPath = res.subtitlePath;
        view.querySelector('#vlcCmd').textContent = res.vlcCommand;
        view.querySelector('#btnXspf').href = ApiClient.getUrl(`${API}/player/playlist`, {
            videoPath: res.videoPath,
            subtitlePath: res.subtitlePath,
        });
    } catch {
        /* non-critical */
    }
}

async function runBatchSync(view) {
    if (!batchTestVideoPath || !batchTestSubPath) {
        Dashboard.toast('No test subtitle to sync');
        return;
    }
    const toolId = view.querySelector('#syncTool').value;
    const refSub = view.querySelector('#refSubPath').value.trim() || undefined;
    const statusEl = view.querySelector('#syncStatus');
    statusEl.textContent = 'Running sync…';
    try {
        const res = await ApiClient.ajax({
            type: 'POST',
            url: ApiClient.getUrl(`${API}/sync`),
            data: JSON.stringify({
                toolId,
                videoPath: batchTestVideoPath,
                subtitlePath: batchTestSubPath,
                referenceSubtitlePath: refSub,
                outputPath: batchTestSubPath,
            }),
            contentType: 'application/json',
        });
        statusEl.textContent = res.success
            ? '✓ Sync complete: ' + res.outputPath
            : '✗ Sync failed: ' + res.error;
    } catch (e) {
        statusEl.textContent = '✗ Error: ' + e;
    }
}

async function confirmBatchDownload(view) {
    const btn = view.querySelector('#btnConfirmDownload');
    const isAuto = btn.dataset.mode === 'auto';
    const lang = view.querySelector('#batchLang').value.trim() || 'en';

    const episodes = isAuto
        ? batchAnalysisData?.episodes.filter(e => !e.hasSubtitle) || []
        : batchMatchedEpisodes?.episodes.filter(e => e.chosenSubtitle) || [];

    if (!episodes.length) {
        Dashboard.toast('Nothing to download');
        return;
    }

    view.querySelector('#batchProgress').style.display = 'block';
    view.querySelector('#confirmSection').style.display = 'none';
    const progressBar = view.querySelector('#progressBar');
    const statusEl = view.querySelector('#batchStatus');

    let done = 0;
    for (const ep of episodes) {
        statusEl.textContent = `Downloading: ${ep.label}…`;
        try {
            let payload;
            if (isAuto) {
                const searchRes = await ApiClient.ajax({
                    type: 'GET',
                    url: ApiClient.getUrl(`${API}/search`, { itemId: ep.itemId, languages: lang }),
                });
                const rawBest = searchRes.results?.[0] || searchRes.Results?.[0];
                if (!rawBest) {
                    done++;
                    progressBar.style.width = `${Math.round(done / episodes.length * 100)}%`;
                    continue;
                }
                payload = makeBatchDownloadPayload(ep.itemId, normalizeResult(rawBest), lang);
            } else {
                payload = makeBatchDownloadPayload(ep.itemId, ep.chosenSubtitle, lang, ep.mediaPath, ep.label);
            }

            const res = await ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl(`${API}/download`),
                data: JSON.stringify(payload),
                contentType: 'application/json',
            });
            statusEl.textContent = res.success
                ? `✓ ${ep.label}: ${res.savedPath}`
                : `✗ ${ep.label}: ${res.error}`;
        } catch (e) {
            statusEl.textContent = `✗ ${ep.label}: ${e}`;
        }
        done++;
        progressBar.style.width = `${Math.round(done / episodes.length * 100)}%`;
        await sleep(200);
    }

    statusEl.textContent = `Batch complete — ${done} episode(s) processed.`;
}

function renderEpisodeList(view, data) {
    view.querySelector('#episodeSection').style.display = 'block';
    view.querySelector('#seriesName').textContent = data.seriesTitle;
    const list = view.querySelector('#episodeList');
    list.innerHTML = '';

    data.episodes.forEach(ep => {
        const row = document.createElement('div');
        row.id = `eprow-${ep.itemId}`;
        row.style.cssText = 'display:flex;align-items:center;gap:10px;padding:6px 10px;border-bottom:1px solid rgba(255,255,255,.06);font-size:13px';
        const icon = ep.hasSubtitle ? '✓' : '○';
        const color = ep.hasSubtitle ? '#4CAF50' : '#aaa';
        row.innerHTML = `
            <span style="color:${color};width:16px;text-align:center">${icon}</span>
            <span style="flex:1">${escHtml(ep.label)}</span>
            <span id="epmatch-${ep.itemId}" style="font-size:11px;color:#888"></span>`;
        list.appendChild(row);
    });
}

function updateEpisodeListWithMatches(view, data) {
    data.episodes.forEach(ep => {
        const el = view.querySelector(`#epmatch-${ep.itemId}`);
        if (!el) return;
        if (ep.hasSubtitle) {
            el.textContent = 'already has subtitle';
            return;
        }
        if (!ep.chosenSubtitle) {
            el.textContent = '— no match found';
            el.style.color = '#f44336';
            return;
        }
        const method = {
            UploaderMatch: '👤 uploader',
            PatternMatch: '🏷 pattern',
            BestAvailable: '📊 best',
            Manual: '✋ manual',
            NotFound: '✗',
        }[ep.matchMethod] || ep.matchMethod;
        el.textContent = `${method}: ${ep.chosenSubtitle.releaseName.slice(0, 50)}`;
    });
}

function makeBatchDownloadPayload(itemId, sub, language, mediaPath, label) {
    return {
        itemId,
        label,
        sourceId: sub.sourceId,
        subtitleId: sub.id,
        downloadUrl: sub.downloadUrl,
        language,
        releaseName: sub.releaseName,
        uploader: sub.uploader || '',
        releaseGroup: sub.releaseGroup || '',
        mediaPath,
    };
}

function startScanPolling(view) {
    if (scanPollTimer) return;
    scanPollTimer = setInterval(() => refreshScanStatus(view), 3000);
}

function stopScanPolling() {
    if (scanPollTimer) {
        clearInterval(scanPollTimer);
        scanPollTimer = null;
    }
}

async function refreshScanStatus(view) {
    try {
        const data = await ApiClient.ajax({
            type: 'GET',
            url: ApiClient.getUrl(`${API}/scan/status`),
        });

        view.querySelector('#scanRunning').style.display = data.isRunning ? 'inline' : 'none';

        if (data.isRunning) {
            startScanPolling(view);
        } else {
            stopScanPolling();
        }

        scanFullLog = data.log || [];
        updateScanStats(view, scanFullLog);
        renderScanLog(view);
    } catch {
        /* ignore transient errors */
    }
}

function updateScanStats(view, log) {
    if (!log.length) {
        view.querySelector('#statsBar').style.display = 'none';
        view.querySelector('#logSection').style.display = 'none';
        return;
    }
    view.querySelector('#statsBar').style.display = 'flex';
    view.querySelector('#logSection').style.display = 'block';
    view.querySelector('#statDownloaded').textContent = log.filter(e => e.status === 'Downloaded').length;
    view.querySelector('#statNotFound').textContent = log.filter(e => e.status === 'NotFound').length;
    view.querySelector('#statFailed').textContent = log.filter(e => e.status === 'Failed' || e.status === 'Error').length;
    view.querySelector('#statSkipped').textContent = log.filter(e => e.status === 'Skipped').length;
}

function renderScanLog(view) {
    const filter = view.querySelector('#logFilter').value;
    const rows = view.querySelector('#logRows');
    const empty = view.querySelector('#emptyLog');
    const log = filter ? scanFullLog.filter(e => e.status === filter) : scanFullLog;

    rows.innerHTML = '';

    if (!log.length) {
        empty.style.display = 'block';
        return;
    }
    empty.style.display = 'none';

    log.slice().reverse().forEach((entry, i) => {
        const row = document.createElement('div');
        row.style.cssText = 'display:grid;grid-template-columns:1fr 60px 100px auto;gap:0;' +
            `padding:6px 12px;font-size:12px;${i % 2 === 0 ? 'background:rgba(255,255,255,.03)' : ''}`;

        const statusColor = {
            Downloaded: '#4CAF50',
            NotFound: '#aaa',
            Failed: '#f44336',
            Error: '#f44336',
            Skipped: '#FF9800',
        }[entry.status] || '#aaa';

        const detail = entry.savedPath
            ? `<span title="${escHtml(entry.savedPath)}" style="color:#888;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;display:block;max-width:260px">${escHtml(entry.savedPath.split('/').pop())}</span>`
            : entry.error
            ? `<span title="${escHtml(entry.error)}" style="color:#f44336">${escHtml(entry.error.slice(0, 60))}</span>`
            : '';

        row.innerHTML = `
            <span style="white-space:nowrap;overflow:hidden;text-overflow:ellipsis" title="${escHtml(entry.itemTitle)}">${escHtml(entry.itemTitle)}</span>
            <span>${entry.language.toUpperCase()}</span>
            <span style="color:${statusColor}">${entry.status}</span>
            <span>${detail}</span>`;
        rows.appendChild(row);
    });
}

function navigateTo(url) {
    if (Dashboard.navigate) {
        Dashboard.navigate(url);
        return;
    }

    window.location.href = '/' + url;
}

function resolveParams(params) {
    const resolved = { ...(params || {}) };
    const querySources = [window.location.search || '', window.location.hash || ''];

    for (const source of querySources) {
        const query = source.includes('?') ? source.slice(source.indexOf('?') + 1) : source.replace(/^\?/, '');
        if (!query) continue;

        const parsed = new URLSearchParams(query);
        for (const [key, value] of parsed.entries()) {
            if (resolved[key] === undefined || resolved[key] === null || resolved[key] === '') {
                resolved[key] = value;
            }
        }
    }

    return resolved;
}

function normalizeResult(r) {
    return {
        id: r.id ?? r.Id ?? '',
        sourceId: r.sourceId ?? r.SourceId ?? '',
        sourceName: r.sourceName ?? r.SourceName ?? '',
        releaseName: r.releaseName ?? r.ReleaseName ?? '',
        language: r.language ?? r.Language ?? '',
        languageName: r.languageName ?? r.LanguageName ?? '',
        downloadCount: r.downloadCount ?? r.DownloadCount ?? 0,
        uploader: r.uploader ?? r.Uploader ?? '',
        uploadDate: r.uploadDate ?? r.UploadDate ?? null,
        isHashMatch: r.isHashMatch ?? r.IsHashMatch ?? false,
        isHearingImpaired: r.isHearingImpaired ?? r.IsHearingImpaired ?? false,
        isMachineTranslated: r.isMachineTranslated ?? r.IsMachineTranslated ?? false,
        releaseGroup: r.releaseGroup ?? r.ReleaseGroup ?? '',
        downloadUrl: r.downloadUrl ?? r.DownloadUrl ?? '',
    };
}

const escHtml = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const sleep = ms => new Promise(r => setTimeout(r, ms));
