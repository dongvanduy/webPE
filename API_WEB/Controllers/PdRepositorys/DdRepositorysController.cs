using API_WEB.Dtos.PdRepositorys;
using API_WEB.ModelsDB;
using API_WEB.ModelsOracle;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using API_WEB.Models;
using PdStock = API_WEB.ModelsDB.PdStock;
using PdStockHistory = API_WEB.ModelsDB.PdStockHistory;

namespace API_WEB.Controllers.PdRepositorys
{
    [Route("[controller]")]
    [ApiController]
    public class DdRepositorysController : ControllerBase
    {
        private readonly CSDL_NE _sqlContext;
        private readonly OracleDbContext _oracleContext;

        public DdRepositorysController(CSDL_NE sqlContext, OracleDbContext oracleContext)
        {
            _sqlContext = sqlContext;
            _oracleContext = oracleContext;
        }

        [HttpGet("GetStockClassification")]
        public async Task<IActionResult> GetStockClassification()
        {
            try
            {
                // 1. Lấy danh sách SerialNumber từ SQL Server (PdStock)
                var stocks = await _sqlContext.PdStocks
                    .AsNoTracking()
                    .Select(s => s.SerialNumber)
                    .Where(sn => !string.IsNullOrEmpty(sn))
                    .ToListAsync();

                if (!stocks.Any())
                {
                    return Ok(new { b31m = 0, b23f = 0 });
                }

                int countB31M = 0;
                int countB23F = 0;

                // 2. Truy vấn Oracle để kiểm tra WIP_GROUP
                using (var conn = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString))
                {
                    await conn.OpenAsync();
                    int pageSize = 999;
                    for (int i = 0; i < stocks.Count; i += pageSize)
                    {
                        var chunk = stocks.Skip(i).Take(pageSize).ToList();
                        var inClause = string.Join(",", chunk.Select(sn => $"'{sn}'"));

                        // Câu lệnh SQL kiểm tra trực tiếp wip_group
                        string sql = $@"SELECT a.SERIAL_NUMBER, a.WIP_GROUP, a.ERROR_FLAG, a.MO_NUMBER, b.PRODUCT_LINE 
                                FROM SFISM4.R107 a
                                INNER JOIN SFIS1.C_MODEL_DESC_T b ON a.MODEL_NAME = b.MODEL_NAME 
                                WHERE a.SERIAL_NUMBER IN ({inClause})";

                        using (var cmd = new OracleCommand(sql, conn))
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string wipGroup = reader["WIP_GROUP"]?.ToString() ?? "";

                                // Phân loại: Nếu chứa 'B31M' thì tính là B31M, còn lại là B23F
                                if (wipGroup.Contains("B31M"))
                                {
                                    countB31M++;
                                }
                                else
                                {
                                    countB23F++;
                                }
                            }
                        }
                    }
                }

                return Ok(new { b31m = countB31M, b23f = countB23F });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // API: Lấy toàn bộ dữ liệu kho để xuất Excel
        [HttpGet("ExportInventoryExcel")]
        public async Task<IActionResult> ExportInventoryExcel()
        {
            try
            {
                // 1. Lấy dữ liệu SQL Server (Chỉ lấy cột cần)
                var stocks = await _sqlContext.PdStocks.AsNoTracking()
                    .Select(s => new { s.SerialNumber, s.ModelName, s.CartonNo, s.LocationStock, s.EntryDate, s.EntryOp })
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
                    s.CartonNo,
                    Location = s.LocationStock,
                    WipGroup = oracleData.ContainsKey(s.SerialNumber) ? oracleData[s.SerialNumber].Wip : "N/A",
                    ErrorFlag = oracleData.ContainsKey(s.SerialNumber) ? oracleData[s.SerialNumber].Error : "N/A",
                    MoNumber = oracleData.ContainsKey(s.SerialNumber) ? oracleData[s.SerialNumber].Mo : "N/A",
                    ProductLine = oracleData.ContainsKey(s.SerialNumber) ? oracleData[s.SerialNumber].Product : "N/A",
                    EntryDate = s.EntryDate?.ToString("yyyy-MM-dd HH:mm:ss"),
                    EntryOP = s.EntryOp
                });

