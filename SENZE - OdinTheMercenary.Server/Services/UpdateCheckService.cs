using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheMercenaryServer.Controllers;

namespace TheMercenaryServer.Services;

[Injectable(InjectionType.Singleton, TypePriority = int.MaxValue - 1)]
public sealed class UpdateCheckService(
    IHttpClientFactory httpClientFactory,
    UpdateCheckConfigController configController,
    MercenaryLogger logger) : IOnLoad
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/notSENZE/Odin-TheMercenary/releases/latest";

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (configController.Config.enabled)
            _ = Task.Run(() => CheckForUpdateAsync(cancellationToken), CancellationToken.None);

        return Task.CompletedTask;
    }

    private async Task CheckForUpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(configController.Config.timeoutSeconds));

            var httpClient = httpClientFactory.CreateClient("Github");
            var release = await httpClient.GetFromJsonAsync<ReleaseInformation>(LatestReleaseUrl, timeout.Token);
            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
                return;

            var tag = release.TagName.Trim().TrimStart('v', 'V');
            var latestVersion = new SemanticVersioning.Version(tag);
            var currentVersion = new SemanticVersioning.Version(ModMetadata.VersionNumber);

            if (latestVersion <= currentVersion)
                return;

            logger.Warn($"A new Odin - The Mercenary version is available: {latestVersion} (installed: {currentVersion}).");
            logger.Warn($"Download: {release.HtmlUrl}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            logger.Debug("GitHub update check timed out.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or ArgumentException)
        {
            logger.Debug($"GitHub update check failed: {ex.Message}");
        }
    }

    private sealed record ReleaseInformation
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; init; } = string.Empty;
    }
}
