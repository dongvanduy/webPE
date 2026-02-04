using API_WEB.Dtos.PdRepositorys;
using API_WEB.Models.Bonepile;
using API_WEB.ModelsDB;
using API_WEB.ModelsOracle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using Oracle.ManagedDataAccess.Client;
using System.Globalization;

namespace API_WEB.Controllers.Bonepile
{
    [Route("[controller]")]
    [ApiController]
    public class ReportController : Controller
    {
        private readonly CSDL_NE _sqlContext;
        private readonly OracleDbContext _oracleContext;
        private static readonly string[] RepositoryBuckets = { "B28M", "B30M", "SFC", "B36R" };

        public ReportController(CSDL_NE sqlContext, OracleDbContext oracleContext)
        {
            _sqlContext = sqlContext;
            _oracleContext = oracleContext;
        }

        [HttpGet("report-repair-before")]
        public async Task<IActionResult> AdapterRepairRecords([FromQuery] StatusRequestBonepile request)
        {
            try
            {
                request ??= new StatusRequestBonepile();

                var filterByStatus = request.Statuses?.Any() == true;
                HashSet<string>? statusFilter = null;

                if (filterByStatus)
                {
                    statusFilter = new HashSet<string>(
                        request.Statuses
                            .Where(s => !string.IsNullOrWhiteSpace(s)),
                        StringComparer.OrdinalIgnoreCase);

                    filterByStatus = statusFilter.Count > 0;
                }

                var dataResult = await BuildAdapterRepairDataAsync();

                var filteredRecords = filterByStatus && statusFilter != null
                    ? dataResult.Records
                        .Where(r => statusFilter.Contains(r.Status ?? string.Empty))
                        .ToList()
                    : dataResult.Records;

                var response = new AdapterRepairOverviewResponse
                {
                    TotalCount = dataResult.TotalCount,
                    StatusCounts = dataResult.StatusCounts,
                    Count = filteredRecords.Count,
                    Data = filteredRecords
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Xảy ra lỗi", error = ex.Message });
            }
        }

        private async Task<AdapterRepairDataResult> BuildAdapterRepairDataAsync()
        {
            static string NormalizeSn(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

            var baseData = await ExecuteAdapterRepairQuery();
            var b28mData = await ExecuteAdapterReworkFgQuery();

            var reworkFgSet = new HashSet<string>(
                b28mData.Select(x => NormalizeSn(x.SERIAL_NUMBER)),
                StringComparer.OrdinalIgnoreCase);

            var allData = baseData.Concat(b28mData)
                .GroupBy(x => NormalizeSn(x.SERIAL_NUMBER), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var scrapDict = (await _sqlContext.ScrapLists
                    .Select(s => new { s.SN, s.ApplyTaskStatus, s.TaskNumber })
                    .ToListAsync())
                .ToDictionary(
                    c => NormalizeSn(c.SN),
                    c => (c.ApplyTaskStatus, c.TaskNumber),
                    StringComparer.OrdinalIgnoreCase);

            var validStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ScrapLackTask","ScrapHasTask","WaitingApprovalScrap","ApprovedBGA","WaitingApprovalBGA",
                "Can'tRepairProcess","WaitingScrap","ReworkFG","RepairInRE","WaitingCheckOut","RepairInPD",
                "PendingInstructions", "waiting repair",
               "CB repaired once but aging day <30", "CB repaired once but aging day >30",
               "CB repaired twice but aging day <30", "CB repaired twice but aging day >30"
            };

            var records = new List<AdapterRepairRecordDto>();

            // Danh sách tạm để chứa các SN cần check lịch sử R109 (RepairInRE/PD)
            var repairSnsToCheck = new List<string>();

            foreach (var b in allData)
            {
                var normalizedSn = NormalizeSn(b.SERIAL_NUMBER);
                string status;

                if (reworkFgSet.Contains(normalizedSn))
                {
                    status = "ReworkFG";
                }
                else if (scrapDict.TryGetValue(normalizedSn, out var scrapInfo))
                {
                    var applyTaskStatus = scrapInfo.ApplyTaskStatus;
                    var taskNumber = scrapInfo.TaskNumber;

                    if (applyTaskStatus == 5 || applyTaskStatus == 6 || applyTaskStatus == 7)
                    {
                        status = "ScrapHasTask";
                    }
                    else if (applyTaskStatus == 0 || applyTaskStatus == 1)
                    {
                        status = string.IsNullOrEmpty(taskNumber) || taskNumber.Equals("N/A", StringComparison.OrdinalIgnoreCase)
                            ? "ScrapLackTask"
                            : "ScrapHasTask";
                    }
                    else
                    {
                        status = applyTaskStatus switch
                        {
                            2 => "WaitingApprovalScrap",
                            4 => "WaitingApprovalBGA",
                            8 => "Can'tRepairProcess",
                            22 => "PendingInstructions",
                            _ => "ApprovedBGA"
                        };
                    }
                }
                else if (b.MO_NUMBER?.Trim().StartsWith("4") == true)
                {
                    status = "ReworkFG";
                }
                else if (b.ERROR_FLAG != "8" && (b.WIP_GROUP.Contains("B28M") || b.WIP_GROUP.Contains("B30M") || b.WORK_FLAG == "2" || b.WORK_FLAG == "5"))
                {
                    status = "RepairInRE";
                }
                else
                {
                    status = b.ERROR_FLAG switch
                    {
                        "7" => "RepairInRE",
                        "8" => "WaitingCheckOut",
                        _ => "RepairInPD"
                    };
                }

                status = status?.Trim() ?? string.Empty;

                // **QUAN TRỌNG**: Nếu status là RepairInRE hoặc RepairInPD, đưa vào list để xử lý nâng cao
                if (status == "RepairInRE" || status == "RepairInPD")
                {
                    repairSnsToCheck.Add(b.SERIAL_NUMBER);
                }

                if (!validStatuses.Contains(status))
                {
                    continue;
                }

                var record = new AdapterRepairRecordDto
                {
                    Sn = b.SERIAL_NUMBER,
                    ModelName = b.MODEL_NAME,
                    MoNumber = b.MO_NUMBER,
                    ProductLine = b.PRODUCT_LINE,
                    ErrorFlag = b.ERROR_FLAG,
                    WorkFlag = b.WORK_FLAG,
                    WipGroup = b.WIP_GROUP,
                    Repository = b.REPOSITORY,
                    TestTime = b.TEST_TIME,
                    TestCode = b.TEST_CODE,
                    ErrorCodeItem = b.ERROR_ITEM_CODE,
                    TestGroup = b.TEST_GROUP,
                    ErrorDesc = b.ERROR_DESC,
                    Repair = b.REPAIR,
                    AgingDay = b.AGING_DAY,
                    CheckInDate = b.CHECKIN_DATE,
                    Status = status
                };
                records.Add(record);
            }
            // ============================================================
            // 4. XỬ LÝ LOGIC MỚI: CHECK LỊCH SỬ R109 CHO CÁC MÁY REPAIR
            // ============================================================
            if (repairSnsToCheck.Any())
            {
                // Lấy thông tin số lần fail liên tiếp
                var consecutiveFailCounts = await GetConsecutiveFailCountsAsync(repairSnsToCheck);

                foreach (var record in records)
                {
                    // Chỉ xử lý các bản ghi đang là RepairInRE hoặc RepairInPD
                    if (record.Status == "RepairInRE" || record.Status == "RepairInPD")
                    {
                        if (consecutiveFailCounts.TryGetValue(record.Sn, out int failCount))
                        {
                            // Parse Aging Day
                            double aging = 0;
                            double.TryParse(record.AgingDay, NumberStyles.Any, CultureInfo.InvariantCulture, out aging);
                            string agingSuffix = aging > 30 ? ">30" : "<30";
                            string baseStatusName = "";

                            // Logic đặt tên status theo yêu cầu
                            if (failCount <= 1)
                            {
                                record.Status = "waiting repair";
                                continue;
                            }
                            else if (failCount == 2)
                            {
                                baseStatusName = "CB repaired once but"; // Fail lần 2 (tức là đã sửa 1 lần rồi mà vẫn fail)
                            }
                            else // > 2
                            {
                                baseStatusName = "CB repaired twice but"; // Fail lần 3 trở lên
                            }

                            // Cập nhật lại Status cuối cùng: VD: "waiting repair - aging < 30"
                            // Bạn có thể format chuỗi này tùy ý để khớp với Frontend
                            record.Status = $"{baseStatusName} aging day {agingSuffix}";
                        }
                    }
                }
            }

            // Lọc lại các record theo validStatuses nếu cần, hoặc bỏ qua bước này nếu muốn lấy tất cả
            var finalRecords = records.Where(r => validStatuses.Any(s => r.Status.Contains(s, StringComparison.OrdinalIgnoreCase)) || validStatuses.Contains(r.Status)).ToList();

            var statusCounts = finalRecords
                .GroupBy(r => r.Status ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(g => new AdapterRepairStatusCountDto
                {
                    Status = g.Key,
                    Count = g.Count()
                })
                .ToList();

            return new AdapterRepairDataResult
            {
                Records = records,
                StatusCounts = statusCounts,
                TotalCount = records.Count
            };
        }

        private class AdapterRepairDataResult
        {
            public List<AdapterRepairRecordDto> Records { get; set; }
            public List<AdapterRepairStatusCountDto> StatusCounts { get; set; }
            public int TotalCount { get; set; }
        }

        private async Task<List<RepairTaskResult>> ExecuteAdapterRepairQuery()
        {
            var result = new List<RepairTaskResult>();

            await using var connection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);
            await connection.OpenAsync();

            string query = @"SELECT 
                    r107.SERIAL_NUMBER,
                    r107.MODEL_NAME,
                    model_desc.PRODUCT_LINE,
                    r107.MO_NUMBER,
                    r107.ERROR_FLAG,
                    r107.WORK_FLAG,
                    r107.WIP_GROUP,
                    CASE
                        WHEN r107.WORK_FLAG = '2'
                             OR r107.WIP_GROUP LIKE '%B28M%' THEN 'B28M'
                        WHEN r107.WORK_FLAG = '5'
                             OR r107.WIP_GROUP LIKE '%B30M%' THEN 'B30M'
                        ELSE 'SFC'
                    END AS REPOSITORY,
                    r109_latest.TEST_GROUP,
                    r109_latest.TEST_TIME,
                    r109_latest.TEST_CODE,
                    r109_latest.ERROR_ITEM_CODE,
                    error_desc.ERROR_DESC,
                    CHECK_IN.IN_DATETIME AS CHECKIN_DATE,
                    TRUNC(SYSDATE - CHECK_IN.IN_DATETIME) AS AGING_DAY
                    FROM sfism4.r107 r107
                JOIN sfis1.c_model_desc_t model_desc
                  ON r107.model_name = model_desc.model_name
                LEFT JOIN (
                    SELECT SERIAL_NUMBER, MAX(IN_DATETIME) AS IN_DATETIME
                    FROM SFISM4.R_REPAIR_IN_OUT_T 
                    WHERE MODEL_NAME IN (SELECT model_name FROM sfis1.c_model_desc_t WHERE model_serial = 'ADAPTER')
                    AND MO_NUMBER NOT LIKE '8%'
                    AND MODEL_NAME NOT LIKE '900%' 
                    AND MODEL_NAME NOT LIKE '930%' 
                    AND MODEL_NAME NOT LIKE '692%'
                    GROUP BY SERIAL_NUMBER
                ) CHECK_IN
                ON CHECK_IN.SERIAL_NUMBER = r107.SERIAL_NUMBER
                LEFT JOIN (
                    SELECT SERIAL_NUMBER, TEST_CODE, TEST_TIME, TEST_GROUP, ERROR_ITEM_CODE
                    FROM (
                        SELECT 
                            R109.SERIAL_NUMBER,
                            R109.TEST_CODE,
                            R109.TEST_TIME,
                            R109.TEST_GROUP,
                            R109.ERROR_ITEM_CODE,
                            ROW_NUMBER() OVER(
                                PARTITION BY R109.SERIAL_NUMBER
                                ORDER BY R109.TEST_TIME DESC
                            ) rn
                        FROM SFISM4.R109 R109
                        WHERE MODEL_NAME IN (SELECT model_name FROM sfis1.c_model_desc_t WHERE model_serial = 'ADAPTER')
                        AND MODEL_NAME NOT LIKE '900%' AND MODEL_NAME NOT LIKE '930%' AND MODEL_NAME NOT LIKE '692%'
                    )
                    WHERE rn = 1
                ) r109_latest
                  ON r109_latest.SERIAL_NUMBER = r107.SERIAL_NUMBER
                INNER JOIN sfis1.C_ERROR_CODE_T error_desc
                  ON r109_latest.TEST_CODE = error_desc.ERROR_CODE
                LEFT JOIN SFISM4.Z_KANBAN_TRACKING_T z
                ON z.SERIAL_NUMBER = r107.SERIAL_NUMBER 
                WHERE 
                    z.SERIAL_NUMBER IS NULL                
                    AND r107.MO_NUMBER NOT LIKE '8%'
                    AND r107.MODEL_NAME NOT LIKE '900%'
                    AND r107.MODEL_NAME NOT LIKE '930%'
                    AND r107.MODEL_NAME NOT LIKE '692%'
                    AND r107.WIP_GROUP NOT LIKE '%BR2C%'
                    AND (
                        r107.ERROR_FLAG IN ('7','8')
                        OR (r107.ERROR_FLAG = '1' AND r109_latest.TEST_TIME <= SYSDATE - (8/24))
                        OR r107.WORK_FLAG IN ('2','5')
                        OR (r107.WIP_GROUP LIKE '%B28M' OR r107.WIP_GROUP LIKE '%B30M')
                    )
                   AND r109_latest.TEST_CODE NOT IN (
                      'BV00','PP10','BRK00','HSK00','SCR00','C028','TA00','CAR0',
                      'C010','C012','LBxx','GB00','CLExx','GL00','BHSK00','DIR02',
                      'DIR03','DR00','GF06','CLE02','CLE03','CLE04','CLE05','CLE06',
                      'CLE07','CLE08','LB01','LB02','LB03','LB04','LB05','LB06','LB07','CK00'
                    )";

            using (var command = new OracleCommand(query, connection))
            {
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        result.Add(new RepairTaskResult
                        {
                            SERIAL_NUMBER = reader["SERIAL_NUMBER"].ToString(),
                            MODEL_NAME = reader["MODEL_NAME"].ToString(),
                            PRODUCT_LINE = reader["PRODUCT_LINE"].ToString(),
                            MO_NUMBER = reader["MO_NUMBER"].ToString(),
                            ERROR_FLAG = reader["ERROR_FLAG"] != DBNull.Value ? reader["ERROR_FLAG"].ToString() : null,
                            WORK_FLAG = reader["WORK_FLAG"] != DBNull.Value ? reader["WORK_FLAG"].ToString() : null,
                            WIP_GROUP = reader["WIP_GROUP"] != DBNull.Value ? reader["WIP_GROUP"].ToString() : null,
                            REPOSITORY = reader["REPOSITORY"] != DBNull.Value ? reader["REPOSITORY"].ToString() : null,
                            TEST_GROUP = reader["TEST_GROUP"] != DBNull.Value ? reader["TEST_GROUP"].ToString() : null,
                            TEST_TIME = reader["TEST_TIME"].ToString(),
                            TEST_CODE = reader["TEST_CODE"].ToString(),
                            ERROR_ITEM_CODE = reader["ERROR_ITEM_CODE"].ToString(),
                            ERROR_DESC = reader["ERROR_DESC"].ToString(),
                            CHECKIN_DATE = reader["CHECKIN_DATE"] != DBNull.Value ? Convert.ToDateTime(reader["CHECKIN_DATE"]) : (DateTime?)null,
                            AGING_DAY = reader["AGING_DAY"] != DBNull.Value ? reader["AGING_DAY"].ToString() : null
                        });
                    }
                }
            }

            return result;
        }

