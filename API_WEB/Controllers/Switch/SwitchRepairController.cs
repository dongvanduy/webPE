using API_WEB.ModelsDB;
using API_WEB.ModelsOracle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

        private async Task<Dictionary<string, string?>> FetchWipGroupsAsync(IReadOnlyCollection<string> serialNumbers)
        {
            var results = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            if (serialNumbers == null || serialNumbers.Count == 0)
                return results;

            var serialList = serialNumbers.ToList();
            var connection = _oracleContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            var paramNames = serialList.Select((_, index) => $":sn{index}").ToList();
            command.CommandText = $"SELECT SERIAL_NUMBER, WIP_GROUP FROM SFISM4.R107 WHERE SERIAL_NUMBER IN ({string.Join(",", paramNames)})";

            for (var i = 0; i < serialList.Count; i++)
            {
                command.Parameters.Add(new OracleParameter($"sn{i}", OracleDbType.Varchar2) { Value = serialList[i] });
            }

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var serial = reader["SERIAL_NUMBER"]?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(serial))
                    continue;

                results[serial] = reader["WIP_GROUP"]?.ToString();
            }

            return results;
        }

        private async Task<Dictionary<string, (string? CurrentErrorCode, string? ErrorDesc)>> FetchLatestR109Async(IReadOnlyCollection<string> serialNumbers)
        {
            var results = new Dictionary<string, (string? CurrentErrorCode, string? ErrorDesc)>(StringComparer.OrdinalIgnoreCase);

            if (serialNumbers == null || serialNumbers.Count == 0)
                return results;

            var serialList = serialNumbers.ToList();
            var connection = _oracleContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            var paramNames = serialList.Select((_, index) => $":sn{index}").ToList();

            command.CommandText = $@"
                SELECT r109_latest.SERIAL_NUMBER,
                       r109_latest.TEST_CODE AS CURRENT_ERROR_CODE,
                       r109_latest.ERROR_DESC
                FROM (
                    SELECT r109.SERIAL_NUMBER,
                           r109.TEST_CODE,
                           r109.ERROR_DESC,
                           r109.TEST_TIME,
                           ROW_NUMBER() OVER (PARTITION BY r109.SERIAL_NUMBER ORDER BY r109.TEST_TIME DESC) AS rn
                    FROM SFISM4.R109 r109
                    WHERE r109.SERIAL_NUMBER IN ({string.Join(",", paramNames)})
                ) r109_latest
                WHERE r109_latest.rn = 1";

            for (var i = 0; i < serialList.Count; i++)
            {
                command.Parameters.Add(new OracleParameter($"sn{i}", OracleDbType.Varchar2) { Value = serialList[i] });
            }

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var serial = reader["SERIAL_NUMBER"]?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(serial))
                    continue;

                var currentErrorCode = reader["CURRENT_ERROR_CODE"]?.ToString();
                var errorDesc = reader["ERROR_DESC"]?.ToString();

                results[serial] = (currentErrorCode, errorDesc);
            }

            return results;
        }

        [HttpPost("analysis-history")]
        public async Task<IActionResult> CreateAnalysisHistory([FromBody] SwitchAnalysisHistoryRequest request)
        {
            try
            {
                if (request?.Items == null || !request.Items.Any())
                {
                    return BadRequest(new { message = "Danh sách SN không được để trống." });
                }

                var normalizedItems = request.Items
                    .Where(item => !string.IsNullOrWhiteSpace(item.SerialNumber))
                    .Select(item => new SwitchAnalysisHistoryItemRequest
                    {
                        SerialNumber = item.SerialNumber.Trim(),
                        ErrorCode = item.ErrorCode?.Trim(),
                        Fa = item.Fa?.Trim(),
                        Status = item.Status?.Trim(),
                        Owner = item.Owner?.Trim(),
                        CustomerOwner = item.CustomerOwner?.Trim()
                    })
                    .ToList();

                if (!normalizedItems.Any())
                {
                    return BadRequest(new { message = "Danh sách SN không hợp lệ." });
                }

                var serialNumbers = normalizedItems
                    .Select(item => item.SerialNumber)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var wipGroups = await FetchWipGroupsAsync(serialNumbers);
                var r109Data = await FetchLatestR109Async(serialNumbers);
                var now = DateTime.Now;

                var historyEntries = normalizedItems.Select(item =>
                {
                    r109Data.TryGetValue(item.SerialNumber, out var r109Info);
                    wipGroups.TryGetValue(item.SerialNumber, out var wipGroup);

                    return new SwitchRepair
                    {
                        SerialNumber = item.SerialNumber,
                        ErrorCode = item.ErrorCode,
                        WipGroup = wipGroup,
                        CurrentErrorCode = r109Info.CurrentErrorCode,
                        ErrorDesc = r109Info.ErrorDesc,
                        Fa = item.Fa,
                        Status = item.Status,
                        Owner = item.Owner,
                        CustomerOwner = item.CustomerOwner,
                        TimeUpdate = now
                    };
                }).ToList();

                await _sqlContext.SwitchRepairs.AddRangeAsync(historyEntries);
                await _sqlContext.SaveChangesAsync();

                var missingR109 = serialNumbers
                    .Where(sn => !r109Data.ContainsKey(sn))
                    .ToList();

                var responseData = historyEntries.Select(entry => new
                {
                    entry.SerialNumber,
                    entry.ErrorCode,
                    entry.WipGroup,
                    entry.CurrentErrorCode,
                    entry.ErrorDesc,
                    entry.Fa,
                    entry.Status,
                    entry.Owner,
                    entry.CustomerOwner,
                    entry.TimeUpdate
                });

                return Ok(new
                {
                    message = "Đã ghi nhận lịch sử phân tích.",
                    data = responseData,
                    missingR109
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi lưu dữ liệu.", error = ex.Message });
            }
        }
    }

    public class SwitchAnalysisHistoryRequest
    {
        public List<SwitchAnalysisHistoryItemRequest> Items { get; set; } = new List<SwitchAnalysisHistoryItemRequest>();
    }

    public class SwitchAnalysisHistoryItemRequest
    {
        public string SerialNumber { get; set; } = string.Empty;
        public string? ErrorCode { get; set; }
        public string? Fa { get; set; }
        public string? Status { get; set; }
        public string? Owner { get; set; }
        public string? CustomerOwner { get; set; }
    }
}
