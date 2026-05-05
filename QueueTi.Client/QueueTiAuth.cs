using System.Net.Http.Json;

namespace QueueTi;

public static class QueueTiAuth
{
    public static async Task<bool> GetAuthRequiredAsync(
        string baseUrl,
        bool insecure = false,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        using var http = BuildHttpClient(baseUrl, insecure);
        return await GetAuthRequiredAsync(http, ct);
    }

    public static async Task<string> LoginAsync(
        string baseUrl,
        string username,
        string password,
        bool insecure = false,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        using var http = BuildHttpClient(baseUrl, insecure);
        return await LoginAsync(http, username, password, ct);
    }

    internal static async Task<bool> GetAuthRequiredAsync(
        HttpClient http,
        CancellationToken ct = default)
    {
        using var response = await http.GetAsync("/api/auth/status", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("auth_required", out var prop) && prop.GetBoolean();
    }

    internal static async Task<string> LoginAsync(
        HttpClient http,
        string username,
        string password,
        CancellationToken ct = default)
    {
        var body = new { username, password };
        using var response = await http.PostAsJsonAsync("/api/auth/login", body, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("token", out var prop))
        {
            throw new InvalidOperationException("Login response did not contain 'token'.");
        }

        return prop.GetString()
            ?? throw new InvalidOperationException("'token' in login response was null.");
    }

    private static HttpClient BuildHttpClient(string baseUrl, bool insecure)
    {
        var handler = new HttpClientHandler();
        if (insecure)
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        return new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };
    }
}
