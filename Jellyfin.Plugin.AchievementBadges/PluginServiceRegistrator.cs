using Jellyfin.Plugin.AchievementBadges.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.AchievementBadges;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<AchievementBadgeService>();
        serviceCollection.AddSingleton<PlaybackCompletionService>();
        serviceCollection.AddSingleton<WatchHistoryBackfillService>();

        serviceCollection.AddSingleton<PlaybackCompletionTracker>();
        serviceCollection.AddHostedService(provider => provider.GetRequiredService<PlaybackCompletionTracker>());

        serviceCollection.AddHostedService<SafeStartupRunner>();

        serviceCollection.AddTransient<IStartupFilter, SidebarInjectionStartup>();
    }
}
