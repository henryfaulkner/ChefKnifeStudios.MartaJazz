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
            await JsRuntime.InvokeVoidAsync("ChefMap.createMap",
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
            await JsRuntime.InvokeVoidAsync("ChefMap.setMapZoom", ElementId, CameraOptions.Zoom);
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
            await JsRuntime.InvokeVoidAsync("ChefMap.setMapZoom", ElementId, zoom);
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
            await JsRuntime.InvokeVoidAsync("ChefMap.toggleTraffic", ElementId, _showTraffic);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    public async Task CenterVehiclePinAsync(int vehicleId)
    {
        try
        {
            await JsRuntime.InvokeVoidAsync("ChefMap.centerVehiclePin", ElementId, vehicleId);
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
            await JsRuntime.InvokeVoidAsync("ChefMap.setMapStyle", ElementId, azureMapStyle);
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

    public async Task PlotVehiclesAsync(object? mapFeatureCollection, bool centerMap = true)
    {
        if (mapFeatureCollection != null)
        {
            try
            {
                await JsRuntime.InvokeVoidAsync("ChefMap.plotFeatures",
                    ElementId, "vehicles", mapFeatureCollection, centerMap);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }
    }

    public async Task ShowRouteShapeAsync(string geoJson)
    {
        try
        {
            await JsRuntime.InvokeVoidAsync("ChefMap.showRouteShape", ElementId, geoJson);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    public async Task ClearRouteShapeAsync()
    {
        try
        {
            await JsRuntime.InvokeVoidAsync("ChefMap.clearRouteShape", ElementId);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    public async Task LoadRouteGeometryForAnimationAsync(string routeId, double[][] coordinates)
    {
        try
        {
            Console.WriteLine($"[Map] LoadRouteGeometryForAnimation: routeId={routeId} coords={coordinates.Length}");
            await JsRuntime.InvokeVoidAsync("ChefMapAnimator.loadRouteGeometry", routeId, coordinates);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Map] LoadRouteGeometryForAnimation failed for routeId={routeId}: {ex}");
        }
    }

    public async Task ProcessNearestPointBatchAsync(object[] records)
    {
        try
        {
            Console.WriteLine($"[Map] ProcessNearestPointBatch: {records.Length} records → JS");
            await JsRuntime.InvokeVoidAsync("ChefMapAnimator.processNearestPointBatch", ElementId, records);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Map] ProcessNearestPointBatch failed: {ex}");
        }
    }
}
