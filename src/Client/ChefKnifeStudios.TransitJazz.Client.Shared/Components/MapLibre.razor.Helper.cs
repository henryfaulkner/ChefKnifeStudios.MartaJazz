using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Components;

public partial class MapLibre : ComponentBase
{
    async Task CreateMapAsync()
    {
        try
        {
            await JsRuntime.InvokeVoidAsync("ChefMapLibre.createMap",
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
            await JsRuntime.InvokeVoidAsync("ChefMapLibre.setMapZoom", ElementId, CameraOptions.Zoom);
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
            await JsRuntime.InvokeVoidAsync("ChefMapLibre.setMapZoom", ElementId, zoom);
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
            await JsRuntime.InvokeVoidAsync("ChefMapLibre.centerVehiclePin", ElementId, vehicleId);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    public async Task PlotVehiclesAsync(object? mapFeatureCollection, bool centerMap = true)
    {
        if (mapFeatureCollection != null)
        {
            try
            {
                await JsRuntime.InvokeVoidAsync("ChefMapLibre.plotFeatures",
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
            await JsRuntime.InvokeVoidAsync("ChefMapLibre.showRouteShape", ElementId, geoJson);
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
            await JsRuntime.InvokeVoidAsync("ChefMapLibre.clearRouteShape", ElementId);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    public async Task AddRouteShapeFeatureAsync(string routeId, double[][] coordinates, string? color)
    {
        try
        {
            await JsRuntime.InvokeVoidAsync("ChefMapLibre.addRouteShapeFeature", ElementId, routeId, coordinates, color);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapLibre] AddRouteShapeFeature failed for routeId={routeId}: {ex}");
        }
    }

    public async Task LoadRouteGeometryForAnimationAsync(string routeId, double[][] coordinates)
    {
        try
        {
            Console.WriteLine($"[MapLibre] LoadRouteGeometryForAnimation: routeId={routeId} coords={coordinates.Length}");
            await JsRuntime.InvokeVoidAsync("ChefMapLibreAnimator.loadRouteGeometry", routeId, coordinates);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapLibre] LoadRouteGeometryForAnimation failed for routeId={routeId}: {ex}");
        }
    }

    public async Task ProcessNearestPointBatchAsync(object[] records)
    {
        try
        {
            Console.WriteLine($"[MapLibre] ProcessNearestPointBatch: {records.Length} records → JS");
            await JsRuntime.InvokeVoidAsync("ChefMapLibreAnimator.processNearestPointBatch", ElementId, records);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapLibre] ProcessNearestPointBatch failed: {ex}");
        }
    }
}
