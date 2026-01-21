using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using API_WEB.ModelsDB;
using System.ComponentModel.DataAnnotations;

namespace API_WEB.Controllers.MaterialSystem
{
    [Route("[controller]")]
    [ApiController]
    public class MaterialSystemController : ControllerBase
    {
        private readonly CSDL_NE _sqlContext;

        // DTOs with validation
        public class AddMaterialRequest
        {
            [Required] public string MaAll { get; set; }
            [Required] public string MaLieu { get; set; }
            public string NhaCungUng { get; set; }
            public string DateCode { get; set; }
            public string LotCode { get; set; }
            [Range(1, int.MaxValue)] public int Qty { get; set; }
            public string OP { get; set; }
            public string ESD { get; set; }
            [Required] public string Location { get; set; }
            public string Remark { get; set; }
        }

        public class ExportDefectiveMaterialRequest
        {
            [Required] public string MaLieu { get; set; }
            public string NhaCungUng { get; set; }
            public string DateCode { get; set; }
            public string LotCode { get; set; }
            [Range(1, int.MaxValue)] public int Qty { get; set; }
            public string OP { get; set; }
            public string Remark { get; set; }
        }

        public class BorrowMaterialRequest
        {
            public string MaLieu { get; set; }
            public string NhaCungUng { get; set; }
            public string DateCode { get; set; }
            public string LotCode { get; set; }
            [Range(0, int.MaxValue)] public int QtyOK { get; set; }
            [Range(0, int.MaxValue)] public int QtyNG { get; set; }
            public string OP { get; set; }
            public string OPborrow { get; set; }
            public string MaAllPart { get; set; }
        }

        public class ReturnMaterialRequest
        {
            [Required] public string Task { get; set; }
            [Required] public string MaLieu { get; set; }
            public string NhaCungUng { get; set; }
            public string DateCode { get; set; }
            public string LotCode { get; set; }
            [Range(0, int.MaxValue)] public int QtyOK { get; set; }
            [Range(0, int.MaxValue)] public int QtyNG { get; set; }
            public string OP { get; set; }
            public string Remark { get; set; }
        }

        public class RequestType
        {
            public string Type { get; set; }
        }

        public class BorrowingSummaryResponse
        {
            public string Task { get; set; }
            public string MaLieu { get; set; }
            public string NhaCungUng { get; set; }
            public string DateCode { get; set; }
            public string LotCode { get; set; }
            public string Op1 { get; set; }
            public string Op2 { get; set; }
            public int Qty1 { get; set; }
            public int Qty2 { get; set; }
            public int Qty3 { get; set; }
            public int Qty4 { get; set; }
            public DateTime? ReturnTime { get; set; }
            public DateTime? BorrowedTime { get; set; }
        }

        public class SearchByMaLieuRequest
        {
            [Required] public string MaLieu { get; set; }
        }

        public MaterialSystemController(CSDL_NE sqlContext)
        {
            _sqlContext = sqlContext ?? throw new ArgumentNullException(nameof(sqlContext));
        }

        private async Task<string> GenerateTaskId()
        {
            var now = DateTime.Now;
            string datePrefix = now.ToString("yyyyMMdd");
            int taskCount = await _sqlContext.HistoryMaterials
                .CountAsync(hm => hm.TASK.StartsWith(datePrefix));
            string taskNumber = taskCount.ToString("D3");
            return $"{datePrefix}{taskNumber}";
        }

        private async Task<SumMaterial?> GetSumMaterial(string maLieu, string nhaCungUng, string dateCode, string lotCode)
        {
            return await _sqlContext.SumMaterials
                .FirstOrDefaultAsync(sm =>
                    sm.MA_LIEU == (maLieu ?? "") &&
                    sm.NHA_CUNG_UNG == (nhaCungUng ?? "") &&
                    sm.DATE_CODE == (dateCode ?? "") &&
                    sm.LOT_CODE == (lotCode ?? ""));
        }

