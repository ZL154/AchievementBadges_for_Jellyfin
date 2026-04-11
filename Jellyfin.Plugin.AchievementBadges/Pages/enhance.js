(function () {
    if (window.__abEnhanceLoaded) return;
    window.__abEnhanceLoaded = true;

    var TOAST_ID = 'ab-toast-container';
    var HOME_ID = 'ab-home-widget';
    var DETAIL_ID = 'ab-detail-ribbon';
    var LAST_SEEN_KEY = 'ab-last-unlock-seen';
    var features = { EnableUnlockToasts: true, EnableHomeWidget: true, EnableItemDetailRibbon: true };

    function getApi() { return window.ApiClient || window.apiClient || null; }

    function getUserId() {
        var api = getApi(); if (!api) return '';
        try {
            if (typeof api.getCurrentUserId === 'function') { var id = api.getCurrentUserId(); if (id) return id; }
            if (api._serverInfo && api._serverInfo.UserId) return api._serverInfo.UserId;
        } catch (e) { }
        return '';
    }

    function buildUrl(p) {
        var api = getApi(); var c = p.replace(/^\/+/, '');
        return (api && typeof api.getUrl === 'function') ? api.getUrl(c) : '/' + c;
    }

    function authHeaders() {
        var h = { 'Content-Type': 'application/json' };
        var api = getApi(); if (!api) return h;
        try {
            if (typeof api.accessToken === 'function') { var t = api.accessToken(); if (t) h['X-Emby-Token'] = t; }
            else if (api._serverInfo && api._serverInfo.AccessToken) h['X-Emby-Token'] = api._serverInfo.AccessToken;
        } catch (e) { }
        return h;
    }

    function fetchJson(path) {
        return fetch(buildUrl(path), { headers: authHeaders(), credentials: 'include' })
            .then(function (r) { return r.ok ? r.json() : Promise.reject(r.statusText); });
    }

    function ensureToastContainer() {
        var c = document.getElementById(TOAST_ID);
        if (c) return c;
        c = document.createElement('div');
        c.id = TOAST_ID;
        c.style.cssText = 'position:fixed;top:16px;right:16px;z-index:99999;display:flex;flex-direction:column;gap:8px;pointer-events:none;';
        document.body.appendChild(c);
        return c;
    }

    var rarityColor = {
        common: '#9aa5b1', uncommon: '#4caf50', rare: '#2196f3',
        epic: '#9c27b0', legendary: '#ff9800', mythic: '#f44336'
    };

    function showToast(badge) {
        var c = ensureToastContainer();
        var color = rarityColor[(badge.Rarity || '').toLowerCase()] || '#9aa5b1';
        var toast = document.createElement('div');
        toast.style.cssText =
            'pointer-events:auto;min-width:280px;max-width:360px;padding:14px 16px;border-radius:10px;' +
            'background:linear-gradient(135deg,rgba(30,35,50,0.96),rgba(15,18,28,0.96));' +
            'border:1px solid ' + color + ';color:#fff;box-shadow:0 8px 24px rgba(0,0,0,0.5);' +
            'font-family:system-ui,sans-serif;animation:abSlideIn 0.35s ease-out;';
        toast.innerHTML =
            '<div style="display:flex;align-items:center;gap:10px;">' +
                '<div style="width:36px;height:36px;border-radius:50%;background:' + color + ';display:flex;align-items:center;justify-content:center;font-size:18px;">🏆</div>' +
                '<div style="flex:1;min-width:0;">' +
                    '<div style="font-size:10px;text-transform:uppercase;letter-spacing:1px;opacity:0.7;">Achievement unlocked</div>' +
                    '<div style="font-size:15px;font-weight:700;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">' + escape(badge.Title || '') + '</div>' +
                    '<div style="font-size:11px;color:' + color + ';font-weight:600;">' + (badge.Rarity || '') + '</div>' +
                '</div>' +
            '</div>';
        c.appendChild(toast);
        setTimeout(function () {
            toast.style.transition = 'opacity 0.4s, transform 0.4s';
            toast.style.opacity = '0';
            toast.style.transform = 'translateX(20px)';
            setTimeout(function () { toast.remove(); }, 450);
        }, 6000);
    }

    function escape(s) { var d = document.createElement('div'); d.textContent = String(s || ''); return d.innerHTML; }

    function pollUnlocks() {
        if (!features.EnableUnlockToasts) return;
        var uid = getUserId(); if (!uid) return;
        var since = localStorage.getItem(LAST_SEEN_KEY) || new Date(Date.now() - 60000).toISOString();
        fetchJson('Plugins/AchievementBadges/users/' + uid + '/unlocks-since?since=' + encodeURIComponent(since))
            .then(function (res) {
                if (res && res.Badges) {
                    res.Badges.forEach(function (b) { showToast(b); });
                }
                if (res && res.Now) { localStorage.setItem(LAST_SEEN_KEY, res.Now); }
            })
            .catch(function () { });
    }

    // ---------- Home widget -----------------------------------------

    function injectHomeWidget() {
        if (!features.EnableHomeWidget) return;
        if (document.getElementById(HOME_ID)) return;
        if (!/#!?\/home/.test(window.location.hash) && !/#!?\/?/.test(window.location.hash)) return;

        var homeSections = document.querySelector('.homeSectionsContainer') || document.querySelector('.pageTabContent .section0') || document.querySelector('.homePage');
        if (!homeSections) return;

        var uid = getUserId(); if (!uid) return;

        var widget = document.createElement('div');
        widget.id = HOME_ID;
        widget.className = 'homePage';
        widget.style.cssText = 'margin:1em 0;padding:1.25em;border-radius:12px;background:linear-gradient(135deg,rgba(30,35,50,0.8),rgba(15,18,28,0.8));border:1px solid rgba(255,255,255,0.08);';
        widget.innerHTML = '<div style="display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:1em;"><div><div style="font-size:0.75em;text-transform:uppercase;letter-spacing:1px;opacity:0.6;">Your achievements</div><div id="ab-home-title" style="font-size:1.3em;font-weight:700;margin-top:0.25em;">Loading...</div><div id="ab-home-sub" style="font-size:0.85em;opacity:0.75;margin-top:0.25em;"></div></div><a href="#!/achievements" style="padding:0.5em 1em;border-radius:999px;background:rgba(255,255,255,0.1);color:#fff;text-decoration:none;font-size:0.85em;font-weight:600;">View all →</a></div><div id="ab-home-next" style="margin-top:1em;display:flex;gap:0.5em;flex-wrap:wrap;"></div>';
        homeSections.insertBefore(widget, homeSections.firstChild);

        Promise.all([
            fetchJson('Plugins/AchievementBadges/users/' + uid + '/rank'),
            fetchJson('Plugins/AchievementBadges/users/' + uid + '/next-badges?limit=3')
        ]).then(function (r) {
            var rank = r[0], next = r[1];
            var title = document.getElementById('ab-home-title');
            var sub = document.getElementById('ab-home-sub');
            var list = document.getElementById('ab-home-next');
            if (title && rank && rank.Tier) {
                title.textContent = rank.Tier.Name + ' · ' + rank.Score + ' pts';
                title.style.color = rank.Tier.Color || '#fff';
            }
            if (sub && rank && rank.NextTier) {
                sub.textContent = (rank.NextTier.MinScore - rank.Score) + ' points to ' + rank.NextTier.Name;
            } else if (sub) {
                sub.textContent = 'Max rank reached!';
            }
            if (list && next && next.length) {
                list.innerHTML = next.map(function (b) {
                    var pct = b.TargetValue > 0 ? Math.round(100 * b.CurrentValue / b.TargetValue) : 0;
                    return '<div style="flex:1;min-width:200px;padding:0.75em;border-radius:8px;background:rgba(255,255,255,0.05);">' +
                        '<div style="font-weight:600;font-size:0.9em;">' + escape(b.Title) + '</div>' +
                        '<div style="font-size:0.75em;opacity:0.7;margin:0.25em 0;">' + escape(b.Description) + '</div>' +
                        '<div style="height:4px;border-radius:2px;background:rgba(255,255,255,0.1);overflow:hidden;"><div style="height:100%;width:' + pct + '%;background:#667eea;"></div></div>' +
                        '<div style="font-size:0.7em;opacity:0.6;margin-top:0.25em;">' + b.CurrentValue + ' / ' + b.TargetValue + '</div>' +
                    '</div>';
                }).join('');
            }
        }).catch(function () { });
    }

    // ---------- Item detail ribbon ----------------------------------

    function injectItemRibbon() {
        if (!features.EnableItemDetailRibbon) return;
        if (document.getElementById(DETAIL_ID)) return;
        if (!/#!?\/details/.test(window.location.hash)) return;

        var anchor = document.querySelector('.detailPagePrimaryContent') || document.querySelector('.itemDetailPage .detailSectionContent');
        if (!anchor) return;

        var uid = getUserId(); if (!uid) return;

        var ribbon = document.createElement('div');
        ribbon.id = DETAIL_ID;
        ribbon.style.cssText = 'margin:0.75em 0;padding:0.75em 1em;border-radius:8px;background:rgba(255,255,255,0.05);border:1px solid rgba(255,255,255,0.08);font-size:0.85em;display:flex;align-items:center;gap:0.75em;';
        ribbon.innerHTML = '<span style="font-size:1.2em;">🏆</span><span id="ab-ribbon-text">Loading achievement progress...</span>';
        anchor.insertBefore(ribbon, anchor.firstChild);

        fetchJson('Plugins/AchievementBadges/users/' + uid + '/summary').then(function (s) {
            var t = document.getElementById('ab-ribbon-text');
            if (t && s) {
                t.textContent = s.Unlocked + ' / ' + s.Total + ' achievements unlocked (' + (s.Percentage || 0) + '%) · Score ' + (s.Score || 0);
            }
        }).catch(function () { });
    }

    // ---------- Bootstrap -------------------------------------------

    function onRouteChange() {
        injectHomeWidget();
        injectItemRibbon();
    }

    function start() {
        var style = document.createElement('style');
        style.textContent = '@keyframes abSlideIn { from { transform: translateX(40px); opacity: 0; } to { transform: translateX(0); opacity: 1; } }';
        document.head.appendChild(style);

        fetchJson('Plugins/AchievementBadges/admin/ui-features').then(function (f) {
            if (f) { features = { EnableUnlockToasts: !!f.EnableUnlockToasts, EnableHomeWidget: !!f.EnableHomeWidget, EnableItemDetailRibbon: !!f.EnableItemDetailRibbon }; }
        }).finally(function () {
            pollUnlocks();
            setInterval(pollUnlocks, 30000);
            onRouteChange();
            window.addEventListener('hashchange', onRouteChange);
            new MutationObserver(onRouteChange).observe(document.body, { childList: true, subtree: true });
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }
})();
