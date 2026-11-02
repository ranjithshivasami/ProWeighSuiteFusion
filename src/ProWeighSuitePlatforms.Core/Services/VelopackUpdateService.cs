using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace ProWeighSuitePlatforms.Services
{
    public class VelopackUpdateService
    {
        private readonly AppSettingsService _settingsService = new();

        public string CurrentVersion
        {
            get
            {
                try { return Helpers.AppVersionHelper.GetVersion(); }
                catch { return "1.0.0"; }
            }
        }

        private const string DefaultFeedRoot = @"C:\ProWeighSuitePlatformsUpdates";

        private async Task<string> ResolveFeedAsync()
        {
            try
            {
                var settings = await _settingsService.LoadAsync();
                var feed = settings.UpdateFeedPath?.Trim();
                if (!string.IsNullOrWhiteSpace(feed))
                    return ResolveBrandFeed(feed);
            }
            catch { }
            return ResolveBrandFeed(DefaultFeedRoot);
        }

        private static string ResolveBrandFeed(string feed)
        {
            if (!string.Equals(feed.TrimEnd('\\'), DefaultFeedRoot, StringComparison.OrdinalIgnoreCase))
                return feed;

            var subdir = ProWeighSuitePlatforms.Configuration.BrandingInfo.Provider?.UpdateFeedSubdir?.Trim();
            return string.IsNullOrWhiteSpace(subdir) ? feed : Path.Combine(feed, subdir);
        }

        private IUpdateSource CreateSource(string feed)
        {
            if (feed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return new SimpleWebSource(feed);
            return new SimpleFileSource(new DirectoryInfo(feed));
        }

        public async Task<(Velopack.UpdateInfo? info, string? error)> CheckForUpdatesAsync(CancellationToken ct = default)
        {
            try
            {
                var feed = await ResolveFeedAsync();
                if (!feed.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(feed))
                    return (null, $"Update folder not found: {feed}");

                var source = CreateSource(feed);
                var mgr = new UpdateManager(source);

                var info = await mgr.CheckForUpdatesAsync();
                return (info, null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        public async Task<(bool ok, string? error)> DownloadAndApplyAsync(Velopack.UpdateInfo info, Action<int>? progress = null, CancellationToken ct = default)
        {
            try
            {
                var feed = await ResolveFeedAsync();
                var source = CreateSource(feed);
                var mgr = new UpdateManager(source);

                await mgr.DownloadUpdatesAsync(info, progress);
                mgr.ApplyUpdatesAndRestart(info);
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public async Task<(bool ok, string? error)> DownloadAsync(Velopack.UpdateInfo info, Action<int>? progress = null, CancellationToken ct = default)
        {
            try
            {
                var feed = await ResolveFeedAsync();
                var source = CreateSource(feed);
                var mgr = new UpdateManager(source);
                await mgr.DownloadUpdatesAsync(info, progress);
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public async Task<(bool ok, string? error)> ApplyAsync(Velopack.UpdateInfo info)
        {
            try
            {
                var feed = await ResolveFeedAsync();
                var source = CreateSource(feed);
                var mgr = new UpdateManager(source);
                mgr.ApplyUpdatesAndRestart(info);
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public string ResolveFeedDisplay()
        {
            try
            {
                var task = _settingsService.LoadAsync();
                task.Wait(1500);
                return ResolveBrandFeed(task.Result?.UpdateFeedPath ?? DefaultFeedRoot);
            }
            catch { return ResolveBrandFeed(DefaultFeedRoot); }
        }

        public Velopack.VelopackAsset? GetPendingRestart()
        {
            try
            {
                var feed = ResolveFeedDisplay();
                var source = CreateSource(feed);
                var mgr = new UpdateManager(source);
                return mgr.UpdatePendingRestart;
            }
            catch { return null; }
        }
    }
}
