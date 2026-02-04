using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace API_WEB.Services.Bonepile
{
    public class BonepileRepositorySnapshotService : BackgroundService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<BonepileRepositorySnapshotService> _logger;
        private readonly IHostEnvironment _env;

        public BonepileRepositorySnapshotService(
            IHttpClientFactory httpClientFactory,
            ILogger<BonepileRepositorySnapshotService> logger,
            IHostEnvironment env)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _env = env;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var nextRun = GetNextRun(DateTime.Now);
                    var delay = nextRun - DateTime.Now;
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, stoppingToken);
                    }

                    await CaptureSnapshotAsync(stoppingToken);
                }
                catch (TaskCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "BonepileRepositorySnapshotService encountered an error.");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
        }

        private DateTime GetNextRun(DateTime from)
        {
            if (_env.IsDevelopment())
            {
                return from.AddMinutes(5);
            }

            var targetTime = new DateTime(from.Year, from.Month, from.Day, 19, 30, 0);
            return from <= targetTime ? targetTime : targetTime.AddDays(1);
        }

        private async Task CaptureSnapshotAsync(CancellationToken stoppingToken)
        {
            var client = _httpClientFactory.CreateClient();
            var response = await client.PostAsync("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/report/bonepile-repository-history/snapshot", null, stoppingToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("BonepileRepositorySnapshotService failed with status {StatusCode}.", response.StatusCode);
            }
        }
    }
}
