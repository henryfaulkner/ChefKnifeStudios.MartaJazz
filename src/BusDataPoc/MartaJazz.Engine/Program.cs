var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient();
builder.Services.AddHostedService<MartaJazz.Engine.Worker>();

var host = builder.Build();
host.Run();
