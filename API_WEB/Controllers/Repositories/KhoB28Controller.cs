using API_WEB.ModelsDB;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace API_WEB.Controllers.Repositories
{
    [Route("[controller]")]
    [ApiController]
    public class KhoB28Controller : ControllerBase
    {
        private readonly CSDL_NE _sqlContext;

        public KhoB28Controller(CSDL_NE sqlContext)
        {
            _sqlContext = sqlContext ?? throw new ArgumentNullException(nameof(sqlContext));
        }

        [HttpGet("GetAll")]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var data = await _sqlContext.KhoB28s
                    .OrderByDescending(item => item.InDate)
                    .ToListAsync();

                return Ok(new { success = true, data });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("Import")]
        public async Task<IActionResult> Import([FromBody] KhoB28ImportRequest request)
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
                var existing = await _sqlContext.KhoB28s.FirstOrDefaultAsync(item => item.SerialNumber == serial);
                if (existing != null)
                {
                    results.Add(new KhoB28ActionResult
                    {
                        SerialNumber = serial,
                        Success = false,
                        Message = "Serial Number đã tồn tại."
                    });
                    continue;
                }

                var newItem = new KhoB28
                {
                    SerialNumber = serial,
                    ModelName = request.ModelName,
                    Location = request.Location,
                    InBy = request.InBy,
                    InDate = DateTime.Now,
                    Status = "Available",
                    Note = request.Note
                };

                _sqlContext.KhoB28s.Add(newItem);
                results.Add(new KhoB28ActionResult
                {
                    SerialNumber = serial,
                    Success = true,
                    Message = "Nhập kho thành công."
                });
            }

            await _sqlContext.SaveChangesAsync();

            return Ok(new
            {
                success = results.Any(r => r.Success),
                results
            });
        }

        [HttpPost("Export")]
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

        [HttpPost("Borrow")]
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

                item.BorrowBy = request.BorrowBy;
                item.BorrowDate = DateTime.Now;
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

        [HttpPost("Return")]
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
                item.BorrowBy = null;
                item.BorrowDate = null;
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
    }

    public class KhoB28ImportRequest
    {
        public List<string> SerialNumbers { get; set; } = new();
        public string? ModelName { get; set; }
        public string? Location { get; set; }
        public string? InBy { get; set; }
        public string? Note { get; set; }
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
