using System.Net;
using Hangfire.Dashboard;

namespace GameVerse.Api.Hosting;

/// <summary>
/// Who may open /hangfire. For now: only requests from this machine, and only in Development.
/// Everywhere else the dashboard is denied.
/// TODO(gameverse): require the `admin` role once Accounts exists (Prompt 1.1) and the admin console lands (Prompt 2.4).
/// </summary>
internal sealed class HangfireDashboardAuth(IHostEnvironment environment) : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!environment.IsDevelopment())
        {
            return false;
        }

        var connection = context.GetHttpContext().Connection;
        var remote = connection.RemoteIpAddress;
        return remote != null && (IPAddress.IsLoopback(remote) || remote.Equals(connection.LocalIpAddress));
    }
}
