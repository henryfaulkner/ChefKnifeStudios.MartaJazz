using System.Collections.Generic;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using System.Threading;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Shared;

public interface ITransitHubPublisher
{
    Task PublishBatchAsync(List<EventEnvelope> batch, CancellationToken ct = default);
}
