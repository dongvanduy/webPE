using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using API_WEB.ModelsOracle;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace API_WEB.Controllers.App
{
    [Route("[controller]")]
    [ApiController]
    public class SopController : ControllerBase
    {
        private readonly OracleDbContext _oracleContext;
        private readonly ILogger<SopController> _logger;
        private readonly string _sopRootPath;

        public SopController(OracleDbContext oracleContext, IConfiguration configuration, ILogger<SopController> logger)
        {
            _oracleContext = oracleContext;
            _logger = logger;
            _sopRootPath = configuration.GetValue<string>("SopSettings:RootPath")?.Trim() ?? Path.Combine("E:", "SOP");
        }

        [HttpGet("{serialNumber}/{stationName}")]
        public async Task<IActionResult> GetSopBySerialAndStation(string serialNumber, string stationName)
        {
            if (string.IsNullOrWhiteSpace(serialNumber) || string.IsNullOrWhiteSpace(stationName))
                return BadRequest(new { message = "Serial number và station name là bắt buộc." });

            var normalizedSerial = serialNumber.Trim().ToUpperInvariant();
            var normalizedStation = stationName.Trim().ToUpperInvariant();

            var modelName = await _oracleContext.OracleDataR107
                .AsNoTracking()
                .Where(r => r.SERIAL_NUMBER == normalizedSerial)
                .Select(r => r.MODEL_NAME)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(modelName))
                return NotFound(new { message = $"Không tìm thấy model cho serial {normalizedSerial}." });

            var pdfPath = ResolveSopPath(modelName.Trim(), normalizedStation);
            if (pdfPath is null)
                return NotFound(new { message = $"Không tìm thấy SOP cho model {modelName} tại station {stationName}." });

            try
            {
                var stream = new FileStream(pdfPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                var result = File(stream, "application/pdf", Path.GetFileName(pdfPath));
                result.EnableRangeProcessing = true;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không thể đọc SOP {Path}", pdfPath);
                return StatusCode(500, new { message = "Không thể đọc file SOP." });
            }
        }

        private string? ResolveSopPath(string modelName, string stationName)
        {
            try
            {
                var modelFolder = Path.Combine(_sopRootPath, SanitizeFileName(modelName));
                if (!Directory.Exists(modelFolder))
                {
                    _logger.LogWarning("Không tìm thấy thư mục model: {Folder}", modelFolder);
                    return null;
                }

                //Đọc mapping.json (nếu có)
                var mappingPath = Path.Combine(modelFolder, "mapping.json");
                if (System.IO.File.Exists(mappingPath))
                {
                    try
                    {
                        var json = System.IO.File.ReadAllText(mappingPath);
                        var mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (mapping != null && mapping.TryGetValue(stationName, out var mappedFile))
                        {
                            var mappedPath = Path.Combine(modelFolder, mappedFile);
                            if (System.IO.File.Exists(mappedPath))
                                return mappedPath;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Không thể đọc mapping.json trong {ModelFolder}", modelFolder);
                    }
                }

                // Tìm trực tiếp theo stationName
                var directCandidates = new[]
                {
                    Path.Combine(modelFolder, $"{stationName}.pdf"),
                    Path.Combine(modelFolder, $"{stationName.ToUpperInvariant()}.pdf"),
                    Path.Combine(modelFolder, $"{stationName.ToLowerInvariant()}.pdf")
                };

                foreach (var file in directCandidates)
                {
                    if (System.IO.File.Exists(file))
                        return file;
                }

                //Fallback: file mặc định
                var defaultPath = Path.Combine(modelFolder, "default.pdf");
                if (System.IO.File.Exists(defaultPath))
                    return defaultPath;

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tìm SOP cho model {ModelName}, station {Station}", modelName, stationName);
                return null;
            }
        }

        private static string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return new string(fileName.Where(ch => !invalidChars.Contains(ch)).ToArray());
        }
    }
}
