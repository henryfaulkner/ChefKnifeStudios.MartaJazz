using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Components;

public partial class Map : ComponentBase
{
    public enum MapStyles
    {
        GrayscaleLight,
        Road,
        Satellite,
        Night,
        Hybrid
    }

    async Task CreateMapAsync()
    {
        try
        {
            await JsRuntime.InvokeVoidAsync("OvercastMap.createMap",
                ElementId, DotNetObjectReference.Create(this));
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    public async Task ChangeMapZoomAsync(bool zoomIn)
    {
        try
        {
            CameraOptions.ChangeZoom(zoomIn);
            await JsRuntime.InvokeVoidAsync("OvercastMap.setMapZoom", ElementId, CameraOptions.Zoom);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    public async Task SetMapZoomAsync(int zoom)
    {
        try
        {
            CameraOptions.Zoom = zoom;
            await JsRuntime.InvokeVoidAsync("OvercastMap.setMapZoom", ElementId, zoom);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    public async Task ShowTrafficAsync()
    {
        try
        {
            _showTraffic = !_showTraffic;
            await JsRuntime.InvokeVoidAsync("OvercastMap.toggleTraffic", ElementId, _showTraffic);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    public async Task SetMapStyleAsync(MapStyles style)
    {
        try
        {
            var azureMapStyle = GetAzureMapStyle(style);

            if (azureMapStyle == string.Empty) return;
            await JsRuntime.InvokeVoidAsync("OvercastMap.setMapStyle", ElementId, azureMapStyle);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    static string GetAzureMapStyle(MapStyles style) => style switch
    {
        MapStyles.GrayscaleLight => "grayscale_light",
        MapStyles.Satellite => "satellite",
        MapStyles.Road => "road",
        MapStyles.Night => "night",
        MapStyles.Hybrid => "satellite_road_labels",
        _ => string.Empty,
    };

    public async Task UpsertBusMarkerAsync(string vehicleId, float latitude, float longitude)
    {
        try
        {
            await JsRuntime.InvokeVoidAsync("OvercastMap.upsertBusMarker",
                ElementId, vehicleId, latitude, longitude);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }
}
