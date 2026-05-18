namespace ChefKnifeStudios.TransitJazz.Shared;

public static class ApiEndpoints
{
    public static class Test
    {
        public const string SignalR = "/test/signalr";
    }

    public static class Gtfs
    {
        public const string GetRouteShape = "/gtfs/routes/{routeId}/shape";
        public const string GetAllRouteShapes = "/gtfs/routes/shapes";
    }
}
