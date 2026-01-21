using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Text.Json;

namespace PESystem.Areas.AOI.Controllers
{
    [Area("AOI")]
    public class CheckDrawController : Controller
    {
        private readonly IWebHostEnvironment _env;

        public CheckDrawController(IWebHostEnvironment env)
        {
            _env = env;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Analyze(IFormFile file, string zonesData)
        {
            if (file == null || string.IsNullOrEmpty(zonesData))
                return Json(new { success = false, message = "Thiếu file hoặc chưa vẽ vùng zoom." });

            try
            {
                // 1. Lưu file
                string uploadFolder = Path.Combine(_env.WebRootPath, "uploads", "aoi");
                if (!Directory.Exists(uploadFolder)) Directory.CreateDirectory(uploadFolder);

                string uniqueName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
                string filePath = Path.Combine(uploadFolder, uniqueName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // 2. Cấu hình Python (QUAN TRỌNG: Sửa đường dẫn Python.exe)
                string pythonExe = @"C:\Users\ADMIN\AppData\Local\Programs\Python\Python311\python.exe";
                string scriptPath = Path.Combine(_env.ContentRootPath, "PythonScripts", "aoi_engine.py");
                string modelPath = Path.Combine(_env.ContentRootPath, "PythonScripts", "best.pt");

                // Escape JSON
                string escapedZones = zonesData.Replace("\"", "\\\"");

                // 3. Gọi Python
                var start = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{scriptPath}\" --image \"{filePath}\" --outdir \"{uploadFolder}\" --model \"{modelPath}\" --zones \"{escapedZones}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(start))
                {
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();
                    process.WaitForExit();

                    if (!string.IsNullOrEmpty(output))
                    {
                        try
                        {
                            using (JsonDocument doc = JsonDocument.Parse(output))
                            {
                                JsonElement root = doc.RootElement;
                                if (root.TryGetProperty("status", out var s) && s.GetString() == "success")
                                {
                                    string pptxFile = root.GetProperty("pptx_file").GetString();
                                    return Json(new { success = true, downloadUrl = $"/uploads/aoi/{pptxFile}" });
                                }
                                return Json(new { success = false, message = "Lỗi Python: " + (root.TryGetProperty("error", out var err) ? err.GetString() : output) });
                            }
                        }
                        catch
                        {
                            return Json(new { success = false, message = "Lỗi Parse JSON Output: " + output });
                        }
                    }
                    return Json(new { success = false, message = "Lỗi System: " + error });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}