        private async Task<List<RepairTaskResult>> ExecuteAdapterReworkFgQuery()
        {
            var result = new List<RepairTaskResult>();

            await using var connection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);
            await connection.OpenAsync();

            string query = @"SELECT 
                    CASE 
                        WHEN REGEXP_LIKE(r107.MODEL_NAME, '^(900|692|930)') THEN NVL(kr.KEY_PART_SN, r107.SERIAL_NUMBER)
                        ELSE r107.SERIAL_NUMBER
                    END AS SERIAL_NUMBER,
                    r107.MODEL_NAME,
                    model_desc.PRODUCT_LINE,
                    r107.MO_NUMBER,
                    r107.ERROR_FLAG,
                    r107.WORK_FLAG,
                    r107.WIP_GROUP,
                    'B28M' AS REPOSITORY,
                    r109_latest.TEST_GROUP,
                    r109_latest.TEST_TIME,
                    r109_latest.TEST_CODE,
                    r109_latest.ERROR_ITEM_CODE,
                    error_desc.ERROR_DESC,
                    CHECK_IN.IN_DATETIME AS CHECKIN_DATE,
                    TRUNC(SYSDATE - CHECK_IN.IN_DATETIME) AS AGING_DAY
                FROM sfism4.r107 r107
                LEFT JOIN (
                    SELECT SERIAL_NUMBER, KEY_PART_SN
                    FROM (
                        SELECT kr.SERIAL_NUMBER, kr.KEY_PART_SN,
                               ROW_NUMBER() OVER (PARTITION BY kr.SERIAL_NUMBER ORDER BY kr.WORK_TIME DESC) rn
                        FROM sfism4.R_WIP_KEYPARTS_T kr
                        WHERE kr.GROUP_NAME = 'SFG_LINK_FG' 
                        AND LENGTH(kr.SERIAL_NUMBER) IN (12, 18, 21, 20, 23)
                          AND LENGTH(kr.KEY_PART_SN) IN (14, 13)
                    )
                    WHERE rn = 1
                ) kr ON r107.SERIAL_NUMBER = kr.SERIAL_NUMBER

