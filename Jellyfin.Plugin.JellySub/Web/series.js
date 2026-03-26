/* JellySub – series.js */
const API = '/JellySub';
let currentMode     = 'auto';    // 'auto' | 'guided'
let analysisData    = null;      // SeriesAnalysisDto
let ep1Anchor       = null;      // SubtitleResultDto chosen for ep1
let matchedEpisodes = null;      // SeriesAnalysisDto after /series/match
let testSubPath     = null;      // path of temp subtitle downloaded for testing
let testVideoPath   = null;

export default function (view, params) {
    view.querySelector('#btnBackToConfig').addEventListener('click', () => navigateTo('configurationpage?name=jellysub'));
    if (params.itemId) view.querySelector('#seriesItemId').value = params.itemId;

    // Mode buttons
    view.querySelector('#btnModeAuto').addEventListener('click',   () => setMode(view, 'auto'));
    view.querySelector('#btnModeGuided').addEventListener('click', () => setMode(view, 'guided'));
    setMode(view, 'auto');

    // Analyse
    view.querySelector('#btnAnalyze').addEventListener('click', () => analyzeClick(view));

    // Confirm download
    view.querySelector('#btnConfirmDownload').addEventListener('click', () => confirmDownload(view));

    // Test / sync controls
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
    view.querySelector('#btnRunSync').addEventListener('click', () => runSync(view));
}

// ── Mode ──────────────────────────────────────────────────────────────────────

function setMode(view, mode) {
    currentMode = mode;
    view.querySelector('#btnModeAuto').classList.toggle('button-alt', mode !== 'auto');
    view.querySelector('#btnModeGuided').classList.toggle('button-alt', mode !== 'guided');
    const hint = view.querySelector('#modeHint');
    hint.style.display = 'block';
    hint.textContent = mode === 'auto'
        ? 'Auto mode: best subtitle is downloaded silently for every episode.'
        : 'Guided mode: you pick the subtitle for episode 1; the plugin matches the same uploader / release for the rest.';
}

// ── Analyse ───────────────────────────────────────────────────────────────────

async function analyzeClick(view) {
    const itemId = view.querySelector('#seriesItemId').value.trim();
    const lang   = view.querySelector('#seriesLang').value.trim() || 'en';
    if (!itemId) { Dashboard.toast('Enter a series item ID'); return; }

    Dashboard.showLoadingMsg();
    try {
        const data = await ApiClient.ajax({
            type: 'POST',
            url: ApiClient.getUrl(`${API}/series/analyze`),
            data: JSON.stringify({ itemId, language: lang }),
            contentType: 'application/json',
        });
        analysisData = data;
        renderEpisodeList(view, data, lang);

        if (currentMode === 'auto') {
            await runAutoMatch(view, itemId, lang);
        } else {
            await loadEp1Candidates(view, data, lang);
        }
    } catch (e) {
        Dashboard.processErrorResponse({ statusText: 'Analyse failed: ' + e });
    } finally {
        Dashboard.hideLoadingMsg();
    }
}

// ── Auto mode ─────────────────────────────────────────────────────────────────

async function runAutoMatch(view, seriesItemId, lang) {
    // For auto mode, just show the confirm button immediately
    // Downloads happen when user confirms
    view.querySelector('#confirmSection').style.display = 'flex';
    view.querySelector('#matchSummary').textContent =
        `${analysisData.episodes.filter(e => !e.hasSubtitle).length} episode(s) need subtitles`;
    view.querySelector('#btnConfirmDownload').dataset.mode     = 'auto';
    view.querySelector('#btnConfirmDownload').dataset.seriesId = view.querySelector('#seriesItemId').value.trim();
    view.querySelector('#btnConfirmDownload').dataset.lang     = lang;
}

// ── Guided mode ───────────────────────────────────────────────────────────────

async function loadEp1Candidates(view, data, lang) {
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

    const results = res.results || [];
    renderEp1Candidates(view, results, ep1, lang);
}

