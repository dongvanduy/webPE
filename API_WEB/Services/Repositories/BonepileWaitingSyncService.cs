using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using API_WEB.Models.Bonepile;
using API_WEB.ModelsDB;
using API_WEB.ModelsOracle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;

namespace API_WEB.Services.Repositories
{
    /// <summary>
    /// BonepileWaitingSyncService — fixed, chunked & strongly logged
    ///  - Lấy danh sách SN từ Oracle (giống API) trong khoảng [2025/03/16 11:00, now)
    ///  - Chỉ giữ SN có status WaitingLink hoặc WaitingKanBanIn
    ///  - Insert bản ghi mới vào BonepileWaitings (tránh trùng; chỉ mở ticket khi chưa có bản ghi mở)
    ///  - Update TimeInLink / TimeInKanban:
    ///      + WaitingLink => đóng khi R107.WIP_GROUP KHÔNG còn chứa "B31M" (case-insensitive)
    ///      + WaitingKanBanIn => đóng khi xuất hiện record trong Z_KANBAN_TRACKING_T
    ///  - Tránh N+1 query sang Oracle: gom theo batch IN (:p0,:p1,...) và CHUNK <= 900 phần tử/batch (giới hạn Oracle)
    ///  - Lưu ý: cột TimeInLink/TimeInKanban phải là DateTime? (nullable)
    ///  - Thêm log chi tiết số ticket mở/đóng và số entity Modified trước khi SaveChanges
    /// </summary>
    public class BonepileWaitingSyncService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BonepileWaitingSyncService> _logger;
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(60);

