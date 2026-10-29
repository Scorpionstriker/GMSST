using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Engine.Handlers;

/// <summary>
/// Calls an HTTP endpoint on this computer, for example a local service's health check (FR-07). Only loopback
/// addresses are allowed and redirects are not followed, so a task cannot reach other machines (Phase 4, Table 8.1).
/// </summary>
public sealed class LocalHttpRequestHandler : ITaskHandler
{
    public const string Type = "LocalHttpRequest";
    private static readonly string[] AllowedMethods = ["GET", "POST", "PUT", "DELETE", "HEAD"];

    private static readonly HttpClient Client = new(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    public string TaskType => Type;
    public string DisplayName => "Local HTTP request";
    public string Description => "Calls a URL on this computer (localhost only) and checks the status code.";
    public string PayloadExample => """{ "url": "http://localhost:5000/health", "method": "GET", "expectedStatus": 200 }""";

    public async Task<TaskResult> ExecuteAsync(ITaskContext context)
    {
        var url = context.GetString("url");
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return TaskResult.Failure("The payload must contain an absolute http or https \"url\".");
        }

        if (!IsLoopback(uri))
        {
            return TaskResult.Failure($"Only addresses on this computer are allowed (localhost, 127.0.0.1 or ::1), not '{uri.Host}'.");
        }

        var method = (context.GetString("method") ?? "GET").Trim().ToUpperInvariant();
        if (!AllowedMethods.Contains(method))
        {
            return TaskResult.Failure($"\"method\" must be one of {string.Join(", ", AllowedMethods)}.");
        }

        var expected = context.GetInt32("expectedStatus");
        using var request = new HttpRequestMessage(new HttpMethod(method), uri);
        if (context.GetString("body") is { } body)
        {
            request.Content = new StringContent(body, Encoding.UTF8, context.GetString("contentType") ?? "application/json");
        }

        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.CancellationToken);
        var status = (int)response.StatusCode;
        var ok = expected is { } wanted ? status == wanted : response.IsSuccessStatusCode;
        context.Logger.LogInformation("{Method} {Url} returned {Status}", method, uri.GetLeftPart(UriPartial.Path), status);

        var output = new { url = uri.GetLeftPart(UriPartial.Path), method, status };
        return ok
            ? TaskResult.Success($"{method} returned {status}.", output)
            : TaskResult.Failure($"{method} {uri.GetLeftPart(UriPartial.Path)} returned {status}" + (expected is null ? "." : $", expected {expected}."), output);
    }

    internal static bool IsLoopback(Uri uri) =>
        uri.IsLoopback || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));
}
