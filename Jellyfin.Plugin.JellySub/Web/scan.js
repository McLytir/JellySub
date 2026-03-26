/* JellySub – scan.js */
const API = '/JellySub';
let pollTimer = null;
let fullLog   = [];

export default function (view) {

    view.querySelector('#btnBackToConfig').addEventListener('click', () => navigateTo('configurationpage?name=jellysub'));

    view.addEventListener('viewshow', () => {
        refreshStatus(view);
    });

    view.addEventListener('viewhide', () => {
        if (pollTimer) clearInterval(pollTimer);
    });

    view.querySelector('#btnStartScan').addEventListener('click', async () => {
        try {
            await ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl(`${API}/scan/trigger`) });
            Dashboard.toast('Library scan started');
            startPolling(view);
        } catch (e) {
            Dashboard.processErrorResponse({ statusText: 'Failed to start scan: ' + e });
        }
    });

    view.querySelector('#btnRefreshStatus').addEventListener('click', () => refreshStatus(view));

    view.querySelector('#logFilter').addEventListener('change', () => renderLog(view));
}

// ── Poll / refresh ────────────────────────────────────────────────────────────

function startPolling(view) {
    if (pollTimer) return;
    pollTimer = setInterval(() => refreshStatus(view), 3000);
}

async function refreshStatus(view) {
    try {
        const data = await ApiClient.ajax({
            type: 'GET',
            url: ApiClient.getUrl(`${API}/scan/status`),
        });

        // Running indicator
        view.querySelector('#scanRunning').style.display = data.isRunning ? 'inline' : 'none';

        if (data.isRunning) {
            startPolling(view);
        } else {
            if (pollTimer) {
                clearInterval(pollTimer);
                pollTimer = null;
            }
        }

        fullLog = data.log || [];
        updateStats(view, fullLog);
        renderLog(view);

    } catch { /* ignore transient errors */ }
}

// ── Stats ─────────────────────────────────────────────────────────────────────

function updateStats(view, log) {
    if (!log.length) return;
    view.querySelector('#statsBar').style.display    = 'flex';
    view.querySelector('#logSection').style.display  = 'block';
    view.querySelector('#statDownloaded').textContent = log.filter(e => e.status === 'Downloaded').length;
    view.querySelector('#statNotFound').textContent   = log.filter(e => e.status === 'NotFound').length;
    view.querySelector('#statFailed').textContent     = log.filter(e => e.status === 'Failed' || e.status === 'Error').length;
    view.querySelector('#statSkipped').textContent    = log.filter(e => e.status === 'Skipped').length;
}

// ── Log table ─────────────────────────────────────────────────────────────────

function renderLog(view) {
    const filter = view.querySelector('#logFilter').value;
    const rows   = view.querySelector('#logRows');
    const empty  = view.querySelector('#emptyLog');
    const log    = filter ? fullLog.filter(e => e.status === filter) : fullLog;

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
            NotFound:   '#aaa',
            Failed:     '#f44336',
            Error:      '#f44336',
            Skipped:    '#FF9800',
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

const escHtml = s => String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
