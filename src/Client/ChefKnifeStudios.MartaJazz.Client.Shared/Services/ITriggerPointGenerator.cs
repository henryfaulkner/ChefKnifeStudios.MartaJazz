using ChefKnifeStudios.MartaJazz.Client.Shared.Models;
using System.Collections.Generic;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Services;

public interface ITriggerPointGenerator
{
    IReadOnlyList<TriggerPoint> Generate(double[][] coords, double[] cumDist);
}
