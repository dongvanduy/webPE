using API_WEB.Models.Bonepile;
using API_WEB.Models.Repositories;
using API_WEB.ModelsDB;
using API_WEB.ModelsOracle;
using DocumentFormat.OpenXml.Drawing.Charts;
using DocumentFormat.OpenXml.InkML;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using Oracle.ManagedDataAccess.Client;
using System.Linq;
using System.Runtime.Intrinsics.X86;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace API_WEB.Controllers.Repositories
{
    [Route("[controller]")]
    [ApiController]
    public class Bonepile2Controller : ControllerBase
    {

        private readonly CSDL_NE _sqlContext;
        private readonly OracleDbContext _oracleContext;

        public Bonepile2Controller(CSDL_NE sqlContext, OracleDbContext oracleContext)
        {
            _sqlContext = sqlContext;
            _oracleContext = oracleContext;
        }

        [HttpPost("data")]
        public async Task<IActionResult> DetailStatus([FromBody] StatusRequest request)
        {
            try
            {
                if (request == null)
                {
                    return BadRequest(new { message = "Yeu cau khong hop le!" });
                }
                var statuses = request.Statuses?.Any() == true ? request.Statuses : new List<string> { "Repair", "CheckOut", "CheckIn", "WaitingLink", "Online", "WaitingKanBanIn", "WaitingApproveScrap", "Scrap" };
                //Kiem tra dinh dang ngay
                if (string.IsNullOrEmpty(request.StartDate) || string.IsNullOrEmpty(request.EndDate))
                {
                    return BadRequest(new { message = "StartDate va EndDate khong duoc de trong!" });
                }
                if (!DateTime.TryParseExact(request.StartDate, "yyyy/MM/dd HH:mm", null, System.Globalization.DateTimeStyles.None, out _)
                    || !DateTime.TryParseExact(request.EndDate, "yyyy/MM/dd HH:mm", null, System.Globalization.DateTimeStyles.None, out _))
                {
                    return BadRequest(new { message = "Dinh dang ngay gio khong hop le!!" });
                }

                //THuc thien truy van Oracle
                var bonepileData = await ExecuteOracleQuery(request);

                if (!bonepileData.Any())
                {
                    return NotFound(new { message = "Khong tim thay du lieu!!", count = 0 });
                }


                var scrapCategories = await _sqlContext.ScrapLists
                    .Where(s => bonepileData.Select(b => b.SN).Contains(s.SN))
                    .Select(s => new ScrapListCategory { SN = s.SN, Category = s.Category })
                    .ToListAsync();

                var validStatuses = new HashSet<string> { "Repair", "CheckOut", "CheckIn", "WaitingLink", "Online", "WaitingKanBanIn", "WaitingApproveScrap", "Scrap" };
                var result = bonepileData.Select(b =>
                {
                    var scrapCategory = scrapCategories.FirstOrDefault(c => c.SN == b.SN);
                    string status;
                    if (scrapCategory != null)
                    {
                        status = scrapCategory.Category == "Scrap" ? "Scrap" : "WaitingApproveScrap";
                    }
                    else if (b.WIP_GROUP.Contains("B31M"))
                    {
                        status = "WaitingLink";
                    }
                    else
                    {
                        // Khong co du lieu trong ScrapLists, dung ERROR_FLAG
                        status = b.ERROR_FLAG switch
                        {
                            "7" => "Repair",
                            "8" => "CheckOut",
                            "1" => "CheckIn",
                            "0" => b.WIP_GROUP.Contains("KANBAN_IN") ? "WaitingKanBanIn" : "Online",
                            _ => "Repair" //
                        };
                    }
                    return new
                    {
                        SN = b.SN,
                        MoNumber = b.MO_NUMBER,
                        ModelName = b.MODEL_NAME,
                        ProductLine = b.PRODUCT_LINE,
                        FailStation = b.FAIL_STATION,
                        Symptom = b.SYMPTOM,
                        ErrorDescription = b.ERROR_DESC,
                        Time = b.TIME.ToString("yyyy-MM-dd HH:mm:ss"),
                        Flag = b.FLAG,
                        PONo = b.PO_NO,
                        POItem = b.PO_ITEM,
                        FailureAging = b.FAILURE_AGING,
                        WipGroup = b.WIP_GROUP,
                        VersionCode = b.VERSION_CODE,
                        WorkFlag = b.WORK_FLAG,
                        ErrorFlag = b.ERROR_FLAG,
                        MONew = b.MO_NEW,
                        Status = status,
                        CheckinRepairTime = b.CHECKIN_REPAIR_TIME?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A",
                        CheckoutRepairTime = b.CHECKOUT_REPAIR_TIME?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A",
                        ScrapStatus = b.SCRAP_STATUS,
                        Category = scrapCategory?.Category ?? "N/A"
                    };
                }).Where(r => validStatuses.Contains(r.Status) && statuses.Contains(r.Status)).ToList();
                if (!result.Any())
                {
                    return NotFound(new { message = "Khong tim thay du lieu!!", count = 0 });
                }
                return Ok(new
                {
                    count = result.Count,
                    data = result
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Xay ra loi", error = ex.Message });
            }
        }

        [HttpPost("status-count")]
        public async Task<IActionResult> StatusCount([FromBody] StatusRequest request)
        {
            try
            {
                // Ki?m tra ??u vao
                if (request == null)
                {
                    return BadRequest(new { message = "Yeu cau khong hop le!" });
                }

                var statuses = request.Statuses?.Any() == true ? request.Statuses : new List<string> { "Repair", "CheckOut", "CheckIn", "WaitingLink", "Online", "WaitingKanBanIn", "WaitingApproveScrap", "Scrap" };

                if (string.IsNullOrEmpty(request.StartDate) || string.IsNullOrEmpty(request.EndDate))
                {
                    return BadRequest(new { message = "StartDate va EndDate khong duoc de trong!" });
                }
                if (!DateTime.TryParseExact(request.StartDate, "yyyy/MM/dd HH:mm", null, System.Globalization.DateTimeStyles.None, out _)
                    || !DateTime.TryParseExact(request.EndDate, "yyyy/MM/dd HH:mm", null, System.Globalization.DateTimeStyles.None, out _))
                {
                    return BadRequest(new { message = "Dinh dang ngay gio khong hop le!!" });
                }

                var bonepileData = await ExecuteOracleQuery(request);

                if (!bonepileData.Any())
                {
                    return NotFound(new { message = "Khong tim thay du lieu!!", count = 0, statusCounts = new { } });
                }

                // L?y Category t? ScrapLists (SQL Server)
                var scrapCategories = await _sqlContext.ScrapLists
                    .Where(s => bonepileData.Select(b => b.SN).Contains(s.SN))
                    .Select(s => new ScrapListCategory { SN = s.SN, Category = s.Category })
                    .ToListAsync();


                var validStatuses = new HashSet<string> { "Repair", "CheckOut", "CheckIn", "WaitingLink", "Online", "WaitingKanBanIn", "WaitingApproveScrap", "Scrap", "Linked" };
                var statusCounts = new Dictionary<string, int>
                {
                    { "Repair", 0 },
                    { "CheckOut", 0 },
                    { "CheckIn", 0 },
                    { "WaitingLink", 0 },
                    { "Online", 0 },
                    { "WaitingKanBanIn", 0 },
                    { "WaitingApproveScrap", 0 },
                    { "Scrap", 0 }
                };

                foreach (var b in bonepileData)
                {
                    var scrapCategory = scrapCategories.FirstOrDefault(c => c.SN == b.SN);
                    string status;
                    if (scrapCategory != null)
                    {
                        status = scrapCategory.Category == "Scrap" ? "Scrap" : "WaitingApproveScrap";
                    }
                    else if (b.WIP_GROUP.Contains("B31M"))
                    {
                        status = "WaitingLink";
                    }
                    else
                    {
                        status = b.ERROR_FLAG switch
                        {
                            "7" => "Repair",
                            "8" => "CheckOut",
                            "1" => "CheckIn",
                            "0" => b.WIP_GROUP.Contains("KANBAN_IN") ? "WaitingKanBanIn" : "Online",
                            //"0" => "Online",
                            _ => "Repair"
                        };
                    }

                    if (validStatuses.Contains(status) && statuses.Contains(status))
                    {
                        statusCounts[status]++;
                    }
                }

                // T?ng s? b?n ghi kh?p
                int totalCount = statusCounts.Values.Sum();

                if (totalCount == 0)
                {
                    return NotFound(new { message = "Khong tim thay du lieu!!", count = 0, statusCounts = new { } });
                }

                // Lo?i b? cac tr?ng thai co s? l??ng 0 ?? tr? v? JSON g?n h?n
                var filteredStatusCounts = statusCounts
                    .Where(kv => kv.Value > 0)
                    .ToDictionary(kv => kv.Key, kv => kv.Value);

                return Ok(new
                {
                    count = totalCount,
                    statusCounts = filteredStatusCounts
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Xay ra loi", error = ex.Message });
            }
        }

        // GET /api/bonepile2/waiting-count?from=2025-09-09%2007:30&to=2025-09-10%2007:30
        [HttpGet("waiting-summary")]
        public async Task<IActionResult> GetWaitingCount([FromQuery] string? from, [FromQuery] string? to)
        {
            try
            {
                // 1) Mặc định: 7:30 hôm qua -> bây giờ
                var now = DateTime.Now;
                var today0730 = new DateTime(now.Year, now.Month, now.Day, 7, 30, 0);
                var defaultStart = today0730.AddDays(-1);
                var defaultEnd = now; // tới thời điểm hiện tại

                // 2) Parse nếu người dùng truyền from/to (optional)
                var formats = new[] { "yyyy/MM/dd HH:mm", "yyyy-MM-dd HH:mm", "yyyy/MM/dd", "yyyy-MM-dd" };
                var startDt = ParseOrDefault(from, formats, defaultStart);
                var endDt = ParseOrDefault(to, formats, defaultEnd);

                if (endDt <= startDt)
                    return BadRequest(new { message = "`to` phải lớn hơn `from`." });

                var q = _sqlContext.BonepileWaiting.AsNoTracking();

                var linked = await q.Where(w => w.TimeInLink != null &&
                                                w.TimeInLink >= startDt &&
                                                w.TimeInLink <= endDt).CountAsync();

                var linkedKanban = await q.Where(w => w.TimeInKanban != null &&
                                                      w.TimeInKanban >= startDt &&
                                                      w.TimeInKanban <= endDt).CountAsync();

                return Ok(new
                {
                    from = startDt.ToString("yyyy/MM/dd HH:mm"),
                    to = endDt.ToString("yyyy/MM/dd HH:mm"),
                    totals = new { linked, linkedKanban }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Xảy ra lỗi", error = ex.Message });
            }

            static DateTime ParseOrDefault(string? s, string[] fmts, DateTime fallback)
            {
                return !string.IsNullOrWhiteSpace(s) &&
                       DateTime.TryParseExact(s.Trim(), fmts, System.Globalization.CultureInfo.InvariantCulture,
                                              System.Globalization.DateTimeStyles.None, out var dt)
                       ? dt : fallback;
            }
        }

        private async Task<List<BonepileResult>> ExecuteOracleQuery(StatusRequest request)
        {
            var result = new List<BonepileResult>();

            await using var connection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);
            await connection.OpenAsync();

            string query = @"
         select P.*, cmd.PRODUCT_LINE from
         (
            SELECT 
                    c.SERIAL_NUMBER, 
                    c.MODEL_NAME, 
                    c.MO_NUMBER, 
                    c.FAIL_STATION, 
                    c.SYMPTOM, 
                    c.ERROR_DESC, 
                    c.TIME, 
                    c.FLAG, 
                    c.PO_NO, 
                    c.PO_ITEM, 
                    c.FAILURE_AGING, 
                    c.WIP_GROUP, 
                    c.VERSION_CODE, 
                    c.WORK_FLAG,
                    CASE 
                        WHEN c.ERROR_FLAG = '0' AND c.MO_NEW IS NOT NULL THEN '0'
                        WHEN c.ERROR_FLAG = '0' AND c.MO_NEW IS NULL THEN '2'
                        ELSE c.ERROR_FLAG 
                    END AS ERROR_FLAG,
                    c.MO_NEW, 
                    c.STATUS, 
                    c.CHECKIN_REPAIR_TIME, 
                    c.CHECKOUT_REPAIR_TIME,
                    CASE 
                        WHEN s.SERIAL_NUMBER IS NOT NULL THEN 'SCRAP' 
                        ELSE '' 
                    END AS SCRAP_STATUS
                FROM (
                    SELECT 
                        a.*, 
                        b.MO_NUMBER1, 
                        b.DATA1, 
                        b.ERROR_FLAG, 
                        b.WORK_FLAG, 
                        b.WIP_GROUP, 
                        b.VERSION_CODE,
                        CASE 
                            WHEN a.MO_NUMBER <> b.MO_NUMBER1 THEN b.MO_NUMBER1
                            WHEN a.MO_NUMBER = b.MO_NUMBER1 
                                AND b.ERROR_FLAG NOT IN ('1', '7', '8') 
                                AND b.DATA1 IS NOT NULL THEN b.DATA1
                            ELSE '' 
                        END AS MO_NEW,
                        CASE 
                            WHEN a.FAILURE_AGING <= 7 THEN 'Normal <7 DAY'
                            WHEN a.FAILURE_AGING > 7 AND a.FAILURE_AGING < 14 THEN 'Medium >7,<14 DAY'
                            ELSE 'High >14 DAY'
                        END AS STATUS,
                        r.IN_DATETIME AS CHECKIN_REPAIR_TIME,
                        r.OUT_DATETIME AS CHECKOUT_REPAIR_TIME
                    FROM (
                        SELECT 
                            a.SERIAL_NUMBER, 
                            a.MODEL_NAME, 
                            a.MO_NUMBER, 
                            a.TEST_STATION AS FAIL_STATION, 
                            a.SYMPTOM, 
                            b.ERROR_DESC, 
                            a.TIME, 
                            a.FLAG, 
                            a.PO_NO, 
                            a.PO_ITEM, 
                            ROUND((SYSDATE - a.TIME), 2) AS FAILURE_AGING
                        FROM SFISM4.NVIDIA_BONPILE_SN_LOG a
                        INNER JOIN SFIS1.C_ERROR_CODE_T b 
                            ON a.SYMPTOM = b.ERROR_CODE
                            WHERE a.TIME BETWEEN TO_DATE(:start_date, 'YYYY/MM/DD HH24:MI')
                            AND TO_DATE(:end_date, 'YYYY/MM/DD HH24:MI')
                            AND NOT EXISTS (
                                SELECT 1 
                                FROM SFISM4.Z_KANBAN_TRACKING_T z 
                                WHERE z.SERIAL_NUMBER = a.SERIAL_NUMBER
                            )
                            AND NOT EXISTS (
                                SELECT 1 
                                FROM SFISM4.R_NV_REPAIR_BONEPILE_T r 
                                WHERE r.SERIAL_NUMBER = a.SERIAL_NUMBER 
                                AND r.DATA7 = 'SCRAP'
                            )
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
                            ON a.SERIAL_NUMBER = b.KEY_PART_SN 
                            AND b.TYPE = 'LINK_MO'
                    ) b 
                        ON a.SERIAL_NUMBER = b.SERIAL_NUMBER
                    LEFT JOIN SFISM4.R_REPAIR_IN_OUT_T r 
                        ON a.SERIAL_NUMBER = r.SERIAL_NUMBER 
                        AND a.FAIL_STATION = r.STATION_NAME 
                        AND a.MO_NUMBER = r.MO_NUMBER
                ) c
                LEFT JOIN (
                    SELECT SERIAL_NUMBER, TEST_GROUP, TEST_CODE
                    FROM SFISM4.R109 
                    WHERE REASON_CODE = 'B001'
                ) s 
                    ON c.SERIAL_NUMBER = s.SERIAL_NUMBER 
                    AND c.FAIL_STATION = s.TEST_GROUP 
                    AND c.SYMPTOM = s.TEST_CODE
                    ORDER BY c.TIME) p
                    inner join sfis1.C_MODEL_DESC_T cmd
                    on p.MODEL_NAME = cmd.MODEL_NAME
                    WHERE cmd.MODEL_SERIAL = 'ADAPTER'";

            using (var command = new OracleCommand(query, connection))
            {
                // Bind parameters
                command.Parameters.Add("start_date", OracleDbType.Varchar2).Value = request.StartDate;
                command.Parameters.Add("end_date", OracleDbType.Varchar2).Value = request.EndDate;

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        result.Add(new BonepileResult
                        {
                            SN = reader["SERIAL_NUMBER"].ToString(),
                            MODEL_NAME = reader["MODEL_NAME"].ToString(),
                            PRODUCT_LINE = reader["PRODUCT_LINE"].ToString(),
                            MO_NUMBER = reader["MO_NUMBER"].ToString(),
                            FAIL_STATION = reader["FAIL_STATION"].ToString(),
                            SYMPTOM = reader["SYMPTOM"].ToString(),
                            ERROR_DESC = reader["ERROR_DESC"].ToString(),
                            TIME = Convert.ToDateTime(reader["TIME"]),
                            FLAG = reader["FLAG"].ToString(),
                            PO_NO = reader["PO_NO"].ToString(),
                            PO_ITEM = reader["PO_ITEM"].ToString(),
                            FAILURE_AGING = Convert.ToDouble(reader["FAILURE_AGING"]),
                            WIP_GROUP = reader["WIP_GROUP"].ToString(),
                            VERSION_CODE = reader["VERSION_CODE"].ToString(),
                            WORK_FLAG = reader["WORK_FLAG"].ToString(),
                            ERROR_FLAG = reader["ERROR_FLAG"].ToString(),
                            MO_NEW = reader["MO_NEW"].ToString(),
                            STATUS = reader["STATUS"].ToString(),
                            CHECKIN_REPAIR_TIME = reader["CHECKIN_REPAIR_TIME"] != DBNull.Value ? Convert.ToDateTime(reader["CHECKIN_REPAIR_TIME"]) : (DateTime?)null,
                            CHECKOUT_REPAIR_TIME = reader["CHECKOUT_REPAIR_TIME"] != DBNull.Value ? Convert.ToDateTime(reader["CHECKOUT_REPAIR_TIME"]) : (DateTime?)null,
                            SCRAP_STATUS = reader["SCRAP_STATUS"].ToString()
                        });
                    }
                }
            }
            return result;
        }


        /// <summary>
        /// Bonpile Before
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>

        [HttpGet("adapter-repair-records")]
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

        [HttpGet("adapter-repair-aging-count")]
        public async Task<IActionResult> AdapterRepairAgingCount()
        {
            try
            {
                var dataResult = await BuildAdapterRepairDataAsync();

                var agingGroups = dataResult.Records
                    .GroupBy(r =>
                    {
                        if (double.TryParse(r.AgingDay, NumberStyles.Any, CultureInfo.InvariantCulture, out double aging))
                        {
                            if (aging < 45) return "<45";
                            if (aging <= 89) return "45-89";
                            return ">=90";
                        }

                        return ">=90";
                    })
                    .Select(g => new { AgeRange = g.Key, Count = g.Count(), Records = g.ToList() })
                    .ToList();

                return Ok(new { totalCount = dataResult.TotalCount, agingCounts = agingGroups });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error occurred!", error = ex.Message });
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
                "Can'tRepairProcess","WaitingScrap","ReworkFG","RepairInRE","WaitingCheckOut","RepairInPD","B36V"
            };

            var records = new List<AdapterRepairRecordDto>();

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
                            _ => "ApprovedBGA"
                        };
                    }
                }
                else if (b.MO_NUMBER?.Trim().StartsWith("4") == true)
                {
                    status = "ReworkFG";
                }
                else if (b.ERROR_FLAG != "8" && (b.WIP_GROUP.Contains("B28M") || b.WIP_GROUP.Contains("B30M")))
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
                    Data11 = b.DATA11,
                    Status = status,
                    TestTime = b.TEST_TIME,
                    TestCode = b.TEST_CODE,
                    ErrorCodeItem = b.ERROR_ITEM_CODE,
                    TestGroup = b.TEST_GROUP,
                    ErrorDesc = b.ERROR_DESC,
                    Repair = b.REPAIR,
                    AgingDay = b.AGING_DAY,
                    CheckInDate = b.CHECKIN_DATE,
                    GroupTestOff = b.STATION_TEST,
                    TestResultOff = b.DATA2,
                    DetailTestOff = b.DATA5,
                    TimeTestOff = b.PASS_TIME
                };

                records.Add(record);
            }

            var statusCounts = records
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

            string query = @"
SELECT 
                    r107.SERIAL_NUMBER,
                    r107.MODEL_NAME,
                    model_desc.PRODUCT_LINE,
                    r107.MO_NUMBER,
                    r107.ERROR_FLAG,
                    r107.WORK_FLAG,
                    r107.WIP_GROUP,
                    r109_latest.TEST_GROUP,
                    r109_latest.TEST_TIME,
                    r109_latest.TEST_CODE,
                    r109_latest.ERROR_ITEM_CODE,
                    error_desc.ERROR_DESC,
                    repair_task.DATA11,
                    rep_detail.DATA19_COMBINED,
                    RE_TEST.GROUP_NAME AS STATION_TEST,
                    RE_TEST.DATA2,
                    RE_TEST.DATA5,
                    RE_TEST.PASS_TIME,
                    CHECK_IN.IN_DATETIME AS CHECKIN_DATE,
                    TRUNC(SYSDATE - CHECK_IN.IN_DATETIME) AS AGING_DAY
                    FROM sfism4.r107 r107
                JOIN sfis1.c_model_desc_t model_desc
                  ON r107.model_name = model_desc.model_name
                LEFT JOIN sfism4.r_repair_task_t repair_task
                  ON r107.SERIAL_NUMBER = repair_task.SERIAL_NUMBER
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
                  ON rep_detail.SERIAL_NUMBER = r107.SERIAL_NUMBER
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
                LEFT JOIN (
                    SELECT SERIAL_NUMBER, GROUP_NAME, PASS_TIME, DATA2, DATA5
                    FROM (
                        SELECT t.SERIAL_NUMBER, t.GROUP_NAME, t.PASS_TIME, t.DATA2, t.DATA5,
                            ROW_NUMBER() OVER (PARTITION BY SERIAL_NUMBER ORDER BY PASS_TIME DESC) rn
                        FROM SFISM4.R_ULT_RESULT_T t
                        WHERE GROUP_NAME LIKE '%_OFF%' AND MODEL_NAME IN (SELECT model_name FROM sfis1.c_model_desc_t WHERE model_serial = 'ADAPTER')
                    )
                    WHERE rn = 1
                ) RE_TEST
                  ON RE_TEST.SERIAL_NUMBER = r107.SERIAL_NUMBER
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
                            TEST_GROUP = reader["TEST_GROUP"] != DBNull.Value ? reader["TEST_GROUP"].ToString() : null,
                            TEST_TIME = reader["TEST_TIME"].ToString(),
                            TEST_CODE = reader["TEST_CODE"].ToString(),
                            ERROR_ITEM_CODE = reader["ERROR_ITEM_CODE"].ToString(),
                            ERROR_DESC = reader["ERROR_DESC"].ToString(),
                            DATA11 = reader["DATA11"] != DBNull.Value ? reader["DATA11"].ToString() : null,
                            REPAIR = reader["DATA19_COMBINED"] != DBNull.Value ? reader["DATA19_COMBINED"].ToString() : null,
                            CHECKIN_DATE = reader["CHECKIN_DATE"] != DBNull.Value ? Convert.ToDateTime(reader["CHECKIN_DATE"]) : (DateTime?)null,
                            AGING_DAY = reader["AGING_DAY"] != DBNull.Value ? reader["AGING_DAY"].ToString() : null,
                            STATION_TEST = reader["STATION_TEST"].ToString(),
                            DATA2 = reader["DATA2"].ToString(),
                            DATA5 = reader["DATA5"].ToString(),
                            PASS_TIME = reader["PASS_TIME"].ToString()
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

            string query = @"
SELECT 
                        CASE 
                            WHEN REGEXP_LIKE(r107.MODEL_NAME, '^(900|692|930)') THEN kr.KEY_PART_SN
                            ELSE r107.SERIAL_NUMBER
                        END AS SERIAL_NUMBER,
                        r107.MODEL_NAME,
                        model_desc.PRODUCT_LINE,
                        r107.MO_NUMBER,
                        r107.ERROR_FLAG,
                        r107.WORK_FLAG,
                        r107.WIP_GROUP,
                        r109_latest.TEST_GROUP,
                        r109_latest.TEST_TIME,
                        r109_latest.TEST_CODE,
                        r109_latest.ERROR_ITEM_CODE,
                        error_desc.ERROR_DESC,
                        repair_task.DATA11,
                        rt19.DATA19_COMBINED,
                        RE_TEST.GROUP_NAME AS STATION_TEST,
                        RE_TEST.DATA2 AS DATA2,
                        RE_TEST.DATA5 AS DATA5,
                        RE_TEST.PASS_TIME,
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
                    LEFT JOIN sfism4.r_repair_task_t repair_task
                      ON r107.SERIAL_NUMBER = repair_task.SERIAL_NUMBER
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
                ) rt19
                  ON rt19.SERIAL_NUMBER = r107.SERIAL_NUMBER
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
                    LEFT JOIN(                    
                        SELECT SERIAL_NUMBER,
                               GROUP_NAME,
                               PASS_TIME,
                               DATA2,
                               PASS_DATE,
                               DATA5
                        FROM (
                            SELECT t.*,
                                   ROW_NUMBER() OVER (
                                       PARTITION BY SERIAL_NUMBER
                                       ORDER BY PASS_TIME DESC
                                   ) AS rn
                            FROM SFISM4.R_ULT_RESULT_T t
                            WHERE GROUP_NAME LIKE '%_OFF%'
                        )
                        WHERE rn = 1
                    ) RE_TEST
                      ON RE_TEST.SERIAL_NUMBER = r107.SERIAL_NUMBER
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
                    TEST_GROUP = reader["TEST_GROUP"] != DBNull.Value ? reader["TEST_GROUP"].ToString() : null,
                    TEST_TIME = reader["TEST_TIME"]?.ToString(),
                    TEST_CODE = reader["TEST_CODE"]?.ToString(),
                    ERROR_ITEM_CODE = reader["ERROR_ITEM_CODE"]?.ToString(),
                    ERROR_DESC = reader["ERROR_DESC"]?.ToString(),
                    DATA11 = reader["DATA11"] != DBNull.Value ? reader["DATA11"].ToString() : null,
                    REPAIR = reader["DATA19_COMBINED"] != DBNull.Value ? reader["DATA19_COMBINED"].ToString() : null,
                    CHECKIN_DATE = reader["CHECKIN_DATE"] != DBNull.Value ? Convert.ToDateTime(reader["CHECKIN_DATE"]) : (DateTime?)null,
                    AGING_DAY = reader["AGING_DAY"] != DBNull.Value ? reader["AGING_DAY"].ToString() : null,
                    STATION_TEST = reader["STATION_TEST"].ToString(),
                    DATA2 = reader["DATA2"].ToString(),
                    DATA5 = reader["DATA5"].ToString(),
                    PASS_TIME = reader["PASS_TIME"].ToString()
                });
            }

            return result;
        }


        /// <summary>
        /// Bonepile After
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        ///

        [HttpGet("bonepile-after-kanban-basic")]
        public async Task<IActionResult> BonepileAfterKanbanBasic()
        {
            try
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

                var khoOkSet = (await _sqlContext.KhoOks
                        .Where(k => snList.Contains(k.SERIAL_NUMBER.Trim().ToUpper()))
                        .Select(k => k.SERIAL_NUMBER)
                        .ToListAsync())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var scrapDict = scrapCategories.ToDictionary(
                    c => c.SN?.Trim().ToUpper() ?? "",
                    c => (ApplyTaskStatus: c.ApplyTaskStatus, TaskNumber: c.TaskNumber),
                    StringComparer.OrdinalIgnoreCase
                );

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
                    "Can'tRepairProcess"
                };

                var databaseRecords = new List<BonepileAfterKanbanBasicRecord>();

                foreach (var b in allData)
                {
                    if (string.IsNullOrWhiteSpace(b.SERIAL_NUMBER))
                    {
                        continue;
                    }

                    var snKey = b.SERIAL_NUMBER.Trim().ToUpper();
                    string status;
                    string statusV2;

                    if (scrapDict.TryGetValue(snKey, out var scrapInfo))
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
                        else if (applyTaskStatus == 4)
                        {
                            status = "WaitingApprovalBGA";
                            statusV2 = DetermineStatusV2(b.TEST_CODE);
                        }
                        else if (applyTaskStatus == 8)
                        {
                            status = "Can'tRepairProcess";
                            statusV2 = DetermineStatusV2(b.TEST_CODE);
                        }
                        else if (applyTaskStatus == 19)
                        {
                            status = "ApprovedBGA";
                            statusV2 = "Repaired";
                        }
                        else
                        {
                            status = "ApprovedBGA";
                            statusV2 = DetermineStatusV2(b.TEST_CODE);
                        }
                    }
                    else if (exportDict.TryGetValue(snKey, out var exportInfo))
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
                                statusV2 = DetermineStatusV2(b.TEST_CODE);
                                break;
                        }
                    }
                    else
                    {
                        status = "RepairInRE";
                        statusV2 = DetermineStatusV2(b.TEST_CODE);
                    }

                    var isInKhoOk = khoOkSet.Contains(snKey);
                    if (string.Equals(status, "RepairInRE", StringComparison.OrdinalIgnoreCase) && isInKhoOk)
                    {
                        status = "WaitingCheckOut";
                    }

                    if (isInKhoOk)
                    {
                        statusV2 = "Repaired";
                    }

                    if (!validStatuses.Contains(status))
                    {
                        continue;
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
                        Status = status,
                        StatusV2 = statusV2
                    });
                }

                //var excelRecords = LoadBonepileAfterKanbanExcelData();

                string NormalizeSerial(string? serialNumber)
                {
                    return string.IsNullOrWhiteSpace(serialNumber)
                        ? string.Empty
                        : serialNumber.Trim();
                }

                //if (excelRecords.Count > 0)
                //{
                //    var excelSerials = new HashSet<string>(
                //        excelRecords
                //            .Select(r => NormalizeSerial(r.SN))
                //            .Where(sn => !string.IsNullOrEmpty(sn)),
                //        StringComparer.OrdinalIgnoreCase);

                //    if (excelSerials.Count > 0)
                //    {
                //        databaseRecords = databaseRecords
                //            .Where(r => !excelSerials.Contains(NormalizeSerial(r.SN)))
                //            .ToList();
                //    }
                //}

                //var combinedRecords = new List<BonepileAfterKanbanBasicRecord>(databaseRecords.Count + excelRecords.Count);
                var combinedRecords = new List<BonepileAfterKanbanBasicRecord>(databaseRecords.Count);
                combinedRecords.AddRange(databaseRecords);
                //combinedRecords.AddRange(excelRecords);

                var statusCounts = combinedRecords
                    .Where(r => !string.IsNullOrWhiteSpace(r.Status))
                    .GroupBy(r => r.Status, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new
                    {
                        Status = g.Key,
                        Count = g.Count()
                    })
                    .ToList();

                var statusCounts_v2 = combinedRecords
                    .Where(r => !string.IsNullOrWhiteSpace(r.StatusV2))
                    .GroupBy(r => r.StatusV2, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new
                    {
                        Status_v2 = g.Key,
                        Count = g.Count()
                    })
                    .ToList();

                Console.WriteLine($"Result Count (including Excel): {combinedRecords.Count}");

                if (!combinedRecords.Any())
                {
                    return NotFound(new { message = "Khong tim thay du lieu!", totalCount = 0 });
                }

                var statusDetails = combinedRecords
                    .Where(r => !string.IsNullOrWhiteSpace(r.Status))
                    .GroupBy(r => r.Status, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

                var statusDetails_v2 = combinedRecords
                    .Where(r => !string.IsNullOrWhiteSpace(r.StatusV2))
                    .GroupBy(r => r.StatusV2, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

                return Ok(new
                {
                    totalCount = combinedRecords.Count,
                    data = combinedRecords,
                    statusCounts,
                    statusCounts_v2,
                    statusDetails,
                    statusDetails_v2
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Xay ra loi", error = ex.Message });
            }
        }
        [HttpGet("bonepile-after-kanban-aging-count")]
        public async Task<IActionResult> BonepileAfterKanbanAgingCount()
        {
            try
            {
                var basicData = await ExecuteBonepileAfterKanbanBasicQuery();

                var excludedSNs = GetExcludedSerialNumbers();
                if (excludedSNs.Any())
                {
                    basicData = basicData.Where(d => !excludedSNs.Contains(d.SERIAL_NUMBER?.Trim().ToUpper())).ToList();
                }

                var snList = basicData
                    .Select(d => d.SERIAL_NUMBER?.Trim().ToUpper())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();

                var invalidSNs = await _sqlContext.ScrapLists
                    .Where(s => snList.Contains(s.SN.Trim().ToUpper()) &&
                                (s.ApplyTaskStatus == 0 || s.ApplyTaskStatus == 1 || s.ApplyTaskStatus == 5 || s.ApplyTaskStatus == 6 || s.ApplyTaskStatus == 7))
                    .Select(s => s.SN.Trim().ToUpper())
                    .ToListAsync();

                if (invalidSNs.Any())
                {
                    basicData = basicData
                        .Where(d => !invalidSNs.Contains(d.SERIAL_NUMBER?.Trim().ToUpper()))
                        .ToList();
                }

                var records = basicData
                    .Select(b => new
                    {
                        SN = b.SERIAL_NUMBER,
                        ModelName = b.MODEL_NAME,
                        MoNumber = b.MO_NUMBER,
                        ProductLine = b.PRODUCT_LINE,
                        WipGroupSFC = b.WIP_GROUP_SFC,
                        WipGroupKANBAN = b.WIP_GROUP_KANBAN,
                        testTime = b.TEST_TIME,
                        testCode = b.TEST_CODE,
                        errorDesc = b.ERROR_DESC,
                        errorCodeItem = b.ERROR_ITEM_CODE,
                        testGroup = b.TEST_GROUP,
                        errorFlag = b.ERROR_FLAG,
                        aging = b.AGING
                    })
                    .ToList();

                var agingGroups = records
                    .GroupBy(r =>
                    {
                        if (r.aging.HasValue)
                        {
                            var aging = r.aging.Value;
                            if (aging < 45) return "<45";
                            if (aging < 90) return "45-89";
                            return ">=90";
                        }
                        return ">=90";
                    })
                    .Select(g => new
                    {
                        AgeRange = g.Key,
                        Count = g.Count(),
                        Records = g.ToList()
                    })
                    .ToList();

                var productLineAgingGroups = records
                    .GroupBy(r => r.ProductLine)
                    .Select(pg => new
                    {
                        ProductLine = pg.Key,
                        AgingCounts = pg
                            .GroupBy(r =>
                            {
                                if (r.aging.HasValue)
                                {
                                    var aging = r.aging.Value;
                                    if (aging < 45) return "<45";
                                    if (aging < 90) return "45-89";
                                    return ">=90";
                                }
                                return ">=90";
                            })
                            .Select(g => new
                            {
                                AgeRange = g.Key,
                                Count = g.Count(),
                                Records = g.ToList()
                            })
                            .ToList()
                    })
                    .ToList();

                return Ok(new
                {
                    totalCount = records.Count,
                    agingCounts = agingGroups,
                    agingCountsByProductLine = productLineAgingGroups
                });
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
                    //DATA19_COMBINED = reader["DATA19_COMBINED"]?.ToString(),
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

        private List<BonepileAfterKanbanBasicRecord> LoadBonepileAfterKanbanExcelData()
        {
            var records = new List<BonepileAfterKanbanBasicRecord>();

            try
            {
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "Uploads");
                var filePath = Path.Combine(uploadsFolder, "b36v.xlsx");

                if (!System.IO.File.Exists(filePath))
                {
                    return records;
                }

                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                using var package = new ExcelPackage(new FileInfo(filePath));
                var worksheet = package.Workbook.Worksheets.FirstOrDefault();

                if (worksheet?.Dimension == null)
                {
                    return records;
                }

                var headerRow = worksheet.Dimension.Start.Row;
                var startRow = headerRow + 1;
                var endRow = worksheet.Dimension.End.Row;

                var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int col = worksheet.Dimension.Start.Column; col <= worksheet.Dimension.End.Column; col++)
                {
                    var headerText = worksheet.Cells[headerRow, col].Text?.Trim();
                    if (!string.IsNullOrEmpty(headerText) && !headers.ContainsKey(headerText))
                    {
                        headers[headerText] = col;
                    }
                }

                string? ReadString(int rowIndex, params string[] keys)
                {
                    foreach (var key in keys)
                    {
                        if (headers.TryGetValue(key, out var columnIndex))
                        {
                            var text = worksheet.Cells[rowIndex, columnIndex].Text?.Trim();
                            if (!string.IsNullOrEmpty(text))
                            {
                                return text;
                            }
                        }
                    }

                    return null;
                }

                string? ReadDateTimeString(int rowIndex, params string[] keys)
                {
                    foreach (var key in keys)
                    {
                        if (headers.TryGetValue(key, out var columnIndex))
                        {
                            var cell = worksheet.Cells[rowIndex, columnIndex];
                            if (cell?.Value == null)
                            {
                                continue;
                            }

                            if (cell.Value is DateTime dateTime)
                            {
                                return dateTime.ToString("yyyy-MM-dd HH:mm:ss");
                            }

                            var text = cell.Text?.Trim();
                            if (string.IsNullOrEmpty(text))
                            {
                                continue;
                            }

                            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                            {
                                return parsedDate.ToString("yyyy-MM-dd HH:mm:ss");
                            }

                            return text;
                        }
                    }

                    return null;
                }

                double? ReadDouble(int rowIndex, params string[] keys)
                {
                    foreach (var key in keys)
                    {
                        if (headers.TryGetValue(key, out var columnIndex))
                        {
                            var cell = worksheet.Cells[rowIndex, columnIndex];
                            if (cell?.Value == null)
                            {
                                continue;
                            }

                            switch (cell.Value)
                            {
                                case double doubleValue:
                                    return doubleValue;
                                case decimal decimalValue:
                                    return (double)decimalValue;
                                case int intValue:
                                    return intValue;
                                case long longValue:
                                    return longValue;
                            }

                            var text = cell.Text?.Trim();
                            if (string.IsNullOrEmpty(text))
                            {
                                continue;
                            }

                            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedDouble))
                            {
                                return parsedDouble;
                            }
                        }
                    }

                    return null;
                }

                for (int row = startRow; row <= endRow; row++)
                {
                    var sn = ReadString(row, "SN", "SerialNumber", "SERIAL_NUMBER");
                    if (string.IsNullOrWhiteSpace(sn))
                    {
                        continue;
                    }

                    var record = new BonepileAfterKanbanBasicRecord
                    {
                        SN = sn,
                        ModelName = ReadString(row, "ModelName", "MODEL_NAME"),
                        MoNumber = ReadString(row, "MoNumber", "MO_NUMBER"),
                        ProductLine = ReadString(row, "ProductLine", "PRODUCT_LINE"),
                        WipGroupSFC = ReadString(row, "WipGroupSFC", "WIP_GROUP_SFC"),
                        WipGroupKANBAN = ReadString(row, "WipGroupKANBAN", "WIP_GROUP_KANBAN"),
                        ErrorFlag = ReadString(row, "ErrorFlag", "ERROR_FLAG"),
                        WorkFlag = ReadString(row, "WorkFlag", "WORK_FLAG"),
                        TestGroup = ReadString(row, "TestGroup", "TEST_GROUP"),
                        TestTime = ReadDateTimeString(row, "TestTime", "TEST_TIME"),
                        TestCode = ReadString(row, "TestCode", "TEST_CODE"),
                        ErrorCodeItem = ReadString(row, "ErrorCodeItem", "ERROR_ITEM_CODE"),
                        ErrorDesc = ReadString(row, "ErrorDesc", "ERROR_DESC"),
                        Status = ReadString(row, "Status"),
                        StatusV2 = ReadString(row, "StatusV2", "STATUS_V2", "Status_V2"),
                        Aging = ReadDouble(row, "Aging", "AGING")
                    };

                    if (string.IsNullOrWhiteSpace(record.Status) && !string.IsNullOrWhiteSpace(record.StatusV2))
                    {
                        record.Status = record.StatusV2;
                    }
                    else if (string.IsNullOrWhiteSpace(record.StatusV2) && !string.IsNullOrWhiteSpace(record.Status))
                    {
                        record.StatusV2 = record.Status;
                    }

                    if (!string.IsNullOrWhiteSpace(record.Status))
                    {
                        record.Status = record.Status.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(record.StatusV2))
                    {
                        record.StatusV2 = record.StatusV2.Trim();
                    }

                    records.Add(record);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading b36v.xlsx: {ex.Message}");
            }

            return records;
        }

        private static string DetermineStatusV2(string testCode)
        {
            if (string.IsNullOrEmpty(testCode))
            {
                return "UnderFA";
            }

            var normalizedTestCode = testCode.Trim();

            return (normalizedTestCode.Length <= 6 && normalizedTestCode != "11098" && normalizedTestCode != "4-NA-1") || normalizedTestCode.Contains("BURNMARK")
                ? "ProcessIssue"
                : "UnderFA";
        }

        [HttpGet("adapter-mo-records")]
        public async Task<IActionResult> AdapterMoRecords([FromQuery] StatusRequestBonepile request)
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

                var allData = await ExecuteAdapterMoQuery();

                var scrapCategories = await _sqlContext.ScrapLists
                    .Select(s => new { SN = s.SN, ApplyTaskStatus = s.ApplyTaskStatus, TaskNumber = s.TaskNumber })
                    .ToListAsync();

                var scrapDict = scrapCategories.ToDictionary(
                    c => c.SN?.Trim().ToUpper() ?? "",
                    c => (ApplyTaskStatus: c.ApplyTaskStatus, TaskNumber: c.TaskNumber),
                    StringComparer.OrdinalIgnoreCase
                );

                var validStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "ScrapLacksTask",
                    "ScrapHasScrap",
                    "ApprovedBGA",
                    "WaitingApprovalBGA",
                    "WaitingApproveScrap",
                    "WAITING_LINK",
                    "LINKED"
                };

                var result = allData
                    .Select(b =>
                    {
                        var sn = b.SERIAL_NUMBER?.Trim().ToUpper() ?? "";
                        string status;

                        if (scrapDict.TryGetValue(sn, out var scrapInfo))
                        {
                            var applyTaskStatus = scrapInfo.ApplyTaskStatus;
                            var taskNumber = scrapInfo.TaskNumber;

                            if (applyTaskStatus == 5 || applyTaskStatus == 6 || applyTaskStatus == 7)
                                status = "ScrapHasTask";
                            else if (applyTaskStatus == 0 || applyTaskStatus == 1)
                            {
                                if (string.IsNullOrEmpty(taskNumber) || taskNumber == "N/A")
                                    status = "ScrapLacksTask";
                                else status = "ScrapHasTask";
                            }
                            else
                            {
                                status = applyTaskStatus switch
                                {
                                    2 => "WaitingApproveScrap",
                                    4 => "WaitingApprovalBGA",
                                    _ => "ApprovedBGA"
                                };
                            }
                        }
                        else
                        {
                            status = b.MO_STATUS;
                        }

                        return new
                        {
                            SN = b.SERIAL_NUMBER,
                            modelName = b.MODEL_NAME,
                            moNumber = b.MO_NUMBER,
                            ProductLine = b.PRODUCT_LINE,
                            wipGroup = b.WIP_GROUP,
                            groupName = b.GROUP_NAME,
                            errorFlag = b.ERROR_FLAG,
                            stationTime = b.STATION_TIME,
                            Status = status
                        };
                    })
                    .Where(r => validStatuses.Contains(r.Status ?? string.Empty) &&
                                (!filterByStatus || (statusFilter?.Contains(r.Status ?? string.Empty) ?? false)))
                    .ToList();

                Console.WriteLine($"Result Count: {result?.Count}");

                if (!result.Any())
                {
                    return NotFound(new { message = "Khong tim thay du lieu!", count = 0 });
                }

                return Ok(new
                {
                    count = result?.Count,
                    data = result
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Xay ra loi", error = ex.Message });
            }
        }

        [HttpGet("adapter-mo-status-count")]
        public async Task<IActionResult> AdapterMoStatusCount()
        {
            try
            {
                var moData = await ExecuteAdapterMoQuery();
                var scrapCategories = await _sqlContext.ScrapLists
                    .Select(s => new { SN = s.SN, ApplyTaskStatus = s.ApplyTaskStatus, TaskNumber = s.TaskNumber })
                    .ToListAsync();

                var scrapDict = scrapCategories.ToDictionary(
                    c => c.SN?.Trim().ToUpper() ?? "",
                    c => (ApplyTaskStatus: c.ApplyTaskStatus, TaskNumber: c.TaskNumber),
                    StringComparer.OrdinalIgnoreCase
                );

                var result = moData
                    .Select(b =>
                    {
                        var sn = b.SERIAL_NUMBER?.Trim().ToUpper() ?? "";
                        string status;

                        if (scrapDict.TryGetValue(sn, out var scrapInfo))
                        {
                            var applyTaskStatus = scrapInfo.ApplyTaskStatus;
                            var taskNumber = scrapInfo.TaskNumber;

                            if (applyTaskStatus == 5 || applyTaskStatus == 6 || applyTaskStatus == 7)
                                status = "ScrapHasTask";
                            else if (applyTaskStatus == 0 || applyTaskStatus == 1)
                            {
                                if (string.IsNullOrEmpty(taskNumber) || taskNumber == "N/A")
                                    status = "ScrapLacksTask";
                                else status = "ScrapHasTask";
                            }
                            else
                            {
                                status = applyTaskStatus switch
                                {
                                    2 => "WaitingApproveScrap",
                                    4 => "WaitingApprovalBGA",
                                    _ => "ApprovedBGA"
                                };
                            }
                        }
                        else
                        {
                            status = b.MO_STATUS;
                        }

                        return status;
                    })
                    .ToList();

                var statusCounts = result
                    .GroupBy(status => status)
                    .Select(g => new
                    {
                        Status = g.Key,
                        Count = g.Count()
                    })
                    .ToList();

                return Ok(new
                {
                    totalCount = result.Count,
                    statusCounts = statusCounts
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Xay ra loi", error = ex.Message });
            }
        }

        private async Task<List<MoTaskResult>> ExecuteAdapterMoQuery()
        {
            var result = new List<MoTaskResult>();

            await using var connection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);
            await connection.OpenAsync();

            string query = @"
               SELECT 
                r107.SERIAL_NUMBER,
                r107.MO_NUMBER,
                r107.MODEL_NAME,
                model_desc.PRODUCT_LINE,
                r107.WIP_GROUP,
                r107.ERROR_FLAG,
                R107.IN_STATION_TIME,
                R107.GROUP_NAME,
                'WAITING_LINK' AS MO_STATUS
            FROM 
                SFISM4.R107 r107
            INNER JOIN SFIS1.C_MODEL_DESC_T model_desc
                ON r107.MODEL_NAME = model_desc.MODEL_NAME
            WHERE 
                r107.WIP_GROUP LIKE '%B31M%'
                AND model_desc.MODEL_SERIAL = 'ADAPTER'
                AND r107.MODEL_NAME NOT LIKE '900%'
                AND r107.MODEL_NAME NOT LIKE '692%'

            UNION ALL

            SELECT 
                c.SERIAL_NUMBER,
                c.MO_NUMBER,
                c.MODEL_NAME,
                b.PRODUCT_LINE,
                c.WIP_GROUP,
                c.ERROR_FLAG,
                latest.IN_STATION_TIME,
                latest.GROUP_NAME,
                'LINKED' AS MO_STATUS
            FROM (
                SELECT 
                    r117.SERIAL_NUMBER,
                    r117.MODEL_NAME,
                    r117.MO_NUMBER,
                    r117.WIP_GROUP,
                    r117.GROUP_NAME,
                    r117.IN_STATION_TIME,
                    ROW_NUMBER() OVER (
                        PARTITION BY r117.SERIAL_NUMBER
                        ORDER BY r117.IN_STATION_TIME DESC
                    ) AS rn
                FROM SFISM4.R117 r117
                WHERE r117.WIP_GROUP LIKE '%B31M%'
            ) latest
            INNER JOIN sfis1.c_model_desc_t b
                ON latest.MODEL_NAME = b.MODEL_NAME
            INNER JOIN sfism4.R107 c
                ON c.SERIAL_NUMBER = latest.SERIAL_NUMBER
            INNER JOIN sfism4.R105 d
                ON d.MO_NUMBER = latest.MO_NUMBER
            LEFT JOIN sfism4.Z_KANBAN_TRACKING_T z
                ON z.SERIAL_NUMBER = latest.SERIAL_NUMBER
            WHERE 
                latest.rn = 1
                AND b.MODEL_SERIAL = 'ADAPTER'
                AND c.MO_NUMBER LIKE '3%' 
                AND c.ERROR_FLAG IN ('0','1')
                AND d.CLOSE_FLAG = 2
                AND z.SERIAL_NUMBER IS NULL
                AND NOT REGEXP_LIKE(c.WIP_GROUP, 'B28M|B30M|B31M|BR2C|B34G|B36R|STOCKIN')";

            using (var command = new OracleCommand(query, connection))
            {
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        result.Add(new MoTaskResult
                        {
                            SERIAL_NUMBER = reader["SERIAL_NUMBER"].ToString(),
                            MODEL_NAME = reader["MODEL_NAME"].ToString(),
                            PRODUCT_LINE = reader["PRODUCT_LINE"].ToString(),
                            WIP_GROUP = reader["WIP_GROUP"].ToString(),
                            MO_NUMBER = reader["MO_NUMBER"].ToString(),
                            GROUP_NAME = reader["GROUP_NAME"].ToString(),
                            ERROR_FLAG = reader["ERROR_FLAG"].ToString(),
                            STATION_TIME = reader["IN_STATION_TIME"] != DBNull.Value ? Convert.ToDateTime(reader["IN_STATION_TIME"]) : (DateTime?)null,
                            MO_STATUS = reader["MO_STATUS"].ToString()
                        });
                    }
                }
            }

            return result;
        }

        [HttpGet("bonepile-npi")]
        public async Task<IActionResult> BonepileNpi()
        {
            try
            {
                var beforeData = await ExecuteBonepileNpiBeforeQuery();
                var afterData = await ExecuteBonepileNpiAfterQuery();

                if (!beforeData.Any() && !afterData.Any())
                {
                    return NotFound(new { message = "Khong tim thay du lieu!", totalCount = 0 });
                }

                var productLines = beforeData
                    .Select(b => b.ProductLine)
                    .Concat(afterData.Select(a => a.ProductLine))
                    .Where(pl => !string.IsNullOrWhiteSpace(pl))
                    .Distinct(StringComparer.OrdinalIgnoreCase);

                var groupedResult = productLines
                    .Select(pl =>
                    {
                        var beforeList = beforeData
                            .Where(b => string.Equals(b.ProductLine, pl, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        var afterList = afterData
                            .Where(a => string.Equals(a.ProductLine, pl, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        return new BonepileNPIProductLineResponse
                        {
                            ProductLine = pl,
                            BeforeCount = beforeList.Count,
                            AfterCount = afterList.Count,
                            BeforeDetails = beforeList,
                            AfterDetails = afterList
                        };
                    })
                    .ToList();

                return Ok(new
                {
                    totalBefore = beforeData.Count,
                    totalAfter = afterData.Count,
                    data = groupedResult
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Xay ra loi", error = ex.Message });
            }
        }

        private async Task<List<BonepileNPISerialBefore>> ExecuteBonepileNpiBeforeQuery()
        {
            var result = new List<BonepileNPISerialBefore>();

            await using var connection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);
            await connection.OpenAsync();

            const string query = @"
SELECT 
    r107.SERIAL_NUMBER,
    r107.MODEL_NAME,
    model_desc.PRODUCT_LINE,
    r107.MO_NUMBER,
    r107.ERROR_FLAG,
    r107.WORK_FLAG,
    r107.WIP_GROUP,
    r109_latest.TEST_GROUP,
    r109_latest.TEST_TIME,
    r109_latest.TEST_CODE,
    r109_latest.ERROR_ITEM_CODE,
    error_desc.ERROR_DESC,
    repair_task.DATA11,
    rep_detail.DATA19_COMBINED,
    RE_TEST.GROUP_NAME AS STATION_TEST,
    RE_TEST.DATA2,
    RE_TEST.DATA5,
    RE_TEST.PASS_TIME,
    CHECK_IN.IN_DATETIME AS CHECKIN_DATE,
    TRUNC(SYSDATE - CHECK_IN.IN_DATETIME) AS AGING_DAY
FROM sfism4.r107 r107
JOIN sfis1.c_model_desc_t model_desc
  ON r107.model_name = model_desc.model_name
LEFT JOIN sfism4.r_repair_task_t repair_task
  ON r107.SERIAL_NUMBER = repair_task.SERIAL_NUMBER
LEFT JOIN (
    SELECT SERIAL_NUMBER, MAX(IN_DATETIME) AS IN_DATETIME
    FROM SFISM4.R_REPAIR_IN_OUT_T 
    WHERE MODEL_NAME IN (SELECT model_name FROM sfis1.c_model_desc_t WHERE model_serial = 'ADAPTER')
      AND MODEL_NAME NOT LIKE '900%' 
      AND MODEL_NAME NOT LIKE '930%' 
      AND MODEL_NAME NOT LIKE '692%'
    GROUP BY SERIAL_NUMBER
) CHECK_IN
  ON CHECK_IN.SERIAL_NUMBER = r107.SERIAL_NUMBER
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
  ON rep_detail.SERIAL_NUMBER = r107.SERIAL_NUMBER
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
          AND MODEL_NAME NOT LIKE '900%' 
          AND MODEL_NAME NOT LIKE '930%' 
          AND MODEL_NAME NOT LIKE '692%'
    )
    WHERE rn = 1
) r109_latest
  ON r109_latest.SERIAL_NUMBER = r107.SERIAL_NUMBER
LEFT JOIN (
    SELECT SERIAL_NUMBER, GROUP_NAME, PASS_TIME, DATA2, DATA5
    FROM (
        SELECT t.SERIAL_NUMBER, t.GROUP_NAME, t.PASS_TIME, t.DATA2, t.DATA5,
            ROW_NUMBER() OVER (PARTITION BY SERIAL_NUMBER ORDER BY PASS_TIME DESC) rn
        FROM SFISM4.R_ULT_RESULT_T t
        WHERE GROUP_NAME LIKE '%_OFF%' 
          AND MODEL_NAME IN (SELECT model_name FROM sfis1.c_model_desc_t WHERE model_serial = 'ADAPTER')
    )
    WHERE rn = 1
) RE_TEST
  ON RE_TEST.SERIAL_NUMBER = r107.SERIAL_NUMBER
INNER JOIN sfis1.C_ERROR_CODE_T error_desc
  ON r109_latest.TEST_CODE = error_desc.ERROR_CODE
LEFT JOIN SFISM4.Z_KANBAN_TRACKING_T z
  ON z.SERIAL_NUMBER = r107.SERIAL_NUMBER 
WHERE 
    z.SERIAL_NUMBER IS NULL
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
    AND (
        r107.MO_NUMBER LIKE '5%'
        OR (
            r107.MO_NUMBER LIKE '3%'
            AND EXISTS (
                SELECT 1 
                FROM sfism4.r117 r117
                WHERE r117.SERIAL_NUMBER = r107.SERIAL_NUMBER
                  AND r117.MO_NUMBER LIKE '5%'
            )
        )
    )";

            using var command = new OracleCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                result.Add(new BonepileNPISerialBefore
                {
                    SerialNumber = reader["SERIAL_NUMBER"].ToString(),
                    ModelName = reader["MODEL_NAME"].ToString(),
                    ProductLine = reader["PRODUCT_LINE"].ToString(),
                    MoNumber = reader["MO_NUMBER"].ToString(),
                    ErrorFlag = reader["ERROR_FLAG"] != DBNull.Value ? reader["ERROR_FLAG"].ToString() : null,
                    WorkFlag = reader["WORK_FLAG"] != DBNull.Value ? reader["WORK_FLAG"].ToString() : null,
                    WipGroup = reader["WIP_GROUP"] != DBNull.Value ? reader["WIP_GROUP"].ToString() : null,
                    TestGroup = reader["TEST_GROUP"] != DBNull.Value ? reader["TEST_GROUP"].ToString() : null,
                    TestTime = reader["TEST_TIME"] != DBNull.Value ? Convert.ToDateTime(reader["TEST_TIME"]) : (DateTime?)null,
                    TestCode = reader["TEST_CODE"]?.ToString(),
                    ErrorItemCode = reader["ERROR_ITEM_CODE"]?.ToString(),
                    ErrorDesc = reader["ERROR_DESC"]?.ToString(),
                    Data11 = reader["DATA11"] != DBNull.Value ? reader["DATA11"].ToString() : null,
                    Repair = reader["DATA19_COMBINED"] != DBNull.Value ? reader["DATA19_COMBINED"].ToString() : null,
                    StationTest = reader["STATION_TEST"] != DBNull.Value ? reader["STATION_TEST"].ToString() : null,
                    Data2 = reader["DATA2"] != DBNull.Value ? reader["DATA2"].ToString() : null,
                    Data5 = reader["DATA5"] != DBNull.Value ? reader["DATA5"].ToString() : null,
                    PassTime = reader["PASS_TIME"] != DBNull.Value ? Convert.ToDateTime(reader["PASS_TIME"]) : (DateTime?)null,
                    CheckinDate = reader["CHECKIN_DATE"] != DBNull.Value ? Convert.ToDateTime(reader["CHECKIN_DATE"]) : (DateTime?)null,
                    AgingDay = reader["AGING_DAY"] != DBNull.Value ? reader["AGING_DAY"].ToString() : null
                });
            }

            return result;
        }

        private async Task<List<BonepileNPISerialAfter>> ExecuteBonepileNpiAfterQuery()
        {
            var result = new List<BonepileNPISerialAfter>();

            await using var connection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);
            await connection.OpenAsync();

            const string query = @"
SELECT /*+ LEADING(A) USE_NL(A B R107 KP R109X R109_OLD) */
      A.SERIAL_NUMBER,
      KP.PARENT_SN AS FG,
      R107.MO_NUMBER,
      A.MODEL_NAME,
      B.PRODUCT_LINE,
      A.WIP_GROUP AS WIP_GROUP_KANBAN,
      R107.WIP_GROUP,
      R107.ERROR_FLAG,
      R107.WORK_FLAG,
      rep_detail.DATA19_COMBINED,
      R109X.TEST_GROUP,
      R109X.TEST_TIME,
      R109X.TEST_CODE,
      R109X.ERROR_ITEM_CODE,
      E.ERROR_DESC,

      TRUNC(SYSDATE) - TRUNC(R109X.TEST_TIME) AS AGING,
      TRUNC(SYSDATE) - TRUNC(R109_OLD.TEST_TIME) AS AGING_OLDEST

FROM SFISM4.Z_KANBAN_TRACKING_T A
JOIN SFIS1.C_MODEL_DESC_T B 
    ON A.MODEL_NAME = B.MODEL_NAME
JOIN SFISM4.R107 R107 
    ON R107.SERIAL_NUMBER = A.SERIAL_NUMBER

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
    JOIN SFISM4.R109 r ON r.SERIAL_NUMBER = base.CAND_SN
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
    JOIN SFISM4.R109 r ON r.SERIAL_NUMBER = base.CAND_SN
    WHERE r.TEST_TIME IS NOT NULL
  )
  WHERE rn = 1
) R109_OLD ON R109_OLD.SN = A.SERIAL_NUMBER

