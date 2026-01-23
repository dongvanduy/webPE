using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;

namespace PESystem.Areas.AOI.Controllers
{
    [Area("AOI")]
    public class CheckDrawController : Controller
    {
        private readonly IHttpClientFactory _clientFactory;
        // URL của Python Service chạy offline trên server (dùng IP loopback)
        private const string AI_SERVICE_URL = "http://127.0.0.1:5000";

        public CheckDrawController(IHttpClientFactory clientFactory)
        {
            _clientFactory = clientFactory;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ScanBoard(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { status = "error", message = "Vui lòng chọn file ảnh hoặc PDF." });

            try
            {
                // Tạo Client kết nối
                var client = _clientFactory.CreateClient();
                client.Timeout = TimeSpan.FromMinutes(30); // Tăng timeout vì AI chạy CPU khá lâu

                using (var content = new MultipartFormDataContent())
                {
                    using (var stream = file.OpenReadStream())
                    {
                        var fileContent = new StreamContent(stream);
                        fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
                        content.Add(fileContent, "file", file.FileName);

                        // Gọi sang Python Service API
                        // Lưu ý: Endpoint bên Python phải là /api/scan như đã cấu hình
                        var response = await client.PostAsync($"{AI_SERVICE_URL}/api/scan", content);

                        if (response.IsSuccessStatusCode)
                        {
                            var jsonString = await response.Content.ReadAsStringAsync();
                            // Trả nguyên JSON từ Python về cho Frontend xử lý
                            return Content(jsonString, "application/json");
                        }
                        else
                        {
                            return StatusCode((int)response.StatusCode, new { status = "error", message = "Lỗi từ AI Server: " + response.ReasonPhrase });
                        }
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(500, new { status = "error", message = "Không kết nối được AI Service (Python chưa chạy?). Chi tiết: " + ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = "error", message = "Lỗi hệ thống: " + ex.Message });
            }
        }
    }
}