function renderEp1Candidates(view, results, ep1, lang) {
    const container = view.querySelector('#ep1Results');
    if (!results.length) {
        container.innerHTML = '<p style="color:#f44336">No subtitles found for episode 1.</p>';
        return;
    }

    results.slice(0, 15).forEach((r, i) => {
        const row = document.createElement('div');
        row.style.cssText = 'display:flex;align-items:center;gap:10px;padding:6px 0;' +
            `border-bottom:1px solid rgba(255,255,255,.06);cursor:pointer;${r.isHashMatch ? 'background:rgba(76,175,80,.05)' : ''}`;
        const badges = [
            r.isHashMatch       ? '<span style="background:#4CAF50;color:#000;font-size:10px;border-radius:3px;padding:1px 4px">HASH</span>' : '',
            r.isHearingImpaired ? '<span style="background:#2196F3;color:#fff;font-size:10px;border-radius:3px;padding:1px 4px">SDH</span>'  : '',
        ].join(' ');
        row.innerHTML = `
            <input type="radio" name="ep1choice" value="${i}" style="flex-shrink:0" />
            <div style="flex:1;min-width:0">
              <div style="white-space:nowrap;overflow:hidden;text-overflow:ellipsis">${badges} ${escHtml(r.releaseName)}</div>
              <div style="font-size:11px;color:#888">${r.sourceName} · ${r.languageName}
                ${r.downloadCount ? '· ⬇' + r.downloadCount.toLocaleString() : ''}
                ${r.uploader ? '· 👤' + escHtml(r.uploader) : ''}</div>
            </div>`;
        row.addEventListener('click', () => {
            row.querySelector('input').checked = true;
            ep1Anchor = r;
            runGuidedMatch(view, ep1, lang);
        });
        container.appendChild(row);
    });
}

async function runGuidedMatch(view, ep1, lang) {
    if (!ep1Anchor) return;
    Dashboard.showLoadingMsg();
    try {
        const seriesItemId = view.querySelector('#seriesItemId').value.trim();
        const data = await ApiClient.ajax({
            type: 'POST',
            url: ApiClient.getUrl(`${API}/series/match`),
            data: JSON.stringify({ seriesItemId, anchorItemId: ep1.itemId, language: lang, anchor: ep1Anchor }),
            contentType: 'application/json',
        });
        matchedEpisodes = data;
        updateEpisodeListWithMatches(view, data);
        // Show player test controls
        await preparePlayerTest(view, ep1, lang);
        view.querySelector('#testSection').style.display = 'block';
        view.querySelector('#confirmSection').style.display = 'flex';
        const found = data.episodes.filter(e => e.chosenSubtitle).length;
        view.querySelector('#matchSummary').textContent =
            `${found} / ${data.episodes.length} episodes matched`;
    } catch (e) {
        Dashboard.processErrorResponse({ statusText: 'Matching failed: ' + e });
    } finally {
        Dashboard.hideLoadingMsg();
    }
}

// ── Player test ───────────────────────────────────────────────────────────────

async function preparePlayerTest(view, ep1, lang) {
    if (!ep1Anchor) return;
    try {
        const res = await ApiClient.ajax({
            type: 'POST',
            url: ApiClient.getUrl(`${API}/player/test`),
            data: JSON.stringify({
                itemId:      ep1.itemId,
                sourceId:    ep1Anchor.sourceId,
                subtitleId:  ep1Anchor.id,
                downloadUrl: ep1Anchor.downloadUrl,
                language:    ep1Anchor.language,
                releaseName: ep1Anchor.releaseName,
            }),
            contentType: 'application/json',
        });
        testVideoPath = res.videoPath;
        testSubPath   = res.subtitlePath;
        view.querySelector('#vlcCmd').textContent = res.vlcCommand;
        // XSPF download link
        const xspfUrl = ApiClient.getUrl(`${API}/player/playlist`, {
            videoPath:    res.videoPath,
            subtitlePath: res.subtitlePath,
        });
        view.querySelector('#btnXspf').href = xspfUrl;
    } catch { /* non-critical */ }
}

