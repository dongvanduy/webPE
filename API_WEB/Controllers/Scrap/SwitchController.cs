using API_WEB.ModelsDB;
using API_WEB.ModelsOracle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;
using System.Data;
using System.Net.Http;
using System.Text.Json;

namespace API_WEB.Controllers.Scrap
{
    [Route("[controller]")]
    [ApiController]
    public class SwitchController : ControllerBase
    {
        private readonly CSDL_NE _sqlContext;
        private readonly OracleDbContext _oracleContext;

        public SwitchController(CSDL_NE sqlContext, OracleDbContext oracleContext)
        {
            _sqlContext = sqlContext;
            _oracleContext = oracleContext;
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

        // API: Cập nhật TaskNumber và PO cho danh sách SN
        [HttpPost("update-task-po-switch")]
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

                await AddHistoryEntriesAsync(recordsToUpdate, "update");
                await _sqlContext.SaveChangesAsync();

                return Ok(new { message = "Cập nhật TaskNumber và PO thành công cho các SN." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi cập nhật dữ liệu.", error = ex.Message });
            }
        }

        // API: Xóa danh sách SN trong ScrapList với ApplyTaskStatus = 2
        [HttpPost("remove-switch-sn-list")]
        public async Task<IActionResult> RemoveSnList([FromBody] UpdateTaskPORequest request)
        {
            try
            {
                if (request?.SnList == null || !request.SnList.Any())
                {
                    return BadRequest(new { message = "SN list cannot be empty!" });
                }

                if (request.SnList.Count >= 230)
                {
                    return BadRequest(new { message = "Each delete request must contain fewer than 230 SNs." });
                }

                // Lấy danh sách bản ghi thỏa mãn điều kiện ApplyTaskStatus = 2 và ModelType SWITCH
                var recordsToRemove = await _sqlContext.ScrapLists
                    .Where(s => request.SnList.Contains(s.SN) && s.ApplyTaskStatus == 2)
                    .ToListAsync();

                if (!recordsToRemove.Any())
                {
                    return NotFound(new { message = "SNs can only be removed when ApplyStatus is 2 (Pending Customer Approval)" });
                }

                var deletedSns = recordsToRemove.Select(r => r.SN).ToList();

                await AddHistoryEntriesAsync(recordsToRemove, "remove");
                _sqlContext.ScrapLists.RemoveRange(recordsToRemove);
                await _sqlContext.SaveChangesAsync();

                return Ok(new
                {
                    message = "SN list deleted successfully",
                    count = deletedSns.Count,
                    deletedSns = deletedSns
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error occurred while deleting data.", error = ex.Message });
            }
        }

        // API: Lấy dữ liệu từ ScrapList với ApplyTaskStatus = 2
        [HttpGet("get-status-two-four-switch")]
        public async Task<IActionResult> GetScrapStatusTwoSwitch()
        {
            try
            {
                // Lấy dữ liệu từ bảng ScrapList với ApplyTaskStatus = 2 & 4
                var scrapData = await _sqlContext.ScrapLists
                    .Where(s => (s.ApplyTaskStatus == 2 || s.ApplyTaskStatus == 4) && s.ModelType == "SWITCH") // Lọc theo ApplyTaskStatus = 2 & 4
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
                    return NotFound(new { message = "No data found with ApplyTaskStatus = 2" });
                }

                return Ok(scrapData);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error occurred while deleting data", error = ex.Message });
            }
        }

        [HttpPost("input-sn-wait-spe-approve-sw")]
        public async Task<IActionResult> InputWaitSpeApproveSw([FromBody] InputSNWaitSpeApproveRequest request)
        {
            // 1. Validate đầu vào cơ bản
            if (request == null || request.SNs == null || !request.SNs.Any())
                return BadRequest(new { message = "SN list is required" });

            if (string.IsNullOrWhiteSpace(request.CreatedBy) ||
                string.IsNullOrWhiteSpace(request.Remark) ||
                string.IsNullOrWhiteSpace(request.Approve))
                return BadRequest(new { message = "CreatedBy, Remark, and Approve are required" });

            // Kiểm tra giá trị hợp lệ
            if (!new[] { "BP-10", "BP-20"}.Contains(request.Remark))
                return BadRequest(new { message = "Remark only accepts 'BP-10' or 'BP-20'" });

            if (!new[] { "2", "4" }.Contains(request.Approve))
                return BadRequest(new { message = "Approve must be '2' (Scrap)"});

            // Kiểm tra độ dài
            if (request.CreatedBy.Length > 50 || request.Remark.Length > 50 || request.Approve.Length > 50)
                return BadRequest(new { message = "CreatedBy, Remark, Approve must not exceed 50 characters" });

            if (request.Description?.Length > 100)
                return BadRequest(new { message = "Description must not exceed 100 characters" });

            // 2. Chuẩn hóa và kiểm tra trùng lặp SN trong request
            var requestSnSet = request.SNs
                .Where(sn => !string.IsNullOrWhiteSpace(sn))
                .Select(sn => sn.Trim())
                .ToList();

            if (requestSnSet.Count != requestSnSet.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                return BadRequest(new { message = "The SN list contains duplicate values" });

            if (requestSnSet.Any(sn => sn.Length > 50))
                return BadRequest(new { message = "SN must not exceed 50 characters" });

            if (!requestSnSet.Any())
                return BadRequest(new { message = "SN invalid!" });

            try
            {
                // 3. Query tất cả SN đã tồn tại trong ScrapList một lần duy nhất
                var existingSnEntities = await _sqlContext.ScrapLists
                    .Where(s => requestSnSet.Contains(s.SN))
                    .ToListAsync();

                var existingSnDict = existingSnEntities
                    .ToDictionary(x => x.SN, StringComparer.OrdinalIgnoreCase);

                var rejectedReasons = new List<string>();
                var approveTag = request.Approve == "2" ? 2 : 4;
                var category = request.Approve == "2" ? "Scrap" : "BGA";
                var now = DateTime.Now;

                // 4. Phân loại SN đã tồn tại
                foreach (var entity in existingSnEntities)
                {
                    switch (entity.ApplyTaskStatus)
                    {
                        case 0:
                            rejectedReasons.Add($"{entity.SN} (SN SPE has approved the scrap, currently waiting for Task/PO approval)");
                            break;
                        case 2:
                            rejectedReasons.Add($"{entity.SN} (SN is waiting for SPE's scrap approval)");
                            break;
                        case 5:
                            rejectedReasons.Add($"{entity.SN} (TaskNumber is available, awaiting transfer to MRB.)");
                            break;
                        case 6:
                            rejectedReasons.Add($"{entity.SN} (Transferred to scrap warehouse, awaiting MRB confirmation)(Đã chuyển kho phế, chờ MRB xác nhận)");
                            break;
                        case 7:
                            rejectedReasons.Add($"{entity.SN} (Successfully transferred to scrap warehouse)");
                            break;
                        case 9:
                            rejectedReasons.Add($"{entity.SN} (Awaiting PM update)");
                            break;
                        case 20:
                            rejectedReasons.Add($"{entity.SN} (Awaiting Cost update)");
                            break;
                        default:
                            rejectedReasons.Add($"{entity.SN} (Unknown status: {entity.ApplyTaskStatus})");
                            break;
                    }
                }

                if (rejectedReasons.Any())
                {
                    return BadRequest(new
                    {
                        message = "Some SNs cannot be processed because they already exist with a disallowed status:",
                        details = rejectedReasons
                    });
                }

                // 5. Xác định SN cần insert mới (loại bỏ tất cả SN đã tồn tại)
                var newSNs = requestSnSet
                    .Where(sn => !existingSnDict.ContainsKey(sn))
                    .ToList();

                // 6. Lấy thông tin ModelName, ModelType từ Oracle (gọi batch nếu có thể)
                var modelInfos = new List<(string SN, string ModelName, string ModelType)>();

                foreach (var sn in newSNs)
                {
                    var (modelName, modelSerial) = await GetInforAsync(sn);
                    modelInfos.Add((sn, modelName ?? "N/A", modelSerial ?? "N/A"));
                }


                // 7. Tạo danh sách entity mới
                var newEntities = modelInfos.Select(info => new ScrapList
                {
                    SN = info.SN,
                    KanBanStatus = "N/A",
                    Sloc = "N/A",
                    ModelName = info.ModelName,
                    ModelType = info.ModelType,
                    TaskNumber = null,
                    PO = null,
                    Cost = "N/A",
                    Remark = request.Remark,
                    CreatedBy = request.CreatedBy,
                    Desc = request.Description?.Trim() ?? "N/A",
                    CreateTime = now,
                    ApplyTime = now,
                    ApproveScrapperson = "N/A",
                    ApplyTaskStatus = approveTag,
                    FindBoardStatus = "N/A",
                    InternalTask = "N/A",
                    Purpose = "N/A",
                    Category = category
                }).ToList();

                // 9. Dùng transaction để đảm bảo tính toàn vẹn dữ liệu
                await using var transaction = await _sqlContext.Database.BeginTransactionAsync();
                try
                {
                    if (newEntities.Any())
                    {
                        _sqlContext.ScrapLists.AddRange(newEntities);
                        await AddHistoryEntriesAsync(newEntities);
                    }
                    await _sqlContext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    // 10. Trả về thông báo chi tiết
                    var msg = new List<string>();
                    if (newEntities.Any()) msg.Add($"Thêm mới {newEntities.Count} SN");
                    return Ok(new
                    {
                        success = true,
                        message = $"Thêm mới {newEntities.Count} SN thành công.",
                        inserted = newEntities.Count,
                        total = requestSnSet.Count
                    });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    throw; // sẽ vào catch ngoài
                }
            }
            catch (Exception ex)
            {
                // Log lỗi nếu cần
                // _logger.LogError(ex, "Lỗi khi nhập SN chờ SPE approve");
                return StatusCode(500, new
                {
                    message = "Đã xảy ra lỗi khi lưu dữ liệu vào hệ thống.",
                    error = ex.Message
                });
            }
        }
    }
}
