namespace WallFlow.Services
{
    public static class ApplicationHost
    {
        public static IApplicationHost? Instance { get; set; }
        public static bool IsGameModeActive { get; set; } = false;
    }
}