LEFT JOIN SFIS1.C_ERROR_CODE_T E 
    ON E.ERROR_CODE = R109X.TEST_CODE

WHERE
    A.WIP_GROUP LIKE '%B36R%'
    AND B.MODEL_SERIAL = 'ADAPTER'
    AND A.WIP_GROUP NOT LIKE '%B36R_STOCKIN'
    AND R107.WIP_GROUP NOT LIKE '%BR2C%'
    AND EXISTS (
        SELECT 1
        FROM sfism4.r117 r117
        WHERE r117.SERIAL_NUMBER = A.SERIAL_NUMBER
          AND r117.MO_NUMBER LIKE '5%'
    )";

            using var command = new OracleCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                result.Add(new BonepileNPISerialAfter
                {
                    SerialNumber = reader["SERIAL_NUMBER"].ToString(),
                    Fg = reader["FG"] != DBNull.Value ? reader["FG"].ToString() : null,
                    MoNumber = reader["MO_NUMBER"].ToString(),
                    ModelName = reader["MODEL_NAME"].ToString(),
                    ProductLine = reader["PRODUCT_LINE"].ToString(),
                    WipGroupKanban = reader["WIP_GROUP_KANBAN"] != DBNull.Value ? reader["WIP_GROUP_KANBAN"].ToString() : null,
                    WipGroup = reader["WIP_GROUP"] != DBNull.Value ? reader["WIP_GROUP"].ToString() : null,
                    ErrorFlag = reader["ERROR_FLAG"] != DBNull.Value ? reader["ERROR_FLAG"].ToString() : null,
                    WorkFlag = reader["WORK_FLAG"] != DBNull.Value ? reader["WORK_FLAG"].ToString() : null,
                    Repair = reader["DATA19_COMBINED"] != DBNull.Value ? reader["DATA19_COMBINED"].ToString() : null,
                    TestGroup = reader["TEST_GROUP"] != DBNull.Value ? reader["TEST_GROUP"].ToString() : null,
                    TestTime = reader["TEST_TIME"] != DBNull.Value ? Convert.ToDateTime(reader["TEST_TIME"]) : (DateTime?)null,
                    TestCode = reader["TEST_CODE"] != DBNull.Value ? reader["TEST_CODE"].ToString() : null,
                    ErrorItemCode = reader["ERROR_ITEM_CODE"] != DBNull.Value ? reader["ERROR_ITEM_CODE"].ToString() : null,
                    ErrorDesc = reader["ERROR_DESC"] != DBNull.Value ? reader["ERROR_DESC"].ToString() : null,
                    Aging = reader["AGING"] != DBNull.Value ? Convert.ToDouble(reader["AGING"]) : (double?)null,
                    AgingOldest = reader["AGING_OLDEST"] != DBNull.Value ? Convert.ToDouble(reader["AGING_OLDEST"]) : (double?)null
                });
            }

            return result;
        }
    }

    public class MoTaskResult
    {
        public string SERIAL_NUMBER { get; set; }
        public string MODEL_NAME { get; set; }
        public string PRODUCT_LINE { get; set; }
        public string WIP_GROUP { get; set; }
        public string ERROR_FLAG { get; set; }
        public string MO_NUMBER { get; set; }
        public string GROUP_NAME { get; set; }
        public DateTime? STATION_TIME { get; set; }
        public string MO_STATUS { get; set; }
    }

}