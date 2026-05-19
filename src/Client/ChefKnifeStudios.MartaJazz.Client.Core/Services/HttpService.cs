using Ardalis.Result;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ChefKnifeStudios.MartaJazz.Client.Core.Services;

public interface IHttpService
{
    Task<Result<T>> GetAsync<T>(string? requestUri, CancellationToken ct = default);
    Task<Result<T>> PostAsync<X, T>(string? requestUri, X body, CancellationToken ct = default);
    Task<Result> PostAsync<X>(string? requestUri, X body, CancellationToken ct = default);
    Task<Result<T>> PutAsync<X, T>(string? requestUri, X body, CancellationToken ct = default);
    Task<Result<T>> PatchAsync<X, T>(string? requestUri, X body, CancellationToken ct = default);
    Task<Result<T>> DeleteAsync<T>(string? requestUri, CancellationToken ct = default);
}

public class HttpService : IHttpService
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _options;

    public HttpService(HttpClient client)
    {
        _client = client;
        _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<Result<T>> GetAsync<T>(string? requestUri, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.GetAsync(requestUri, ct);
            return await HandleResponse<T>(response, ct);
        }
        catch (Exception ex)
        {
            return Result<T>.Error(ex.Message);
        }
    }

    public async Task<Result<T>> PostAsync<X, T>(string? requestUri, X body, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.PostAsJsonAsync(requestUri, body, _options, ct);
            return await HandleResponse<T>(response, ct);
        }
        catch (Exception ex)
        {
            return Result<T>.Error(ex.Message);
        }
    }

    public async Task<Result> PostAsync<X>(string? requestUri, X body, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.PostAsJsonAsync(requestUri, body, _options, ct);
            return response.IsSuccessStatusCode ? Result.Success() : Result.Error(response.ReasonPhrase ?? "Error");
        }
        catch (Exception ex)
        {
            return Result.Error(ex.Message);
        }
    }

    public async Task<Result<T>> PutAsync<X, T>(string? requestUri, X body, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.PutAsJsonAsync(requestUri, body, _options, ct);
            return await HandleResponse<T>(response, ct);
        }
        catch (Exception ex)
        {
            return Result<T>.Error(ex.Message);
        }
    }

    public async Task<Result<T>> PatchAsync<X, T>(string? requestUri, X body, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.PatchAsJsonAsync(requestUri, body, _options, ct);
            return await HandleResponse<T>(response, ct);
        }
        catch (Exception ex)
        {
            return Result<T>.Error(ex.Message);
        }
    }

    public async Task<Result<T>> DeleteAsync<T>(string? requestUri, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.DeleteAsync(requestUri, ct);
            return await HandleResponse<T>(response, ct);
        }
        catch (Exception ex)
        {
            return Result<T>.Error(ex.Message);
        }
    }

    private async Task<Result<T>> HandleResponse<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadFromJsonAsync<T>(_options, ct);
            return Result<T>.Success(content!);
        }

        return response.StatusCode switch
        {
            System.Net.HttpStatusCode.NotFound => Result<T>.NotFound(),
            System.Net.HttpStatusCode.Unauthorized => Result<T>.Unauthorized(),
            System.Net.HttpStatusCode.Forbidden => Result<T>.Forbidden(),
            _ => Result<T>.Error(response.ReasonPhrase ?? "Error")
        };
    }
}
