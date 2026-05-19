using ChefKnifeStudios.MartaJazz.Shared.EventData;
using ChefKnifeStudios.MartaJazz.Shared.Events;
using System.Collections.Generic;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker;

public static class EventMapper
{
    public static VehicleData ToVehicleData(VehicleDescriptor v, VehiclePosition vp)
    {
        return new VehicleData(
            v?.Id ?? string.Empty,
            v?.Label,
            v?.LicensePlate,
            vp?.OccupancyStatus,
            vp?.OccupancyPercentage != null ? (int?)vp.OccupancyPercentage.Value : null
        );
    }

    public static PositionData ToPositionData(Position p, VehiclePosition vp)
    {
        if (p == null) return null!;

        return new PositionData(
            p.Latitude,
            p.Longitude,
            p.Bearing,
            p.Speed,
            p.Odometer,
            vp?.Timestamp != null ? (long?)vp.Timestamp.Value : null,
            vp?.CurrentStopSequence,
            vp?.StopId,
            vp?.CurrentStatus,
            null
        );
    }

    public static TripData? ToTripData(TripDescriptor? t)
    {
        if (t == null) return null;

        return new TripData(
            t.TripId,
            t.RouteId,
            t.DirectionId != null ? (int?)t.DirectionId.Value : null,
            t.StartTime,
            t.StartDate,
            t.ScheduleRelationship
        );
    }

    public static StopTimeData ToStopTimeData(StopTimeUpdate stu)
    {
        if (stu == null) return null!;

        return new StopTimeData(
            stu.StopId,
            stu.StopSequence,
            stu.Arrival?.Time,
            stu.Arrival?.Delay,
            stu.Arrival?.Uncertainty,
            stu.Departure?.Time,
            stu.Departure?.Delay,
            stu.Departure?.Uncertainty,
            stu.ScheduleRelationship
        );
    }

    public static AlertData ToAlertData(Alert a)
    {
        if (a == null) return null!;

        return new AlertData(
            ResolveTranslation(a.HeaderText),
            ResolveTranslation(a.DescriptionText),
            ResolveTranslation(a.Url),
            a.Cause ?? AlertCause.UnknownCause,
            a.Effect ?? AlertEffect.UnknownEffect,
            a.SeverityLevel ?? AlertSeverity.UnknownSeverity,
            a.ActivePeriod.FirstOrDefault()?.Start != null ? (long?)a.ActivePeriod.FirstOrDefault()!.Start!.Value : null,
            a.ActivePeriod.FirstOrDefault()?.End != null ? (long?)a.ActivePeriod.FirstOrDefault()!.End!.Value : null,
            a.InformedEntity.Where(e => !string.IsNullOrEmpty(e.RouteId)).Select(e => e.RouteId!).ToList(),
            a.InformedEntity.Where(e => !string.IsNullOrEmpty(e.StopId)).Select(e => e.StopId!).ToList()
        );
    }

    public static string? ResolveTranslation(TranslatedString? ts)
    {
        if (ts?.Translation == null || ts.Translation.Count == 0)
            return null;

        var englishTranslation = ts.Translation.FirstOrDefault(t => t.Language == "en");
        if (englishTranslation != null)
            return englishTranslation.Text;

        var untaggedTranslation = ts.Translation.FirstOrDefault(t => string.IsNullOrEmpty(t.Language));
        if (untaggedTranslation != null)
            return untaggedTranslation.Text;

        return ts.Translation[0].Text;
    }

    public static bool IsAlertActive(Alert a, DateTimeOffset now)
    {
        var nowSec = now.ToUnixTimeSeconds();

        foreach (var period in a.ActivePeriod)
        {
            var start = period.Start is null or 0 ? long.MinValue : (long)period.Start.Value;
            var end = period.End is null or 0 ? long.MaxValue : (long)period.End.Value;

            if (start <= nowSec && nowSec < end)
                return true;
        }

        return false;
    }
}
