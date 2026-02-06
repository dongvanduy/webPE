using API_WEB.ModelsDB;
using API_WEB.ModelsOracle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;
namespace API_WEB.Controllers.Repositories
{
    public class AutoExportController : Controller
    {
        private readonly CSDL_NE _sqlContext;
        private readonly OracleDbContext _oracleContext;
        public AutoExportController(CSDL_NE sqlContext, OracleDbContext oracleContext)
        {
            _sqlContext = sqlContext;
            _oracleContext = oracleContext;
        }
        [HttpPost("AutoExport")]
        public async Task<IActionResult> AutoExport()
        {
            try
            {
                // 1. Pre-load all products into a dictionary (eliminates N+1 queries)
                var allProducts = await _sqlContext.Products.ToListAsync();
                if (!allProducts.Any())
                {
                    return Ok(new { success = false, message = "Không có Serial Number nào trong hệ thống SQL Server." });
                }

                // Create lookup dictionary for O(1) access
                var productDict = allProducts.ToDictionary(p => p.SerialNumber, p => p);
                var serialNumbers = allProducts.Select(p => p.SerialNumber).ToList();

                // 2. Kết nối Oracle - single connection for all queries
                await using var oracleConnection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);
                await oracleConnection.OpenAsync();

                // 3. Batch fetch all required data from Oracle (reduces DB round trips)
                var wipGroups = await GetWipGroupsFromOracleAsync(oracleConnection, serialNumbers);
                var r107Info = await GetR107ExtendedInfoAsync(oracleConnection, serialNumbers);

                // 4. Filter SNs based on conditions (optimized with pre-loaded data)
                var snToExport = new List<string>();
                var blacklistedCount = 0;

                foreach (var sn in serialNumbers)
                {
                    // Use dictionary lookup instead of DB query
                    if (!productDict.TryGetValue(sn, out var product))
                        continue;

                    // Skip available products
                    if (product.BorrowStatus == "Available")
                        continue;

                    // === BLACKLIST RULE: Block items where wipR107 contains 'B28M' or 'B30M' ===
                    if (IsBlacklisted(r107Info, sn))
                    {
                        blacklistedCount++;
                        continue; // Skip blacklisted items
                    }

                    // TH1: No result from Z_KANBAN_TRACKING_T
                    if (!wipGroups.ContainsKey(sn))
                    {
                        // Use pre-fetched R107 data instead of individual query
                        var errorFlag = r107Info.TryGetValue(sn, out var info) ? info.ErrorFlag : null;
                        if (errorFlag == "0")
                        {
                            snToExport.Add(sn);
                        }
                    }
                    // Note: B36R_TO_SFG logic is commented out in original code
                }

                if (!snToExport.Any())
                {
                    return Ok(new
                    {
                        success = false,
                        message = "Không có SN nào thỏa mãn điều kiện xuất kho.",
                        blacklistedCount
                    });
                }

                // 5. Build export records using pre-loaded product data
                var exportDate = DateTime.Now;
                var exports = snToExport
                    .Where(sn => productDict.ContainsKey(sn))
                    .Select(sn =>
                    {
                        var product = productDict[sn];
                        return new Export
                        {
                            SerialNumber = product.SerialNumber,
                            ExportDate = exportDate,
                            ExportPerson = "Auto_Export",
                            ProductLine = product.ProductLine,
                            EntryDate = product.EntryDate,
                            EntryPerson = product.EntryPerson,
                            ModelName = product.ModelName
                        };
                    })
                    .ToList();

                // Batch insert exports
                await _sqlContext.Exports.AddRangeAsync(exports);

                // 6. Batch remove exported products
                var productsToRemove = allProducts.Where(p => snToExport.Contains(p.SerialNumber)).ToList();
                _sqlContext.Products.RemoveRange(productsToRemove);

                await _sqlContext.SaveChangesAsync();

                // 7. Return result with blacklist info
                return Ok(new
                {
                    success = true,
                    totalExported = snToExport.Count,
                    blacklistedCount,
                    exportedSerialNumbers = snToExport
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"System Error: {ex.Message}");
                return StatusCode(500, new { success = false, message = $"Lỗi hệ thống: {ex.Message}" });
            }
        }