                -- JOIN để lấy WIP_GROUP của SFG
                LEFT JOIN sfism4.r107 r107_sfg 
                    ON r107_sfg.SERIAL_NUMBER = kr.KEY_PART_SN

                JOIN sfis1.c_model_desc_t model_desc
                  ON r107.model_name = model_desc.model_name
             LEFT JOIN (
                SELECT SERIAL_NUMBER, MAX(IN_DATETIME) AS IN_DATETIME
                FROM SFISM4.R_REPAIR_IN_OUT_T 
                GROUP BY SERIAL_NUMBER
            ) CHECK_IN
            ON CHECK_IN.SERIAL_NUMBER = r107.SERIAL_NUMBER
                LEFT JOIN (
                    SELECT SERIAL_NUMBER, TEST_CODE, TEST_TIME, TEST_GROUP, ERROR_ITEM_CODE
                    FROM (
                        SELECT R109.SERIAL_NUMBER,
                               R109.TEST_CODE,
                               R109.TEST_TIME,
                               R109.TEST_GROUP,
                               R109.ERROR_ITEM_CODE,
                               ROW_NUMBER() OVER(PARTITION BY R109.SERIAL_NUMBER ORDER BY R109.TEST_TIME DESC, R109.TEST_CODE DESC) AS rn
                        FROM SFISM4.R109 R109
                    )
                    WHERE rn = 1
                ) r109_latest
                  ON r109_latest.SERIAL_NUMBER = r107.SERIAL_NUMBER
                INNER JOIN sfis1.C_ERROR_CODE_T error_desc
                  ON r109_latest.TEST_CODE = error_desc.ERROR_CODE
                WHERE model_desc.MODEL_SERIAL = 'ADAPTER'
                  AND r107.SERIAL_NUMBER NOT IN (SELECT SERIAL_NUMBER FROM SFISM4.Z_KANBAN_TRACKING_T)
                  AND r107.MO_NUMBER LIKE '4%'
                  AND (r107.WIP_GROUP LIKE '%B28M%' or r107.error_flag in ('7') or r107.work_flag in ('2'))
                AND (r107_sfg.WIP_GROUP IS NULL OR r107_sfg.WIP_GROUP NOT LIKE '%BR2C%')";