// ── Sync ──────────────────────────────────────────────────────────────────────

async function runSync(view) {
    if (!testVideoPath || !testSubPath) {
        Dashboard.toast('No test subtitle to sync'); return;
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
                videoPath:            testVideoPath,
                subtitlePath:         testSubPath,
                referenceSubtitlePath: refSub,
                outputPath:           testSubPath, // overwrite
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

// ── Confirm download ──────────────────────────────────────────────────────────

async function confirmDownload(view) {
    const btn     = view.querySelector('#btnConfirmDownload');
    const isAuto  = btn.dataset.mode === 'auto';
    const lang    = view.querySelector('#seriesLang').value.trim() || 'en';
    const itemId  = view.querySelector('#seriesItemId').value.trim();

    const episodes = isAuto
        ? analysisData?.episodes.filter(e => !e.hasSubtitle) || []
        : matchedEpisodes?.episodes.filter(e => e.chosenSubtitle) || [];

    if (!episodes.length) { Dashboard.toast('Nothing to download'); return; }

    view.querySelector('#batchProgress').style.display = 'block';
    view.querySelector('#confirmSection').style.display = 'none';
    const progressBar = view.querySelector('#progressBar');
    const statusEl    = view.querySelector('#batchStatus');

    let done = 0;
    for (const ep of episodes) {
        statusEl.textContent = `Downloading: ${ep.label}…`;
        try {
            let payload;
            if (isAuto) {
                // Search first, pick best
                const searchRes = await ApiClient.ajax({
                    type: 'GET',
                    url: ApiClient.getUrl(`${API}/search`, { itemId: ep.itemId, languages: lang }),
                });
                const best = searchRes.results?.[0];
                if (!best) { done++; continue; }
                payload = makeDownloadPayload(ep.itemId, best, lang);
            } else {
                const sub = ep.chosenSubtitle;
                payload = makeDownloadPayload(ep.itemId, sub, lang, ep.mediaPath, ep.label);
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

// ── Rendering helpers ─────────────────────────────────────────────────────────

function renderEpisodeList(view, data, lang) {
    view.querySelector('#episodeSection').style.display = 'block';
    view.querySelector('#seriesName').textContent       = data.seriesTitle;
    const list = view.querySelector('#episodeList');
    list.innerHTML = '';

    data.episodes.forEach(ep => {
        const row = document.createElement('div');
        row.id = `eprow-${ep.itemId}`;
        row.style.cssText = 'display:flex;align-items:center;gap:10px;padding:6px 10px;' +
            'border-bottom:1px solid rgba(255,255,255,.06);font-size:13px';
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
        if (ep.hasSubtitle) { el.textContent = 'already has subtitle'; return; }
        if (!ep.chosenSubtitle) { el.textContent = '— no match found'; el.style.color = '#f44336'; return; }
        const method = { UploaderMatch: '👤 uploader', PatternMatch: '🏷 pattern', BestAvailable: '📊 best', Manual: '✋ manual', NotFound: '✗' }[ep.matchMethod] || ep.matchMethod;
        el.textContent = `${method}: ${ep.chosenSubtitle.releaseName.slice(0, 50)}`;
    });
}

function makeDownloadPayload(itemId, sub, language, mediaPath, label) {
    return {
        itemId,
        label,
        sourceId:     sub.sourceId,
        subtitleId:   sub.id,
        downloadUrl:  sub.downloadUrl,
        language,
        releaseName:  sub.releaseName,
        uploader:     sub.uploader     || '',
        releaseGroup: sub.releaseGroup || '',
        mediaPath,
    };
}

function navigateTo(url) {
    if (Dashboard.navigate) {
        Dashboard.navigate(url);
        return;
    }

    window.location.href = '/' + url;
}

const escHtml = s => String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const sleep   = ms => new Promise(r => setTimeout(r, ms));