        /// <summary>
        /// Checks if a serial number is blacklisted based on R107 WIP_GROUP.
        /// Blacklist rule: WIP_GROUP contains 'B28M' or 'B30M'
        /// </summary>
        private static bool IsBlacklisted(Dictionary<string, R107ExtendedInfo> r107Info, string serialNumber)
        {
            if (!r107Info.TryGetValue(serialNumber, out var info))
                return false;

            var wipGroup = info.WipGroup;
            if (string.IsNullOrEmpty(wipGroup))
                return false;

            // Blacklist rule: Block if WIP_GROUP contains 'B28M' or 'B30M'
            return wipGroup.Contains("B28M", StringComparison.OrdinalIgnoreCase) ||
                   wipGroup.Contains("B30M", StringComparison.OrdinalIgnoreCase);
        }

        //================================= Helper Methods ================================

        /// <summary>
        /// Extended info from R107 table including ERROR_FLAG for optimization
        /// </summary>
        private record R107ExtendedInfo(string? MoNumber, string? WipGroup, string? ErrorFlag);

        /// <summary>
        /// Batch fetch extended R107 info including ERROR_FLAG, MO_NUMBER, and WIP_GROUP
        /// This eliminates N+1 queries when checking error flags individually
        /// </summary>
        private async Task<Dictionary<string, R107ExtendedInfo>> GetR107ExtendedInfoAsync(OracleConnection connection, List<string> serialNumbers)
        {
            var infos = new Dictionary<string, R107ExtendedInfo>();

            if (!serialNumbers.Any()) return infos;

            var batchSize = 1000; // Avoid ORA-01795 error
            for (var i = 0; i < serialNumbers.Count; i += batchSize)
            {
                var batch = serialNumbers.Skip(i).Take(batchSize).ToList();
                var serialList = string.Join(",", batch.Select(sn => $"'{sn}'"));

                var query = $@"
                    SELECT SERIAL_NUMBER, MO_NUMBER, WIP_GROUP, ERROR_FLAG
                    FROM SFISM4.R107
                    WHERE SERIAL_NUMBER IN ({serialList})";

                using var command = new OracleCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var serialNumber = reader["SERIAL_NUMBER"]?.ToString();
                    var moNumber = reader["MO_NUMBER"]?.ToString();
                    var wipGroup = reader["WIP_GROUP"]?.ToString();
                    var errorFlag = reader["ERROR_FLAG"]?.ToString();

                    if (!string.IsNullOrEmpty(serialNumber) && !infos.ContainsKey(serialNumber))
                    {
                        infos.Add(serialNumber, new R107ExtendedInfo(moNumber, wipGroup, errorFlag));
                    }
                }
            }

            return infos;
        }

        // Lấy WIP_GROUP từ Z_KANBAN_TRACKING_T
        private async Task<Dictionary<string, string>> GetWipGroupsFromOracleAsync(OracleConnection connection, List<string> serialNumbers)
        {
            var wipGroups = new Dictionary<string, string>();

            if (!serialNumbers.Any()) return wipGroups;

            var batchSize = 1000; // Chia batch để tránh lỗi ORA-01795
            for (var i = 0; i < serialNumbers.Count; i += batchSize)
            {
                var batch = serialNumbers.Skip(i).Take(batchSize).ToList();
                var serialList = string.Join(",", batch.Select(sn => $"'{sn}'"));

                var query = $@"
            SELECT SERIAL_NUMBER, WIP_GROUP 
            FROM SFISM4.Z_KANBAN_TRACKING_T
            WHERE SERIAL_NUMBER IN ({serialList})";

                using var command = new OracleCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var serialNumber = reader["SERIAL_NUMBER"]?.ToString();
                    var wipGroup = reader["WIP_GROUP"]?.ToString();
                    if (!string.IsNullOrEmpty(serialNumber) && !wipGroups.ContainsKey(serialNumber))
                    {
                        wipGroups.Add(serialNumber, wipGroup);
                    }
                }
            }

