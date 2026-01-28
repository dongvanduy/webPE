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
            if (request == null)
            {
                return BadRequest("Dữ liệu không hợp lệ.");
            }

            var serialNumbers = request.SerialNumbers?
                .Where(sn => !string.IsNullOrWhiteSpace(sn))
                .Select(sn => sn.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (serialNumbers == null || serialNumbers.Count == 0)
            {
                return BadRequest("Danh sách serialNumber trống.");
            }

            if (string.IsNullOrWhiteSpace(request.HbMb) || string.IsNullOrWhiteSpace(request.TypeBonepile))
            {
                return BadRequest("HB_MB và typeBonepile là bắt buộc.");
            }

            var existingSerials = await _sqlContext.DPUManagers
                .Where(d => serialNumbers.Contains(d.SerialNumber))
                .Select(d => d.SerialNumber)
                .ToListAsync();

            var existingSerialSet = existingSerials
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var insertItems = new List<DPUManager>();
            var notFoundInOracle = new List<string>();

            const string query = @"
SELECT SERIAL_NUMBER AS SerialNumber,
       MODEL_NAME AS ModelName,
       DATA4 AS DescFirstFail,
       IN_STATION_TIME AS FirstFailTime
FROM (
    SELECT t.*,
           ROW_NUMBER() OVER (
               PARTITION BY t.serial_number
               ORDER BY t.in_station_time ASC
           ) rn
    FROM sfism4.r_fail_atedata_t t
    WHERE t.data4 LIKE '%DPU_MEM%' AND SERIAL_NUMBER = :serialNumber
)
WHERE rn = 1";

            foreach (var serialNumber in serialNumbers)
            {
                if (existingSerialSet.Contains(serialNumber))
                {
                    continue;
                }

                var oracleResult = await _oracleContext.RFailAteDataFirstFails
                    .FromSqlRaw(query, new OracleParameter("serialNumber", serialNumber))
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                if (oracleResult == null)
                {
                    notFoundInOracle.Add(serialNumber);
                }

                insertItems.Add(new DPUManager
                {
                    SerialNumber = serialNumber,
                    HbMb = request.HbMb.Trim(),
                    TypeBonepile = request.TypeBonepile.Trim(),
                    FirstFailTime = oracleResult?.FirstFailTime,
                    DescFirstFail = oracleResult?.DescFirstFail,
                    ModelName = oracleResult?.ModelName
                });
            }

            if (insertItems.Count == 0)
            {
                return Ok(new
                {
                    Message = "Không có serialNumber mới để thêm.",
                    ExistingSerials = existingSerials,
                    NotFoundInOracle = notFoundInOracle
                });
            }

            _sqlContext.DPUManagers.AddRange(insertItems);
            await _sqlContext.SaveChangesAsync();

            return Ok(new
            {
                Message = "Đã thêm dữ liệu vào DPUManager.",
                Inserted = insertItems.Count,
                ExistingSerials = existingSerials,
                NotFoundInOracle = notFoundInOracle
            });
        }
    }
}
