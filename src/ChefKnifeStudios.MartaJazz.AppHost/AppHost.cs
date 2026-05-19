using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.ChefKnifeStudios_MartaJazz_Server_WebAPI>("apiservice")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.ChefKnifeStudios_MartaJazz_Client_WebApp>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.AddProject<Projects.ChefKnifeStudios_MartaJazz_Server_TransitDataWorker>("transitdataworker")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