            return wipGroups;
        }

        /// <summary>
        /// Wrapper method for backward compatibility - delegates to GetR107ExtendedInfoAsync
        /// </summary>
        private async Task<Dictionary<string, (string? MoNumber, string? WipGroup)>> GetR107InfoAsync(OracleConnection connection, List<string> serialNumbers)
        {
            var extendedInfos = await GetR107ExtendedInfoAsync(connection, serialNumbers);
            return extendedInfos.ToDictionary(
                kvp => kvp.Key,
                kvp => (kvp.Value.MoNumber, kvp.Value.WipGroup)
            );
        }

        public async Task CheckLinkMoAsync()
        {
            var exports = await _sqlContext.Exports
                .Where(e => e.CheckingB36R > 0).ToListAsync();
            if (!exports.Any())
            {
                return;
            }
            // Danh sách SN tương ứng
            var serialNumbers = exports
                .Select(e => e.SerialNumber)
                .Where(sn => !string.IsNullOrEmpty(sn))
                .Distinct()
                .ToList();

            // Tra cứu ExportDate mới nhất cho từng SN trên toàn bảng Exports (tránh N+1)
            var latestExportDates = await _sqlContext.Exports
                .Where(e => serialNumbers.Contains(e.SerialNumber))
                .GroupBy(e => e.SerialNumber)
                .Select(g => new
                {
                    SerialNumber = g.Key,
                    Latest = g.Max(x => x.ExportDate)
                })
                .ToDictionaryAsync(x => x.SerialNumber, x => x.Latest);

            await using var oracleConnection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);
            await oracleConnection.OpenAsync();

            var wipGroups = await GetWipGroupsFromOracleAsync(oracleConnection, serialNumbers);
            var r107Infos = await GetR107ExtendedInfoAsync(oracleConnection, serialNumbers);

            foreach (var exp in exports)
            {
                var sn = exp.SerialNumber;

                var wipGroup = wipGroups.TryGetValue(sn, out var wg) ? wg : null;
                var r107Info = r107Infos.TryGetValue(sn, out var info) ? info : null;
                var wipGroupR107 = r107Info?.WipGroup;

                // Xác định bản ghi export hiện tại có phải ExportDate mới nhất theo SN
                var isLatestExport =
                    latestExportDates.TryGetValue(sn, out var latestDate)
                    && exp.ExportDate.HasValue
                    && latestDate.HasValue
                    && exp.ExportDate.Value == latestDate.Value;

                bool containsB36R_TO_SFG = !string.IsNullOrEmpty(wipGroup) && wipGroup.Contains("B36R");
                bool r107ContainsB36R = !string.IsNullOrEmpty(wipGroupR107) && wipGroupR107.Contains("B36R") && !wipGroupR107.Contains("REPAIR_B36R");

                var linked = false;
                if (!string.IsNullOrEmpty(wipGroup) && wipGroup.Contains("B36R_TO_SFG"))
                {
                    if (!string.IsNullOrEmpty(wipGroupR107) && wipGroupR107.Contains("REPAIR_B36R"))
                    {
                        linked = true;
                    }
                    else if (!(!string.IsNullOrEmpty(wipGroupR107) && wipGroupR107.Contains("B36R")))
                    {
                        linked = true;
                    }
                }
                else if (!string.IsNullOrEmpty(wipGroup) &&
                         (wipGroup.Contains("KANBAN_IN") || wipGroup.Contains("KANBAN_OUT")))
                {
                    if (!exp.KanbanTime.HasValue)
                    {
                        exp.KanbanTime = DateTime.Now;
                    }
                    exp.CheckingB36R = 3;
                }

                if (linked)
                {
                    if (!exp.LinkTime.HasValue)
                    {
                        exp.LinkTime = DateTime.Now;
                    }
                    exp.CheckingB36R = 2;
                }

                // ===== Điều kiện mới: chỉ nâng lên 4 khi đang = 2 và là export mới nhất =====
                if (exp.CheckingB36R == 2
                    && containsB36R_TO_SFG
                    && exp.LinkTime.HasValue
                    && r107ContainsB36R
                    && isLatestExport)
                {
                    exp.CheckingB36R = 4;//Loi quay lai RE
                }
            }
            await _sqlContext.SaveChangesAsync();
        }

        [HttpGet("checking-b36r")]
        public async Task<IActionResult> CheckingB36R(DateTime? startDate, DateTime? endDate)
        {
            try
            {
                // Validate khoảng thời gian (nếu có đủ 2 đầu)
                if (startDate.HasValue && endDate.HasValue && startDate > endDate)
                {
                    return BadRequest(new { success = false, message = "Khoảng thời gian không hợp lệ (startDate > endDate)." });
                }

                // Base: chỉ quan tâm 2 trạng thái 1 va 2
                var baseQuery = _sqlContext.Exports
                    .Where(e => e.CheckingB36R == 1 || e.CheckingB36R == 2);

                // Nếu có truyền thời gian => lọc theo ExportDate
                IQueryable<Export> rangeQuery = baseQuery;
                bool hasRange = startDate.HasValue || endDate.HasValue;
                if (startDate.HasValue)
                    rangeQuery = rangeQuery.Where(e => e.ExportDate >= startDate.Value);
                if (endDate.HasValue)
                    rangeQuery = rangeQuery.Where(e => e.ExportDate <= endDate.Value);

                // Bước 1: Lấy danh sách theo khoảng thời gian (nếu có) + khử trùng trong khoảng theo ExportDate mới nhất
                // Tie-break: LinkTime mới hơn
                // Lưu ý: EF Core có thể dịch GroupBy + First() với OrderBy trong Select; mẫu này cùng kiểu với code trước đó
                var exportsInWindow = await rangeQuery
                    .GroupBy(e => e.SerialNumber)
                    .Select(g => g.OrderByDescending(x => x.ExportDate)
                                  .ThenByDescending(x => x.LinkTime)
                                  .First())
                    .ToListAsync();

                // Nếu không truyền khoảng thời gian, giữ hành vi cũ: lấy record mới nhất (ExportDate, rồi LinkTime) cho mỗi SN
                if (!hasRange)
                {
                    exportsInWindow = await baseQuery
                        .GroupBy(e => e.SerialNumber)
                        .Select(g => g.OrderByDescending(x => x.ExportDate)
                                      .ThenByDescending(x => x.LinkTime)
                                      .First())
                        .ToListAsync();
                }

                if (exportsInWindow == null || exportsInWindow.Count == 0)
                {
                    return Ok(new { success = false, message = "Không tìm thấy Serial Number phù hợp." });
                }

                // Danh sách SN trong kết quả (để tra trạng thái hiện tại)
                var snList = exportsInWindow
                    .Select(e => e.SerialNumber)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Distinct()
                    .ToList();

                await using var oracleConnection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);
                await oracleConnection.OpenAsync();
                var r107Infos = await GetR107InfoAsync(oracleConnection, snList);

                // Bước 2: Xác định trạng thái hiện tại cho từng SN (mới nhất toàn cục, không giới hạn thời gian)
                var latestBySn = await _sqlContext.Exports
                    .Where(e => snList.Contains(e.SerialNumber))
                    .GroupBy(e => e.SerialNumber)
                    .Select(g => g.OrderByDescending(x => x.ExportDate)
                                  .ThenByDescending(x => x.LinkTime)
                                  .First())
                    .ToListAsync();

                var latestDict = latestBySn.ToDictionary(
                    k => k.SerialNumber,
                    v => v.CheckingB36R
                );

                // Bước 3: Gắn status hiện tại vào kết quả trong khoảng thời gian
                var now = DateTime.Now;
                var shaped = exportsInWindow.Select(e =>
                {
                    var currentStatus = latestDict.TryGetValue(e.SerialNumber, out var s) ? s : e.CheckingB36R;
                    var statusText = currentStatus == 1 ? "Chờ Link MO"
                                 : currentStatus == 2 ? "Đã link MO"
                                 : "Không xác định";
                    var moNumber = r107Infos.TryGetValue(e.SerialNumber, out var info) ? info.MoNumber : null;
                    var agingDays = e.ExportDate.HasValue ? Math.Round((now - e.ExportDate.Value).TotalDays, 2) : 0;
                    return new
                    {
                        SN = e.SerialNumber,
                        ProductLine = e.ProductLine,
                        ModelName = e.ModelName,
                        MoNumber = moNumber,
                        ExportDate = e.ExportDate.HasValue ? e.ExportDate.Value.ToString("yyyy-MM-dd HH:mm:ss") : "",
                        LinkTime = e.LinkTime.HasValue ? e.LinkTime.Value.ToString("yyyy-MM-dd HH:mm:ss") : "",
                        Status = statusText,
                        CheckingB36R = currentStatus,
                        AgingDays = agingDays
                    };
                }).ToList();

                // Chia nhóm theo trạng thái hiện tại
                var awaiting = shaped.Where(x => x.CheckingB36R == 1).ToList();
                var linked = shaped.Where(x => x.CheckingB36R == 2).ToList();

                if (!awaiting.Any() && !linked.Any())
                {
                    return Ok(new { success = false, message = "Không có Serial Number nào ở trạng thái Chờ Link/Đã Link." });
                }

                return Ok(new
                {
                    success = true,
                    // Tổng các SN trong khoảng (đã khử trùng theo ExportDate)
                    totalCount = shaped.Count,

                    // Đếm theo trạng thái hiện tại
                    awaitingLinkCount = awaiting.Count,
                    linkCount = linked.Count,

                    // Dữ liệu chi tiết
                    awaiting,
                    linked,

                    message = hasRange
                        ? "Thống kê Link MO theo khoảng ExportDate (status theo trạng thái hiện tại)."
                        : "Thống kê Link MO mới nhất (status theo trạng thái hiện tại)."
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"System Error: {ex.Message}");
                return StatusCode(500, new { success = false, message = $"Lỗi hệ thống: {ex.Message}" });
            }
        }

        [HttpGet("checking-b36r/aging")]
        public async Task<IActionResult> GetCheckingB36RAging()
        {
            try
            {
                var now = DateTime.Now;

                // ✅ Lấy latest export theo SN toàn cục trước (fix bug cũ)
                var latestExports = await _sqlContext.Exports
                    .Where(e => e.ExportDate != null)
                    .GroupBy(e => e.SerialNumber)
                    .Select(g => g.OrderByDescending(x => x.ExportDate)
                                  .ThenByDescending(x => x.LinkTime)
                                  .First())
                    .ToListAsync();

                // Chỉ quan tâm trạng thái hiện tại 1/2
                latestExports = latestExports
                    .Where(e => e.CheckingB36R == 1 || e.CheckingB36R == 2)
                    .ToList();

                var waitingList = latestExports.Where(e => e.CheckingB36R == 1).ToList();
                var linkedList = latestExports.Where(e => e.CheckingB36R == 2).ToList();

                // ===== WaitingLink => tra Oracle để phân station "ĐÃ MỞ MO" vs "CHỜ MỞ MO" =====
                var waitingSNs = waitingList
                    .Select(x => x.SerialNumber)
                    .Where(sn => !string.IsNullOrWhiteSpace(sn))
                    .Distinct()
                    .ToList();

                Dictionary<string, DateTime?> inStationTimes = new(StringComparer.OrdinalIgnoreCase);

                if (waitingSNs.Count > 0)
                {
                    await using var oracleConnection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);
                    await oracleConnection.OpenAsync();
                    inStationTimes = await GetLinkMoInStationTimesAsync(oracleConnection, waitingSNs);
                }

                // Build items for waiting categories
                var openedMoItems = new List<AgingItem>();
                var waitingOpenMoItems = new List<AgingItem>();

                foreach (var e in waitingList)
                {
                    if (!e.ExportDate.HasValue) continue;
                    var exportDate = e.ExportDate.Value;

                    inStationTimes.TryGetValue(e.SerialNumber, out var inTime);

                    // Rule: chỉ tính "ĐÃ MỞ MO" nếu IN_STATION_TIME tồn tại và >= ExportDate mới nhất
                    if (inTime.HasValue && inTime.Value >= exportDate)
                    {
                        openedMoItems.Add(new AgingItem(
                            e.SerialNumber,
                            e.ProductLine,
                            e.ModelName,
                            StartTime: inTime.Value,
                            ExportDate: exportDate,
                            InStationTime: inTime,
                            Station: "ĐÃ MỞ MO"
                        ));
                    }
                    else
                    {
                        waitingOpenMoItems.Add(new AgingItem(
                            e.SerialNumber,
                            e.ProductLine,
                            e.ModelName,
                            StartTime: exportDate,
                            ExportDate: exportDate,
                            InStationTime: inTime, // có thể có nhưng < exportDate => coi như không hợp lệ cho cycle này
                            Station: "CHỜ MỞ MO"
                        ));
                    }
                }

                // Linked: giữ aging theo ExportDate (nếu muốn theo LinkTime thì nói mình đổi)
                var linkedItems = linkedList
                    .Where(e => e.ExportDate.HasValue)
                    .Select(e => new AgingItem(
                        e.SerialNumber,
                        e.ProductLine,
                        e.ModelName,
                        StartTime: e.ExportDate!.Value,
                        ExportDate: e.ExportDate!.Value,
                        InStationTime: null,
                        Station: "ĐÃ LINK MO"
                    ))
                    .ToList();

                var openedMoSummary = BuildAgingSummary(openedMoItems, now);
                var waitingOpenMoSummary = BuildAgingSummary(waitingOpenMoItems, now);
                var linkedSummary = BuildAgingSummary(linkedItems, now);

                return Ok(new
                {
                    success = true,

                    // ✅ bỏ waitingLink, thay bằng 2 nhóm station cho trạng thái = 1
                    openedMo = openedMoSummary.Buckets,
                    waitingOpenMo = waitingOpenMoSummary.Buckets,

                    // giữ linked
                    linked = linkedSummary.Buckets,

                    openedMoDetails = openedMoSummary.Details,
                    waitingOpenMoDetails = waitingOpenMoSummary.Details,
                    linkedDetails = linkedSummary.Details
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"System Error: {ex.Message}");
                return StatusCode(500, new { success = false, message = $"Lỗi hệ thống: {ex.Message}" });
            }
        }


        private sealed class AgingSummary
        {
            public List<object> Buckets { get; init; } = new();
            public object Details { get; init; } = new();
        }
        private sealed record AgingItem(
            string SerialNumber,
            string? ProductLine,
            string? ModelName,
            DateTime StartTime,
            DateTime ExportDate,
            DateTime? InStationTime,
            string Station
        );


        private static AgingSummary BuildAgingSummary(IEnumerable<AgingItem> items, DateTime now)
        {
            var lessThanOne = 0;
            var oneToThree = 0;
            var moreThanThree = 0;

            var lessThanOneDetails = new List<object>();
            var oneToThreeDetails = new List<object>();
            var moreThanThreeDetails = new List<object>();

            foreach (var item in items)
            {
                var agingDays = (now - item.StartTime).TotalDays;
                var detail = new
                {
                    item.SerialNumber,
                    item.ProductLine,
                    item.ModelName,
                    item.Station,
                    ExportDate = item.ExportDate.ToString("yyyy-MM-dd HH:mm:ss"),
                    InStationTime = item.InStationTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                    AgingDays = Math.Round(agingDays, 2)
                };

                if (agingDays < 1)
                {
                    lessThanOne++;
                    lessThanOneDetails.Add(detail);
                }
                else if (agingDays <= 3)
                {
                    oneToThree++;
                    oneToThreeDetails.Add(detail);
                }
                else
                {
                    moreThanThree++;
                    moreThanThreeDetails.Add(detail);
                }
            }

            return new AgingSummary
            {
                Buckets = new List<object>
        {
            new { label = "<1 ngày", count = lessThanOne },
            new { label = "1-3 ngày", count = oneToThree },
            new { label = ">3 ngày", count = moreThanThree }
        },
                Details = new
                {
                    LessThanOneDay = lessThanOneDetails,
                    OneToThreeDays = oneToThreeDetails,
                    MoreThanThreeDays = moreThanThreeDetails
                }
            };
        }


        private async Task<Dictionary<string, DateTime?>> GetLinkMoInStationTimesAsync(
        OracleConnection connection,
        List<string> serialNumbers)
        {
            var result = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);
            if (serialNumbers == null || serialNumbers.Count == 0) return result;

            var batchSize = 1000;
            for (var i = 0; i < serialNumbers.Count; i += batchSize)
            {
                var batch = serialNumbers.Skip(i).Take(batchSize).ToList();
                var serialList = string.Join(",", batch.Select(sn => $"'{sn}'"));

                var query = $@"
            SELECT KEY_PART_SN, MAX(IN_STATION_TIME) AS IN_STATION_TIME
            FROM SFISM4.R_KEYPART_BLACK_WHITE_LIST_T
            WHERE TYPE = 'LINK_MO'
              AND KEY_PART_SN IN ({serialList})
            GROUP BY KEY_PART_SN";

                using var cmd = new OracleCommand(query, connection);
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var sn = reader["KEY_PART_SN"]?.ToString();
                    if (string.IsNullOrWhiteSpace(sn)) continue;

                    DateTime? inStationTime = null;
                    var raw = reader["IN_STATION_TIME"];
                    if (raw != DBNull.Value && raw != null)
                        inStationTime = Convert.ToDateTime(raw);

                    if (!result.ContainsKey(sn))
                        result.Add(sn, inStationTime);
                }
            }

            return result;
        }

    }


}
