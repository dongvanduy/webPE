using System;
using System.Collections.Generic;
using System.Linq;
using API_WEB.ModelsOracle;
using API_WEB.Services.SmartFA;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace API_WEB.Controllers.SmartFA
{
    [Route("[controller]")]
    public class DataChartController : ControllerBase
    {
        private readonly OracleDbContext _oracleDbContext;
        private readonly ICellUpdateTracker _cellUpdateTracker;
        public DataChartController(OracleDbContext oracleDbContext, ICellUpdateTracker cellUpdateTracker)
        {
            _oracleDbContext = oracleDbContext ?? throw new ArgumentNullException(nameof(oracleDbContext));
            _cellUpdateTracker = cellUpdateTracker ?? throw new ArgumentNullException(nameof(cellUpdateTracker));
        }
        [HttpGet("getCountModelName")]
        public async Task<IActionResult> GetModelNameCount()
        {
            var data = await _oracleDbContext.OracleDataRepairTask
                .Where(r => r.MODEL_NAME != null)
                .GroupBy(r => r.MODEL_NAME)
                .Select(g => new { ModelName = g.Key, TotalCount = g.Count() })
                .ToListAsync();

            // ✅ Tính tổng số lượng tất cả models
            int totalAllModels = data.Sum(d => d.TotalCount);

            // ✅ Sắp xếp giảm dần theo TotalCount và lấy TOP 10
            var topModels = data.OrderByDescending(d => d.TotalCount).Take(10).ToList();

            return Ok(new
            {
                TotalAllModels = totalAllModels,  // ✅ Tổng số lượng tất cả models
                Models = topModels                // ✅ Danh sách 10 model có số lượng cao nhất
            });
        }
        [HttpGet("getCountLocation")]
        public async Task<IActionResult> GetLocationCount()
        {
            var now = DateTime.Now;

            var updateSnapshot = _cellUpdateTracker.GetAll();

            var locationData = await (
                from task in _oracleDbContext.OracleDataRepairTask
                join r107 in _oracleDbContext.OracleDataR107
                on task.SERIAL_NUMBER equals r107.SERIAL_NUMBER
                where !string.IsNullOrEmpty(task.DATA18) && !(task.DATA18.Contains("HIẾU ĐÓNG PHẾ")) &&
                    !(r107.WIP_GROUP.Contains("BR2C") || r107.WIP_GROUP.Contains("BCFA"))
                join err in _oracleDbContext.ErrorCodes
                    on task.TEST_CODE equals err.ERROR_CODE into errJoin
                from err in errJoin.DefaultIfEmpty()
                group new { task, err } by task.DATA18 into g
                select new
                {
                    Location = g.Key,
                    TotalCount = g.Count(),
                    Details = g.Select(x => new
                    {
                        SerialNumber = x.task.SERIAL_NUMBER,
                        TestCode = x.task.TEST_CODE,
                        ErrorDesc = x.err != null ? x.err.ERROR_DESC : string.Empty,
                        MONumber = x.task.MO_NUMBER,
                        ModelName = x.task.MODEL_NAME,
                        Aging = x.task.DATE3.HasValue ? Math.Round((now - x.task.DATE3.Value).TotalDays, 2) : (double?)null
                    }).ToList()
                })
                .ToListAsync();

            int totalAllLocations = locationData.Sum(d => d.TotalCount);

            var sortedLocations = locationData
    .OrderByDescending(d => d.TotalCount)
    .Select(item =>
    {
        var key = (item.Location ?? string.Empty).Trim().ToUpperInvariant();
        var hasUpdate = updateSnapshot.TryGetValue(key, out var updateInfo);
        return new LocationDto
        {
            Location = item.Location,
            TotalCount = item.TotalCount,
            Details = item.Details,
            IsUpdated = hasUpdate,
            LastUpdatedAt = updateInfo?.LastUpdatedAt
        };
    })
    .ToList();

            var seenLocations = new HashSet<string>(sortedLocations.Select(x => (x.Location ?? "").Trim().ToUpperInvariant()), StringComparer.OrdinalIgnoreCase);

            var trackedOnly = updateSnapshot
                .Where(entry => !seenLocations.Contains(entry.Key))
                .Select(entry => new LocationDto
                {
                    Location = entry.Key,
                    TotalCount = 0,
                    Details = Array.Empty<object>(),
                    IsUpdated = true,
                    LastUpdatedAt = entry.Value.LastUpdatedAt
                });

            var allLocations = sortedLocations.Concat(trackedOnly).ToList();


            return Ok(new
            {
                TotalAllLocations = totalAllLocations,
                Locations = allLocations
            });
        }

        [HttpGet("get-aging-counts")]
        public async Task<IActionResult> GetAgingCounts()
        {
            var now = DateTime.Now;

            var data = await (
                from task in _oracleDbContext.OracleDataRepairTask
                join r107 in _oracleDbContext.OracleDataR107
                on task.SERIAL_NUMBER equals r107.SERIAL_NUMBER
                where task.DATE3 != null && task.DATA18 != null &&
                      task.DATA18 != "TRONG_KHO" &&
                      task.DATA17 != "Nhận(Nhập Kho Phế)" &&
                      task.DATA17 != "Nhập(Kho Phế)" &&
                      task.DATA17 != "Nhận(Nhập Kho Ok)" &&
                      task.DATA18 != "KHO PHẾ" &&
                      task.DATA17 != "Nhận(Nhập Kho)" &&
                      task.DATA17 != "Nhận(Nhập kho)" &&
                      task.DATA17 != "Giao(Cho mượn từ Kho)" &&
                      !(r107.WIP_GROUP.Contains("BR2C") || r107.WIP_GROUP.Contains("BCFA"))
                join err in _oracleDbContext.ErrorCodes
                    on task.TEST_CODE equals err.ERROR_CODE into errJoin
                from err in errJoin.DefaultIfEmpty()
                select new
                {
                    task.SERIAL_NUMBER,
                    task.MODEL_NAME,
                    task.TEST_CODE,
                    task.MO_NUMBER,
                    task.TEST_GROUP,
                    task.DATA18,
                    ErrorDesc = err != null ? err.ERROR_DESC : string.Empty,
                    AgingDays = (now - task.DATE3.Value).TotalDays
                }
            ).ToListAsync();

            var lessThanOne = data.Where(d => d.AgingDays < 1).ToList();
            var oneToThree = data.Where(d => d.AgingDays >= 1 && d.AgingDays <= 3).ToList();
            var moreThanThree = data.Where(d => d.AgingDays > 3).ToList();

            return Ok(new
            {
                success = true,
                data = new
                {
                    LessThanOneDay = lessThanOne.Count,
                    OneToThreeDays = oneToThree.Count,
                    MoreThanThreeDays = moreThanThree.Count
                },
                details = new
                {
                    LessThanOneDay = lessThanOne.Select(x => new
                    {
                        SerialNumber = x.SERIAL_NUMBER,
                        TestCode = x.TEST_CODE,
                        TestGroup = x.TEST_GROUP,
                        ErrorDesc = x.ErrorDesc,
                        MONumber = x.MO_NUMBER,
                        ModelName = x.MODEL_NAME,
                        Aging = Math.Round(x.AgingDays, 2),
                        Location = x.DATA18
                    }).ToList(),
                    OneToThreeDays = oneToThree.Select(x => new
                    {
                        SerialNumber = x.SERIAL_NUMBER,
                        TestCode = x.TEST_CODE,
                        TestGroup = x.TEST_GROUP,
                        ErrorDesc = x.ErrorDesc,
                        MONumber = x.MO_NUMBER,
                        ModelName = x.MODEL_NAME,
                        Aging = Math.Round(x.AgingDays, 2),
                        Location = x.DATA18
                    }).ToList(),
                    MoreThanThreeDays = moreThanThree.Select(x => new
                    {
                        SerialNumber = x.SERIAL_NUMBER,
                        TestCode = x.TEST_CODE,
                        TestGroup = x.TEST_GROUP,
                        ErrorDesc = x.ErrorDesc,
                        MONumber = x.MO_NUMBER,
                        ModelName = x.MODEL_NAME,
                        Aging = Math.Round(x.AgingDays, 2),
                        Location = x.DATA18
                    }).ToList()
                }
            });
        }

    }
    public class LocationDto
    {
        public string Location { get; set; }
        public int TotalCount { get; set; }
        public object Details { get; set; }
        public bool IsUpdated { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
    }

}
