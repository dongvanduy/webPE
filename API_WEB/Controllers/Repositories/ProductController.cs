using API_WEB.Helpers;
using API_WEB.ModelsDB;
using API_WEB.ModelsOracle;
using DocumentFormat.OpenXml.InkML;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using static API_WEB.Controllers.Repositories.KhoScrapController;
using static API_WEB.Controllers.SmartFA.CheckInOutController;
using System.Net;
using API_WEB.Helpers.Repositories;

namespace API_WEB.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class ProductController : ControllerBase
    {
        private readonly CSDL_NE _sqlContext;
        private readonly OracleDbContext _oracleContext;
        private readonly HttpClient _httpClient;
        private static readonly HashSet<string> AllowedWorkFlags = new(StringComparer.OrdinalIgnoreCase) { "2", "5", "3" };

        public ProductController(CSDL_NE sqlContext, OracleDbContext oracleContext, HttpClient httpClient)
        {
            _sqlContext = sqlContext ?? throw new ArgumentNullException(nameof(sqlContext));
            _oracleContext = oracleContext ?? throw new ArgumentNullException(nameof(oracleContext));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        // API 1: Lấy tổng số lượng SN trong bảng Product
        [HttpGet("total")]
        public async Task<IActionResult> GetTotalSerialNumbers()
        {
            try
            {
                // Truy vấn tổng số lượng SN trong bảng Product
                var totalCount = await _sqlContext.Products.CountAsync();
                return Ok(new { success = true, totalCount });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // Thống kê số lượng SN đang được mượn
        [HttpGet("borrowed/count")]
        public async Task<IActionResult> GetBorrowedCount()
        {
            try
            {
                var borrowedCount = await _sqlContext.Products
                    .CountAsync(p => p.BorrowStatus == "Borrowed");
                return Ok(new { success = true, borrowedCount });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("borrowed/list")]
        public async Task<IActionResult> GetBorrowedSerialNumbers()
        {
            try
            {
                var serialNumbers = await _sqlContext.Products
                    .Where(p => p.BorrowStatus == "Borrowed" && !string.IsNullOrWhiteSpace(p.SerialNumber))
                    .Select(p => p.SerialNumber.Trim())
                    .Distinct()
                    .ToListAsync();

                return Ok(new { success = true, serialNumbers });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // Thống kê số lượng mượn/trả trong ngày
        [HttpGet("borrowed/daily")]
        public async Task<IActionResult> GetBorrowReturnToday()
        {
            try
            {
                var today = DateTime.Today;
                var tomorrow = today.AddDays(1);

                var borrowedToday = await _sqlContext.BorrowHistories
                    .CountAsync(b => b.BorrowDate >= today && b.BorrowDate < tomorrow);

                var returnedToday = await _sqlContext.Products
                    .CountAsync(p => p.EntryDate >= today && p.EntryDate < tomorrow);

                return Ok(new { success = true, borrowedToday, returnedToday });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // Thống kê số lượng SN đang mượn theo số ngày (aging)
        [HttpGet("borrowed/aging")]
        public async Task<IActionResult> GetBorrowAging()
        {
            try
            {
                var today = DateTime.Today;
                var agingData = await _sqlContext.Products
                    .Where(p => p.BorrowStatus == "Borrowed" && p.BorrowDate != null)
                    .Select(p => new
                    {
                        days = EF.Functions.DateDiffDay(p.BorrowDate.Value, today)
                    })
                    .GroupBy(x => x.days)
                    .Select(g => new { days = g.Key, count = g.Count() })
                    .OrderBy(g => g.days)
                    .ToListAsync();

                return Ok(new { success = true, aging = agingData });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // Chi tiết mượn/trả trong ngày
        [HttpGet("borrowed/daily/details")]
        public async Task<IActionResult> GetBorrowReturnDetails([FromQuery] string type)
        {
            try
            {
                var today = DateTime.Today;
                var tomorrow = today.AddDays(1);

                if (type == "borrowed")
                {
                    var borrowDetails = await _sqlContext.BorrowHistories
                        .Where(b => b.BorrowDate >= today && b.BorrowDate < tomorrow)
                        .Select(b => new
                        {
                            serialNumber = b.SerialNumber,
                            borrower = b.BorrowPerson,
                            borrowDate = b.BorrowDate,
                            returnDate = (DateTime?)null,
                            location = string.Empty
                        })
                        .ToListAsync();

                    return Ok(new { success = true, data = borrowDetails });
                }
                else if (type == "returned")
                {
                    var returnedProducts = await _sqlContext.Products
                        .Include(p => p.Shelf)
                        .Where(p => p.EntryDate >= today && p.EntryDate < tomorrow && p.BorrowStatus == "Available")
                        .Select(p => new
                        {
                            p.SerialNumber,
                            p.EntryDate,
                            p.Shelf,
                            p.ColumnNumber,
                            p.LevelNumber,
                            p.TrayNumber
                        })
                        .ToListAsync();

                    var result = returnedProducts.Select(p =>
                    {
                        var lastBorrow = _sqlContext.BorrowHistories
                            .Where(bh => bh.SerialNumber == p.SerialNumber)
                            .OrderByDescending(bh => bh.BorrowDate)
                            .FirstOrDefault();

                        return new
                        {
                            serialNumber = p.SerialNumber,
                            borrower = lastBorrow?.BorrowPerson,
                            borrowDate = lastBorrow?.BorrowDate,
                            returnDate = p.EntryDate,
                            location = p.Shelf != null
                                ? $"{p.Shelf.ShelfCode}{p.ColumnNumber}-{p.LevelNumber}-K({p.TrayNumber})"
                                : string.Empty
                        };
                    }).ToList();

                    return Ok(new { success = true, data = result });
                }

                return BadRequest(new { success = false, message = "Invalid type" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // Chi tiết aging mượn
        [HttpGet("borrowed/aging/details")]
        public async Task<IActionResult> GetBorrowAgingDetails([FromQuery] int days)
        {
            try
            {
                var today = DateTime.Today;
                var data = await _sqlContext.Products
                    .Where(p => p.BorrowStatus == "Borrowed" && p.BorrowDate != null &&
                                EF.Functions.DateDiffDay(p.BorrowDate.Value, today) == days)
                    .Select(p => new
                    {
                        serialNumber = p.SerialNumber,
                        borrower = p.BorrowPerson,
                        borrowDate = p.BorrowDate,
                        returnDate = (DateTime?)null,
                        location = string.Empty
                    })
                    .ToListAsync();

                return Ok(new { success = true, data });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }




        //HELPER lấy danh sách SN hợp lệ từ Oracle R107
        private async Task<HashSet<string>> GetExistingSerialsInKanbanAsync(
        OracleConnection connection,
        List<string> serialNumbers)
        {
            var exists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (serialNumbers == null || serialNumbers.Count == 0) return exists;

            const int batchSize = 1000;

            for (int i = 0; i < serialNumbers.Count; i += batchSize)
            {
                var batch = serialNumbers.Skip(i).Take(batchSize).ToList();
                var serialList = string.Join(",", batch.Select(sn => $"'{sn.Replace("'", "''")}'"));

                var query = $@"
            SELECT DISTINCT SERIAL_NUMBER
            FROM SFISM4.Z_KANBAN_TRACKING_T
            WHERE SERIAL_NUMBER IN ({serialList})";

                using var cmd = new OracleCommand(query, connection);
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var sn = reader["SERIAL_NUMBER"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(sn))
                        exists.Add(sn);
                }
            }

            return exists;
        }

        // Thống kê số lượng SN trong kho OK theo aging (tính từ ngày nhập kho)
        [HttpGet("warehouse-aging")]
        public async Task<IActionResult> GetWarehouseAging()
        {
            try
            {
                var now = DateTime.Now;

                // 1) Lấy danh sách SN trong kho OK (SQL)
                var serialNumbers = await _sqlContext.KhoOks
                    .Where(k => k.entryDate != null && k.SERIAL_NUMBER != null)
                    .Select(k => k.SERIAL_NUMBER!)
                    .Distinct()
                    .ToListAsync();

                if (serialNumbers.Count == 0)
                {
                    return Ok(new
                    {
                        success = true,
                        data = new object[]
                        {
                    new { label = "<1 ngày", count = 0 },
                    new { label = "1-3 ngày", count = 0 },
                    new { label = ">3 ngày", count = 0 },
                        },
                        details = new
                        {
                            LessThanOneDay = new object[0],
                            OneToThreeDays = new object[0],
                            MoreThanThreeDays = new object[0],
                        }
                    });
                }

                // 2) Query Oracle: chỉ giữ SN tồn tại trong Z_KANBAN_TRACKING_T
                await using var oracleConnection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);
                await oracleConnection.OpenAsync();

                var existsInKanban = await GetExistingSerialsInKanbanAsync(oracleConnection, serialNumbers);

                // 3) Lấy detail từ SQL rồi lọc bằng HashSet (tránh lỗi 2100 params)
                var detailRows = await _sqlContext.KhoOks
                    .Where(k => k.entryDate != null)
                    .Select(k => new
                    {
                        SerialNumber = k.SERIAL_NUMBER,
                        k.ShelfCode,
                        k.ColumnNumber,
                        k.LevelNumber,
                        EntryDate = k.entryDate!.Value,
                        AgingDays = EF.Functions.DateDiffDay(k.entryDate!.Value, now)
                    })
                    .ToListAsync();

                detailRows = detailRows
                    .Where(r => !string.IsNullOrWhiteSpace(r.SerialNumber) && existsInKanban.Contains(r.SerialNumber))
                    .ToList();

                // 4) Bucket + details như cũ
                var bucketOrder = new[] { "<1 ngày", "1-3 ngày", ">3 ngày" };

                var counts = detailRows
                    .GroupBy(row => row.AgingDays < 1 ? "<1 ngày" : row.AgingDays <= 3 ? "1-3 ngày" : ">3 ngày")
                    .ToDictionary(g => g.Key, g => g.Count());

                var result = bucketOrder
                    .Select(label => new { label, count = counts.TryGetValue(label, out var count) ? count : 0 })
                    .ToList();

                var lessThanOne = detailRows.Where(row => row.AgingDays < 1).ToList();
                var oneToThree = detailRows.Where(row => row.AgingDays >= 1 && row.AgingDays <= 3).ToList();
                var moreThanThree = detailRows.Where(row => row.AgingDays > 3).ToList();

                return Ok(new
                {
                    success = true,
                    data = result,
                    details = new
                    {
                        LessThanOneDay = lessThanOne,
                        OneToThreeDays = oneToThree,
                        MoreThanThreeDays = moreThanThree
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        [HttpPost("report")]
        public async Task<IActionResult> GetProductReportByTime([FromBody] TimeRangeRequest request)
        {
            try
            {
                // Kiểm tra input
                if (request.StartDate == null || request.EndDate == null)
                {
                    return BadRequest(new { success = false, message = "Thời gian bắt đầu và kết thúc không được để trống." });
                }

                // Truy vấn danh sách nhập từ bảng Products
                var importedDataFromProducts = await _sqlContext.Products
                    .Where(p => p.EntryDate.HasValue && p.EntryDate.Value >= request.StartDate && p.EntryDate.Value <= request.EndDate)
                    .Select(p => new
                    {
                        SerialNumber = p.SerialNumber,
                        ProductLine = p.ProductLine,
                        ModelName = p.ModelName,
                        EntryDate = p.EntryDate
                    })
                    .ToListAsync();

                // Truy vấn danh sách nhập từ bảng Exports (EntryDate)
                var importedDataFromExports = await _sqlContext.Exports
                    .Where(e => e.EntryDate.HasValue && e.EntryDate.Value >= request.StartDate && e.EntryDate.Value <= request.EndDate)
                    .Select(e => new
                    {
                        SerialNumber = e.SerialNumber,
                        ProductLine = e.ProductLine,
                        ModelName = e.ModelName,
                        EntryDate = e.EntryDate
                    })
                    .ToListAsync();

                // Gộp hai danh sách nhập lại và loại bỏ trùng lặp
                var importedData = importedDataFromProducts
                    .Concat(importedDataFromExports)
                    .GroupBy(x => x.SerialNumber)
                    .Select(g => g.First())
                    .ToList();

                // Truy vấn danh sách xuất từ bảng Exports
                var exportedData = await _sqlContext.Exports
                    .Where(e => e.ExportDate.HasValue && e.ExportDate.Value >= request.StartDate && e.ExportDate.Value <= request.EndDate)
                    .Select(e => new
                    {
                        SerialNumber = e.SerialNumber,
                        ProductLine = e.ProductLine,
                        ModelName = e.ModelName,
                        ExportDate = e.ExportDate
                    })
                    .ToListAsync();

                // Tính tổng số lượng nhập và xuất
                var importedCount = importedData.Count;
                var exportedCount = exportedData.Count;

                // Trả về kết quả chi tiết
                return Ok(new
                {
                    success = true,
                    report = new
                    {
                        startDate = request.StartDate,
                        endDate = request.EndDate,
                        importedCount,
                        exportedCount,
                        importedData,
                        exportedData
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // Request model
        public class TimeRangeRequest
        {
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
        }

        [HttpPost("SaveProducts")]
        public async Task<IActionResult> SaveProducts([FromBody] SaveProductRequest request)
        {
            try
            {
                Console.WriteLine($"Input: shelf = {request.Shelf}, column={request.Column}, level={request.Level}, tray={request.Tray}");

                if (request.SerialNumbers == null || !request.SerialNumbers.Any())
                {
                    return BadRequest(new { success = false, message = "Danh sách Serial Number không hợp lệ." });
                }

                var normalizedSerials = request.SerialNumbers
                    .Where(sn => !string.IsNullOrWhiteSpace(sn))
                    .Select(sn => sn.Trim())
                    .ToList();

                if (!normalizedSerials.Any())
                {
                    return BadRequest(new { success = false, message = "Danh sách Serial Number không hợp lệ." });
                }

                var shelfData = await _sqlContext.Shelves.FirstOrDefaultAsync(s => s.ShelfCode == request.Shelf);
                if (shelfData == null)
                {
                    return BadRequest(new { success = false, message = "Ma ke khong hop le!!!" });
                }

                int maxSlots = shelfData.ShelfCode.Contains("XE") ? 20 : 8;
                var occupiedPositions = await _sqlContext.Products
                    .Where(p => p.ShelfId == shelfData.ShelfId &&
                                p.ColumnNumber == request.Column &&
                                p.LevelNumber == request.Level &&
                                p.TrayNumber == request.Tray &&
                                p.PositionInTray.HasValue)
                    .Select(p => p.PositionInTray!.Value)
                    .ToListAsync();

                if (occupiedPositions.Count >= maxSlots)
                {
                    return BadRequest(new { success = false, message = "Khay da day!" });
                }

                var results = new List<object>();
                var processedSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var serialsToUpdateOracle = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var serialNumber in normalizedSerials)
                {
                    SerialLinkResolver.SerialLinkInfo linkInfo;
                    try
                    {
                        linkInfo = await SerialLinkResolver.ResolveAsync(_oracleContext, serialNumber);
                    }
                    catch (Exception ex)
                    {
                        results.Add(new
                        {
                            serialNumber,
                            success = false,
                            message = $"Không thể xác định liên kết SerialNumber: {ex.Message}"
                        });
                        continue;
                    }

                    var storageSerial = linkInfo.StorageSerial;
                    var validationResult = await ValidateSerialForImportAsync(serialNumber, linkInfo);
                    if (!validationResult.IsValid)
                    {
                        results.Add(new
                        {
                            serialNumber,
                            linkedSerial = linkInfo.LinkedFgSerial ?? storageSerial,
                            success = false,
                            message = validationResult.ErrorMessage
                        });
                        continue;
                    }

                    var relatedSerials = new HashSet<string>(linkInfo.RelatedSerials, StringComparer.OrdinalIgnoreCase)
                    {
                        serialNumber
                    };

                    if (processedSerials.Overlaps(relatedSerials))
                    {
                        results.Add(new
                        {
                            serialNumber,
                            linkedSerial = linkInfo.LinkedFgSerial ?? storageSerial,
                            success = false,
                            message = "SerialNumber đã được xử lý thông qua serial liên kết khác."
                        });
                        continue;
                    }

                    processedSerials.UnionWith(relatedSerials);
                    var removedFromOtherKho = false;

                    var scrapItems = await _sqlContext.KhoScraps
                        .Where(k => relatedSerials.Contains(k.SERIAL_NUMBER))
                        .ToListAsync();
                    if (scrapItems.Any())
                    {
                        _sqlContext.KhoScraps.RemoveRange(scrapItems);
                        removedFromOtherKho = true;
                    }

                    var okItems = await _sqlContext.KhoOks
                        .Where(k => relatedSerials.Contains(k.SERIAL_NUMBER))
                        .ToListAsync();
                    if (okItems.Any())
                    {
                        _sqlContext.KhoOks.RemoveRange(okItems);
                        removedFromOtherKho = true;
                    }

                    if (removedFromOtherKho)
                    {
                        await _sqlContext.SaveChangesAsync();
                        await LogAction("REMOVE_FROM_OTHER_KHO", storageSerial, request.EntryPerson ?? string.Empty);
                    }

                    var existingProduct = await _sqlContext.Products
                        .FirstOrDefaultAsync(p => relatedSerials.Contains(p.SerialNumber));

                    if (existingProduct != null)
                    {
                        if (!string.Equals(existingProduct.SerialNumber, storageSerial, StringComparison.OrdinalIgnoreCase))
                        {
                            existingProduct.SerialNumber = storageSerial;
                        }

                        if (existingProduct.BorrowStatus == "Borrowed")
                        {
                            Console.WriteLine($"Updating product {storageSerial} with new location.");

                            int positionInTray = Enumerable.Range(1, maxSlots)
                                .Except(occupiedPositions)
                                .FirstOrDefault();

                            if (positionInTray == 0)
                            {
                                results.Add(new
                                {
                                    serialNumber,
                                    linkedSerial = linkInfo.LinkedFgSerial ?? storageSerial,
                                    success = false,
                                    message = "Không tìm được vị trí trống!"
                                });
                                continue;
                            }

                            existingProduct.ShelfId = shelfData.ShelfId;
                            existingProduct.ColumnNumber = request.Column;
                            existingProduct.LevelNumber = request.Level;
                            existingProduct.TrayNumber = request.Tray;
                            existingProduct.PositionInTray = positionInTray;
                            existingProduct.BorrowStatus = "Available";
                            existingProduct.EntryPerson = request.EntryPerson;
                            existingProduct.EntryDate = DateTime.Now;
                            existingProduct.BorrowDate = null;
                            existingProduct.BorrowPerson = string.Empty;

                            occupiedPositions.Add(positionInTray);

                            _sqlContext.Products.Update(existingProduct);
                            await _sqlContext.SaveChangesAsync();
                            await LogAction("UPDATE_LOCATION", storageSerial, request.EntryPerson ?? string.Empty);
                            serialsToUpdateOracle.UnionWith(relatedSerials);

                            results.Add(new
                            {
                                serialNumber,
                                linkedSerial = linkInfo.LinkedFgSerial ?? storageSerial,
                                success = true,
                                message = "Sản phẩm đã được cập nhật vị trí."
                            });
                        }
                        else
                        {
                            results.Add(new
                            {
                                serialNumber,
                                linkedSerial = linkInfo.LinkedFgSerial ?? storageSerial,
                                success = false,
                                message = $"SerialNumber {storageSerial} đã tồn tại trong hệ thống."
                            });
                        }

                        continue;
                    }

                    int positionIntray = Enumerable.Range(1, maxSlots)
                        .Except(occupiedPositions)
                        .FirstOrDefault();

                    if (positionIntray == 0)
                    {
                        results.Add(new
                        {
                            serialNumber,
                            linkedSerial = linkInfo.LinkedFgSerial ?? storageSerial,
                            success = false,
                            message = "Khong tim duoc vi tri trong!"
                        });
                        continue;
                    }

                    occupiedPositions.Add(positionIntray);

                    var (modelName, productLine) = await GetModelAndProductLineAsync(linkInfo.PrioritySerials);

                    var newProduct = new Product
                    {
                        SerialNumber = storageSerial,
                        ShelfId = shelfData.ShelfId,
                        ColumnNumber = request.Column,
                        LevelNumber = request.Level,
                        TrayNumber = request.Tray,
                        PositionInTray = positionIntray,
                        EntryDate = DateTime.Now,
                        EntryPerson = request.EntryPerson,
                        ProductLine = productLine,
                        ModelName = modelName,
                        BorrowDate = null,
                        BorrowPerson = string.Empty,
                        BorrowStatus = "Available"
                    };

                    _sqlContext.Products.Add(newProduct);
                    await _sqlContext.SaveChangesAsync();
                    await LogAction("IMPORT_PRODUCT", storageSerial, request.EntryPerson ?? string.Empty);
                    serialsToUpdateOracle.UnionWith(relatedSerials);

                    results.Add(new
                    {
                        serialNumber,
                        linkedSerial = linkInfo.LinkedFgSerial ?? storageSerial,
                        success = true,
                        message = "Da them san pham thanh cong"
                    });
                }

                if (serialsToUpdateOracle.Any())
                {
                    await RemoveLocationHelper.SendReceivingStatusAsync(serialsToUpdateOracle, request.EntryPerson ?? string.Empty, null, "Nhập(Kho Repair)", _oracleContext);
                }

                return Ok(new { success = true, results });
            }
            catch (OracleException ex)
            {
                Console.WriteLine($"Oracle ERROR: {ex.Message}");
                return StatusCode(500, new { success = false, message = $"Loi Oracle:{ex.Message}" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"System ERROR:{ex.Message}");
                return StatusCode(500, new { sucess = false, message = $"Loi He Thong: {ex.Message}" });
            }
        }

        private async Task<(bool IsValid, string? ErrorMessage)> ValidateSerialForImportAsync(string originalSerial, SerialLinkResolver.SerialLinkInfo linkInfo)
        {
            //if (linkInfo == null)
            //{
            //    return (false, "SERIAL_NUMBER CHƯA ĐƯỢC UNLINK!!");
            //}

            var serialCandidates = new List<string>();

            if (!string.IsNullOrWhiteSpace(originalSerial))
            {
                serialCandidates.Add(originalSerial.Trim());
            }

            if (linkInfo.PrioritySerials != null)
            {
                serialCandidates.AddRange(linkInfo.PrioritySerials);
            }

            string? validatedSerial = null;

            foreach (var candidate in serialCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                const string oracleQuery = @"SELECT SERIAL_NUMBER, ERROR_FLAG, WORK_FLAG
                                              FROM SFISM4.R107
                                              WHERE SERIAL_NUMBER = :serialNumber
                                                AND ROWNUM = 1";

                var oracleParam = new OracleParameter("serialNumber", OracleDbType.Varchar2)
                {
                    Value = candidate.Trim()
                };

                var oracleResult = await _oracleContext.OracleDataR107
                    .FromSqlRaw(oracleQuery, oracleParam)
                    .AsNoTracking()
                    .Select(r => new { r.SERIAL_NUMBER, r.ERROR_FLAG, r.WORK_FLAG })
                    .FirstOrDefaultAsync();

                if (oracleResult == null)
                {
                    continue;
                }

                var errorFlag = oracleResult.ERROR_FLAG?.Trim();
                if (string.Equals(errorFlag, "1", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, $"SerialNumber {candidate.Trim()} CÓ ERROR_FLAG = 1 KHÔNG NHẬP KHO!");
                }

                //var workFlag = oracleResult.WORK_FLAG?.Trim() ?? string.Empty;
                //if (!AllowedWorkFlags.Contains(workFlag))
                //{
                //    return (false, $"SerialNumber {candidate.Trim()} CÓ WORK_FLAG = {workFlag} KHÔNG NHẬP KHO!");
                //}

                validatedSerial = candidate.Trim();
                break;
            }

            var serialsToCheckKeyPart = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(originalSerial))
            {
                serialsToCheckKeyPart.Add(originalSerial.Trim());
            }

            if (linkInfo.RelatedSerials != null)
            {
                foreach (var related in linkInfo.RelatedSerials)
                {
                    if (!string.IsNullOrWhiteSpace(related))
                    {
                        serialsToCheckKeyPart.Add(related.Trim());
                    }
                }
            }

            if (validatedSerial != null)
            {
                serialsToCheckKeyPart.Add(validatedSerial);
            }

            //foreach (var serial in serialsToCheckKeyPart)
            //{
            //    if (await KeyPartSerialExistsAsync(serial))
            //    {
            //        return (false, $"SerialNumber {serial} đang tồn tại trong bảng R_WIP_KEYPARTS_T với vai trò KEY_PART_SN.");
            //    }
            //}

            return (true, null);
        }

        private async Task<bool> KeyPartSerialExistsAsync(string serialNumber)
        {
            if (string.IsNullOrWhiteSpace(serialNumber))
            {
                return false;
            }

            const string query = @"SELECT KEY_PART_SN
                                    FROM SFISM4.R_WIP_KEYPARTS_T
                                    WHERE KEY_PART_SN = :serialNumber
                                      AND ROWNUM = 1";

            var connection = _oracleContext.Database.GetDbConnection();
            var shouldClose = connection.State != System.Data.ConnectionState.Open;

            if (shouldClose)
            {
                await connection.OpenAsync();
            }

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = query;

                if (command is OracleCommand oracleCommand)
                {
                    oracleCommand.BindByName = true;
                }

                var parameter = new OracleParameter("serialNumber", OracleDbType.Varchar2)
                {
                    Value = serialNumber.Trim()
                };

                command.Parameters.Add(parameter);

                var result = await command.ExecuteScalarAsync();
                return result != null && result != DBNull.Value;
            }
            finally
            {
                if (shouldClose && connection.State == System.Data.ConnectionState.Open)
                {
                    await connection.CloseAsync();
                }
            }
        }
        public class SaveProductRequest
        {
            public string? Shelf { get; set; }
            public int Column { get; set; }
            public int Level { get; set; }
            public int Tray { get; set; }
            public string? EntryPerson { get; set; }
            public List<string>? SerialNumbers { get; set; }
        }

        private async Task<(string ModelName, string ProductLine)> GetModelAndProductLineAsync(IEnumerable<string> serialCandidates)
        {
            foreach (var candidate in serialCandidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                Console.WriteLine($"ModelName for {candidate} from Oracle...");
                const string modelNameQuery = @"SELECT SERIAL_NUMBER, MODEL_NAME, MO_NUMBER, WIP_GROUP, WORK_FLAG, ERROR_FLAG
                                                FROM SFISM4.R107
                                                WHERE SERIAL_NUMBER = :serialNumber AND ROWNUM = 1";
                var modelNameParam = new OracleParameter("serialNumber", OracleDbType.Varchar2)
                {
                    Value = candidate
                };

                var modelNameResult = await _oracleContext.OracleDataR107
                    .FromSqlRaw(modelNameQuery, modelNameParam)
                    .AsNoTracking()
                    .ToListAsync();

                var modelName = modelNameResult.FirstOrDefault()?.MODEL_NAME ?? string.Empty;

                if (string.IsNullOrEmpty(modelName))
                {
                    continue;
                }

                Console.WriteLine($"Product_Line for {modelName} from Oracle...");
                const string productLineQuery = @"SELECT MODEL_NAME, PRODUCT_LINE, MODEL_SERIAL
                                            FROM SFIS1.C_MODEL_DESC_T
                                            WHERE MODEL_NAME = :modelName AND ROWNUM = 1";

                var productLineParam = new OracleParameter("modelName", OracleDbType.NVarchar2)
                {
                    Value = modelName
                };

                var productLineResult = await _oracleContext.OracleDataCModelDesc
                    .FromSqlRaw(productLineQuery, productLineParam)
                    .AsNoTracking()
                    .Select(pl => new
                    {
                        MODEL_NAME = pl.MODEL_NAME ?? string.Empty,
                        PRODUCT_LINE = pl.PRODUCT_LINE ?? string.Empty
                    })
                    .ToListAsync();

                var productLine = productLineResult.FirstOrDefault()?.PRODUCT_LINE ?? string.Empty;

                return (modelName, productLine);
            }

            return (string.Empty, string.Empty);
        }

        [HttpGet("TrayInfo")]
        public async Task<IActionResult> GetTrayInfo(string shelf, int column, int level, int tray)
        {
            try
            {
                var shelfData = await _sqlContext.Shelves.FirstOrDefaultAsync(s => s.ShelfCode == shelf);
                if (shelfData == null)
                {
                    return BadRequest(new { success = false, message = "Ma ke kho nhop le!!" });
                }

                //2. Lay danh sach cac san pham co trong Tray
                var productsInTray = await _sqlContext.Products.Where(p => p.ShelfId == shelfData.ShelfId &&
                p.ColumnNumber == column &&
                p.LevelNumber == level &&
                p.TrayNumber == tray).OrderBy(p => p.PositionInTray).ToListAsync();

                //3. So luong vi tri toi da trong khay
                int maxSlots = shelf.Contains("XE") ? 20 : 8;

                // 4. Tạo danh sách serials với vị trí
                var occupiedPositions = productsInTray.Select(p => new
                {
                    SerialNumber = p.SerialNumber,
                    PositionInTray = p.PositionInTray
                }).ToList();

                return Ok(new
                {
                    success = true,
                    maxSlots = maxSlots,
                    occupiedCount = occupiedPositions.Count,
                    occupiedPositions = occupiedPositions
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"system ERROR: {ex.Message}");
                return StatusCode(500, new { success = false, message = $"Loi he thong: {ex.Message}" });
            }
        }

        [HttpGet("GetSNInfo")]
        public async Task<IActionResult> GetSNInfo(string serialNumber)
        {
            if (string.IsNullOrWhiteSpace(serialNumber))
                return BadRequest(new { success = false, message = "serialNumber là bắt buộc." });

            await using var connection = new OracleConnection(_oracleContext.Database.GetDbConnection().ConnectionString);

            try
            {
                await connection.OpenAsync();

                string query = @"
            SELECT 
                a.SERIAL_NUMBER,
                a.MODEL_NAME,
                b.PRODUCT_LINE,
                a.WIP_GROUP
            FROM sfism4.r107 a
            INNER JOIN sfis1.c_model_desc_t b
                ON a.MODEL_NAME = b.MODEL_NAME
            WHERE a.SERIAL_NUMBER = :serialNumber
            AND ROWNUM = 1";

                var checkInList = new List<InforSN>();

                await using var cmd = new OracleCommand(query, connection);
                cmd.Parameters.Add(new OracleParameter("serialNumber", OracleDbType.Varchar2)
                {
                    Value = serialNumber.Trim().ToUpperInvariant()
                });

                await using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    checkInList.Add(new InforSN
                    {
                        modelName = reader["MODEL_NAME"]?.ToString() ?? "",
                        productLine = reader["PRODUCT_LINE"]?.ToString() ?? "",
                        wipGroup = reader["WIP_GROUP"]?.ToString() ?? ""
                    });
                }

                if (!checkInList.Any())
                    return NotFound(new { success = false, message = $"Không tìm thấy dữ liệu cho SN: {serialNumber}" });

                return Ok(new { success = true, data = checkInList.First() });
            }
            catch (OracleException ex)
            {
                return StatusCode(500, new { success = false, message = $"Lỗi Oracle: {ex.Message}" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Lỗi hệ thống: {ex.Message}" });
            }
        }


        //Add Action
        [HttpPut("UpdateAction")]
        public async Task<IActionResult> UpdateProduct([FromBody] UpdateActionRequest request)
        {
            try
            {
                if (request.SerialNumbers == null || !request.SerialNumbers.Any())
                {
                    return BadRequest(new { success = false, message = "Không có Serial Number nào được cung cấp." });
                }

                // Lấy danh sách sản phẩm cần cập nhật
                var products = await _sqlContext.Products
                    .Where(p => request.SerialNumbers.Contains(p.SerialNumber))
                    .ToListAsync();

                if (!products.Any())
                {
                    return NotFound(new { success = false, message = "Không tìm thấy sản phẩm nào phù hợp." });
                }

                foreach (var product in products)
                {
                    product.Action = request.Action ?? product.Action; // Cập nhật cột Note
                }

                await _sqlContext.SaveChangesAsync();

                return Ok(new { success = true, message = "Update success!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"System Error: {ex.Message}");
                return StatusCode(500, new { success = false, message = $"Lỗi hệ thống: {ex.Message}" });
            }
        }

        //Add Note
        [HttpPut("UpdateProduct")]
        public async Task<IActionResult> UpdateAction([FromBody] UpdateProductRequest request)
        {
            try
            {
                if (request.SerialNumbers == null || !request.SerialNumbers.Any())
                {
                    return BadRequest(new { success = false, message = "Không có Serial Number nào được cung cấp." });
                }

                // Lấy danh sách sản phẩm cần cập nhật
                var products = await _sqlContext.Products
                    .Where(p => request.SerialNumbers.Contains(p.SerialNumber))
                    .ToListAsync();

                if (!products.Any())
                {
                    return NotFound(new { success = false, message = "Không tìm thấy sản phẩm nào phù hợp." });
                }

                foreach (var product in products)
                {
                    product.Note = request.Note ?? product.Note; // Cập nhật cột Note
                }

                await _sqlContext.SaveChangesAsync();

                return Ok(new { success = true, message = "Update success!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"System Error: {ex.Message}");
                return StatusCode(500, new { success = false, message = $"Lỗi hệ thống: {ex.Message}" });
            }
        }

        //add Scrap status

        [HttpPut("UpdateScrap")]
        public async Task<IActionResult> UpdateScrap([FromBody] UpdateScrapRequest request)
        {
            try
            {
                if (request.SerialNumbers == null || !request.SerialNumbers.Any())
                {
                    return BadRequest(new { success = false, message = "Không có Serial Number nào được cung cấp." });
                }

                if (string.IsNullOrEmpty(request.ScrapStatus))
                {
                    return BadRequest(new { success = false, message = "ScrapStatus không được để trống." });
                }

                var products = await _sqlContext.Products
                    .Where(p => request.SerialNumbers.Contains(p.SerialNumber))
                    .ToListAsync();

                if (!products.Any())
                {
                    return NotFound(new { success = false, message = "Không tìm thấy sản phẩm nào phù hợp." });
                }

                foreach (var product in products)
                {
                    // Nối ScrapStatus vào Scrap hiện có, nếu Scrap là null thì khởi tạo bằng ScrapStatus
                    product.Scrap = string.IsNullOrEmpty(product.Scrap)
                        ? request.ScrapStatus
                        : $"{product.Scrap};{request.ScrapStatus}";
                }

                await _sqlContext.SaveChangesAsync();

                return Ok(new { success = true, message = "Cập nhật Scrap thành công!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"System Error: {ex.Message}");
                return StatusCode(500, new { success = false, message = $"Lỗi hệ thống: {ex.Message}" });
            }
        }

        public class UpdateProductRequest
        {
            public List<string>? SerialNumbers { get; set; } // Danh sách Serial Numbers cần cập nhật
            public string? Note { get; set; } // Ghi chú cần cập nhật
        }

        public class UpdateActionRequest
        {
            public List<string>? SerialNumbers { get; set; } // Danh sách Serial Numbers cần cập nhật
            public string? Action { get; set; } // Ghi chú cần cập nhật
        }

        public class UpdateScrapRequest
        {
            public List<string>? SerialNumbers { get; set; }
            public string? ScrapStatus { get; set; }
        }

        /// <summary>
        /// LẤY TOP MODEL_NAME TRONG PRODUCT
        /// </summary>
        /// <returns></returns>
        [HttpGet("top_models")]
        public async Task<IActionResult> GetTopModels()
        {
            try
            {
                // Lấy top 10 modelName và số lượng của chúng, nhóm theo modelName
                var topModels = await _sqlContext.Products
                    .GroupBy(p => p.ModelName)
                    .Select(g => new
                    {
                        ModelName = g.Key,
                        Count = g.Count()
                    })
                    .OrderByDescending(x => x.Count)
                    .Take(10)
                    .ToListAsync();

                return Ok(new { success = true, topModels });
            }
            catch (Exception ex)
            {
                // Xử lý lỗi và trả về phản hồi thất bại
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        /// <summary>
        /// API LẤY thông tin để CHECKIN
        /// </summary>
        /// <param name="serialNumber"></param>
        /// <returns></returns>
        [HttpGet("SearchCheckIn")]
        public async Task<IActionResult> SearchCheckIn(string serialNumber)
        {
            try
            {
                Console.WriteLine($"Fetching info for SerialNumber: {serialNumber}");

                // Lấy ModelName từ bảng SFISM4.R107876u
                string modelNameQuery = @"
        SELECT SERIAL_NUMBER, MODEL_NAME, MO_NUMBER, WIP_GROUP 
        FROM SFISM4.R107 
        WHERE SERIAL_NUMBER = :serialNumber AND ROWNUM = 1";

                var modelNameParam = new OracleParameter("serialNumber", OracleDbType.Varchar2) { Value = serialNumber };

                var modelNameResult = await _oracleContext.OracleDataR107
                    .FromSqlRaw(modelNameQuery, modelNameParam)
                    .AsNoTracking()
                    .ToListAsync();

                if (modelNameResult == null || !modelNameResult.Any())
                {
                    return NotFound(new { success = false, message = $"Không tìm thấy ModelName cho SerialNumber {serialNumber}" });
                }

                string modelName = modelNameResult.First().MODEL_NAME;

                // Lấy ProductLine từ bảng SFIS1.C_MODEL_DESC_T
                string productLineQuery = @"
        SELECT MODEL_NAME, PRODUCT_LINE 
        FROM SFIS1.C_MODEL_DESC_T 
        WHERE MODEL_NAME = :modelName AND ROWNUM = 1";

                var productLineParam = new OracleParameter("modelName", OracleDbType.Varchar2) { Value = modelName };

                var productLineResult = await _oracleContext.OracleDataCModelDesc
                    .FromSqlRaw(productLineQuery, productLineParam)
                    .AsNoTracking()
                    .Select(pl => new
                    {
                        MODEL_NAME = pl.MODEL_NAME,
                        PRODUCT_LINE = pl.PRODUCT_LINE ?? ""
                    }).ToListAsync();

                string productLine = productLineResult.FirstOrDefault()?.PRODUCT_LINE ?? "";

                // Lấy Test_group, Test_code, Data1 từ bảng SFISM4.R109
                string testInfoQuery = @"
        SELECT TEST_GROUP, TEST_CODE, DATA1 
        FROM (
            SELECT TEST_GROUP, TEST_CODE, DATA1 
            FROM SFISM4.R109 
            WHERE SERIAL_NUMBER = :serialNumber 
            ORDER BY TEST_TIME DESC
        )
        WHERE ROWNUM = 1";

                // Truy vấn thủ công sử dụng OracleCommand
                using (var connection = _oracleContext.Database.GetDbConnection())
                {
                    await connection.OpenAsync();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = testInfoQuery;
                        command.Parameters.Add(new OracleParameter("serialNumber", OracleDbType.Varchar2) { Value = serialNumber });

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (reader.Read())
                            {
                                var testGroup = reader["TEST_GROUP"]?.ToString();
                                var testCode = reader["TEST_CODE"]?.ToString();
                                var data1 = reader["DATA1"]?.ToString();

                                return Ok(new
                                {
                                    success = true,
                                    modelName,
                                    productLine,
                                    testGroup,
                                    testCode,
                                    data1
                                });
                            }
                        }
                    }
                }

                // Nếu không có dữ liệu từ bảng R109
                return Ok(new
                {
                    success = true,
                    modelName,
                    productLine,
                    testInfo = "Không có dữ liệu Test_group, Test_code, Data1"
                });
            }
            catch (OracleException ex)
            {
                Console.WriteLine($"Oracle Error: {ex.Message}");
                return StatusCode(500, new { success = false, message = $"Lỗi Oracle: {ex.Message}" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"System Error: {ex.Message}");
                return StatusCode(500, new { success = false, message = $"Lỗi hệ thống: {ex.Message}" });
            }
        }
        private async Task LogAction(string action, string serialNumber, string user, string note = null)
        {
            _sqlContext.Logs.Add(new LogKhoScrap
            {
                Action = action,
                SerialNumber = serialNumber,
                User = user,
                Note = note,
                Timestamp = DateTime.Now
            });
            await _sqlContext.SaveChangesAsync();
        }
        public class InforSN
        {
            public string wipGroup { get; set; } = string.Empty;
            public string productLine { get; set; } = string.Empty;
            public string modelName { get; set; } = string.Empty;

        }
    }
}
