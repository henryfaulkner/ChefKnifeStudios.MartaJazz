using ChefKnifeStudios.MartaJazz.Shared.Events;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker;

public interface ITransitHubPublisher
{
    Task StartAsync(CancellationToken ct = default);
    Task<bool> PublishBatchAsync(List<EventEnvelope> batch, CancellationToken ct = default);
}
