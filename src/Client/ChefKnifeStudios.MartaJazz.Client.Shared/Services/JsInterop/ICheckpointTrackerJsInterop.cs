using ChefKnifeStudios.MartaJazz.Client.Shared.Models;
using Microsoft.JSInterop;
using System;
using System.Threading.Tasks;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Services.JsInterop;

public interface ICheckpointTrackerJsInterop : IAsyncDisposable
{
    Task ConfigureRouteAsync(string routeId, TriggerPoint[] triggerPoints, DotNetObjectReference<object> dotNetRef);
    Task ClearAsync();
}
