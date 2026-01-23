using API_WEB.Dtos.B28M;
using API_WEB.Dtos.PdRepositorys;
using API_WEB.ModelsDB;
using API_WEB.ModelsOracle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;

namespace API_WEB.Controllers.Repositories
{
    [Route("[controller]")]
    [ApiController]
    public class KhoB28Controller : ControllerBase
    {
        private readonly CSDL_NE _sqlContext;
        private readonly OracleDbContext _oracleContext;

        public KhoB28Controller(CSDL_NE sqlContext, OracleDbContext oracleContext)
        {
            _sqlContext = sqlContext;
            _oracleContext = oracleContext;
        }

        // Them san pham vao bang hiện tại
        [HttpPost("add-serial")]
        public IActionResult PostToTable([FromBody] List<InputDto> b28mDtos)
        {
            if (b28mDtos == null || !b28mDtos.Any())
            {
                return BadRequest(new { message = "Product list is empty or invalid." });
            }

            try
            {
                var errorMessages = new List<string>();
                var seenSerialNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var existingSerialNumbers = new HashSet<string>(
                    _sqlContext.KhoB28s.AsNoTracking().Select(p => p.SerialNumber),
                    StringComparer.OrdinalIgnoreCase);
                var serialsMissingModel = b28mDtos
                    .Where(dto => !string.IsNullOrWhiteSpace(dto.SerialNumber) && string.IsNullOrWhiteSpace(dto.ModelName))
                    .Select(dto => dto.SerialNumber.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var modelNameLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                const int pageSize = 1000;
                for (int i = 0; i < serialsMissingModel.Count; i += pageSize)
                {
                    var chunk = serialsMissingModel.Skip(i).Take(pageSize).ToList();
                    var oracleModels = _oracleContext.OracleDataR107PdStock
                        .AsNoTracking()
                        .Where(item => chunk.Contains(item.SERIAL_NUMBER))
                        .Select(item => new { item.SERIAL_NUMBER, item.MODEL_NAME })
                        .ToList();

                    foreach (var item in oracleModels)
                    {
                        if (!modelNameLookup.ContainsKey(item.SERIAL_NUMBER))
                        {
                            modelNameLookup[item.SERIAL_NUMBER] = item.MODEL_NAME;
                        }
                    }
                }

                foreach (var dto in b28mDtos)
                {
                    // Kiểm tra dữ liệu không hợp lệ
                    var serialNumber = dto.SerialNumber?.Trim();
                    var location = dto.Location?.Trim();
                    var inBy = dto.InBy?.Trim();
                    var modelName = string.IsNullOrWhiteSpace(dto.ModelName) ? null : dto.ModelName.Trim();

                    if (string.IsNullOrEmpty(serialNumber))
                    {
                        errorMessages.Add($"Product with missing SerialNumber: {dto?.ModelName ?? "Unknown Model"}.");
                        continue;
                    }


                    if (string.IsNullOrEmpty(location))
                    {
                        errorMessages.Add($"Product with SerialNumber {serialNumber} has a missing LocationStock.");
                        continue;
                    }

                    if (string.IsNullOrEmpty(inBy))
                    {
                        errorMessages.Add($"Product with SerialNumber {serialNumber} has a missing EntryOp.");
                        continue;
                    }

                    if (!seenSerialNumbers.Add(serialNumber))
                    {
                        errorMessages.Add($"Product with SerialNumber {serialNumber} is duplicated in the request.");
                        continue;
                    }

                    // Kiểm tra xem SerialNumber đã tồn tại hay chưa
                    if (existingSerialNumbers.Contains(serialNumber))
                    {
                        errorMessages.Add($"Product with SerialNumber {serialNumber} already exists in stock.");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(modelName)
                        && modelNameLookup.TryGetValue(serialNumber, out var oracleModelName))
                    {
                        modelName = oracleModelName?.Trim();
                    }

                    // Chuyển từ DTO sang Entity
                    var b28m = new KhoB28
                    {
                        SerialNumber = serialNumber,
                        ModelName = modelName,
                        Location = location,
                        InDate = DateTime.Now,
                        InBy = inBy,
                        Status = "Available"
                    };

                    // Thêm sản phẩm vào database
                    _sqlContext.KhoB28s.Add(b28m);
                }

                // Lưu thay đổi vào database
                _sqlContext.SaveChanges();

                // Phản hồi nếu có lỗi
                if (errorMessages.Any())
                {
                    return Ok(new
                    {
                        message = "Some products were not processed due to errors.",
                        errors = errorMessages
                    });
                }

                // Phản hồi thành công
                return Ok(new { message = "All products were added successfully." });
            }
            catch (Exception ex)
            {
                // Log chi tiết lỗi
                Console.WriteLine($"Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }

                return StatusCode(500, new
                {
                    message = $"Internal server error: {ex.Message}",
                    details = ex.InnerException?.Message
                });
            }
        }

        [HttpPost("export")]
        public async Task<IActionResult> Export([FromBody] KhoB28ExportRequest request)
        {
            if (request?.SerialNumbers == null || !request.SerialNumbers.Any())
            {
                return BadRequest(new { success = false, message = "Danh sách Serial Number không hợp lệ." });
            }

            var results = new List<KhoB28ActionResult>();
            var serials = request.SerialNumbers
                .Select(sn => sn?.Trim().ToUpper())
                .Where(sn => !string.IsNullOrEmpty(sn))
                .Distinct()
                .ToList();

            foreach (var serial in serials)
            {
                var item = await _sqlContext.KhoB28s.FirstOrDefaultAsync(entry => entry.SerialNumber == serial);
                if (item == null)
                {
                    results.Add(new KhoB28ActionResult
                    {
                        SerialNumber = serial,
                        Success = false,
                        Message = "Serial Number không tồn tại."
                    });
                    continue;
                }

                _sqlContext.KhoB28s.Remove(item);
                results.Add(new KhoB28ActionResult
                {
                    SerialNumber = serial,
                    Success = true,
                    Message = "Xuất kho thành công."
                });
            }

            await _sqlContext.SaveChangesAsync();

            return Ok(new
            {
                success = results.Any(r => r.Success),
                results
            });
        }

        [HttpPost("borrow")]
        public async Task<IActionResult> Borrow([FromBody] KhoB28BorrowRequest request)
        {
            if (request?.SerialNumbers == null || !request.SerialNumbers.Any() || string.IsNullOrWhiteSpace(request.BorrowBy))
            {
                return BadRequest(new { success = false, message = "Danh sách Serial Numbers và người mượn là bắt buộc." });
            }

            var results = new List<KhoB28ActionResult>();
            var serials = request.SerialNumbers
                .Select(sn => sn?.Trim().ToUpper())
                .Where(sn => !string.IsNullOrEmpty(sn))
                .Distinct()
                .ToList();

            foreach (var serial in serials)
            {
                var item = await _sqlContext.KhoB28s.FirstOrDefaultAsync(entry => entry.SerialNumber == serial);
                if (item == null)
                {
                    results.Add(new KhoB28ActionResult
                    {
                        SerialNumber = serial,
                        Success = false,
                        Message = "Serial Number không tồn tại."
                    });
                    continue;
                }

                item.Borrower = request.BorrowBy;
                item.BorrowTime = DateTime.Now;
                item.Status = "Borrowed";
                item.Location = null;

                results.Add(new KhoB28ActionResult
                {
                    SerialNumber = serial,
                    Success = true,
                    Message = "Cho mượn thành công."
                });
            }

            await _sqlContext.SaveChangesAsync();

            return Ok(new
            {
                success = results.Any(r => r.Success),
                results
            });
        }

        [HttpPost("return")]
        public async Task<IActionResult> Return([FromBody] KhoB28ReturnRequest request)
        {
            if (request?.SerialNumbers == null || !request.SerialNumbers.Any())
            {
                return BadRequest(new { success = false, message = "Danh sách Serial Numbers không hợp lệ." });
            }

            var results = new List<KhoB28ActionResult>();
            var serials = request.SerialNumbers
                .Select(sn => sn?.Trim().ToUpper())
                .Where(sn => !string.IsNullOrEmpty(sn))
                .Distinct()
                .ToList();

            foreach (var serial in serials)
            {
                var item = await _sqlContext.KhoB28s.FirstOrDefaultAsync(entry => entry.SerialNumber == serial);
                if (item == null)
                {
                    results.Add(new KhoB28ActionResult
                    {
                        SerialNumber = serial,
                        Success = false,
                        Message = "Serial Number không tồn tại."
                    });
                    continue;
                }

                item.Status = "Available";
                item.Borrower = null;
                item.BorrowTime = null;
                item.Location = request.Location ?? item.Location;
                item.InBy = request.ReturnBy ?? item.InBy;
                item.InDate = DateTime.Now;

                results.Add(new KhoB28ActionResult
                {
                    SerialNumber = serial,
                    Success = true,
                    Message = "Trả kho thành công."
                });
            }

            await _sqlContext.SaveChangesAsync();

            return Ok(new
            {
                success = results.Any(r => r.Success),
                results
            });
        }

        [HttpGet("get-all")]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                // 1. Lấy dữ liệu từ SQL Server (Chỉ lấy các cột cần thiết để tiết kiệm RAM)
                // Dùng AsNoTracking để truy vấn nhanh nhất có thể
                var stocks = await _sqlContext.KhoB28s
                    .AsNoTracking()
                    .Select(s => new {
                        s.SerialNumber,
                        s.ModelName,
                        s.Location,
                        s.InDate,
                        s.InBy,
                        s.Status,
                        s.Borrower,
                        s.BorrowTime
                    })
                    .ToListAsync();

                if (stocks == null || !stocks.Any())
                {
                    return Ok(new { data = new List<PdStockOracleDto>() });
                }

                // 2. Lọc danh sách Serial Number để gửi sang Oracle
                var snList = stocks
                    .Select(s => s.SerialNumber)
                    .Where(sn => !string.IsNullOrWhiteSpace(sn))
                    .Distinct()
                    .ToList();

                // 3. Truy vấn Oracle bằng Raw SQL (Giống logic SearchController)
                var oracleLookup = new Dictionary<string, (string WipGroup, string ErrorFlag, string MoNumber, string ProductLine)>(StringComparer.OrdinalIgnoreCase);

                using (var conn = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString))
                {
                    await conn.OpenAsync();

                    // Chia nhỏ 1000 SN để tránh lỗi ORA-01795
                    int pageSize = 999;
                    for (int i = 0; i < snList.Count; i += pageSize)
                    {
                        var chunk = snList.Skip(i).Take(pageSize).ToList();
                        // Tạo chuỗi IN ('SN1', 'SN2', ...)
                        var inClause = string.Join(",", chunk.Select(sn => $"'{sn}'"));

                        string sql = $@"SELECT a.SERIAL_NUMBER, a.WIP_GROUP, a.ERROR_FLAG, a.MO_NUMBER, b.PRODUCT_LINE 
                                FROM SFISM4.R107 a
                                INNER JOIN SFIS1.C_MODEL_DESC_T b ON a.MODEL_NAME = b.MODEL_NAME 
                                WHERE a.SERIAL_NUMBER IN ({inClause})";

                        using (var cmd = new OracleCommand(sql, conn))
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string sn = reader["SERIAL_NUMBER"]?.ToString();
                                if (sn != null && !oracleLookup.ContainsKey(sn))
                                {
                                    oracleLookup[sn] = (
                                        reader["WIP_GROUP"]?.ToString() ?? "N/A",
                                        reader["ERROR_FLAG"]?.ToString() ?? "N/A",
                                        reader["MO_NUMBER"]?.ToString() ?? "N/A",
                                        reader["PRODUCT_LINE"]?.ToString() ?? "N/A"
                                    );
                                }
                            }
                        }
                    }
                }

