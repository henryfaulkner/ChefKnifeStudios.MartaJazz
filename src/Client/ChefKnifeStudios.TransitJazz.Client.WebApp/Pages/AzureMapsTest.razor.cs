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
         * - should have a default location, as fallback if no jobsites are available to show on map
         * - use selected customer's location
         */
        => new() { Center = new Position(33.7680, -84.3640), Zoom = 15 };

    async Task MapOnReadyAsync(Map sender)
    {
        var jobsites = SampleDataHelper.Jobsites;

        var featureCollection = new
        {
            type = "FeatureCollection",
            features = jobsites.Select(s => new
            {
                type = "Feature",
                id = $"job-site-{s.JobsiteId}",
                properties = new
                {
                    waJobsiteId = s.JobsiteId,
                    waJobsiteName = s.JobsiteName,
                    pinIcon = s.IsFavorite ? "stop-pin-red.png" :
                        s.IsActive ? "stop-pin-green.png" : "stop-pin-blue.png"
                },
                geometry = new
                {
                    type = "Point",
                    coordinates = new[] { s.Longitude, s.Latitude }
                }
            }).ToArray(),
        };

        await sender.PlotJobSitesAsync(featureCollection);

        // await sender.CenterJobsitePinAsync(42187);
    }
}

public static class SampleDataHelper
{
    public static IReadOnlyList<JobsiteData> Jobsites =>
    [
        new(JobsiteId: 42183,
            JobsiteName: "Ponce City Market",
            JobsiteAddress: "675 Ponce De Leon Ave NE, Atlanta, GA 30308",
            Latitude: 33.7721,
            Longitude: -84.3660,
            IsFavorite: true),

        new(JobsiteId: 42184,
            JobsiteName: "Krog Street Market",
            JobsiteAddress: "99 Krog St NE, Atlanta, GA 30307",
            Latitude: 33.7558,
            Longitude: -84.3601),

        new(JobsiteId: 42186,
            JobsiteName: "Inman Park MARTA Station",
            JobsiteAddress: "1015 Dekalb Ave NE, Atlanta, GA 30307",
            Latitude: 33.7596,
            Longitude: -84.3524,
            IsActive: false),

        new(JobsiteId: 42187,
            JobsiteName: "The BeltLine Eastside Trail",
            JobsiteAddress: "Auburn Ave NE & Randolph St NE, Atlanta, GA 30312",
            Latitude: 33.7620,
            Longitude: -84.3700),

        new(JobsiteId: 42188,
            JobsiteName: "Ford Factory Lofts",
            JobsiteAddress: "699 Ponce De Leon Ave NE, Atlanta, GA 30308",
            Latitude: 33.7726,
            Longitude: -84.3645,
            IsActive: false)
    ];
}

public record JobsiteData(
    int JobsiteId,
    string JobsiteName,
    string JobsiteAddress,
    double Latitude,
    double Longitude,
    bool IsActive = true,
    bool IsFavorite = false);