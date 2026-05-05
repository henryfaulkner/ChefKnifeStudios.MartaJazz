using Ardalis.Result;
using ChefKnifeStudios.TransitJazz.Client.Core.Enums;
using ChefKnifeStudios.TransitJazz.Shared;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.TransitJazz.Client.Core.Services.EndpointsServices;

public interface IMapsEndpointsService
{
    Task<Result<Discard>> GetMapsAuthToken(CancellationToken cancellationToken = default);
}

public class MapsEndpointsService
{
    readonly ILogger<MapsEndpointsService> _logger;
    readonly IHttpService _httpService;

    public MapsEndpointsService(
        ILogger<MapsEndpointsService> logger,
        IHttpServiceFactory httpClientFactory)
    {
        _logger = logger;
        _httpService = httpClientFactory.Create(nameof(APIs.TransitJazzAPI));
    }

    public async Task<Result<Discard>> GetMapsAuthToken(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _httpService.GetAsync<Discard>(ApiEndpoints.Maps.GetMapsAuthToken, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetMapsAuthToken. TraceIdentifier: {TraceId}", Guid.NewGuid());
            return Result.Error("An unexpected error occurred.");
        }
    }
}
