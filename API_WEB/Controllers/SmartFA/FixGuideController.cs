using API_WEB.ModelsDB;
using API_WEB.ModelsOracle;
using DocumentFormat.OpenXml.InkML;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using Oracle.ManagedDataAccess.Client;
using System.Text.RegularExpressions;

namespace API_WEB.Controllers.SmartFA
{
    [Route("[controller]")]
    [ApiController]
    public class FixGuideController : ControllerBase
    {
        private readonly CSDL_NE _sqlContext;
        private readonly OracleDbContext _oracleContext;

        public FixGuideController(CSDL_NE sqlContext, OracleDbContext oracleContext)
        {
            _sqlContext = sqlContext;
            _oracleContext = oracleContext;
        }
        
        [HttpGet("GetProductLines")]
        public async Task<IActionResult> GetProductLines()
        {
            try
            {
                var productLines = await _sqlContext.Products
                    .Where(p => !string.IsNullOrWhiteSpace(p.ProductLine)) // bỏ trống hoặc null
                    .Select(p => p.ProductLine.Replace(" ", "").Trim().ToUpper())           // xóa khoảng trắng & in hoa
                    .Distinct()                                            //Loaị bỏ trùng lặp
                    .OrderBy(p => p)                                       // sắp xếp (tùy chọn)
                    .ToListAsync();

                return Ok(new { success = true, productLines });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Lỗi hệ thống: {ex.Message}" });
            }
        }


        [HttpPost("Upload")]
        public async Task<IActionResult> UploadGuide(IFormFile file, string productLine)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { success = false, message = "File không hợp lệ." });

            if (string.IsNullOrEmpty(productLine))
                return BadRequest(new { success = false, message = "ProductLine không được để trống." });

            try
            {
                // Chuẩn hóa ProductLine: Bỏ khoảng trắng, dấu '-' và viết hoa
                string standardizedProductLine = productLine.Replace(" ", "").Replace("-", "").ToUpper();

                // Thiết lập LicenseContext
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

                // Đọc nội dung file Excel
                using var stream = new MemoryStream();
                await file.CopyToAsync(stream);
                stream.Position = 0;
                using var package = new ExcelPackage(stream);
                var worksheet = package.Workbook.Worksheets.FirstOrDefault();

                if (worksheet == null)
                    return BadRequest(new { success = false, message = "Không tìm thấy sheet trong file Excel." });

                // Xóa dữ liệu cũ theo ProductLine (nếu cần)
                var existingGuides = _sqlContext.GuideRecords
                    .Where(g => g.ProductLine.Replace(" ", "").Replace("-", "").ToUpper() == standardizedProductLine);
                _sqlContext.GuideRecords.RemoveRange(existingGuides);
                await _sqlContext.SaveChangesAsync();

                // Lấy số hàng trong sheet
                int rowCount = worksheet.Dimension.Rows;

                // Thêm dữ liệu từ file Excel vào database
                for (int row = 2; row <= rowCount; row++) // Bỏ qua tiêu đề (hàng đầu tiên)
                {
                    string testCode = worksheet.Cells[row, 3].Text.Trim();
                    string checkPoint = worksheet.Cells[row, 6].Text.Trim();

                    if (!string.IsNullOrEmpty(testCode) && !string.IsNullOrEmpty(checkPoint))
                    {
                        _sqlContext.GuideRecords.Add(new GuideRecords
                        {
                            ProductLine = standardizedProductLine, // Gán ProductLine đã chuẩn hóa
                            Check_Point = checkPoint,
                            TestCode = testCode
                        });
                    }
                }

                await _sqlContext.SaveChangesAsync();
                return Ok(new { success = true, message = "File đã được upload và lưu thành công." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Đã xảy ra lỗi: {ex.Message}" });
            }
        }

        [HttpPost("UploadUser")]
        public async Task<IActionResult> UploadUser(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { success = false, message = "File không hợp lệ." });

            try
            {
                // Thiết lập LicenseContext
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

                // Đọc nội dung file Excel
                using var stream = new MemoryStream();
                await file.CopyToAsync(stream);
                using var package = new ExcelPackage(stream);
                var worksheet = package.Workbook.Worksheets.FirstOrDefault();

                if (worksheet == null)
                    return BadRequest(new { success = false, message = "Không tìm thấy sheet trong file Excel." });

                // Nếu muốn xóa hết dữ liệu trước khi nhập mới
                _sqlContext.Users.RemoveRange(_sqlContext.Users);
                await _sqlContext.SaveChangesAsync();

                // Lấy số hàng trong sheet
                int rowCount = worksheet.Dimension.Rows;

                // Danh sách lưu dữ liệu mới
                var newUsers = new List<User>();

                // Duyệt qua từng dòng, bỏ qua dòng đầu tiên (tiêu đề)
                for (int row = 2; row <= rowCount; row++)
                {
                    string userCode = worksheet.Cells[row, 2].Text.Trim();
                    string passwordHash = worksheet.Cells[row, 3].Text.Trim();
                    string role = worksheet.Cells[row, 4].Text.Trim();
                    string permissions = worksheet.Cells[row, 5].Text.Trim();
                    string fullName = worksheet.Cells[row, 6].Text.Trim();
                    string email = worksheet.Cells[row, 7].Text.Trim();
                    string status = worksheet.Cells[row, 8].Text.Trim();

                    // Chỉ thêm nếu có userCode (bắt buộc)
                    if (!string.IsNullOrEmpty(userCode))
                    {
                        newUsers.Add(new User
                        {
                            Username = userCode,
                            Password = passwordHash,
                            Role = role,
                            AllowedAreas = permissions,
                            FullName = fullName,
                            Email = email,
                            Department = status
                        });
                    }
                }

                // Thêm vào database
                if (newUsers.Count > 0)
                {
                    await _sqlContext.Users.AddRangeAsync(newUsers);
                    await _sqlContext.SaveChangesAsync();
                }

                return Ok(new { success = true, message = "Dữ liệu đã được upload thành công." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Đã xảy ra lỗi: {ex.Message}",
                    details = ex.InnerException?.Message
                });
            }
        }

