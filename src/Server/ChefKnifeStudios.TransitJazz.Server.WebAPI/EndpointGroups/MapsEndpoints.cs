using Azure.Core;
using Azure.Identity;
using ChefKnifeStudios.TransitJazz.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.EndpointGroups;

public static class MapsEndpoints
{
    public static IEndpointRouteBuilder MapMapsEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup(string.Empty)
            .WithName(nameof(ApiEndpoints.Maps))
            .WithTags(nameof(ApiEndpoints.Maps));

        group.MapGet(ApiEndpoints.Maps.GetMapsAuthToken, async (
            [FromServices] IConfiguration configuration,
            [FromServices] ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken = default) =>
        {
            var logger = loggerFactory.CreateLogger(nameof(MapsEndpoints));
            try
            {
                var credOptions = new DefaultAzureCredentialOptions();

                var managedIdentityClientId = configuration.GetValue<string>("AzureMaps:ManagedIdentityClientId");
                var tenantId = configuration.GetValue<string>("AzureMaps:TenantId");

                if (!string.IsNullOrWhiteSpace(managedIdentityClientId)) credOptions.ManagedIdentityClientId = managedIdentityClientId;
                if (!string.IsNullOrWhiteSpace(tenantId)) credOptions.TenantId = tenantId;

                DefaultAzureCredential tokenProvider = new(credOptions);

                var accessToken = await tokenProvider.GetTokenAsync(
                    new TokenRequestContext(["https://atlas.microsoft.com/.default"]),
                    cancellationToken);

                return Results.Text(accessToken.Token);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception in Maps.GetMapsAuthToken endpoint. TraceIdentifier: {TraceId}", context.TraceIdentifier);
                return Results.Problem("Failed to retrieve Azure Maps token.");
            }
        })
        .WithName(nameof(ApiEndpoints.Maps.GetMapsAuthToken))
        .Produces<IEnumerable<string>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        return group;
    }
}