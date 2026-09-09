using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using Jellyfin.Plugin.KidGuard.Services;

namespace Jellyfin.Plugin.KidGuard;
public sealed class Plugin : BasePlugin<BasePluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths paths, IXmlSerializer serializer) : base(paths, serializer) { }
    public override string Name => "KidGuard";
    public override string Description => "Parent-reviewed, individual child libraries with transparent recommendations.";
    public override Guid Id => Guid.Parse("f2247450-a459-4c15-9ee2-9e56c8737ce1");
    public IEnumerable<PluginPageInfo> GetPages() => [
        new() { Name = "kidguard", EmbeddedResourcePath = "Jellyfin.Plugin.KidGuard.UI.index.html", EnableInMainMenu = true, MenuSection = "server", MenuIcon = "family_restroom" },
        new() { Name = "kidguard.js", EmbeddedResourcePath = "Jellyfin.Plugin.KidGuard.UI.kidguard.js" }
    ];
}
public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost host)
    {
        services.AddSingleton<Manager>();
        services.AddSingleton<LibraryAdapter>();
        services.AddSingleton<Providers.ContentAnalysis>();
        services.AddSingleton<SnapshotGuard>();
        services.Configure<MvcOptions>(o => o.Filters.AddService<SnapshotGuard>());
        services.AddHostedService<LibraryMonitor>();
    }
}
