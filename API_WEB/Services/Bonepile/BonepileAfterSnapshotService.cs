using API_WEB.ModelsDB;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace API_WEB.Services.Bonepile
{
    public class BonepileAfterSnapshotService : BackgroundService
    {
        private static readonly string[] StatusOrder =
        {
            "ScrapHasTask",
            "ScrapLackTask",
            "WaitingLink",
            "Linked",
            "WaitingApprovalScrap",
            "ApprovedBGA",
            "WaitingApprovalBGA",
            "RepairInRE",
            "WaitingCheckOut",
            "Can'tRepairProcess"
        };

        private static readonly string[] StatusV2Order =
        {
            "UnderFA",
            "ProcessIssue",
            "Repaired",
            "Scrap"
        };

        private static readonly string[] AgingBuckets = { "<45", "45-89", ">=90" };

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BonepileAfterSnapshotService> _logger;
        private readonly IHostEnvironment _env;
        public BonepileAfterSnapshotService(IServiceScopeFactory scopeFactory, ILogger<BonepileAfterSnapshotService> logger, IHostEnvironment env)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _env = env;
        }


        private static (string Status, string StatusV2) ResolveStatusWithV2(
    string serialNumber,
    Models.Bonepile.BonepileAfterKanbanResult item,
    IReadOnlyDictionary<string, (int ApplyTaskStatus, string? TaskNumber)> scrapDict,
    IReadOnlyDictionary<string, (int CheckingB36R, DateTime? ExportDate)> exportDict,
    IReadOnlySet<string> khoOkSet)
        {
            string status;
            string statusV2 = string.Empty;
            var isInKhoOk = khoOkSet.Contains(serialNumber);

            if (scrapDict.TryGetValue(serialNumber, out var scrapInfo))
            {
                var applyTaskStatus = scrapInfo.ApplyTaskStatus;

                if (applyTaskStatus == 5 || applyTaskStatus == 6 || applyTaskStatus == 7)
                {
                    status = "ScrapHasTask";
                    statusV2 = string.Empty;
                }
                else if (applyTaskStatus == 0 || applyTaskStatus == 1)
                {
                    if (string.IsNullOrEmpty(scrapInfo.TaskNumber) || scrapInfo.TaskNumber == "N/A")
                        status = "ScrapLackTask";
                    else status = "ScrapHasTask";

                    statusV2 = "Scrap";
                }
                else if (applyTaskStatus == 2)
                {
                    status = "WaitingApprovalScrap";
                    statusV2 = "Scrap";
                }
                else if (applyTaskStatus == 4 || applyTaskStatus == 10)
                {
                    status = "WaitingApprovalBGA";
                    statusV2 = DetermineStatusV2(item.TEST_CODE);
                }
                else if (applyTaskStatus == 8)
                {
                    status = "Can'tRepairProcess";
                    statusV2 = DetermineStatusV2(item.TEST_CODE);
                }
                else
                {
                    status = "ApprovedBGA";
                    statusV2 = DetermineStatusV2(item.TEST_CODE);
                }
            }
            else if (exportDict.TryGetValue(serialNumber, out var exportInfo))
            {
                switch (exportInfo.CheckingB36R)
                {
                    case 1:
                        status = "WaitingLink";
                        statusV2 = "Repaired";
                        break;
                    case 2:
                        status = "Linked";
                        statusV2 = "Repaired";
                        break;
                    default:
                        status = "RepairInRE";
                        statusV2 = DetermineStatusV2(item.TEST_CODE);
                        break;
                }
            }
            else
            {
                status = "RepairInRE";
                statusV2 = DetermineStatusV2(item.TEST_CODE);
            }

            if (string.Equals(status, "RepairInRE", StringComparison.OrdinalIgnoreCase) && isInKhoOk)
            {
                status = "WaitingCheckOut";
            }

            if (isInKhoOk)
            {
                statusV2 = "Repaired";
            }

            return (status, statusV2);
        }

        private static string DetermineStatusV2(string? testCode)
        {
            if (string.IsNullOrEmpty(testCode))
            {
                return "UnderFA";
            }

            var normalizedTestCode = testCode.Trim();

            return (normalizedTestCode.Length <= 6 && !string.Equals(normalizedTestCode, "11098", StringComparison.OrdinalIgnoreCase))
                || normalizedTestCode.Contains("BURNMARK", StringComparison.OrdinalIgnoreCase)
                ? "ProcessIssue"
                : "UnderFA";
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

                    await CaptureSnapshotAsync(nextRun, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    // Service is stopping
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "BonepileAfterSnapshotService encountered an error.");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
        }

        private async Task CaptureSnapshotAsync(DateTime scheduledRun, CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IBonepileAfterRepository>();
            var sqlContext = scope.ServiceProvider.GetRequiredService<CSDL_NE>();

            var rawData = await repository.ExecuteBonepileAfterKanbanBasicQueryAsync(stoppingToken);
            var excludedSn = repository.GetExcludedSerialNumbers();

            if (excludedSn.Any())
            {
                var excludedSet = new HashSet<string>(excludedSn, StringComparer.OrdinalIgnoreCase);
                rawData = rawData
                    .Where(d => !excludedSet.Contains(d.SERIAL_NUMBER?.Trim()?.ToUpperInvariant() ?? string.Empty))
                    .ToList();
            }

            var records = await BuildProcessedRecordsAsync(rawData, sqlContext, stoppingToken);
            var statusCounts = CalculateStatusCounts(records);
            var totalCount = records.Count;
            var productLineAging = CalculateProductLineAging(records);

            await SaveSnapshotAsync(sqlContext, scheduledRun, statusCounts, totalCount, productLineAging, stoppingToken);
        }

        private DateTime GetNextRun(DateTime from)
        {
            if (_env.IsDevelopment())
            {
                return from.AddMinutes(5);
            }
            var targetTime = new DateTime(from.Year, from.Month, from.Day, 7, 30, 0);
            return from <= targetTime ? targetTime : targetTime.AddDays(1);
        }

        private async Task<List<ProcessedRecord>> BuildProcessedRecordsAsync(
            List<Models.Bonepile.BonepileAfterKanbanResult> rawData,
            CSDL_NE sqlContext,
            CancellationToken stoppingToken)
        {
            var snList = rawData
                .Select(d => d.SERIAL_NUMBER?.Trim().ToUpperInvariant())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            var scrapDict = await sqlContext.ScrapLists
                .Where(s => snList.Contains(s.SN.Trim().ToUpper()))
                .Select(s => new { SN = s.SN, s.ApplyTaskStatus, s.TaskNumber })
                .ToDictionaryAsync(
                    k => k.SN.Trim().ToUpper(),
                    v => (v.ApplyTaskStatus, v.TaskNumber),
                    StringComparer.OrdinalIgnoreCase,
                    cancellationToken: stoppingToken);

            var exportRecords = await sqlContext.Exports
                .Where(e => snList.Contains(e.SerialNumber.Trim().ToUpper()) && e.CheckingB36R > 0 && e.CheckingB36R <= 4)
                .ToListAsync(stoppingToken);

            var exportDict = exportRecords
                .GroupBy(e => e.SerialNumber?.Trim().ToUpper() ?? string.Empty)
                .Select(g => g.OrderByDescending(e => e.ExportDate).First())
                .ToDictionary(
                    e => e.SerialNumber.Trim().ToUpper(),
                    e => (e.CheckingB36R ?? 0, e.ExportDate),
                    StringComparer.OrdinalIgnoreCase);

            var khoOkSet = (await sqlContext.KhoOks
                    .Where(k => snList.Contains(k.SERIAL_NUMBER.Trim().ToUpper()))
                    .Select(k => k.SERIAL_NUMBER)
                    .ToListAsync(stoppingToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var validStatuses = new HashSet<string>(StatusOrder, StringComparer.OrdinalIgnoreCase);
            var result = new List<ProcessedRecord>(rawData.Count);

            foreach (var item in rawData)
            {
                var sn = item.SERIAL_NUMBER?.Trim().ToUpperInvariant() ?? string.Empty;
                if (string.IsNullOrEmpty(sn))
                {
                    continue;
                }

                var (status, statusV2) = ResolveStatusWithV2(sn, item, scrapDict, exportDict, khoOkSet);
                if (!validStatuses.Contains(status))
                {
                    continue;
                }

                result.Add(new ProcessedRecord
                {
                    SerialNumber = sn,
                    ProductLine = item.PRODUCT_LINE,
                    Aging = item.AGING,
                    Status = status,
                    StatusV2 = statusV2
                });
            }

            return result;
        }

        private static Dictionary<string, int> CalculateStatusCounts(IEnumerable<ProcessedRecord> records)
        {
            var counts = StatusV2Order.ToDictionary(status => status, _ => 0, StringComparer.OrdinalIgnoreCase);
            foreach (var record in records)
            {
                if (string.IsNullOrWhiteSpace(record.StatusV2))
                {
                    continue;
                }

                if (!counts.ContainsKey(record.StatusV2))
                {
                    counts[record.StatusV2] = 0;
                }

                counts[record.StatusV2]++;
            }


            return counts;
        }

        private static List<ProductLineAgingCount> CalculateProductLineAging(IEnumerable<ProcessedRecord> records)
        {
            var filtered = records
                .Where(r => !string.Equals(r.Status, "ScrapHasTask", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(r.Status, "ScrapLackTask", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var results = new List<ProductLineAgingCount>();
            foreach (var group in filtered.GroupBy(r => string.IsNullOrWhiteSpace(r.ProductLine) ? "UNKNOWN" : r.ProductLine))
            {
                var counters = AgingBuckets.ToDictionary(bucket => bucket, _ => 0);
                foreach (var record in group)
                {
                    var bucket = GetAgingBucket(record.Aging);
                    counters[bucket]++;
                }

                foreach (var bucket in AgingBuckets)
                {
                    results.Add(new ProductLineAgingCount(group.Key, bucket, counters[bucket]));
                }
            }

            return results;
        }

        private static string GetAgingBucket(double? aging)
        {
            if (aging.HasValue)
            {
                if (aging.Value < 45) return "<45";
                if (aging.Value < 90) return "45-89";
            }

            return ">=90";
        }

        private static async Task SaveSnapshotAsync(
            CSDL_NE sqlContext,
            DateTime snapshotTime,
            Dictionary<string, int> statusCounts,
            int totalCount,
            List<ProductLineAgingCount> productLineAging,
            CancellationToken stoppingToken)
        {
            await using var transaction = await sqlContext.Database.BeginTransactionAsync(stoppingToken);
            try
            {
                var statusEntries = await sqlContext.BonepileAfterStatusHistories
                    .Where(x => EF.Functions.DateDiffDay(x.SnapshotDate, snapshotTime) == 0)
                    .ToListAsync(stoppingToken);
                var totalEntries = await sqlContext.BonepileAfterTotalHistories
                    .Where(x => EF.Functions.DateDiffDay(x.SnapshotDate, snapshotTime) == 0)
                    .ToListAsync(stoppingToken);
                var agingEntries = await sqlContext.BonepileAfterProductLineAgingHistories
                    .Where(x => EF.Functions.DateDiffDay(x.SnapshotDate, snapshotTime) == 0)
                    .ToListAsync(stoppingToken);

                if (statusEntries.Count > 0)
                {
                    sqlContext.BonepileAfterStatusHistories.RemoveRange(statusEntries);
                }

                if (totalEntries.Count > 0)
                {
                    sqlContext.BonepileAfterTotalHistories.RemoveRange(totalEntries);
                }

                if (agingEntries.Count > 0)
                {
                    sqlContext.BonepileAfterProductLineAgingHistories.RemoveRange(agingEntries);
                }

                if (statusEntries.Count > 0 || totalEntries.Count > 0 || agingEntries.Count > 0)
                {
                    await sqlContext.SaveChangesAsync(stoppingToken);
                }

                var statusHistory = statusCounts
                    .Select(kvp => new BonepileAfterStatusHistory
                    {
                        SnapshotDate = snapshotTime,
                        Status = kvp.Key,
                        Count = kvp.Value
                    })
                    .ToList();

                var totalHistory = new BonepileAfterTotalHistory
                {
                    SnapshotDate = snapshotTime,
                    TotalCount = totalCount
                };

                var agingHistory = productLineAging
                    .Select(item => new BonepileAfterProductLineAgingHistory
                    {
                        SnapshotDate = snapshotTime,
                        ProductLine = item.ProductLine,
                        AgeRange = item.AgeRange,
                        Count = item.Count
                    })
                    .ToList();

                await sqlContext.BonepileAfterStatusHistories.AddRangeAsync(statusHistory, stoppingToken);
                await sqlContext.BonepileAfterTotalHistories.AddAsync(totalHistory, stoppingToken);
                await sqlContext.BonepileAfterProductLineAgingHistories.AddRangeAsync(agingHistory, stoppingToken);
                await sqlContext.SaveChangesAsync(stoppingToken);

                await transaction.CommitAsync(stoppingToken);
            }
            catch
            {
                await transaction.RollbackAsync(stoppingToken);
                throw;
            }
        }

        private sealed record ProcessedRecord
        {
            public string SerialNumber { get; init; } = string.Empty;
            public string? ProductLine { get; init; }
            public double? Aging { get; init; }
            public string Status { get; init; } = string.Empty;
            public string StatusV2 { get; init; } = string.Empty;
        }

        private sealed record ProductLineAgingCount(string ProductLine, string AgeRange, int Count);
    }
}
