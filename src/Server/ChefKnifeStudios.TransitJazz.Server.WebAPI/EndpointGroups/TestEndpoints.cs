using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.EndpointGroups;

public static class TestEndpoints
{
    public static IEndpointRouteBuilder MapTestEndpoints(this IEndpointRouteBuilder builder)
    {
        // Test endpoints removed as part of SignalR rearchitecture
        return builder;
    }
}
