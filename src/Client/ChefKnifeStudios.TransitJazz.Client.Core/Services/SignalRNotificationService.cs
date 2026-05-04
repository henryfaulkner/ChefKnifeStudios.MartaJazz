using ChefKnifeStudios.TransitJazz.Shared.DTOs.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.TransitJazz.Client.Core.Services;

public delegate void TransitJazzNotificationHandler(TransitJazzNotification notification);

public interface ISignalRNotificationService : IAsyncDisposable
{
    event TransitJazzNotificationHandler? HandleNotificationReceived;
    Task InitAsync(string userId);
    Task JoinGroupAsync(string groupId);
    Task LeaveGroupAsync(string groupId);
}

public class SignalRNotificationService : ISignalRNotificationService
{
    private HubConnection? _hubConnection;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SignalRNotificationService> _logger;

    public event TransitJazzNotificationHandler? HandleNotificationReceived;

    public SignalRNotificationService(IConfiguration configuration, ILogger<SignalRNotificationService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task InitAsync(string userId)
    {
        try
        {
            var baseUri = _configuration["AppSettings:ExternalApis:0:BaseUri"] ?? "https://localhost:7269";
            var hubUrl = $"{baseUri}/cks-notification?playerId={userId}";

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On<TransitJazzNotification>("ReceiveTransitJazzNotification", notification =>
            {
                HandleNotificationReceived?.Invoke(notification);
            });

            await _hubConnection.StartAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize SignalR connection");
            _hubConnection = null;
        }
    }

    public async Task JoinGroupAsync(string groupId)
    {
        if (_hubConnection?.State == HubConnectionState.Connected)
            await _hubConnection.InvokeAsync("JoinGroupAsync", groupId);
    }

    public async Task LeaveGroupAsync(string groupId)
    {
        if (_hubConnection?.State == HubConnectionState.Connected)
            await _hubConnection.InvokeAsync("LeaveGroupAsync", groupId);
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection != null)
            await _hubConnection.DisposeAsync();
    }
}
