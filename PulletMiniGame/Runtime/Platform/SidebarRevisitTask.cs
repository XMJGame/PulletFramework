using System;

namespace PulletMiniGame.Platform
{
    public sealed class SidebarRevisitTask
    {
        private const string StoragePrefix = "pullet.sidebar.claimed.";
        private readonly ISidebarRevisitService _sidebar;
        private readonly IPlatformStorageService _storage;

        public SidebarRevisitTask(ISidebarRevisitService sidebar, IPlatformStorageService storage)
        {
            _sidebar = sidebar ?? throw new ArgumentNullException(nameof(sidebar));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public bool CanClaim(string periodKey)
        {
            ValidatePeriodKey(periodKey);
            return _sidebar.EnteredFromSidebar && !_storage.HasKey(StoragePrefix + periodKey);
        }

        public void MarkClaimed(string periodKey)
        {
            ValidatePeriodKey(periodKey);
            _storage.SetString(StoragePrefix + periodKey, "1");
            _storage.Save();
        }

        public bool TryMarkClaimed(string periodKey)
        {
            if (!CanClaim(periodKey)) return false;
            MarkClaimed(periodKey);
            return true;
        }

        public static string ChinaDailyPeriodKey(DateTimeOffset utcNow)
        {
            return utcNow.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd");
        }

        private static void ValidatePeriodKey(string periodKey)
        {
            if (string.IsNullOrWhiteSpace(periodKey))
                throw new ArgumentException("Reward period key is required.", nameof(periodKey));
        }
    }
}
