using API_WEB.ModelsDB;
using API_WEB.ModelsOracle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Oracle.ManagedDataAccess.Client;
using System.Linq;
using System.Text;

namespace API_WEB.Controllers.Repositories
{
    [Route("[controller]")]
    [ApiController]
    public class LocationSyncController : ControllerBase
    {
        private readonly CSDL_NE _sqlContext;
        private readonly OracleDbContext _oracleContext;
        private readonly IHttpClientFactory _httpClientFactory;

        public LocationSyncController(CSDL_NE sqlContext, OracleDbContext oracleContext, IHttpClientFactory httpClientFactory)
        {
            _sqlContext = sqlContext;
            _oracleContext = oracleContext;
            _httpClientFactory = httpClientFactory;
        }

        public class UpdateLocationRequest
        {
            public List<string> SerialNumbers { get; set; } = new();
            public string? Tag { get; set; } // optional: cho phép custom tag
        }

        // ===========================
        // RESET DATA18 VỀ NULL (qua API receiving-status)
        // ===========================
        [HttpPost("reset-data18")]
        public async Task<IActionResult> ResetData18([FromBody] UpdateLocationRequest? request)
        {
            try
            {
                // 1) Xác định danh sách SN cần reset
                List<string> targetSNs;

                if (request?.SerialNumbers != null && request.SerialNumbers.Any())
                {
                    targetSNs = request.SerialNumbers
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                else
                {
                    // Không truyền SN -> tự lấy các SN có DATA18 IS NOT NULL từ Oracle
                    targetSNs = await GetSerialNumbersWithData18Async();
                }

                if (targetSNs.Count == 0)
                {
                    return Ok(new { success = true, totalProcessed = 0, results = Array.Empty<object>() });
                }

                // 2) Gọi API receiving-status để reset (location="" , owner="")
                var tag = string.IsNullOrWhiteSpace(request?.Tag) ? "Reset(DATA18=NULL)" : request!.Tag!.Trim();

                const int CHUNK = 200; // gửi theo lô để nhẹ server
                var results = new List<object>();

                for (int i = 0; i < targetSNs.Count; i += CHUNK)
                {
                    var chunk = targetSNs.Skip(i).Take(CHUNK).ToList();
                    try
                    {
                        await SendReceivingStatusAsync(chunk, owner: string.Empty, location: string.Empty, tag: tag);
                        results.Add(new
                        {
                            success = true,
                            count = chunk.Count,
                            serialNumbers = chunk
                        });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new
                        {
                            success = false,
                            count = chunk.Count,
                            serialNumbers = chunk,
                            error = ex.Message
                        });
                    }
                }

                return Ok(new
                {
                    success = true,
                    totalProcessed = targetSNs.Count,
                    batches = results.Count,
                    results
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Xảy ra lỗi khi reset DATA18 qua receiving-status.", error = ex.Message });
            }
        }

        // Lấy danh sách SN có DATA18 IS NOT NULL từ Oracle (đọc-only)
        private async Task<List<string>> GetSerialNumbersWithData18Async()
        {
            var sns = new List<string>();
            await using var conn = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);
            await conn.OpenAsync();

            const string sql = @"
                SELECT DISTINCT SERIAL_NUMBER
                FROM SFISM4.R_REPAIR_TASK_T
                WHERE DATA18 IS NOT NULL AND DATA18 LIKE '%-%'";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var sn = reader["SERIAL_NUMBER"]?.ToString();
                if (!string.IsNullOrWhiteSpace(sn)) sns.Add(sn.Trim());
            }
            return sns;
        }
        private async Task SendReceivingStatusAsync(IEnumerable<string> serialNumbers, string owner, string location, string tag)
        {
            if (serialNumbers == null) return;

            var cleanedSerials = serialNumbers
                .Where(sn => !string.IsNullOrWhiteSpace(sn))
                .Select(sn => sn.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (cleanedSerials.Count == 0) return;

            var serialsWithLocation = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await using var conn = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);
            await conn.OpenAsync();

            const int oracleParameterLimit = 900; // tránh vượt quá giới hạn 1000 phần tử của Oracle
            for (var offset = 0; offset < cleanedSerials.Count; offset += oracleParameterLimit)
            {
                var batch = cleanedSerials.Skip(offset).Take(oracleParameterLimit).ToList();
                if (batch.Count == 0) continue;

                var parameterNames = batch
                    .Select((_, index) => $"p{offset + index}")
                    .ToArray();
                var query = $@"
                SELECT SERIAL_NUMBER
                  FROM SFISM4.R_REPAIR_TASK_T
                 WHERE DATA18 IS NOT NULL
                   AND TRIM(DATA18) <> ''
                   AND SERIAL_NUMBER IN ({string.Join(",", parameterNames.Select(name => $":{name}"))})";
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = query;

                for (var i = 0; i < batch.Count; i++)
                {
                    cmd.Parameters.Add(new OracleParameter(parameterNames[i], batch[i]));
                }

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var sn = reader["SERIAL_NUMBER"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(sn))
                    {
                        serialsWithLocation.Add(sn.Trim());
                    }
                }
            }

            if (serialsWithLocation.Count == 0) return;

            var client = _httpClientFactory.CreateClient();
            var payload = new
            {
                serialnumbers = string.Join(",", serialsWithLocation),
                owner,
                location,
                tag
            };

            var json = JsonConvert.SerializeObject(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var resp = await client.PostAsync("https://sfc-portal.cns.myfiinet.com:443/sfcSmartRepair/api/receiving_status", content);
            resp.EnsureSuccessStatusCode();
        }
    }
}
