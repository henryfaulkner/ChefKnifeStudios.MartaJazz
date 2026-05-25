using ChefKnifeStudios.MartaJazz.Server.WebAPI.Interfaces;
using ChefKnifeStudios.MartaJazz.Server.WebAPI.Repositories;
using ChefKnifeStudios.MartaJazz.Shared;
using ChefKnifeStudios.MartaJazz.Shared.Events;
using Scalar.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System;
using ChefKnifeStudios.MartaJazz.Server.WebAPI.EndpointGroups;
using ChefKnifeStudios.MartaJazz.Server.WebAPI.GtfsStatic;
using ChefKnifeStudios.MartaJazz.Server.WebAPI.SignalR;
using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddProblemDetails();

builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", policy =>
    {
        policy.WithOrigins("https://localhost:7150", "https://localhost:5186", "https://localhost:7333", "https://localhost:52832", "http://localhost:52833")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddSignalR(options =>
    {
        options.EnableDetailedErrors = true;
        options.MaximumReceiveMessageSize = 1024 * 1024; // 1MB
    })
    .AddJsonProtocol(options => JsonSettings.ApplyTo(options.PayloadSerializerOptions));

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

builder.Services.AddSingleton(typeof(IKeyValueRepository<>), typeof(InMemoryKeyValueRepository<>));
builder.Services.AddHttpClient();
builder.Services.AddHostedService<GtfsStaticLoader>();
builder.Services.AddSingleton<IFeatureFlagService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var flagsSection = config.GetSection("AppSettings:FeatureFlags");
    var flags = new Dictionary<ChefKnifeStudios.MartaJazz.Shared.Enums.FeatureFlags, bool>();
    if (flagsSection.Exists())
    {
        foreach (var child in flagsSection.GetChildren())
        {
            if (Enum.TryParse<ChefKnifeStudios.MartaJazz.Shared.Enums.FeatureFlags>(child.Key, out var flag))
            {
                flags[flag] = bool.Parse(child.Value ?? "false");
            }
        }
    }
    return new FeatureFlagService(flags);
});

builder.Services.AddSingleton<ITransitHubPublisher, SignalRHubPublisher>();
builder.Services.AddHttpClient("RouteShapeApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["services:apiservice:https:0"]
        ?? builder.Configuration["WebApi:BaseUrl"]!);
});
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

app.UseExceptionHandler();

//app.UseAuthentication();
//app.UseAuthorization();

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

app.MapHub<TransitHub>("/hubs/transit").AllowAnonymous();
app.MapHub<WorkerTransitHub>("/hubs/worker-transit")
    .AllowAnonymous();
    //.RequireAuthorization("TransitDataPublisher");

app.MapTestEndpoints()
    .MapGtfsEndpoints();

app.MapDefaultEndpoints();

app.Run();