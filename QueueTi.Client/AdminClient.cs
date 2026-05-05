using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace QueueTi;

public sealed class AdminClient : IDisposable, IAsyncDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly TokenStore? _tokenStore;
    private readonly ILogger<AdminClient> _logger;
    private readonly bool _ownsHttpClient;
    private readonly CancellationTokenSource _refreshCts = new();
    private readonly Task _refreshTask;
    private int _disposed;

    public AdminClient(HttpClient httpClient)
        : this(httpClient, options: null, tokenStore: null, ownsHttpClient: false, loggerFactory: null) { }

    internal AdminClient(
        HttpClient httpClient,
        QueueTiClientOptions? options,
        TokenStore? tokenStore,
        bool ownsHttpClient,
        ILoggerFactory? loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _http = httpClient;
        _ownsHttpClient = ownsHttpClient;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<AdminClient>();

        _tokenStore = tokenStore ?? (options?.BearerToken is not null ? new TokenStore(options.BearerToken) : null);

        _refreshTask = options?.TokenRefresher is not null && _tokenStore is not null
            ? TokenRefreshLoop.RunAsync(_tokenStore, options.TokenRefresher, _logger, _refreshCts.Token)
            : Task.CompletedTask;
    }

    public static AdminClient Create(
        string baseUrl,
        QueueTiClientOptions options,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentNullException.ThrowIfNull(options);

        TokenStore? store = null;
        HttpMessageHandler handler;

        if (options.BearerToken is not null)
        {
            store = new TokenStore(options.BearerToken);
            handler = new BearerTokenHandler(store) { InnerHandler = BuildHttpClientHandler(options) };
        }
        else
        {
            handler = BuildHttpClientHandler(options);
        }

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };
        return new AdminClient(httpClient, options, store, ownsHttpClient: true, loggerFactory);
    }

    private static HttpClientHandler BuildHttpClientHandler(QueueTiClientOptions options)
    {
        var handler = new HttpClientHandler();
        if (options.Insecure)
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        return handler;
    }

    public void SetToken(string token)
    {
        if (_tokenStore is null)
        {
            throw new InvalidOperationException(
                "Cannot set token: no TokenStore is configured. Provide a BearerToken in QueueTiClientOptions.");
        }

        _tokenStore.Set(token);
    }

    // Tier 1 — topic config

    public async Task<List<TopicConfig>> ListTopicConfigsAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("/api/topic-configs", ct);
        await CheckResponseAsync(response);
        return (await response.Content.ReadFromJsonAsync<List<TopicConfig>>(_jsonOptions, ct))!;
    }

    public async Task<TopicConfig> UpsertTopicConfigAsync(
        string topic, TopicConfig config, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(config);

        using var response = await _http.PutAsJsonAsync(
            $"/api/topic-configs/{Uri.EscapeDataString(topic)}", config, _jsonOptions, ct);
        await CheckResponseAsync(response);
        return (await response.Content.ReadFromJsonAsync<TopicConfig>(_jsonOptions, ct))!;
    }

    public async Task DeleteTopicConfigAsync(string topic, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        using var response = await _http.DeleteAsync(
            $"/api/topic-configs/{Uri.EscapeDataString(topic)}", ct);
        await CheckResponseAsync(response);
    }

    // Tier 1 — topic schema

    public async Task<List<TopicSchema>> ListTopicSchemasAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("/api/topic-schemas", ct);
        await CheckResponseAsync(response);
        return (await response.Content.ReadFromJsonAsync<List<TopicSchema>>(_jsonOptions, ct))!;
    }

    public async Task<TopicSchema> GetTopicSchemaAsync(string topic, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        using var response = await _http.GetAsync(
            $"/api/topic-schemas/{Uri.EscapeDataString(topic)}", ct);
        await CheckResponseAsync(response);
        return (await response.Content.ReadFromJsonAsync<TopicSchema>(_jsonOptions, ct))!;
    }

    public async Task<TopicSchema> UpsertTopicSchemaAsync(
        string topic, string schemaJson, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaJson);

        var body = new { schemaJson };
        using var response = await _http.PutAsJsonAsync(
            $"/api/topic-schemas/{Uri.EscapeDataString(topic)}", body, _jsonOptions, ct);
        await CheckResponseAsync(response);
        return (await response.Content.ReadFromJsonAsync<TopicSchema>(_jsonOptions, ct))!;
    }

    public async Task DeleteTopicSchemaAsync(string topic, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        using var response = await _http.DeleteAsync(
            $"/api/topic-schemas/{Uri.EscapeDataString(topic)}", ct);
        await CheckResponseAsync(response);
    }

    // Tier 2 — consumer groups

    public async Task<List<string>> ListConsumerGroupsAsync(string topic, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        using var response = await _http.GetAsync(
            $"/api/topics/{Uri.EscapeDataString(topic)}/consumer-groups", ct);
        await CheckResponseAsync(response);
        return (await response.Content.ReadFromJsonAsync<List<string>>(_jsonOptions, ct))!;
    }

    public async Task RegisterConsumerGroupAsync(
        string topic, string group, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(group);

        var body = new { group };
        using var response = await _http.PostAsJsonAsync(
            $"/api/topics/{Uri.EscapeDataString(topic)}/consumer-groups", body, _jsonOptions, ct);
        await CheckResponseAsync(response);
    }

    public async Task UnregisterConsumerGroupAsync(
        string topic, string group, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(group);

        using var response = await _http.DeleteAsync(
            $"/api/topics/{Uri.EscapeDataString(topic)}/consumer-groups/{Uri.EscapeDataString(group)}", ct);
        await CheckResponseAsync(response);
    }

    // Tier 3 — stats

    public async Task<List<TopicStat>> StatsAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("/api/stats", ct);
        await CheckResponseAsync(response);
        return (await response.Content.ReadFromJsonAsync<List<TopicStat>>(_jsonOptions, ct))!;
    }

    private static async Task CheckResponseAsync(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new QueueTiNotFoundException(body.Length > 0 ? body : "Resource not found.");
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new QueueTiConflictException(
                body.Length > 0 ? body : "Conflict with current resource state.");
        }

        response.EnsureSuccessStatusCode();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _refreshCts.Cancel();
        try { _refreshTask.GetAwaiter().GetResult(); } catch { }

        if (_ownsHttpClient)
        {
            _http.Dispose();
        }

        _tokenStore?.Dispose();
        _refreshCts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _refreshCts.CancelAsync();
        try { await _refreshTask; } catch { }

        if (_ownsHttpClient)
        {
            _http.Dispose();
        }

        _tokenStore?.Dispose();
        _refreshCts.Dispose();
    }
}
