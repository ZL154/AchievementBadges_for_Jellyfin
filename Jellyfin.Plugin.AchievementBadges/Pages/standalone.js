(function () {
    var ROUTE = "#!/achievements";
    var ROOT_ID = "achievementBadgesStandaloneRoot";

    var iconMap = {
        play_circle:'\u25b6', travel_explore:'\ud83e\udded', weekend:'\ud83d\udecb', chair:'\ud83e\ude91', home:'\ud83c\udfe0',
        movie_filter:'\ud83c\udf9e', live_tv:'\ud83d\udcfa', theaters:'\ud83c\udfad', local_fire_department:'\ud83d\udd25',
        bolt:'\u26a1', military_tech:'\ud83c\udfc6', auto_awesome:'\u2728', movie:'\ud83c\udfac', tv:'\ud83d\udcfa',
        dark_mode:'\ud83c\udf19', nights_stay:'\ud83c\udf03', bedtime:'\ud83d\ude34', wb_sunny:'\ud83c\udf05', light_mode:'\u2600',
        sunny:'\ud83c\udf1e', event:'\ud83d\udcc5', event_available:'\ud83d\uddd3', celebration:'\ud83c\udf89', stars:'\ud83c\udf1f',
        collections_bookmark:'\ud83d\udcda', inventory_2:'\ud83d\uddc3', today:'\ud83d\udcc6', calendar_month:'\ud83d\uddd3',
        favorite:'\u2764', timeline:'\ud83d\udcc8', insights:'\ud83d\udcca', all_inclusive:'\u267e', speed:'\ud83d\udca8',
        hourglass_bottom:'\u23f3', directions_run:'\ud83c\udfc3', sports_score:'\ud83c\udfc1', local_movies:'\ud83c\udf7f',
        emoji_events:'\ud83c\udfc6'
    };

    function icon(name) { return iconMap[(name || '').toLowerCase()] || '\ud83c\udfc5'; }

    function rarityClass(r) {
        var v = (r || '').toLowerCase();
        if (v === 'uncommon') return 'ab-r-uncommon';
        if (v === 'rare') return 'ab-r-rare';
        if (v === 'epic') return 'ab-r-epic';
        if (v === 'legendary') return 'ab-r-legendary';
        if (v === 'mythic') return 'ab-r-mythic';
        return 'ab-r-common';
    }

    function getApiClient() { return window.ApiClient || window.apiClient || null; }

    function buildUrl(path) {
        var clean = String(path || '').replace(/^\/+/, '');
        var api = getApiClient();
        if (api && typeof api.getUrl === 'function') return api.getUrl(clean);
        return '/' + clean;
    }

    function getAuthHeaders() {
        var api = getApiClient();
        var h = { 'Content-Type': 'application/json' };
        if (!api) return h;
        try {
            if (typeof api.accessToken === 'function') {
                var t = api.accessToken();
                if (t) h['X-Emby-Token'] = t;
            } else if (api._serverInfo && api._serverInfo.AccessToken) {
                h['X-Emby-Token'] = api._serverInfo.AccessToken;
            }
        } catch (e) {}
        return h;
    }

    function fetchJson(path, method) {
        return fetch(buildUrl(path), {
            method: method || 'GET',
            headers: getAuthHeaders(),
            credentials: 'include'
        }).then(function (r) {
            if (!r.ok) {
                return r.text().then(function (t) {
                    var msg = 'Error ' + r.status;
                    try { var b = JSON.parse(t); if (b && b.Message) msg = b.Message; } catch (e) {}
                    throw new Error(msg);
                });
            }
            if (r.status === 204) return null;
            return r.text().then(function (t) { return t ? JSON.parse(t) : null; });
        });
    }

    function getCurrentUserId() {
        var api = getApiClient();
        if (api) {
            try {
                if (typeof api.getCurrentUserId === 'function') {
                    var id = api.getCurrentUserId();
                    if (id) return Promise.resolve(id);
                }
                if (api._serverInfo && api._serverInfo.UserId) return Promise.resolve(api._serverInfo.UserId);
            } catch (e) {}
        }
        return fetchJson('Users/Me').then(function (me) {
            return me && me.Id ? me.Id : '';
        }).catch(function () { return ''; });
    }

    function injectStyles() {
        if (document.getElementById('ab-standalone-css')) return;
        var s = document.createElement('style');
        s.id = 'ab-standalone-css';
        s.textContent = '#' + ROOT_ID + '{position:fixed;inset:0;z-index:999999;overflow-y:auto;padding:2em;background:var(--theme-body-background,#181818);color:#fff;font-family:inherit;}' +
            '#' + ROOT_ID + ' .ab-wrap{max-width:1500px;margin:0 auto;}' +
            '#' + ROOT_ID + ' .ab-topbar{display:flex;justify-content:space-between;align-items:center;gap:1em;flex-wrap:wrap;margin-bottom:1.2em;}' +
            '#' + ROOT_ID + ' .ab-back{padding:0.6em 1em;border-radius:10px;border:1px solid rgba(255,255,255,0.12);background:rgba(255,255,255,0.04);color:#fff;cursor:pointer;text-decoration:none;display:inline-flex;align-items:center;gap:0.5em;font-weight:700;}' +
            '#' + ROOT_ID + ' .ab-hero{display:flex;justify-content:space-between;align-items:flex-start;flex-wrap:wrap;gap:1em;padding:1.4em;border-radius:18px;background:rgba(255,255,255,0.05);border:1px solid rgba(255,255,255,0.12);}' +
            '#' + ROOT_ID + ' .ab-hero-left{display:flex;align-items:center;gap:1em;}' +
            '#' + ROOT_ID + ' .ab-hero-icon{width:60px;height:60px;border-radius:999px;display:flex;align-items:center;justify-content:center;background:rgba(255,255,255,0.1);font-size:1.6em;}' +
            '#' + ROOT_ID + ' .ab-hero-title{font-size:1.25em;font-weight:700;}' +
            '#' + ROOT_ID + ' .ab-hero-sub{font-size:0.92em;opacity:0.8;margin-top:0.2em;}' +
            '#' + ROOT_ID + ' .ab-showcase{display:grid;grid-template-columns:repeat(auto-fill,minmax(180px,1fr));gap:0.8em;margin-top:1em;}' +
            '#' + ROOT_ID + ' .ab-sc-card{display:flex;align-items:center;gap:0.6em;padding:0.7em;border-radius:12px;border:1px solid rgba(255,255,255,0.12);background:rgba(255,255,255,0.04);}' +
            '#' + ROOT_ID + ' .ab-sc-icon{width:36px;height:36px;border-radius:999px;display:flex;align-items:center;justify-content:center;background:rgba(255,255,255,0.08);}' +
            '#' + ROOT_ID + ' .ab-stats{margin-top:1.5em;display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:1em;}' +
            '#' + ROOT_ID + ' .ab-stat{padding:1em;border-radius:14px;border:1px solid rgba(255,255,255,0.12);background:rgba(255,255,255,0.04);}' +
            '#' + ROOT_ID + ' .ab-stat-t{font-size:0.9em;opacity:0.8;}' +
            '#' + ROOT_ID + ' .ab-stat-v{font-size:2em;font-weight:700;margin-top:0.2em;}' +
            '#' + ROOT_ID + ' .ab-tabs{margin-top:1.5em;display:flex;gap:0.65em;flex-wrap:wrap;}' +
            '#' + ROOT_ID + ' .ab-tab{padding:0.55em 0.95em;border-radius:10px;border:1px solid rgba(255,255,255,0.12);background:rgba(255,255,255,0.04);cursor:pointer;font-weight:700;color:#fff;}' +
            '#' + ROOT_ID + ' .ab-tab.active{background:rgba(255,255,255,0.12);}' +
            '#' + ROOT_ID + ' .ab-panel{margin-top:1.5em;}' +
            '#' + ROOT_ID + ' .ab-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(270px,1fr));gap:1em;margin-top:1em;}' +
            '#' + ROOT_ID + ' .ab-card{padding:1em;border-radius:12px;border:1px solid rgba(255,255,255,0.12);background:rgba(255,255,255,0.03);}' +
            '#' + ROOT_ID + ' .ab-card-h{display:flex;gap:0.8em;align-items:center;margin-bottom:0.7em;}' +
            '#' + ROOT_ID + ' .ab-card-icon{width:42px;height:42px;border-radius:999px;display:flex;align-items:center;justify-content:center;background:rgba(255,255,255,0.1);font-size:1.2em;flex-shrink:0;}' +
            '#' + ROOT_ID + ' .ab-card-title{font-size:1.05em;font-weight:700;}' +
            '#' + ROOT_ID + ' .ab-card-meta{font-size:0.92em;opacity:0.9;}' +
            '#' + ROOT_ID + ' .ab-desc{margin-top:0.5em;line-height:1.45;}' +
            '#' + ROOT_ID + ' .ab-prog-text{display:flex;justify-content:space-between;font-size:0.92em;margin:0.7em 0 0.35em;opacity:0.8;}' +
            '#' + ROOT_ID + ' .ab-prog-bar{height:10px;border-radius:999px;overflow:hidden;background:#0f1318;border:1px solid rgba(255,255,255,0.1);}' +
            '#' + ROOT_ID + ' .ab-prog-fill{height:100%;background:#60a5fa;}' +
            '#' + ROOT_ID + ' .ab-footer{margin-top:0.8em;display:flex;justify-content:space-between;align-items:center;gap:0.6em;flex-wrap:wrap;}' +
            '#' + ROOT_ID + ' .ab-btn{padding:0.5em 0.85em;border-radius:8px;border:1px solid rgba(255,255,255,0.14);background:rgba(255,255,255,0.05);color:#fff;cursor:pointer;}' +
            '#' + ROOT_ID + ' .ab-unlocked{color:#4ade80;font-weight:700;}' +
            '#' + ROOT_ID + ' .ab-locked{color:#f87171;font-weight:700;}' +
            '#' + ROOT_ID + ' .ab-r-common{color:#9fb3c8;}' +
            '#' + ROOT_ID + ' .ab-r-uncommon{color:#34d399;}' +
            '#' + ROOT_ID + ' .ab-r-rare{color:#60a5fa;}' +
            '#' + ROOT_ID + ' .ab-r-epic{color:#a78bfa;}' +
            '#' + ROOT_ID + ' .ab-r-legendary{color:#fbbf24;}' +
            '#' + ROOT_ID + ' .ab-r-mythic{color:#f43f5e;}' +
            '#' + ROOT_ID + ' .ab-lb-row{display:flex;justify-content:space-between;gap:1em;padding:0.75em 0;border-bottom:1px solid rgba(255,255,255,0.08);}' +
            '#' + ROOT_ID + ' .ab-lb-row:last-child{border-bottom:none;}' +
            '#' + ROOT_ID + ' .ab-panel-card{padding:1.1em;border-radius:14px;border:1px solid rgba(255,255,255,0.12);background:rgba(255,255,255,0.03);}' +
            '#' + ROOT_ID + ' .ab-muted{opacity:0.7;}' +
            '#' + ROOT_ID + ' .ab-error{margin-top:1em;padding:1em;border:1px solid rgba(248,113,113,0.45);border-radius:12px;background:rgba(248,113,113,0.08);color:#fca5a5;}' +
            '#' + ROOT_ID + ' .ab-eyebrow{font-size:0.88em;font-weight:700;letter-spacing:0.06em;text-transform:uppercase;color:#9fb3c8;margin-bottom:0.7em;}' +
            '@media(max-width:900px){#' + ROOT_ID + '{padding:1em;}}';
        document.head.appendChild(s);
    }

    var userId = '';
    var root = null;

    function el(id) { return root ? root.querySelector('#' + id) : null; }

    function createRoot() {
        var r = document.getElementById(ROOT_ID);
        if (r) { r.innerHTML = ''; } else { r = document.createElement('div'); r.id = ROOT_ID; }
        r.innerHTML =
            '<div class="ab-wrap">' +
                '<div class="ab-topbar">' +
                    '<h2 style="margin:0;">Achievements</h2>' +
                    '<a class="ab-back" href="/web/index.html#!/home">\u2190 Back Home</a>' +
                '</div>' +
                '<div class="ab-hero">' +
                    '<div style="flex:1;min-width:280px;">' +
                        '<div class="ab-hero-left">' +
                            '<div class="ab-hero-icon">\ud83c\udfc5</div>' +
                            '<div><div id="abSaTitle" class="ab-hero-title">Achievement Profile</div><div id="abSaSub" class="ab-hero-sub">Loading...</div></div>' +
                        '</div>' +
                        '<div style="margin-top:1em;"><div class="ab-eyebrow">Showcase</div><div id="abSaShowcase" class="ab-showcase"><div class="ab-muted">Equip badges to build your showcase.</div></div></div>' +
                    '</div>' +
                '</div>' +
                '<div id="abSaError" style="display:none;" class="ab-error"></div>' +
                '<div class="ab-stats">' +
                    '<div class="ab-stat"><div class="ab-stat-t">Unlocked</div><div id="abSaUnlocked" class="ab-stat-v">0</div></div>' +
                    '<div class="ab-stat"><div class="ab-stat-t">Total</div><div id="abSaTotal" class="ab-stat-v">0</div></div>' +
                    '<div class="ab-stat"><div class="ab-stat-t">Completion</div><div id="abSaPct" class="ab-stat-v">0%</div></div>' +
                    '<div class="ab-stat"><div class="ab-stat-t">Score</div><div id="abSaScore" class="ab-stat-v">0</div></div>' +
                '</div>' +
                '<div class="ab-tabs">' +
                    '<button type="button" class="ab-tab active" id="abSaTabBadges">My Badges</button>' +
                    '<button type="button" class="ab-tab" id="abSaTabLb">Leaderboard</button>' +
                    '<button type="button" class="ab-tab" id="abSaTabStats">Stats</button>' +
                '</div>' +
                '<div id="abSaPanelBadges" class="ab-panel">' +
                    '<h3 style="margin:0 0 0.75em;">Equipped badges</h3>' +
                    '<div id="abSaEquippedEmpty" class="ab-muted" style="padding:0.8em;border:1px dashed rgba(255,255,255,0.16);border-radius:12px;">No equipped badges yet.</div>' +
                    '<div id="abSaEquipped" class="ab-grid"></div>' +
                    '<div id="abSaGrid" class="ab-grid" style="margin-top:1.5em;"></div>' +
                '</div>' +
                '<div id="abSaPanelLb" class="ab-panel" style="display:none;"><div class="ab-panel-card"><h3 style="margin:0 0 0.75em;">Leaderboard</h3><div id="abSaLb">Loading...</div></div></div>' +
                '<div id="abSaPanelStats" class="ab-panel" style="display:none;"><div class="ab-panel-card"><h3 style="margin:0 0 0.75em;">Server Stats</h3><div id="abSaServerStats">Loading...</div></div></div>' +
            '</div>';
        return r;
    }

    function showError(msg) {
        var e = el('abSaError');
        if (e) { e.textContent = msg; e.style.display = 'block'; }
    }

    function setTab(name) {
        var panels = { badges: 'abSaPanelBadges', lb: 'abSaPanelLb', stats: 'abSaPanelStats' };
        var tabs = { badges: 'abSaTabBadges', lb: 'abSaTabLb', stats: 'abSaTabStats' };
        for (var k in panels) {
            var p = el(panels[k]); if (p) p.style.display = k === name ? 'block' : 'none';
            var t = el(tabs[k]); if (t) t.classList.toggle('active', k === name);
        }
    }

    function renderShowcase(badges) {
        var sc = el('abSaShowcase'); if (!sc) return;
        sc.innerHTML = '';
        if (!badges || !badges.length) { sc.innerHTML = '<div class="ab-muted">Equip badges to build your showcase.</div>'; return; }
        badges.forEach(function (b) {
            var c = document.createElement('div'); c.className = 'ab-sc-card';
            c.innerHTML = '<div class="ab-sc-icon">' + icon(b.Icon) + '</div><div><div style="font-weight:700;">' + b.Title + '</div><div class="' + rarityClass(b.Rarity) + '" style="font-size:0.88em;">' + b.Rarity + '</div></div>';
            sc.appendChild(c);
        });
    }

    function renderEquipped(badges) {
        var row = el('abSaEquipped'), empty = el('abSaEquippedEmpty'); if (!row) return;
        row.innerHTML = '';
        if (!badges || !badges.length) { if (empty) empty.style.display = 'block'; return; }
        if (empty) empty.style.display = 'none';
        badges.forEach(function (b) {
            var c = document.createElement('div'); c.className = 'ab-card'; c.setAttribute('data-badge-id', b.Id);
            c.innerHTML = '<div class="ab-card-h"><div class="ab-card-icon">' + icon(b.Icon) + '</div><div style="flex:1;"><div class="ab-card-title">' + b.Title + '</div><div class="ab-card-meta ' + rarityClass(b.Rarity) + '">' + b.Rarity + '</div></div></div>' +
                '<div class="ab-footer"><div class="ab-unlocked">Equipped</div><button type="button" class="ab-btn">Unequip</button></div>';
            c.querySelector('button').addEventListener('click', function () { doUnequip(b.Id); });
            row.appendChild(c);
        });
    }

    function renderBadges(badges, equippedIds) {
        var grid = el('abSaGrid'); if (!grid) return;
        grid.innerHTML = '';
        if (!badges || !badges.length) return;
        badges.forEach(function (b) {
            var cur = b.CurrentValue || 0, tar = b.TargetValue || 0;
            var pct = tar > 0 ? Math.min(cur / tar * 100, 100) : 0;
            var eq = equippedIds && equippedIds[b.Id];
            var c = document.createElement('div'); c.className = 'ab-card';
            c.innerHTML = '<div class="ab-card-h"><div class="ab-card-icon">' + icon(b.Icon) + '</div><div style="flex:1;"><div class="ab-card-title">' + b.Title + '</div><div class="ab-card-meta ' + rarityClass(b.Rarity) + '">' + b.Rarity + ' \u2022 ' + b.Category + '</div></div></div>' +
                '<div class="ab-desc">' + b.Description + '</div>' +
                '<div class="ab-prog-text"><span>Progress</span><span>' + cur + '/' + tar + '</span></div>' +
                '<div class="ab-prog-bar"><div class="ab-prog-fill" style="width:' + pct + '%;"></div></div>' +
                '<div class="ab-footer"><div class="' + (b.Unlocked ? 'ab-unlocked' : 'ab-locked') + '">' + (b.Unlocked ? 'Unlocked' : 'Locked') + '</div>' +
                '<button type="button" class="ab-btn"' + (!b.Unlocked ? ' disabled style="opacity:0.5;"' : '') + '>' + (eq ? 'Unequip' : 'Equip') + '</button></div>';
            if (b.Unlocked) {
                c.querySelector('.ab-footer button').addEventListener('click', function () {
                    if (eq) doUnequip(b.Id); else doEquip(b.Id);
                });
            }
            grid.appendChild(c);
        });
    }

    function loadAll() {
        if (!userId) { showError('Could not detect user.'); return Promise.resolve(); }
        var eqIds = {};
        return Promise.all([
            fetchJson('Plugins/AchievementBadges/users/' + userId),
            fetchJson('Plugins/AchievementBadges/users/' + userId + '/summary'),
            fetchJson('Plugins/AchievementBadges/users/' + userId + '/equipped'),
            fetchJson('Plugins/AchievementBadges/leaderboard?limit=10'),
            fetchJson('Plugins/AchievementBadges/server/stats')
        ]).then(function (results) {
            var badges = results[0], summary = results[1], equipped = results[2], lb = results[3], stats = results[4];

            var sub = el('abSaSub');
            if (sub) sub.textContent = 'Completion: ' + ((summary && summary.Percentage != null) ? summary.Percentage : 0) + '%';
            var u = el('abSaUnlocked'); if (u) u.textContent = summary ? summary.Unlocked : 0;
            var t = el('abSaTotal'); if (t) t.textContent = summary ? summary.Total : 0;
            var p = el('abSaPct'); if (p) p.textContent = (summary && typeof summary.Percentage === 'number' ? summary.Percentage.toFixed(1) : '0') + '%';
            var sc = el('abSaScore'); if (sc) sc.textContent = summary ? (summary.Score || 0) : 0;

            renderShowcase(equipped);
            renderEquipped(equipped);
            if (equipped) equipped.forEach(function (b) { eqIds[b.Id] = true; });
            renderBadges(badges, eqIds);

            var lbBox = el('abSaLb');
            if (lbBox) {
                if (!lb || !lb.length) { lbBox.innerHTML = '<div class="ab-muted">No data yet.</div>'; }
                else {
                    lbBox.innerHTML = lb.map(function (e, i) {
                        return '<div class="ab-lb-row"><div><strong>#' + (i + 1) + '</strong> \u2022 ' + (e.UserName || e.UserId) + '</div><div>' + (e.Score || 0) + ' pts \u2022 ' + e.Unlocked + ' unlocked</div></div>';
                    }).join('');
                }
            }

            var stBox = el('abSaServerStats');
            if (stBox && stats) {
                stBox.innerHTML = '<div>Total users: ' + (stats.TotalUsers || 0) + '</div>' +
                    '<div style="margin-top:0.4em;">Badges unlocked: ' + (stats.TotalBadgesUnlocked || 0) + '</div>' +
                    '<div style="margin-top:0.4em;">Items watched: ' + (stats.TotalItemsWatched || 0) + '</div>' +
                    '<div style="margin-top:0.4em;">Movies watched: ' + (stats.TotalMoviesWatched || 0) + '</div>' +
                    '<div style="margin-top:0.4em;">Series completed: ' + (stats.TotalSeriesCompleted || 0) + '</div>' +
                    '<div style="margin-top:0.4em;">Most common badge: ' + (stats.MostCommonBadge || 'None') + '</div>';
            }
        }).catch(function (err) {
            showError('Failed to load achievements. ' + (err && err.message ? err.message : String(err)));
        });
    }

    function doEquip(badgeId) {
        fetchJson('Plugins/AchievementBadges/users/' + userId + '/equipped/' + badgeId, 'POST').then(function () { return loadAll(); }).catch(function (e) { showError('Failed to equip. ' + e.message); });
    }

    function doUnequip(badgeId) {
        fetchJson('Plugins/AchievementBadges/users/' + userId + '/equipped/' + badgeId, 'DELETE').then(function () { return loadAll(); }).catch(function (e) { showError('Failed to unequip. ' + e.message); });
    }

    function mountRoute() {
        injectStyles();
        root = document.getElementById(ROOT_ID);
        if (!root) {
            root = createRoot();
            document.body.appendChild(root);
        } else {
            root = createRoot();
        }
        root.style.display = 'block';

        el('abSaTabBadges').addEventListener('click', function () { setTab('badges'); });
        el('abSaTabLb').addEventListener('click', function () { setTab('lb'); });
        el('abSaTabStats').addEventListener('click', function () { setTab('stats'); });
        setTab('badges');

        getCurrentUserId().then(function (id) {
            userId = id;
            if (!id) { showError('Could not detect your user account. Please log in.'); return; }
            return loadAll();
        });
    }

    function unmountRoute() {
        var r = document.getElementById(ROOT_ID);
        if (r) r.style.display = 'none';
    }

    function onRouteChange() {
        if (window.location.hash && window.location.hash.indexOf(ROUTE.replace('#', '')) !== -1) {
            mountRoute();
        } else {
            unmountRoute();
        }
    }

    window.addEventListener('hashchange', onRouteChange);
    window.addEventListener('popstate', onRouteChange);

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', onRouteChange);
    } else {
        onRouteChange();
    }
})();
