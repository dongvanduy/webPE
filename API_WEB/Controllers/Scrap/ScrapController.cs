using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using API_WEB.ModelsDB;
using API_WEB.ModelsOracle;
using System.Net.Http;
using System.Text.Json;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Oracle.ManagedDataAccess.Client;
using System.Globalization;
using System.IO;
using OfficeOpenXml;

namespace API_WEB.Controllers.Scrap
{
    [Route("[controller]")]
    [ApiController]
    public class ScrapController : ControllerBase
    {
        private readonly CSDL_NE _sqlContext;
        private readonly OracleDbContext _oracleContext;
        private readonly HttpClient _httpClient;

        public ScrapController(CSDL_NE sqlContext, OracleDbContext oracleContext, IHttpClientFactory httpClientFactory)
        {
            _sqlContext = sqlContext;
            _oracleContext = oracleContext;

            // Cấu hình HttpClient với HttpClientHandler để bỏ qua chứng chỉ SSL (chỉ dùng trong môi trường test)
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true // Bỏ qua kiểm tra chứng chỉ
            };
            _httpClient = new HttpClient(handler);
            _httpClient.BaseAddress = new Uri("https://sfc-portal.cns.myfiinet.com/SfcSmartRepair/");
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        //Thêm SN vao HistoryScrapList
        private async Task AddHistoryEntriesAsync(IEnumerable<ScrapList> records, string action = "update")
        {
            if (records == null)
                return;

            var historyEntries = new List<HistoryScrapList>();

            foreach (var record in records)
            {
                if (record == null)
                    continue;

                // ⭐ MUST: Lấy thời gian chính xác
                var appliedAt = record.ApplyTime ?? record.CreateTime;
                if (appliedAt == default)
                    appliedAt = DateTime.Now;

                var createdAt = record.CreateTime == default ? appliedAt : record.CreateTime;

                //LẤY MODEL_NAME + MODEL_SERIAL CHO TỪNG SN
                var (modelName, modelSerial) = await GetInforAsync(record.SN);

                //Tạo entry HistoryScrapList
                var historyEntry = new HistoryScrapList
                {
                    SN = record.SN,
                    KanBanStatus = record.KanBanStatus,
                    ModelName = modelName,
                    ModelType = modelSerial,
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

                historyEntries.Add(historyEntry);
            }

            if (historyEntries.Any())
            {
                await _sqlContext.HistoryScrapLists.AddRangeAsync(historyEntries);
            }
        }

        [HttpPost("upload-tasknumber-po")]
        public async Task<IActionResult> UploadTaskNumberPo(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { message = "File không hợp lệ." });
            }

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            using var package = new ExcelPackage(stream);
            var worksheet = package.Workbook.Worksheets.FirstOrDefault();

            if (worksheet == null)
            {
                return BadRequest(new { message = "Không tìm thấy sheet trong file Excel." });
            }

            var headerMap = BuildHeaderMap(worksheet);

            if (!headerMap.TryGetValue("serialnumber", out var serialCol)
                || !headerMap.TryGetValue("tasknumber", out var taskCol)
                || !headerMap.TryGetValue("po", out var poCol))
            {
                return BadRequest(new { message = "File Excel phải có các cột SerialNumber, TaskNumber và PO." });
            }

            var rowCount = worksheet.Dimension.Rows;
            var updates = new List<TaskPoUpdateRow>();

            for (var row = 2; row <= rowCount; row++)
            {
                var serialNumber = worksheet.Cells[row, serialCol].Text.Trim();
                if (string.IsNullOrEmpty(serialNumber))
                {
                    continue;
                }

                var taskNumber = worksheet.Cells[row, taskCol].Text.Trim();
                var po = worksheet.Cells[row, poCol].Text.Trim();

                if (string.IsNullOrEmpty(taskNumber) && string.IsNullOrEmpty(po))
                {
                    continue;
                }

                updates.Add(new TaskPoUpdateRow
                {
                    RowIndex = row,
                    SerialNumber = serialNumber,
                    TaskNumber = string.IsNullOrEmpty(taskNumber) ? null : taskNumber,
                    PO = string.IsNullOrEmpty(po) ? null : po
                });
            }

            if (!updates.Any())
            {
                return BadRequest(new { message = "Không có dữ liệu hợp lệ để cập nhật." });
            }

            var serialNumbers = updates.Select(u => u.SerialNumber).Distinct().ToList();
            var scrapRecords = await _sqlContext.ScrapLists
                .Where(s => serialNumbers.Contains(s.SN))
                .ToListAsync();

            var scrapDict = scrapRecords.ToDictionary(s => s.SN, StringComparer.OrdinalIgnoreCase);
            var missingSerials = serialNumbers.Where(sn => !scrapDict.ContainsKey(sn)).ToList();

            if (missingSerials.Any())
            {
                return BadRequest(new { message = $"Các SerialNumber sau không tồn tại trong ScrapList: {string.Join(", ", missingSerials)}" });
            }

            var updatedRecords = new List<ScrapList>();

            foreach (var update in updates)
            {
                if (!scrapDict.TryGetValue(update.SerialNumber, out var record))
                {
                    continue;
                }

                var isUpdated = false;

                if (!string.IsNullOrEmpty(update.TaskNumber))
                {
                    record.TaskNumber = update.TaskNumber;
                    isUpdated = true;
                }

                if (!string.IsNullOrEmpty(update.PO))
                {
                    record.PO = update.PO;
                    isUpdated = true;
                }

                if (isUpdated)
                {
                    updatedRecords.Add(record);
                }
            }

            if (!updatedRecords.Any())
            {
                return BadRequest(new { message = "Không có bản ghi nào được cập nhật." });
            }

            await AddHistoryEntriesAsync(updatedRecords, "update-task-po");
            await _sqlContext.SaveChangesAsync();

            return Ok(new
            {
                message = "Cập nhật thành công TaskNumber/PO.",
                updated = updatedRecords.Count
            });
        }

        private static Dictionary<string, int> BuildHeaderMap(ExcelWorksheet worksheet)
        {
            var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var columnCount = worksheet.Dimension.Columns;

            for (var col = 1; col <= columnCount; col++)
            {
                var header = NormalizeHeader(worksheet.Cells[1, col].Text);
                if (string.IsNullOrEmpty(header))
                {
                    continue;
                }

                if (header == "sn")
                {
                    header = "serialnumber";
                }
                else if (header is "serial" or "serialno" or "serialnum")
                {
                    header = "serialnumber";
                }
                else if (header is "task" or "taskno")
                {
                    header = "tasknumber";
                }
                else if (header is "purchaseorder" or "ponumber" or "pono")
                {
                    header = "po";
                }

                if (!headerMap.ContainsKey(header))
                {
                    headerMap.Add(header, col);
                }
            }

            return headerMap;
        }

        private static string NormalizeHeader(string header)
        {
            return string.IsNullOrWhiteSpace(header)
                ? string.Empty
                : header.Trim().Replace(" ", "").Replace("_", "").ToLowerInvariant();
        }

        private class TaskPoUpdateRow
        {
            public int RowIndex { get; set; }
            public string SerialNumber { get; set; } = string.Empty;
            public string? TaskNumber { get; set; }
            public string? PO { get; set; }
        }

