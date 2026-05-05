using ChefKnifeStudios.TransitJazz.Client.Core;
using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Client.Shared.EventArgs;
using MatBlazor;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.Collections.Generic;
using ChefKnifeStudios.TransitJazz.Shared.Enums;
using ChefKnifeStudios.TransitJazz.Shared;
using Blazored.LocalStorage;
using System;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

var appSettings = builder.Configuration.GetSection("AppSettings").Get<AppSettings>();
if (appSettings?.ExternalApis != null)
{
    foreach (var api in appSettings.ExternalApis.Where(a => a.AddHttpClient))
    {
        builder.Services.AddHttpClient(api.Name, client =>
        {
            client.BaseAddress = new Uri(api.BaseUri);
        });
    }
}

builder.Services.AddSingleton<IHttpServiceFactory>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return new HttpServiceFactory(name => factory.CreateClient(name));
});

var featureFlags = appSettings?.FeatureFlags ?? new Dictionary<FeatureFlags, bool>();
builder.Services.AddSingleton<IFeatureFlagService>(_ => new FeatureFlagService(featureFlags));

builder.Services.AddSingleton<IEventNotificationService, EventNotificationService>();

builder.Services.AddMatBlazor();

builder.Services.AddMatToaster(new MatToastConfiguration
{
    Position = MatToastPosition.BottomRight,
    PreventDuplicates = true,
    NewestOnTop = true,
    ShowCloseButton = true,
    ShowProgressBar = true
});

builder.Services.AddBlazoredLocalStorage();

await builder.Build().RunAsync();
