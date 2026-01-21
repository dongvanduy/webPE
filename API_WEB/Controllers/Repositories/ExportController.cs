using API_WEB.Helpers.Repositories;
using API_WEB.ModelsDB;
using API_WEB.ModelsOracle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

namespace API_WEB.Controllers.Repositories
{
    [Route("[controller]")]
    [ApiController]
    public class ExportController : ControllerBase
    {
        private readonly CSDL_NE _sqlContext;
        private readonly OracleDbContext _oracleContext;
        private readonly HttpClient _httpClient;

        public ExportController(CSDL_NE sqlContext, OracleDbContext oracleContext, HttpClient httpClient)
        {
            _sqlContext = sqlContext ?? throw new ArgumentNullException(nameof(sqlContext));
            _oracleContext = oracleContext ?? throw new ArgumentNullException(nameof(oracleContext));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        private class SnInfoResponse
        {
            public bool success { get; set; }
            public string? productLine { get; set; }
            public string? modelName { get; set; }
        }

        [HttpPost("ExportSerialNumber")]
        public async Task<IActionResult> ExportSN([FromBody] RequestExport request)
        {
            if (request?.SerialNumbers == null || request.SerialNumbers.Count == 0)
            {
                return BadRequest(new { success = false, message = "SerialNumber is empty!" });
            }

            var sns = request.SerialNumbers
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (sns.Count == 0)
                return BadRequest(new { success = false, message = "SerialNumber is empty after normalization!" });

            using var tx = await _sqlContext.Database.BeginTransactionAsync();

            try
            {
                // Lấy các bản ghi có liên quan
                var products = await _sqlContext.Products.Where(p => sns.Contains(p.SerialNumber)).ToListAsync();
                var khoOks = await _sqlContext.KhoOks.Where(o => sns.Contains(o.SERIAL_NUMBER)).ToListAsync();
                var khoScraps = await _sqlContext.KhoScraps.Where(s => sns.Contains(s.SERIAL_NUMBER)).ToListAsync();

                // Lấy thêm thông tin Serial từ Oracle
                var connectionString = _oracleContext.Database.GetDbConnection().ConnectionString;
                var snInfoMap = await InfoSerialNumberHelper.GetBatchSNInfoAsync(sns, connectionString);

                // XÓA method GetBatchSNInfoAsync cũ!
                var productBySn = products
                    .GroupBy(p => p.SerialNumber, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                var exportEntries = new List<Export>(sns.Count);

                foreach (var sn in sns)
                {
                    productBySn.TryGetValue(sn, out var p);
                    snInfoMap.TryGetValue(sn, out var info);

                    exportEntries.Add(new Export
                    {
                        SerialNumber = sn,
                        ExportDate = DateTime.Now,
                        ExportPerson = request.ExportPerson,
                        ProductLine = !string.IsNullOrEmpty(info.ProductLine)
                            ? info.ProductLine
                            : (p?.ProductLine ?? string.Empty),
                        EntryDate = p?.EntryDate,
                        ModelName = !string.IsNullOrEmpty(info.ModelName)
                            ? info.ModelName
                            : (p?.ModelName ?? string.Empty),
                        CheckingB36R = 1
                    });
                }

                // Xoá dữ liệu khỏi các bảng liên quan
                if (products.Count > 0) _sqlContext.Products.RemoveRange(products);
                if (khoOks.Count > 0) _sqlContext.KhoOks.RemoveRange(khoOks);
                if (khoScraps.Count > 0) _sqlContext.KhoScraps.RemoveRange(khoScraps);

                // Thêm bản ghi Export
                await _sqlContext.Exports.AddRangeAsync(exportEntries);
                await _sqlContext.SaveChangesAsync();
                await tx.CommitAsync();


                //Goi API remove location (DATA18) = ''
                try
                {
                    await RemoveLocationHelper.SendReceivingStatusAsync(sns, request.ExportPerson ?? string.Empty, null, "Xuất(Kho Ok)", _oracleContext);
                    Console.WriteLine($"[ExportSN] Đã gọi API receiving-status cho {sns.Count} serials.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ExportSN] Lỗi khi gọi RepairStatus API: {ex.Message}");
                }

                // Trả về kết quả
                var data = exportEntries.Select(e => new
                {
                    e.Id,
                    e.SerialNumber,
                    e.ExportDate,
                    e.ExportPerson,
                    e.ProductLine,
                    e.EntryPerson,
                    e.CheckingB36R,
                    e.ModelName,
                    e.EntryDate
                });

                return Ok(new
                {
                    success = true,
                    totalRequested = sns.Count,
                    deleted = new { products = products.Count, khoOk = khoOks.Count, khoScrap = khoScraps.Count },
                    data
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                Console.WriteLine($"[ExportSN] ERROR: {ex.Message}");
                return StatusCode(500, new { success = false, message = "Đã xảy ra lỗi hệ thống." });
            }
        }

        [HttpPost("UpdateMissingInfo")]
        public async Task<IActionResult> UpdateMissingInfo()
        {
            var exports = await _sqlContext.Exports
                .Where(e => string.IsNullOrEmpty(e.ProductLine) || string.IsNullOrEmpty(e.ModelName))
                .ToListAsync();

            if (exports.Count == 0)
            {
                return Ok(new { success = true, updated = 0 });
            }

            var connectipnString = _oracleContext.Database.GetDbConnection().ConnectionString;
            var serialNumbers = exports.Select(e => e.SerialNumber).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var snInfoMap = await InfoSerialNumberHelper.GetBatchSNInfoAsync(serialNumbers, connectipnString);

            var updated = 0;

            foreach (var export in exports)
            {
                if (snInfoMap.TryGetValue(export.SerialNumber, out var info))
                {
                    if (string.IsNullOrEmpty(export.ModelName))
                        export.ModelName = info.ModelName;

                    if (string.IsNullOrEmpty(export.ProductLine))
                        export.ProductLine = info.ProductLine;

                    updated++;

                }
            }
            await _sqlContext.SaveChangesAsync();
            return Ok(new { success = true, updated });
        }


        [HttpGet]
        public async Task<IActionResult> Get([
            FromQuery] string? serialNumber,
            [FromQuery] List<string>? serialNumbers)
        {
            var baseQuery = _sqlContext.Exports
                .AsNoTracking()
                .Where(e => e.CheckingB36R != null);

            if (serialNumbers != null && serialNumbers.Count > 0)
            {
                var sns = serialNumbers
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (sns.Count > 0)
                {
                    baseQuery = baseQuery.Where(e => sns.Contains(e.SerialNumber));
                }
            }
            else if (!string.IsNullOrWhiteSpace(serialNumber))
            {
                baseQuery = baseQuery.Where(e =>
                    EF.Functions.Like(e.SerialNumber, $"%{serialNumber}%"));
            }

            // chỉ giữ bản ghi có ExportDate mới nhất theo từng SerialNumber
            var latestPerSnQuery = baseQuery.Where(e =>
                e.ExportDate ==
                _sqlContext.Exports
                    .Where(x => x.SerialNumber == e.SerialNumber && x.CheckingB36R != null)
                    .Max(x => x.ExportDate)
            )
            // nếu có nhiều bản ghi cùng ExportDate, chọn bản có Id lớn nhất
            .Where(e =>
                e.Id ==
                _sqlContext.Exports
                    .Where(x => x.SerialNumber == e.SerialNumber
                                && x.CheckingB36R != null
                                && x.ExportDate == e.ExportDate)
                    .Max(x => x.Id)
            );

            var data = await latestPerSnQuery
                .OrderByDescending(e => e.ExportDate)
                .ThenByDescending(e => e.Id)
                .Take(100)
                .ToListAsync();

            return Ok(new { success = true, data });
        }


        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var export = await _sqlContext.Exports.FindAsync(id);
            if (export == null)
            {
                return NotFound(new { success = false, message = "Export not found" });
            }

            _sqlContext.Exports.Remove(export);
            await _sqlContext.SaveChangesAsync();
            return Ok(new { success = true });
        }

        public class RequestExport
        {
            public string? ExportPerson { get; set; }
            public List<string>? SerialNumbers { get; set; }
        }
    }
}
