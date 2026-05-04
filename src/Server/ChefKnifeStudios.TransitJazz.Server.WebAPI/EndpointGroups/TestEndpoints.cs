using Ardalis.Result;
using ChefKnifeStudios.TransitJazz.Server.Core.Interfaces;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.DTOs.SignalR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using Endpoints = ChefKnifeStudios.TransitJazz.Shared.TransitJazzApiEndpoints.Test;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.EndpointGroups;

public static class TestEndpoints
{
    public static IEndpointRouteBuilder MapTestEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup(string.Empty)
            .WithName(nameof(TransitJazzApiEndpoints.Test))
            .WithTags(nameof(TransitJazzApiEndpoints.Test));

        group.MapPost(Endpoints.SignalR, static async (
            [FromBody] TransitJazzNotification notification,
            [FromServices] ITransitJazzNotificationHelper notificationHelper,
            [FromServices] ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken ct = default) =>
        {
            var logger = loggerFactory.CreateLogger(nameof(TestEndpoints));
            try
            {
                await notificationHelper.BroadcastToGroupAsync("test", notification, ct);
                return Result.Success();
            }
                catch (ApplicationException ex)
                {
                logger.LogError(ex, "Exception in Test.SignalR endpoint. TraceIdentifier: {TraceId}", context.TraceIdentifier);
                return Result.Error("An unexpected error occurred.");
            }
                catch (Exception ex)
                {
                logger.LogError(ex, "Exception in Test.SignalR endpoint. TraceIdentifier: {TraceId}", context.TraceIdentifier);
                return Result.CriticalError("An unexpected error occurred.");
            }
        })
        .WithName(nameof(Endpoints.SignalR))
        .Produces<IEnumerable<string>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        return builder;
    }
}
