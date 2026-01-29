#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using API_WEB.Dtos.DPU;
using API_WEB.ModelsDB;
using API_WEB.ModelsOracle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;

namespace API_WEB.Controllers.DPU
{
    [Route("[controller]")]
    [ApiController]
    public class ConfigController : ControllerBase
    {
        private readonly CSDL_NE _sqlContext;
        private readonly OracleDbContext _oracleContext;

        public ConfigController(CSDL_NE sqlContext, OracleDbContext oracleContext)
        {
            _sqlContext = sqlContext;
            _oracleContext = oracleContext;
        }

        [HttpPost("add-dpu-manager")]
        public async Task<IActionResult> AddDpuManager([FromBody] AddDpuManagerRequest request)
        {
            if (request == null || request.SerialNumbers == null)
            {
                return BadRequest("Dữ liệu không hợp lệ.");
            }

            var inputSerials = request.SerialNumbers?
                .Where(sn => !string.IsNullOrWhiteSpace(sn))
                .Select(sn => sn.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!inputSerials.Any())
            {
                return BadRequest("Danh sách serialNumber trống.");
            }

            if (string.IsNullOrWhiteSpace(request.HbMb) || string.IsNullOrWhiteSpace(request.TypeBonepile))
            {
                return BadRequest("HB_MB và typeBonepile là bắt buộc.");
            }

            var existingInSql = await _sqlContext.DPUManagers
                .Where(d => inputSerials.Contains(d.SerialNumber))
                .Select(d => d.SerialNumber)
                .ToListAsync();

            var existingSet = new HashSet<string>(existingInSql, StringComparer.OrdinalIgnoreCase);
            var newSerials = inputSerials.Where(sn => !existingSet.Contains(sn)).ToList();

            if (!newSerials.Any())
                return Ok(new { Message = "Tất cả Serial Numbers đã tồn tại trong hệ thống.", ExistingCount = existingInSql.Count });

            var oracleDataMap = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);

