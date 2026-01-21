using API_WEB.ModelsDB;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using static API_WEB.Controllers.MaterialSystem.MaterialSystemController;

namespace API_WEB.Controllers.App
{
    [Route("[controller]")]
    [ApiController]
    public class ScanController : ControllerBase
    {
        private readonly CSDL_NE _sqlContext;

        public ScanController(CSDL_NE sqlContext)
        {
            _sqlContext = sqlContext;
        }
        [HttpPost("scanSN")]
        public async Task<IActionResult> SaveSerialNumber([FromBody] SerialNumberScanRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.SerialNumber))
            {
                return BadRequest(new { message = "SerialNumber is required." });
            }

            string sn = request.SerialNumber.Trim().ToUpper();

            // Chỉ cho phép SN có 12 hoặc 14 ký tự
            if (sn.Length != 12 && sn.Length != 14)
            {
                return BadRequest(new { message = "SerialNumber must be 12 or 14 characters." });
            }

            // Tìm SN trong DB
            var existingLog = await _sqlContext.ScanLogs.FindAsync(sn);

            if (existingLog != null)
            {
                // Nếu đã có thì update thời gian
                existingLog.CreatedAt = DateTime.Now;
                _sqlContext.ScanLogs.Update(existingLog);
                await _sqlContext.SaveChangesAsync();
                return Ok(new { message = "Serial number updated successfully." });
            }
            else
            {
                // Nếu chưa có thì insert mới
                var log = new ScanLog
                {
                    SerialNumber = sn,
                    CreatedAt = DateTime.Now
                };
                _sqlContext.ScanLogs.Add(log);
                await _sqlContext.SaveChangesAsync();
                return Ok(new { message = "Serial number saved successfully." });
            }
        }

        [HttpPost("SearchBySN")]
        public async Task<IActionResult> SearchBySN([FromBody] SerialNumberListScanRequest request)
        {
            try
            {
                // Kiểm tra request và danh sách serial number
                if (request?.SerialNumber == null || !request.SerialNumber.Any())
                {
                    return BadRequest(new { message = "SerialNumber list is required and cannot be empty." });
                }

                // Tìm các bản ghi có SerialNumber nằm trong danh sách request.SerialNumber
                var sumMaterials = await _sqlContext.ScanLogs
                    .Where(sm => request.SerialNumber.Contains(sm.SerialNumber))
                    .ToListAsync();

                if (!sumMaterials.Any())
                {
                    return NotFound(new { message = "No records found for the given SerialNumbers." });
                }

                return Ok(sumMaterials);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while searching for materials.", error = ex.Message });
            }
        }

        [HttpGet("count")]
        public async Task<IActionResult> CountByCreatedAt([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            if (from.HasValue && to.HasValue && from > to)
                return BadRequest(new { message = "`from` phải ≤ `to`" });

            var query = _sqlContext.ScanLogs.AsNoTracking().AsQueryable();

            if (from.HasValue)
                query = query.Where(x => x.CreatedAt >= from.Value);

            // dùng khoảng nửa mở [from, to) để tránh double count theo mili giây
            if (to.HasValue)
                query = query.Where(x => x.CreatedAt < to.Value);

            var count = await query.CountAsync();
            return Ok(new { count });
        }

        [HttpGet("found-count")]
        public async Task<IActionResult> CountFoundItems([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            if (from.HasValue && to.HasValue && from > to)
                return BadRequest(new { message = "`from` phải ≤ `to`" });

            var query = _sqlContext.SearchListItems.AsNoTracking()
                .Where(i => i.IsFound && i.SerialNumber != null)
                .Join(_sqlContext.ScanLogs.AsNoTracking(),
                    item => item.SerialNumber!,
                    log => log.SerialNumber,
                    (item, log) => new { item, log })
                .Join(_sqlContext.SearchLists.AsNoTracking(),
                    itemLog => itemLog.item.SearchListId,
                    list => list.Id,
                    (itemLog, list) => new
                    {
                        SerialNumber = itemLog.item.SerialNumber!,
                        ListName = list.ListName,
                        ScanTime = itemLog.log.CreatedAt
                    })
                .AsQueryable();

            if (from.HasValue)
                query = query.Where(x => x.ScanTime >= from.Value);

            if (to.HasValue)
                query = query.Where(x => x.ScanTime < to.Value);

            var items = await query
                .OrderBy(x => x.ScanTime)
                .ToListAsync();

            var count = items.Count;
            return Ok(new { count, items });
        }

        [HttpGet("search-lists/details")]
        public async Task<IActionResult> GetSearchListsWithDetails()
        {
            var searchLists = await _sqlContext.SearchLists
                .AsNoTracking()
                .Select(list => new
                {
                    list.Id,
                    ListName = list.ListName ?? string.Empty,
                    list.CreatedAt,
                    list.CreatedBy
                })
                .OrderBy(list => list.ListName)
                .ToListAsync();

            var scanLogs = await _sqlContext.ScanLogs
                .AsNoTracking()
                .Select(log => new { log.SerialNumber, log.CreatedAt })
                .ToListAsync();

            var scanLogLookup = scanLogs
                .ToDictionary(log => log.SerialNumber, log => log.CreatedAt, StringComparer.OrdinalIgnoreCase);

            var searchListItems = await _sqlContext.SearchListItems
                .AsNoTracking()
                .ToListAsync();

            var itemsLookup = searchListItems
                .GroupBy(item => item.SearchListId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(item =>
                    {
                        var serial = item.SerialNumber?.Trim();
                        DateTime? scanTime = null;
                        if (!string.IsNullOrWhiteSpace(serial) &&
                            scanLogLookup.TryGetValue(serial, out var foundAt))
                        {
                            scanTime = foundAt;
                        }

                        return new SearchListItemDetailDto
                        {
                            Id = item.Id,
                            SerialNumber = serial,
                            IsFound = item.IsFound,
                            ScanTime = scanTime
                        };
                    })
                    .OrderBy(detail => detail.SerialNumber ?? string.Empty)
                    .ThenBy(detail => detail.Id)
                    .ToList());

            var result = new List<SearchListDetailDto>(searchLists.Count);

            foreach (var list in searchLists)
            {
                if (!itemsLookup.TryGetValue(list.Id, out var listItems))
                {
                    listItems = new List<SearchListItemDetailDto>();
                }

                result.Add(new SearchListDetailDto
                {
                    SearchListId = list.Id,
                    ListName = list.ListName,
                    CreatedAt = list.CreatedAt,
                    CreatedBy = list.CreatedBy,
                    TotalItems = listItems.Count,
                    FoundItems = listItems.Count(item => item.IsFound),
                    Items = listItems
                });
            }

            return Ok(new { totalLists = result.Count, lists = result });
        }


        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to)
        {
            if (from.HasValue && to.HasValue && from > to)
                return BadRequest(new { message = "`from` phải ≤ `to`" });

            // ===== Query ScanLogs =====
            var scanQuery = _sqlContext.ScanLogs.AsNoTracking().AsQueryable();

            if (from.HasValue)
                scanQuery = scanQuery.Where(x => x.CreatedAt >= from.Value.LocalDateTime);

            if (to.HasValue)
                scanQuery = scanQuery.Where(x => x.CreatedAt < to.Value.LocalDateTime);

            var scanCount = await scanQuery.CountAsync();

            var scannedItems = await scanQuery
                .OrderBy(x => x.CreatedAt)
                .Select(x => new
                {
                    x.SerialNumber,
                    ScanTime = x.CreatedAt
                })
                .ToListAsync();

            // ===== Query Found Items =====
            var foundQuery = _sqlContext.SearchListItems.AsNoTracking()
                .Where(i => i.IsFound && i.SerialNumber != null)
                .Join(_sqlContext.ScanLogs.AsNoTracking(),
                    item => item.SerialNumber!,
                    log => log.SerialNumber,
                    (item, log) => new { item, log })
                .Join(_sqlContext.SearchLists.AsNoTracking(),
                    itemLog => itemLog.item.SearchListId,
                    list => list.Id,
                    (itemLog, list) => new
                    {
                        SerialNumber = itemLog.item.SerialNumber!,
                        ListName = list.ListName,
                        ScanTime = itemLog.log.CreatedAt
                    })
                .AsQueryable();

            if (from.HasValue)
                foundQuery = foundQuery.Where(x => x.ScanTime >= from.Value.LocalDateTime);

            if (to.HasValue)
                foundQuery = foundQuery.Where(x => x.ScanTime < to.Value.LocalDateTime);

            var foundItems = await foundQuery
                .OrderBy(x => x.ScanTime)
                .ToListAsync();

            var foundCount = foundItems.Count;

            // ===== Trả về chung =====
            return Ok(new
            {
                scanCount,
                scannedItems,
                foundCount,
                foundItems
            });
        }


        [HttpGet("export-excel")]
        public async Task<IActionResult> ExportAll()
        {
            var logs = await _sqlContext.ScanLogs
                .AsNoTracking()
                .OrderBy(l => l.CreatedAt)
                .ToListAsync();

            if (logs == null || logs.Count == 0)
            {
                return NotFound(new { message = "No data to export." });
            }

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("ScanLogs");
            ws.Cells[1, 1].Value = "SerialNumber";
            ws.Cells[1, 2].Value = "CreatedAt";

            for (int i = 0; i < logs.Count; i++)
            {
                ws.Cells[i + 2, 1].Value = logs[i].SerialNumber;
                ws.Cells[i + 2, 2].Value = logs[i].CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
            }

            ws.Cells.AutoFitColumns();

            var fileBytes = package.GetAsByteArray();
            var fileName = $"ScanLogs_{DateTime.Now:yyyyMMddHHmmss}.xlsx";
            return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
        public class SearchListDetailDto
        {
            public int SearchListId { get; set; }
            public string ListName { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public string? CreatedBy { get; set; }
            public int TotalItems { get; set; }
            public int FoundItems { get; set; }
            public List<SearchListItemDetailDto> Items { get; set; } = new();
        }
        public class SearchListItemDetailDto
        {
            public int Id { get; set; }
            public string? SerialNumber { get; set; }
            public bool IsFound { get; set; }
            public DateTime? ScanTime { get; set; }
        }
        public class SerialNumberScanRequest
        {
            public string SerialNumber { get; set; } = string.Empty;
        }
        public class SerialNumberListScanRequest
        {
            public List<string> SerialNumber { get; set; }
        }
    }
}