        /// <summary>
        /// Edit checkpoint
        /// </summary>
        /// <param name="dto"></param>
        /// <returns></returns>
        [HttpPut("update-checkpoint")]
        public async Task<IActionResult> UpdateCheckPoint([FromBody] UpdateCheckPointDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.TestCode) || string.IsNullOrWhiteSpace(dto.ProductLine))
            {
                return BadRequest("Invalid input data.");
            }

            // Chuẩn hóa ProductLine
            dto.ProductLine = NormalizeProductLine(dto.ProductLine);

            var record = await _sqlContext.Set<GuideRecords>()
                .FirstOrDefaultAsync(r => r.TestCode == dto.TestCode && r.ProductLine == dto.ProductLine);

            if (record == null)
            {
                // Tạo mới bản ghi nếu không tìm thấy
                var newRecord = new GuideRecords
                {
                    TestCode = dto.TestCode,
                    ProductLine = dto.ProductLine,
                    Check_Point = dto.Check_Point
                };

                await _sqlContext.Set<GuideRecords>().AddAsync(newRecord);
                await _sqlContext.SaveChangesAsync();

                return Ok(new { success = true, message = "Đã tạo mới bản ghi và thêm Check_Point thành công." });
            }

            // Cập nhật Check_Point nếu bản ghi đã tồn tại
            record.Check_Point = dto.Check_Point;
            await _sqlContext.SaveChangesAsync();

            return Ok(new { success = true, message = "Đã cập nhật Check_Point thành công." });
        }
        private string NormalizeProductLine(string productLine)
        {
            return Regex.Replace(productLine.ToUpper(), @"[\s-]", "");
        }

        [HttpPost("GetCheckPoints")]
        public async Task<IActionResult> GetCheckPoints([FromBody] CheckPointRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.ProductLine) || string.IsNullOrEmpty(request.TestCode))
                {
                    return BadRequest(new { success = false, message = "ProductLine hoặc Data1 rỗng." });
                }

                // Chuẩn hóa ProductLine: viết hoa, bỏ khoảng trắng và dấu '-'
                string normalizedProductLine = NormalizeProductLine(request.ProductLine);


                // Truy vấn cơ sở dữ liệu để lấy CheckPoints
                var checkPoints = await _sqlContext.GuideRecords
                    .Where(g => g.ProductLine == normalizedProductLine && g.TestCode == request.TestCode)
                    .Select(g => g.Check_Point)
                    .ToListAsync();

                if (!checkPoints.Any())
                {
                    return NotFound(new { success = false, message = "Không tìm thấy CheckPoint phù hợp." });
                }

                return Ok(new { success = true, checkPoints });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Lỗi hệ thống: {ex.Message}" });
            }
        }

        public class CheckPointRequest
        {
            public string ProductLine { get; set; }
            public string TestCode { get; set; }
        }
        public class CheckInRequest
        {
            public string ProductLine { get; set; }
            public string ErrorCode { get; set; }
        }
        public class UpdateCheckPointDto
        {
            public string TestCode { get; set; }
            public string ProductLine { get; set; }
            public string Check_Point { get; set; }
        }
    }
}
