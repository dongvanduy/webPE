using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using API_WEB.ModelsDB;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace API_WEB.Controllers.BGA
{
    [Route("bga-replace")]
    [ApiController]
    public class ReplaceBgaController : ControllerBase
    {
        private readonly CSDL_NE _sqlContext;

        private static readonly Dictionary<int, int> _linearNextStatusMap = new()
        {
            { 10, 11 },
            { 11, 12 },
            { 12, 13 },
            { 13, 14 },
            { 14, 15 },
            { 15, 16 },
            { 18, 19 }
        };

        private static readonly Dictionary<int, string> _statusDescriptions = new()
        {
            { 4, "Waiting approve replace BGA" },
            { 10, "Check in barking" },
            { 11, "Check out barking" },
            { 12, "VI after barking" },
            { 13, "Remove BGA, clean pad" },
            { 14, "Check VI BGA pad" },
            { 15, "Replace BGA" },
            { 16, "Xray" },
            { 17, "ICT, FT" },
            { 18, "Waiting return PD line ok" },
            { 19, "Return PD line ok" }
        };

        private static string GetStatusName(int status)
        {
            return _statusDescriptions.TryGetValue(status, out var name)
                ? name
                : status.ToString();
        }

        public ReplaceBgaController(CSDL_NE sqlContext)
        {
            _sqlContext = sqlContext;
        }

        private IQueryable<ScrapList> QueryBgaScrapLists()
        {
            return _sqlContext.ScrapLists
                .AsNoTracking()
                .Where(s => s.Category == null || EF.Functions.Like(s.Category, "%BGA%"));
        }

        private IQueryable<HistoryScrapList> QueryBgaHistory()
        {
            return _sqlContext.HistoryScrapLists
                .AsNoTracking()
                .Where(h => h.Category == null || EF.Functions.Like(h.Category, "%BGA%"));
        }

        private static List<string> NormalizeSerialNumbers(IEnumerable<string>? serialNumbers)
        {
            return serialNumbers?
                .Where(sn => !string.IsNullOrWhiteSpace(sn))
                .Select(sn => sn.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<string>();
        }

        private static bool IsBgaCategory(string? category)
        {
            return !string.IsNullOrWhiteSpace(category) && category.Contains("BGA", StringComparison.OrdinalIgnoreCase);
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetByStatus([FromQuery] int status)
        {
            if (!_statusDescriptions.ContainsKey(status))
            {
                return BadRequest(new { message = "Trạng thái không hợp lệ." });
            }

            var records = await QueryBgaScrapLists()
                .Where(s => s.ApplyTaskStatus == status)
                .OrderBy(s => s.SN)
                .Select(s => new
                {
                    s.SN,
                    s.TaskNumber,
                    s.InternalTask,
                    s.Desc,
                    s.ApplyTaskStatus,
                    s.FindBoardStatus,
                    s.ApproveScrapperson,
                    s.ApplyTime
                })
                .ToListAsync();

            if (!records.Any())
            {
                return NotFound(new { message = "Không có dữ liệu cho trạng thái được yêu cầu." });
            }

            var response = records
                .Select(record => new
                {
                    record.SN,
                    record.TaskNumber,
                    record.InternalTask,
                    record.Desc,
                    record.ApplyTaskStatus,
                    StatusName = GetStatusName(record.ApplyTaskStatus),
                    record.FindBoardStatus,
                    record.ApproveScrapperson,
                    record.ApplyTime
                })
                .ToList();

            return Ok(response);
        }

        [HttpGet("dashboard/status-summary")]
        public async Task<IActionResult> GetStatusSummary()
        {
            var summary = await QueryBgaScrapLists()
                .Where(s => s.ApplyTaskStatus != 4)
                .GroupBy(s => s.ApplyTaskStatus)
                .Select(g => new
                {
                    Status = g.Key,
                    Count = g.Count()
                })
                .OrderBy(item => item.Status)
                .ToListAsync();

            var response = summary
                .Select(item => new
                {
                    item.Status,
                    item.Count,
                    StatusName = GetStatusName(item.Status)
                })
                .ToList();

            return Ok(response);
        }

        [HttpGet("dashboard/barking-aging")]
        public async Task<IActionResult> GetBarkingAging()
        {
            var now = DateTime.Now;

            var records = await _sqlContext.ScrapLists
                .AsNoTracking()
                .Where(s => s.ApplyTaskStatus == 11)
                .Select(s => new
                {
                    s.SN,
                    s.ApplyTime
                })
                .ToListAsync();

            var payload = records
                .Select(record =>
                {
                    double? hours = null;
                    double? minutes = null;

                    if (record.ApplyTime.HasValue)
                    {
                        var elapsed = now - record.ApplyTime.Value;
                        if (elapsed.TotalMinutes < 0)
                        {
                            elapsed = TimeSpan.Zero;
                        }

                        hours = Math.Round(elapsed.TotalHours, 2);
                        minutes = Math.Round(elapsed.TotalMinutes, 0);
                    }

                    return new
                    {
                        sn = record.SN,
                        applyTime = record.ApplyTime,
                        hours,
                        minutes
                    };
                })
                .OrderByDescending(item => item.applyTime ?? DateTime.MinValue)
                .ToList();

            return Ok(payload);
        }

        [HttpPost("status/bulk")]
        public async Task<IActionResult> GetStatusForList([FromBody] BulkSnRequest request)
        {
            var normalizedSNs = NormalizeSerialNumbers(request?.SNs);
            if (!normalizedSNs.Any())
            {
                return BadRequest(new { message = "Danh sách SN không hợp lệ." });
            }

                var scrapRecords = await _sqlContext.ScrapLists
                    .AsNoTracking()
                    .Where(s => normalizedSNs.Contains(s.SN))
                    .Select(s => new
                    {
                        s.SN,
                        s.TaskNumber,
                        s.InternalTask,
                        s.Desc,
                        s.ApplyTaskStatus,
                        s.FindBoardStatus,
                        s.ApproveScrapperson,
                        s.ApplyTime,
                        s.CreateTime,
                        s.Category,
                        s.Remark
                    })
                    .ToListAsync();

            var scrapLookup = scrapRecords
                .GroupBy(s => s.SN, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                var historyRecords = await QueryBgaHistory()
                    .Where(h => normalizedSNs.Contains(h.SN))
                    .Select(h => new
                    {
                        h.SN,
                        h.TaskNumber,
                        h.InternalTask,
                        h.Desc,
                        h.ApplyTaskStatus,
                        h.FindBoardStatus,
                        h.ApproveScrapperson,
                        h.ApplyTime,
                        h.CreateTime,
                        h.Category,
                        h.Remark
                    })
                    .ToListAsync();

            var historyLookup = historyRecords
                .GroupBy(h => h.SN, StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    Key = g.Key,
                    Value = g
                        .OrderByDescending(item => item.ApplyTime ?? item.CreateTime)
                        .FirstOrDefault()
                })
                .Where(item => item.Value != null)
                .ToDictionary(item => item.Key, item => item.Value!, StringComparer.OrdinalIgnoreCase);

            var result = new List<object>();

            foreach (var sn in normalizedSNs)
            {
                if (scrapLookup.TryGetValue(sn, out var record))
                {
                    var inBgaCategory = string.IsNullOrWhiteSpace(record.Category) || IsBgaCategory(record.Category);
                    var waitingApproval = record.ApplyTaskStatus == 4;
                    var isValid = inBgaCategory && !waitingApproval;
                    string? message = null;

                    if (!inBgaCategory)
                    {
                        message = "SN không thuộc Replace BGA.";
                    }
                    else if (waitingApproval)
                    {
                        message = "SN đang chờ approve replace BGA tại Scrap view.";
                    }

                    result.Add(new
                    {
                        sn = record.SN,
                        record.TaskNumber,
                        record.InternalTask,
                        record.Desc,
                        record.ApplyTaskStatus,
                        statusName = GetStatusName(record.ApplyTaskStatus),
                        record.FindBoardStatus,
                        record.ApproveScrapperson,
                        record.ApplyTime,
                        record.CreateTime,
                        record.Category,
                        record.Remark,
                        isValid,
                        message,
                        source = "Current"
                    });

                    continue;
                }

                if (historyLookup.TryGetValue(sn, out var history))
                {
                    var inBgaCategory = string.IsNullOrWhiteSpace(history.Category) || IsBgaCategory(history.Category);
                    string? message = inBgaCategory ? null : "SN không thuộc Replace BGA.";

                    result.Add(new
                    {
                        sn = history.SN,
                        history.TaskNumber,
                        history.InternalTask,
                        history.Desc,
                        history.ApplyTaskStatus,
                        statusName = GetStatusName(history.ApplyTaskStatus),
                        history.FindBoardStatus,
                        history.ApproveScrapperson,
                        history.ApplyTime,
                        history.CreateTime,
                        history.Category,
                        history.Remark,
                        isValid = inBgaCategory,
                        message,
                        source = "History"
                    });

                    continue;
                }

                result.Add(new
                {
                    sn,
                    TaskNumber = (string?)null,
                    InternalTask = (string?)null,
                    Desc = (string?)null,
                    ApplyTaskStatus = (int?)null,
                    statusName = (string?)null,
                    FindBoardStatus = (string?)null,
                    ApproveScrapperson = (string?)null,
                    ApplyTime = (DateTime?)null,
                    CreateTime = (DateTime?)null,
                    Category = (string?)null,
                    Remark = (string?)null,
                    isValid = false,
                    message = "Không tìm thấy SN trong Replace BGA.",
                    source = (string?)null
                });
            }

            return Ok(result);
        }

        [HttpPost("history")]
        public async Task<IActionResult> GetHistory([FromBody] BulkSnRequest request)
        {
            var normalizedSNs = NormalizeSerialNumbers(request?.SNs);
            if (!normalizedSNs.Any())
            {
                return BadRequest(new { message = "Danh sách SN không hợp lệ." });
            }

            var histories = await QueryBgaHistory()
                .Where(h => normalizedSNs.Contains(h.SN))
                .OrderBy(h => h.SN)
                .ThenByDescending(h => h.ApplyTime ?? h.CreateTime)
                .Select(h => new
                {
                    h.SN,
                    h.TaskNumber,
                    h.InternalTask,
                    h.Desc,
                    h.ApplyTaskStatus,
                    h.FindBoardStatus,
                    h.ApproveScrapperson,
                    h.ApplyTime,
                    h.CreateTime,
                    h.Category,
                    h.Remark
                })
                .ToListAsync();

            var currentRecords = await QueryBgaScrapLists()
                .Where(s => normalizedSNs.Contains(s.SN))
                .Select(s => new
                {
                    s.SN,
                    s.TaskNumber,
                    s.InternalTask,
                    s.Desc,
                    s.ApplyTaskStatus,
                    s.FindBoardStatus,
                    s.ApproveScrapperson,
                    s.ApplyTime,
                    s.CreateTime,
                    s.Category,
                    s.Remark
                })
                .ToListAsync();

            var response = histories
                .Select(history => new
                {
                    history.SN,
                    history.TaskNumber,
                    history.InternalTask,
                    history.Desc,
                    history.ApplyTaskStatus,
                    StatusName = GetStatusName(history.ApplyTaskStatus),
                    history.FindBoardStatus,
                    history.ApproveScrapperson,
                    history.ApplyTime,
                    history.CreateTime,
                    history.Category,
                    history.Remark,
                    Source = "History"
                })
                .Concat(currentRecords.Select(record => new
                {
                    record.SN,
                    record.TaskNumber,
                    record.InternalTask,
                    record.Desc,
                    record.ApplyTaskStatus,
                    StatusName = GetStatusName(record.ApplyTaskStatus),
                    record.FindBoardStatus,
                    record.ApproveScrapperson,
                    record.ApplyTime,
                    record.CreateTime,
                    record.Category,
                    record.Remark,
                    Source = "Current"
                }))
                .OrderBy(item => item.SN)
                .ThenByDescending(item => item.ApplyTime ?? item.CreateTime)
                .ToList();

            return Ok(response);
        }

        [HttpPost("update-status")]
        public async Task<IActionResult> UpdateStatus([FromBody] UpdateBgaStatusRequest request)
        {
            if (request == null || request.SNs == null || !request.SNs.Any())
            {
                return BadRequest(new { message = "Danh sách SN không được để trống." });
            }

            if (!_statusDescriptions.ContainsKey(request.CurrentStatus) || request.CurrentStatus == 4 || request.CurrentStatus == 19)
            {
                return BadRequest(new { message = "Trạng thái hiện tại không hợp lệ." });
            }

            if (string.IsNullOrWhiteSpace(request.Remark))
            {
                return BadRequest(new { message = "Remark không được để trống." });
            }

            request.Remark = request.Remark.Trim();
            if (request.Remark.Length > 50)
            {
                return BadRequest(new { message = "Remark không được dài quá 50 ký tự." });
            }

            var normalizedSNs = request.SNs
                .Where(sn => !string.IsNullOrWhiteSpace(sn))
                .Select(sn => sn.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!normalizedSNs.Any())
            {
                return BadRequest(new { message = "Danh sách SN không hợp lệ." });
            }

            var scrapRecords = await _sqlContext.ScrapLists
                .Where(s => normalizedSNs.Contains(s.SN))
                .ToListAsync();

            var nonBgaRecords = scrapRecords
                .Where(record => !string.IsNullOrWhiteSpace(record?.Category) && !IsBgaCategory(record?.Category))
                .Select(record => record?.SN)
                .Where(sn => !string.IsNullOrEmpty(sn))
                .Cast<string>()
                .ToList();

            if (nonBgaRecords.Any())
            {
                return BadRequest(new
                {
                    message = "Một số SN không thuộc Replace BGA.",
                    details = nonBgaRecords
                });
            }

            var missingSNs = normalizedSNs
                .Except(scrapRecords.Select(s => s.SN), StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (missingSNs.Any())
            {
                return NotFound(new { message = $"Không tìm thấy các SN: {string.Join(", ", missingSNs)}." });
            }

            var invalidStatusSNs = scrapRecords
                .Where(s => s.ApplyTaskStatus != request.CurrentStatus)
                .Select(s => $"{s.SN} (trạng thái hiện tại: {s.ApplyTaskStatus})")
                .ToList();

            if (invalidStatusSNs.Any())
            {
                return BadRequest(new
                {
                    message = "Một số SN không ở trạng thái yêu cầu để cập nhật.",
                    details = invalidStatusSNs
                });
            }

            int nextStatus;
            if (request.CurrentStatus == 16 || request.CurrentStatus == 17)
            {
                var decision = string.IsNullOrWhiteSpace(request.Decision)
                    ? request.XrayResult
                    : request.Decision;

                if (string.IsNullOrWhiteSpace(decision))
                {
                    var message = request.CurrentStatus == 16
                        ? "Vui lòng chọn kết quả Xray (OK hoặc NG)."
                        : "Vui lòng chọn kết quả ICT/FT (OK hoặc NG).";
                    return BadRequest(new { message });
                }

                var normalizedResult = decision.Trim().ToUpperInvariant();
                if (normalizedResult == "OK")
                {
                    nextStatus = request.CurrentStatus == 16 ? 17 : 18;
                }
                else if (normalizedResult == "NG")
                {
                    nextStatus = 13;
                }
                else
                {
                    return BadRequest(new { message = "Giá trị kết quả chỉ được phép là OK hoặc NG." });
                }
            }
            else
            {
                if (!_linearNextStatusMap.TryGetValue(request.CurrentStatus, out nextStatus))
                {
                    return BadRequest(new { message = "Không xác định được trạng thái tiếp theo." });
                }
            }

            var updateTime = DateTime.Now;

            foreach (var record in scrapRecords)
            {
                record.ApplyTaskStatus = nextStatus;
                record.FindBoardStatus = request.Remark;
                if (record.CreateTime == default)
                {
                    record.CreateTime = updateTime;
                }
                record.ApplyTime = updateTime;
            }

            await AddHistoryEntriesAsync(scrapRecords);
            await _sqlContext.SaveChangesAsync();

            var nextStatusName = _statusDescriptions.ContainsKey(nextStatus)
                ? _statusDescriptions[nextStatus]
                : nextStatus.ToString();

            return Ok(new
            {
                message = $"Cập nhật thành công {scrapRecords.Count} SN sang trạng thái '{nextStatusName}'.",
                nextStatus,
                nextStatusName
            });
        }

        private async Task AddHistoryEntriesAsync(IEnumerable<ScrapList> records, string action = "update")
        {
            if (records == null)
            {
                return;
            }

            var historyEntries = records
                .Where(record => record != null)
                .Select(record =>
                {
                    var appliedAt = record.ApplyTime ?? record.CreateTime;
                    if (appliedAt == default)
                    {
                        appliedAt = DateTime.Now;
                    }

                    var createdAt = record.CreateTime == default ? appliedAt : record.CreateTime;

                    return new HistoryScrapList
                    {
                        SN = record.SN,
                        KanBanStatus = record.KanBanStatus,
                        Sloc = record.Sloc,
                        TaskNumber = record.TaskNumber,
                        PO = record.PO,
                        CreatedBy = record.CreatedBy,
                        Cost = record.Cost,
                        InternalTask = record.InternalTask,
                        Desc = record.Desc,
                        CreateTime = createdAt,
                        ApproveScrapperson = record.ApproveScrapperson,
                        ApplyTaskStatus = record.ApplyTaskStatus,
                        FindBoardStatus = record.FindBoardStatus,
                        Remark = record.Remark,
                        Purpose = record.Purpose,
                        Category = record.Category,
                        ApplyTime = appliedAt,
                        SpeApproveTime = record.SpeApproveTime,
                        Action = action
                    };
                })
                .ToList();

            if (!historyEntries.Any())
            {
                return;
            }

            await _sqlContext.HistoryScrapLists.AddRangeAsync(historyEntries);
        }
    }

    public class UpdateBgaStatusRequest
    {
        public List<string> SNs { get; set; } = new();
        public int CurrentStatus { get; set; }
        public string? Remark { get; set; }
        public string? Decision { get; set; }
        public string? XrayResult { get; set; }
    }

    public class BulkSnRequest
    {
        public List<string>? SNs { get; set; }
    }
}