        [HttpPost("AddMaterial")]
        public async Task<IActionResult> AddMaterial([FromBody] AddMaterialRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                if (await _sqlContext.HistoryMaterials.AnyAsync(m => m.MA_ALL == request.MaAll && m.TYPE == "Input"))
                    return BadRequest(new { message = "Tem Allpart đã tồn tại. Không thể thêm mới!" });

                var taskValue = await GenerateTaskId();

                var historyMaterial = new HistoryMaterial
                {
                    MA_ALL = request.MaAll,
                    MA_LIEU = request.MaLieu,
                    NHA_CUNG_UNG = request.NhaCungUng ?? "",
                    DATE_CODE = request.DateCode ?? "",
                    LOT_CODE = request.LotCode ?? "",
                    QTY1 = request.Qty,
                    OP1 = request.OP ?? "",
                    ESD = request.ESD ?? "",
                    LOCATION = request.Location,
                    REMARK = request.Remark,
                    TASK = taskValue,
                    TYPE = "Input",
                    BORROWED_TIME = DateTime.Now
                };

                _sqlContext.HistoryMaterials.Add(historyMaterial);

                var sumMaterial = await GetSumMaterial(request.MaLieu, request.NhaCungUng, request.DateCode, request.LotCode);
                if (sumMaterial == null)
                {
                    _sqlContext.SumMaterials.Add(new SumMaterial
                    {
                        MA_LIEU = request.MaLieu,
                        NHA_CUNG_UNG = request.NhaCungUng ?? "",
                        DATE_CODE = request.DateCode ?? "",
                        LOT_CODE = request.LotCode ?? "",
                        TONG_LINH = request.Qty,
                        SO_LUONG_OK = request.Qty,
                        SO_LUONG_NG = 0,
                        CHO_MUON = 0,
                        DA_BAO_PHE = 0,
                        ESD = request.ESD ?? "",
                        LOCATION = request.Location,
                        REMARK = request.Remark
                    });
                }
                else
                {
                    sumMaterial.TONG_LINH += request.Qty;
                    sumMaterial.SO_LUONG_OK += request.Qty;
                    sumMaterial.REMARK = request.Remark;
                }

                await _sqlContext.SaveChangesAsync();
                return Ok(new { message = "Material added successfully.", task = taskValue });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while adding material.", error = ex.Message });
            }
        }