            using (var conn = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString))
            {
                await conn.OpenAsync();

                // Chia chunk 1000 để tránh lỗi ORA-01795
                for (int i = 0; i < newSerials.Count; i += 1000)
                {
                    var chunk = newSerials.Skip(i).Take(1000).ToList();
                    var inClause = string.Join(",", chunk.Select(sn => $"'{sn}'"));

                    // Senior Note: Sử dụng Subquery để lấy bản ghi đầu tiên (First Fail) cho từng Serial trong 1 lần quét
                    string sql = $@"
                SELECT SERIAL_NUMBER, MODEL_NAME, DATA4, IN_STATION_TIME
                FROM (
                    SELECT t.SERIAL_NUMBER, t.MODEL_NAME, t.DATA4, t.IN_STATION_TIME,
                           ROW_NUMBER() OVER (PARTITION BY t.SERIAL_NUMBER ORDER BY t.IN_STATION_TIME ASC) as rn
                    FROM sfism4.r_fail_atedata_t t
                    WHERE t.data4 LIKE '%DPU_MEM%' 
                    AND t.SERIAL_NUMBER IN ({inClause})
                ) WHERE rn = 1";

                    using (var cmd = new OracleCommand(sql, conn))
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            oracleDataMap[reader["SERIAL_NUMBER"].ToString()] = new
                            {
                                ModelName = reader["MODEL_NAME"]?.ToString(),
                                DescFirstFail = reader["DATA4"]?.ToString(),
                                FirstFailTime = reader["IN_STATION_TIME"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["IN_STATION_TIME"])
                            };
                        }
                    }
                }
            }
            // 4. Chuẩn bị dữ liệu để lưu vào SQL Server
            var insertItems = new List<DPUManager>();
            var notFoundInOracle = new List<string>();

            foreach (var sn in newSerials)
            {
                if (oracleDataMap.TryGetValue(sn, out var o))
                {
                    insertItems.Add(new DPUManager
                    {
                        SerialNumber = sn,
                        HB_MB = request.HbMb.Trim(),
                        TypeBonpile = request.TypeBonepile.Trim(),
                        ModelName = o.ModelName,
                        DescFirstFail = o.DescFirstFail,
                        First_Fail_Time = o.FirstFailTime
                    });
                }
                else
                {
                    notFoundInOracle.Add(sn);
                    // Senior tư duy: Có thể vẫn add vào DB với thông tin N/A nếu nghiệp vụ cho phép, 
                    // hoặc bỏ qua như logic hiện tại.
                }
            }

            // 5. Thực thi Transaction
            if (insertItems.Any())
            {
                _sqlContext.DPUManagers.AddRange(insertItems);
                await _sqlContext.SaveChangesAsync();
            }

            return Ok(new
            {
                Success = true,
                InsertedCount = insertItems.Count,
                ExistingCount = existingInSql.Count,
                NotFoundInOracle = notFoundInOracle,
                Message = $"Thành công: {insertItems.Count}. Đã tồn tại: {existingInSql.Count}. Không tìm thấy ở Oracle: {notFoundInOracle.Count}."
            });
        }

        [HttpPost("update-dpu-fields")]
        public async Task<IActionResult> UpdateDpuFields([FromBody] UpdateDpuFieldsRequest request)
        {
            if (request.SerialNumbers == null || !request.SerialNumbers.Any())
                return BadRequest("Danh sách Serial Number không được để trống.");

            try
            {
                // 1. Lấy danh sách các bản ghi cần update từ SQL Server
                var entities = await _sqlContext.DPUManagers
                    .Where(d => request.SerialNumbers.Contains(d.SerialNumber))
                    .ToListAsync();

                if (!entities.Any())
                    return NotFound("Không tìm thấy Serial Number nào trong hệ thống.");

                // 2. Áp dụng thay đổi (Chỉ update những trường có dữ liệu trong request)
                foreach (var item in entities)
                {
                    if (request.DDRToolResult != null) item.DDRToolResult = request.DDRToolResult;
                    if (request.NV_Instruction != null) item.NV_Instruction = request.NV_Instruction;
                    if (request.ReworkFXV != null) item.ReworkFXV = request.ReworkFXV;
                    if (request.CurrentStatus != null) item.CurrentStatus = request.CurrentStatus;
                    if (request.Remark != null) item.Remark = request.Remark;
                    if (request.Remark2 != null) item.Remark2 = request.Remark2;

                    // Gọi logic tính toán tự động
                    item.UpdateBusinessLogic();
                }

                // 3. Lưu vào Database
                await _sqlContext.SaveChangesAsync();

                return Ok(new
                {
                    Success = true,
                    UpdatedCount = entities.Count,
                    Message = $"Đã cập nhật thành công {entities.Count} bản ghi."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Lỗi hệ thống: {ex.Message}");
            }
        }

        [HttpPost("search-dpu-manager")]
        public async Task<IActionResult> SearchDpuManager([FromBody] List<string> serialNumbers)
        {
            if (serialNumbers == null || !serialNumbers.Any())
            {
                return BadRequest("Danh sách Serial Number không được để trống.");
            }

            var distinctSerials = serialNumbers
                .Where(sn => !string.IsNullOrWhiteSpace(sn))
                .Select(sn => sn.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!distinctSerials.Any())
            {
                return BadRequest("Danh sách Serial Number không hợp lệ.");
            }

            var data = await _sqlContext.DPUManagers
                .AsNoTracking()
                .Where(item => distinctSerials.Contains(item.SerialNumber))
                .ToListAsync();

            if (!data.Any())
            {
                return NotFound("Không tìm thấy Serial Number nào trong hệ thống.");
            }

            var orderMap = distinctSerials
                .Select((sn, index) => new { sn, index })
                .ToDictionary(item => item.sn, item => item.index, StringComparer.OrdinalIgnoreCase);

            var orderedData = data
                .OrderBy(item => orderMap.TryGetValue(item.SerialNumber, out var index) ? index : int.MaxValue)
                .ToList();

            return Ok(new
            {
                Success = true,
                TotalRecord = orderedData.Count,
                Data = orderedData
            });
        }

        //UPDATE FT_OFF
        [HttpPost("sync-error-ft-off")]
        public async Task<IActionResult> SyncErrorFTOff([FromBody] List<string> serialNumbers)
        {
            // 1. Validate Input
            if (serialNumbers == null || !serialNumbers.Any())
            {
                return BadRequest("Danh sách Serial Number không được để trống.");
            }

            // Chuẩn hóa đầu vào (Trim + Upper + Distinct) để tránh lỗi so sánh
            var distinctSerials = serialNumbers
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToUpper())
                .Distinct()
                .ToList();

            try
            {
                // 2. Lấy dữ liệu đích từ SQL Server (Chỉ lấy những bản ghi có tồn tại)
                var sqlEntities = await _sqlContext.DPUManagers
                    .Where(x => distinctSerials.Contains(x.SerialNumber))
                    .ToListAsync();

                if (!sqlEntities.Any())
                {
                    return NotFound("Không tìm thấy Serial Number nào trong hệ thống SQL Server.");
                }

                // Tạo Dictionary để map nhanh dữ liệu từ Oracle
                var oracleDataMap = new Dictionary<string, string>();

                // 3. Truy vấn Oracle (Batch Query - Xử lý theo chunk để tránh lỗi quá 1000 phần tử trong IN clause)
                using (var conn = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString))
                {
                    await conn.OpenAsync();

                    int pageSize = 1000;
                    for (int i = 0; i < distinctSerials.Count; i += pageSize)
                    {
                        var chunk = distinctSerials.Skip(i).Take(pageSize).ToList();
                        var inClause = string.Join(",", chunk.Select(s => $"'{s}'"));

                        // Query tối ưu lấy dòng mới nhất (Latest) theo PASS_DATE
                        string sql = $@"
                    SELECT SERIAL_NUMBER, ERROR_DESC
                    FROM (
                        SELECT 
                            SERIAL_NUMBER, 
                            DATA5 AS ERROR_DESC, 
                            ROW_NUMBER() OVER (PARTITION BY SERIAL_NUMBER ORDER BY PASS_DATE DESC) AS rn
                        FROM SFISM4.R_ULT_RESULT_T 
                        WHERE GROUP_NAME = 'FT_OFF' 
                          AND SERIAL_NUMBER IN ({inClause})
                    )
                    WHERE rn = 1";

                        using (var cmd = new OracleCommand(sql, conn))
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var sn = reader["SERIAL_NUMBER"]?.ToString();
                                var err = reader["ERROR_DESC"]?.ToString();
                                if (!string.IsNullOrEmpty(sn))
                                {
                                    oracleDataMap[sn] = err;
                                }
                            }
                        }
                    }
                }

                // 4. Update dữ liệu vào SQL Server (In-Memory update)
                int updatedCount = 0;
                var resultList = new List<object>(); // Danh sách trả về cho Client

                foreach (var entity in sqlEntities)
                {
                    string oldRemark2 = entity.Remark2;
                    string newErrorDesc = "N/A"; // Mặc định nếu không tìm thấy bên Oracle

                    if (oracleDataMap.TryGetValue(entity.SerialNumber, out var oracleError))
                    {
                        newErrorDesc = oracleError;

                        // Chỉ update nếu dữ liệu có sự thay đổi để tối ưu performance DB
                        if (entity.Remark2 != newErrorDesc)
                        {
                            entity.Remark2 = newErrorDesc;
                            entity.Updated_At = DateTime.Now;
                            updatedCount++;
                        }
                    }

                    // Thêm vào danh sách trả về để User kiểm tra
                    resultList.Add(new
                    {
                        SerialNumber = entity.SerialNumber,
                        OldRemark2 = oldRemark2,
                        NewRemark2 = newErrorDesc, // Chính là giá trị ERROR_DESC từ Oracle
                        Status = oracleDataMap.ContainsKey(entity.SerialNumber) ? "Synced" : "NotFoundInOracle"
                    });
                }

                // 5. Commit xuống Database
                if (updatedCount > 0)
                {
                    await _sqlContext.SaveChangesAsync();
                }

                return Ok(new
                {
                    Success = true,
                    Message = $"Đã đồng bộ {updatedCount} bản ghi.",
                    TotalRequested = distinctSerials.Count,
                    TotalFoundInSql = sqlEntities.Count,
                    Data = resultList
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Lỗi hệ thống: {ex.Message}");
            }
        }

        //UPDATE FT_OFF
        [HttpPost("sync-error-ft-on")]
        public async Task<IActionResult> SyncErrorFTOnline([FromBody] List<string> serialNumbers)
        {
            // 1. Validate Input
            if (serialNumbers == null || !serialNumbers.Any())
            {
                return BadRequest("Danh sách Serial Number không được để trống.");
            }

            // Chuẩn hóa đầu vào (Trim + Upper + Distinct) để tránh lỗi so sánh
            var distinctSerials = serialNumbers
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToUpper())
                .Distinct()
                .ToList();

            try
            {
                // 2. Lấy dữ liệu đích từ SQL Server (Chỉ lấy những bản ghi có tồn tại)
                var sqlEntities = await _sqlContext.DPUManagers
                    .Where(x => distinctSerials.Contains(x.SerialNumber))
                    .ToListAsync();

                if (!sqlEntities.Any())
                {
                    return NotFound("Không tìm thấy Serial Number nào trong hệ thống SQL Server.");
                }

                // Tạo Dictionary để map nhanh dữ liệu từ Oracle
                var oracleDataMap = new Dictionary<string, string>();

                // 3. Truy vấn Oracle (Batch Query - Xử lý theo chunk để tránh lỗi quá 1000 phần tử trong IN clause)
                using (var conn = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString))
                {
                    await conn.OpenAsync();

                    int pageSize = 1000;
                    for (int i = 0; i < distinctSerials.Count; i += pageSize)
                    {
                        var chunk = distinctSerials.Skip(i).Take(pageSize).ToList();
                        var inClause = string.Join(",", chunk.Select(s => $"'{s}'"));

                        // Query tối ưu lấy dòng mới nhất (Latest) theo PASS_DATE
                        string sql = $@"
                    SELECT SERIAL_NUMBER, ERROR_DESC
                    FROM (
                        SELECT 
                            SERIAL_NUMBER, 
                            DATA5 AS ERROR_DESC, 
                            ROW_NUMBER() OVER (PARTITION BY SERIAL_NUMBER ORDER BY PASS_DATE DESC) AS rn
                        FROM SFISM4.R_ULT_RESULT_T 
                        WHERE GROUP_NAME = 'FT' 
                          AND SERIAL_NUMBER IN ({inClause})
                    )
                    WHERE rn = 1";

                        using (var cmd = new OracleCommand(sql, conn))
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var sn = reader["SERIAL_NUMBER"]?.ToString();
                                var err = reader["ERROR_DESC"]?.ToString();
                                if (!string.IsNullOrEmpty(sn))
                                {
                                    oracleDataMap[sn] = err;
                                }
                            }
                        }
                    }
                }

                // 4. Update dữ liệu vào SQL Server (In-Memory update)
                int updatedCount = 0;
                var resultList = new List<object>(); // Danh sách trả về cho Client

                foreach (var entity in sqlEntities)
                {
                    string oldRemark2 = entity.Remark2;
                    string newErrorDesc = "N/A"; // Mặc định nếu không tìm thấy bên Oracle

                    if (oracleDataMap.TryGetValue(entity.SerialNumber, out var oracleError))
                    {
                        newErrorDesc = oracleError;

                        // Chỉ update nếu dữ liệu có sự thay đổi để tối ưu performance DB
                        if (entity.Remark2 != newErrorDesc)
                        {
                            entity.Remark2 = newErrorDesc;
                            entity.Updated_At = DateTime.Now;
                            updatedCount++;
                        }
                    }

                    // Thêm vào danh sách trả về để User kiểm tra
                    resultList.Add(new
                    {
                        SerialNumber = entity.SerialNumber,
                        OldRemark2 = oldRemark2,
                        NewRemark2 = newErrorDesc, // Chính là giá trị ERROR_DESC từ Oracle
                        Status = oracleDataMap.ContainsKey(entity.SerialNumber) ? "Synced" : "NotFoundInOracle"
                    });
                }

                // 5. Commit xuống Database
                if (updatedCount > 0)
                {
                    await _sqlContext.SaveChangesAsync();
                }

                return Ok(new
                {
                    Success = true,
                    Message = $"Đã đồng bộ {updatedCount} bản ghi.",
                    TotalRequested = distinctSerials.Count,
                    TotalFoundInSql = sqlEntities.Count,
                    Data = resultList
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Lỗi hệ thống: {ex.Message}");
            }
        }

        // API: Lấy toàn bộ dữ liệu DPU kèm tính toán Aging
        [HttpGet("get-all-dpu-aging")]
        public async Task<IActionResult> GetAllDpuWithAging()
        {
            try
            {
                // 1. Lấy dữ liệu từ DB (Sử dụng AsNoTracking để tối ưu tốc độ đọc)
                var rawData = await _sqlContext.DPUManagers
                    .AsNoTracking()
                    .OrderByDescending(x => x.Created_At) // Sắp xếp mới nhất lên đầu
                    .ToListAsync();

                // 2. Tính toán Aging trong bộ nhớ (In-Memory Processing)
                // Lý do: DateTime.Now phía server chính xác hơn và format chuỗi phức tạp trong SQL rất chậm.
                var currentTime = DateTime.Now;

                var result = rawData.Select(item =>
                {
                    double agingHours = 0;
                    string agingText = "";

                    if (item.First_Fail_Time.HasValue)
                    {
                        var span = currentTime - item.First_Fail_Time.Value;

                        // Tính tổng giờ (để sort/tô màu cảnh báo)
                        agingHours = Math.Round(span.TotalHours, 1);

                        // Format hiển thị đẹp (Ví dụ: 2d 4h 30m)
                        if (span.TotalDays >= 1)
                        {
                            agingText = $"{span.Days}d {span.Hours}h";
                        }
                        else
                        {
                            agingText = $"{span.Hours}h {span.Minutes}m";
                        }
                    }
                    else
                    {
                        agingText = "N/A";
                    }

                    // Trả về Anonymous Object (hoặc bạn có thể tạo DTO riêng)
                    return new
                    {
                        // Copy lại các thuộc tính gốc cần thiết
                        item.ID,
                        item.SerialNumber,
                        item.ModelName,
                        item.HB_MB,
                        item.TypeBonpile,
                        item.TYPE,
                        item.First_Fail_Time,
                        item.DescFirstFail,
                        item.DDRToolResult,
                        item.QTY_RAM_FAIL,
                        item.NV_Instruction,
                        item.ReworkFXV,
                        item.CurrentStatus,
                        item.Remark,
                        item.Remark2,
                        item.Created_At,
                        item.Updated_At,

                        // Cột tính toán thêm
                        AgingHours = agingHours, // Dùng cột này để sort trên Grid hoặc Excel
                        AgingText = agingText    // Dùng cột này để hiển thị
                    };
                });

                return Ok(new
                {
                    Success = true,
                    TotalRecord = rawData.Count,
                    Data = result
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Lỗi hệ thống: {ex.Message}");
            }
        }
    }
}
