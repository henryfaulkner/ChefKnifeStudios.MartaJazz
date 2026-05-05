using ChefKnifeStudios.TransitJazz.Server.WebAPI.SignalR;
using ChefKnifeStudios.TransitJazz.Shared.EventData;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.EndpointGroups;

public static class TestEndpoints
{
    public static IEndpointRouteBuilder MapTestEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/test/signalr-ping", async (IHubContext<TransitHub> hub) =>
        {
            var batch = new List<EventEnvelope>
            {
                new EventEnvelope("TestPing", DateTimeOffset.UtcNow, new ArrivalPredictionEvent(null, null, null, null, [])),
            };
            await hub.Clients.All.SendAsync("ReceiveBatch", batch);
            return Results.Ok($"Sent batch of {batch.Count} to all clients.");
        }).AllowAnonymous();

        return builder;
    }
}
