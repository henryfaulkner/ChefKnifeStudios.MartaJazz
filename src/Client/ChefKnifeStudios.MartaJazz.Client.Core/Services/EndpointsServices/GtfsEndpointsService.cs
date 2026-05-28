using Ardalis.Result;
using ChefKnifeStudios.MartaJazz.Client.Core.Enums;
using ChefKnifeStudios.MartaJazz.Shared;
using ChefKnifeStudios.MartaJazz.Shared.GtfsData;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.MartaJazz.Client.Core.Services.EndpointsServices;

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
            _logger.LogDebug("GtfsEndpointsService.GetAllRouteShapes: requesting {Url}", ApiEndpoints.Gtfs.GetAllRouteShapes);
            var result = await _httpService.GetAsync<IEnumerable<RouteShapeFeature>>(ApiEndpoints.Gtfs.GetAllRouteShapes, cancellationToken);

            if (result.IsSuccess)
            {
                var list = result.Value?.ToList();
                _logger.LogDebug("GtfsEndpointsService.GetAllRouteShapes: deserialized {Count} features", list?.Count ?? 0);
                if (list is not null)
                {
                    foreach (var f in list)
                    {
                        _logger.LogDebug(
                            "  route: RouteId={RouteId} ShortName={ShortName} CoordCount={CoordCount} Color={Color} GeomType={GeomType}",
                            f.Properties?.RouteId,
                            f.Properties?.RouteShortName,
                            f.Geometry?.Coordinates?.Length ?? -1,
                            f.Properties?.Color,
                            f.Geometry?.Type);
                    }
                }
            }
            else
            {
                _logger.LogWarning("GtfsEndpointsService.GetAllRouteShapes: non-success result — Status={Status} Errors={Errors}",
                    result.Status, string.Join("; ", result.Errors));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetAllRouteShapes. TraceIdentifier: {TraceId}", Guid.NewGuid());
            return Result.Error("An unexpected error occurred.");
        }
    }
}
