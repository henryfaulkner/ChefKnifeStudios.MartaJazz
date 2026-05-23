using System;
using System.Threading.Tasks;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Services.JsInterop;

public interface ITransitSynthJsInterop : IAsyncDisposable
{
    Task UnlockAsync();
    Task<bool> IsUnlockedAsync();
    Task TriggerNoteAsync(string routeId, string vehicleId);
}
