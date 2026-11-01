using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DEX.App.Account;

public sealed record AccountInfo(string Username, string Plan);

public sealed class ApiException(string code, bool network = false) : Exception(code)
{
    public string Code { get; } = code;
    public bool Network { get; } = network;
}

// Stores only an install-scoped random device id and the refresh token, both protected with Windows DPAPI. Passwords are never stored.
public sealed class AccountClient
{
    private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "COMPIVOX");
    private static readonly string DeviceFile = Path.Combine(Dir, "device.bin");
    private static readonly string SessionFile = Path.Combine(Dir, "session.bin");
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("compivox-desktop-v1");

    private readonly HttpClient _http;
    private string? _access;
    private string? _refresh;
    private DateTime _accessExpires = DateTime.MinValue;

    public AccountClient()
    {
        var baseUrl = Environment.GetEnvironmentVariable("COMPIVOX_API_URL") ?? "https://compivox.xyz";
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("COMPIVOX-Desktop/1.0");
        SiteUrl = baseUrl.TrimEnd('/');
        DeviceId = LoadOrCreateDeviceId();
    }

    public string SiteUrl { get; }
    public string DeviceId { get; }

    public string LinkDeviceUrl => $"{SiteUrl}/account?device={DeviceId}&name={Uri.EscapeDataString(Environment.MachineName)}";

    private static string LoadOrCreateDeviceId()
    {
        Directory.CreateDirectory(Dir);
        try
        {
            if (File.Exists(DeviceFile))
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(DeviceFile), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException) { }
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        File.WriteAllBytes(DeviceFile, ProtectedData.Protect(Encoding.UTF8.GetBytes(id), Entropy, DataProtectionScope.CurrentUser));
        return id;
    }

    private static void SaveRefresh(string token) =>
        File.WriteAllBytes(SessionFile, ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Entropy, DataProtectionScope.CurrentUser));

    private static string? LoadRefresh()
    {
        try { return File.Exists(SessionFile) ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(SessionFile), Entropy, DataProtectionScope.CurrentUser)) : null; }
        catch (CryptographicException) { ClearStored(); return null; }
    }

    private static void ClearStored() { try { File.Delete(SessionFile); } catch (IOException) { } }

    private async Task<JsonObject> SendAsync(HttpMethod method, string path, object? body, bool auth, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, path);
        if (body is not null) req.Content = JsonContent.Create(body);
        if (auth && _access is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _access);
        HttpResponseMessage res;
        try { res = await _http.SendAsync(req, ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw new ApiException("network", true); }
        using (res)
        {
            JsonObject? json = null;
            try { json = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct)) as JsonObject; } catch (JsonException) { }
            if (!res.IsSuccessStatusCode)
                throw new ApiException(json?["error"]?.GetValue<string>() ?? $"http_{(int)res.StatusCode}");
            return json ?? new JsonObject();
        }
    }

    private AccountInfo Accept(JsonObject r, bool remember)
    {
        _access = r["accessToken"]!.GetValue<string>();
        _accessExpires = DateTime.UtcNow.AddSeconds((r["expiresIn"]?.GetValue<int>() ?? 900) - 30);
        _refresh = r["refreshToken"]?.GetValue<string>() ?? _refresh;
        if (remember && _refresh is not null) SaveRefresh(_refresh);
        var user = r["user"]!;
        var username = user["username"]?.GetValue<string>() ?? user["email"]!.GetValue<string>();
        return new AccountInfo(username, r["plan"]?["plan"]?.GetValue<string>() ?? "FREE");
    }

    /// <summary>웹사이트 로그인 후 전달받은 일회용 코드를 세션으로 교환한다.</summary>
    public async Task<AccountInfo> ExchangeCodeAsync(string code, CancellationToken ct = default)
    {
        var r = await SendAsync(HttpMethod.Post, "api/auth/desktop-exchange", new { code }, false, ct);
        return Accept(r, true);
    }

    public async Task<AccountInfo?> RestoreAsync(CancellationToken ct = default)
    {
        var rt = LoadRefresh();
        if (rt is null) return null;
        try
        {
            var r = await SendAsync(HttpMethod.Post, "api/auth/refresh", new { refreshToken = rt }, false, ct);
            return Accept(r, File.Exists(SessionFile));
        }
        catch (ApiException ex) when (!ex.Network) { ClearStored(); return null; }
    }

    private async Task EnsureTokenAsync(CancellationToken ct)
    {
        if (_access is not null && DateTime.UtcNow < _accessExpires) return;
        if (await RestoreAsync(ct) is null) throw new ApiException("unauthorized");
    }

    public async Task<bool> IsDeviceRegisteredAsync(CancellationToken ct = default)
    {
        await EnsureTokenAsync(ct);
        var r = await SendAsync(HttpMethod.Post, "api/devices/status", new { deviceId = DeviceId }, true, ct);
        return r["registered"]?.GetValue<bool>() ?? false;
    }

    public async Task AuthorizeBuildAsync(string project, CancellationToken ct = default)
    {
        await EnsureTokenAsync(ct);
        await SendAsync(HttpMethod.Post, "api/builds/authorize", new { deviceId = DeviceId, project }, true, ct);
    }

    public async Task LogoutAsync()
    {
        var rt = LoadRefresh();
        try { await SendAsync(HttpMethod.Post, "api/auth/logout", new { refreshToken = rt }, true, CancellationToken.None); } catch (ApiException) { }
        _access = null;
        ClearStored();
    }
}
