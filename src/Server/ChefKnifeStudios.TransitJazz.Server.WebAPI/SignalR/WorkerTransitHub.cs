using System.Collections.Generic;
using System.Threading.Tasks;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.SignalR;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.SignalR;

//[Authorize(Policy = "TransitDataPublisher")]
public class WorkerTransitHub : Hub
{
    private readonly IHubContext<TransitHub> _clientHub;
    private readonly ILogger<WorkerTransitHub> _logger;

    public WorkerTransitHub(IHubContext<TransitHub> clientHub, ILogger<WorkerTransitHub> logger)
    {
        _clientHub = clientHub;
        _logger = logger;
    }

    public async Task PublishBatch(List<EventEnvelope> batch)
    {
        await _clientHub.Clients.All.SendAsync("ReceiveBatch", batch);
        _logger.LogInformation("Relayed {Count} events from worker", batch.Count);
    }
}
