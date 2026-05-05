using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHttpClient();
builder.Services.AddSingleton<TokenProvider>();
builder.Services.AddSingleton<ITransitHubPublisher, SignalRHubPublisher>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
