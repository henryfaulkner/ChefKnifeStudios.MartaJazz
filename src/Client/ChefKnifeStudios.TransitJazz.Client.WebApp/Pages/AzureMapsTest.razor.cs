using ChefKnifeStudios.TransitJazz.Client.Shared.Components;
using ChefKnifeStudios.TransitJazz.Client.Shared.Models;
using Microsoft.AspNetCore.Components;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Client.WebApp.Pages;

public partial class AzureMapsTest : ComponentBase
{
    static CameraOptions DefaultCameraOptions
        => new() { Center = new Position(33.7680, -84.3640), Zoom = 15 };

    async Task MapOnReadyAsync(Map sender)
    {
        await Task.CompletedTask;
    }
}
