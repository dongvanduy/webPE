using API_WEB.Helpers;

namespace API_WEB.Services.Authentication
{
    public class OtpCleanupService : BackgroundService
    {
        private readonly ILogger<OtpCleanupService> _logger;
        public OtpCleanupService(ILogger<OtpCleanupService> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    OtpStore.CleanExpired();
                    _logger.LogInformation("OTP cleanup ran at: {time}", DateTime.UtcNow);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during OTP cleanup");
                }

                // Chạy mỗi 5 phút
                await Task.Delay(TimeSpan.FromMinutes(240), stoppingToken);
            }
        }
    }

}