        // API INPUT-SN ADAPTER
        [HttpPost("input-sn")]
        public async Task<IActionResult> InputSN([FromBody] InputSNRequest request)
        {
            try
            {
                // Kiểm tra dữ liệu đầu vào
                if (request == null || request.SNs == null || !request.SNs.Any())
                {
                    return BadRequest(new { message = "Danh sách SN không được để trống." });
                }

                var snList = request.SNs.Select(sn => sn?.Trim()).ToList();
                if (snList.Any(string.IsNullOrEmpty))
                {
                    return BadRequest(new { message = "SN không được để trống." });
                }

                var duplicateSNs = snList.GroupBy(sn => sn)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();

                if (duplicateSNs.Any())
                {
                    return BadRequest(new { message = $"Danh sách SN bị trùng lặp: {string.Join(", ", duplicateSNs)}" });
                }


                if (string.IsNullOrEmpty(request.CreatedBy) || string.IsNullOrEmpty(request.ApproveScrapPerson) || string.IsNullOrEmpty(request.SpeApproveTime))
                {
                    return BadRequest(new { message = "CreatedBy , ApproveScrapPerson và SpeAproveTime  không được để trống." });
                }

                // Kiểm tra Purpose hợp lệ
                if (string.IsNullOrEmpty(request.Purpose) || (request.Purpose != "0" && request.Purpose != "1" && request.Purpose != "2" && request.Purpose != "3" && request.Purpose != "4"))
                {
                    return BadRequest(new { message = "Purpose phải là '0','1','2','3' hoặc '4'." });
                }

                // Kiểm tra độ dài các trường
                if (snList.Any(sn => sn.Length > 50))
                {
                    return BadRequest(new { message = "SN không được dài quá 50 ký tự." });
                }

                if (request.CreatedBy.Length > 50 || request.ApproveScrapPerson.Length > 50 || (request.Description != null && request.Description.Length > 100))
                {
                    return BadRequest(new { message = "CreatedBy, Description, và ApproveScrapPerson không được dài quá 50 ký tự." });
                }

                // Kiểm tra danh sách SN trong bảng ScrapList
                var existingSNs = await _sqlContext.ScrapLists
                    .Where(s => snList.Contains(s.SN))
                    .ToListAsync();

                // Tìm các SN không tồn tại trong bảng ScrapList
                var nonExistingSNs = request.SNs.Except(existingSNs.Select(s => s.SN)).ToList();
                if (nonExistingSNs.Any())
                {
                    return BadRequest(new { message = $"Các SN sau không tồn tại trong bảng ScrapList: {string.Join(", ", nonExistingSNs)}" });
                }

                // Kiểm tra trạng thái ApplyTaskStatus của các SN
                var rejectedSNs = new List<string>();
                var validSNs = new List<ScrapList>();

                foreach (var sn in existingSNs)
                {
                    if (sn.ApplyTaskStatus == 0)
                    {
                        rejectedSNs.Add($"{sn.SN} (SPE đã đồng ý phế, chờ xin task)");
                    }
                    else if (sn.ApplyTaskStatus == 1)
                    {
                        rejectedSNs.Add($"{sn.SN} (đang xin task, chờ NV gửi task)");
                    }
                    else if (sn.ApplyTaskStatus == 3)
                    {
                        rejectedSNs.Add($"{sn.SN} (NV đã approved thay BGA)");
                    }
                    else if (sn.ApplyTaskStatus == 5)
                    {
                        rejectedSNs.Add($"{sn.SN} (Đã có task, chờ chuyển MRB)");
                    }
                    else if (sn.ApplyTaskStatus == 6)
                    {
                        rejectedSNs.Add($"{sn.SN} (Đã chuyển kho phế, chờ MRB xác nhận)");
                    }
                    else if (sn.ApplyTaskStatus == 7)
                    {
                        rejectedSNs.Add($"{sn.SN} (Đã chuyển kho phế thành công)");
                    }
                    else if (sn.ApplyTaskStatus == 2 || sn.ApplyTaskStatus == 4)
                    {
                        validSNs.Add(sn); // SN hợp lệ để cập nhật
                    }
                    else
                    {
                        rejectedSNs.Add($"{sn.SN} (trạng thái không xác định: {sn.ApplyTaskStatus})");
                    }
                }

                if (rejectedSNs.Any())
                {
                    return BadRequest(new { message = $"Các SN sau không hợp lệ để cập nhật: {string.Join(", ", rejectedSNs)}" });
                }

                if (!validSNs.Any())
                {
                    return BadRequest(new { message = "Không có SN nào hợp lệ để cập nhật (yêu cầu ApplyTaskStatus = 2 & 4)." });
                }


                // Tạo danh sách SN để gửi đến API bên thứ ba
                var serialNumbers = string.Join(",", request.SNs);
                Console.WriteLine($"Sending t_serial_numbers to external API: {serialNumbers}");

                // Gọi API bên thứ ba để lấy dữ liệu AfterBeforeKanban
                var externalRequest = new
                {
                    type = "task_system",
                    t_serial_numbers = serialNumbers
                };

                HttpResponseMessage externalResponse;
                List<ExternalApiResponse> externalData = null;

                try
                {
                    externalResponse = await _httpClient.PostAsJsonAsync("api/query", externalRequest);
                    externalResponse.EnsureSuccessStatusCode();
                    var responseContent = await externalResponse.Content.ReadAsStringAsync();

                    externalData = JsonSerializer.Deserialize<List<ExternalApiResponse>>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch (HttpRequestException ex)
                {
                    // Log chi tiết lỗi khi gọi API bên thứ ba
                    return StatusCode(500, new { message = "Không thể kết nối đến API bên thứ ba.", error = ex.Message, innerException = ex.InnerException?.Message });
                }

                // Xác định giá trị cho Purpose, Category, và Sloc dựa trên đầu vào Purpose
                string purposeValue;
                switch (request.Purpose)
                {
                    case "0":
                        purposeValue = "SPE approve to scrap";
                        break;
                    case "1":
                        purposeValue = "Scrap to quarterly";
                        break;
                    case "2":
                        purposeValue = "Approved to engineer sample";
                        break;
                    case "3":
                        purposeValue = "Approved to master board";
                        break;
                    case "4":
                        purposeValue = "SPE approve to BGA";
                        break;
                    default:
                        purposeValue = "Unknown"; // Giá trị mặc định nếu Purpose không hợp lệ
                        break;
                }

                string categoryValue;
                switch (request.Purpose)
                {
                    case "0":
                    case "1":
                        categoryValue = "Scrap";
                        break;
                    case "2":
                        categoryValue = "Engineer sample"; // Sửa lỗi chính tả từ "Enginerr sample"
                        break;
                    case "3":
                        categoryValue = "Master board";
                        break;
                    case "4":
                        categoryValue = "BGA";
                        break;
                    default:
                        categoryValue = "Unknown"; // Giá trị mặc định nếu Purpose không hợp lệ
                        break;
                }

                string slocValue;
                switch (request.Purpose)
                {
                    case "0":
                    case "1":
                    case "2":
                    case "3":
                        slocValue = "FXVZ";
                        break;
                    case "4":
                        slocValue = "FXV7";
                        break;
                    default:
                        slocValue = "Unknown";
                        break;
                }

                int ApplyTaskStatusValue;
                switch (request.Purpose)
                {
                    case "0":
                    case "1":
                    case "2":
                    case "3":
                        ApplyTaskStatusValue = 0;
                        break;
                    case "4":
                        ApplyTaskStatusValue = 10;
                        break;
                    default:
                        ApplyTaskStatusValue = 0; // Giá trị mặc định nếu Purpose không hợp lệ
                        break;
                }
                // Tạo InternalTask mới (đảm bảo không trùng)
                string newInternalTask = await GenerateUniqueInternalTask();

                // Lưu ý các SN không tìm thấy dữ liệu từ API bên thứ ba
                var unmatchedSNs = new List<string>();

                // Cập nhật các bản ghi hợp lệ trong bảng ScrapList
                var updateTime = DateTime.Now;

                foreach (var scrapEntry in validSNs)
                {
                    // Tìm dữ liệu từ API bên thứ ba tương ứng với SN
                    var normalizedSN = scrapEntry.SN?.Trim();
                    var (modelName, modelSerial) = await GetInforAsync(scrapEntry.SN);

                    var externalInfo = externalData?.FirstOrDefault(e =>
                    {
                        var normalizedBoardSN = e.BoardSN?.Trim();
                        Console.WriteLine($"Comparing: sn='{normalizedSN}', external.BoardSN='{normalizedBoardSN}'");
                        return normalizedBoardSN == normalizedSN;
                    });

                    string kanBanStatus = "unknown"; // Giá trị mặc định nếu không tìm thấy dữ liệu
                    if (externalInfo == null)
                    {
                        Console.WriteLine($"No matching data found for SN: {normalizedSN}");
                        unmatchedSNs.Add(normalizedSN);
                    }
                    else
                    {
                        kanBanStatus = externalInfo?.AfterBeforeKanban ?? "unknown";
                        Console.WriteLine($"Matched data for SN: {normalizedSN}, KanBanStatus: {kanBanStatus}");
                    }

                    // Cập nhật các trường của bản ghi
                    scrapEntry.KanBanStatus = kanBanStatus; // Lấy từ API bên thứ ba
                    scrapEntry.Sloc = slocValue; // Dựa trên Purpose
                    scrapEntry.TaskNumber = "N/A"; // Để trống
                    scrapEntry.PO = "N/A"; // Để trống
                    scrapEntry.Cost = "N/A"; // Không cho phép NULL, để trống dưới dạng chuỗi rỗng
                    scrapEntry.ModelName = modelName;
                    scrapEntry.ModelType = modelSerial;
                    scrapEntry.CreatedBy = request.CreatedBy;
                    scrapEntry.Desc = request.Description ?? ""; // Không cho phép NULL, dùng chuỗi rỗng nếu Description là null
                    if (scrapEntry.CreateTime == default)
                    {
                        scrapEntry.CreateTime = updateTime;
                    }
                    scrapEntry.ApplyTime = updateTime; // Thời gian thay đổi trạng thái
                    scrapEntry.ApproveScrapperson = request.ApproveScrapPerson;
                    scrapEntry.ApplyTaskStatus = ApplyTaskStatusValue;
                    scrapEntry.FindBoardStatus = "chưa tìm thấy"; // Mặc định
                    scrapEntry.InternalTask = newInternalTask; // Gán InternalTask chung cho tất cả SN
                    scrapEntry.Purpose = purposeValue; // Dựa trên Purpose
                    scrapEntry.Category = categoryValue; // Dựa trên Purpose
                    scrapEntry.SpeApproveTime = request.SpeApproveTime; // Dựa trên SpeAproveTime
                }

                // Thông báo nếu có SN không tìm thấy dữ liệu từ API bên thứ ba
                string message = "Cập nhật danh sách SN thành công.";
                if (unmatchedSNs.Any())
                {
                    message += $" Tuy nhiên, không tìm thấy dữ liệu từ API bên thứ ba cho các SN: {string.Join(", ", unmatchedSNs)}";
                }

                // Lưu thay đổi vào bảng ScrapList
                await AddHistoryEntriesAsync(validSNs, "input");
                await _sqlContext.SaveChangesAsync();

                return Ok(new { message, internalTask = newInternalTask });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi cập nhật dữ liệu.", error = ex.Message });
            }
        }

        // Hàm tạo InternalTask mới theo định dạng "Task-{năm + tháng + ngày}-số thứ tự"
        private async Task<string> GenerateUniqueInternalTask()
        {
            try
            {
                // Lấy ngày hiện tại
                var currentDate = DateTime.Now;
                string datePart = currentDate.ToString("yyyyMMdd"); // Định dạng: yyyyMMdd (ví dụ: 20250327)

                // Tạo tiền tố cho InternalTask
                string prefix = $"Task-{datePart}-"; // Ví dụ: Task-20250327-

                // Tìm số thứ tự lớn nhất của InternalTask trong ngày hiện tại
                var existingTasks = await _sqlContext.ScrapLists
                    .Where(s => s.InternalTask != null && s.InternalTask.StartsWith(prefix))
                    .Select(s => s.InternalTask)
                    .ToListAsync();

                int maxSequenceNumber = 0;
                if (existingTasks.Any())
                {
                    // Lấy số thứ tự từ các InternalTask hiện có
                    foreach (var task in existingTasks)
                    {
                        // Tách phần số thứ tự từ InternalTask (ví dụ: Task-20250327-5 -> 5)
                        var sequencePart = task.Substring(prefix.Length); // Lấy phần sau tiền tố
                        if (int.TryParse(sequencePart, out int sequenceNumber))
                        {
                            if (sequenceNumber > maxSequenceNumber)
                            {
                                maxSequenceNumber = sequenceNumber;
                            }
                        }
                    }
                }

                // Tăng số thứ tự lên 1
                int newSequenceNumber = maxSequenceNumber + 1;

                // Tạo InternalTask mới
                string newInternalTask = $"{prefix}{newSequenceNumber}"; // Ví dụ: Task-20250327-5

                // Kiểm tra xem InternalTask đã tồn tại chưa (đề phòng trường hợp bất ngờ)
                while (await _sqlContext.ScrapLists.AnyAsync(s => s.InternalTask == newInternalTask))
                {
                    newSequenceNumber++;
                    newInternalTask = $"{prefix}{newSequenceNumber}";
                }

                return newInternalTask;
            }
            catch (Exception ex)
            {
                throw new Exception("Đã xảy ra lỗi khi tạo InternalTask mới.", ex);
            }
        }

        // API: Lấy dữ liệu từ ScrapList theo InternalTask trong 3 tháng gần nhất
        [HttpGet("get-scrap-status-zero")]
        public async Task<IActionResult> GetScrapStatusZero()
        {
            try
            {
                var threeMonthsAgo = DateTime.Now.AddMonths(-3);

                // Lấy dữ liệu từ bảng ScrapList với InternalTask hợp lệ
                var scrapRecords = await _sqlContext.ScrapLists
                    .Where(s => s.InternalTask != null && s.InternalTask != "N/A")
                    .ToListAsync();

                var scrapData = scrapRecords
                    .Where(s => s.CreateTime != default && s.CreateTime >= threeMonthsAgo)
                    .GroupBy(s => s.InternalTask)
                    .Select(g =>
                    {
                        var first = g.First();
                        return new
                        {
                            InternalTask = g.Key,
                            Description = first.Desc,
                            ApproveScrapPerson = first.ApproveScrapperson,
                            KanBanStatus = first.KanBanStatus,
                            Category = first.Category,
                            Remark = first.Remark,
                            CreateTime = first.CreateTime.ToString("yyyy-MM-dd"),
                            CreateBy = first.CreatedBy,
                            ApplyTaskStatus = first.ApplyTaskStatus,
                            Purpose = first.Purpose,
                            TotalQty = g.Count()
                        };
                    })
                    .ToList();

                if (!scrapData.Any())
                {
                    return NotFound(new { message = "Không tìm thấy dữ liệu phù hợp trong 3 tháng gần nhất." });
                }

                return Ok(scrapData);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi lấy dữ liệu.", error = ex.Message });
            }
        }

        // API: Tạo task và tích hợp dữ liệu từ API bên thứ ba
        [HttpPost("create-task")]
        public async Task<IActionResult> CreateTask([FromBody] CreateTaskRequest request)
        {
            try
            {
                // Kiểm tra dữ liệu đầu vào
                if (request == null || request.InternalTasks == null || !request.InternalTasks.Any())
                {
                    return BadRequest(new { message = "Danh sách InternalTasks không được để trống." });
                }

                // Lấy tất cả SN từ ScrapList dựa trên InternalTasks
                var scrapRecords = await _sqlContext.ScrapLists
                    .Where(s => request.InternalTasks.Contains(s.InternalTask))
                    .ToListAsync();

                if (!scrapRecords.Any())
                {
                    return NotFound(new { message = "Không tìm thấy dữ liệu cho các InternalTasks được cung cấp." });
                }

                var rejectedStatuses = new[] { 2, 4, 8 };
                var rejectedSNs = scrapRecords
                    .Where(s => rejectedStatuses.Contains(s.ApplyTaskStatus))
                    .Select(s =>
                    {
                        var reason = s.ApplyTaskStatus switch
                        {
                            2 => "đang chờ SPE approve scrap",
                            4 => "đang chờ approved thay BGA",
                            8 => "lỗi process, không thể sửa chữa",
                            _ => $"trạng thái không hợp lệ ({s.ApplyTaskStatus})"
                        };
                        return $"{s.SN} ({reason})";
                    })
                    .ToList();

                if (rejectedSNs.Any())
                {
                    return BadRequest(new { message = $"Các SN sau không hợp lệ để tạo task: {string.Join(", ", rejectedSNs)}" });
                }

                // Tạo danh sách SN để gửi đến API bên thứ ba
                var serialNumbers = string.Join(",", scrapRecords.Select(s => s.SN));
                Console.WriteLine($"Sending t_serial_numbers to external API: {serialNumbers}");

                // Gọi API bên thứ ba
                var externalRequest = new
                {
                    type = "task_system",
                    t_serial_numbers = serialNumbers
                };

                HttpResponseMessage externalResponse;
                List<ExternalApiResponse> externalData = null;

                try
                {
                    externalResponse = await _httpClient.PostAsJsonAsync("api/query", externalRequest);
                    externalResponse.EnsureSuccessStatusCode();
                    var responseContent = await externalResponse.Content.ReadAsStringAsync();
                    Console.WriteLine($"External API response: {responseContent}");
                    externalData = JsonSerializer.Deserialize<List<ExternalApiResponse>>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true // Bỏ qua phân biệt hoa thường
                    });
                }
                catch (HttpRequestException ex)
                {
                    // Log chi tiết lỗi khi gọi API bên thứ ba
                    return StatusCode(500, new { message = "Không thể kết nối đến API bên thứ ba.", error = ex.Message, innerException = ex.InnerException?.Message });
                }

                // Kết hợp dữ liệu từ ScrapList và API bên thứ ba
                var unmatchedSNs = new List<string>();
                var result = scrapRecords.Select(scrap =>
                {
                    var normalizedScrapSN = scrap.SN?.Trim();
                    var externalInfos = externalData?
                        .Where(e =>
                        {
                            var normalizedBoardSN = e.BoardSN?.Trim();
                            Console.WriteLine($"Comparing: scrap.SN='{normalizedScrapSN}', external.BoardSN='{normalizedBoardSN}'");
                            return normalizedBoardSN == normalizedScrapSN;
                        })
                        .ToList() ?? new List<ExternalApiResponse>();
                    var externalInfo = externalInfos.FirstOrDefault();
                    if (externalInfo == null)
                    {
                        Console.WriteLine($"No matching data found for SN: {normalizedScrapSN}");
                        unmatchedSNs.Add(normalizedScrapSN);
                    }
                    else
                    {
                        Console.WriteLine($"Matched data for SN: {normalizedScrapSN}, BoardSN: {externalInfo.BoardSN}");
                    }

                    // Thử parse SmtTime thành DateTime nếu cần
                    DateTime? smtTime = null;
                    if (!string.IsNullOrEmpty(externalInfo?.SmtTime))
                    {
                        if (DateTime.TryParse(externalInfo.SmtTime, out DateTime parsedDate))
                        {
                            smtTime = parsedDate;
                        }
                        else
                        {
                            Console.WriteLine($"Invalid SmtTime format for SN: {normalizedScrapSN}, SmtTime: {externalInfo.SmtTime}");
                        }
                    }

                    // Kiểm tra nếu Remark là "BP-20" thì gán Sloc = "FXV8" và Plant = "8620"
                    string slocValue = scrap.Remark == "BP-20" ? "FXV8" : scrap.Sloc;
                    if (scrap.Purpose == "Approved to master board")
                    {
                        slocValue = "FXV1";
                    }
                    string plantValue = scrap.Remark == "BP-20" ? "8620" : externalInfo?.Plant;

                    var consigICs = externalInfos
                        .Select((info, index) => new { info, index })
                        .OrderByDescending(entry => !string.IsNullOrEmpty(entry.info.IcPn)
                            && entry.info.IcPn.StartsWith("MLX", StringComparison.OrdinalIgnoreCase))
                        .ThenBy(entry => entry.index)
                        .Select(entry => new ConsigIcInfo
                        {
                            IcPn = entry.info.IcPn,
                            IcDetailPn = entry.info.IcDetailPn,
                            Qty = entry.info.Qty
                        })
                        .Where(entry => !string.IsNullOrEmpty(entry.IcPn)
                            || !string.IsNullOrEmpty(entry.IcDetailPn)
                            || !string.IsNullOrEmpty(entry.Qty))
                        .ToList();

                    return new
                    {
                        InternalTask = scrap.InternalTask,
                        Purpose = scrap.Purpose,
                        BoardSN = scrap.SN,
                        AfterBeforeKanban = externalInfo?.AfterBeforeKanban ?? scrap.KanBanStatus,
                        Category = scrap.Category,
                        Sloc = slocValue,
                        TaskNumber = scrap.TaskNumber,
                        PONumber = scrap.PO,
                        CreateBy = scrap.CreatedBy,
                        CreateDate = scrap.CreateTime.ToString("yyyy-MM-dd"),
                        Cost = scrap.Cost,
                        Remark = scrap.Remark,
                        Item = externalInfo?.Item,
                        Project = externalInfo?.Project,
                        Opn = externalInfo?.Opn,
                        IcPn = externalInfo?.IcPn,
                        IcDetailPn = externalInfo?.IcDetailPn,
                        Qty = externalInfo?.Qty,
                        Cm = externalInfo?.Cm,
                        Plant = plantValue,
                        SmtTime = smtTime, // Sử dụng giá trị đã parse (hoặc null nếu không parse được)
                        Description = scrap.Desc,
                        SpeApproveTime = scrap.SpeApproveTime,
                        ConsigICs = consigICs
                    };
                }).ToList();

                // Thông báo nếu không tìm thấy dữ liệu từ API bên thứ ba
                string message = null;
                if (unmatchedSNs.Any())
                {
                    message = $"Không tìm thấy dữ liệu từ API bên thứ ba cho các SN: {string.Join(", ", unmatchedSNs)}";
                }

                return Ok(new { message, data = result });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi tạo task.", error = ex.Message, innerException = ex.InnerException?.Message });
            }
        }

        // API: Tạo task dựa trên danh sách SNs
        [HttpPost("create-task-sn")]
        public async Task<IActionResult> CreateTaskBySN([FromBody] CreateTaskBySNRequest request)
        {
            try
            {
                // Kiểm tra dữ liệu đầu vào
                if (request == null || request.SNs == null || !request.SNs.Any())
                {
                    return BadRequest(new { message = "Danh sách SNs không được để trống." });
                }

                // Kiểm tra xem tất cả SNs có tồn tại trong bảng ScrapList không
                var existingSNs = await _sqlContext.ScrapLists
                    .Where(s => request.SNs.Contains(s.SN))
                    .ToListAsync();

                var nonExistingSNs = request.SNs.Except(existingSNs.Select(s => s.SN)).ToList();
                if (nonExistingSNs.Any())
                {
                    return BadRequest(new { message = $"Các SN sau không tồn tại trong bảng ScrapList: {string.Join(", ", nonExistingSNs)}" });
                }

                // Kiểm tra trạng thái ApplyTaskStatus của các SN: chỉ chấp nhận các giá trị khác 2, 4, 8
                var rejectedSNs = new List<string>();
                var validSNs = new List<ScrapList>();

                foreach (var sn in existingSNs)
                {
                    if (sn.ApplyTaskStatus == 2 || sn.ApplyTaskStatus == 4 || sn.ApplyTaskStatus == 8)
                    {
                        string reason = sn.ApplyTaskStatus switch
                        {
                            2 => "đang chờ SPE approve scrap",
                            4 => "đang chờ approved thay BGA",
                            8 => "lỗi process, không thể sửa chữa",
                            _ => $"trạng thái không hợp lệ ({sn.ApplyTaskStatus})"
                        };
                        rejectedSNs.Add($"{sn.SN} ({reason})");
                    }
                    else
                    {
                        validSNs.Add(sn); // SN hợp lệ để xử lý
                    }
                }

                if (rejectedSNs.Any())
                {
                    return BadRequest(new { message = $"Các SN sau không hợp lệ để tạo task: {string.Join(", ", rejectedSNs)}" });
                }

                if (!validSNs.Any())
                {
                    return BadRequest(new { message = "Không có SN nào hợp lệ để tạo task." });
                }

                // Tạo danh sách SN để gửi đến API bên thứ ba
                var serialNumbers = string.Join(",", validSNs.Select(s => s.SN));
                Console.WriteLine($"Sending t_serial_numbers to external API: {serialNumbers}");

                // Gọi API bên thứ ba
                var externalRequest = new
                {
                    type = "task_system",
                    t_serial_numbers = serialNumbers
                };

                HttpResponseMessage externalResponse;
                List<ExternalApiResponse> externalData = null;

                try
                {
                    externalResponse = await _httpClient.PostAsJsonAsync("api/query", externalRequest);
                    externalResponse.EnsureSuccessStatusCode();
                    var responseContent = await externalResponse.Content.ReadAsStringAsync();
                    Console.WriteLine($"External API response: {responseContent}");
                    externalData = JsonSerializer.Deserialize<List<ExternalApiResponse>>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true // Bỏ qua phân biệt hoa thường
                    });
                }
                catch (HttpRequestException ex)
                {
                    // Log chi tiết lỗi khi gọi API bên thứ ba
                    return StatusCode(500, new { message = "Không thể kết nối đến API bên thứ ba.", error = ex.Message, innerException = ex.InnerException?.Message });
                }

                // Kết hợp dữ liệu từ ScrapList và API bên thứ ba
                var unmatchedSNs = new List<string>();
                var result = validSNs.Select(scrap =>
                {
                    var normalizedScrapSN = scrap.SN?.Trim();
                    var externalInfos = externalData?
                        .Where(e =>
                        {
                            var normalizedBoardSN = e.BoardSN?.Trim();
                            Console.WriteLine($"Comparing: scrap.SN='{normalizedScrapSN}', external.BoardSN='{normalizedBoardSN}'");
                            return normalizedBoardSN == normalizedScrapSN;
                        })
                        .ToList() ?? new List<ExternalApiResponse>();
                    var externalInfo = externalInfos.FirstOrDefault();
                    if (externalInfo == null)
                    {
                        Console.WriteLine($"No matching data found for SN: {normalizedScrapSN}");
                        unmatchedSNs.Add(normalizedScrapSN);
                    }
                    else
                    {
                        Console.WriteLine($"Matched data for SN: {normalizedScrapSN}, BoardSN: {externalInfo.BoardSN}");
                    }

                    // Thử parse SmtTime thành DateTime nếu cần
                    DateTime? smtTime = null;
                    if (!string.IsNullOrEmpty(externalInfo?.SmtTime))
                    {
                        if (DateTime.TryParse(externalInfo.SmtTime, out DateTime parsedDate))
                        {
                            smtTime = parsedDate;
                        }
                        else
                        {
                            Console.WriteLine($"Invalid SmtTime format for SN: {normalizedScrapSN}, SmtTime: {externalInfo.SmtTime}");
                        }
                    }

                    // Kiểm tra nếu Remark là "BP-20" thì gán Sloc = "FXV8" và Plant = "8620"
                    string slocValue = scrap.Remark == "BP-20" ? "FXV8" : scrap.Sloc;
                    if (scrap.Purpose == "Approved to master board")
                    {
                        slocValue = "FXV1";
                    }
                    string plantValue = scrap.Remark == "BP-20" ? "8620" : externalInfo?.Plant;

                    var consigICs = externalInfos
                        .Select((info, index) => new { info, index })
                        .OrderByDescending(entry => !string.IsNullOrEmpty(entry.info.IcPn)
                            && entry.info.IcPn.StartsWith("MLX", StringComparison.OrdinalIgnoreCase))
                        .ThenBy(entry => entry.index)
                        .Select(entry => new ConsigIcInfo
                        {
                            IcPn = entry.info.IcPn,
                            IcDetailPn = entry.info.IcDetailPn,
                            Qty = entry.info.Qty
                        })
                        .Where(entry => !string.IsNullOrEmpty(entry.IcPn)
                            || !string.IsNullOrEmpty(entry.IcDetailPn)
                            || !string.IsNullOrEmpty(entry.Qty))
                        .ToList();

                    return new
                    {
                        InternalTask = scrap.InternalTask,
                        Purpose = scrap.Purpose,
                        BoardSN = scrap.SN,
                        AfterBeforeKanban = externalInfo?.AfterBeforeKanban ?? scrap.KanBanStatus,
                        Category = scrap.Category,
                        Sloc = slocValue,
                        TaskNumber = scrap.TaskNumber,
                        PONumber = scrap.PO,
                        CreateBy = scrap.CreatedBy,
                        CreateDate = scrap.CreateTime.ToString("yyyy-MM-dd"),
                        Cost = scrap.Cost,
                        Remark = scrap.Remark,
                        Item = externalInfo?.Item,
                        Project = externalInfo?.Project,
                        Opn = externalInfo?.Opn,
                        IcPn = externalInfo?.IcPn,
                        IcDetailPn = externalInfo?.IcDetailPn,
                        Qty = externalInfo?.Qty,
                        Cm = externalInfo?.Cm,
                        Plant = plantValue,
                        SmtTime = smtTime, // Sử dụng giá trị đã parse (hoặc null nếu không parse được)
                        Description = scrap.Desc,
                        SpeApproveTime = scrap.SpeApproveTime,
                        ConsigICs = consigICs
                    };
                }).ToList();

                // Thông báo nếu không tìm thấy dữ liệu từ API bên thứ ba
                string message = null;
                if (unmatchedSNs.Any())
                {
                    message = $"Không tìm thấy dữ liệu từ API bên thứ ba cho các SN: {string.Join(", ", unmatchedSNs)}";
                }

                return Ok(new { message, data = result });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi tạo task.", error = ex.Message, innerException = ex.InnerException?.Message });
            }
        }


        // API: Tạo task dựa trên danh sách SNs
        [HttpPost("create-task-bonepile")]
        public async Task<IActionResult> CreateTaskBonepile([FromBody] CreateTaskBySNRequest request)
        {
            try
            {
                // Kiểm tra dữ liệu đầu vào
                if (request == null || request.SNs == null || !request.SNs.Any())
                {
                    return BadRequest(new { message = "Danh sách SNs không được để trống." });
                }

                // Kiểm tra xem tất cả SNs có tồn tại trong bảng ScrapList không
                var existingSNs = await _sqlContext.ScrapLists
                    .Where(s => request.SNs.Contains(s.SN))
                    .ToListAsync();

                var nonExistingSNs = request.SNs.Except(existingSNs.Select(s => s.SN)).ToList();
                if (nonExistingSNs.Any())
                {
                    return BadRequest(new { message = $"Các SN sau không tồn tại trong bảng ScrapList: {string.Join(", ", nonExistingSNs)}" });
                }

                // Kiểm tra trạng thái ApplyTaskStatus của các SN: chỉ chấp nhận các giá trị khác 2, 4, 8
                var rejectedSNs = new List<string>();
                var validSNs = new List<ScrapList>();

                foreach (var sn in existingSNs)
                {
                    if (sn.ApplyTaskStatus == 2 || sn.ApplyTaskStatus == 4 || sn.ApplyTaskStatus == 8)
                    {
                        string reason = sn.ApplyTaskStatus switch
                        {
                            2 => "đang chờ SPE approve scrap",
                            4 => "đang chờ approved thay BGA",
                            8 => "lỗi process, không thể sửa chữa",
                            _ => $"trạng thái không hợp lệ ({sn.ApplyTaskStatus})"
                        };
                        rejectedSNs.Add($"{sn.SN} ({reason})");
                    }
                    else
                    {
                        validSNs.Add(sn); // SN hợp lệ để xử lý
                    }
                }

                if (rejectedSNs.Any())
                {
                    return BadRequest(new { message = $"Các SN sau không hợp lệ để tạo task: {string.Join(", ", rejectedSNs)}" });
                }

                if (!validSNs.Any())
                {
                    return BadRequest(new { message = "Không có SN nào hợp lệ để tạo task." });
                }

                // Lấy thông tin OPN từ SFISM4.NVIDIA_BONPILE_SN_LOG theo Serial Number
                var oracleConnectionString = _oracleContext.Database.GetConnectionString();
                var bonepileOpnMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                using (var connection = new OracleConnection(oracleConnectionString))
                {
                    await connection.OpenAsync();
                    var snParams = string.Join(",", validSNs.Select((_, i) => $":p{i}"));
                    using var command = new OracleCommand($"SELECT SERIAL_NUMBER, MODEL_NAME FROM SFISM4.NVIDIA_BONPILE_SN_LOG WHERE SERIAL_NUMBER IN ({snParams})", connection);
                    for (int i = 0; i < validSNs.Count; i++)
                    {
                        command.Parameters.Add(new OracleParameter($"p{i}", OracleDbType.Varchar2) { Value = validSNs[i].SN });
                    }

                    using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var serialNumber = reader.IsDBNull(0) ? null : reader.GetString(0)?.Trim();
                        var modelName = reader.IsDBNull(1) ? null : reader.GetString(1)?.Trim();

                        if (!string.IsNullOrEmpty(serialNumber))
                        {
                            bonepileOpnMap[serialNumber] = modelName;
                        }
                    }
                }

                // Tạo danh sách SN để gửi đến API bên thứ ba
                var serialNumbers = string.Join(",", validSNs.Select(s => s.SN));
                Console.WriteLine($"Sending t_serial_numbers to external API: {serialNumbers}");

                // Gọi API bên thứ ba
                var externalRequest = new
                {
                    type = "task_system",
                    t_serial_numbers = serialNumbers
                };

                HttpResponseMessage externalResponse;
                List<ExternalApiResponse> externalData = null;

                try
                {
                    externalResponse = await _httpClient.PostAsJsonAsync("api/query", externalRequest);
                    externalResponse.EnsureSuccessStatusCode();
                    var responseContent = await externalResponse.Content.ReadAsStringAsync();
                    Console.WriteLine($"External API response: {responseContent}");
                    externalData = JsonSerializer.Deserialize<List<ExternalApiResponse>>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true // Bỏ qua phân biệt hoa thường
                    });
                }
                catch (HttpRequestException ex)
                {
                    // Log chi tiết lỗi khi gọi API bên thứ ba
                    return StatusCode(500, new { message = "Không thể kết nối đến API bên thứ ba.", error = ex.Message, innerException = ex.InnerException?.Message });
                }

                // Kết hợp dữ liệu từ ScrapList và API bên thứ ba
                var unmatchedSNs = new List<string>();
                var result = validSNs.Select(scrap =>
                {
                    var normalizedScrapSN = scrap.SN?.Trim();
                    var externalInfo = externalData?.FirstOrDefault(e =>
                    {
                        var normalizedBoardSN = e.BoardSN?.Trim();
                        Console.WriteLine($"Comparing: scrap.SN='{normalizedScrapSN}', external.BoardSN='{normalizedBoardSN}'");
                        return normalizedBoardSN == normalizedScrapSN;
                    });
                    if (externalInfo == null)
                    {
                        Console.WriteLine($"No matching data found for SN: {normalizedScrapSN}");
                        unmatchedSNs.Add(normalizedScrapSN);
                    }
                    else
                    {
                        Console.WriteLine($"Matched data for SN: {normalizedScrapSN}, BoardSN: {externalInfo.BoardSN}");
                    }

                    // Thử parse SmtTime thành DateTime nếu cần
                    DateTime? smtTime = null;
                    if (!string.IsNullOrEmpty(externalInfo?.SmtTime))
                    {
                        if (DateTime.TryParse(externalInfo.SmtTime, out DateTime parsedDate))
                        {
                            smtTime = parsedDate;
                        }
                        else
                        {
                            Console.WriteLine($"Invalid SmtTime format for SN: {normalizedScrapSN}, SmtTime: {externalInfo.SmtTime}");
                        }
                    }

                    // Kiểm tra nếu Remark là "BP-20" thì gán Sloc = "FXV8" và Plant = "8620"
                    string slocValue = scrap.Remark == "BP-20" ? "FXV8" : scrap.Sloc;
                    if (scrap.Purpose == "Approved to master board")
                    {
                        slocValue = "FXV1";
                    }
                    string plantValue = scrap.Remark == "BP-20" ? "8620" : externalInfo?.Plant;
                    bonepileOpnMap.TryGetValue(normalizedScrapSN, out var bonepileOpn);

                    return new
                    {
                        InternalTask = scrap.InternalTask,
                        Purpose = scrap.Purpose,
                        BoardSN = scrap.SN,
                        AfterBeforeKanban = externalInfo?.AfterBeforeKanban ?? scrap.KanBanStatus,
                        Category = scrap.Category,
                        Sloc = slocValue,
                        TaskNumber = scrap.TaskNumber,
                        PONumber = scrap.PO,
                        CreateBy = scrap.CreatedBy,
                        CreateDate = scrap.CreateTime.ToString("yyyy-MM-dd"),
                        Cost = scrap.Cost,
                        Remark = scrap.Remark,
                        Item = externalInfo?.Item,
                        Project = externalInfo?.Project,
                        Opn = bonepileOpn,
                        IcPn = externalInfo?.IcPn,
                        IcDetailPn = externalInfo?.IcDetailPn,
                        Qty = externalInfo?.Qty,
                        Cm = externalInfo?.Cm,
                        Plant = plantValue,
                        SmtTime = smtTime, // Sử dụng giá trị đã parse (hoặc null nếu không parse được)
                        Description = scrap.Desc,
                        SpeApproveTime = scrap.SpeApproveTime
                    };
                }).ToList();

                // Thông báo nếu không tìm thấy dữ liệu từ API bên thứ ba
                string message = null;
                if (unmatchedSNs.Any())
                {
                    message = $"Không tìm thấy dữ liệu từ API bên thứ ba cho các SN: {string.Join(", ", unmatchedSNs)}";
                }

                return Ok(new { message, data = result });
            }
            catch (Exception ex)





            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi tạo task.", error = ex.Message, innerException = ex.InnerException?.Message });
            }
        }

        // API: Cập nhật TaskNumber và PO cho danh sách SN
        [HttpPost("update-task-po")]
        public async Task<IActionResult> UpdateTaskPO([FromBody] UpdateTaskPORequest request)
        {
            try
            {
                // Kiểm tra dữ liệu đầu vào
                if (request == null)
                {
                    return BadRequest(new { message = "Yêu cầu không hợp lệ. Vui lòng kiểm tra dữ liệu đầu vào." });
                }

                // Kiểm tra và xử lý SnList
                var snList = request.SnList ?? new List<string>();
                if (!snList.Any())
                {
                    return BadRequest(new { message = "Danh sách SN không được để trống." });
                }

                if (string.IsNullOrEmpty(request.Task) || string.IsNullOrEmpty(request.PO))
                {
                    return BadRequest(new { message = "Task và PO không được để trống." });
                }

                // Kiểm tra độ dài các trường
                if (snList.Any(sn => sn?.Length > 50))
                {
                    return BadRequest(new { message = "SN không được dài quá 50 ký tự." });
                }

                if (request.Task.Length > 50 || request.PO.Length > 50)
                {
                    return BadRequest(new { message = "Task và PO không được dài quá 50 ký tự." });
                }

                // Kiểm tra xem tất cả SN có tồn tại trong bảng ScrapList không
                var existingSNs = await _sqlContext.ScrapLists
                    .Where(s => snList.Contains(s.SN))
                    .Select(s => s.SN)
                    .ToListAsync();

                var nonExistingSNs = snList.Except(existingSNs).ToList();
                if (nonExistingSNs.Any())
                {
                    return BadRequest(new { message = $"Các SN sau không tồn tại trong bảng ScrapList: {string.Join(", ", nonExistingSNs)}" });
                }

                // Kiểm tra xem các SN đã có TaskNumber hoặc PO chưa
                var recordsToUpdate = await _sqlContext.ScrapLists
                    .Where(s => snList.Contains(s.SN))
                    .ToListAsync();

                var rejectedSNs = new List<string>();
                foreach (var record in recordsToUpdate)
                {
                    if (record.TaskNumber != null && record.TaskNumber != "N/A")
                    {
                        rejectedSNs.Add(record.SN);
                    }
                }

                if (rejectedSNs.Any())
                {
                    return BadRequest(new { message = $"Các SN sau đã có TaskNumber hoặc PO và không thể cập nhật: {string.Join(", ", rejectedSNs)}" });
                }

                // Cập nhật TaskNumber và PO cho các SN
                foreach (var record in recordsToUpdate)
                {
                    record.TaskNumber = request.Task;
                    record.PO = request.PO;
                    record.ApplyTaskStatus = 5;
                    record.ApplyTime = DateTime.Now;
                }

                await AddHistoryEntriesAsync(recordsToUpdate);
                await _sqlContext.SaveChangesAsync();

                return Ok(new { message = "Cập nhật TaskNumber và PO thành công cho các SN." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi cập nhật dữ liệu.", error = ex.Message });
            }
        }

        // API mới: Cập nhật Cost cho danh sách Board SN
        [HttpPost("update-cost")]
        public async Task<IActionResult> UpdateCost([FromBody] UpdateCostRequest request)
        {
            try
            {
                // Kiểm tra dữ liệu đầu vào
                if (request == null)
                {
                    return BadRequest(new { message = "Yêu cầu không hợp lệ. Vui lòng kiểm tra dữ liệu đầu vào." });
                }

                // Kiểm tra và xử lý BoardSNs và Costs
                var boardSNs = request.BoardSNs ?? new List<string>();
                var costs = request.Costs ?? new List<double>();

                if (!boardSNs.Any() || !costs.Any())
                {
                    return BadRequest(new { message = "Danh sách Board SN và Cost không được để trống." });
                }

                if (boardSNs.Count != costs.Count)
                {
                    return BadRequest(new { message = "Số lượng Board SN và Cost không khớp nhau." });
                }

                // Kiểm tra độ dài của Board SN
                if (boardSNs.Any(sn => sn?.Length > 50))
                {
                    return BadRequest(new { message = "Board SN không được dài quá 50 ký tự." });
                }

                // Kiểm tra giá trị Cost hợp lệ (ví dụ: không âm)
                if (costs.Any(cost => cost < 0))
                {
                    return BadRequest(new { message = "Cost không được là số âm." });
                }

                // Kiểm tra xem tất cả Board SN có tồn tại trong bảng ScrapList không
                var existingSNs = await _sqlContext.ScrapLists
                    .Where(s => boardSNs.Contains(s.SN))
                    .Select(s => s.SN)
                    .ToListAsync();

                var nonExistingSNs = boardSNs.Except(existingSNs).ToList();
                if (nonExistingSNs.Any())
                {
                    return BadRequest(new { message = $"Các Board SN sau không tồn tại trong bảng ScrapList: {string.Join(", ", nonExistingSNs)}" });
                }

                // Kiểm tra xem các Board SN đã có Cost chưa
                var recordsToUpdate = await _sqlContext.ScrapLists
                    .Where(s => boardSNs.Contains(s.SN))
                    .ToListAsync();

                var rejectedSNs = new List<string>();
                foreach (var record in recordsToUpdate)
                {
                    // Kiểm tra nếu Cost không rỗng và không phải là chuỗi rỗng
                    if (!string.IsNullOrEmpty(record.Cost) && record.Cost != "")
                    {
                        rejectedSNs.Add(record.SN);
                    }
                }

                if (rejectedSNs.Any())
                {
                    return BadRequest(new { message = $"Các Board SN sau đã có Cost và không thể cập nhật: {string.Join(", ", rejectedSNs)}" });
                }

                // Cập nhật Cost cho các Board SN
                for (int i = 0; i < boardSNs.Count; i++)
                {
                    var record = recordsToUpdate.FirstOrDefault(r => r.SN == boardSNs[i]);
                    if (record != null)
                    {
                        record.Cost = costs[i].ToString(); // Chuyển Cost thành chuỗi để lưu vào cột Cost
                    }
                }

                await AddHistoryEntriesAsync(recordsToUpdate);
                await _sqlContext.SaveChangesAsync();

                return Ok(new { message = "Cập nhật Cost thành công cho các Board SN." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi cập nhật Cost.", error = ex.Message });
            }
        }

        // API: Lấy dữ liệu từ ScrapList với ApplyTaskStatus = 1 (lịch sử áp dụng)
        [HttpGet("get-history-apply")]
        public async Task<IActionResult> GetHistoryApply()
        {
            try
            {
                // Lấy dữ liệu từ bảng ScrapList với ApplyTaskStatus = 1
                var scrapData = await _sqlContext.ScrapLists
                    .Where(s => s.ApplyTaskStatus == 1) // Lọc theo ApplyTaskStatus = 1
                    .GroupBy(s => s.InternalTask) // Nhóm theo InternalTask
                    .Select(g => new
                    {
                        InternalTask = g.Key,
                        Description = g.First().Desc,
                        ApproveScrapPerson = g.First().ApproveScrapperson,
                        KanBanStatus = g.First().KanBanStatus,
                        Category = g.First().Category,
                        Remark = g.First().Remark,
                        CreateTime = g.First().CreateTime.ToString("yyyy-MM-dd"),
                        CreateBy = g.First().CreatedBy,
                        ApplyTime = g.First().ApplyTime.HasValue ? g.First().ApplyTime.Value.ToString("yyyy-MM-dd") : "N/A",
                        ApplyTaskStatus = g.First().ApplyTaskStatus,
                        TotalQty = g.Count()
                    })
                    .ToListAsync();

                if (!scrapData.Any())
                {
                    return NotFound(new { message = "Không tìm thấy dữ liệu với ApplyTaskStatus = 1." });
                }

                return Ok(scrapData);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi lấy dữ liệu lịch sử.", error = ex.Message });
            }
        }

        // API: Tìm kiếm lịch sử ScrapList theo danh sách SN
        [HttpPost("history-by-sn")]
        public async Task<IActionResult> GetHistoryBySn([FromBody] HistoryBySnRequest request)
        {
            try
            {
                if (request?.SNs == null || !request.SNs.Any())
                {
                    return BadRequest(new { message = "Danh sách SN không được để trống." });
                }

                var normalizedSNs = request.SNs
                    .Where(sn => !string.IsNullOrWhiteSpace(sn))
                    .Select(sn => sn.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (!normalizedSNs.Any())
                {
                    return BadRequest(new { message = "Không có SN hợp lệ để tìm kiếm." });
                }

                var historyRecords = await _sqlContext.HistoryScrapLists
                    .Where(history => normalizedSNs.Contains(history.SN))
                    .OrderByDescending(history => history.Id)
                    .ToListAsync();

                var foundSNs = new HashSet<string>(historyRecords.Select(history => history.SN), StringComparer.OrdinalIgnoreCase);
                var missingSNs = normalizedSNs
                    .Where(sn => !foundSNs.Contains(sn))
                    .ToList();

                var result = historyRecords.Select(history => new
                {
                    history.Id,
                    history.SN,
                    history.KanBanStatus,
                    history.Sloc,
                    history.TaskNumber,
                    history.PO,
                    CreatedBy = history.CreatedBy,
                    history.Cost,
                    history.InternalTask,
                    Description = history.Desc,
                    CreateTime = history.CreateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    ApproveScrapPerson = history.ApproveScrapperson,
                    history.ApplyTaskStatus,
                    history.FindBoardStatus,
                    history.Remark,
                    history.Purpose,
                    history.Category,
                    ApplyTime = history.ApplyTime.HasValue ? history.ApplyTime.Value.ToString("yyyy-MM-dd HH:mm:ss") : "N/A",
                    history.SpeApproveTime
                }).ToList();

                return Ok(new { data = result, missingSNs });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi tìm kiếm lịch sử SN.", error = ex.Message });
            }
        }

        // API: Lấy dữ liệu FindBoardStatus từ ScrapList
        [HttpGet("get-find-board-status")]
        public async Task<IActionResult> GetFindBoardStatus()
        {
            try
            {
                // Lấy dữ liệu từ bảng ScrapList và nhóm theo InternalTask
                var scrapData = await _sqlContext.ScrapLists
                    .GroupBy(s => s.InternalTask) // Nhóm theo InternalTask
                    .Select(g => new
                    {
                        InternalTask = g.Key,
                        ApproveScrapPerson = g.First().ApproveScrapperson, // Lấy giá trị đầu tiên
                        Description = g.First().Desc, // Lấy giá trị đầu tiên
                        AfterBefore = g.First().KanBanStatus, // Lấy giá trị đầu tiên của KanBanStatus (After/Before)
                        CreateTime = g.First().CreateTime.ToString("yyyy-MM-dd"), // Chỉ lấy ngày tháng năm
                        TotalQty = g.Count(), // Tổng số lượng SN trong InternalTask
                        QtyScraped = g.Count(s => s.FindBoardStatus == "Đã chuyển kho phế"), // Số lượng SN có trạng thái "Đã báo phế"
                        QtyFindOk = g.Count(s => s.FindBoardStatus == "Đã tìm thấy"), // Số lượng SN có trạng thái "Đã tìm thấy"
                        QtyWaitFind = g.Count(s => s.FindBoardStatus == "Chưa tìm thấy") // Số lượng SN có trạng thái "Chưa tìm thấy"
                    })
                    .ToListAsync();

                if (!scrapData.Any())
                {
                    return NotFound(new { message = "Không tìm thấy dữ liệu trong bảng ScrapList." });
                }

                // Tính toán Status dựa trên TotalQty và QtyScraped
                var result = scrapData.Select(item => new
                {
                    item.InternalTask,
                    item.ApproveScrapPerson,
                    item.Description,
                    AfterBefore = item.AfterBefore, // After/Before Kanban
                    item.CreateTime,
                    item.TotalQty,
                    item.QtyScraped,
                    item.QtyFindOk,
                    item.QtyWaitFind,
                    Status = item.QtyScraped == item.TotalQty ? "close" : "on-going" // Nếu QtyScraped = TotalQty thì "close", ngược lại "on-going"
                }).ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi lấy dữ liệu FindBoardStatus.", error = ex.Message });
            }
        }

        // API: Lấy chi tiết dữ liệu từ ScrapList dựa trên InternalTasks hoặc SNs hoặc TaskNumber
        [HttpPost("detail-task-status")]
        public async Task<IActionResult> DetailTaskStatus([FromBody] DetailTaskStatusRequest request)
        {
            try
            {
                // Kiểm tra dữ liệu đầu vào
                if (request == null)
                {
                    return BadRequest(new { message = "Yêu cầu không hợp lệ. Vui lòng kiểm tra dữ liệu đầu vào." });
                }

                // Kiểm tra xem cả ba danh sách có trống không
                bool internalTasksEmpty = request.InternalTasks == null || !request.InternalTasks.Any();
                bool sNsEmpty = request.SNs == null || !request.SNs.Any();
                bool taskNumberEmpty = request.TaskNumber == null || !request.TaskNumber.Any();

                if (internalTasksEmpty && sNsEmpty && taskNumberEmpty)
                {
                    return BadRequest(new { message = "Cần cung cấp ít nhất một InternalTask hoặc một TaskNumber hoặc một SN." });
                }

                // Lấy dữ liệu từ bảng ScrapList
                IQueryable<ScrapList> query = _sqlContext.ScrapLists;

                // Nếu InternalTasks không trống, lọc theo InternalTasks
                if (!internalTasksEmpty)
                {
                    query = query.Where(s => request.InternalTasks.Contains(s.InternalTask));
                }

                // Nếu SNs không trống, lọc theo SNs
                if (!sNsEmpty)
                {
                    query = query.Where(s => request.SNs.Contains(s.SN));
                }

                // Nếu TaskNumber không trống, lọc theo TaskNumber
                if (!taskNumberEmpty)
                {
                    query = query.Where(s => request.TaskNumber.Contains(s.TaskNumber));
                }

                var scrapData = await query.ToListAsync();

                if (!scrapData.Any())
                {
                    return NotFound(new { message = "Không tìm thấy dữ liệu cho các InternalTasks hoặc SNs hoặc TaskNumber được cung cấp." });
                }

                // Chuyển dữ liệu thành định dạng trả về
                var result = scrapData.Select(s => new
                {
                    SN = s.SN,
                    InternalTask = s.InternalTask,
                    Description = s.Desc,
                    ApproveScrapPerson = s.ApproveScrapperson,
                    KanBanStatus = s.KanBanStatus,
                    Sloc = s.Sloc,
                    TaskNumber = s.TaskNumber,
                    PO = s.PO,
                    Cost = s.Cost,
                    Remark = s.Remark,
                    CreatedBy = s.CreatedBy,
                    CreateTime = s.CreateTime.ToString("yyyy-MM-dd"),
                    ApplyTime = s.ApplyTime.HasValue ? s.ApplyTime.Value.ToString("yyyy-MM-dd") : "N/A",
                    ApplyTaskStatus = s.ApplyTaskStatus,
                    FindBoardStatus = s.FindBoardStatus,
                    Purpose = s.Purpose,
                    Category = s.Category,
                    SpeApproveTime = s.SpeApproveTime
                }).ToList();

                return Ok(new { data = result });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi lấy dữ liệu chi tiết.", error = ex.Message });
            }
        }

        // Class để nhận dữ liệu đầu vào cho API detail-task-status
        public class DetailTaskStatusRequest
        {
            public List<string> InternalTasks { get; set; } = new List<string>();
            public List<string> SNs { get; set; } = new List<string>();
            public List<string> TaskNumber { get; set; } = new List<string>();
        }

        public class HistoryBySnRequest
        {
            public List<string> SNs { get; set; } = new List<string>();
        }

        // API: Cập nhật trạng thái FindBoardStatus trong bảng ScrapList
        [HttpPost("update-status-find-board")]
        public async Task<IActionResult> UpdateStatusFindBoard([FromBody] UpdateStatusFindBoardRequest request)
        {
            try
            {
                // Kiểm tra dữ liệu đầu vào
                if (request == null || request.SNs == null || !request.SNs.Any())
                {
                    return BadRequest(new { message = "Danh sách Serial Numbers (SNs) không được để trống." });
                }

                if (string.IsNullOrEmpty(request.Status))
                {
                    return BadRequest(new { message = "Trạng thái (Status) không được để trống." });
                }

                // Xác định giá trị FindBoardStatus dựa trên status đầu vào
                string findBoardStatus;
                switch (request.Status)
                {
                    case "1":
                        findBoardStatus = "Đã tìm thấy";
                        break;
                    case "2":
                        findBoardStatus = "Đã chuyển kho phế";
                        break;
                    default:
                        return BadRequest(new { message = "Trạng thái không hợp lệ. Chỉ chấp nhận giá trị '1' (Đã tìm thấy) hoặc '2' (Đã chuyển kho phế)." });
                }

                // Tìm các bản ghi trong ScrapList có SN khớp với danh sách SNs
                var scrapRecords = await _sqlContext.ScrapLists
                    .Where(s => request.SNs.Contains(s.SN))
                    .ToListAsync();

                if (!scrapRecords.Any())
                {
                    return NotFound(new { message = "Không tìm thấy bản ghi nào trong ScrapList khớp với danh sách SNs được cung cấp." });
                }

                // Cập nhật cột FindBoardStatus cho các bản ghi tìm thấy
                foreach (var record in scrapRecords)
                {
                    record.FindBoardStatus = findBoardStatus;
                }

                // Lưu thay đổi vào cơ sở dữ liệu
                await AddHistoryEntriesAsync(scrapRecords);
                await _sqlContext.SaveChangesAsync();

                return Ok(new { message = $"Cập nhật trạng thái FindBoardStatus thành công cho {scrapRecords.Count} bản ghi." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi cập nhật trạng thái FindBoardStatus.", error = ex.Message });
            }
        }

        // Class để nhận dữ liệu đầu vào cho API update-status-find-board
        public class UpdateStatusFindBoardRequest
        {
            public List<string> SNs { get; set; } = new List<string>();
            public string Status { get; set; } = string.Empty;
        }

        // API: Input SN chờ SPE approve
        [HttpPost("input-sn-wait-spe-approve")]
        public async Task<IActionResult> InputSNWaitSpeApprove([FromBody] InputSNWaitSpeApproveRequest request)
        {
            try
            {
                // Kiểm tra dữ liệu đầu vào
                if (request == null || request.SNs == null || !request.SNs.Any())
                {
                    return BadRequest(new { message = "Danh sách SN không được để trống." });
                }

                var snList = request.SNs.Select(sn => sn?.Trim()).ToList();
                if (snList.Any(string.IsNullOrEmpty))
                {
                    return BadRequest(new { message = "SN không được để trống." });
                }

                var duplicateSNs = snList.GroupBy(sn => sn)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();

                if (duplicateSNs.Any())
                {
                    return BadRequest(new { message = $"Danh sách SN bị trùng lặp: {string.Join(", ", duplicateSNs)}" });
                }

                if (string.IsNullOrEmpty(request.CreatedBy) || string.IsNullOrEmpty(request.Remark) || string.IsNullOrEmpty(request.Approve))
                {
                    return BadRequest(new { message = "CreatedBy, Remark và Approve không được để trống." });
                }

                // Kiểm tra Remark hợp lệ (BP-10, BP-20, B36R)
                if (request.Remark != "BP-10" && request.Remark != "BP-20" && request.Remark != "B36R")
                {
                    return BadRequest(new { message = "Remark phải là 'BP-10', 'BP-20' hoặc 'B36R'." });
                }

                // Kiểm tra Approve hợp lệ (giả sử 2 hoặc 4 dựa trên code trước)
                if (request.Approve != "2" && request.Approve != "4")
                {
                    return BadRequest(new { message = "Approve phải là '2' hoặc '4'." });
                }

                // Kiểm tra độ dài các trường
                if (snList.Any(sn => sn.Length > 50))
                {
                    return BadRequest(new { message = "SN không được dài quá 50 ký tự." });
                }

                if (request.CreatedBy.Length > 50 || request.Description?.Length > 100 || request.Remark.Length > 50 || request.Approve.Length > 50)
                {
                    return BadRequest(new { message = "CreatedBy, Description, Remark và Approve không được dài quá 50 ký tự." });
                }

                // Kiểm tra trùng lặp SN trong bảng ScrapList
                var existingSNs = await _sqlContext.ScrapLists
                    .AsNoTracking() // Thêm cái này để EF không giữ lại instance trong bộ nhớ tracking
                    .Where(s => snList.Contains(s.SN))
                    .ToListAsync();

                var rejectedSNs = new List<string>();
                var updateSNs = new List<ScrapList>(); // Danh sách SN hợp lệ để cập nhật trạng thái
                var insertSNs = snList.ToList(); // Danh sách SN để insert

                foreach (var sn in existingSNs)
                {
                    // Logic reject tương tự create-task, nhưng điều chỉnh nếu cần
                    if (sn.ApplyTaskStatus == 0)
                    {
                        rejectedSNs.Add($"{sn.SN} (SN SPE đã approved phế, đang chờ xin Task/PO)");
                    }
                    else if (sn.ApplyTaskStatus == 1)
                    {
                        rejectedSNs.Add($"{sn.SN} (SN đã gửi NV xin Task/PO)");
                    }
                    else if (sn.ApplyTaskStatus == 2)
                    {
                        rejectedSNs.Add($"{sn.SN} (SN đang chờ SPE approve scrap)");
                    }
                    else if (sn.ApplyTaskStatus == 4)
                    {
                        if (request.Approve == "4")
                        {
                            updateSNs.Add(sn);
                            insertSNs.Remove(sn.SN);
                        }
                        else
                        {
                            rejectedSNs.Add($"{sn.SN} (SN đang chờ SPE approve BGA)");
                        }
                    }
                    else if (sn.ApplyTaskStatus == 10)
                    {
                        rejectedSNs.Add($"{sn.SN} (SN đang trong quy trình Replace BGA)");
                    }
                    else if (sn.ApplyTaskStatus == 5)
                    {
                        rejectedSNs.Add($"{sn.SN} (Đã có task, chờ chuyển MRB)");
                    }
                    else if (sn.ApplyTaskStatus == 6)
                    {
                        rejectedSNs.Add($"{sn.SN} (Đã chuyển kho phế, chờ MRB xác nhận)");
                    }
                    else if (sn.ApplyTaskStatus == 7)
                    {
                        rejectedSNs.Add($"{sn.SN} (Đã chuyển kho phế thành công)");
                    }
                    else if (sn.ApplyTaskStatus == 16 || sn.ApplyTaskStatus == 17 || sn.ApplyTaskStatus == 8 || sn.ApplyTaskStatus == 19)
                    {
                        updateSNs.Add(sn); // SN hợp lệ để cập nhật
                        insertSNs.Remove(sn.SN); // Loại bỏ SN khỏi danh sách insert
                    }
                    else
                    {
                        rejectedSNs.Add($"{sn.SN} (Trạng thái không xác định)");
                    }
                }

                if (rejectedSNs.Any())
                {
                    return BadRequest(new { message = $"Các SN sau đã có trong scrap list: {string.Join(", ", rejectedSNs)}" });
                }

                // ket noi voi oracle data base
                var oracleConnectionString = _oracleContext.Database.GetConnectionString();

                //using (var connection = new OracleConnection(oracleConnectionString))
                //{
                //    await connection.OpenAsync();

                //    // Chuẩn bị câu lệnh SQL
                //    string snListSql = string.Join(",", snList.Select(sn => $"'{sn}'"));
                //    string sqlQuery = $@"
                //    SELECT SERIAL_NUMBER, MO_NUMBER 
                //    FROM SFISM4.R117 
                //    WHERE SERIAL_NUMBER IN ({snListSql}) 
                //    AND GROUP_NAME = 'SMTLOADING' 
                //    AND MO_NUMBER LIKE '5%'";

                //    // Chỉ kiểm tra điều kiện MO_NUMBER nếu Approve = 2 (những bản cần xin báo phế)
                //    if (request.Approve == "2")
                //    {

                //        using (var command = new OracleCommand(sqlQuery, connection))
                //        {
                //            using (var reader = await command.ExecuteReaderAsync())
                //            {
                //                var invalidSNs = new List<string>();
                //                while (await reader.ReadAsync())
                //                {
                //                    string serialNumber = reader.GetString(0);
                //                    string moNumber = reader.GetString(1);
                //                    invalidSNs.Add($"{serialNumber} (MO: {moNumber})");
                //                }

                //                if (invalidSNs.Any())
                //                {
                //                    return BadRequest(new { message = $"Các SN sau có MO_NUMBER bắt đầu bằng 5xxxx và không thể xử lý: {string.Join(", ", invalidSNs)}" });
                //                }
                //            }
                //        }
                //    }
                //}


                // Kiểm tra điều kiện Bonepile dựa trên Remark
                using var bonepileConnection = new OracleConnection(oracleConnectionString);
                await bonepileConnection.OpenAsync();

                var foundSNsInBonepile = new List<string>();
                var foundSNsInKanban = new List<string>();
                if (request.Remark == "BP-10" || request.Remark == "BP-20")
                {
                    // Query bảng bonepile 2.0: SFISM4.NVIDIA_BONEPILE_SN_LOG
                    var snParams = string.Join(",", snList.Select((_, i) => $":p{i}"));
                    using var bonepileCommand = new OracleCommand($"SELECT SERIAL_NUMBER FROM sfism4.nvidia_bonpile_sn_log WHERE SERIAL_NUMBER IN ({snParams})", bonepileConnection);
                    for (int i = 0; i < snList.Count; i++)
                    {
                        bonepileCommand.Parameters.Add(new OracleParameter($"p{i}", OracleDbType.Varchar2) { Value = snList[i] });
                    }
                    using var bonepileReader = await bonepileCommand.ExecuteReaderAsync();
                    while (await bonepileReader.ReadAsync())
                    {
                        foundSNsInBonepile.Add(bonepileReader.GetString(0));
                    }

                    // Các SN BP-10/BP-20 không được tồn tại trong Z_KANBAN_TRACKING_T
                    var kanbanParams = string.Join(",", snList.Select((_, i) => $":k{i}"));
                    using var kanbanCheckCommand = new OracleCommand($"SELECT SERIAL_NUMBER FROM SFISM4.Z_KANBAN_TRACKING_T WHERE SERIAL_NUMBER IN ({kanbanParams})", bonepileConnection);
                    for (int i = 0; i < snList.Count; i++)
                    {
                        kanbanCheckCommand.Parameters.Add(new OracleParameter($"k{i}", OracleDbType.Varchar2) { Value = snList[i] });
                    }
                    using var kanbanReader = await kanbanCheckCommand.ExecuteReaderAsync();
                    while (await kanbanReader.ReadAsync())
                    {
                        foundSNsInKanban.Add(kanbanReader.GetString(0));
                    }
                }

                if (foundSNsInKanban.Any())
                {
                    return BadRequest(new { message = $"Các SN sau tồn tại trong SFISM4.Z_KANBAN_TRACKING_T và không thể xử lý với Remark {request.Remark}: {string.Join(", ", foundSNsInKanban)}" });
                }

                // Xử lý theo Remark
                if (request.Remark == "BP-10")
                {
                    // Pass nếu không tồn tại trong bonepile 2.0 (không có SN nào trong bảng)
                    if (foundSNsInBonepile.Any())
                    {
                        return BadRequest(new { message = $"Các SN sau đã tồn tại trong bonepile 2.0 (SFISM4.NVIDIA_BONEPILE_SN_LOG): {string.Join(", ", foundSNsInBonepile)}" });
                    }
                }
                else if (request.Remark == "BP-20")
                {
                    // Pass nếu tất cả SN đều có trong bảng, reject nếu thiếu bất kỳ SN nào
                    var missingSNs = snList.Except(foundSNsInBonepile).ToList();
                    if (missingSNs.Any())
                    {
                        return BadRequest(new { message = $"Các SN sau không tồn tại trong bảng Bonepile 2.0 (SFISM4.NVIDIA_BONEPILE_SN_LOG): {string.Join(", ", missingSNs)}" });
                    }
                }
                else if (request.Remark == "B36R")
                {
                    // Query bảng Z_KANBAN_TRACKING_T
                    using var kanbanConnection = new OracleConnection(oracleConnectionString);
                    await kanbanConnection.OpenAsync();
                    var kanbanFoundSNs = new List<string>();
                    var snParams = string.Join(",", snList.Select((_, i) => $":p{i}"));
                    using var kanbanCommand = new OracleCommand($"SELECT SERIAL_NUMBER FROM SFISM4.Z_KANBAN_TRACKING_T WHERE SERIAL_NUMBER IN ({snParams})", kanbanConnection);
                    for (int i = 0; i < snList.Count; i++)
                    {
                        kanbanCommand.Parameters.Add(new OracleParameter($"p{i}", OracleDbType.Varchar2) { Value = snList[i] });
                    }
                    using var kanbanReader = await kanbanCommand.ExecuteReaderAsync();
                    while (await kanbanReader.ReadAsync())
                    {
                        kanbanFoundSNs.Add(kanbanReader.GetString(0));
                    }

                    // Pass nếu tất cả SN đều có trong Z_KANBAN_TRACKING_T
                    var missingKanbanSNs = snList.Except(kanbanFoundSNs).ToList();
                    if (missingKanbanSNs.Any())
                    {
                        return BadRequest(new { message = $"Các SN sau không tồn tại trong SFISM4.Z_KANBAN_TRACKING_T: {string.Join(", ", missingKanbanSNs)}" });
                    }
                }

                // Tạo biến ApproveTag
                int approveTag = request.Approve == "2" ? 2 : 4;
                var updateTime = DateTime.Now;
                string updateCategory = request.Approve == "2" ? "Scrap" : "BGA";
                
                // Tạo danh sách ScrapList để lưu vào bảng (cho các SN mới)
                var scrapListEntries = new List<ScrapList>();
                foreach (var sn in insertSNs)
                {
                    var (modelName, modelSerial) = await GetInforAsync(sn);
                    var scrapEntry = new ScrapList
                    {
                        SN = sn,
                        KanBanStatus = "N/A",
                        Sloc = "N/A",
                        ModelName = modelName,
                        ModelType = modelSerial,
                        TaskNumber = null,
                        PO = null,
                        Cost = "N/A",
                        Remark = request.Remark,
                        CreatedBy = request.CreatedBy,
                        Desc = request.Description ?? "N/A",
                        CreateTime = updateTime,
                        ApplyTime = updateTime,
                        ApproveScrapperson = "N/A",
                        ApplyTaskStatus = approveTag,
                        FindBoardStatus = "N/A",
                        InternalTask = "N/A",
                        Purpose = "N/A",
                        Category = updateCategory
                    };

                    scrapListEntries.Add(scrapEntry);
                }

                // Cập nhật các bản ghi có ApplyTaskStatus = 3 (nếu có)
                foreach (var sn in updateSNs)
                {
                    sn.KanBanStatus = "N/A";
                    sn.Sloc = "N/A";
                    sn.TaskNumber = null;
                    sn.PO = null;
                    sn.Cost = "N/A";
                    sn.Remark = request.Remark;
                    sn.CreatedBy = request.CreatedBy;
                    sn.Desc = request.Description ?? "N/A";
                    if (sn.CreateTime == default)
                    {
                        sn.CreateTime = updateTime;
                    }
                    sn.ApplyTime = updateTime;
                    sn.ApproveScrapperson = "N/A";
                    sn.ApplyTaskStatus = approveTag;
                    sn.FindBoardStatus = "N/A";
                    sn.InternalTask = "N/A";
                    sn.Purpose = "N/A";
                    sn.Category = updateCategory;
                }

                // Lưu vào bảng ScrapList (thêm mới và cập nhật)
                if (scrapListEntries.Any())
                {
                    _sqlContext.ScrapLists.AddRange(scrapListEntries);
                }
                if (updateSNs.Any())
                {
                    _sqlContext.ScrapLists.UpdateRange(updateSNs);
                }
                if (scrapListEntries.Any())
                {
                    await AddHistoryEntriesAsync(scrapListEntries);
                }
                if (updateSNs.Any())
                {
                    await AddHistoryEntriesAsync(updateSNs);
                }
                await _sqlContext.SaveChangesAsync();

                string message = "Lưu danh sách SN thành công.";
                if (updateSNs.Any())
                {
                    message += $" Đã cập nhật {updateSNs.Count} SN.";
                }

                return Ok(new { message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi lưu dữ liệu.", error = ex.Message });
            }
        }

        // API: GET - Tự động quét và cập nhật ModelName/ModelType cho tất cả bản ghi thiếu trong ScrapList
        [HttpGet("auto-update-model-info")]
        public async Task<IActionResult> AutoUpdateModelInfo()
        {
            try
            {
                // 1. Lấy tất cả bản ghi ScrapList mà ModelName hoặc ModelType đang trống
                var recordsToUpdate = await _sqlContext.ScrapLists
                    .Where(s => string.IsNullOrEmpty(s.ModelName) || string.IsNullOrEmpty(s.ModelType))
                    .ToListAsync();

                if (!recordsToUpdate.Any())
                {
                    return Ok(new
                    {
                        message = "Tất cả bản ghi đã có ModelName và ModelType. Không cần cập nhật.",
                        updatedCount = 0,
                        notFoundSNs = new List<string>()
                    });
                }

                var notFoundSNs = new List<string>();
                var updatedSNs = new List<string>();
                int updatedCount = 0;

                // 2. Duyệt từng bản ghi và cập nhật
                foreach (var record in recordsToUpdate)
                {
                    var (modelName, modelSerial) = await GetInforAsync(record.SN);

                    if (string.IsNullOrEmpty(modelName) || string.IsNullOrEmpty(modelSerial))
                    {
                        notFoundSNs.Add(record.SN);
                        continue;
                    }

                    record.ModelName = modelName;
                    record.ModelType = modelSerial;
                    updatedCount++;
                    updatedSNs.Add(record.SN);
                }

                // 3. Chỉ SaveChanges nếu có thay đổi
                if (updatedCount > 0)
                {
                    await _sqlContext.SaveChangesAsync();
                }

                // 4. Trả về kết quả chi tiết
                return Ok(new
                {
                    message = $"Đã quét {recordsToUpdate.Count} bản ghi. " +
                              $"Cập nhật thành công {updatedCount} bản ghi.",
                    totalScanned = recordsToUpdate.Count,
                    updatedCount,
                    notFoundCount = notFoundSNs.Count,
                    updatedSNs,         // (tùy chọn) danh sách SN đã được cập nhật
                    notFoundSNs         // danh sách SN không tìm thấy thông tin
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Đã xảy ra lỗi khi tự động cập nhật ModelName/ModelType.",
                    error = ex.Message,
                    stackTrace = ex.StackTrace
                });
            }
        }


        // API: Lấy dữ liệu từ ScrapList với ApplyTaskStatus = 2 & 4
        [HttpGet("get-scrap-status-two-and-four")]
        public async Task<IActionResult> GetScrapStatusTwo()
        {
            try
            {
                // Lấy dữ liệu từ bảng ScrapList với ApplyTaskStatus = 2 & 4
                var scrapData = await _sqlContext.ScrapLists
                    .Where(s => (s.ApplyTaskStatus == 2 || s.ApplyTaskStatus == 4) && s.ModelType == "ADAPTER") // Lọc theo ApplyTaskStatus = 2 & 4
                    .Select(s => new
                    {
                        SN = s.SN,
                        Description = s.Desc,
                        CreateTime = s.CreateTime.ToString("yyyy-MM-dd"),
                        ApplyTaskStatus = s.ApplyTaskStatus,
                        Remark = s.Remark,
                        CreateBy = s.CreatedBy
                    })
                    .ToListAsync();

                if (!scrapData.Any())
                {
                    return NotFound(new { message = "Không tìm thấy dữ liệu với ApplyTaskStatus = 2." });
                }

                return Ok(scrapData);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi lấy dữ liệu.", error = ex.Message });
            }
        }


        // API: BẮT BUỘC truyền cả status và modelType
        [HttpGet("get-task-by-status")]
        public async Task<IActionResult> GetTaskByStatus(
            [FromQuery] int status,
            [FromQuery] string modelType)  // Bỏ ? và default value → bắt buộc truyền
        {
            // === VALIDATION: Bắt buộc phải có cả 2 tham số ===
            if (string.IsNullOrWhiteSpace(modelType))
                return BadRequest(new { message = "Thiếu tham số 'modelType'. Vui lòng truyền đầy đủ status và modelType." });

            if (status < 0)
                return BadRequest(new { message = "Tham số 'status' không hợp lệ." });

            try
            {
                var data = await _sqlContext.ScrapLists
                    .Where(s => s.ApplyTaskStatus == status
                             && s.TaskNumber != null
                             && s.TaskNumber != ""
                             && s.ModelType == modelType)
                    .GroupBy(s => s.TaskNumber)
                    .Select(g => new
                    {
                        TaskNumber = g.Key,
                        MinApplyTime = g.Min(s => s.ApplyTime),   // để nguyên DateTime?
                        TotalQty = g.Count()
                    })
                    .OrderByDescending(g => g.MinApplyTime)
                    .ToListAsync();

                if (!data.Any())
                {
                    return NotFound(new
                    {
                        message = $"Không tìm thấy TaskNumber nào với ApplyTaskStatus = {status} và ModelType = '{modelType}'."
                    });
                }

                // Format ngày ở client-side (an toàn, không lỗi translate)
                var result = data.Select(g => new
                {
                    g.TaskNumber,
                    ApplyTime = g.MinApplyTime.HasValue
                        ? g.MinApplyTime.Value.ToString("yyyy-MM-dd")
                        : "N/A",
                    g.TotalQty
                }).ToList();

                return Ok(new
                {
                    filter = new { status, modelType },
                    count = result.Count,
                    data = result
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Đã xảy ra lỗi khi lấy danh sách task.",
                    error = ex.Message
                });
            }
        }

        // API: Xác nhận chuyển MRB
        [HttpPost("confirm-move-mrb")]
        public async Task<IActionResult> ConfirmMoveMRB([FromBody] ConfirmMoveMRBRequest request)
        {
            try
            {
                // Kiểm tra dữ liệu đầu vào
                if (request == null)
                {
                    return BadRequest(new { message = "Yêu cầu không hợp lệ. Vui lòng kiểm tra dữ liệu đầu vào." });
                }

                var snList = request.SNs ?? new List<string>();
                var taskNumberList = request.TaskNumbers ?? new List<string>();

                if (!snList.Any() || !taskNumberList.Any())
                {
                    return BadRequest(new { message = "Danh sách SNs và TaskNumbers không được để trống." });
                }

                // Kiểm tra độ dài của SN và TaskNumber
                if (snList.Any(sn => sn?.Length > 50) || taskNumberList.Any(task => task?.Length > 50))
                {
                    return BadRequest(new { message = "SN hoặc TaskNumber không được dài quá 50 ký tự." });
                }

                // Lấy tất cả các bản ghi từ ScrapList dựa trên TaskNumbers
                var scrapRecords = await _sqlContext.ScrapLists
                    .Where(s => taskNumberList.Contains(s.TaskNumber))
                    .ToListAsync();

                if (!scrapRecords.Any())
                {
                    return NotFound(new { message = "Không tìm thấy dữ liệu trong ScrapList cho các TaskNumbers được cung cấp." });
                }

                // Lấy danh sách SN từ ScrapList dựa trên TaskNumbers
                var scrapSNs = scrapRecords.Select(s => s.SN).ToList();

                // So sánh SNs từ đầu vào với SNs từ ScrapList
                var unmatchedSNs = snList.Except(scrapSNs).ToList();
                var matchedSNs = snList.Intersect(scrapSNs).ToList();

                // Nếu có bất kỳ SN nào không khớp, không thực hiện cập nhật
                if (unmatchedSNs.Any())
                {
                    return BadRequest(new { message = $"Các SN sau không khớp với dữ liệu từ TaskNumbers: {string.Join(", ", unmatchedSNs)}. Không thực hiện cập nhật." });
                }

                // Nếu tất cả SN khớp, thực hiện cập nhật
                var updatedRecords = new List<ScrapList>();
                foreach (var sn in matchedSNs)
                {
                    var record = scrapRecords.FirstOrDefault(r => r.SN == sn);
                    if (record != null)
                    {
                        if (record.ApplyTaskStatus == 5)
                        {
                            record.ApplyTaskStatus = 6;
                            record.ApplyTime = DateTime.Now;
                            updatedRecords.Add(record);
                        }
                        else if (record.ApplyTaskStatus == 6)
                        {
                            record.ApplyTaskStatus = 7;
                            record.ApplyTime = DateTime.Now;
                            updatedRecords.Add(record);
                        }
                        // Nếu ApplyTaskStatus không phải 5 hoặc 6, giữ nguyên (không cập nhật)
                    }
                }

                if (!updatedRecords.Any())
                {
                    return Ok(new { message = "Không có bản ghi nào được cập nhật do trạng thái không hợp lệ." });
                }

                // Lưu thay đổi vào cơ sở dữ liệu
                await AddHistoryEntriesAsync(updatedRecords);
                await _sqlContext.SaveChangesAsync();

                string message = "Cập nhật trạng thái ApplyTaskStatus thành công.";
                return Ok(new { message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi xác nhận chuyển MRB.", error = ex.Message });
            }
        }

        // Class để nhận dữ liệu đầu vào cho API confirm-move-mrb
        public class ConfirmMoveMRBRequest
        {
            public List<string> SNs { get; set; } = new List<string>();
            public List<string> TaskNumbers { get; set; } = new List<string>();
        }

        // API: Cập nhật ApplyTaskStatus cho danh sách SN
        [HttpPost("update-apply-task-status")]
        public async Task<IActionResult> UpdateApplyTaskStatus([FromBody] UpdateApplyTaskStatusRequest request)
        {
            try
            {
                // ----------------------------
                // 1. Kiểm tra dữ liệu đầu vào
                // ----------------------------
                if (request == null || request.SNs == null || !request.SNs.Any())
                {
                    return BadRequest(new { message = "Danh sách SN không được để trống." });
                }

                if (string.IsNullOrEmpty(request.Type) || (request.Type != "check" && request.Type != "update"))
                {
                    return BadRequest(new { message = "Type phải là 'check' hoặc 'update'." });
                }

                if (request.ApplyTaskStatus != 5 && request.ApplyTaskStatus != 6 && request.ApplyTaskStatus != 7)
                {
                    return BadRequest(new { message = "ApplyTaskStatus phải là 5, 6 hoặc 7." });
                }

                // ------------------------------------------------
                // 2. Kiểm tra SN có tồn tại trong ScrapList không
                // ------------------------------------------------
                var existingSNs = await _sqlContext.ScrapLists
                    .Where(s => request.SNs.Contains(s.SN))
                    .ToListAsync();

                var nonExistingSNs = request.SNs.Except(existingSNs.Select(s => s.SN)).ToList();
                if (nonExistingSNs.Any())
                {
                    return BadRequest(new { message = $"Các SN sau không tồn tại trong bảng ScrapList: {string.Join(", ", nonExistingSNs)}" });
                }

                // -------------------------------------------------------------
                // 3. Xác định ApplyTaskStatus hiện tại phải là gì trước khi đổi
                // -------------------------------------------------------------
                int requiredCurrentStatus;
                switch (request.ApplyTaskStatus)
                {
                    case 6: requiredCurrentStatus = 5; break;
                    case 7: requiredCurrentStatus = 6; break;
                    case 5: requiredCurrentStatus = 7; break;
                    default:
                        return BadRequest(new { message = "ApplyTaskStatus không hợp lệ." });
                }

                // -------------------------------------------------------------
                // 4. Kiểm tra trạng thái hiện tại
                // -------------------------------------------------------------
                var invalidSNs = existingSNs
                    .Where(s => s.ApplyTaskStatus != requiredCurrentStatus)
                    .Select(s => $"{s.SN} (trạng thái hiện tại: {s.ApplyTaskStatus})")
                    .ToList();

                if (invalidSNs.Any())
                {
                    return BadRequest(new { message = $"Các SN sau không có trạng thái đúng ({requiredCurrentStatus}): {string.Join(", ", invalidSNs)}" });
                }

                // -------------------------------------------------------------
                // 5. Nếu type = check → chỉ validate và trả kết quả
                // -------------------------------------------------------------
                if (request.Type == "check")
                {
                    return Ok(new
                    {
                        message = "CHECK_OK",
                        nextStatus = request.ApplyTaskStatus
                    });
                }

                // -------------------------------------------------------------
                // 6. Nếu type = update → thực hiện update dữ liệu
                // -------------------------------------------------------------
                foreach (var record in existingSNs)
                {
                    record.ApplyTaskStatus = request.ApplyTaskStatus;
                    record.ApplyTime = DateTime.Now;
                }

                await AddHistoryEntriesAsync(existingSNs);
                await _sqlContext.SaveChangesAsync();

                return Ok(new { message = "UPDATE-OK" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi cập nhật dữ liệu.", error = ex.Message });
            }
        }


        // API: Cập nhật ApplyTaskStatus cho SWITCH
        [HttpPost("update-apply-status")]
        public async Task<IActionResult> UpdateApplyStatus([FromBody] UpdateApplyStatusRequest request)
        {
            try
            {
                if (request == null || request.SNs == null || !request.SNs.Any())
                {
                    return BadRequest(new { message = "Danh sách SN không được để trống." });
                }

                if (request.TargetStatus != 9 && request.TargetStatus != 20)
                {
                    return BadRequest(new { message = "TargetStatus chỉ hỗ trợ cập nhật về 9 hoặc 20." });
                }

                var records = await _sqlContext.ScrapLists
                    .Where(s => request.SNs.Contains(s.SN))
                    .ToListAsync();

                var missingSNs = request.SNs.Except(records.Select(r => r.SN)).ToList();
                if (missingSNs.Any())
                {
                    return BadRequest(new { message = $"Các SN sau không tồn tại trong bảng ScrapList: {string.Join(", ", missingSNs)}" });
                }

                var requiredCurrentStatus = request.TargetStatus == 9 ? 0 : 9;
                var invalidSNs = records
                    .Where(r => r.ApplyTaskStatus != requiredCurrentStatus)
                    .Select(r => $"{r.SN} (trạng thái hiện tại: {r.ApplyTaskStatus})")
                    .ToList();

                if (invalidSNs.Any())
                {
                    return BadRequest(new { message = $"Các SN sau không có trạng thái hợp lệ ({requiredCurrentStatus}): {string.Join(", ", invalidSNs)}" });
                }

                var updateTime = DateTime.Now;

                foreach (var record in records)
                {
                    record.ApplyTaskStatus = request.TargetStatus;
                    record.ApplyTime = updateTime;
                }

                await AddHistoryEntriesAsync(records);
                await _sqlContext.SaveChangesAsync();

                return Ok(new { message = $"Cập nhật ApplyTaskStatus = {request.TargetStatus} thành công cho {records.Count} SN." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi cập nhật dữ liệu.", error = ex.Message });
            }
        }


        // API: Đồng bộ dữ liệu từ ScrapList với SFISM4.R_REPAIR_SCRAP
        [HttpPost("sync-scrap")]
        public async Task<IActionResult> SyncScrap()
        {
            await Task.CompletedTask;
            return Ok(new { message = "Đồng bộ sang hệ thống repair_scrap đã được vô hiệu hóa." });
        }


        private async Task<(string ModelName, string ModelSerial)> GetInforAsync(string serialNumber)
        {
            if (string.IsNullOrWhiteSpace(serialNumber))
                return (string.Empty, string.Empty);

            // Đảm bảo connection mở
            var connection = _oracleContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            await using var command = connection.CreateCommand();

            // === Bước 1: Lấy MODEL_NAME từ R107 ===
            command.CommandText = @"
        select distinct MODEL_NAME from sfism4.R107 where ( SERIAL_NUMBER = :sn or serial_number = :sn or shipping_sn  = :sn or shipping_sn2 = :sn or po_no = :sn OR shipping_sn2 =REPLACE(SUBSTR(:sn,-17,17),':','') )";

            var paramSn = new OracleParameter("sn", OracleDbType.Varchar2) { Value = serialNumber };
            command.Parameters.Add(paramSn);

            var modelNameObj = await command.ExecuteScalarAsync();
            var modelName = (modelNameObj?.ToString() ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(modelName))
                return (string.Empty, string.Empty);

            // === Bước 2: Lấy MODEL_SERIAL từ C_MODEL_DESC_T ===
            command.CommandText = @"
        SELECT NVL(MODEL_SERIAL, '') 
        FROM SFIS1.C_MODEL_DESC_T 
        WHERE MODEL_NAME = :model 
          AND ROWNUM = 1";

            command.Parameters.Clear();
            var paramModel = new OracleParameter("model", OracleDbType.Varchar2) { Value = modelName };
            command.Parameters.Add(paramModel);

            var modelSerialObj = await command.ExecuteScalarAsync();
            var modelSerial = (modelSerialObj?.ToString() ?? string.Empty).Trim();

            return (modelName, modelSerial);
        }


        [HttpPost("process-can-not-repair")]
        public async Task<IActionResult> ProcessCanNotRepair([FromBody] ProcessCanNotRepairRequest request)
        {
            try
            {
                if (request == null || request.SNs == null || !request.SNs.Any())
                    return BadRequest(new { message = "Danh sách SN không được để trống." });

                if (string.IsNullOrEmpty(request.CreatedBy))
                    return BadRequest(new { message = "CreatedBy không được để trống." });

                if (request.SNs.Any(sn => sn.Length > 50))
                    return BadRequest(new { message = "SN không được dài quá 50 ký tự." });

                if (request.CreatedBy.Length > 50 || (request.Description != null && request.Description.Length > 100))
                    return BadRequest(new { message = "CreatedBy và Description không được dài quá giới hạn." });

                // Check duplicate từ SQL
                var existingSNs = await _sqlContext.ScrapLists
                    .Where(s => request.SNs.Contains(s.SN))
                    .Select(s => s.SN)
                    .ToListAsync();

                var duplicateSNs = existingSNs.Intersect(request.SNs).ToList();
                if (duplicateSNs.Any())
                {
                    return BadRequest(new
                    {
                        message = $"Các SN sau đã tồn tại trong ScrapList: {string.Join(", ", duplicateSNs)}"
                    });
                }

                var scrapListEntries = new List<ScrapList>();
                var createdAt = DateTime.Now;

                foreach (var sn in request.SNs)
                {
                    // ⭐ LẤY MODEL_NAME + MODEL_SERIAL
                    var (modelName, modelSerial) = await GetInforAsync(sn);

                    var scrapEntry = new ScrapList
                    {
                        SN = sn,
                        ModelName = modelName,        // ⭐ Ghi model name
                        ModelType = modelSerial,    // ⭐ Ghi model serial
                        KanBanStatus = "N/A",
                        Sloc = "N/A",
                        TaskNumber = null,
                        PO = null,
                        Cost = "N/A",
                        Remark = null,
                        CreatedBy = request.CreatedBy,
                        Desc = request.Description ?? "N/A",
                        CreateTime = createdAt,
                        ApplyTime = createdAt,
                        ApproveScrapperson = "N/A",
                        ApplyTaskStatus = 8,
                        FindBoardStatus = "N/A",
                        InternalTask = "N/A",
                        Purpose = "process can't repair",
                        Category = "N/A"
                    };

                    scrapListEntries.Add(scrapEntry);
                }

                // Insert into ScrapList + History
                _sqlContext.ScrapLists.AddRange(scrapListEntries);
                await AddHistoryEntriesAsync(scrapListEntries);
                await _sqlContext.SaveChangesAsync();

                return Ok(new { message = $"Đã insert thành công {scrapListEntries.Count} SN vào ScrapList." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Đã xảy ra lỗi khi insert dữ liệu.",
                    error = ex.Message
                });
            }
        }


        [HttpPost("pending-guide")]
        public async Task<IActionResult> PendingGuide([FromBody] ProcessCanNotRepairRequest request)
        {
            try
            {
                if (request == null || request.SNs == null || !request.SNs.Any())
                    return BadRequest(new { message = "Danh sách SN không được để trống." });

                if (string.IsNullOrEmpty(request.CreatedBy))
                    return BadRequest(new { message = "CreatedBy không được để trống." });

                if (request.SNs.Any(sn => sn.Length > 50))
                    return BadRequest(new { message = "SN không được dài quá 50 ký tự." });

                if (request.CreatedBy.Length > 50 || (request.Description != null && request.Description.Length > 100))
                    return BadRequest(new { message = "CreatedBy và Description không được dài quá giới hạn." });

                // Check duplicate từ SQL
                var existingSNs = await _sqlContext.ScrapLists
                    .Where(s => request.SNs.Contains(s.SN))
                    .Select(s => s.SN)
                    .ToListAsync();

                var duplicateSNs = existingSNs.Intersect(request.SNs).ToList();
                if (duplicateSNs.Any())
                {
                    return BadRequest(new
                    {
                        message = $"Các SN sau đã tồn tại trong ScrapList: {string.Join(", ", duplicateSNs)}"
                    });
                }

                var scrapListEntries = new List<ScrapList>();
                var createdAt = DateTime.Now;

                foreach (var sn in request.SNs)
                {
                    // ⭐ LẤY MODEL_NAME + MODEL_SERIAL
                    var (modelName, modelSerial) = await GetInforAsync(sn);

                    var scrapEntry = new ScrapList
                    {
                        SN = sn,
                        ModelName = modelName,        // ⭐ Ghi model name
                        ModelType = modelSerial,    // ⭐ Ghi model serial
                        KanBanStatus = "N/A",
                        Sloc = "N/A",
                        TaskNumber = null,
                        PO = null,
                        Cost = "N/A",
                        Remark = null,
                        CreatedBy = request.CreatedBy,
                        Desc = request.Description ?? "N/A",
                        CreateTime = createdAt,
                        ApplyTime = createdAt,
                        ApproveScrapperson = "N/A",
                        ApplyTaskStatus = 22,
                        FindBoardStatus = "N/A",
                        InternalTask = "N/A",
                        Purpose = "Wait for instructions from the customer",
                        Category = "N/A"
                    };

                    scrapListEntries.Add(scrapEntry);
                }

                // Insert into ScrapList + History
                _sqlContext.ScrapLists.AddRange(scrapListEntries);
                await AddHistoryEntriesAsync(scrapListEntries);
                await _sqlContext.SaveChangesAsync();

                return Ok(new { message = $"Đã insert thành công {scrapListEntries.Count} SN vào ScrapList." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Đã xảy ra lỗi khi insert dữ liệu.",
                    error = ex.Message
                });
            }
        }

        // API: Lấy dữ liệu từ ScrapList với ApplyTaskStatus = 8
        [HttpGet("get-scrap-status-eight")]
        public async Task<IActionResult> GetScrapStatusEight()
        {
            try
            {
                // Lấy dữ liệu từ bảng ScrapList với ApplyTaskStatus = 8
                var scrapData = await _sqlContext.ScrapLists
                    .Where(s => s.ApplyTaskStatus == 8) // Lọc theo ApplyTaskStatus = 8
                    .Select(s => new
                    {
                        SN = s.SN,
                        Description = s.Desc,
                        CreateTime = s.CreateTime.ToString("yyyy-MM-dd"),
                        ApplyTaskStatus = s.ApplyTaskStatus,
                        Remark = s.Remark,
                        CreateBy = s.CreatedBy
                    })
                    .ToListAsync();

                if (!scrapData.Any())
                {
                    return NotFound(new { message = "Không tìm thấy dữ liệu với ApplyTaskStatus = 8." });
                }

                return Ok(scrapData);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi lấy dữ liệu.", error = ex.Message });
            }
        }


        // API: Lấy dữ liệu từ ScrapList với ApplyTaskStatus = 22
        [HttpGet("get-status-pending-guide")]
        public async Task<IActionResult> GetStatusPending()
        {
            try
            {
                // Lấy dữ liệu từ bảng ScrapList với ApplyTaskStatus = 22
                var scrapData = await _sqlContext.ScrapLists
                    .Where(s => s.ApplyTaskStatus == 22) // Lọc theo ApplyTaskStatus = 22
                    .Select(s => new
                    {
                        SN = s.SN,
                        Description = s.Desc,
                        CreateTime = s.CreateTime.ToString("yyyy-MM-dd"),
                        ApplyTaskStatus = s.ApplyTaskStatus,
                        Remark = s.Remark,
                        CreateBy = s.CreatedBy
                    })
                    .ToListAsync();

                if (!scrapData.Any())
                {
                    return NotFound(new { message = "Không tìm thấy dữ liệu với ApplyTaskStatus = 22" });
                }

                return Ok(scrapData);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi lấy dữ liệu.", error = ex.Message });
            }
        }



        // API: Checking Scrap quarterly
        [HttpPost("checking-scrap-quarterly")]
        public async Task<IActionResult> CheckingScrapQuarterly([FromBody] CheckingScrapQuarterlyRequest request)
        {
            try
            {
                // Kiểm tra dữ liệu đầu vào
                if (request == null || string.IsNullOrEmpty(request.Type))
                {
                    return BadRequest(new { message = "Type không được để trống. Phải là 'summary' hoặc 'detail'." });
                }

                if (request.Type != "summary" && request.Type != "detail")
                {
                    return BadRequest(new { message = "Type phải là 'summary' hoặc 'detail'." });
                }

                // Lấy dữ liệu từ bảng ScrapList với Purpose = 'Scrap to quarterly'
                var scrapData = await _sqlContext.ScrapLists
                    .Where(s => s.Purpose == "Scrap to quarterly" && s.ModelType == "ADAPTER")
                    .ToListAsync();

                if (!scrapData.Any())
                {
                    return Ok(new { message = "Không có dữ liệu với Purpose = 'Scrap to quarterly'." });
                }

                if (request.Type == "detail")
                {
                    // Trả về tất cả dữ liệu chi tiết
                    var detailData = scrapData.Select(s => new
                    {
                        SN = s.SN,
                        KanBanStatus = s.KanBanStatus,
                        TaskNumber = s.TaskNumber,
                        PO = s.PO,
                        CreatedBy = s.CreatedBy,
                        Description = s.Desc,
                        CreateTime = s.CreateTime,
                        ApplyTaskStatus = s.ApplyTaskStatus,
                        Remark = s.Remark,
                        Purpose = s.Purpose
                    }).ToList();

                    return Ok(new { data = detailData });
                }
                else // summary
                {
                    // Tổng hợp dữ liệu theo năm và quý
                    var summaryData = scrapData
                        .GroupBy(s => new { Year = s.CreateTime.Year, Quarter = GetQuarter(s.CreateTime.Month) })
                        .Select(g => new
                        {
                            Year = g.Key.Year,
                            Quarter = g.Key.Quarter,
                            Total = g.Count(),
                            WaitingApproved = g.Count(x => x.ApplyTaskStatus == 2),
                            HaventTask = g.Count(x => x.ApplyTaskStatus == 0 || x.ApplyTaskStatus == 1),
                            HaveTask = g.Count(x => x.ApplyTaskStatus == 5),
                            MovedScrap = g.Count(x => x.ApplyTaskStatus == 7)
                        })
                        .OrderBy(x => x.Year)
                        .ThenBy(x => x.Quarter)
                        .ToList();

                    return Ok(new { data = summaryData });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi kiểm tra dữ liệu Scrap quarterly.", error = ex.Message });
            }
        }

        // API: Checking Scrap quarterly SWITCH
        [HttpPost("checking-scrap-quarterly-switch")]
        public async Task<IActionResult> CheckingScrapQuarterlySwitch([FromBody] CheckingScrapQuarterlyRequest request)
        {
            try
            {
                // Kiểm tra dữ liệu đầu vào
                if (request == null || string.IsNullOrEmpty(request.Type))
                {
                    return BadRequest(new { message = "Type không được để trống. Phải là 'summary' hoặc 'detail'." });
                }

                if (request.Type != "summary" && request.Type != "detail")
                {
                    return BadRequest(new { message = "Type phải là 'summary' hoặc 'detail'." });
                }

                // Lấy dữ liệu từ bảng ScrapList với Purpose = 'Scrap to quarterly'
                var scrapData = await _sqlContext.ScrapLists
                    .Where(s => s.Purpose == "Scrap to quarterly" && s.ModelType == "SWITCH")
                    .ToListAsync();

                if (!scrapData.Any())
                {
                    return Ok(new { message = "Không có dữ liệu với Purpose = 'Scrap to quarterly'." });
                }

                if (request.Type == "detail")
                {
                    // Trả về tất cả dữ liệu chi tiết
                    var detailData = scrapData.Select(s => new
                    {
                        SN = s.SN,
                        KanBanStatus = s.KanBanStatus,
                        TaskNumber = s.TaskNumber,
                        PO = s.PO,
                        CreatedBy = s.CreatedBy,
                        Description = s.Desc,
                        CreateTime = s.CreateTime,
                        ApplyTaskStatus = s.ApplyTaskStatus,
                        Remark = s.Remark,
                        Purpose = s.Purpose
                    }).ToList();

                    return Ok(new { data = detailData });
                }
                else // summary
                {
                    // Tổng hợp dữ liệu theo năm và quý
                    var summaryData = scrapData
                        .GroupBy(s => new { Year = s.CreateTime.Year, Quarter = GetQuarter(s.CreateTime.Month) })
                        .Select(g => new
                        {
                            Year = g.Key.Year,
                            Quarter = g.Key.Quarter,
                            Total = g.Count(),
                            WaitingApproved = g.Count(x => x.ApplyTaskStatus == 2),
                            HaventTask = g.Count(x => x.ApplyTaskStatus == 0 || x.ApplyTaskStatus == 1),
                            HaveTask = g.Count(x => x.ApplyTaskStatus == 5),
                            MovedScrap = g.Count(x => x.ApplyTaskStatus == 7)
                        })
                        .OrderBy(x => x.Year)
                        .ThenBy(x => x.Quarter)
                        .ToList();

                    return Ok(new { data = summaryData });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi kiểm tra dữ liệu Scrap quarterly.", error = ex.Message });
            }
        }


        // Hàm helper để xác định quý dựa trên tháng
        private int GetQuarter(int month)
        {
            return (month - 1) / 3 + 1; // Q1: 1-3 -> 1, Q2: 4-6 -> 2, etc.
        }

    }

    // Class để nhận dữ liệu đầu vào cho API update-cost
    public class UpdateCostRequest
    {
        public List<string> BoardSNs { get; set; } = new List<string>();
        public List<double> Costs { get; set; } = new List<double>();
    }

    // Class để nhận dữ liệu đầu vào cho API update-task-po
    public class UpdateTaskPORequest
    {
        public List<string> SnList { get; set; } = new List<string>();
        public string Task { get; set; } = string.Empty;
        public string PO { get; set; } = string.Empty;
    }

    // Class để nhận dữ liệu đầu vào cho API create-task
    public class CreateTaskRequest
    {
        public List<string> InternalTasks { get; set; } = new List<string>();
        public string SaveApplyStatus { get; set; } = string.Empty;
    }

    // Class để nhận dữ liệu đầu vào cho API create-task-sn
    public class CreateTaskBySNRequest
    {
        public List<string> SNs { get; set; } = new List<string>();
        public string SaveApplyStatus { get; set; } = string.Empty;
    }

    // Class để nhận dữ liệu đầu vào cho API input-sn
    public class InputSNRequest
    {
        public List<string> SNs { get; set; } = new List<string>();
        public string CreatedBy { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ApproveScrapPerson { get; set; } = string.Empty;
        public string Purpose { get; set; } = string.Empty;
        public string SpeApproveTime { get; set; } = null;
    }

    // Class để ánh xạ dữ liệu từ API bên thứ ba
    public class ExternalApiResponse
    {
        public double? Item { get; set; }
        public string Project { get; set; }
        public string Opn { get; set; }
        [JsonPropertyName("ic pn")]
        public string IcPn { get; set; }
        [JsonPropertyName("ic detail pn")]
        public string IcDetailPn { get; set; }
        [JsonPropertyName("boarch sn")]
        public string BoardSN { get; set; }
        [JsonPropertyName("smt time")]
        public string SmtTime { get; set; }
        [JsonPropertyName("qty")]
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string Qty { get; set; }
        [JsonPropertyName("after/before kanban")]
        public string AfterBeforeKanban { get; set; }
        public string Cm { get; set; }
        public string Plant { get; set; }
    }

    public class ConsigIcInfo
    {
        public string IcPn { get; set; }
        public string IcDetailPn { get; set; }
        public string Qty { get; set; }
    }

    // Class để nhận dữ liệu đầu vào cho API input-sn-wait-spe-approve
    public class InputSNWaitSpeApproveRequest
    {
        public List<string> SNs { get; set; } = new List<string>();
        public string Description { get; set; } = string.Empty;
        public string Remark { get; set; } = string.Empty;
        public string Approve { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = string.Empty;
    }

    // Thêm class request mới UpdateApplyTaskStatusRequest
    public class UpdateApplyTaskStatusRequest
    {
        public string Type { get; set; } = string.Empty; // "check" hoặc "update"
        public List<string> SNs { get; set; } = new List<string>();
        public int ApplyTaskStatus { get; set; }
    }

    public class UpdateApplyStatusRequest
    {
        public List<string> SNs { get; set; } = new List<string>();
        public int TargetStatus { get; set; }
    }

    // Class để nhận dữ liệu đầu vào cho API process-can-not-repair
    public class ProcessCanNotRepairRequest
    {
        public List<string> SNs { get; set; } = new List<string>();
        public string Description { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = string.Empty;
    }

    // Class để nhận dữ liệu đầu vào cho API checking-scrap-quarterly
    public class CheckingScrapQuarterlyRequest
    {
        public string Type { get; set; } = string.Empty;
    }

    public class FlexibleStringConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => reader.GetDouble().ToString(CultureInfo.InvariantCulture),
                JsonTokenType.Null => null,
                _ => throw new JsonException($"Unexpected token parsing string. Token: {reader.TokenType}")
            };
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStringValue(value);
        }
    }

}
