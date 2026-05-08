using ChefKnifeStudios.TransitJazz.Client.Shared.Components;
using ChefKnifeStudios.TransitJazz.Client.Shared.Models;
using Microsoft.AspNetCore.Components;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Client.WebApp.Pages;

public partial class AzureMapsTest : ComponentBase
{
    static CameraOptions DefaultCameraOptions
        /*
         * NOTE:
         * - should have a default location, as fallback if no Vehicles are available to show on map
         * - use selected customer's location
         */
        => new() { Center = new Position(33.7680, -84.3640), Zoom = 15 };

    async Task MapOnReadyAsync(Map sender)
    {
        var Vehicles = SampleDataHelper.Vehicles;

        var featureCollection = new
        {
            type = "FeatureCollection",
            features = Vehicles.Select(s => new
            {
                type = "Feature",
                id = $"job-site-{s.VehicleId}",
                properties = new
                {
                    waVehicleId = s.VehicleId,
                    waVehicleName = s.VehicleName,
                    pinIcon = "stop-pin-red",
                },
                geometry = new
                {
                    type = "Point",
                    coordinates = new[] { s.Longitude, s.Latitude }
                }
            }).ToArray(),
        };

        await sender.PlotVehiclesAsync(featureCollection);

        // await sender.CenterVehiclePinAsync(42187);
    }
}

public static class SampleDataHelper
{
    public static IReadOnlyList<VehicleData> Vehicles =>
    [
        new(VehicleId: 42183,
            VehicleName: "Ponce City Market",
            VehicleAddress: "675 Ponce De Leon Ave NE, Atlanta, GA 30308",
            Latitude: 33.7721,
            Longitude: -84.3660),

        new(VehicleId: 42184,
            VehicleName: "Krog Street Market",
            VehicleAddress: "99 Krog St NE, Atlanta, GA 30307",
            Latitude: 33.7558,
            Longitude: -84.3601),

        new(VehicleId: 42186,
            VehicleName: "Inman Park MARTA Station",
            VehicleAddress: "1015 Dekalb Ave NE, Atlanta, GA 30307",
            Latitude: 33.7596,
            Longitude: -84.3524),

        new(VehicleId: 42187,
            VehicleName: "The BeltLine Eastside Trail",
            VehicleAddress: "Auburn Ave NE & Randolph St NE, Atlanta, GA 30312",
            Latitude: 33.7620,
            Longitude: -84.3700),

        new(VehicleId: 42188,
            VehicleName: "Ford Factory Lofts",
            VehicleAddress: "699 Ponce De Leon Ave NE, Atlanta, GA 30308",
            Latitude: 33.7726,
            Longitude: -84.3645)
    ];
}

public record VehicleData(
    int VehicleId,
    string VehicleName,
    string VehicleAddress,
    double Latitude,
    double Longitude);