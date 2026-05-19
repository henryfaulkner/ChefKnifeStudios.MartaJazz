using System.Collections.Generic;
using ChefKnifeStudios.MartaJazz.Shared.Events;
using System.Threading;
using System.Threading.Tasks;

namespace ChefKnifeStudios.MartaJazz.Shared;

public interface ITransitHubPublisher
{
    Task PublishBatchAsync(List<EventEnvelope> batch, CancellationToken ct = default);
}
