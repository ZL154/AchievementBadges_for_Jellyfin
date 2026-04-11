using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AchievementBadges.Services;

public class SidebarInjectionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SidebarInjectionMiddleware> _logger;

    private const string InjectionScript = @"<script>
(function(){
    try { console.log('[AchievementBadges] inline injection script loaded'); } catch(e){}
    var SIDEBAR_ID='ab-sidebar-entry';
    var SHOWCASE_ID='ab-sidebar-showcase';
    var HEADER_ID='ab-header-badges';
    var iconMap={play_circle:'\u25b6',travel_explore:'\ud83e\udded',weekend:'\ud83d\udecb',chair:'\ud83e\ude91',home:'\ud83c\udfe0',movie_filter:'\ud83c\udf9e',live_tv:'\ud83d\udcfa',theaters:'\ud83c\udfad',local_fire_department:'\ud83d\udd25',bolt:'\u26a1',military_tech:'\ud83c\udfc6',auto_awesome:'\u2728',movie:'\ud83c\udfac',tv:'\ud83d\udcfa',dark_mode:'\ud83c\udf19',nights_stay:'\ud83c\udf03',bedtime:'\ud83d\ude34',wb_sunny:'\ud83c\udf05',light_mode:'\u2600',sunny:'\ud83c\udf1e',event:'\ud83d\udcc5',event_available:'\ud83d\uddd3',celebration:'\ud83c\udf89',stars:'\ud83c\udf1f',collections_bookmark:'\ud83d\udcda',inventory_2:'\ud83d\uddc3',today:'\ud83d\udcc6',calendar_month:'\ud83d\uddd3',favorite:'\u2764',timeline:'\ud83d\udcc8',insights:'\ud83d\udcca',all_inclusive:'\u267e',speed:'\ud83d\udca8',hourglass_bottom:'\u23f3',directions_run:'\ud83c\udfc3',sports_score:'\ud83c\udfc1',local_movies:'\ud83c\udf7f',emoji_events:'\ud83c\udfc6'};
    function ic(n){return iconMap[(n||'').toLowerCase()]||'\ud83c\udfc5';}
    var rarityColors={common:'#9fb3c8',uncommon:'#34d399',rare:'#60a5fa',epic:'#a78bfa',legendary:'#fbbf24',mythic:'#f43f5e'};
    function rc(r){return rarityColors[(r||'').toLowerCase()]||'#9fb3c8';}

    function getApi(){return window.ApiClient||window.apiClient||null;}
    function getUserId(){
        var api=getApi();if(!api)return '';
        try{if(typeof api.getCurrentUserId==='function'){var id=api.getCurrentUserId();if(id)return id;}
        if(api._serverInfo&&api._serverInfo.UserId)return api._serverInfo.UserId;}catch(e){}return '';
    }
    function buildUrl(p){var api=getApi();var c=p.replace(/^\/+/,'');return(api&&typeof api.getUrl==='function')?api.getUrl(c):'/'+c;}
    function authHeaders(){
        var h={'Content-Type':'application/json'};var api=getApi();if(!api)return h;
        try{if(typeof api.accessToken==='function'){var t=api.accessToken();if(t)h['X-Emby-Token']=t;}
        else if(api._serverInfo&&api._serverInfo.AccessToken)h['X-Emby-Token']=api._serverInfo.AccessToken;}catch(e){}return h;
    }
    function fetchEquipped(){
        var uid=getUserId();if(!uid)return Promise.resolve([]);
        return fetch(buildUrl('Plugins/AchievementBadges/users/'+uid+'/equipped'),{headers:authHeaders(),credentials:'include'})
            .then(function(r){return r.ok?r.json():[];}).catch(function(){return [];});
    }

    function injectSidebar(){
        try {
            if(document.getElementById(SIDEBAR_ID)){ return; }
            // Try multiple selectors — different Jellyfin themes/plugins rebuild the nav menu differently.
            var nav = document.querySelector('.mainDrawer-scrollContainer .navMenuOptions')
                   || document.querySelector('.mainDrawer-scrollContainer')
                   || document.querySelector('.navDrawer-scrollContainer')
                   || document.querySelector('.drawer-navigationPlugins')
                   || document.querySelector('.navMenuOptions')
                   || document.querySelector('[is=""emby-scroller""] .navMenuOptions');
            if(!nav){
                return;
            }
            console.log('[AchievementBadges] injectSidebar: found nav container, adding entry');
            var wrap=document.createElement('div');wrap.id=SIDEBAR_ID;
            var a=document.createElement('a');a.className='navMenuOption';a.style.cursor='pointer';
            a.innerHTML='<span class=""material-icons navMenuOptionIcon"">emoji_events</span><span class=""navMenuOptionText"">Achievements</span>';
            a.addEventListener('click',function(e){e.preventDefault();window.location.hash='/achievements';});
            wrap.appendChild(a);
            var showcase=document.createElement('div');showcase.id=SHOWCASE_ID;
            showcase.style.cssText='display:flex;gap:4px;padding:2px 12px 8px 42px;flex-wrap:wrap;';
            wrap.appendChild(showcase);
            nav.appendChild(wrap);
            refreshShowcases();
        } catch(e) { console.error('[AchievementBadges] injectSidebar error:', e); }
    }

    function injectHeader(){
        try {
            if(document.getElementById(HEADER_ID)){ return; }
            var headerRight=document.querySelector('.headerRight')||document.querySelector('.skinHeader .headerButton:last-child');
            if(!headerRight){
                return;
            }
            console.log('[AchievementBadges] injectHeader: found header, adding badges container');
            var container=document.createElement('div');container.id=HEADER_ID;
            container.style.cssText='display:flex;align-items:center;gap:3px;margin-right:6px;';
            container.title='Equipped Badges';
            container.style.cursor='pointer';
            container.addEventListener('click',function(){window.location.hash='/achievements';});
            var parent=headerRight.parentElement;
            if(parent)parent.insertBefore(container,headerRight);
            refreshShowcases();
        } catch(e) { console.error('[AchievementBadges] injectHeader error:', e); }
    }

    function refreshShowcases(){
        fetchEquipped().then(function(badges){
            var sc=document.getElementById(SHOWCASE_ID);
            if(sc){
                sc.innerHTML='';
                if(badges&&badges.length){
                    badges.forEach(function(b){
                        var pill=document.createElement('div');pill.title=b.Title+' ('+b.Rarity+')';
                        pill.style.cssText='display:flex;align-items:center;gap:3px;padding:2px 6px;border-radius:999px;background:rgba(255,255,255,0.06);border:1px solid '+rc(b.Rarity)+';font-size:11px;cursor:default;';
                        pill.innerHTML='<span style=""font-size:12px;"">'+ic(b.Icon)+'</span><span style=""color:'+rc(b.Rarity)+';font-weight:600;"">'+b.Title+'</span>';
                        sc.appendChild(pill);
                    });
                }
            }
            var hdr=document.getElementById(HEADER_ID);
            if(hdr){
                hdr.innerHTML='';
                if(badges&&badges.length){
                    badges.forEach(function(b){
                        var dot=document.createElement('div');dot.title=b.Title+' ('+b.Rarity+')';
                        dot.style.cssText='width:26px;height:26px;border-radius:999px;display:flex;align-items:center;justify-content:center;background:rgba(255,255,255,0.08);border:1.5px solid '+rc(b.Rarity)+';font-size:13px;';
                        dot.textContent=ic(b.Icon);
                        hdr.appendChild(dot);
                    });
                }
            }
        });
    }

    function tryInject(){
        injectSidebar();
        injectHeader();
    }

    function start(){
        try { console.log('[AchievementBadges] start() running, readyState=', document.readyState); } catch(e){}
        tryInject();
        // Retry on a loop for the first 15 seconds in case nav hasn't mounted yet
        // or another plugin rebuilt it after we injected
        var attempts = 0;
        var retryInterval = setInterval(function(){
            attempts++;
            tryInject();
            if(attempts >= 15){ clearInterval(retryInterval); }
        }, 1000);
        // MutationObserver catches any later nav rebuilds (e.g. SPA route changes)
        if(document.body){
            new MutationObserver(function(){tryInject();}).observe(document.body,{childList:true,subtree:true});
        }
        // Refresh equipped badge content periodically
        setInterval(refreshShowcases,30000);
    }
    if(document.readyState==='loading'){
        document.addEventListener('DOMContentLoaded',start);
    } else {
        start();
    }
    // Also kick off start() after a small delay as a belt-and-braces approach,
    // since some themes load asynchronously and the initial DOM is minimal
    setTimeout(function(){ try{ tryInject(); }catch(e){} }, 500);
    setTimeout(function(){ try{ tryInject(); }catch(e){} }, 2000);
})();
</script>
<script src=""/Plugins/AchievementBadges/client-script/standalone"" defer></script>
<script src=""/Plugins/AchievementBadges/client-script/enhance"" defer></script>";

    public SidebarInjectionMiddleware(RequestDelegate next, ILogger<SidebarInjectionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsIndexHtmlRequest(context))
        {
            await _next(context);
            return;
        }

        _logger.LogInformation("[AchievementBadges] middleware handling index.html request: {Path}", context.Request.Path.Value);

        var originalBody = context.Response.Body;

        try
        {
            using var buffer = new MemoryStream();
            context.Response.Body = buffer;

            await _next(context);

            buffer.Seek(0, SeekOrigin.Begin);

            if (context.Response.ContentType != null &&
                context.Response.ContentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                var html = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync();

                if (html.Contains("</body>", StringComparison.OrdinalIgnoreCase))
                {
                    html = html.Replace("</body>", InjectionScript + "</body>",
                        StringComparison.OrdinalIgnoreCase);

                    var bytes = Encoding.UTF8.GetBytes(html);
                    context.Response.ContentLength = bytes.Length;
                    context.Response.Body = originalBody;
                    await context.Response.Body.WriteAsync(bytes);

                    _logger.LogInformation("[AchievementBadges] Injected scripts into {Path} ({Bytes} bytes).", context.Request.Path.Value, bytes.Length);
                    return;
                }
                else
                {
                    _logger.LogWarning("[AchievementBadges] middleware: response HTML did not contain </body> tag, skipping injection. Content-Type={ContentType}", context.Response.ContentType);
                }
            }
            else
            {
                _logger.LogWarning("[AchievementBadges] middleware: response content type not HTML, skipping injection. Content-Type={ContentType}", context.Response.ContentType);
            }

            buffer.Seek(0, SeekOrigin.Begin);
            context.Response.Body = originalBody;
            await buffer.CopyToAsync(originalBody);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AchievementBadges] Error in script injection middleware.");
            context.Response.Body = originalBody;
        }
    }

    private static bool IsIndexHtmlRequest(HttpContext context)
    {
        return context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && context.Request.Path.Value != null
            && context.Request.Path.Value.Contains("index.html", StringComparison.OrdinalIgnoreCase)
            && !context.Request.Path.Value.Contains("/api/", StringComparison.OrdinalIgnoreCase);
    }
}
