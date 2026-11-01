// Pfad: Server/Controllers/WebUiServer.cs

using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TheMercenaryServer.Models;
using TheMercenaryServer.Services;

namespace TheMercenaryServer.Controllers;

/// <summary>
/// Lightweight local Web UI (HttpListener) so users can configure Odin spawns without editing JSON.
/// </summary>
[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.PostLoad + 700)]
public sealed class WebUiServer(
    WebUiConfigController webUiConfig,
    SpawnConfigController spawnConfig,
    LocationTable locationTable,
    SpawnController spawnController,
    BountyService bountyService,
    MercenaryLogger logger
) : IOnLoad
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var cfg = webUiConfig.Config;

        if (!cfg.enabled)
        {
            logger.Info("WebUI disabled (config/webui.jsonc).");
            return Task.CompletedTask;
        }

        var host = string.IsNullOrWhiteSpace(cfg.host) ? "127.0.0.1" : cfg.host;
        var port = cfg.port <= 0 ? 6970 : cfg.port;
        var prefix = $"http://{host}:{port}/";

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
            _listener.Start();

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cancellationToken.Register(() => _listener?.Stop());
            _ = Task.Run(() => ListenLoop(_listener, _cts.Token));

            logger.Info($"WebUI listening on {prefix} (local config editor).");
        }
        catch (Exception ex)
        {
            logger.Error($"Failed to start WebUI on {prefix}: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private async Task ListenLoop(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext? ctx = null;
            try
            {
                ctx = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch
            {
                if (!listener.IsListening)
                    break;
            }

            if (ctx == null)
                continue;

            _ = Task.Run(() => Handle(ctx, ct), ct);
        }
    }

    private async Task Handle(HttpListenerContext ctx, CancellationToken cancellationToken)
    {
        try
        {
            var req = ctx.Request;
            var res = ctx.Response;

            // Safety: by default only allow localhost.
            var allowRemote = webUiConfig.Config.allowRemote;
            if (!allowRemote && req.RemoteEndPoint != null && !IPAddress.IsLoopback(req.RemoteEndPoint.Address))
            {
                res.StatusCode = 403;
                await WriteText(res, "Forbidden").ConfigureAwait(false);
                return;
            }

            var path = (req.Url?.AbsolutePath ?? "/").TrimEnd('/');
            if (path == "")
                path = "/";

            // API
            if (path == "/api/config" && req.HttpMethod == "GET")
            {
                await HandleGetConfig(res).ConfigureAwait(false);
                return;
            }

            if (path == "/api/config" && req.HttpMethod == "POST")
            {
                await HandlePostConfig(req, res).ConfigureAwait(false);
                return;
            }

            if (path == "/api/meta" && req.HttpMethod == "GET")
            {
                await HandleGetMeta(res).ConfigureAwait(false);
                return;
            }

            if (path == "/api/bounty/status" && req.HttpMethod == "GET")
            {
                var status = await bountyService.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                await WriteJson(res, status).ConfigureAwait(false);
                return;
            }

            if (path == "/api/bounty/heat" && req.HttpMethod == "POST")
            {
                await HandleSetBountyHeat(req, res, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (path == "/api/bounty/reset" && req.HttpMethod == "POST")
            {
                var result = await bountyService.TryResetLatestStateAsync(cancellationToken).ConfigureAwait(false);
                if (!result.Success)
                {
                    res.StatusCode = 409;
                    await WriteText(res, "No unambiguous player profile is available yet. Start one raid first.").ConfigureAwait(false);
                    return;
                }

                await WriteJson(res, result.Status).ConfigureAwait(false);
                return;
            }

            // Static files
            if (path == "/")
            {
                await ServeFile(res, "ui/index.html", "text/html; charset=utf-8").ConfigureAwait(false);
                return;
            }

            if (path == "/style.css")
            {
                await ServeFile(res, "ui/style.css", "text/css; charset=utf-8").ConfigureAwait(false);
                return;
            }

            if (path == "/background.png")
            {
                await ServeFile(res, "ui/background.png", "image/png").ConfigureAwait(false);
                return;
            }

            res.StatusCode = 404;
            await WriteText(res, "Not found").ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try { ctx.Response.Close(); } catch { }
        }
        catch
        {
            // Never crash the server for UI.
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
        }
    }

    private Task HandleGetConfig(HttpListenerResponse res)
    {
        var cfg = spawnConfig.Config;

        var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
        res.StatusCode = 200;
        res.ContentType = "application/json; charset=utf-8";
        return WriteBytes(res, Encoding.UTF8.GetBytes(json));
    }

    private async Task HandlePostConfig(HttpListenerRequest req, HttpListenerResponse res)
    {
        using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
        var body = await reader.ReadToEndAsync().ConfigureAwait(false);

        SpawnConfig? newCfg;
        try
        {
            newCfg = JsonSerializer.Deserialize<SpawnConfig>(body, new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            res.StatusCode = 400;
            await WriteText(res, $"Invalid JSON: {ex.Message}").ConfigureAwait(false);
            return;
        }

        if (newCfg == null)
        {
            res.StatusCode = 400;
            await WriteText(res, "Invalid JSON: empty payload").ConfigureAwait(false);
            return;
        }

        // Minimal validation: clamp chance, normalize zones + disabledZones
        foreach (var kvp in newCfg.maps)
        {
            var m = kvp.Value;

            if (m.chance < 0) m.chance = 0;
            if (m.chance > 100) m.chance = 100;

            // zones (authoritative list) - trim + remove empty + distinct
            m.zones ??= new List<string>();
            for (var i = m.zones.Count - 1; i >= 0; i--)
            {
                if (string.IsNullOrWhiteSpace(m.zones[i]))
                    m.zones.RemoveAt(i);
                else
                    m.zones[i] = m.zones[i].Trim();
            }
            m.zones = m.zones
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(z => z, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // disabledZones - trim + remove empty + distinct
            m.disabledZones ??= new List<string>();
            for (var i = m.disabledZones.Count - 1; i >= 0; i--)
            {
                if (string.IsNullOrWhiteSpace(m.disabledZones[i]))
                    m.disabledZones.RemoveAt(i);
                else
                    m.disabledZones[i] = m.disabledZones[i].Trim();
            }
            m.disabledZones = m.disabledZones
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(z => z, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        spawnConfig.SaveToDisk(newCfg);
        spawnController.ApplySpawnConfig(force: true);

        res.StatusCode = 200;
        await WriteText(res, "Saved and applied.").ConfigureAwait(false);
    }

    private async Task HandleGetMeta(HttpListenerResponse res)
    {
        var cfg = spawnConfig.Config;

        // Maps from config only (no fake maps)
        var maps = cfg.maps.Keys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Best-effort zones:
        // 1) Prefer config zones (authoritative)
        // 2) If zones are empty, derive from Locations DB (union of BossZone strings)
        var zonesByMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var locDict = locationTable.GetDictionary();

        foreach (var mapId in maps)
        {
            var mapCfg = cfg.maps[mapId];

            var zones = (mapCfg.zones ?? new List<string>())
                .Where(z => !string.IsNullOrWhiteSpace(z))
                .Select(z => z.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (zones.Count == 0)
            {
                if (TryFindLocation(locDict, mapId, out var loc) && loc != null) // CS8604 fixed
                {
                    zones = DeriveZonesFromDb(loc)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(z => z, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
            }

            zonesByMap[mapId] = zones;
        }

        var payload = new
        {
            version = ModMetadata.VersionNumber,
            maps,
            zonesByMap
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        res.StatusCode = 200;
        res.ContentType = "application/json; charset=utf-8";
        await WriteBytes(res, Encoding.UTF8.GetBytes(json)).ConfigureAwait(false);
    }

    private async Task HandleSetBountyHeat(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
        var body = await reader.ReadToEndAsync().ConfigureAwait(false);

        BountyHeatRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<BountyHeatRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            res.StatusCode = 400;
            await WriteText(res, $"Invalid JSON: {ex.Message}").ConfigureAwait(false);
            return;
        }

        if (request == null)
        {
            res.StatusCode = 400;
            await WriteText(res, "Invalid JSON: empty payload").ConfigureAwait(false);
            return;
        }

        var result = await bountyService.TrySetLatestHeatAsync(request.heat, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            res.StatusCode = 409;
            await WriteText(res, "No unambiguous player profile is available yet. Start one raid first.").ConfigureAwait(false);
            return;
        }

        await WriteJson(res, result.Status).ConfigureAwait(false);
    }

    private static bool TryFindLocation(
        Dictionary<string, Location> locDict,
        string locationKey,
        out Location? loc
    )
    {
        if (locDict.TryGetValue(locationKey, out loc))
            return loc != null;

        var foundKey = locDict.Keys.FirstOrDefault(k =>
            string.Equals(k, locationKey, StringComparison.OrdinalIgnoreCase));

        if (foundKey != null)
        {
            loc = locDict[foundKey];
            return loc != null;
        }

        loc = null;
        return false;
    }

    private static List<string> DeriveZonesFromDb(Location loc)
    {
        var list = new List<string>();
        var spawns = loc.Base?.BossLocationSpawn;
        if (spawns == null || spawns.Count == 0)
            return list;

        foreach (var s in spawns)
        {
            var bz = s?.BossZone;
            if (string.IsNullOrWhiteSpace(bz))
                continue;

            foreach (var part in bz.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(part))
                    list.Add(part.Trim());
            }
        }

        return list;
    }

    private async Task ServeFile(HttpListenerResponse res, string relativePath, string contentType)
    {
        var abs = Path.Join(webUiConfig.GetModRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(abs))
        {
            res.StatusCode = 404;
            await WriteText(res, "Not found").ConfigureAwait(false);
            return;
        }

        var bytes = await File.ReadAllBytesAsync(abs).ConfigureAwait(false);
        res.StatusCode = 200;
        res.ContentType = contentType;
        await WriteBytes(res, bytes).ConfigureAwait(false);
    }

    private static Task WriteText(HttpListenerResponse res, string text)
        => WriteBytes(res, Encoding.UTF8.GetBytes(text));

    private static Task WriteJson<T>(HttpListenerResponse res, T value)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        res.StatusCode = 200;
        res.ContentType = "application/json; charset=utf-8";
        return WriteBytes(res, Encoding.UTF8.GetBytes(json));
    }

    private static Task WriteBytes(HttpListenerResponse res, byte[] bytes)
    {
        res.ContentLength64 = bytes.Length;
        return res.OutputStream.WriteAsync(bytes, 0, bytes.Length)
            .ContinueWith(_ => { try { res.OutputStream.Close(); res.Close(); } catch { } });
    }
}
