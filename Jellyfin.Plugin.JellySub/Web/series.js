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
            data: JSON.stringify({ ItemId: itemId, Language: lang }),
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
        `${analysisData.Episodes.filter(e => !e.HasSubtitle).length} episode(s) need subtitles`;
    view.querySelector('#btnConfirmDownload').dataset.mode     = 'auto';
    view.querySelector('#btnConfirmDownload').dataset.seriesId = view.querySelector('#seriesItemId').value.trim();
    view.querySelector('#btnConfirmDownload').dataset.lang     = lang;
}

// ── Guided mode ───────────────────────────────────────────────────────────────

async function loadEp1Candidates(view, data, lang) {
    const ep1 = data.Episodes.find(e => !e.HasSubtitle);
    if (!ep1) {
        view.querySelector('#ep1Pane').style.display = 'none';
        view.querySelector('#confirmSection').style.display = 'flex';
        view.querySelector('#matchSummary').textContent = 'All episodes already have subtitles.';
        return;
    }

    view.querySelector('#ep1Pane').style.display = 'block';
    view.querySelector('#ep1Searching').style.display = 'block';
    view.querySelector('#ep1Results').innerHTML = '';

    const url = ApiClient.getUrl(`${API}/search`, { itemId: ep1.ItemId, languages: lang });
    const res = await ApiClient.ajax({ type: 'GET', url });
    view.querySelector('#ep1Searching').style.display = 'none';

    const results = res.Results || [];
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
            `border-bottom:1px solid rgba(255,255,255,.06);cursor:pointer;${r.IsHashMatch ? 'background:rgba(76,175,80,.05)' : ''}`;
        const badges = [
            r.IsHashMatch       ? '<span style="background:#4CAF50;color:#000;font-size:10px;border-radius:3px;padding:1px 4px">HASH</span>' : '',
            r.IsHearingImpaired ? '<span style="background:#2196F3;color:#fff;font-size:10px;border-radius:3px;padding:1px 4px">SDH</span>'  : '',
        ].join(' ');
        row.innerHTML = `
            <input type="radio" name="ep1choice" value="${i}" style="flex-shrink:0" />
            <div style="flex:1;min-width:0">
              <div style="white-space:nowrap;overflow:hidden;text-overflow:ellipsis">${badges} ${escHtml(r.ReleaseName)}</div>
              <div style="font-size:11px;color:#888">${r.SourceName} · ${r.LanguageName}
                ${r.DownloadCount ? '· ⬇' + r.DownloadCount.toLocaleString() : ''}
                ${r.Uploader ? '· 👤' + escHtml(r.Uploader) : ''}</div>
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
            data: JSON.stringify({ SeriesItemId: seriesItemId, AnchorItemId: ep1.ItemId, Language: lang, Anchor: ep1Anchor }),
            contentType: 'application/json',
        });
        matchedEpisodes = data;
        updateEpisodeListWithMatches(view, data);
        // Show player test controls
        await preparePlayerTest(view, ep1, lang);
        view.querySelector('#testSection').style.display = 'block';
        view.querySelector('#confirmSection').style.display = 'flex';
        const found = data.Episodes.filter(e => e.ChosenSubtitle).length;
        view.querySelector('#matchSummary').textContent =
            `${found} / ${data.Episodes.length} episodes matched`;
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
                ItemId:      ep1.ItemId,
                SourceId:    ep1Anchor.SourceId,
                SubtitleId:  ep1Anchor.Id,
                DownloadUrl: ep1Anchor.DownloadUrl,
                Language:    ep1Anchor.Language,
                ReleaseName: ep1Anchor.ReleaseName,
            }),
            contentType: 'application/json',
        });
        testVideoPath = res.VideoPath;
        testSubPath   = res.SubtitlePath;
        view.querySelector('#vlcCmd').textContent = res.VlcCommand;
        // XSPF download link
        const xspfUrl = ApiClient.getUrl(`${API}/player/playlist`, {
            videoPath:    res.VideoPath,
            subtitlePath: res.SubtitlePath,
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
                ToolId:               toolId,
                VideoPath:            testVideoPath,
                SubtitlePath:         testSubPath,
                ReferenceSubtitlePath: refSub,
                OutputPath:           testSubPath, // overwrite
            }),
            contentType: 'application/json',
        });
        statusEl.textContent = res.Success
            ? '✓ Sync complete: ' + res.OutputPath
            : '✗ Sync failed: ' + res.Error;
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
        ? analysisData?.Episodes.filter(e => !e.HasSubtitle) || []
        : matchedEpisodes?.Episodes.filter(e => e.ChosenSubtitle) || [];

    if (!episodes.length) { Dashboard.toast('Nothing to download'); return; }

    view.querySelector('#batchProgress').style.display = 'block';
    view.querySelector('#confirmSection').style.display = 'none';
    const progressBar = view.querySelector('#progressBar');
    const statusEl    = view.querySelector('#batchStatus');

    let done = 0;
    for (const ep of episodes) {
        statusEl.textContent = `Downloading: ${ep.Label}…`;
        try {
            let payload;
            if (isAuto) {
                // Search first, pick best
                const searchRes = await ApiClient.ajax({
                    type: 'GET',
                    url: ApiClient.getUrl(`${API}/search`, { itemId: ep.ItemId, languages: lang }),
                });
                const best = searchRes.Results?.[0];
                if (!best) { done++; continue; }
                payload = makeDownloadPayload(ep.ItemId, best, lang);
            } else {
                const sub = ep.ChosenSubtitle;
                payload = makeDownloadPayload(ep.ItemId, sub, lang, ep.MediaPath, ep.Label);
            }

            const res = await ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl(`${API}/download`),
                data: JSON.stringify(payload),
                contentType: 'application/json',
            });
            statusEl.textContent = res.Success
                ? `✓ ${ep.Label}: ${res.SavedPath}`
                : `✗ ${ep.Label}: ${res.Error}`;
        } catch (e) {
            statusEl.textContent = `✗ ${ep.Label}: ${e}`;
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
    view.querySelector('#seriesName').textContent       = data.SeriesTitle;
    const list = view.querySelector('#episodeList');
    list.innerHTML = '';

    data.Episodes.forEach(ep => {
        const row = document.createElement('div');
        row.id = `eprow-${ep.ItemId}`;
        row.style.cssText = 'display:flex;align-items:center;gap:10px;padding:6px 10px;' +
            'border-bottom:1px solid rgba(255,255,255,.06);font-size:13px';
        const icon = ep.HasSubtitle ? '✓' : '○';
        const color = ep.HasSubtitle ? '#4CAF50' : '#aaa';
        row.innerHTML = `
            <span style="color:${color};width:16px;text-align:center">${icon}</span>
            <span style="flex:1">${escHtml(ep.Label)}</span>
            <span id="epmatch-${ep.ItemId}" style="font-size:11px;color:#888"></span>`;
        list.appendChild(row);
    });
}

function updateEpisodeListWithMatches(view, data) {
    data.Episodes.forEach(ep => {
        const el = view.querySelector(`#epmatch-${ep.ItemId}`);
        if (!el) return;
        if (ep.HasSubtitle) { el.textContent = 'already has subtitle'; return; }
        if (!ep.ChosenSubtitle) { el.textContent = '— no match found'; el.style.color = '#f44336'; return; }
        const method = { UploaderMatch: '👤 uploader', PatternMatch: '🏷 pattern', BestAvailable: '📊 best', Manual: '✋ manual', NotFound: '✗' }[ep.MatchMethod] || ep.MatchMethod;
        el.textContent = `${method}: ${ep.ChosenSubtitle.ReleaseName.slice(0, 50)}`;
    });
}

function makeDownloadPayload(itemId, sub, language, mediaPath, label) {
    return {
        ItemId:       itemId,
        Label:        label,
        SourceId:     sub.SourceId,
        SubtitleId:   sub.Id,
        DownloadUrl:  sub.DownloadUrl,
        Language:     language,
        ReleaseName:  sub.ReleaseName,
        Uploader:     sub.Uploader     || '',
        ReleaseGroup: sub.ReleaseGroup || '',
        MediaPath:    mediaPath,
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
