namespace API_WEB.Helpers
{
    public static class OtpStore
    {
        private static readonly Dictionary<string, OtpEntry> _otps = new();
        private static readonly object _lock = new();

        private class OtpEntry
        {
            public string Code { get; set; } = default!;
            public DateTime ExpiresAt { get; set; }
            public int SendCount { get; set; } = 0;
            public DateTime FirstSentAt { get; set; }
        }

        public static bool TrySetOtp(string email, string code, TimeSpan ttl, int maxPerWindow, TimeSpan window, out string? error)
        {
            lock (_lock)
            {
                if (_otps.TryGetValue(email, out var entry))
                {
                    // throttle window kiểm soát số lần gửi
                    if (DateTime.UtcNow - entry.FirstSentAt < window && entry.SendCount >= maxPerWindow)
                    {
                        error = $"Bạn đã yêu cầu OTP quá nhiều lần, vui lòng thử lại sau {window.TotalMinutes} phút.";
                        return false;
                    }

                    // reset cửa sổ nếu đã qua window
                    if (DateTime.UtcNow - entry.FirstSentAt >= window)
                    {
                        entry.SendCount = 0;
                        entry.FirstSentAt = DateTime.UtcNow;
                    }

                    entry.SendCount++;
                    entry.Code = code;
                    entry.ExpiresAt = DateTime.UtcNow.Add(ttl);
                }
                else
                {
                    _otps[email] = new OtpEntry
                    {
                        Code = code,
                        ExpiresAt = DateTime.UtcNow.Add(ttl),
                        SendCount = 1,
                        FirstSentAt = DateTime.UtcNow
                    };
                }
                error = null;
                return true;
            }
        }

        public static bool ValidateOtp(string email, string code)
        {
            lock (_lock)
            {
                if (_otps.TryGetValue(email, out var entry))
                {
                    if (DateTime.UtcNow > entry.ExpiresAt) return false; // hết hạn
                    return entry.Code == code;
                }
                return false;
            }
        }

        public static void RemoveOtp(string email)
        {
            lock (_lock)
            {
                _otps.Remove(email);
            }
        }

        public static void CleanExpired()
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                var expiredKeys = _otps
                    .Where(kvp => kvp.Value.ExpiresAt < now)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expiredKeys)
                {
                    _otps.Remove(key);
                }
            }
        }
    }

}