                return Ok(new { data = result });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        // API: Lấy tổng số lượng SN và Carton nhanh
        [HttpGet("GetStockSummaryCount")]
        public async Task<IActionResult> GetStockSummaryCount()
        {
            try
            {
                // Đếm tổng số SerialNumber
                var totalSn = await _sqlContext.PdStocks.CountAsync();

                // Đếm số lượng Carton duy nhất (Distinct)
                var totalCartons = await _sqlContext.PdStocks
                    .Where(s => s.CartonNo != null && s.CartonNo != "")
                    .Select(s => s.CartonNo)
                    .Distinct()
                    .CountAsync();

                return Ok(new
                {
                    totalSn = totalSn,
                    totalCartons = totalCartons
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi đếm dữ liệu kho.", error = ex.Message });
            }
        }

        // API: Thống kê số lượng theo từng ModelName
        [HttpGet("GetStockSummaryByModel")]
        public async Task<IActionResult> GetStockSummaryByModel()
        {
            try
            {
                var modelSummary = await _sqlContext.PdStocks
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

        [HttpGet("GetAllWithR107")]
        public async Task<IActionResult> GetAllWithR107()
        {
            try
            {
                // 1. Lấy dữ liệu từ SQL Server (Chỉ lấy các cột cần thiết để tiết kiệm RAM)
                // Dùng AsNoTracking để truy vấn nhanh nhất có thể
                var stocks = await _sqlContext.PdStocks
                    .AsNoTracking()
                    .Select(s => new {
                        s.SerialNumber,
                        s.ModelName,
                        s.CartonNo,
                        s.LocationStock,
                        s.EntryDate,
                        s.EntryOp
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
                var result = stocks.Select(s => new PdStockOracleDto
                {
                    SerialNumber = s.SerialNumber,
                    ModelName = s.ModelName,
                    CartonNo = s.CartonNo,
                    LocationStock = s.LocationStock,
                    EntryDate = s.EntryDate,
                    EntryOp = s.EntryOp,
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

        // Tìm sản phẩm theo danh sách serialNumber
        [HttpPost("GetBySerialNumber")]
        public IActionResult GetBySerialNumber([FromBody] List<string> serialNumbers)
        {
            if (serialNumbers == null || !serialNumbers.Any())
            {
                return BadRequest("Serial numbers list cannot be null or empty.");
            }

            var results = _sqlContext.PdStocks
                .Where(s => serialNumbers.Contains(s.SerialNumber))
                .ToList();

            if (results == null || !results.Any())
            {
                return NotFound();
            }

            return Ok(new { data = results });
        }

        //Tìm sản phẩm theo ModelName
        [HttpPost("GetByModelName")]
        public IActionResult GetByModelName([FromBody] List<string> modelNames)
        {
            if (modelNames == null || !modelNames.Any())
            {
                return BadRequest("Serial numbers list cannot be null or empty.");
            }

            var results = _sqlContext.PdStocks
                .Where(s => modelNames.Contains(s.ModelName))
                .ToList();

            if (results == null || !results.Any())
            {
                return NotFound();
            }

            return Ok(new { data = results });
        }

        // Tìm sản phẩm theo CartonNo
        [HttpPost("GetByCartonNo")]
        public IActionResult GetByCartonNo([FromBody] List<string> CartonNos)
        {
            // 1. Kiểm tra đầu vào
            if (CartonNos == null || !CartonNos.Any())
            {
                return BadRequest(new { message = "Danh sách CartonNo không được để trống." });
            }

            // 2. Truy vấn
            var results = _sqlContext.PdStocks
                .AsNoTracking() // Dùng AsNoTracking để tăng hiệu năng tra cứu
                .Where(s => CartonNos.Contains(s.CartonNo))
                .ToList();

            // 3. Trả về kết quả (Kể cả rỗng vẫn trả về 200 Ok)
            return Ok(new
            {
                data = results ?? new List<PdStock>(),
                count = results?.Count ?? 0
            });
        }

        // Them san pham vao bang hiện tại
        [HttpPost("PostToTable")]
        public IActionResult PostToTable([FromBody] List<AddPdStockDto> pdStockDtos)
        {
            if (pdStockDtos == null || !pdStockDtos.Any())
            {
                return BadRequest(new { message = "Product list is empty or invalid." });
            }

            try
            {
                var errorMessages = new List<string>();
                var seenSerialNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var existingSerialNumbers = new HashSet<string>(
                    _sqlContext.PdStocks.AsNoTracking().Select(p => p.SerialNumber),
                    StringComparer.OrdinalIgnoreCase);
                var serialsMissingModel = pdStockDtos
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

                foreach (var dto in pdStockDtos)
                {
                    // Kiểm tra dữ liệu không hợp lệ
                    var serialNumber = dto.SerialNumber?.Trim();
                    var cartonNo = string.IsNullOrWhiteSpace(dto.CartonNo) ? "N/A" : dto.CartonNo.Trim();
                    var locationStock = dto.LocationStock?.Trim();
                    var entryOp = dto.EntryOp?.Trim();
                    var modelName = string.IsNullOrWhiteSpace(dto.ModelName) ? null : dto.ModelName.Trim();

                    if (string.IsNullOrEmpty(serialNumber))
                    {
                        errorMessages.Add($"Product with missing SerialNumber: {dto?.ModelName ?? "Unknown Model"}.");
                        continue;
                    }

                    if (string.IsNullOrEmpty(cartonNo))
                    {
                        errorMessages.Add($"Product with SerialNumber {serialNumber} has a missing CartonNo.");
                        continue;
                    }

                    if (string.IsNullOrEmpty(locationStock))
                    {
                        errorMessages.Add($"Product with SerialNumber {serialNumber} has a missing LocationStock.");
                        continue;
                    }

                    if (string.IsNullOrEmpty(entryOp))
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
                    var pdStock = new PdStock
                    {
                        SerialNumber = serialNumber,
                        ModelName = modelName,
                        CartonNo = cartonNo,
                        LocationStock = locationStock,
                        EntryDate = DateTime.Now,
                        EntryOp = entryOp
                    };

                    // Thêm sản phẩm vào database
                    _sqlContext.PdStocks.Add(pdStock);
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

        //============== Xóa sản phẩm khỏi kho hiện tại==========
        [HttpDelete("DeleteBySerialNumbers")]
        public IActionResult DeleteBySerialNumbers([FromBody] List<DeletePdStockDto> deletePdStockDtos)
        {
            if (deletePdStockDtos == null || !deletePdStockDtos.Any())
            {
                return BadRequest("Invalid or empty product data.");
            }

            try
            {
                var errorMessages = new List<string>();
                var notFoundSerialNumbers = new List<string>();

                foreach (var deletePdStockDto in deletePdStockDtos)
                {
                    // Kiểm tra dữ liệu không hợp lệ
                    if (string.IsNullOrEmpty(deletePdStockDto.SerialNumber))
                    {
                        errorMessages.Add($"Invalid data for SerialNumber: {deletePdStockDto.SerialNumber ?? "Unknown"}.");
                        continue;
                    }

                    var product = _sqlContext.PdStocks.FirstOrDefault(p => p.SerialNumber == deletePdStockDto.SerialNumber);
                    if (product == null)
                    {
                        notFoundSerialNumbers.Add(deletePdStockDto.SerialNumber);
                        continue;
                    }

                    // Lưu dữ liệu sản phẩm vào bảng PdStockHistory
                    var productHistory = new PdStockHistory
                    {
                        SerialNumber = product.SerialNumber,
                        ModelName = product.ModelName,
                        CartonNo = product.CartonNo,
                        LocationStock = product.LocationStock,
                        EntryDate = product.EntryDate,
                        EntryOp = product.EntryOp,
                        OutDate = DateTime.Now,
                        OutOp = deletePdStockDto.OutOp
                    };

                    _sqlContext.PdStockHistories.Add(productHistory);

                    // Xóa sản phẩm khỏi bảng PdStock
                    _sqlContext.PdStocks.Remove(product);
                }

                // Lưu các thay đổi vào database
                _sqlContext.SaveChanges();

                // Phản hồi nếu có lỗi
                if (notFoundSerialNumbers.Any() || errorMessages.Any())
                {
                    return Ok(new
                    {
                        message = "Some products could not be processed.",
                        notFoundSerialNumbers = notFoundSerialNumbers,
                        errors = errorMessages
                    });
                }

                return Ok(new { message = "Tất cả sản phẩm được xóa thành công. " });
            }

            catch (Exception ex)
            {
                // Log chi tiết lỗi
                Console.WriteLine($"Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }
                return StatusCode(500, $"Internal server error: {ex.Message}. Details: {ex.InnerException?.Message}");
            }
        }

        // Tìm lịch sử sản phẩm theo serialNumber
        [HttpPost("GetHistoryBySerialNumber")]
        public IActionResult GetHistoryBySerialNumber([FromBody] List<string> SerialNumber)
        {
            if (SerialNumber == null || !SerialNumber.Any())
            {
                return BadRequest("Serial numbers list cannot be null or empty.");
            }
            var results = _sqlContext.PdStockHistories.Where(s => SerialNumber.Contains(s.SerialNumber)).ToList();

            if (results == null || !results.Any())
            {
                return NotFound();
            }

            return Ok(new { data = results });
        }

        // Tìm sản phẩm trong bảng R107 theo carton hoặc sn
        [HttpPost("GetR107ByInputList")]
        public async Task<IActionResult> GetR107ByInputList([FromBody] List<string> inputList)
        {
            if (inputList == null || inputList.Count == 0)
            {
                return BadRequest(new { message = "The input list is required and cannot be empty." });
            }

            // Loại bỏ khoảng trắng thừa để tìm kiếm chính xác hơn
            var cleanInputs = inputList.Select(x => x.Trim()).ToList();

            var result = await _oracleContext.OracleDataR107PdStock
                .Where(p => cleanInputs.Contains(p.CARTON_NO) || cleanInputs.Contains(p.SERIAL_NUMBER))
                .Select(p => new
                {
                    p.CARTON_NO,
                    p.SERIAL_NUMBER,
                    p.MODEL_NAME
                })
                .ToListAsync();

            if (result == null || result.Count == 0)
            {
                return NotFound(new { message = "No products found matching the given Carton Numbers or Serial Numbers." });
            }

            return Ok(new { data = result });
        }


        // Tìm sản phẩm nhập kho theo khoảng thời gian
        [HttpPost("GetProductsByDateRange")]
        public async Task<IActionResult> GetProductsByDateRange([FromBody] DateRangeDto dateRangeDto)
        {
            if (dateRangeDto == null || dateRangeDto.StartDate == default || dateRangeDto.EndDate == default)
            {
                return BadRequest(new { message = "Invalid date range provided." });
            }

            try
            {
                // 1. Lấy dữ liệu từ SQL Server (Giới hạn cột để nhẹ RAM)
                var pdStockProducts = await _sqlContext.PdStocks
                    .AsNoTracking()
                    .Where(p => p.EntryDate >= dateRangeDto.StartDate && p.EntryDate <= dateRangeDto.EndDate)
                    .Select(p => new { p.SerialNumber, p.ModelName, p.CartonNo, p.LocationStock, p.EntryOp, p.EntryDate, Action = "In Stock" })
                    .ToListAsync();

                var pdStockHistoryProducts = await _sqlContext.PdStockHistories
                    .AsNoTracking()
                    .Where(h => h.EntryDate >= dateRangeDto.StartDate && h.EntryDate <= dateRangeDto.EndDate)
                    .Select(h => new { h.SerialNumber, h.ModelName, h.CartonNo, h.LocationStock, h.EntryOp, h.EntryDate, Action = "History" })
                    .ToListAsync();

                var allProducts = pdStockProducts.Concat(pdStockHistoryProducts).ToList();

                if (!allProducts.Any())
                {
                    return Ok(new { message = "No products found.", data = new List<object>() });
                }

                // 2. Truy vấn Oracle bằng Raw SQL (Tối ưu hiệu năng)
                var snList = allProducts.Select(x => x.SerialNumber).Where(sn => !string.IsNullOrEmpty(sn)).Distinct().ToList();
                var oracleLookup = new Dictionary<string, (string Wip, string Error, string Product)>(StringComparer.OrdinalIgnoreCase);

                using (var conn = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString))
                {
                    await conn.OpenAsync();
                    int pageSize = 999;
                    for (int i = 0; i < snList.Count; i += pageSize)
                    {
                        var chunk = snList.Skip(i).Take(pageSize).ToList();
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
                                string sn = reader["SERIAL_NUMBER"].ToString();
                                if (!oracleLookup.ContainsKey(sn))
                                {
                                    oracleLookup[sn] = (reader["WIP_GROUP"]?.ToString() ?? "N/A", reader["ERROR_FLAG"]?.ToString() ?? "N/A", reader["PRODUCT_LINE"]?.ToString() ?? "N/A");
                                }
                            }
                        }
                    }
                }

                // 3. Gộp dữ liệu bằng Dictionary Lookup (O(1))
                var result = allProducts.Select(p => new
                {
                    p.SerialNumber,
                    p.ModelName,
                    p.CartonNo,
                    p.LocationStock,
                    p.EntryOp,
                    p.EntryDate,
                    p.Action,
                    WipGroup = oracleLookup.TryGetValue(p.SerialNumber ?? "", out var info) ? info.Wip : "N/A",
                    ErrorFlag = info.Error ?? "N/A",
                    ProductLine = info.Product ?? "N/A"
                }).ToList();

                return Ok(new { message = "Products retrieved successfully.", data = result });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Internal server error: {ex.Message}" });
            }
        }
        //Tìm sản phẩm xuất kho theo khoảng thời gian
        [HttpPost("GetExportedProductsByDateRange")]
        public async Task<IActionResult> GetExportedProductsByDateRange([FromBody] DateRangeDto dateRangeDto)
        {
            if (dateRangeDto == null || dateRangeDto.StartDate == default || dateRangeDto.EndDate == default)
            {
                return BadRequest(new { message = "Invalid date range provided." });
            }

            try
            {
                // 1. Lấy dữ liệu xuất kho từ SQL Server
                var exportedProducts = await _sqlContext.PdStockHistories
                    .AsNoTracking()
                    .Where(h => h.OutDate >= dateRangeDto.StartDate && h.OutDate <= dateRangeDto.EndDate)
                    .Select(h => new { h.SerialNumber, h.ModelName, h.CartonNo, h.LocationStock, h.EntryDate, h.EntryOp, h.OutDate, h.OutOp })
                    .ToListAsync();

                if (!exportedProducts.Any())
                {
                    return Ok(new { message = "No exported products found.", data = new List<object>() });
                }

                // 2. Truy vấn Oracle bằng Raw SQL
                var snList = exportedProducts.Select(x => x.SerialNumber).Where(sn => !string.IsNullOrEmpty(sn)).Distinct().ToList();
                var oracleLookup = new Dictionary<string, (string Wip, string Error, string Product)>(StringComparer.OrdinalIgnoreCase);

                using (var conn = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString))
                {
                    await conn.OpenAsync();
                    int pageSize = 999;
                    for (int i = 0; i < snList.Count; i += pageSize)
                    {
                        var chunk = snList.Skip(i).Take(pageSize).ToList();
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
                                string sn = reader["SERIAL_NUMBER"].ToString();
                                if (!oracleLookup.ContainsKey(sn))
                                {
                                    oracleLookup[sn] = (reader["WIP_GROUP"]?.ToString() ?? "N/A", reader["ERROR_FLAG"]?.ToString() ?? "N/A", reader["PRODUCT_LINE"]?.ToString() ?? "N/A");
                                }
                            }
                        }
                    }
                }

                // 3. Gộp dữ liệu
                var result = exportedProducts.Select(h => new
                {
                    h.SerialNumber,
                    h.ModelName,
                    h.CartonNo,
                    h.LocationStock,
                    h.EntryDate,
                    h.EntryOp,
                    h.OutDate,
                    h.OutOp,
                    WipGroup = oracleLookup.TryGetValue(h.SerialNumber ?? "", out var info) ? info.Wip : "N/A",
                    ErrorFlag = info.Error ?? "N/A",
                    ProductLine = info.Product ?? "N/A"
                }).ToList();

                return Ok(new { message = "Exported products retrieved successfully.", data = result });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Internal server error: {ex.Message}" });
            }
        }


        // Tìm sản phẩm theo mã SERIAL_NUMBER trong bảng R107
        [HttpPost("GetR107bySN")]
        public async Task<IActionResult> GetR107bySN([FromBody] List<string> serialNumbers)
        {
            if (serialNumbers == null || serialNumbers.Count == 0)
            {
                return BadRequest("The serial number list is required and cannot be empty.");
            }

            var result = await _oracleContext.OracleDataR107PdStock
                .Where(p => serialNumbers.Contains(p.SERIAL_NUMBER))
                .Select(p => new
                {
                    p.CARTON_NO,
                    p.SERIAL_NUMBER,
                    p.MODEL_NAME
                })
                .ToListAsync();

            if (result == null || result.Count == 0)
            {
                return NotFound("No products found with the given SERIAL_NUMBER.");
            }

            return Ok(new { data = result });
        }

    }
}
