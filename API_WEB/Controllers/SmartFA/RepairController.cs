using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using API_WEB.Models.SmartFA;
using API_WEB.ModelsDB;
using API_WEB.ModelsOracle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace API_WEB.Controllers.SmartFA
{
    [ApiController]
    [Route("[controller]")]
    public class RepairController : ControllerBase
    {
        private readonly CSDL_NE _sqlContext;
        private readonly OracleDbContext _oracleContext;

        public RepairController(CSDL_NE sqlContext, OracleDbContext oracleContext)
        {
            _sqlContext = sqlContext ?? throw new ArgumentNullException(nameof(sqlContext));
            _oracleContext = oracleContext ?? throw new ArgumentNullException(nameof(oracleContext));
        }

        [HttpGet("{serialNumber}")]
        public async Task<IActionResult> GetRepairData(string serialNumber)
        {
            if (string.IsNullOrWhiteSpace(serialNumber))
            {
                return BadRequest(new { success = false, message = "SerialNumber is required." });
            }

            var latestFault = await _oracleContext.OracleDataRepairTaskDetail
                .Where(x => x.SERIAL_NUMBER == serialNumber)
                .OrderByDescending(x => x.TEST_TIME)
                .Select(x => new
                {
                    x.MODEL_NAME,
                    x.TEST_CODE,
                    x.DATA1,
                    x.TEST_GROUP
                })
                .FirstOrDefaultAsync();

            if (latestFault == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy lỗi mới nhất của serial." });
            }

            var productLine = await _oracleContext.OracleDataCModelDesc
                .Where(m => m.MODEL_NAME == latestFault.MODEL_NAME)
                .Select(m => m.PRODUCT_LINE)
                .FirstOrDefaultAsync() ?? string.Empty;

            var faultRecord = await _sqlContext.FaultRecords
                .Where(f => f.SerialNumber == serialNumber)
                .OrderByDescending(f => f.CreatedAt)
                .FirstOrDefaultAsync();

            if (faultRecord == null || !string.Equals(faultRecord.ErrorCode, latestFault.TEST_CODE, StringComparison.OrdinalIgnoreCase))
            {
                faultRecord = new FaultRecord
                {
                    FaultId = Guid.NewGuid(),
                    SerialNumber = serialNumber,
                    ProductLine = productLine,
                    ModelName = latestFault.MODEL_NAME ?? string.Empty,
                    ErrorCode = latestFault.TEST_CODE ?? string.Empty,
                    ErrorDesc = latestFault.DATA1,
                    TestGroup = latestFault.TEST_GROUP,
                    CreatedAt = DateTime.UtcNow
                };

                _sqlContext.FaultRecords.Add(faultRecord);
                await _sqlContext.SaveChangesAsync();
            }

            var cookbooks = await _sqlContext.Cookbooks
                .Where(c => c.ModelName == latestFault.MODEL_NAME && c.ErrorCode == latestFault.TEST_CODE)
                .ToListAsync();

            var cookbookIds = cookbooks.Select(c => c.CookbookId).ToList();
            var steps = await _sqlContext.CookbookSteps
                .Where(s => cookbookIds.Contains(s.CookbookId))
                .OrderBy(s => s.StepOrder)
                .ToListAsync();

            var statusHistory = await _sqlContext.RepairStepStatuses
                .Where(s => s.FaultId == faultRecord.FaultId)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            var latestStatusByStep = statusHistory
                .GroupBy(s => s.StepId)
                .ToDictionary(g => g.Key, g => g.First());

            CookbookDto? MapCookbook(string type)
            {
                var cookbook = cookbooks.FirstOrDefault(c => string.Equals(c.TypeCb, type, StringComparison.OrdinalIgnoreCase));
                if (cookbook == null)
                {
                    return null;
                }

                var cbSteps = steps.Where(s => s.CookbookId == cookbook.CookbookId)
                    .OrderBy(s => s.StepOrder)
                    .Select(s =>
                    {
                        latestStatusByStep.TryGetValue(s.StepId, out var status);
                        return new RepairStepDto
                        {
                            StepId = s.StepId,
                            Order = s.StepOrder,
                            Text = s.StepText,
                            Status = status?.Status,
                            User = status?.UserRepair,
                            UpdatedAt = status?.CreatedAt
                        };
                    })
                    .ToList();

                return new CookbookDto
                {
                    CookbookId = cookbook.CookbookId,
                    Type = cookbook.TypeCb,
                    Steps = cbSteps
                };
            }

            var testResult = await BuildTestResultAsync(serialNumber);

            var response = new RepairResponse
            {
                Fault = new FaultDto
                {
                    FaultId = faultRecord.FaultId,
                    SerialNumber = faultRecord.SerialNumber,
                    ProductLine = faultRecord.ProductLine,
                    ModelName = faultRecord.ModelName,
                    ErrorCode = faultRecord.ErrorCode,
                    ErrorDesc = faultRecord.ErrorDesc,
                    TestGroup = faultRecord.TestGroup
                },
                NvCookbook = MapCookbook("NV"),
                FxnCookbook = MapCookbook("FXN"),
                TestResult = testResult
            };

            if (response.NvCookbook != null && testResult != null)
            {
                response.NvCookbook.TestResultNote = testResult.DisplayValue;
            }

            if (response.FxnCookbook != null && testResult != null)
            {
                response.FxnCookbook.TestResultNote = testResult.DisplayValue;
            }

            return Ok(new { success = true, data = response });
        }

        [HttpPost("step-status")]
        public async Task<IActionResult> SaveStepStatus([FromBody] RepairStepStatusRequest request)
        {
            if (request == null)
            {
                return BadRequest(new { success = false, message = "Payload is required." });
            }

            if (request.fault_id == Guid.Empty)
            {
                return BadRequest(new { success = false, message = "fault_id is required." });
            }

            var trimmedStatus = request.status?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedStatus))
            {
                return BadRequest(new { success = false, message = "status is required." });
            }

            var fault = await _sqlContext.FaultRecords.FirstOrDefaultAsync(f => f.FaultId == request.fault_id);
            if (fault == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy fault tương ứng." });
            }

            var userRepair = request.user_repair?.Trim();
            var suggestionText = request.suggestion_text?.Trim();
            Guid resolvedCookbookId;
            Guid resolvedStepId;

            if (request.is_suggestion)
            {
                var fxnCookbook = await _sqlContext.Cookbooks
                    .FirstOrDefaultAsync(c => c.ModelName == fault.ModelName && c.ErrorCode == fault.ErrorCode && c.TypeCb == "FXN");

                if (fxnCookbook == null)
                {
                    fxnCookbook = new Cookbook
                    {
                        CookbookId = Guid.NewGuid(),
                        TypeCb = "FXN",
                        ModelName = fault.ModelName,
                        ErrorCode = fault.ErrorCode,
                        TestGroup = fault.TestGroup ?? string.Empty,
                        CreatedAt = DateTime.UtcNow
                    };

                    _sqlContext.Cookbooks.Add(fxnCookbook);
                    await _sqlContext.SaveChangesAsync();
                }

                var nextOrder = await _sqlContext.CookbookSteps
                    .Where(s => s.CookbookId == fxnCookbook.CookbookId)
                    .Select(s => (int?)s.StepOrder)
                    .MaxAsync() ?? 0;

                var newStep = new CookbookStep
                {
                    StepId = Guid.NewGuid(),
                    CookbookId = fxnCookbook.CookbookId,
                    StepOrder = nextOrder + 1,
                    StepText = string.IsNullOrWhiteSpace(suggestionText)
                        ? "AI debug suggestion"
                        : $"AI debug suggestion: {suggestionText}",
                    CreatedAt = DateTime.UtcNow
                };

                _sqlContext.CookbookSteps.Add(newStep);

                resolvedCookbookId = fxnCookbook.CookbookId;
                resolvedStepId = newStep.StepId;
            }
            else
            {
                if (!request.cookbook_id.HasValue || !request.step_id.HasValue || request.cookbook_id == Guid.Empty || request.step_id == Guid.Empty)
                {
                    return BadRequest(new { success = false, message = "cookbook_id và step_id là bắt buộc." });
                }

                resolvedCookbookId = request.cookbook_id.Value;
                resolvedStepId = request.step_id.Value;

                var cookbookExists = await _sqlContext.Cookbooks.AnyAsync(c => c.CookbookId == resolvedCookbookId);
                var stepExists = await _sqlContext.CookbookSteps.AnyAsync(s => s.StepId == resolvedStepId);

                if (!cookbookExists || !stepExists)
                {
                    return NotFound(new { success = false, message = "Không tìm thấy cookbook/step tương ứng." });
                }
            }

            var status = new RepairStepStatus
            {
                Id = Guid.NewGuid(),
                FaultId = request.fault_id,
                CookbookId = resolvedCookbookId,
                StepId = resolvedStepId,
                UserRepair = userRepair,
                Status = trimmedStatus,
                CreatedAt = DateTime.UtcNow
            };

            _sqlContext.RepairStepStatuses.Add(status);
            await _sqlContext.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                data = new
                {
                    status,
                    cookbook_id = resolvedCookbookId,
                    step_id = resolvedStepId
                }
            });
        }

        [HttpPost("fxn-cookbook")]
        public async Task<IActionResult> CreateFxnCookbook([FromBody] CreateCookbookRequest request)
        {
            if (request == null || request.FaultId == Guid.Empty)
            {
                return BadRequest(new { success = false, message = "fault_id is required." });
            }

            var stepText = request.StepText?.Trim();
            if (string.IsNullOrWhiteSpace(stepText))
            {
                return BadRequest(new { success = false, message = "step_text is required." });
            }

            var fault = await _sqlContext.FaultRecords.FirstOrDefaultAsync(f => f.FaultId == request.FaultId);
            if (fault == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy fault tương ứng." });
            }

            var fxnCookbook = await _sqlContext.Cookbooks
                .FirstOrDefaultAsync(c => c.ModelName == fault.ModelName && c.ErrorCode == fault.ErrorCode && c.TypeCb == "FXN");

            var isNewCookbook = fxnCookbook == null;
            if (isNewCookbook)
            {
                fxnCookbook = new Cookbook
                {
                    CookbookId = Guid.NewGuid(),
                    TypeCb = "FXN",
                    ModelName = fault.ModelName,
                    ErrorCode = fault.ErrorCode,
                    TestGroup = fault.TestGroup ?? string.Empty,
                    CreatedAt = DateTime.UtcNow
                };

                _sqlContext.Cookbooks.Add(fxnCookbook);
            }

            var nextOrder = isNewCookbook
                ? 0
                : await _sqlContext.CookbookSteps
                    .Where(s => s.CookbookId == fxnCookbook!.CookbookId)
                    .Select(s => (int?)s.StepOrder)
                    .MaxAsync() ?? 0;

            var newStep = new CookbookStep
            {
                StepId = Guid.NewGuid(),
                CookbookId = fxnCookbook!.CookbookId,
                StepOrder = nextOrder + 1,
                StepText = stepText,
                CreatedAt = DateTime.UtcNow
            };

            _sqlContext.CookbookSteps.Add(newStep);
            await _sqlContext.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                data = new
                {
                    cookbook = new
                    {
                        cookbook_id = fxnCookbook!.CookbookId,
                        type_cb = fxnCookbook.TypeCb,
                        model_name = fxnCookbook.ModelName,
                        error_code = fxnCookbook.ErrorCode,
                        test_group = fxnCookbook.TestGroup,
                        created_at = fxnCookbook.CreatedAt
                    },
                    step = new
                    {
                        step_id = newStep.StepId,
                        cookbook_id = newStep.CookbookId,
                        step_order = newStep.StepOrder,
                        step_text = newStep.StepText,
                        created_at = newStep.CreatedAt
                    }
                }
            });
        }

        [HttpGet("{serialNumber}/test-result")]
        public async Task<IActionResult> GetTestResult(string serialNumber)
        {
            if (string.IsNullOrWhiteSpace(serialNumber))
            {
                return BadRequest(new { success = false, message = "SerialNumber is required." });
            }

            var statusHistory = await _sqlContext.RepairStepStatuses
                .Join(_sqlContext.FaultRecords,
                    status => status.FaultId,
                    fault => fault.FaultId,
                    (status, fault) => new { status, fault })
                .Where(x => x.fault.SerialNumber == serialNumber)
                .Select(x => x.status)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            var testResult = await BuildTestResultAsync(serialNumber);
            return Ok(new { success = true, data = testResult });
        }

        private async Task<TestResultDto?> BuildTestResultAsync(string serialNumber)
        {
            var testResult = await _oracleContext.UltResults
                .Where(r => r.SERIAL_NUMBER == serialNumber && r.GROUP_NAME.Contains("OFF"))
                .OrderByDescending(r => r.PASS_DATE)
                .Select(r => new TestResultDto
                {
                    PassDate = r.PASS_DATE,
                    Data5 = r.DATA5
                })
                .FirstOrDefaultAsync();

            if (testResult == null || testResult.PassDate == null)
            {
                return testResult;
            }

            var lastRepairAt = await _sqlContext.RepairStepStatuses
                .Join(_sqlContext.FaultRecords,
                    status => status.FaultId,
                    fault => fault.FaultId,
                    (status, fault) => new { status, fault })
                .Where(x => x.fault.SerialNumber == serialNumber)
                .OrderByDescending(x => x.status.CreatedAt)
                .Select(x => (DateTime?)x.status.CreatedAt)
                .FirstOrDefaultAsync();

            testResult.ShowData5 = lastRepairAt.HasValue && testResult.PassDate > lastRepairAt.Value;
            testResult.DisplayValue = testResult.ShowData5
                ? (string.IsNullOrWhiteSpace(testResult.Data5) ? "Pending Test" : testResult.Data5)
                : "Pending Test";
            return testResult;
        }
    }
}
