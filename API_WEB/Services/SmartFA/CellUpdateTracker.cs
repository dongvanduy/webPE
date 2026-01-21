using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace API_WEB.Services.SmartFA
{
    public interface ICellUpdateTracker
    {
        void MarkUpdated(string? cellName, DateTime timestamp);

        bool TryGetInfo(string? cellName, out CellUpdateInfo? info);

        IReadOnlyDictionary<string, CellUpdateInfo> GetAll();
    }

    public sealed class CellUpdateTracker : ICellUpdateTracker
    {
        private readonly ConcurrentDictionary<string, CellUpdateInfo> _updates =
            new(StringComparer.OrdinalIgnoreCase);

        public void MarkUpdated(string? cellName, DateTime timestamp)
        {
            var normalizedName = Normalize(cellName);
            if (string.IsNullOrEmpty(normalizedName))
            {
                return;
            }

            Cleanup(timestamp);
            _updates[normalizedName] = new CellUpdateInfo(timestamp);
        }

        public bool TryGetInfo(string? cellName, out CellUpdateInfo? info)
        {
            Cleanup(DateTime.Now);
            var normalizedName = Normalize(cellName);
            if (string.IsNullOrEmpty(normalizedName))
            {
                info = null;
                return false;
            }

            var hasValue = _updates.TryGetValue(normalizedName, out var storedInfo);
            info = hasValue ? storedInfo : null;
            return hasValue;
        }

        public IReadOnlyDictionary<string, CellUpdateInfo> GetAll()
        {
            Cleanup(DateTime.Now);
            return _updates.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        private void Cleanup(DateTime referenceTime)
        {
            var threshold = ShiftScheduleHelper.GetCurrentShiftStart(referenceTime);
            foreach (var entry in _updates.ToArray())
            {
                if (entry.Value.LastUpdatedAt < threshold)
                {
                    _updates.TryRemove(entry.Key, out _);
                }
            }
        }

        private static string? Normalize(string? cellName)
        {
            if (string.IsNullOrWhiteSpace(cellName))
            {
                return null;
            }

            return cellName.Trim().ToUpperInvariant();
        }
    }

    public sealed class CellUpdateInfo
    {
        public CellUpdateInfo(DateTime lastUpdatedAt)
        {
            LastUpdatedAt = lastUpdatedAt;
        }

        public DateTime LastUpdatedAt { get; }
    }

    internal static class ShiftScheduleHelper
    {
        private static readonly TimeSpan ShiftOneStart = new(6, 30, 0);
        private static readonly TimeSpan ShiftTwoStart = new(18, 30, 0);

        public static DateTime GetCurrentShiftStart(DateTime referenceTime)
        {
            var today = referenceTime.Date;
            var shiftOneStartToday = today + ShiftOneStart;
            var shiftTwoStartToday = today + ShiftTwoStart;

            if (referenceTime >= shiftTwoStartToday)
            {
                return shiftTwoStartToday;
            }

            if (referenceTime >= shiftOneStartToday)
            {
                return shiftOneStartToday;
            }

            return shiftTwoStartToday.AddDays(-1);
        }
    }
}
