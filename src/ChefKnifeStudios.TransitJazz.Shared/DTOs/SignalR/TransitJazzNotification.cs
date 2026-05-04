using ChefKnifeStudios.TransitJazz.Shared.Enums;

namespace ChefKnifeStudios.TransitJazz.Shared.DTOs.SignalR;

public record TransitJazzNotification(TransitJazzNotificationType Type, string Message);
