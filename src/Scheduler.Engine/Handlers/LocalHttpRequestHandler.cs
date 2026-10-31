using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Engine.Handlers;

/// <summary>
/// Calls an HTTP endpoint on this computer, for example a local service's health check (FR-07). Only loopback
/// addresses are allowed and redirects are not followed, so a task cannot reach other machines (Phase 4, Table 8.1).
/// </summary>
/// <remarks>
/// WP-1: the optional "headers" object adds request headers. A value written as "env:NAME" is read from the
/// environment variable NAME when the task runs, so a key such as GSST_AGENT_KEY is never stored in the database or
/// written to a log. The optional "notRunningMessage" replaces the error text when nothing listens on the port.
/// "includeResponseInError" includes a response body of at most 2,000 characters in failures when explicitly enabled.
/// </remarks>
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
    public string PayloadExample =>
        """{ "url": "http://127.0.0.1:5087/health", "method": "GET", "expectedStatus": 200, "headers": { "X-GSST-Key": "env:GSST_AGENT_KEY" } }""";

    internal const string EnvironmentPrefix = "env:";

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

        if (AddHeaders(context, request) is { } headerError)
        {
            return TaskResult.Failure(headerError);
        }

        HttpResponseMessage response;
        try
        {
            response = await Client.SendAsync(request, HttpCompletionOption.ResponseContentRead, context.CancellationToken);
        }
        catch (HttpRequestException ex) when (ex.HttpRequestError == HttpRequestError.ConnectionError)
        {
            return TaskResult.Failure(context.GetString("notRunningMessage") is { Length: > 0 } notRunning
                ? notRunning
                : $"Nothing is listening on {uri.GetLeftPart(UriPartial.Authority)}: {ex.Message}");
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            var ok = expected is { } wanted ? status == wanted : response.IsSuccessStatusCode;
            context.Logger.LogInformation("{Method} {Url} returned {Status}", method, uri.GetLeftPart(UriPartial.Path), status);

            var text = await response.Content.ReadAsStringAsync(context.CancellationToken);
            var output = new { url = uri.GetLeftPart(UriPartial.Path), method, status, response = text.Length <= 2000 ? text : text[..2000] };
            var failureMessage = $"{method} {uri.GetLeftPart(UriPartial.Path)} returned {status}" +
                                 (expected is null ? "." : $", expected {expected}.");
            if (!ok && context.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                && context.Payload.TryGetProperty("includeResponseInError", out var includeResponse)
                && includeResponse.ValueKind == System.Text.Json.JsonValueKind.True)
            {
                failureMessage += " Response: " + (text.Length <= 2000 ? text : text[..2000]);
            }

            return ok
                ? TaskResult.Success($"{method} returned {status}.", output)
                : TaskResult.Failure(failureMessage, output);
        }
    }

    /// <summary>Adds the payload's "headers". Returns an error message, or null when all headers were added.</summary>
    internal static string? AddHeaders(ITaskContext context, HttpRequestMessage request)
    {
        if (context.Payload.ValueKind != System.Text.Json.JsonValueKind.Object
            || !context.Payload.TryGetProperty("headers", out var headers) || headers.ValueKind == System.Text.Json.JsonValueKind.Null)
        {
            return null;
        }

        if (headers.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return "\"headers\" must be an object of header names and text values.";
        }

        foreach (var header in headers.EnumerateObject())
        {
            if (header.Value.ValueKind != System.Text.Json.JsonValueKind.String)
            {
                return $"The value of header '{header.Name}' must be text.";
            }

            var value = header.Value.GetString() ?? "";
            if (value.StartsWith(EnvironmentPrefix, StringComparison.Ordinal))
            {
                var variable = value[EnvironmentPrefix.Length..].Trim();
                value = Environment.GetEnvironmentVariable(variable) ?? "";
                if (value.Length == 0)
                {
                    return $"The environment variable {variable} for header '{header.Name}' is not set on this computer.";
                }
            }

            if (!request.Headers.TryAddWithoutValidation(header.Name, value)
                && request.Content?.Headers.TryAddWithoutValidation(header.Name, value) != true)
            {
                return $"The header '{header.Name}' cannot be set.";
            }
        }

        return null;
    }

    internal static bool IsLoopback(Uri uri) =>
        uri.IsLoopback || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));
}
