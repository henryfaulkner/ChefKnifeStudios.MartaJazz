using ChefKnifeStudios.TransitJazz.Client.Shared.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Components;

public partial class Map : ComponentBase
{
    public string ElementId { get; } = $"cks-map-{Guid.NewGuid()}".ToLower();
    bool _showTraffic;
    
    //[Inject] public IAccessTokenProvider AccessTokenProvider { get; set; } = null!;
    [Inject] public IJSRuntime JsRuntime { get; set; } = null!;
    [Inject] public IConfiguration Configuration { get; set; } = null!;
    [Parameter]
    public CameraOptions CameraOptions { get; set; }
        = new() { Center = new Position(0, 0), Zoom = 1 };
    [Parameter] public EventCallback<Map> OnMapReady { get; set; }
    [Parameter] public EventCallback<Map> OnMapBodyClicked { get; set; }
    [Parameter] public EventCallback<(Map Map, string VehicleId)> OnBusMarkerClicked { get; set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await CreateMapAsync();
        }
    }

    [JSInvokable("notifyMapReadyAsync")]
    public async Task NotifyMapReadyAsync()
    {
        await OnMapReady.InvokeAsync(this);
    }

    [JSInvokable("mapBodyClickedAsync")]
    public async Task MapBodyClickedAsync()
    {
        await OnMapBodyClicked.InvokeAsync(this);
    }

    [JSInvokable("BusMarkerClickedAsync")]
    public async Task BusMarkerClickedAsync(string vehicleId)
    {
        await OnBusMarkerClicked.InvokeAsync((this, vehicleId));
    }

    [JSInvokable("getMapSettings")]
    public async Task<object> GetMapSettings()
    {
        var longitude = CameraOptions.Center.Longitude;
        var latitude = CameraOptions.Center.Latitude;

        var language = CultureInfo.DefaultThreadCurrentCulture?.Name ?? "en-US";
        var tokenApiUrl = "https://localhost:52834/maps/auth/token";
        //var apiToken = await GetBearerTokenAsync();

        return new
        {
            mapAccClientId = Configuration.GetValue<string>("AzureMaps:AccountClientId"),
            tokenApiUrl,
            //apiToken,  // apiToken = "<<bearer token for TJ API>>",
            center = new[] { longitude, latitude },
            zoom = CameraOptions.Zoom,
            language,
            style = GetAzureMapStyle(MapStyles.Road)
        };
    }

    void ChangeMapStyle(MapStyles style) => _ = SetMapStyleAsync(style);

    //async Task<string?> GetBearerTokenAsync()
    //{
    //    var tokenResult = await AccessTokenProvider.RequestAccessToken();

    //    if (tokenResult.TryGetToken(out var token))
    //    {
    //        return token.Value;
    //    }

    //    return null; // Token not available
    //}
}