        public BonepileWaitingSyncService(IServiceProvider serviceProvider, ILogger<BonepileWaitingSyncService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("BonepileWaitingSyncService started.");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var sqlContext = scope.ServiceProvider.GetRequiredService<CSDL_NE>();
                    var oracleContext = scope.ServiceProvider.GetRequiredService<OracleDbContext>();

                    await SyncWaitingStatusesAsync(sqlContext, oracleContext, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error running BonepileWaitingSyncService");
                }

                try { await Task.Delay(Interval, stoppingToken); } catch { }
            }
            _logger.LogInformation("BonepileWaitingSyncService stopped.");
        }

        private async Task SyncWaitingStatusesAsync(CSDL_NE sqlContext, OracleDbContext oracleContext, CancellationToken ct)
        {
            var start = "2025/03/16 11:00"; // FIX CỐ ĐỊNH
            var end = DateTime.Now.ToString("yyyy/MM/dd HH:mm");

            var data = await ExecuteOracleFullQuery(oracleContext, start, end, ct);
            if (data.Count == 0)
            {
                _logger.LogInformation("No oracle rows in range.");
                return;
            }

            // map scrap category (nếu có) theo SN
            var snList = data.Select(d => d.SN).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var scrapDict = (await sqlContext.ScrapLists
                .AsNoTracking()
                .Where(s => snList.Contains(s.SN))
                .Select(s => new { s.SN, s.Category })
                .ToListAsync(ct))
                .GroupBy(x => x.SN, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Category, StringComparer.OrdinalIgnoreCase);

            // phân loại status
            var waitingLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var waitingKanbans = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var b in data)
            {
                scrapDict.TryGetValue(b.SN, out var scrapCat);
                var status = ClassifyStatus(b, scrapCat);
                if (status == "WaitingLink") waitingLinks.Add(b.SN);
                if (status == "WaitingKanBanIn") waitingKanbans.Add(b.SN);
            }

            // chèn bản ghi mở nếu cần
            var toAdd = new List<BonepileWaiting>();
            foreach (var sn in waitingLinks)
                await AddIfNeeded(sqlContext, toAdd, sn, "WaitingLink", ct);
            foreach (var sn in waitingKanbans)
                await AddIfNeeded(sqlContext, toAdd, sn, "WaitingKanBanIn", ct);

            if (toAdd.Count > 0)
            {
                sqlContext.BonepileWaiting.AddRange(toAdd);
                await sqlContext.SaveChangesAsync(ct);
                _logger.LogInformation("Inserted {Count} BonepileWaiting rows.", toAdd.Count);
            }

            // cập nhật thời gian đóng
            await UpdateWaitingTimesAsync(sqlContext, oracleContext, ct);
        }

        private static string ClassifyStatus(BonepileResult b, string? scrapCategory)
        {
            if (!string.IsNullOrEmpty(scrapCategory))
                return scrapCategory == "Scrap" ? "Scrap" : "WaitingApproveScrap";

            var wip = b.WIP_GROUP ?? string.Empty;
            if (InB31M(wip)) return "WaitingLink"; // còn trong B31M → vẫn WaitingLink
            if (b.ERROR_FLAG == "0" && ContainsIgnoreCase(wip, "KANBAN_IN")) return "WaitingKanBanIn";

            return b.ERROR_FLAG switch
            {
                "7" => "Repair",
                "8" => "CheckOut",
                "1" => "CheckIn",
                "0" => "Online",
                _ => "Unknown"
            };
        }

        private static bool ContainsIgnoreCase(string? haystack, string needle)
            => !string.IsNullOrEmpty(haystack) && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool InB31M(string? wip)
            => !string.IsNullOrWhiteSpace(wip) && wip.Trim().IndexOf("B31M", StringComparison.OrdinalIgnoreCase) >= 0;

        private async Task<List<BonepileResult>> ExecuteOracleFullQuery(OracleDbContext oracleContext, string startDate, string endDate, CancellationToken ct)
        {
            var result = new List<BonepileResult>();

            await using var conn = new OracleConnection(oracleContext.Database.GetDbConnection().ConnectionString);
            await conn.OpenAsync(ct);

            const string sql = @"
                SELECT 
                    c.SERIAL_NUMBER, 
                    c.WIP_GROUP, 
                    CASE 
                        WHEN c.ERROR_FLAG = '0' AND c.MO_NEW IS NOT NULL THEN '0'
                        WHEN c.ERROR_FLAG = '0' AND c.MO_NEW IS NULL THEN '2'
                        ELSE c.ERROR_FLAG 
                    END AS ERROR_FLAG
                FROM (
                    SELECT 
                        a.SERIAL_NUMBER,
                        b.WIP_GROUP,
                        b.ERROR_FLAG,
                        CASE 
                            WHEN a.MO_NUMBER <> b.MO_NUMBER1 THEN b.MO_NUMBER1
                            WHEN a.MO_NUMBER = b.MO_NUMBER1 
                                 AND b.ERROR_FLAG NOT IN ('1','7','8') 
                                 AND b.DATA1 IS NOT NULL THEN b.DATA1
                            ELSE ''
                        END AS MO_NEW
                    FROM (
                        SELECT 
                            a.SERIAL_NUMBER, 
                            a.MO_NUMBER
                        FROM SFISM4.NVIDIA_BONPILE_SN_LOG a
                        INNER JOIN SFIS1.C_ERROR_CODE_T b ON a.SYMPTOM = b.ERROR_CODE
                        WHERE a.TIME BETWEEN TO_DATE(:start_date, 'YYYY/MM/DD HH24:MI')
                              AND TO_DATE(:end_date, 'YYYY/MM/DD HH24:MI')
                          AND NOT EXISTS (
                              SELECT 1 FROM SFISM4.Z_KANBAN_TRACKING_T z WHERE z.SERIAL_NUMBER = a.SERIAL_NUMBER
                          )
                          AND NOT EXISTS (
                              SELECT 1 FROM SFISM4.R_NV_REPAIR_BONEPILE_T r WHERE r.SERIAL_NUMBER = a.SERIAL_NUMBER AND r.DATA7 = 'SCRAP'
                          )
                          AND a.PRODUCT_LINE NOT LIKE 'SA%'
                          AND a.PRODUCT_LINE NOT LIKE 'Switch%'
                          AND a.MODEL_NAME   NOT LIKE 'SA%'
                        GROUP BY a.SERIAL_NUMBER, a.MO_NUMBER
                    ) a
                    LEFT JOIN (
                        SELECT 
                            a.SERIAL_NUMBER, 
                            a.MO_NUMBER AS MO_NUMBER1, 
                            b.DATA1, 
                            a.ERROR_FLAG, 
                            a.WORK_FLAG, 
                            a.WIP_GROUP, 
                            a.VERSION_CODE
                        FROM SFISM4.R107 a
                        LEFT JOIN SFISM4.R_KEYPART_BLACK_WHITE_LIST_T b 
                          ON a.SERIAL_NUMBER = b.KEY_PART_SN AND b.TYPE = 'LINK_MO'
                    ) b ON a.SERIAL_NUMBER = b.SERIAL_NUMBER
                ) c
                LEFT JOIN (
                    SELECT SERIAL_NUMBER, TEST_GROUP, TEST_CODE
                    FROM SFISM4.R109 
                    WHERE REASON_CODE = 'B001'
                ) s ON c.SERIAL_NUMBER = s.SERIAL_NUMBER
                ORDER BY c.SERIAL_NUMBER";

            await using var cmd = new OracleCommand(sql, conn);
            cmd.Parameters.Add(":start_date", OracleDbType.Varchar2).Value = startDate;
            cmd.Parameters.Add(":end_date", OracleDbType.Varchar2).Value = endDate;

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(new BonepileResult
                {
                    SN = reader["SERIAL_NUMBER"].ToString()!,
                    WIP_GROUP = reader["WIP_GROUP"].ToString(),
                    ERROR_FLAG = reader["ERROR_FLAG"].ToString()
                });
            }

            _logger.LogInformation("Oracle rows fetched: {count}", result.Count);
            return result;
        }

        private static async Task AddIfNeeded(CSDL_NE sqlContext, List<BonepileWaiting> toAdd, string serial, string status, CancellationToken ct)
        {
            serial = (serial ?? string.Empty).Trim();

            if (string.Equals(status, "WaitingLink", StringComparison.OrdinalIgnoreCase))
            {
                // Lấy bản ghi WaitingLink mới nhất (nếu có)
                var latestLink = await sqlContext.BonepileWaiting
                    .AsNoTracking()
                    .Where(w => w.SerialNumber == serial && EF.Functions.Collate(w.Status, "SQL_Latin1_General_CP1_CI_AS") == "WaitingLink")
                    .OrderByDescending(w => w.CreatedAt)
                    .FirstOrDefaultAsync(ct);

                var shouldOpen =
                    latestLink == null                    // chưa có -> mở mới
                    || latestLink.TimeInLink.HasValue;    // đã đóng vòng trước -> mở vòng mới

                if (shouldOpen)
                {
                    toAdd.Add(new BonepileWaiting
                    {
                        SerialNumber = serial,
                        Status = "WaitingLink",
                        CreatedAt = DateTime.Now
                    });
                }
                return;
            }

            if (string.Equals(status, "WaitingKanBanIn", StringComparison.OrdinalIgnoreCase))
            {
                // Nếu đã có bất kỳ WaitingKanBanIn thì bỏ qua (trạng thái cuối)
                var hasKanban = await sqlContext.BonepileWaiting
                    .AsNoTracking()
                    .AnyAsync(w => w.SerialNumber == serial && EF.Functions.Collate(w.Status, "SQL_Latin1_General_CP1_CI_AS") == "WaitingKanBanIn", ct);

                if (!hasKanban)
                {
                    toAdd.Add(new BonepileWaiting
                    {
                        SerialNumber = serial,
                        Status = "WaitingKanBanIn",
                        CreatedAt = DateTime.Now
                    });
                }
                return;
            }

            // Các status khác: không mở ticket ở service này
        }

        private async Task UpdateWaitingTimesAsync(CSDL_NE sqlContext, OracleDbContext oracleContext, CancellationToken ct)
        {
            // ===== 1) WaitingLink: chọn bản ghi mới nhất theo SN với TimeInLink == null (anti-join) =====
            var latestOpenLinks = await sqlContext.BonepileWaiting
                .Where(w =>
                    EF.Functions.Collate(w.Status, "SQL_Latin1_General_CP1_CI_AS") == "WaitingLink" &&
                    w.TimeInLink == null &&
                    !sqlContext.BonepileWaiting.Any(w2 =>
                        w2.SerialNumber == w.SerialNumber &&
                        EF.Functions.Collate(w2.Status, "SQL_Latin1_General_CP1_CI_AS") == "WaitingLink" &&
                        w2.CreatedAt > w.CreatedAt))
                .ToListAsync(ct);

            var openLinkSNs = latestOpenLinks
                .Select(x => (x.SerialNumber ?? string.Empty).Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.LogInformation("Open latest WaitingLink tickets (SNs): {cnt}", openLinkSNs.Count);

            if (openLinkSNs.Count > 0)
            {
                var r107Map = await BatchGetR107WipGroupAsync(oracleContext, openLinkSNs, ct);

                int closed = 0;
                foreach (var row in latestOpenLinks)
                {
                    var sn = (row.SerialNumber ?? string.Empty).Trim();
                    if (sn.Length == 0) continue;

                    // CHỈ đóng khi CÓ R107 và WIP_GROUP KHÔNG chứa B31M
                    if (r107Map.TryGetValue(sn, out var wip) && !InB31M(wip))
                    {
                        row.TimeInLink = DateTime.Now;
                        closed++;
                    }
                }
                _logger.LogInformation("Closed WaitingLink tickets this run: {cnt}", closed);
            }

            // ===== 2) WaitingKanBanIn: chọn bản ghi mới nhất theo SN với TimeInKanban == null (anti-join) =====
            var latestOpenKb = await sqlContext.BonepileWaiting
                .Where(w =>
                    EF.Functions.Collate(w.Status, "SQL_Latin1_General_CP1_CI_AS") == "WaitingKanBanIn" &&
                    w.TimeInKanban == null &&
                    !sqlContext.BonepileWaiting.Any(w2 =>
                        w2.SerialNumber == w.SerialNumber &&
                        EF.Functions.Collate(w2.Status, "SQL_Latin1_General_CP1_CI_AS") == "WaitingKanBanIn" &&
                        w2.CreatedAt > w.CreatedAt))
                .ToListAsync(ct);

            var openKbSNs = latestOpenKb
                .Select(x => (x.SerialNumber ?? string.Empty).Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.LogInformation("Open latest WaitingKanBanIn tickets (SNs): {cnt}", openKbSNs.Count);

            if (openKbSNs.Count > 0)
            {
                var hasKanban = await BatchHasKanbanAsync(oracleContext, openKbSNs, ct);

                int closed = 0;
                foreach (var row in latestOpenKb)
                {
                    var sn = (row.SerialNumber ?? string.Empty).Trim();
                    if (sn.Length == 0) continue;

                    // CHỈ đóng khi SN tồn tại trong Z_KANBAN_TRACKING_T
                    if (hasKanban.Contains(sn))
                    {
                        row.TimeInKanban = DateTime.Now;
                        closed++;
                    }
                }
                _logger.LogInformation("Closed WaitingKanBanIn tickets this run: {cnt}", closed);
            }

            var modified = sqlContext.ChangeTracker.Entries().Count(e => e.State == EntityState.Modified);
            _logger.LogInformation("Modified entities to save: {count}", modified);
            await sqlContext.SaveChangesAsync(ct);
        }


        // ===== Oracle Batch Helpers (chunk <= 900) =====
        private static IEnumerable<List<string>> Chunk(List<string> src, int size = 900)
        {
            for (int i = 0; i < src.Count; i += size)
                yield return src.GetRange(i, Math.Min(size, src.Count - i));
        }

        private static async Task<Dictionary<string, string?>> BatchGetR107WipGroupAsync(OracleDbContext oracleContext, List<string> sns, CancellationToken ct)
        {
            var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (sns.Count == 0) return map;

            await using var conn = new OracleConnection(oracleContext.Database.GetDbConnection().ConnectionString);
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);

            foreach (var batch in Chunk(sns, 900))
            {
                var paramNames = new List<string>(batch.Count);
                for (int i = 0; i < batch.Count; i++) paramNames.Add($":p{i}");

                var sql = $"SELECT SERIAL_NUMBER, WIP_GROUP FROM SFISM4.R107 WHERE SERIAL_NUMBER IN ({string.Join(',', paramNames)})";
                await using var cmd = new OracleCommand(sql, conn);
                for (int i = 0; i < batch.Count; i++) cmd.Parameters.Add(paramNames[i], OracleDbType.Varchar2).Value = batch[i].Trim();

                try
                {
                    await using var rd = await cmd.ExecuteReaderAsync(ct);
                    while (await rd.ReadAsync(ct))
                        map[rd["SERIAL_NUMBER"].ToString()!] = rd["WIP_GROUP"] as string;
                }
                catch (Exception ex)
                {
                    // log để biết batch fail
                    // không throw để các batch khác vẫn chạy
                    Console.Error.WriteLine($"[R107 batch error] size={batch.Count} msg={ex.Message}");
                }
            }

            return map;
        }

        private static async Task<HashSet<string>> BatchHasKanbanAsync(OracleDbContext oracleContext, List<string> sns, CancellationToken ct)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (sns.Count == 0) return set;

            await using var conn = new OracleConnection(oracleContext.Database.GetDbConnection().ConnectionString);
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);

            foreach (var batch in Chunk(sns, 900))
            {
                var paramNames = new List<string>(batch.Count);
                for (int i = 0; i < batch.Count; i++) paramNames.Add($":k{i}");

                var sql = $"SELECT SERIAL_NUMBER FROM SFISM4.Z_KANBAN_TRACKING_T WHERE SERIAL_NUMBER IN ({string.Join(',', paramNames)})";
                await using var cmd = new OracleCommand(sql, conn);
                for (int i = 0; i < batch.Count; i++) cmd.Parameters.Add(paramNames[i], OracleDbType.Varchar2).Value = batch[i].Trim();

                try
                {
                    await using var rd = await cmd.ExecuteReaderAsync(ct);
                    while (await rd.ReadAsync(ct))
                        set.Add(rd["SERIAL_NUMBER"].ToString()!);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[KANBAN batch error] size={batch.Count} msg={ex.Message}");
                }
            }

            return set;
        }
    }

    // Chỉ các field cần để sync (không phụ thuộc entity Oracle mapping đầy đủ)
    public class BonepileResult
    {
        public string SN { get; set; } = string.Empty;
        public string? WIP_GROUP { get; set; }
        public string? ERROR_FLAG { get; set; }
    }
}
