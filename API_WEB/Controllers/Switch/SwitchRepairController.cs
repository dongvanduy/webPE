using API_WEB.ModelsDB;
using API_WEB.ModelsOracle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using Oracle.ManagedDataAccess.Client;
using System.Data;

namespace API_WEB.Controllers.Scrap
{
    [Route("[controller]")]
    [ApiController]
    public class SwitchRepairController : ControllerBase
    {
        private readonly CSDL_NE _sqlContext;
        private readonly OracleDbContext _oracleContext;

        public SwitchRepairController(CSDL_NE sqlContext, OracleDbContext oracleContext)
        {
            _sqlContext = sqlContext;
            _oracleContext = oracleContext;
        }


        private async Task<Dictionary<string, (string? WipGroup, string? ModelName)>> FetchR107Async(IReadOnlyCollection<string> serialNumbers)
        {
            var results = new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase);
            if (serialNumbers == null || serialNumbers.Count == 0) return results;

            var serialList = serialNumbers.ToList();
            var connection = _oracleContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open) await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            var paramNames = serialList.Select((_, i) => $":sn{i}").ToList();

            command.CommandText = $@"
        SELECT SERIAL_NUMBER, WIP_GROUP, MODEL_NAME
        FROM SFISM4.R107
        WHERE SERIAL_NUMBER IN ({string.Join(",", paramNames)})";

            for (var i = 0; i < serialList.Count; i++)
                command.Parameters.Add(new OracleParameter($"sn{i}", OracleDbType.Varchar2) { Value = serialList[i] });

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var sn = reader["SERIAL_NUMBER"]?.ToString();
                if (string.IsNullOrWhiteSpace(sn)) continue;

