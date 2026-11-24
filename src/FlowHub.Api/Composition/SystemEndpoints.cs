using FlowHub.Dispatching.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FlowHub.Api.Composition;

/// <summary>
/// システムの運転状態（停止とスタート）を切り替える HTTP の窓口。画面ができたら、画面から呼ぶ。
/// </summary>
internal static class SystemEndpoints
{
    public static RouteGroupBuilder MapSystemEndpoints(this IEndpointRouteBuilder endpoints, DispatchingService dispatching)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(dispatching);

        var group = endpoints.MapGroup("/system");
        group.MapGet("/status", () => Results.Json(new { state = dispatching.IsRunning ? "started" : "stopped" }));
        group.MapPost("/start", async (CancellationToken cancellationToken) =>
        {
            await dispatching.StartAsync(cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        });
        group.MapPost("/stop", async (CancellationToken cancellationToken) =>
        {
            await dispatching.StopAsync(cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        });
        return group;
    }
}