        [HttpPost("ExportDefectiveMaterial")]
        public async Task<IActionResult> ExportDefectiveMaterial([FromBody] ExportDefectiveMaterialRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                using var transaction = await _sqlContext.Database.BeginTransactionAsync();
                var taskValue = await GenerateTaskId();

                var historyMaterial = new HistoryMaterial
                {
                    MA_LIEU = request.MaLieu,
                    NHA_CUNG_UNG = request.NhaCungUng ?? "",
                    DATE_CODE = request.DateCode ?? "",
                    LOT_CODE = request.LotCode ?? "",
                    QTY1 = request.Qty,
                    OP1 = request.OP ?? "",
                    REMARK = request.Remark,
                    TASK = taskValue,
                    TYPE = "DefectiveExport",
                    BORROWED_TIME = DateTime.Now,
                    ESD = "N/A"
                };

                _sqlContext.HistoryMaterials.Add(historyMaterial);

                var sumMaterial = await GetSumMaterial(request.MaLieu, request.NhaCungUng, request.DateCode, request.LotCode);
                if (sumMaterial == null)
                {
                    await transaction.RollbackAsync();
                    return NotFound(new { message = "No matching record found in SumMaterial." });
                }

                int newSoLuongNG = sumMaterial.SO_LUONG_NG - request.Qty;
                if (newSoLuongNG < 0)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new
                    {
                        message = "Invalid operation: SO_LUONG_NG cannot be negative.",
                        currentSoLuongNG = sumMaterial.SO_LUONG_NG,
                        requestedQty = request.Qty
                    });
                }

                sumMaterial.SO_LUONG_NG = newSoLuongNG;
                sumMaterial.DA_BAO_PHE += request.Qty;

                await _sqlContext.SaveChangesAsync();
                await transaction.CommitAsync();
                return Ok(new { message = "Defective material exported successfully.", task = taskValue });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while exporting defective material.", error = ex.Message });
            }
        }

        [HttpPost("BorrowMaterial")]
        public async Task<IActionResult> BorrowMaterial([FromBody] BorrowMaterialRequest request)
        {
            if (!ModelState.IsValid || (request.QtyOK + request.QtyNG) == 0)
                return BadRequest(new { message = "Invalid operation: Total borrowed quantity (QtyOK + QtyNG) cannot be zero." });

            try
            {
                using var transaction = await _sqlContext.Database.BeginTransactionAsync();
                var taskValue = await GenerateTaskId();

                var historyMaterial = new HistoryMaterial
                {
                    MA_LIEU = request.MaLieu ?? "",
                    MA_ALL = request.MaAllPart ?? "",
                    NHA_CUNG_UNG = request.NhaCungUng ?? "",
                    DATE_CODE = request.DateCode ?? "",
                    LOT_CODE = request.LotCode ?? "",
                    QTY1 = request.QtyOK,
                    QTY2 = request.QtyNG,
                    OP1 = request.OP ?? "",
                    OP2 = request.OPborrow,
                    TASK = taskValue,
                    TYPE = "Borrow",
                    BORROWED_TIME = DateTime.Now,
                    ESD = "N/A"
                };

                _sqlContext.HistoryMaterials.Add(historyMaterial);

                var sumMaterial = await GetSumMaterial(request.MaLieu, request.NhaCungUng, request.DateCode, request.LotCode);
                if (sumMaterial == null)
                {
                    await transaction.RollbackAsync();
                    return NotFound(new { message = "No matching record found in SumMaterial." });
                }

                int newSoLuongOK = sumMaterial.SO_LUONG_OK - request.QtyOK;
                int newSoLuongNG = sumMaterial.SO_LUONG_NG - request.QtyNG;

                if (newSoLuongOK < 0 || newSoLuongNG < 0)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new
                    {
                        message = $"Invalid operation: {(newSoLuongOK < 0 ? "SO_LUONG_OK" : "SO_LUONG_NG")} cannot be negative.",
                        currentSoLuongOK = sumMaterial.SO_LUONG_OK,
                        currentSoLuongNG = sumMaterial.SO_LUONG_NG,
                        requestedQtyOK = request.QtyOK,
                        requestedQtyNG = request.QtyNG
                    });
                }

                sumMaterial.SO_LUONG_OK = newSoLuongOK;
                sumMaterial.SO_LUONG_NG = newSoLuongNG;
                sumMaterial.CHO_MUON += (request.QtyOK + request.QtyNG);

                await _sqlContext.SaveChangesAsync();
                await transaction.CommitAsync();
                return Ok(new { message = "Material borrowed successfully.", task = taskValue });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while borrowing material.", error = ex.Message });
            }
        }

        [HttpPost("ReturnMaterial")]
        public async Task<IActionResult> ReturnMaterial([FromBody] ReturnMaterialRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                using var transaction = await _sqlContext.Database.BeginTransactionAsync();
                var historyMaterial = await _sqlContext.HistoryMaterials
                    .FirstOrDefaultAsync(hm => hm.TASK == request.Task);

                if (historyMaterial == null)
                {
                    await transaction.RollbackAsync();
                    return NotFound(new { message = "No matching record found in HistoryMaterial for the given Task." });
                }

                int borrowedTotal = (historyMaterial.QTY1 ?? 0) + (historyMaterial.QTY2 ?? 0);
                int returnedTotal = request.QtyOK + request.QtyNG;

                if (borrowedTotal != returnedTotal)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new
                    {
                        message = "Invalid operation: Total returned quantity must match the total borrowed quantity.",
                        borrowedTotal,
                        returnedTotal
                    });
                }

                historyMaterial.QTY3 = request.QtyOK;
                historyMaterial.QTY4 = request.QtyNG;
                historyMaterial.RETURN_TIME = DateTime.Now;
                historyMaterial.OP2 = request.OP;
                historyMaterial.REMARK = request.Remark;

                var sumMaterial = await GetSumMaterial(request.MaLieu, request.NhaCungUng, request.DateCode, request.LotCode);
                if (sumMaterial == null)
                {
                    await transaction.RollbackAsync();
                    return NotFound(new { message = "No matching record found in SumMaterial." });
                }

                int newChoMuon = sumMaterial.CHO_MUON - (request.QtyOK + request.QtyNG);
                if (newChoMuon < 0)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new
                    {
                        message = "Invalid operation: CHO_MUON cannot be negative.",
                        currentChoMuon = sumMaterial.CHO_MUON,
                        requestedQtyOK = request.QtyOK,
                        requestedQtyNG = request.QtyNG
                    });
                }

                sumMaterial.SO_LUONG_OK += request.QtyOK;
                sumMaterial.SO_LUONG_NG += request.QtyNG;
                sumMaterial.CHO_MUON = newChoMuon;

                await _sqlContext.SaveChangesAsync();
                await transaction.CommitAsync();
                return Ok(new { message = "Material returned successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while returning material.", error = ex.Message });
            }
        }

        [HttpGet("GetAllSumMaterials")]
        public async Task<IActionResult> GetAllSumMaterials()
        {
            try
            {
                var sumMaterials = await _sqlContext.SumMaterials.ToListAsync();
                return Ok(sumMaterials);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while retrieving SumMaterial data.", error = ex.Message });
            }
        }

        [HttpPost("GetHistory")]
        public async Task<IActionResult> GetHistory([FromBody] RequestType type)
        {
            try
            {
                var query = _sqlContext.HistoryMaterials.AsQueryable();

                //If type is "Borrow" -> exclude MA_LIEU starting with RES, CAP, 1, or 032
                if (!string.IsNullOrEmpty(type?.Type) && type.Type.Equals("Borrow", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(t =>
                        t.TYPE == "Borrow" &&
                        !(t.MA_LIEU != null && (
                            t.MA_LIEU.ToUpper().StartsWith("RES") ||
                            t.MA_LIEU.ToUpper().StartsWith("CAP") ||
                            //t.MA_LIEU.StartsWith("1") ||
                            t.MA_LIEU.StartsWith("032")
                        ))
                    );
                }
                // 3️⃣ If another type is specified -> filter by that type
                else if (!string.IsNullOrEmpty(type?.Type))
                {
                    query = query.Where(t => t.TYPE == type.Type);
                }
                // If type is "" -> return all records
                else
                {
                    // No filtering
                }
                var transactions = await query.ToListAsync();
                return Ok(transactions);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while retrieving history.",
                    error = ex.Message
                });
            }
        }

        [HttpGet("GetBorrowingSummary")]
        public async Task<IActionResult> GetBorrowingSummary()
        {
            try
            {
                var borrowingSummary = await _sqlContext.HistoryMaterials
                    .AsNoTracking()
                    .Where(hm => hm.TYPE == "Borrow")
                    .Where(hm => (hm.QTY1 ?? 0) + (hm.QTY2 ?? 0) > (hm.QTY3 ?? 0) + (hm.QTY4 ?? 0))
                    // ❗ Loại bỏ MA_LIEU bắt đầu bằng RES / CAP / 1 / 032
                    .Where(hm => !(EF.Functions.Like(hm.MA_LIEU!, "RES%")
                                || EF.Functions.Like(hm.MA_LIEU!, "CAP%")
                                //|| EF.Functions.Like(hm.MA_LIEU!, "1%")
                                || EF.Functions.Like(hm.MA_LIEU!, "032%")))
                    .Select(hm => new BorrowingSummaryResponse
                    {
                        Task = hm.TASK,
                        MaLieu = hm.MA_LIEU,
                        NhaCungUng = hm.NHA_CUNG_UNG,
                        DateCode = hm.DATE_CODE,
                        LotCode = hm.LOT_CODE,
                        Op1 = hm.OP1 ?? string.Empty,
                        Op2 = hm.OP2 ?? string.Empty,
                        Qty1 = hm.QTY1 ?? 0,
                        Qty2 = hm.QTY2 ?? 0,
                        Qty3 = hm.QTY3 ?? 0,
                        Qty4 = hm.QTY4 ?? 0,
                        ReturnTime = hm.RETURN_TIME,   // đảm bảo map đúng kiểu DateTime? trong entity
                        BorrowedTime = hm.BORROWED_TIME
                    })
                    .OrderByDescending(x => x.BorrowedTime)
                    .ToListAsync();

                return Ok(borrowingSummary);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while retrieving borrowing summary.", error = ex.Message });
            }
        }



        [HttpPost("SearchByMaLieu")]
        public async Task<IActionResult> SearchByMaLieu([FromBody] SearchByMaLieuRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var sumMaterials = await _sqlContext.SumMaterials
                    .Where(sm => sm.MA_LIEU == request.MaLieu)
                    .ToListAsync();

                if (!sumMaterials.Any())
                    return NotFound(new { message = "No records found for the given MaLieu." });

                return Ok(sumMaterials);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while searching for materials.", error = ex.Message });
            }
        }
    }
}