using System.Security.Cryptography;
using System.Text;

namespace DemoApps.MaintenanceAgent.Services;

/// <summary>
/// Every endpoint needs the header X-GSST-Key equal to the GSST_AGENT_KEY environment variable; anything else gets
/// 401 (WP-1). When the variable is not set, every request is refused, so the agent is never open by mistake. The key
/// is compared in constant time.
/// </summary>
public sealed class AgentKeyFilter(string? expectedKey) : IEndpointFilter
{
    public bool HasKey => !string.IsNullOrEmpty(expectedKey);

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        IsValid(context.HttpContext.Request.Headers[AgentOptions.KeyHeader].ToString())
            ? next(context)
            : ValueTask.FromResult<object?>(Results.Json(new { error = "Missing or wrong X-GSST-Key header." }, statusCode: StatusCodes.Status401Unauthorized));

    internal bool IsValid(string? presented)
    {
        if (string.IsNullOrEmpty(expectedKey) || string.IsNullOrEmpty(presented))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(expectedKey));
    }
}
