using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PESystem.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;

namespace PESystem.Controllers
{
    public class AuthController : Controller
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IHttpClientFactory _clientFactory;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            IHttpClientFactory clientFactory,
            ILogger<AuthController> logger)
        {
            _clientFactory = clientFactory;
            _logger = logger;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Home");  // đã đăng nhập thì về Home
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Login(LoginModel model, string? returnUrl = null)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var client = _clientFactory.CreateClient("ApiClient");
            HttpResponseMessage response;

            try
            {
                response = await client.PostAsJsonAsync("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Auth/login", new LoginRequest(model.Username!, model.Password!));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to call login API");
                ModelState.AddModelError(string.Empty, "Không thể kết nối đến máy chủ.");
                return View(model);
            }

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    ModelState.AddModelError(string.Empty, "Username hoặc Password không đúng.");
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "Đăng nhập thất bại.");
                }
                return View(model);
            }

            LoginResponse? loginResponse = null;
            try
            {
                loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse login response");
            }

            if (loginResponse?.User == null || string.IsNullOrWhiteSpace(loginResponse.AccessToken))
            {
                ModelState.AddModelError(string.Empty, "Đăng nhập thất bại.");
                return View(model);
            }

            var apiUser = loginResponse.User;
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, apiUser.UserId.ToString()),
                new Claim(ClaimTypes.Name, apiUser.Username ?? string.Empty),
                new Claim(ClaimTypes.Role, apiUser.Role ?? string.Empty),
                new Claim("AllowedAreas", apiUser.AllowedAreas ?? string.Empty),
                new Claim("FullName", apiUser.FullName ?? string.Empty),
                new Claim("Email", apiUser.Email ?? string.Empty),
                new Claim("Department", apiUser.Department ?? string.Empty),
                new Claim("AccessToken", loginResponse.AccessToken)
            };

            if (!string.IsNullOrWhiteSpace(loginResponse.RefreshToken))
            {
                claims.Add(new Claim("RefreshToken", loginResponse.RefreshToken));
            }

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProps = new AuthenticationProperties { IsPersistent = model.RememberMe };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                authProps);

            return RedirectToAction("Home", "Home");
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Register(RegisterModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var client = _clientFactory.CreateClient("ApiClient");
            HttpResponseMessage response;

            try
            {
                response = await client.PostAsJsonAsync(
                    "https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Auth/register",
                    new RegisterRequest(
                        model.Username!,
                        model.Password!,
                        model.FullName!,
                        model.Email!,
                        model.Department!));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to call register API");
                ModelState.AddModelError(string.Empty, "Không thể kết nối đến máy chủ.");
                return View(model);
            }

            if (response.IsSuccessStatusCode)
            {
                return RedirectToAction(nameof(Login));
            }

            var error = await response.Content.ReadAsStringAsync();
            ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(error) ? "Đăng ký thất bại." : error);
            return View(model);
        }

        [HttpGet]
        [Authorize]
        public IActionResult ChangePassword()
        {
            return View();
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> ChangePassword(ChangePasswordModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var client = CreateClientWithToken(out var hasToken);
            if (!hasToken)
            {
                await SignOutCurrentUserAsync();
                return RedirectToAction(nameof(Login));
            }

            HttpResponseMessage response;
            try
            {
                response = await client.PostAsJsonAsync("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Auth/change-password", new ChangePasswordRequest(model.OldPassword, model.NewPassword));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to call change password API");
                ModelState.AddModelError(string.Empty, "Không thể kết nối đến máy chủ.");
                return View(model);
            }

            if (response.IsSuccessStatusCode)
            {
                ModelState.Clear();
                ViewBag.Message = "Password changed";
                return View(new ChangePasswordModel());
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await SignOutCurrentUserAsync();
                return RedirectToAction(nameof(Login));
            }

            var error = await response.Content.ReadAsStringAsync();
            ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(error) ? "Failed to change password" : error);
            return View(model);
        }


        [HttpGet]
        [AllowAnonymous]
        public IActionResult Reset()
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Home");  // đã đăng nhập thì về Home
            return View(new ResetFlowModel());
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Reset(ResetFlowModel model, string action)
        {
            var client = _clientFactory.CreateClient("ApiClient");

            if (action == "send")
            {
                // gọi API forgot-password
                var res = await client.PostAsJsonAsync("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Auth/forgot-password", new { email = model.Email });
                if (res.IsSuccessStatusCode)
                {
                    ModelState.Clear();
                    model.OtpSent = true;
                    model.Message = "OTP đã gửi, vui lòng kiểm tra email.";
                }
                else
                {
                    model.Message = "Không thể gửi OTP.";
                }
                return View(model);
            }

            if (action == "reset")
            {
                if (!ModelState.IsValid)
                {
                    model.OtpSent = true;
                    return View(model);
                }

                var res = await client.PostAsJsonAsync("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Auth/reset-password",
                    new { email = model.Email, otp = model.Otp, newPassword = model.NewPassword });

                if (res.IsSuccessStatusCode)
                {
                    TempData["Message"] = "Đặt lại mật khẩu thành công. Đăng nhập lại.";
                    return RedirectToAction("Login");
                }

                model.OtpSent = true;
                model.Message = "Đặt lại mật khẩu thất bại.";
                return View(model);
            }

            model.Message = "Yêu cầu không hợp lệ.";
            return View(model);
        }


        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            var client = CreateClientWithToken(out var hasToken);
            var refreshToken = User.FindFirst("RefreshToken")?.Value;

            if (hasToken && !string.IsNullOrWhiteSpace(refreshToken))
            {
                try
                {
                    await client.PostAsJsonAsync("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Auth/logout", new RefreshRequest(refreshToken));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to call logout API");
                }
            }

            await SignOutCurrentUserAsync();
            return RedirectToAction(nameof(Login));
        }

        private HttpClient CreateClientWithToken(out bool hasToken)
        {
            var client = _clientFactory.CreateClient("ApiClient");
            var accessToken = User.FindFirst("AccessToken")?.Value;

            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                hasToken = true;
            }
            else
            {
                hasToken = false;
            }

            return client;
        }

        private async Task SignOutCurrentUserAsync()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            Response.Cookies.Delete(".AspNetCore.Cookies");
        }

        private record LoginRequest(string Username, string Password);

        private record ChangePasswordRequest(string OldPassword, string NewPassword);

        private record RegisterRequest(string Username, string Password, string FullName, string Email, string Department);

        private record ForgotPasswordRequest(string Email);

        private record ResetPasswordRequest(string Email, string Otp, string NewPassword);

        private record RefreshRequest(string RefreshToken);

        private class LoginResponse
        {
            public string TokenType { get; set; } = string.Empty;
            public string AccessToken { get; set; } = string.Empty;
            public string? RefreshToken { get; set; }
            public ApiUser? User { get; set; }
        }

        private class ApiUser
        {
            public int UserId { get; set; }
            public string? Username { get; set; }
            public string? Role { get; set; }
            public string? FullName { get; set; }
            public string? Email { get; set; }
            public string? Department { get; set; }
            public string? AllowedAreas { get; set; }
        }
    }
}