                var wip = reader["WIP_GROUP"]?.ToString();
                var model = reader["MODEL_NAME"]?.ToString();
                results[sn] = (wip, model);
            }

            return results;
        }

        private async Task<Dictionary<string, (string? ErrorCode, string? ErrorDesc)>> FetchLatestR109Async(IReadOnlyCollection<string> serialNumbers)
        {
            var results = new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase);
            if (serialNumbers == null || serialNumbers.Count == 0) return results;

            var serialList = serialNumbers.ToList();
            var connection = _oracleContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open) await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            var paramNames = serialList.Select((_, i) => $":sn{i}").ToList();

            // TODO: đổi tên cột DESC_CODE đúng theo schema thật của R109 (nếu có)
            command.CommandText = $@"SELECT x.SERIAL_NUMBER,
                       x.TEST_CODE AS ERROR_CODE,
                       x.ERROR_DESC
                FROM (
                    SELECT r109.SERIAL_NUMBER,
                           r109.TEST_CODE,
                           b.ERROR_DESC,
                           r109.TEST_TIME,
                           ROW_NUMBER() OVER (PARTITION BY r109.SERIAL_NUMBER ORDER BY r109.TEST_TIME DESC) rn
                    FROM SFISM4.R109 r109
                    inner join SFIS1.C_ERROR_CODE_T b on r109.test_code = b.ERROR_code
                    WHERE r109.SERIAL_NUMBER IN ({string.Join(",", paramNames)})
                ) x
                WHERE x.rn = 1";

            for (var i = 0; i < serialList.Count; i++)
                command.Parameters.Add(new OracleParameter($"sn{i}", OracleDbType.Varchar2) { Value = serialList[i] });

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var sn = reader["SERIAL_NUMBER"]?.ToString();
                if (string.IsNullOrWhiteSpace(sn)) continue;

                var ec = reader["ERROR_CODE"]?.ToString();
                var ed = reader["ERROR_DESC"]?.ToString();
                results[sn] = (ec, ed);
            }

            return results;
        }

        [HttpPost("sw-input")]
        public async Task<IActionResult> CreateAnalysisHistory([FromBody] SwitchAnalysisHistoryRequest request)
        {
            if (request?.Items == null || !request.Items.Any())
                return BadRequest(new { message = "Danh sách SN không được để trống." });

            var normalized = request.Items
                .Where(x => !string.IsNullOrWhiteSpace(x.SerialNumber))
                .Select(x => new SwitchAnalysisHistoryItemRequest
                {
                    SerialNumber = x.SerialNumber.Trim(),
                    EnterErrorCode = x.EnterErrorCode?.Trim(),
                    Fa = x.Fa?.Trim(),
                    Status = x.Status?.Trim(),
                    OwnerPE = x.OwnerPE?.Trim(),
                    Customer = x.Customer?.Trim(),
                    FailStation = x.FailStation?.Trim(),
                })
                .ToList();

            if (!normalized.Any())
                return BadRequest(new { message = "Danh sách SN không hợp lệ." });

            var serials = normalized.Select(x => x.SerialNumber)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var r107 = await FetchR107Async(serials);
            var r109 = await FetchLatestR109Async(serials);
            var now = DateTime.Now;

            var entities = normalized.Select(item =>
            {
                r107.TryGetValue(item.SerialNumber, out var r107Info);
                r109.TryGetValue(item.SerialNumber, out var r109Info);

                return new SwitchRepair
                {
                    SerialNumber = item.SerialNumber,

                    // user nhập
                    EnterErrorCode = item.EnterErrorCode,
                    Fa = item.Fa,
                    Status = item.Status,
                    Owner = item.OwnerPE,
                    CustomerOwner = item.Customer,
                    FailStation = item.FailStation,

                    // oracle
                    ErrorCode = r109Info.ErrorCode,
                    ErrorDesc = r109Info.ErrorDesc,
                    WipGroup = r107Info.WipGroup,
                    ModelName = r107Info.ModelName,

                    TimeUpdate = now
                };
            }).ToList();

            await _sqlContext.SwitchRepairs.AddRangeAsync(entities);
            await _sqlContext.SaveChangesAsync();

            var missingR107 = serials.Where(sn => !r107.ContainsKey(sn)).ToList();
            var missingR109 = serials.Where(sn => !r109.ContainsKey(sn)).ToList();

            var data = entities.Select(e => new
            {
                e.SerialNumber,
                e.EnterErrorCode,
                e.Fa,
                e.Owner,
                e.CustomerOwner,
                e.FailStation,
                e.Status,
                e.ErrorCode,
                e.WipGroup,
                e.ModelName,
                e.ErrorDesc,
                e.TimeUpdate
            });

            return Ok(new { message = "Đã ghi nhận lịch sử phân tích.", data, missingR107, missingR109 });
        }


        [HttpPost("sw-search")]
        public async Task<IActionResult> Search([FromBody] SwitchSearchRequest request)
        {
            var serials = request?.SerialNumbers?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new();
            var mode = request?.SearchMode?.Trim().ToLowerInvariant() ?? "current";
            var isHistory = mode == "history";

            if (serials.Count == 0)
                return BadRequest(new { message = "Danh sách SN không được để trống." });

            if (isHistory)
            {
                var historyLocal = await _sqlContext.SwitchRepairs
                    .Where(x => serials.Contains(x.SerialNumber))
                    .OrderByDescending(x => x.TimeUpdate)
                    .ToListAsync();

                var results = historyLocal.Select(local => new
                {
                    SerialNumber = local.SerialNumber,
                    EnterErrorCode = local.EnterErrorCode ?? "",
                    Fa = local.Fa ?? "",
                    OwnerPE = local.Owner ?? "",
                    Customer = local.CustomerOwner ?? "",
                    FailStation = local.FailStation ?? "",
                    Status = local.Status ?? "",
                    ErrorCode = local.ErrorCode ?? "",
                    WipGroup = local.WipGroup ?? "",
                    ModelName = local.ModelName ?? "",
                    ErrorDesc = local.ErrorDesc ?? "",
                    TimeUpdate = local.TimeUpdate
                });

                return Ok(new { message = "OK", data = results, missingR107 = new List<string>(), missingR109 = new List<string>() });
            }

            // lấy latest theo TimeUpdate từ SQL
            var latestLocal = await _sqlContext.SwitchRepairs
                .Where(x => serials.Contains(x.SerialNumber))
                .GroupBy(x => x.SerialNumber)
                .Select(g => g.OrderByDescending(x => x.TimeUpdate).FirstOrDefault())
                .ToListAsync();

            var localDict = latestLocal
                .Where(x => x != null)
                .ToDictionary(x => x!.SerialNumber, x => x!, StringComparer.OrdinalIgnoreCase);

            // refresh oracle để trả đúng ErrorCode/Desc/Wip/Model hiện tại
            var r107 = await FetchR107Async(serials);
            var r109 = await FetchLatestR109Async(serials);

            var resultsLatest = serials.Select(sn =>
            {
                localDict.TryGetValue(sn, out var local);
                r107.TryGetValue(sn, out var r107Info);
                r109.TryGetValue(sn, out var r109Info);

                return new
                {
                    SerialNumber = sn,

                    // user nhập (từ local nếu đã từng lưu)
                    EnterErrorCode = local?.EnterErrorCode ?? "",
                    Fa = local?.Fa ?? "",
                    OwnerPE = local?.Owner ?? "",
                    Customer = local?.CustomerOwner ?? "",
                    FailStation = local?.FailStation ?? "",
                    Status = local?.Status ?? "",

                    // oracle (fresh)
                    ErrorCode = r109Info.ErrorCode ?? "",
                    WipGroup = r107Info.WipGroup ?? "",
                    ModelName = r107Info.ModelName ?? "",
                    ErrorDesc = r109Info.ErrorDesc ?? "",

                    TimeUpdate = local?.TimeUpdate
                };
            });

            var missingR107 = serials.Where(sn => !r107.ContainsKey(sn)).ToList();
            var missingR109 = serials.Where(sn => !r109.ContainsKey(sn)).ToList();

            return Ok(new { message = "OK", data = resultsLatest, missingR107, missingR109 });
        }

        [HttpGet("export-excel")]
        public async Task<IActionResult> ExportExcel()
        {
            var data = await _sqlContext.SwitchRepairs
                .AsNoTracking()
                .OrderBy(x => x.SerialNumber)
                .ToListAsync();

            if (data.Count == 0)
            {
                return NotFound(new { message = "Không có dữ liệu để xuất." });
            }

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("SwitchRepairs");

            ws.Cells[1, 1].Value = "SERIAL_NUMBER";
            ws.Cells[1, 2].Value = "MODEL_NAME";
            ws.Cells[1, 3].Value = "FAIL_STATION";
            ws.Cells[1, 4].Value = "ENTER_ERROR_CODE";
            ws.Cells[1, 5].Value = "WIP_GROUP";
            ws.Cells[1, 6].Value = "ERROR_CODE";
            ws.Cells[1, 7].Value = "ERROR_DESC";
            ws.Cells[1, 8].Value = "FA";
            ws.Cells[1, 9].Value = "STATUS";
            ws.Cells[1, 10].Value = "OWNER";
            ws.Cells[1, 11].Value = "CUSTOMER_OWNER";
            ws.Cells[1, 12].Value = "TIME_UPDATE";

            for (var i = 0; i < data.Count; i++)
            {
                var row = i + 2;
                var item = data[i];
                ws.Cells[row, 1].Value = item.SerialNumber;
                ws.Cells[row, 2].Value = item.ModelName;
                ws.Cells[row, 3].Value = item.FailStation;
                ws.Cells[row, 4].Value = item.EnterErrorCode;
                ws.Cells[row, 5].Value = item.WipGroup;
                ws.Cells[row, 6].Value = item.ErrorCode;
                ws.Cells[row, 7].Value = item.ErrorDesc;
                ws.Cells[row, 8].Value = item.Fa;
                ws.Cells[row, 9].Value = item.Status;
                ws.Cells[row, 10].Value = item.Owner;
                ws.Cells[row, 11].Value = item.CustomerOwner;
                ws.Cells[row, 12].Value = item.TimeUpdate.ToString("yyyy-MM-dd HH:mm:ss");
            }

            ws.Cells.AutoFitColumns();

            var fileBytes = package.GetAsByteArray();
            var fileName = $"SwitchRepair_{DateTime.Now:yyyyMMddHHmmss}.xlsx";
            return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

    }

    public class SwitchAnalysisHistoryRequest
    {
        public List<SwitchAnalysisHistoryItemRequest> Items { get; set; } = new();
    }

    public class SwitchAnalysisHistoryItemRequest
    {
        public string SerialNumber { get; set; } = string.Empty;

        // user nhập / excel
        public string? EnterErrorCode { get; set; }
        public string? Fa { get; set; }
        public string? Status { get; set; }
        public string? OwnerPE { get; set; }
        public string? Customer { get; set; }
        public string? FailStation { get; set; }
    }

    public class SwitchSearchRequest
    {
        public List<string> SerialNumbers { get; set; } = new();
        public string? SearchMode { get; set; }
    }

}
