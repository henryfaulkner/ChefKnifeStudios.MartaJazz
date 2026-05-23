using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker;
using ChefKnifeStudios.MartaJazz.Shared;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHttpClient();
builder.Services.AddHttpClient("RouteShapeApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["services:apiservice:https:0"]
        ?? builder.Configuration["WebApi:BaseUrl"]!);
});
builder.Services.AddSingleton<TokenProvider>();
builder.Services.AddSingleton<ITransitHubPublisher, SignalRHubPublisher>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