                // 4. Ghép dữ liệu (Join in-memory)
                var result = stocks.Select(s => new B28OracleDto
                {
                    SerialNumber = s.SerialNumber,
                    ModelName = s.ModelName,
                    Location = s.Location,
                    InDate = s.InDate,
                    InBy = s.InBy,
                    Borrower = s.Borrower,
                    Status = s.Status,
                    BorrowTime = s.BorrowTime,
                    WipGroup = oracleLookup.ContainsKey(s.SerialNumber ?? "") ? oracleLookup[s.SerialNumber].WipGroup : "N/A",
                    ErrorFlag = oracleLookup.ContainsKey(s.SerialNumber ?? "") ? oracleLookup[s.SerialNumber].ErrorFlag : "N/A",
                    MoNumber = oracleLookup.ContainsKey(s.SerialNumber ?? "") ? oracleLookup[s.SerialNumber].MoNumber : "N/A",
                    ProductLine = oracleLookup.ContainsKey(s.SerialNumber ?? "") ? oracleLookup[s.SerialNumber].ProductLine : "N/A",
                }).ToList();

                return Ok(new { data = result });
            }
            catch (Exception ex)
            {
                // Log lỗi chi tiết để kiểm tra nếu vẫn bị treo
                return StatusCode(500, new { message = "Lỗi hệ thống khi truy vấn dữ liệu chéo.", error = ex.Message });
            }
        }

        // API: Lấy toàn bộ dữ liệu kho để xuất Excel
        [HttpGet("export-excel")]
        public async Task<IActionResult> ExportExcel()
        {
            try
            {
                // 1. Lấy dữ liệu SQL Server (Chỉ lấy cột cần)
                var stocks = await _sqlContext.KhoB28s.AsNoTracking()
                    .Select(s => new { s.SerialNumber, s.ModelName, s.InDate, s.InBy, s.Status, s.BorrowTime, s.Borrower, s.Location})
                    .ToListAsync();

                var snList = stocks.Select(s => s.SerialNumber).Where(sn => !string.IsNullOrEmpty(sn)).ToList();

                // 2. Truy vấn Oracle bằng Raw SQL (Giống SearchController)
                var oracleData = new Dictionary<string, (string Wip, string Error, string Mo, string Product)>();

                using (var conn = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString)) // Lấy connection string từ config
                {
                    await conn.OpenAsync();
                    // Chia 1000 để tránh giới hạn ORA-01795
                    for (int i = 0; i < snList.Count; i += 1000)
                    {
                        var chunk = snList.Skip(i).Take(1000).ToList();
                        var inClause = string.Join(",", chunk.Select(sn => $"'{sn}'"));

                        string sql = $@"SELECT a.SERIAL_NUMBER, a.WIP_GROUP, a.ERROR_FLAG, a.MO_NUMBER, b.PRODUCT_LINE 
                                FROM SFISM4.R107 a
                                INNER JOIN SFIS1.C_MODEL_DESC_T b ON a.MODEL_NAME = b.MODEL_NAME 
                                WHERE a.SERIAL_NUMBER IN ({inClause})";

                        using (var cmd = new OracleCommand(sql, conn))
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                oracleData[reader["SERIAL_NUMBER"].ToString()] =
                                    (reader["WIP_GROUP"].ToString(), reader["ERROR_FLAG"].ToString(), reader["MO_NUMBER"].ToString(), reader["PRODUCT_LINE"].ToString());
                            }
                        }
                    }
                }

                // 3. Kết hợp dữ liệu
                var result = stocks.Select(s => new {
                    s.SerialNumber,
                    s.ModelName,
                    s.Location,
                    s.InBy,
                    s.InDate,
                    s.Status,
                    s.Borrower,
                    s.BorrowTime,
                    WipGroup = oracleData.ContainsKey(s.SerialNumber) ? oracleData[s.SerialNumber].Wip : "N/A",
                    ErrorFlag = oracleData.ContainsKey(s.SerialNumber) ? oracleData[s.SerialNumber].Error : "N/A",
                    MoNumber = oracleData.ContainsKey(s.SerialNumber) ? oracleData[s.SerialNumber].Mo : "N/A",
                    ProductLine = oracleData.ContainsKey(s.SerialNumber) ? oracleData[s.SerialNumber].Product : "N/A"
                });

                return Ok(new { data = result });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        // API: Lấy tổng số lượng SN và Carton nhanh
        [HttpGet("get-count")]
        public async Task<IActionResult> GetSummaryCount()
        {
            try
            {
                // Đếm tổng số SerialNumber
                var totalSn = await _sqlContext.KhoB28s.CountAsync();

                return Ok(new
                {
                    totalSn = totalSn
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi đếm dữ liệu kho.", error = ex.Message });
            }
        }

        // API: Thống kê số lượng theo từng ModelName
        [HttpGet("get-model")]
        public async Task<IActionResult> GetSummaryByModel()
        {
            try
            {
                var modelSummary = await _sqlContext.KhoB28s
                    .AsNoTracking()
                    .Where(s => !string.IsNullOrEmpty(s.ModelName))
                    .GroupBy(s => s.ModelName)
                    .Select(g => new
                    {
                        ModelName = g.Key,
                        Count = g.Count()
                    })
                    .OrderByDescending(x => x.Count) // Sắp xếp từ nhiều đến ít
                    .ToListAsync();

                return Ok(new { data = modelSummary });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi thống kê theo Model.", error = ex.Message });
            }
        }

    }

    public class KhoB28ExportRequest
    {
        public List<string> SerialNumbers { get; set; } = new();
    }

    public class KhoB28BorrowRequest
    {
        public List<string> SerialNumbers { get; set; } = new();
        public string BorrowBy { get; set; } = null!;
    }

    public class KhoB28ReturnRequest
    {
        public List<string> SerialNumbers { get; set; } = new();
        public string? Location { get; set; }
        public string? ReturnBy { get; set; }
    }

    public class KhoB28ActionResult
    {
        public string SerialNumber { get; set; } = null!;
        public bool Success { get; set; }
        public string Message { get; set; } = null!;
    }
}
