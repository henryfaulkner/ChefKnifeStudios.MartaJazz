using ChefKnifeStudios.TransitJazz.Client.Core.Enums;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace ChefKnifeStudios.TransitJazz.Client.Core.Services;

public delegate Task SignalRNotificationHandler(List<EventEnvelope> batch);

public interface ISignalRNotificationService
{
    event SignalRNotificationHandler? NotificationReceived;

    Task InitAsync(CancellationToken ct = default);
}

public class SignalRNotificationService(
        IConfiguration configuration,
        IWebAssemblyHostEnvironment hostEnvironment,
        ILogger<SignalRNotificationService> logger) : ISignalRNotificationService
{
    private HubConnection? _hubConnection;
    public event SignalRNotificationHandler? NotificationReceived;

    public async Task InitAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Starting SignalRNotificationService.InitAsync");
        if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
        {
            
            return;
        }

        try
        {
            CloseConnection();

            var apis = configuration.GetSection("AppSettings:ExternalApis");
            var itemArray = apis.GetChildren();

            var setting = itemArray.FirstOrDefault(a =>
                a.GetValue<string>("Name") == nameof(APIs.TransitJazzSignalR));

            if (setting != null)
            {
                var baseUrl = setting.GetValue("BaseUri", string.Empty)?.TrimEnd('/');
                if (baseUrl is null)
                {
                    string errMsg = "BaseUrl for PokerAttackSignalR API config is null.";
                    logger.LogCritical(errMsg);
                    throw new ApplicationException(errMsg);
                }

                Uri baseUri;
                if (Uri.IsWellFormedUriString(baseUrl, UriKind.Absolute))
                {
                    baseUri = new Uri(baseUrl);
                }
                else
                {
                    var hostUri = new Uri(hostEnvironment.BaseAddress, UriKind.Absolute);
                    var relativeUri = new Uri(baseUrl, UriKind.Relative);
                    baseUri = new Uri(hostUri, relativeUri);
                }

                var url = $"{baseUri.ToString().TrimEnd('/')}/hubs/transit";

                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(url)
                    .WithAutomaticReconnect()
                    .ConfigureLogging(logging =>
                    {
                        logging.SetMinimumLevel(LogLevel.Debug);
                    })
                    .AddJsonProtocol(options =>
                    {
                        JsonSettings.ApplyTo(options.PayloadSerializerOptions);
                    })
                    .Build();

                logger.LogInformation("Connecting to SignalR hub: {host}", baseUri.Host);

                _hubConnection.On<List<EventEnvelope>>("ReceiveBatch", batch =>
                {
                    logger.LogInformation("Received batch of {count} events from SignalR hub", batch.Count);
                    NotificationReceived?.Invoke(batch);
                });

                await _hubConnection.StartAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error initializing SignalR Notification Hub");
            _hubConnection = null;
        }
        finally
        {
            logger.LogInformation("Ending SignalRNotificationService.InitAsync");
        }
    }

    public void Dispose()
    {
        try
        {
            CloseConnection();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error disposing SignalR connection");
            throw;
        }
    }

    private void CloseConnection()
    {
        try
        {
            if (_hubConnection == null) return;
            _ = _hubConnection.StopAsync();
            _hubConnection = null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error closing SignalR connection");
            throw;
        }
    }

    private Task<bool> EnsureConnectedAsync(string operationName)
    {
        if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
        {
            return Task.FromResult(true);
        }

        logger.LogWarning(
            "SignalR operation '{operation}' attempted but connection not established. State: {state}. Automatic reconnect will handle this.",
            operationName,
            _hubConnection?.State.ToString() ?? "null");

        return Task.FromResult(false);
    }
}