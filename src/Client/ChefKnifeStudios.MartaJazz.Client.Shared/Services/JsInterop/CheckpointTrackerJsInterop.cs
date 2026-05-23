using ChefKnifeStudios.MartaJazz.Client.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Services.JsInterop;

public class CheckpointTrackerJsInterop : ICheckpointTrackerJsInterop
{
    readonly Lazy<Task<IJSObjectReference>> _moduleTask;
    readonly ILogger<CheckpointTrackerJsInterop> _logger;

    public CheckpointTrackerJsInterop(
        IJSRuntime jsRuntime,
        ILogger<CheckpointTrackerJsInterop> logger)
    {
        _logger = logger;
        _moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", $"./_content/ChefKnifeStudios.MartaJazz.Client.Shared/js/checkpoint-tracker.js?g={Guid.NewGuid().ToString().ToLower()}").AsTask());
    }

    public async Task ConfigureRouteAsync(string routeId, TriggerPoint[] triggerPoints, DotNetObjectReference<object> dotNetRef)
    {
        try
        {
            var module = await _moduleTask.Value;
            var jsPoints = triggerPoints.Select(p => new { index = p.Index, alongDistanceM = p.AlongDistanceM }).ToArray();
            await module.InvokeVoidAsync("configureRoute", routeId, jsPoints, dotNetRef);
        }
        catch (Exception ex) { LogError(ex, nameof(ConfigureRouteAsync)); }
    }

    public async Task ClearAsync()
    {
        try
        {
            var module = await _moduleTask.Value;
            await module.InvokeVoidAsync("clear");
        }
        catch (Exception ex) { LogError(ex, nameof(ClearAsync)); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_moduleTask.IsValueCreated)
        {
            try
            {
                var module = await _moduleTask.Value;
                await module.InvokeVoidAsync("clear");
                await module.DisposeAsync();
            }
            catch (Exception ex) { LogError(ex, nameof(DisposeAsync)); }
        }
    }

    void LogError(Exception ex, string method)
    {
        _logger.LogError(ex, "CheckpointTrackerJsInterop.{Method} encountered a JavaScript error: {Message}", method, ex.Message);
    }
}
