using ChefKnifeStudios.TransitJazz.Server.WebAPI.SignalR;
using ChefKnifeStudios.TransitJazz.Server.Core.Interfaces;
using ChefKnifeStudios.TransitJazz.Server.Infrastructure;
using ChefKnifeStudios.TransitJazz.Server.BL.Services;
using ChefKnifeStudios.TransitJazz.Shared;
using Scalar.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.EndpointGroups;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddProblemDetails();

builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", policy =>
    {
        policy.WithOrigins("https://localhost:7150", "https://localhost:5186", "https://localhost:7333")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.ConfigureHttpJsonOptions(static options =>
{
    var src = JsonOptions.Get();
    var dest = options.SerializerOptions;

    // copy common scalar settings
    dest.PropertyNameCaseInsensitive = src.PropertyNameCaseInsensitive;
    dest.PropertyNamingPolicy = src.PropertyNamingPolicy;
    dest.DictionaryKeyPolicy = src.DictionaryKeyPolicy;
    dest.DefaultIgnoreCondition = src.DefaultIgnoreCondition;
    dest.Converters.Clear();
    foreach (var conv in src.Converters)
    {
        dest.Converters.Add(conv);
    }
    dest.WriteIndented = src.WriteIndented;
});

builder.Services.AddSignalR();

builder.Services.AddSingleton<IUserIdProvider, PlayerIdProvider>();
builder.Services.AddSingleton<ITransitJazzNotificationHelper, TransitJazzNotificationHelper>();
builder.Services.AddSingleton<IPlayerConnectionTracker, PlayerConnectionTracker>();
builder.Services.AddSingleton(typeof(IKeyValueRepository<>), typeof(InMemoryKeyValueRepository<>));
builder.Services.AddSingleton<IFeatureFlagService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var flagsSection = config.GetSection("AppSettings:FeatureFlags");
    var flags = new Dictionary<ChefKnifeStudios.TransitJazz.Shared.Enums.FeatureFlags, bool>();
    if (flagsSection.Exists())
    {
        foreach (var child in flagsSection.GetChildren())
        {
            if (Enum.TryParse<ChefKnifeStudios.TransitJazz.Shared.Enums.FeatureFlags>(child.Key, out var flag))
            {
                flags[flag] = bool.Parse(child.Value ?? "false");
            }
        }
    }
    return new FeatureFlagService(flags);
});

var app = builder.Build();

app.UseExceptionHandler();

app.MapOpenApi().AllowAnonymous();

app.MapScalarApiReference(options =>
{
    options.Title = "TransitJazz API";
    options.Theme = ScalarTheme.Solarized;
    options.Layout = ScalarLayout.Classic;
    options.DarkMode = true;
    options.HiddenClients = true;
    options.DefaultHttpClient = new(ScalarTarget.JavaScript, ScalarClient.Axios);
});

app.UseCors("Default");

app.MapHub<SignalRNotificationHub>("/cks-notification");

app.MapTestEndpoints();

app.MapDefaultEndpoints();

app.Run();
