using Ardalis.Result;
using System.Text.Json;

namespace ChefKnifeStudios.TransitJazz.Client.Core.Services;

public interface IHttpServiceFactory
{
    IHttpService Create(string name);
}

public class HttpServiceFactory : IHttpServiceFactory
{
    private readonly Func<string, HttpClient> _httpClientFactory;

    public HttpServiceFactory(Func<string, HttpClient> httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public IHttpService Create(string name)
    {
        var client = _httpClientFactory(name);
        return new HttpService(client);
    }
}
