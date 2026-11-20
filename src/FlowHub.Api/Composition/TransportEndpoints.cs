using FlowHub.Dispatching;
using FlowHub.Dispatching.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FlowHub.Api.Composition;

/// <summary>
/// 搬送の HTTP の窓口。画面ができたら、画面から呼ぶ。
/// </summary>
internal static class TransportEndpoints
{
    public static RouteGroupBuilder MapTransportEndpoints(this IEndpointRouteBuilder endpoints, DispatchingService dispatching)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(dispatching);

        var group = endpoints.MapGroup("/transports");

        // 搬送を取りやめる（割り当てたあとでも、AGV へのコマンドの単位で取りやめる）
        group.MapPost("/{id:long}/cancel", async (long id, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await dispatching.CancelAsync(new TransportId(id), cancellationToken).ConfigureAwait(false);
                return Results.Json(new { result = ToText(result) });
            }
            catch (InvalidOperationException)
            {
                return Results.NotFound(); // 知らない搬送
            }
        });
        return group;
    }

    private static string ToText(TransportCancelResult result) => result switch
    {
        TransportCancelResult.Canceled => "canceled",
        TransportCancelResult.Canceling => "canceling",
        TransportCancelResult.Completing => "completing",
        _ => "notCancelable",
    };
}