            using var command = new OracleCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new RepairTaskResult
                {
                    SERIAL_NUMBER = reader["SERIAL_NUMBER"].ToString(),
                    MODEL_NAME = reader["MODEL_NAME"].ToString(),
                    PRODUCT_LINE = reader["PRODUCT_LINE"].ToString(),
                    MO_NUMBER = reader["MO_NUMBER"].ToString(),
                    ERROR_FLAG = reader["ERROR_FLAG"] != DBNull.Value ? reader["ERROR_FLAG"].ToString() : null,
                    WORK_FLAG = reader["WORK_FLAG"] != DBNull.Value ? reader["WORK_FLAG"].ToString() : null,
                    WIP_GROUP = reader["WIP_GROUP"] != DBNull.Value ? reader["WIP_GROUP"].ToString() : null,
                    REPOSITORY = reader["REPOSITORY"] != DBNull.Value ? reader["REPOSITORY"].ToString() : null,
                    TEST_GROUP = reader["TEST_GROUP"] != DBNull.Value ? reader["TEST_GROUP"].ToString() : null,
                    TEST_TIME = reader["TEST_TIME"]?.ToString(),
                    TEST_CODE = reader["TEST_CODE"]?.ToString(),
                    ERROR_ITEM_CODE = reader["ERROR_ITEM_CODE"]?.ToString(),
                    ERROR_DESC = reader["ERROR_DESC"]?.ToString(),
                    CHECKIN_DATE = reader["CHECKIN_DATE"] != DBNull.Value ? Convert.ToDateTime(reader["CHECKIN_DATE"]) : (DateTime?)null,
                    AGING_DAY = reader["AGING_DAY"] != DBNull.Value ? reader["AGING_DAY"].ToString() : null
                });
            }
            return result;
        }

        // --- Hàm helper mới để lấy lịch sử R109 ---
        private async Task<Dictionary<string, int>> GetConsecutiveFailCountsAsync(List<string> serialNumbers)
        {
            var result = new Dictionary<string, int>();

            // Oracle giới hạn mệnh đề IN (...) khoảng 1000 phần tử, nên cần chia nhỏ
            var chunks = serialNumbers.Chunk(1000);

            await using var connection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);
            await connection.OpenAsync();

            foreach (var chunk in chunks)
            {
                // Tạo danh sách param :sn0, :sn1...
                var snParams = chunk.Select((sn, index) => $":sn{index}").ToList();
                var inClause = string.Join(",", snParams);

                string query = $@"
                    SELECT SERIAL_NUMBER, TEST_GROUP, TEST_TIME
                    FROM SFISM4.R109
                    WHERE SERIAL_NUMBER IN ({inClause})
                    AND MODEL_NAME IN (SELECT model_name FROM sfis1.c_model_desc_t WHERE model_serial = 'ADAPTER')
                    ORDER BY SERIAL_NUMBER, TEST_TIME DESC";

                using var command = new OracleCommand(query, connection);

                // Add parameters
                for (int i = 0; i < chunk.Length; i++)
                {
                    command.Parameters.Add(new OracleParameter($":sn{i}", chunk[i]));
                }

                using var reader = await command.ExecuteReaderAsync();

                // Đọc dữ liệu vào bộ nhớ tạm để xử lý
                var historyList = new List<(string SN, string Group)>();
                while (await reader.ReadAsync())
                {
                    historyList.Add((
                        reader["SERIAL_NUMBER"].ToString(),
                        reader["TEST_GROUP"]?.ToString() ?? ""
                    ));
                }

                // Xử lý Logic đếm liên tiếp (Consecutive Count) trong C#
                var groupedBySn = historyList.GroupBy(x => x.SN);
                foreach (var group in groupedBySn)
                {
                    var items = group.ToList(); // Đã sort DESC từ SQL
                    if (!items.Any()) continue;

                    string currentTestGroup = items[0].Group;
                    int consecutiveCount = 1;

                    // Duyệt từ phần tử thứ 2 trở đi
                    for (int i = 1; i < items.Count; i++)
                    {
                        if (items[i].Group == currentTestGroup)
                        {
                            consecutiveCount++;
                        }
                        else
                        {
                            // Gặp group khác -> dừng đếm
                            break;
                        }
                    }

                    // Lưu kết quả
                    result[group.Key] = consecutiveCount;
                }
            }

            return result;
        }


        //============================================AFTER STATUS================================================
        [HttpGet("report-repair-after")]
        public async Task<IActionResult> RepairAfterKanbanBasic()
        {
            try
            {
                var databaseRecords = await BuildAfterKanbanBasicRecordsAsync();

                var statusCounts = databaseRecords
                    .Where(r => !string.IsNullOrWhiteSpace(r.Status))
                    .GroupBy(r => r.Status, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new
                    {
                        Status = g.Key,
                        Count = g.Count()
                    })
                    .ToList();

                if (!databaseRecords.Any())
                {
                    return NotFound(new { message = "Khong tim thay du lieu!", totalCount = 0 });
                }

                var statusDetails = databaseRecords
                    .Where(r => !string.IsNullOrWhiteSpace(r.Status))
                    .GroupBy(r => r.Status, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

                return Ok(new
                {
                    totalCount = databaseRecords.Count,
                    data = databaseRecords,
                    statusCounts,
                    statusDetails,
                });

            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Xay ra loi", error = ex.Message });
            }
        }

        private async Task<List<BonepileAfterKanbanBasicRecord>> BuildAfterKanbanBasicRecordsAsync()
        {
            var allData = await ExecuteBonepileAfterKanbanBasicQuery();

            var excludedSNs = GetExcludedSerialNumbers();
            if (excludedSNs.Any())
            {
                allData = allData.Where(d => !excludedSNs.Contains(d.SERIAL_NUMBER?.Trim().ToUpper())).ToList();
            }

            var snList = allData
                .Select(d => d.SERIAL_NUMBER?.Trim().ToUpper())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            var scrapCategories = await _sqlContext.ScrapLists
                .Where(s => snList.Contains(s.SN.Trim().ToUpper()))
                .Select(s => new { SN = s.SN, ApplyTaskStatus = s.ApplyTaskStatus, TaskNumber = s.TaskNumber })
                .ToListAsync();

            var scrapDict = scrapCategories.ToDictionary(
                c => c.SN?.Trim().ToUpper() ?? "",
                c => (ApplyTaskStatus: c.ApplyTaskStatus, TaskNumber: c.TaskNumber),
                StringComparer.OrdinalIgnoreCase
            );

            var khoOkSet = (await _sqlContext.KhoOks
                    .Where(k => snList.Contains(k.SERIAL_NUMBER.Trim().ToUpper()))
                    .Select(k => k.SERIAL_NUMBER)
                    .ToListAsync())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var exportRecords = await _sqlContext.Exports
                .Where(e => snList.Contains(e.SerialNumber.Trim().ToUpper()) && e.CheckingB36R > 0 && e.CheckingB36R <= 4)
                .ToListAsync();

            var exportDict = exportRecords
                .GroupBy(e => e.SerialNumber?.Trim().ToUpper() ?? "")
                .Select(g => g.OrderByDescending(e => e.ExportDate).First())
                .ToDictionary(
                    e => e.SerialNumber.Trim().ToUpper(),
                    e => (e.CheckingB36R, e.ExportDate),
                    StringComparer.OrdinalIgnoreCase);

            var validStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ScrapLackTask",
                "ScrapHasTask",
                "WaitingLink",
                "Linked",
                "WaitingApprovalScrap",
                "ApprovedBGA",
                "WaitingApprovalBGA",
                "RepairInRE",
                "WaitingCheckOut",
                "Can'tRepairProcess",
                "PendingInstructions"
            };

            var databaseRecords = new List<BonepileAfterKanbanBasicRecord>();
            var repairSnsToCheck = new List<string>();

            foreach (var b in allData)
            {
                if (string.IsNullOrWhiteSpace(b.SERIAL_NUMBER))
                {
                    continue;
                }

                var snKey = b.SERIAL_NUMBER.Trim().ToUpper();
                string status;

                if (scrapDict.TryGetValue(snKey, out var scrapInfo))
                {
                    var applyTaskStatus = scrapInfo.ApplyTaskStatus;

                    if (applyTaskStatus == 5 || applyTaskStatus == 6 || applyTaskStatus == 7)
                    {
                        status = "ScrapHasTask";
                    }
                    else if (applyTaskStatus == 0 || applyTaskStatus == 1)
                    {
                        if (string.IsNullOrEmpty(scrapInfo.TaskNumber) || scrapInfo.TaskNumber == "N/A")
                            status = "ScrapLackTask";
                        else status = "ScrapHasTask";
                    }
                    else if (applyTaskStatus == 2)
                    {
                        status = "WaitingApprovalScrap";
                    }
                    else if (applyTaskStatus == 4)
                    {
                        status = "WaitingApprovalBGA";
                    }
                    else if (applyTaskStatus == 8)
                    {
                        status = "Can'tRepairProcess";
                    }
                    else if (applyTaskStatus == 22)
                    {
                        status = "PendingInstructions";
                    }
                    else
                    {
                        status = "ApprovedBGA";
                    }
                }
                else if (exportDict.TryGetValue(snKey, out var exportInfo))
                {
                    switch (exportInfo.CheckingB36R)
                    {
                        case 1:
                            status = "WaitingLink";
                            break;
                        case 2:
                            status = "Linked";
                            break;
                        default:
                            status = "RepairInRE";
                            break;
                    }
                }
                else
                {
                    status = "RepairInRE";
                }

                var isInKhoOk = khoOkSet.Contains(snKey);
                if (string.Equals(status, "RepairInRE", StringComparison.OrdinalIgnoreCase) && isInKhoOk)
                {
                    status = "WaitingCheckOut";
                }

                if (!validStatuses.Contains(status))
                {
                    continue;
                }

                if (status == "RepairInRE")
                {
                    repairSnsToCheck.Add(b.SERIAL_NUMBER);
                }

                databaseRecords.Add(new BonepileAfterKanbanBasicRecord
                {
                    SN = b.SERIAL_NUMBER,
                    ModelName = b.MODEL_NAME,
                    MoNumber = b.MO_NUMBER,
                    ProductLine = b.PRODUCT_LINE,
                    WipGroupSFC = b.WIP_GROUP_SFC,
                    WipGroupKANBAN = b.WIP_GROUP_KANBAN,
                    ErrorFlag = b.ERROR_FLAG,
                    WorkFlag = b.WORK_FLAG,
                    TestGroup = b.TEST_GROUP,
                    TestTime = b.TEST_TIME?.ToString("yyyy-MM-dd HH:mm:ss"),
                    TestCode = b.TEST_CODE,
                    ErrorCodeItem = b.ERROR_ITEM_CODE,
                    ErrorDesc = b.ERROR_DESC,
                    Aging = b.AGING,
                    AgingOld = b.AGING_OLD,
                    Status = status
                });
            }

            if (repairSnsToCheck.Any())
            {
                var consecutiveFailCounts = await GetConsecutiveFailCountsAsync(repairSnsToCheck);

                foreach (var record in databaseRecords)
                {
                    if (record.Status == "RepairInRE")
                    {
                        if (consecutiveFailCounts.TryGetValue(record.SN, out int failCount))
                        {
                            double agingVal = record.Aging ?? 0;
                            string agingSuffix = agingVal > 30 ? ">30" : "<30";
                            string baseStatusName = "";

                            if (failCount <= 1)
                            {
                                baseStatusName = "waiting repair";
                            }
                            else if (failCount == 2)
                            {
                                baseStatusName = "CB repaired once but";
                            }
                            else
                            {
                                baseStatusName = "CB repaired twice but";
                            }

                            record.Status = $"{baseStatusName} aging day {agingSuffix}";
                        }
                    }
                }
            }

            return databaseRecords;
        }

        [HttpPost("bonepile-repository-history/snapshot")]
        public async Task<IActionResult> CaptureBonepileRepositorySnapshot()
        {
            try
            {
                var snapshotDate = DateTime.Today;
                var beforeData = await BuildAdapterRepairDataAsync();
                var afterRecords = await BuildAfterKanbanBasicRecordsAsync();

                var repositoryCounts = RepositoryBuckets.ToDictionary(bucket => bucket, _ => 0, StringComparer.OrdinalIgnoreCase);

                foreach (var record in beforeData.Records)
                {
                    var repository = (record.Repository ?? string.Empty).Trim();
                    if (repositoryCounts.ContainsKey(repository))
                    {
                        repositoryCounts[repository] += 1;
                    }
                }

                repositoryCounts["B36R"] = afterRecords.Count;

                var existingSnapshots = await _sqlContext.BonepileRepositoryDailyHistories
                    .Where(h => h.SnapshotDate == snapshotDate)
                    .ToListAsync();

                if (existingSnapshots.Any())
                {
                    _sqlContext.BonepileRepositoryDailyHistories.RemoveRange(existingSnapshots);
                }

                var newSnapshots = repositoryCounts.Select(entry => new BonepileRepositoryDailyHistory
                {
                    SnapshotDate = snapshotDate,
                    Repository = entry.Key,
                    Count = entry.Value
                }).ToList();

                _sqlContext.BonepileRepositoryDailyHistories.AddRange(newSnapshots);
                await _sqlContext.SaveChangesAsync();

                return Ok(new
                {
                    snapshotDate = snapshotDate.ToString("yyyy-MM-dd"),
                    repositoryCounts
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Xay ra loi", error = ex.Message });
            }
        }

        [HttpGet("bonepile-repository-history")]
        public async Task<IActionResult> GetBonepileRepositoryHistory()
        {
            try
            {
                var history = await _sqlContext.BonepileRepositoryDailyHistories
                    .AsNoTracking()
                    .OrderBy(h => h.SnapshotDate)
                    .ToListAsync();

                var groupedHistory = history
                    .GroupBy(h => h.SnapshotDate.Date)
                    .Select(group => new
                    {
                        snapshotDate = group.Key.ToString("yyyy-MM-dd"),
                        repositoryCounts = RepositoryBuckets.ToDictionary(
                            bucket => bucket,
                            bucket => group.FirstOrDefault(item => string.Equals(item.Repository, bucket, StringComparison.OrdinalIgnoreCase))?.Count ?? 0,
                            StringComparer.OrdinalIgnoreCase)
                    })
                    .ToList();

                return Ok(groupedHistory);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Xay ra loi", error = ex.Message });
            }
        }

        private async Task<List<BonepileAfterKanbanResult>> ExecuteBonepileAfterKanbanBasicQuery()
        {
            var result = new List<BonepileAfterKanbanResult>();

            await using var connection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);
            await connection.OpenAsync();

            string query = @"
                SELECT /*+ LEADING(A) USE_NL(A B R107 KP R109X R109_OLD) */
                  A.SERIAL_NUMBER,
                  KP.PARENT_SN AS FG,
                  R107.MO_NUMBER,
                  A.MODEL_NAME,
                  B.PRODUCT_LINE,
                  A.WIP_GROUP AS WIP_GROUP_KANBAN,
                  R107.WIP_GROUP AS WIP_GROUP_SFC,
                  R107.ERROR_FLAG,
                  R107.WORK_FLAG,
                  rep_detail.DATA19_COMBINED,
                  R109X.TEST_GROUP,
                  R109X.TEST_TIME,
                  R109X.TEST_CODE,
                  R109X.ERROR_ITEM_CODE,
                  E.ERROR_DESC,

                  -- AGING theo test_time mới nhất
                  TRUNC(SYSDATE) - TRUNC(R109X.TEST_TIME) AS AGING,

                  -- AGING theo test_time cũ nhất
                  TRUNC(SYSDATE) - TRUNC(R109_OLD.TEST_TIME) AS AGING_OLDEST

                FROM SFISM4.Z_KANBAN_TRACKING_T A
                JOIN SFIS1.C_MODEL_DESC_T B ON A.MODEL_NAME = B.MODEL_NAME
                JOIN SFISM4.R107 R107 ON R107.SERIAL_NUMBER = A.SERIAL_NUMBER

                /* mapping Parent_SN theo WORK_TIME mới nhất */
                LEFT JOIN (
                  SELECT SERIAL_NUMBER AS PARENT_SN, KEY_PART_SN
                  FROM (
                    SELECT kp.SERIAL_NUMBER, kp.KEY_PART_SN,
                           ROW_NUMBER() OVER (PARTITION BY kp.KEY_PART_SN ORDER BY kp.WORK_TIME DESC) rn
                    FROM SFISM4.P_WIP_KEYPARTS_T kp
                    WHERE kp.GROUP_NAME = 'SFG_LINK_FG'
                      AND LENGTH(kp.SERIAL_NUMBER) IN (11,12,18,20,21,23)
                      AND LENGTH(kp.KEY_PART_SN)   IN (13,14)
                  ) WHERE rn = 1
                ) KP ON KP.KEY_PART_SN = A.SERIAL_NUMBER

                /* Test mới nhất (SN ∪ Parent_SN) */
                LEFT JOIN (
                  SELECT *
                  FROM (
                    SELECT
                      base.SN,
                      r.TEST_GROUP,
                      r.TEST_TIME,
                      r.TEST_CODE,
                      r.ERROR_ITEM_CODE,
                      ROW_NUMBER() OVER (
                        PARTITION BY base.SN
                        ORDER BY r.TEST_TIME DESC, r.TEST_CODE DESC
                      ) AS rn
                    FROM (
                      SELECT A.SERIAL_NUMBER AS SN, A.SERIAL_NUMBER AS CAND_SN
                      FROM SFISM4.Z_KANBAN_TRACKING_T A
                      UNION ALL
                      SELECT A.SERIAL_NUMBER AS SN, KP.PARENT_SN AS CAND_SN
                      FROM SFISM4.Z_KANBAN_TRACKING_T A
                      JOIN (
                        SELECT SERIAL_NUMBER AS PARENT_SN, KEY_PART_SN
                        FROM (
                          SELECT kp.SERIAL_NUMBER, kp.KEY_PART_SN,
                                 ROW_NUMBER() OVER (PARTITION BY kp.KEY_PART_SN ORDER BY kp.WORK_TIME DESC) rn
                          FROM SFISM4.P_WIP_KEYPARTS_T kp
                          WHERE kp.GROUP_NAME = 'SFG_LINK_FG'
                        ) WHERE rn = 1
                      ) KP ON KP.KEY_PART_SN = A.SERIAL_NUMBER
                    ) base
                    JOIN SFISM4.R109 r
                      ON r.SERIAL_NUMBER = base.CAND_SN
                    WHERE r.TEST_TIME IS NOT NULL
                  )
                  WHERE rn = 1
                ) R109X ON R109X.SN = A.SERIAL_NUMBER

                LEFT JOIN (
                    SELECT SERIAL_NUMBER,
                           LISTAGG(TRIM(DATA19), ' | ') 
                             WITHIN GROUP (ORDER BY MIN_DATE) AS DATA19_COMBINED
                    FROM (
                        SELECT DISTINCT SERIAL_NUMBER, TRIM(DATA19) AS DATA19,
                                        MIN(DATE3) AS MIN_DATE
                        FROM sfism4.R_REPAIR_TASK_DETAIL_T
                        WHERE UPPER(DATA17) IN ('CONFIRM', 'SAVE')
                          AND DATA19 IS NOT NULL
                          AND DATA19 != 'CONFIRM_PUT_B36R'
                          AND MODEL_NAME IN (SELECT model_name FROM sfis1.c_model_desc_t WHERE model_serial = 'ADAPTER')
                        GROUP BY SERIAL_NUMBER, TRIM(DATA19)
                    )
                    GROUP BY SERIAL_NUMBER
                ) rep_detail
                ON rep_detail.SERIAL_NUMBER = A.SERIAL_NUMBER
                /* Test cũ nhất (SN ∪ Parent_SN) */
                LEFT JOIN (
                  SELECT *
                  FROM (
                    SELECT
                      base.SN,
                      r.TEST_TIME,
                      ROW_NUMBER() OVER (
                        PARTITION BY base.SN
                        ORDER BY r.TEST_TIME ASC
                      ) AS rn
                    FROM (
                      SELECT A.SERIAL_NUMBER AS SN, A.SERIAL_NUMBER AS CAND_SN
                      FROM SFISM4.Z_KANBAN_TRACKING_T A
                      UNION ALL
                      SELECT A.SERIAL_NUMBER AS SN, KP.PARENT_SN AS CAND_SN
                      FROM SFISM4.Z_KANBAN_TRACKING_T A
                      JOIN (
                        SELECT SERIAL_NUMBER AS PARENT_SN, KEY_PART_SN
                        FROM (
                          SELECT kp.SERIAL_NUMBER, kp.KEY_PART_SN,
                                 ROW_NUMBER() OVER (PARTITION BY kp.KEY_PART_SN ORDER BY kp.WORK_TIME DESC) rn
                          FROM SFISM4.P_WIP_KEYPARTS_T kp
                          WHERE kp.GROUP_NAME = 'SFG_LINK_FG'
                        ) WHERE rn = 1
                      ) KP ON KP.KEY_PART_SN = A.SERIAL_NUMBER
                    ) base
                    JOIN SFISM4.R109 r
                      ON r.SERIAL_NUMBER = base.CAND_SN
                    WHERE r.TEST_TIME IS NOT NULL
                  )
                  WHERE rn = 1
                ) R109_OLD ON R109_OLD.SN = A.SERIAL_NUMBER

                LEFT JOIN SFIS1.C_ERROR_CODE_T E ON E.ERROR_CODE = R109X.TEST_CODE

                WHERE
                  A.WIP_GROUP LIKE '%B36R%'
                  AND B.MODEL_SERIAL = 'ADAPTER'
                  AND R107.WIP_GROUP NOT LIKE '%BR2C%'
                  AND R107.WIP_GROUP NOT LIKE '%B36R_STOCKIN%'";

            using var command = new OracleCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new BonepileAfterKanbanResult
                {
                    SERIAL_NUMBER = reader["SERIAL_NUMBER"]?.ToString(),
                    MO_NUMBER = reader["MO_NUMBER"]?.ToString(),
                    MODEL_NAME = reader["MODEL_NAME"]?.ToString(),
                    PRODUCT_LINE = reader["PRODUCT_LINE"]?.ToString(),
                    WIP_GROUP_KANBAN = reader["WIP_GROUP_KANBAN"]?.ToString(),
                    WIP_GROUP_SFC = reader["WIP_GROUP_SFC"]?.ToString(),
                    ERROR_FLAG = reader["ERROR_FLAG"]?.ToString(),
                    WORK_FLAG = reader["WORK_FLAG"]?.ToString(),
                    TEST_GROUP = reader["TEST_GROUP"]?.ToString(),
                    TEST_TIME = reader["TEST_TIME"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["TEST_TIME"]),
                    TEST_CODE = reader["TEST_CODE"]?.ToString(),
                    ERROR_ITEM_CODE = reader["ERROR_ITEM_CODE"]?.ToString(),
                    ERROR_DESC = reader["ERROR_DESC"]?.ToString(),
                    AGING = reader["AGING"] == DBNull.Value ? (double?)null : Convert.ToDouble(reader["AGING"]),
                    AGING_OLD = reader["AGING_OLDEST"] == DBNull.Value ? (double?)null : Convert.ToDouble(reader["AGING_OLDEST"])
                });
            }
            return result;
        }

        //Lay nhung SN scrap trong file excel.
        private List<string> GetExcludedSerialNumbers()
        {
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "Uploads");
            var filePath = Path.Combine(uploadsFolder, "ScrapOk.xlsx");
            var snList = new List<string>();

            if (System.IO.File.Exists(filePath))
            {
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                using var package = new ExcelPackage(new FileInfo(filePath));
                var worksheet = package.Workbook.Worksheets.FirstOrDefault();

                if (worksheet != null)
                {
                    int rowCount = worksheet.Dimension.Rows;
                    for (int row = 1; row <= rowCount; row++)
                    {
                        var sn = worksheet.Cells[row, 1].Text.Trim();
                        if (!string.IsNullOrEmpty(sn))
                        {
                            snList.Add(sn.ToUpper());
                        }
                    }
                }
            }

            return snList;
        }


    }
}
