using Ardalis.Result;
using ChefKnifeStudios.TransitJazz.Client.Core.Enums;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.TransitJazz.Client.Core.Services.EndpointsServices;

public interface IGtfsEndpointsService
{
    Task<Result<RouteShapeFeature>> GetRouteShape(string routeId, CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<RouteShapeFeature>>> GetAllRouteShapes(CancellationToken cancellationToken = default);
}

public class GtfsEndpointsService : IGtfsEndpointsService
{
    readonly ILogger<GtfsEndpointsService> _logger;
    readonly IHttpService _httpService;

    public GtfsEndpointsService(
        ILogger<GtfsEndpointsService> logger,
        IHttpServiceFactory httpClientFactory)
    {
        _logger = logger;
        _httpService = httpClientFactory.Create(nameof(APIs.TransitJazzAPI));
    }

    public async Task<Result<RouteShapeFeature>> GetRouteShape(string routeId, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = ApiEndpoints.Gtfs.GetRouteShape.Replace("{routeId}", routeId);
            var result = await _httpService.GetAsync<RouteShapeFeature>(url, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetRouteShape. TraceIdentifier: {TraceId}", Guid.NewGuid());
            return Result.Error("An unexpected error occurred.");
        }
    }

    public async Task<Result<IEnumerable<RouteShapeFeature>>> GetAllRouteShapes(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _httpService.GetAsync<IEnumerable<RouteShapeFeature>>(ApiEndpoints.Gtfs.GetAllRouteShapes, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetAllRouteShapes. TraceIdentifier: {TraceId}", Guid.NewGuid());
            return Result.Error("An unexpected error occurred.");
        }
    }
}
