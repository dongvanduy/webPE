#nullable disable
using API_WEB.Controllers.Repositories;
using API_WEB.Models.Repositories;
using API_WEB.Models.SmartFA;
using API_WEB.ModelsDB;
using API_WEB.ModelsOracle;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;
using System.Data;
using DocumentFormat.OpenXml.InkML;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace API_WEB.Controllers.SmartFA
{
    [Route("[controller]")]
    [ApiController]
    public class SearchFAController : ControllerBase
    {
        private readonly CSDL_NE _sqlContext;
        private readonly OracleDbContext _oracleContext;
        public SearchFAController(CSDL_NE sqlContext, OracleDbContext oracleContext)
        {
            _sqlContext = sqlContext ?? throw new ArgumentNullException(nameof(sqlContext));
            _oracleContext = oracleContext ?? throw new ArgumentNullException(nameof(oracleContext));
        }

        [HttpPost("search")]
        public async Task<IActionResult> SearchRepairTask([FromBody] SearchRequestNe request)
        {
            await using var connection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);

            try
            {
                await connection.OpenAsync();
                var searchQuery = @"
                 SELECT 
                    task.SERIAL_NUMBER,
                    task.MODEL_NAME,
                    task.MO_NUMBER,
                    task.TEST_GROUP,
                    task.TEST_CODE AS ERROR_CODE,
                    task.DATA1 AS ERROR_DESC,
                    task.DATA11 AS STATUS,
                    task.DATA12 AS PR_STATUS,
                    task.DATE3,
                    task.TESTER,
                    task.DATA13 as HANDOVER,
                    task.DATA17 as ACTION,
                    task.DATA18 as POSITION,
                    cmd.PRODUCT_LINE,
                    r107.WIP_GROUP,
                    CASE WHEN zkt.WIP_GROUP IS NOT NULL THEN zkt.WIP_GROUP
                        ELSE 'Before'
                    END AS KANBAN_WIP
                FROM SFISM4.R_REPAIR_TASK_T task
                JOIN SFIS1.C_MODEL_DESC_T md ON task.MODEL_NAME = md.MODEL_NAME
                JOIN SFISM4.R107 r107 ON task.SERIAL_NUMBER = r107.SERIAL_NUMBER
                LEFT JOIN SFISM4.Z_KANBAN_TRACKING_T zkt 
                    ON task.SERIAL_NUMBER = zkt.SERIAL_NUMBER
                LEFT JOIN SFIS1.C_MODEL_DESC_T cmd 
                    ON task.MODEL_NAME = cmd.MODEL_NAME
                WHERE md.MODEL_SERIAL <> 'SWITCH'
                AND r107.WIP_GROUP NOT LIKE '%BR2C%'";
                var parameters = new List<OracleParameter>();

                if (!string.IsNullOrEmpty(request.Data1))
                {
                    searchQuery += " AND task.DATA1 LIKE :p_data1 ";
                    parameters.Add(new OracleParameter("p_data1", $"%{request.Data1}%"));
                }

                if (!string.IsNullOrEmpty(request.ModelName))
                {
                    searchQuery += " AND task.MODEL_NAME = :p_model ";
                    parameters.Add(new OracleParameter("p_model", request.ModelName));
                }

                if (!string.IsNullOrEmpty(request.TestCode))
                {
                    searchQuery += " AND task.TEST_CODE = :p_testcode ";
                    parameters.Add(new OracleParameter("p_testcode", request.TestCode));
                }

                if (!string.IsNullOrEmpty(request.Status))
                {
                    searchQuery += " AND task.DATA11 = :p_status ";
                    parameters.Add(new OracleParameter("p_status", request.Status));
                }

                if (!string.IsNullOrEmpty(request.HandoverStatus))
                {
                    searchQuery += " AND task.DATA13 = :p_handover ";
                    parameters.Add(new OracleParameter("p_handover", request.HandoverStatus));
                }

                // Filter SN list
                if (request.SerialNumbers != null && request.SerialNumbers.Any())
                {
                    string inClause = string.Join(",", request.SerialNumbers.Select((sn, idx) => $":sn{idx}"));
                    searchQuery += $" AND task.SERIAL_NUMBER IN ({inClause}) ";

                    int i = 0;
                    foreach (var sn in request.SerialNumbers)
                    {
                        parameters.Add(new OracleParameter($"sn{i++}", sn));
                    }
                }

                var cmd = new OracleCommand(searchQuery, connection);
                cmd.BindByName = true;
                foreach (var p in parameters)
                {
                    cmd.Parameters.Add(p);
                }
                var results = new List<OracleRepairResult>();
                await using var reder = await cmd.ExecuteReaderAsync();
                while(await reder.ReadAsync())
                {
                    results.Add(new OracleRepairResult
                    {
                        SERIAL_NUMBER = reder["SERIAL_NUMBER"]?.ToString(),
                        MODEL_NAME = reder["MODEL_NAME"]?.ToString(),
                        MO_NUMBER = reder["MO_NUMBER"]?.ToString(),
                        TEST_GROUP = reder["TEST_GROUP"]?.ToString(),
                        ERROR_CODE = reder["ERROR_CODE"]?.ToString(),
                        ERROR_DESC = reder["ERROR_DESC"]?.ToString(),
                        STATUS = reder["STATUS"]?.ToString(),
                        PR_STATUS = reder["PR_STATUS"]?.ToString(),
                        DATE3 = reder["DATE3"] as DateTime?,
                        TESTER = reder["TESTER"]?.ToString(),
                        HANDOVER = reder["HANDOVER"]?.ToString(),
                        ACTION = reder["ACTION"]?.ToString(),
                        POSITION = reder["POSITION"]?.ToString(),
                        PRODUCT_LINE = reder["PRODUCT_LINE"]?.ToString(),
                        WIP_GROUP = reder["WIP_GROUP"]?.ToString(),
                        KANBAN_WIP = reder["KANBAN_WIP"]?.ToString()
                    });
                }
                return Ok(new
                {
                    success = true,
                    count = results.Count,
                    data = results
                });
            }
            catch (OracleException ex)
            {
                return StatusCode(500, new { success = false, message = $"Database error: {ex.Message}" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"System error: {ex.Message}" });
            }
            finally
            {
                if (connection.State == ConnectionState.Open)
                    await connection.CloseAsync();
            }
        }

        public class OracleRepairResult
        {
            public string SERIAL_NUMBER { get; set; }
            public string MODEL_NAME { get; set; }
            public string MO_NUMBER { get; set; }
            public string TEST_GROUP { get; set; }
            public string ERROR_CODE { get; set; }
            public string ERROR_DESC { get; set; }
            public string STATUS { get; set; }
            public string PR_STATUS { get; set; }
            public DateTime? DATE3 { get; set; }
            public string TESTER { get; set; }
            public string HANDOVER { get; set; }
            public string ACTION { get; set; }
            public string POSITION { get; set; }
            public string WIP_GROUP { get; set; }
            public string KANBAN_WIP { get; set; }
            public string PRODUCT_LINE { get; set; }
        }

        [HttpGet("get-fullname")]
        public IActionResult GetFullName(string username)
        {
            if (string.IsNullOrEmpty(username))
            {
                return BadRequest(new { success = false, message = "Username không được để trống!" });
            }
            try
            {
                // Tìm FullName dua trên Username
                var user = _sqlContext.Users.FirstOrDefault(u => u.Username == username);

                if (user == null)
                {
                    return NotFound(new { success = false, message = "Không tìm thấy người dùng!" });
                }

                return Ok(new { success = true, fullName = user.FullName });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });


            }
        }

        [HttpPost("get-fullname-batch")]
        public IActionResult GetFullNameBatch([FromBody] List<string> usernames)
        {
            var users = _sqlContext.Users
                .Where(u => usernames.Contains(u.Username))
                .ToDictionary(u => u.Username, u => u.FullName);

            return Ok(new { success = true, data = users });
        }

        [HttpGet("get-unique-modelnames")]
        public async Task<IActionResult> GetUniqueModelNames()
        {
            try
            {
                // Truy van lay danh sách ModelName khác nhau voi dieu kien MODEL_SERIAL != "SWITCH"
                var uniqueModelNames = await (
                    from task in _oracleContext.OracleDataRepairTask
                    join modelDesc in _oracleContext.OracleDataCModelDesc
                    on task.MODEL_NAME equals modelDesc.MODEL_NAME
                    where task.MODEL_NAME != null
                          && modelDesc.MODEL_SERIAL != "SWITCH" // Ðieu kien MODEL_SERIAL
                    select task.MODEL_NAME
                )
                .Distinct()
                .ToListAsync();

                return Ok(new
                {
                    success = true,
                    data = uniqueModelNames
                });
            }
            catch (Oracle.ManagedDataAccess.Client.OracleException ex)
            {
                // Xu lý loi liên quan den Oracle
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Oracle Error: {ex.Message}"
                });
            }
            catch (Exception ex)
            {
                // Xu lý loi chung
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        [HttpGet("get-unique-status")]
        public async Task<IActionResult> GetUniqueStatus()
        {
            try
            {
                // Truy van lay danh sách status khác nhau
                var uniqueStatus = await _oracleContext.OracleDataRepairTask
                    .Where(task => task.DATA11 != null && !task.MODEL_NAME.StartsWith("SA")) // Loai bo giá tri NULL
                    .Select(task => task.DATA11)
                    .Distinct()
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    data = uniqueStatus
                });
            }
            catch (Oracle.ManagedDataAccess.Client.OracleException ex)
            {
                //Xử lý liên quan đến oracle
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Oracle Error: {ex.Message}"
                });
            }
            catch (Exception ex)
            {
                //Xử lý lỗi chung
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        [HttpGet("get-unique-SN")]
        public async Task<IActionResult> GetUniqueSN()
        {
            try
            {
                var uniqueStatus = await (
                    from task in _oracleContext.OracleDataRepairTask
                    join modelDesc in _oracleContext.OracleDataCModelDesc
                    on task.MODEL_NAME equals modelDesc.MODEL_NAME
                    where task.SERIAL_NUMBER != null
                          && modelDesc.MODEL_SERIAL != "SWITCH"
                          && task.MODEL_NAME != null
                          && modelDesc.MODEL_NAME != null
                    select task.SERIAL_NUMBER
                )
                .Distinct()
                .ToListAsync();

                return Ok(new
                {
                    success = true,
                    data = uniqueStatus
                });
            }
            catch (Oracle.ManagedDataAccess.Client.OracleException ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Oracle Error: {ex.Message}"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        [HttpPost("search-history-by-list")]
        public async Task<IActionResult> GetHistoryBySerialNumberList([FromBody] List<string> serialNumbers)
        {
            try
            {
                if (serialNumbers == null || serialNumbers.Count == 0)
                {
                    return BadRequest(new { success = false, message = "Danh sách SN không được để trống!" });
                }

                // Truy v?n l?ch s? theo danh sách Serial Numbers
                var history = await _oracleContext.Set<R_REPAIR_TASK_DETAIL_T>()
                    .Where(detail => serialNumbers.Contains(detail.SERIAL_NUMBER))
                    .Select(detail => new
                    {
                        SERIAL_NUMBER = detail.SERIAL_NUMBER ?? "",
                        MO_NUMBER = detail.MO_NUMBER ?? "",
                        MODEL_NAME = detail.MODEL_NAME ?? "",
                        TEST_GROUP = detail.TEST_GROUP ?? "",
                        TEST_CODE = detail.TEST_CODE ?? "",
                        DATA1 = detail.DATA1 ?? "",
                        DATA11 = detail.DATA11 ?? "",
                        DATA12 = detail.DATA12 ?? "",
                        DATE3 = detail.DATE3,
                        TESTER = detail.TESTER ?? "",
                        DATA17 = detail.DATA17 ?? "",//Type
                        DATA18 = detail.DATA18 ?? "",//Location
                        DATA19 = detail.DATA19 ?? "",//Lich su sua chua.
                    })
                    .ToListAsync();

                if (history == null || history.Count == 0)
                {
                    return Ok(new { success = true, message = "Không tìm thấy lịch sử", data = new List<object>() });
                }

                return Ok(new { success = true, data = history });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("get-status-counts")]
        public async Task<IActionResult> GetStatusCounts([FromBody] string type)
        {
            try
            {
                IQueryable<dynamic> query;

                switch (type?.ToUpper())
                {
                    case "ALL":
                        query = _oracleContext.OracleDataRepairTask
                            .Where(task => task.DATA11 != null)
                            .Join(
                                _oracleContext.OracleDataR107,
                                task => task.SERIAL_NUMBER,     // khóa join
                                r107 => r107.SERIAL_NUMBER,
                                (task, r107) => new { task, r107 }
                            )
                            .Join(
                                _oracleContext.OracleDataCModelDesc,
                                tr => tr.task.MODEL_NAME,
                                modelDesc => modelDesc.MODEL_NAME,
                                (tr, modelDesc) => new { tr.task, tr.r107, modelDesc }
                            )
                            .Where(joined =>
                                joined.modelDesc.MODEL_SERIAL != "SWITCH" &&
                                !EF.Functions.Like(joined.r107.WIP_GROUP, "%BR2C%") &&
                                !EF.Functions.Like(joined.r107.WIP_GROUP, "%BCFA%")
                            )
                            .GroupBy(joined => joined.task.DATA11)
                            .Select(group => new
                            {
                                Status = group.Key,
                                Count = group.Count()
                            })
                            .OrderByDescending(x => x.Count);
                        break;

                    case "HANDOVER":
                        query = _oracleContext.OracleDataRepairTask
                            .Where(task => task.DATA13 != null)
                            .Join(
                                _oracleContext.OracleDataR107,
                                task => task.SERIAL_NUMBER,
                                r107 => r107.SERIAL_NUMBER,
                                (task, r107) => new { task, r107 }
                            )
                            .Join(
                                _oracleContext.OracleDataCModelDesc,
                                tr => tr.task.MODEL_NAME,
                                modelDesc => modelDesc.MODEL_NAME,
                                (tr, modelDesc) => new { tr.task, tr.r107, modelDesc }
                            )
                            .Where(joined =>
                                joined.modelDesc.MODEL_SERIAL != "SWITCH" &&
                                !EF.Functions.Like(joined.r107.WIP_GROUP, "%BR2C%") &&
                                !EF.Functions.Like(joined.r107.WIP_GROUP, "%BCFA%")
                            )
                            .GroupBy(joined => joined.task.DATA11)
                            .Select(group => new
                            {
                                Status = group.Key,
                                Count = group.Count()
                            })
                            .OrderByDescending(x => x.Count);
                        break;

                    case "ONLINE":
                        query = _oracleContext.OracleDataRepairTask
                            .Where(task => task.DATA18 != null &&
                                           task.DATA18 != "TRONG_KHO" &&
                                           task.DATA13 != "WAITING_HAND_OVER")
                            .Join(
                                _oracleContext.OracleDataR107,
                                task => task.SERIAL_NUMBER,
                                r107 => r107.SERIAL_NUMBER,
                                (task, r107) => new { task, r107 }
                            )
                            .Join(
                                _oracleContext.OracleDataCModelDesc,
                                tr => tr.task.MODEL_NAME,
                                modelDesc => modelDesc.MODEL_NAME,
                                (tr, modelDesc) => new { tr.task, tr.r107, modelDesc }
                            )
                            .Where(joined =>
                                joined.modelDesc.MODEL_SERIAL != "SWITCH" &&
                                !EF.Functions.Like(joined.r107.WIP_GROUP, "%BR2C%") &&
                                !EF.Functions.Like(joined.r107.WIP_GROUP, "%BCFA%")
                            )
                            .GroupBy(joined => joined.task.DATA11)
                            .Select(group => new
                            {
                                Status = group.Key,
                                Count = group.Count()
                            })
                            .OrderByDescending(x => x.Count);
                        break;

                    default:
                        return BadRequest(new
                        {
                            success = false,
                            message = "Type không hợp lệ. Vui lòng sử dụng: 'ALL', 'HANDOVER', hoặc 'ONLINE'."
                        });
                }

                var statusCounts = await query.ToListAsync();
                int totalCount = (type.ToUpper() == "ALL") ? statusCounts.Sum(x => x.Count) : 0;

                return Ok(new
                {
                    success = true,
                    data = statusCounts,
                    totalCount = totalCount
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }


        [HttpPost("get-repair-owner-data")]
        public async Task<IActionResult> GetRepairOwnerData([FromBody] TimeRequest request)
        {
            try
            {
                if (request == null || request.StartDate == null || request.EndDate == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Date không được để trống!"
                    });
                }

                var rawData = await (
                    from detail in _oracleContext.OracleDataRepairTaskDetail
                    join repair in _oracleContext.OracleDataR109
                        on detail.TESTER equals repair.REPAIRER
                    join r107 in _oracleContext.OracleDataR107
                        on repair.SERIAL_NUMBER equals r107.SERIAL_NUMBER
                    where
                        detail.DATE3 >= request.StartDate &&
                        detail.DATE3 <= request.EndDate &&
                        repair.REPAIR_TIME >= request.StartDate &&
                        repair.REPAIR_TIME <= request.EndDate &&
                        EF.Functions.Like(detail.DATA17, "%Nhận%")
                    select new
                    {
                        OWNER = detail.TESTER,
                        repair.SERIAL_NUMBER,
                        repair.MODEL_NAME,
                        repair.TEST_GROUP,
                        r107.WIP_GROUP,
                        r107.MO_NUMBER,
                        r107.ERROR_FLAG,
                        repair.TEST_CODE,
                        repair.DATA1,
                        REPAIRER = repair.REPAIRER,
                        REPAIR_TIME = repair.REPAIR_TIME,
                        repair.REASON_CODE,
                        repair.ERROR_ITEM_CODE
                    }
                ).ToListAsync();

                // Group theo SERIAL_NUMBER + REPAIR_TIME
                var groupedData = rawData
                    .GroupBy(x => new { x.SERIAL_NUMBER, x.REPAIR_TIME })
                    .Select(g => new
                    {
                        SERIAL_NUMBER = g.Key.SERIAL_NUMBER,
                        REPAIR_TIME = g.Key.REPAIR_TIME,
                        OWNER = g.First().OWNER,
                        MODEL_NAME = g.First().MODEL_NAME,
                        TEST_GROUP = g.First().TEST_GROUP,
                        WIP_GROUP = g.First().WIP_GROUP,
                        MO_NUMBER = g.First().MO_NUMBER,
                        ERROR_FLAG = g.First().ERROR_FLAG,
                        TEST_CODE = g.First().TEST_CODE,
                        DATA1 = g.First().DATA1,
                        REPAIRER = g.First().REPAIRER,
                        REASON_CODE = g.First().REASON_CODE,
                        // Gộp ERROR_ITEM_CODE
                        ERROR_ITEM_CODE = string.Join(",",
                            g.Select(x => x.ERROR_ITEM_CODE)
                             .Where(x => !string.IsNullOrWhiteSpace(x))
                             .Distinct())
                    })
                    .ToList();

                return Ok(new
                {
                    success = true,
                    data = groupedData
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }
         
        // API lấy dữ liệu từ R109 theo SerialNumber với repair_time mới nhất
        [HttpPost("get-repair-owner-data-by-sn")]
        public async Task<IActionResult> GetRepairOwnerDataBySerial([FromBody] SerialNumberRequest request)
        {
            try
            {
                var serialNumbers = request?.SerialNumbers?
                    .Where(sn => !string.IsNullOrWhiteSpace(sn))
                    .Distinct()
                    .ToList() ?? new List<string>();

                if (!serialNumbers.Any())
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "SerialNumber không được để trống!",
                    });
                }

                const int batchSize = 1000;
                var allResults = new List<RepairOwnerBySerialResult>();

                for (int i = 0; i < serialNumbers.Count; i += batchSize)
                {
                    var batch = serialNumbers.Skip(i).Take(batchSize);
                    var serialList = string.Join(",", batch.Select(sn => $"'{sn.Replace("'", "''")}'"));

                    string query = $@"
                        SELECT SerialNumber, ModelName, ReasonCode, TestCode, ErrorDesc, Repairer, TestGroup, ErrorItemCode, RepairTime
                        FROM (
                            SELECT NVL(r.SERIAL_NUMBER,'') AS SerialNumber,
                                   NVL(r.MODEL_NAME,'') AS ModelName,
                                   NVL(r.REASON_CODE,'') AS ReasonCode,
                                   NVL(r.TEST_CODE,'') AS TestCode,
                                   NVL(c.ERROR_DESC,'') AS ErrorDesc,
                                   NVL(r.REPAIRER,' ') AS Repairer,
                                   NVL(r.TEST_GROUP,'') AS TestGroup,
                                   NVL(r.ERROR_ITEM_CODE,' ') AS ErrorItemCode,
                                   NVL(TO_CHAR(r.REPAIR_TIME,'YYYY/MM/DD HH24:MI:SS'),' ') AS RepairTime,
                                   ROW_NUMBER() OVER(PARTITION BY r.SERIAL_NUMBER ORDER BY NVL(r.REPAIR_TIME, TO_DATE('1970-01-01','YYYY-MM-DD')) DESC) AS RN
                            FROM SFISM4.R109 r
                            INNER JOIN SFIS1.C_ERROR_CODE_T c ON r.TEST_CODE = c.ERROR_CODE
                            WHERE r.REPAIR_TIME IS NOT NULL AND r.SERIAL_NUMBER IN ({serialList})
                        )
                        WHERE RN = 1";

                    var batchResults = await _oracleContext.Set<RepairOwnerBySerialResult>()
                        .FromSqlRaw(query)
                        .ToListAsync();

                    allResults.AddRange(batchResults);
                }

                return Ok(new
                {
                    success = true,
                    data = allResults
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // API lấy dữ liệu từ R109 theo SerialNumber với repair_time mới nhất
        [HttpPost("get-repair-data")]
        public async Task<IActionResult> GetRepairDatal([FromBody] SerialNumberRequest request)
        {
            try
            {
                var serialNumbers = request?.SerialNumbers?
                    .Where(sn => !string.IsNullOrWhiteSpace(sn))
                    .Distinct()
                    .ToList() ?? new List<string>();

                if (!serialNumbers.Any())
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "SerialNumber không được để trống!",
                    });
                }

                const int batchSize = 1000;
                var allResults = new List<RepairOwnerBySerialResult>();

                for (int i = 0; i < serialNumbers.Count; i += batchSize)
                {
                    var batch = serialNumbers.Skip(i).Take(batchSize);
                    var serialList = string.Join(",", batch.Select(sn => $"'{sn.Replace("'", "''")}'"));

                    string query = $@"
                           SELECT 
                                   NVL(r.SERIAL_NUMBER,'') AS SerialNumber,
                                   NVL(r.MODEL_NAME,'') AS ModelName,
                                   NVL(r.REASON_CODE,'') AS ReasonCode,
                                   NVL(r.TEST_CODE,'') AS TestCode,
                                   NVL(c.ERROR_DESC,'') AS ErrorDesc,
                                   NVL(r.REPAIRER,' ') AS Repairer,
                                   NVL(r.TEST_GROUP,'') AS TestGroup,
                                   NVL(r.ERROR_ITEM_CODE,' ') AS ErrorItemCode,
                                   NVL(TO_CHAR(r.REPAIR_TIME,'YYYY/MM/DD HH24:MI:SS'),' ') AS RepairTime,
                                   ROW_NUMBER() OVER(PARTITION BY r.SERIAL_NUMBER ORDER BY NVL(r.REPAIR_TIME, TO_DATE('1970-01-01','YYYY-MM-DD')) ASC) AS RN
                            FROM SFISM4.R109 r
                            INNER JOIN SFIS1.C_ERROR_CODE_T c ON r.TEST_CODE = c.ERROR_CODE
                            WHERE r.REPAIR_TIME IS NOT NULL AND r.SERIAL_NUMBER IN ({serialList})";

                    var batchResults = await _oracleContext.Set<RepairOwnerBySerialResult>()
                        .FromSqlRaw(query)
                        .ToListAsync();

                    allResults.AddRange(batchResults);
                }

                return Ok(new
                {
                    success = true,
                    data = allResults
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        //API HUONG DAN REPAIR
        [HttpPost("get-repair-suggestions")]
        public async Task<IActionResult> GetRepairSuggestions([FromBody] RepairSuggestionRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.SerialNumber))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "SerialNumber không được để trống!",
                });
            }

            try
            {
                using var connection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);
                await connection.OpenAsync();

                const string query = @"
SELECT *
FROM (
    SELECT 
        C.REASON_CODE,
        C.ERROR_ITEMS,
        C.CNT,
        ROUND((C.CNT / T.TOTAL_CNT) * 100, 2) AS RATE_PERCENT
    FROM
        (
            SELECT 
                REASON_CODE,
                ERROR_ITEMS,
                COUNT(*) AS CNT
            FROM 
                (
                    SELECT 
                        R.REPAIR_TIME,
                        R.REASON_CODE,
                        LISTAGG(
                            UPPER(TRIM(REPLACE(R.ERROR_ITEM_CODE, CHR(9), '')))
                        , ', ') 
                        WITHIN GROUP (ORDER BY 
                            UPPER(TRIM(REPLACE(R.ERROR_ITEM_CODE, CHR(9), '')))
                        ) AS ERROR_ITEMS
                    FROM SFISM4.R_REPAIR_T R
                    WHERE R.MODEL_NAME = (
                            SELECT MODEL_NAME FROM (
                                SELECT MODEL_NAME
                                FROM SFISM4.R_REPAIR_T
                                WHERE SERIAL_NUMBER = :serial
                                ORDER BY TEST_TIME DESC
                            ) WHERE ROWNUM = 1
                        )
                      AND R.TEST_CODE = (
                            SELECT TEST_CODE FROM (
                                SELECT TEST_CODE
                                FROM SFISM4.R_REPAIR_T
                                WHERE SERIAL_NUMBER = :serial
                                ORDER BY TEST_TIME DESC
                            ) WHERE ROWNUM = 1
                        )
                      AND R.TEST_GROUP = (
                            SELECT TEST_GROUP FROM (
                                SELECT TEST_GROUP
                                FROM SFISM4.R_REPAIR_T
                                WHERE SERIAL_NUMBER = :serial
                                ORDER BY TEST_TIME DESC
                            ) WHERE ROWNUM = 1
                        )
                      AND R.REPAIR_TIME IS NOT NULL
                    GROUP BY 
                        R.REPAIR_TIME, 
                        R.REASON_CODE
                )
            GROUP BY REASON_CODE, ERROR_ITEMS
        ) C,
        (
            SELECT COUNT(*) AS TOTAL_CNT
            FROM (
                SELECT DISTINCT REPAIR_TIME
                FROM SFISM4.R_REPAIR_T R
                WHERE R.MODEL_NAME = (
                        SELECT MODEL_NAME FROM (
                            SELECT MODEL_NAME
                            FROM SFISM4.R_REPAIR_T
                            WHERE SERIAL_NUMBER = :serial
                            ORDER BY TEST_TIME DESC
                        ) WHERE ROWNUM = 1
                    )
                AND R.TEST_CODE = (
                        SELECT TEST_CODE FROM (
                            SELECT TEST_CODE
                            FROM SFISM4.R_REPAIR_T
                            WHERE SERIAL_NUMBER = :serial
                            ORDER BY TEST_TIME DESC
                        ) WHERE ROWNUM = 1
                    )
                AND R.TEST_GROUP = (
                        SELECT TEST_GROUP FROM (
                            SELECT TEST_GROUP
                            FROM SFISM4.R_REPAIR_T
                            WHERE SERIAL_NUMBER = :serial
                            ORDER BY TEST_TIME DESC
                        ) WHERE ROWNUM = 1
                    )
                AND R.REPAIR_TIME IS NOT NULL
            )
        ) T
    ORDER BY C.CNT DESC
)
WHERE ROWNUM <= 3";

                using var command = new OracleCommand(query, connection)
                {
                    BindByName = true
                };

                command.Parameters.Add(new OracleParameter("serial", request.SerialNumber));

                var suggestions = new List<RepairSuggestionResult>();

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    suggestions.Add(new RepairSuggestionResult
                    {
                        ReasonCode = reader["REASON_CODE"]?.ToString() ?? string.Empty,
                        ErrorItems = reader["ERROR_ITEMS"]?.ToString() ?? string.Empty,
                        Count = reader["CNT"] != DBNull.Value ? Convert.ToInt32(reader["CNT"]) : 0,
                        RatePercent = reader["RATE_PERCENT"] != DBNull.Value ? Convert.ToDecimal(reader["RATE_PERCENT"]) : 0m
                    });
                }

                return Ok(new
                {
                    success = true,
                    data = suggestions
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // API lấy dữ liệu confirm và save theo theo thời gian.
        [HttpPost("get-confirm-save-data")]
        public async Task<IActionResult> GetConfirmSaveData([FromBody] TimeRequest request)
        {
            try
            {
                if (request.StartDate == null || request.EndDate == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Date không được để trống!"
                    });
                }

                var result = await _oracleContext.OracleDataRepairTaskDetail
                    .GroupJoin(_oracleContext.OracleDataR107,
                        task => task.SERIAL_NUMBER,
                        OracleDataR107 => OracleDataR107.SERIAL_NUMBER,
                        (task, r107Collection) => new { task, r107Collection })
                    .SelectMany(
                        x => x.r107Collection.DefaultIfEmpty(),
                        (task, OracleDataR107) => new { task.task, OracleDataR107 })
                    .Where(x =>
                        (EF.Functions.Like(x.task.DATA17.ToUpper(), "%CONFIRM%") || EF.Functions.Like(x.task.DATA17.ToUpper(), "%SAVE%")) &&
                        x.task.DATE3 >= request.StartDate && x.task.DATE3 <= request.EndDate && x.task.TESTER != null)
                    .Select(x => new
                    {
                        OWNER = x.task.TESTER,
                        x.task.SERIAL_NUMBER,
                        x.task.MODEL_NAME,
                        x.task.TEST_GROUP,
                        x.task.TEST_CODE,
                        x.task.DATA1,
                        TYPE = x.task.DATA17,
                        TIME_SAVE = x.task.DATE3,
                        CURRENT_STATUS = x.task.DATA12,
                        NEXT_STATUS = x.task.DATA11,
                        REMARK = x.task.DATA19,
                        WIP_GROUP = x.OracleDataR107 != null ? x.OracleDataR107.WIP_GROUP : null,
                        MO_NUMBER = x.OracleDataR107 != null ? x.OracleDataR107.MO_NUMBER : null,
                    })
                    .OrderBy(task => task.OWNER) // Sắp xếp theo OWNER
                    .ToListAsync();
                return Ok(new
                {
                    success = true,
                    data = result
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }


        //=======THỐNG KÊ SỐ LƯỢNG VI-RE===============
        [HttpPost("get-vi-re-confirm-data")]
        public async Task<IActionResult> GetViReConfirmData([FromBody] TimeRequest request)
        {
            try
            {
                if (request == null || request.StartDate == default || request.EndDate == default)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Date không được để trống!"
                    });
                }

                var filteredData = await _oracleContext.OracleDataRepairTaskDetail
                    .Where(detail => detail.DATA12 != null
                        && detail.DATA12.Trim().ToUpper() == "VI-RE"
                        && (detail.DATA19 == null || detail.DATA19 != "CONFIRM_PUT_B36R")
                        && detail.DATA17 != null
                        && detail.DATA17.Trim().ToUpper() == "CONFIRM"
                        && detail.DATE3 >= request.StartDate && detail.DATE3 <= request.EndDate)
                    .Select(detail => new ViReConfirmDetailDto
                    {
                        SerialNumber = detail.SERIAL_NUMBER,
                        MONumber = detail.MO_NUMBER,
                        ModelName = detail.MODEL_NAME,
                        TestTime = detail.TEST_TIME,
                        TestCode = detail.TEST_CODE,
                        Tester = detail.TESTER,
                        TestGroup = detail.TEST_GROUP,
                        ErrorDesc = detail.DATA1,
                        Status = detail.DATA11,
                        PreStatus = detail.DATA12,
                        Date3 = detail.DATE3
                    })
                    .ToListAsync();

                var grouped = filteredData
                    .GroupBy(item => string.IsNullOrWhiteSpace(item.Tester) ? "UNKNOWN" : item.Tester!.Trim())
                    .Select(group =>
                    {
                        var orderedDetails = group
                            .OrderByDescending(x => x.TestTime ?? x.Date3)
                            .ToList();

                        var okCount = orderedDetails.Count(detail => IsOkStatus(detail.Status));
                        var ngCount = Math.Max(orderedDetails.Count - okCount, 0);

                        return new ViReConfirmOwnerDto
                        {
                            Owner = group.Key,
                            Count = orderedDetails.Count,
                            OkCount = okCount,
                            NgCount = ngCount,
                            Details = orderedDetails
                        };
                    })
                    .OrderByDescending(x => x.Count)
                    .ToList();

                return Ok(new
                {
                    success = true,
                    totalCount = filteredData.Count,
                    totalOk = grouped.Sum(x => x.OkCount),
                    totalNg = grouped.Sum(x => x.NgCount),
                    data = grouped
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }
        //===========THỐNG KÊ SỐ LƯỢNG CONFIRM CHECK LIST ===============
        [HttpPost("get-check-list-confirm-data")]
        public async Task<IActionResult> GetCheckListConfirmData([FromBody] TimeRequest request)
        {
            try
            {
                if (request == null || request.StartDate == default || request.EndDate == default)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Date không được để trống!"
                    });
                }

                var filteredData = await _oracleContext.OracleDataRepairTaskDetail
                    .Where(detail => detail.DATA12 != null
                        && detail.DATA11.Trim().ToUpper() == "CHECK_LIST"
                        && (detail.DATA19 == null || detail.DATA19 != "CONFIRM_PUT_B36R")
                        && detail.DATA17 != null
                        && detail.DATA17.Trim().ToUpper() == "CONFIRM"
                        && detail.DATE3 >= request.StartDate && detail.DATE3 <= request.EndDate)
                    .Select(detail => new CheckListConfirmDetailDto
                    {
                        SerialNumber = detail.SERIAL_NUMBER,
                        MONumber = detail.MO_NUMBER,
                        ModelName = detail.MODEL_NAME,
                        TestTime = detail.TEST_TIME,
                        TestCode = detail.TEST_CODE,
                        Tester = detail.TESTER,
                        TestGroup = detail.TEST_GROUP,
                        ErrorDesc = detail.DATA1,
                        Status = detail.DATA11,
                        PreStatus = detail.DATA12,
                        Date3 = detail.DATE3
                    })
                    .ToListAsync();

                if (!filteredData.Any())
                {
                    return Ok(new
                    {
                        success = true,
                        totalCount = 0,
                        totalOk = 0,
                        totalNg = 0,
                        data = new List<CheckListConfirmOwnerDto>()
                    });
                }

                var serialNumbers = filteredData
                    .Select(item => item.SerialNumber?.Trim())
                    .Where(sn => !string.IsNullOrWhiteSpace(sn))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (!serialNumbers.Any())
                {
                    return Ok(new
                    {
                        success = true,
                        totalCount = 0,
                        totalOk = 0,
                        totalNg = 0,
                        data = new List<CheckListConfirmOwnerDto>()
                    });
                }

                // Giữ nguyên toàn bộ filteredData là validData
                var validData = filteredData;

                var grouped = validData
                    .GroupBy(item => string.IsNullOrWhiteSpace(item.Tester) ? "UNKNOWN" : item.Tester!.Trim())
                    .Select(group =>
                    {
                        var orderedDetails = group
                            .OrderByDescending(x => x.TestTime ?? x.Date3)
                            .ToList();

                        var okCount = orderedDetails.Count(detail => IsOkStatus(detail.Status));
                        var ngCount = Math.Max(orderedDetails.Count - okCount, 0);

                        return new CheckListConfirmOwnerDto
                        {
                            Owner = group.Key,
                            Count = orderedDetails.Count,
                            OkCount = okCount,
                            NgCount = ngCount,
                            Details = orderedDetails
                        };
                    })
                    .OrderByDescending(x => x.Count)
                    .ToList();

                return Ok(new
                {
                    success = true,
                    totalCount = validData.Count,
                    totalOk = grouped.Sum(x => x.OkCount),
                    totalNg = grouped.Sum(x => x.NgCount),
                    data = grouped
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        private static readonly string OkStatusNormalized = RemoveDiacritics("CHỜ TRẢ").ToUpperInvariant();

        private static bool IsOkStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            var normalized = RemoveDiacritics(status).ToUpperInvariant();
            return normalized == OkStatusNormalized;
        }

        private static string RemoveDiacritics(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var normalizedString = input.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder(normalizedString.Length);

            foreach (var c in normalizedString)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }
        //================END====================


        // API tổng hợp summary owner theo thời gian
        [HttpPost("get-summary-owner")]
        public async Task<IActionResult> GetSummaryOwner([FromBody] TimeRequest timeRange)
        {
            try
            {
                using (var connection = new OracleConnection("User Id=TE;Password=B05te;Data Source=(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(PROTOCOL=TCP)(HOST=10.220.130.220)(PORT=1521)))(CONNECT_DATA=(SERVER=DEDICATED)(SERVICE_NAME=vnsfc)));"))
                {
                    await connection.OpenAsync();

                    string query = @"
                SELECT 
                    COALESCE(B1.owner, COALESCE(B2.owner, COALESCE(B3.owner, B4.owner))) AS owner,
                    B1.NHAN,
                    B2.CONFIRM,
                    B3.SAVE,
                    B4.REPAIR
                FROM (
                    SELECT COUNT(serial_number) AS NHAN, tester AS owner
                    FROM SFISM4.R_REPAIR_TASK_DETAIL_T  
                    WHERE data17 LIKE '%Nhận%'  
                      AND date3 BETWEEN TO_DATE(:StartDate, 'YYYY-MM-DD HH24:MI:SS') 
                                   AND TO_DATE(:EndDate, 'YYYY-MM-DD HH24:MI:SS')
                    GROUP BY tester
                ) B1
                LEFT JOIN (
                    SELECT COUNT(a2.serial_number) AS CONFIRM, a2.tester AS owner
                    FROM (
                        SELECT aa.* 
                        FROM (
                            SELECT * 
                            FROM SFISM4.R_REPAIR_TASK_DETAIL_T  
                            WHERE UPPER(data17) LIKE '%CONFIRM%'  
                              AND date3 BETWEEN TO_DATE(:StartDate, 'YYYY-MM-DD HH24:MI:SS') 
                                          AND TO_DATE(:EndDate, 'YYYY-MM-DD HH24:MI:SS')
                            UNION ALL
                            SELECT * 
                            FROM SFISM4.R_REPAIR_TASK_DETAIL_T  
                            WHERE UPPER(data17) LIKE '%SAVE%'  
                              AND date3 BETWEEN TO_DATE(:StartDate, 'YYYY-MM-DD HH24:MI:SS') 
                                          AND TO_DATE(:EndDate, 'YYYY-MM-DD HH24:MI:SS') 
                              AND SERIAL_NUMBER NOT IN (
                                  SELECT SERIAL_NUMBER 
                                  FROM SFISM4.R_REPAIR_TASK_DETAIL_T  
                                  WHERE UPPER(data17) LIKE '%CONFIRM%'
                              )
                        ) aa 
                        WHERE (aa.serial_number, tester) NOT IN (
                            SELECT serial_number, repairer  
                            FROM sfism4.R109 
                            WHERE repair_time BETWEEN TO_DATE(:StartDate, 'YYYY-MM-DD HH24:MI:SS') 
                                                 AND TO_DATE(:EndDate, 'YYYY-MM-DD HH24:MI:SS')
                        )
                    ) a2 
                    WHERE UPPER(data17) LIKE '%CONFIRM%'    
                    GROUP BY tester
                ) B2 ON B1.owner = B2.owner
                LEFT JOIN (
                    SELECT COUNT(a3.serial_number) AS SAVE, a3.tester AS owner
                    FROM (
                        SELECT aa.* 
                        FROM (
                            SELECT * 
                            FROM SFISM4.R_REPAIR_TASK_DETAIL_T  
                            WHERE UPPER(data17) LIKE '%CONFIRM%'  
                            UNION ALL
                            SELECT * 
                            FROM SFISM4.R_REPAIR_TASK_DETAIL_T  
                            WHERE UPPER(data17) LIKE '%SAVE%'  
                              AND SERIAL_NUMBER NOT IN (
                                  SELECT SERIAL_NUMBER 
                                  FROM SFISM4.R_REPAIR_TASK_DETAIL_T  
                                  WHERE UPPER(data17) LIKE '%CONFIRM%'
                              )
                        ) aa 
                        WHERE (aa.serial_number, tester) NOT IN (
                            SELECT serial_number, repairer  
                            FROM sfism4.R109 
                            WHERE repair_time BETWEEN TO_DATE(:StartDate, 'YYYY-MM-DD HH24:MI:SS') 
                                                 AND TO_DATE(:EndDate, 'YYYY-MM-DD HH24:MI:SS')
                        )
                    ) a3 
                    WHERE UPPER(data17) LIKE '%SAVE%'    
                    GROUP BY tester
                ) B3 ON B1.owner = B3.owner
                LEFT JOIN (
                    SELECT COUNT(serial_number) AS REPAIR, repairer AS owner
                    FROM sfism4.R109
                    WHERE repair_time BETWEEN TO_DATE(:StartDate, 'YYYY-MM-DD HH24:MI:SS') 
                                                AND TO_DATE(:EndDate, 'YYYY-MM-DD HH24:MI:SS')  
                    GROUP BY repairer
                ) B4 ON B1.owner = B4.owner
                ORDER BY owner";

                    using (var command = new OracleCommand(query, connection))
                    {
                        command.Parameters.Add(new OracleParameter("StartDate", timeRange.StartDate.ToString("yyyy-MM-dd HH:mm:ss")));
                        command.Parameters.Add(new OracleParameter("EndDate", timeRange.EndDate.ToString("yyyy-MM-dd HH:mm:ss")));

                        var data = new List<dynamic>();
                        int totalNHAN = 0, totalCONFIRM = 0, totalSAVE = 0, totalREPAIR = 0;

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                int nhan = reader["Nhận"] == DBNull.Value ? 0 : Convert.ToInt32(reader["Nhận"]);
                                int confirm = reader["CONFIRM"] == DBNull.Value ? 0 : Convert.ToInt32(reader["CONFIRM"]);
                                int save = reader["SAVE"] == DBNull.Value ? 0 : Convert.ToInt32(reader["SAVE"]);
                                int repair = reader["REPAIR"] == DBNull.Value ? 0 : Convert.ToInt32(reader["REPAIR"]);

                                data.Add(new
                                {
                                    Owner = reader["owner"]?.ToString(),
                                    NHAN = nhan,
                                    CONFIRM = confirm,
                                    SAVE = save,
                                    REPAIR = repair
                                });

                                // C?ng d?n d? tính t?ng
                                totalNHAN += nhan;
                                totalCONFIRM += confirm;
                                totalSAVE += save;
                                totalREPAIR += repair;
                            }
                        }

                        return Ok(new
                        {
                            success = true,
                            data,
                            totals = new
                            {
                                Total_NHAN = totalNHAN,
                                Total_CONFIRM = totalCONFIRM,
                                Total_SAVE = totalSAVE,
                                Total_REPAIR = totalREPAIR
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        [HttpPost("get-allowed-areas")]
        public IActionResult GetAllowedAreas([FromBody] CardCodeRequest request)
        {
            if (string.IsNullOrEmpty(request.CardCode))
            {
                return BadRequest(new { success = false, message = "Emp not null!" });
            }

            try
            {
                // Tìm ngu?i dùng theo mã th? (Username)
                var user = _sqlContext.Users.FirstOrDefault(u => u.Username == request.CardCode);

                if (user == null)
                {
                    return NotFound(new { success = false, message = "Not found Emp!" });
                }

                return Ok(new { success = true, allowedAreas = user.AllowedAreas });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("get-latest-tester")]
        public async Task<IActionResult> GetLatestTester([FromBody] List<string> serialNumbers)
        {
            if (serialNumbers == null || !serialNumbers.Any())
                return BadRequest(new { message = "Serial numbers list cannot be empty." });
            try
            {
                var serialNumbersList = string.Join(",", serialNumbers.Select(sn => $"'{sn}'"));
                // SQL Query ch? l?y SERIAL_NUMBER và TESTER
                string query = $@"
                    SELECT *
                        FROM (
                            SELECT
                                DETAIL_T.SERIAL_NUMBER,
                                DETAIL_T.TESTER,
                                DETAIL_T.DATE3,
                                DETAIL_T.DATA17
                            FROM SFISM4.R_REPAIR_TASK_DETAIL_T DETAIL_T
                            WHERE DETAIL_T.SERIAL_NUMBER IN ({serialNumbersList}) 
                            AND DETAIL_T.DATA17 = 'confirm'
                            ORDER BY DETAIL_T.DATE3 DESC
                        )
                        WHERE ROWNUM = 1
                        ";

                using (var command = _oracleContext.Database.GetDbConnection().CreateCommand())
                {
                    command.CommandText = query;
                    command.CommandType = System.Data.CommandType.Text;

                    _oracleContext.Database.OpenConnection(); // M? k?t n?i

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        var results = new List<SerialNumberTesterDto>();
                        while (await reader.ReadAsync())
                        {
                            results.Add(new SerialNumberTesterDto
                            {
                                SERIAL_NUMBER = reader["SERIAL_NUMBER"].ToString(),
                                TESTER = reader["TESTER"].ToString()
                            });
                        }

                        if (!results.Any())
                            return NotFound(new { message = "No records found for the given Serial Numbers!" });

                        return Ok(results);
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while processing the request.", error = ex.Message });
            }
        }

        //API get-retest-summary
        [HttpPost("get-retest-summary")]
        public async Task<IActionResult> GetRetestSummary([FromBody] TimeRequest request)
        {
            try
            {
                using (var connection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
                SELECT 
                    tester AS ID,
                    COUNT(CASE WHEN test_result = 'PASS' THEN 1 END) AS QTY_OK,
                    COUNT(CASE WHEN test_result = 'FAIL' THEN 1 END) AS QTY_NG,
                    COUNT(CASE WHEN test_result = 'not data' THEN 1 END) AS NOT_DEFINE,
                    COUNT(*) AS TOTAL
                FROM (
                    SELECT 
                        tester,
                        NVL(REGEXP_SUBSTR(data19, 'KQ:([^/]+)', 1, 1, NULL, 1), 'not data') AS test_result
                    FROM sfism4.r_repair_task_detail_t 
                    WHERE data12 = 'RETEST'
                    AND data17 = 'confirm'
                    AND (data19 IS NULL OR data19 != 'CONFIRM_PUT_B36R') 
                    AND date3 BETWEEN TO_DATE(:StartDate, 'YYYY-MM-DD HH24:MI:SS') 
                                  AND TO_DATE(:EndDate, 'YYYY-MM-DD HH24:MI:SS')
                ) sub
                GROUP BY tester
                ORDER BY TOTAL DESC";

                    using (var command = new OracleCommand(query, connection))
                    {
                        command.Parameters.Add(new OracleParameter("StartDate", request.StartDate.ToString("yyyy-MM-dd HH:mm:ss")));
                        command.Parameters.Add(new OracleParameter("EndDate", request.EndDate.ToString("yyyy-MM-dd HH:mm:ss")));

                        var data = new List<dynamic>();

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                data.Add(new
                                {
                                    ID = reader["ID"].ToString(),
                                    QTY_OK = Convert.ToInt32(reader["QTY_OK"]),
                                    QTY_NG = Convert.ToInt32(reader["QTY_NG"]),
                                    NOT_DEFINE = Convert.ToInt32(reader["NOT_DEFINE"]),
                                    TOTAL = Convert.ToInt32(reader["TOTAL"])
                                });
                            }
                        }

                        return Ok(new
                        {
                            success = true,
                            data
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        //API get-retest-detail-normal
        [HttpPost("get-retest-details-normal")]
        public async Task<IActionResult> GetRetestDetails([FromBody] TimeRequest request)
        {
            try
            {
                using (var connection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
                SELECT 
                    date3,
                    tester,
                    serial_number,
                    model_name,
                    test_group,
                    data1,
                    NVL(REGEXP_SUBSTR(data19, 'KQ:([^/]+)', 1, 1, NULL, 1), 'not data') AS test_result,
                    NVL(REGEXP_SUBSTR(data19, 'remark:([^/]+)', 1, 1, NULL, 1), 'not data') AS remark
                FROM sfism4.r_repair_task_detail_t 
                WHERE data12 = 'RETEST'
                AND data17 = 'confirm'
                AND (data19 IS NULL OR data19 != 'CONFIRM_PUT_B36R')
                AND data1 NOT LIKE '%mcu_test_group%' 
                AND data1 NOT LIKE '%DPU_MEM_TEST%' 
                AND date3 BETWEEN TO_DATE(:StartDate, 'YYYY-MM-DD HH24:MI:SS') 
                              AND TO_DATE(:EndDate, 'YYYY-MM-DD HH24:MI:SS')
                ORDER BY tester DESC";

                    using (var command = new OracleCommand(query, connection))
                    {
                        command.Parameters.Add(new OracleParameter("StartDate", request.StartDate.ToString("yyyy-MM-dd HH:mm:ss")));
                        command.Parameters.Add(new OracleParameter("EndDate", request.EndDate.ToString("yyyy-MM-dd HH:mm:ss")));

                        var data = new List<dynamic>();

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                data.Add(new
                                {
                                    Date = reader["date3"] != DBNull.Value ? Convert.ToDateTime(reader["date3"]) : (DateTime?)null,
                                    Tester = reader["tester"].ToString(),
                                    SerialNumber = reader["serial_number"].ToString(),
                                    ModelName = reader["model_name"].ToString(),
                                    TestGroup = reader["test_group"].ToString(),
                                    Data1 = reader["data1"].ToString(),
                                    TestResult = reader["test_result"].ToString(),
                                    Remark = reader["remark"].ToString()
                                });
                            }
                        }

                        return Ok(new
                        {
                            success = true,
                            data
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        //API get-retest-detail-MCU
        [HttpPost("get-retest-mcu-test-group")]
        public async Task<IActionResult> GetRetestMCUTestGroup([FromBody] TimeRequest request)
        {
            try
            {
                using (var connection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString))
                {
                    await connection.OpenAsync();
                    string query = @"
                        SELECT 
                            date3,
                            tester,
                            serial_number,
                            model_name,
                            test_group,
                            data1,
                            NVL(REGEXP_SUBSTR(data19, 'KQ:([^/]+)', 1, 1, NULL, 1), 'not data') AS test_result,
                            NVL(REGEXP_SUBSTR(data19, 'remark:([^/]+)', 1, 1, NULL, 1), 'not data') AS remark
                        FROM sfism4.r_repair_task_detail_t 
                        WHERE data12 = 'RETEST'
                        AND data17 = 'confirm'
                        AND (data19 IS NULL OR data19 != 'CONFIRM_PUT_B36R')
                        AND data1 LIKE '%mcu_test_group%' 
                        AND date3 BETWEEN TO_DATE(:StartDate, 'YYYY-MM-DD HH24:MI:SS') 
                                      AND TO_DATE(:EndDate, 'YYYY-MM-DD HH24:MI:SS')
                        ORDER BY tester DESC";

                    using (var command = new OracleCommand(query, connection))
                    {
                        command.Parameters.Add(new OracleParameter("StartDate", request.StartDate.ToString("yyyy-MM-dd HH:mm:ss")));
                        command.Parameters.Add(new OracleParameter("EndDate", request.EndDate.ToString("yyyy-MM-dd HH:mm:ss")));

                        var data = new List<dynamic>();

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                data.Add(new
                                {
                                    Date = reader["date3"] != DBNull.Value ? Convert.ToDateTime(reader["date3"]) : (DateTime?)null,
                                    Tester = reader["tester"].ToString(),
                                    SerialNumber = reader["serial_number"].ToString(),
                                    ModelName = reader["model_name"].ToString(),
                                    TestGroup = reader["test_group"].ToString(),
                                    Data1 = reader["data1"].ToString(),
                                    TestResult = reader["test_result"].ToString(),
                                    Remark = reader["remark"].ToString()
                                });
                            }
                        }

                        return Ok(new
                        {
                            success = true,
                            data
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        //API get-retest-dpu-mem-test
        [HttpPost("get-retest-dpu-mem-test")]
        public async Task<IActionResult> GetRetestDPUMemTest([FromBody] TimeRequest request)
        {
            try
            {
                using (var connection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
                SELECT 
                    date3,
                    tester,
                    serial_number,
                    model_name,
                    test_group,
                    data1,
                    NVL(REGEXP_SUBSTR(data19, 'KQ:([^/]+)', 1, 1, NULL, 1), 'not data') AS test_result,
                    NVL(REGEXP_SUBSTR(data19, 'remark:([^/]+)', 1, 1, NULL, 1), 'not data') AS remark
                FROM sfism4.r_repair_task_detail_t 
                WHERE data12 = 'RETEST'
                AND data17 = 'confirm'
                AND (data19 IS NULL OR data19 != 'CONFIRM_PUT_B36R')
                AND data1 LIKE '%DPU_MEM_TEST%' 
                AND date3 BETWEEN TO_DATE(:StartDate, 'YYYY-MM-DD HH24:MI:SS') 
                              AND TO_DATE(:EndDate, 'YYYY-MM-DD HH24:MI:SS')
                ORDER BY tester DESC";

                    using (var command = new OracleCommand(query, connection))
                    {
                        command.Parameters.Add(new OracleParameter("StartDate", request.StartDate.ToString("yyyy-MM-dd HH:mm:ss")));
                        command.Parameters.Add(new OracleParameter("EndDate", request.EndDate.ToString("yyyy-MM-dd HH:mm:ss")));

                        var data = new List<dynamic>();

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                data.Add(new
                                {
                                    Date = reader["date3"] != DBNull.Value ? Convert.ToDateTime(reader["date3"]) : (DateTime?)null,
                                    Tester = reader["tester"].ToString(),
                                    SerialNumber = reader["serial_number"].ToString(),
                                    ModelName = reader["model_name"].ToString(),
                                    TestGroup = reader["test_group"].ToString(),
                                    Data1 = reader["data1"].ToString(),
                                    TestResult = reader["test_result"].ToString(),
                                    Remark = reader["remark"].ToString()
                                });
                            }
                        }

                        return Ok(new
                        {
                            success = true,
                            data
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        [HttpGet("get-error-codes")]
        public async Task<IActionResult> GetErrorCodes([FromQuery] string term = "", [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            try
            {
                var query = _oracleContext.Set<ErrorCode>().FromSqlRaw("SELECT ERROR_CODE, ERROR_DESC FROM SFIS1.C_ERROR_CODE_T");

                // Tìm kiếm gần đúng nếu có term
                if (!string.IsNullOrEmpty(term))
                {
                    query = (IQueryable<ErrorCode>)query.Where(e =>
                        EF.Functions.Like(e.ERROR_CODE, $"%{term}%")
                    );
                }

                var totalCount = await query.CountAsync();
                var errorCodes = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                Console.WriteLine($"Số lượng mã lỗi được lấy: {errorCodes.Count}");

                return Ok(new
                {
                    success = true,
                    errorCodes = errorCodes.Select(e => new
                    {
                        e.ERROR_CODE,
                        ERROR_DESC = e.ERROR_DESC
                    }),
                    totalCount,
                    page,
                    pageSize
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi khi lấy mã lỗi: {ex}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Lỗi khi lấy danh sách Error Codes",
                    error = ex.Message,
                    stackTrace = ex.StackTrace
                });
            }
        }
    }
}