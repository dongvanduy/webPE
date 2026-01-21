using API_WEB.Dtos.Auth;
using API_WEB.Helpers;
using API_WEB.ModelsDB;
using API_WEB.Services.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;

namespace API_WEB.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly CSDL_NE _context;
        private readonly TokenService _tokenService;
        public record RefreshRequest(string RefreshToken);

        public AuthController(CSDL_NE context, TokenService tokenService)
        {
            _context = context;
            _tokenService = tokenService;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
           if(dto == null || string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password))
           {
                return BadRequest("Invalid payload!");

           }
            var user = await _context.Users.SingleOrDefaultAsync(u => u.Username == dto.Username);
            if (user == null) return Unauthorized("Invalid username or password");
            var hasher = new PasswordHasher<User>();
            var result = hasher.VerifyHashedPassword(user, user.Password, dto.Password);
            if (result != PasswordVerificationResult.Success)
                return Unauthorized("Invalid username or password");

            var accessToken = _tokenService.CreateAccessToken(user);

            // sau khi có accessToken:
            var refresh = new API_WEB.ModelsDB.RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.UserId,
                Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
                ExpiresAt = DateTime.UtcNow.AddDays(14),
                CreatedAt = DateTime.UtcNow,
                IsRevoked = false
            };
            _context.RefreshTokens.Add(refresh);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                tokenType = "Bearer",
                accessToken,
                refreshToken = refresh.Token,
                user = new
                {
                    user.UserId,
                    user.Username,
                    user.FullName,
                    user.Email,
                    user.Role,
                    user.Department,
                    user.AllowedAreas
                }
            });
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            if (dto == null
                || string.IsNullOrWhiteSpace(dto.Username)
                || string.IsNullOrWhiteSpace(dto.Password)
                || string.IsNullOrWhiteSpace(dto.FullName)
                || string.IsNullOrWhiteSpace(dto.Email)
                || string.IsNullOrWhiteSpace(dto.Department))
            {
                return BadRequest("Invalid payload!");
            }

            if (await _context.Users.AnyAsync(u => u.Username == dto.Username))
            {
                return BadRequest("Username already exists.");
            }

            if (await _context.Users.AnyAsync(u => u.Email == dto.Email))
            {
                return BadRequest("Email already exists.");
            }

            var user = new User
            {
                Username = dto.Username,
                FullName = dto.FullName,
                Email = dto.Email,
                Department = dto.Department,
                Role = "User",
                AllowedAreas = string.Empty
            };

            var hasher = new PasswordHasher<User>();
            user.Password = hasher.HashPassword(user, dto.Password);

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                user.UserId,
                user.Username,
                user.FullName,
                user.Email,
                user.Department,
                user.Role,
                user.AllowedAreas
            });
        }

        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            // Lấy username từ claim
            var username = User.Identity?.Name
                ?? User.Claims.FirstOrDefault(c => c.Type == "unique_name" || c.Type == "name" || c.Type == "preferred_username")?.Value;

            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var user = await _context.Users.SingleOrDefaultAsync(u => u.Username == username);
            if (user == null) return NotFound();

            return Ok(new
            {
                user.UserId,
                user.Username,
                user.FullName,
                user.Email,
                user.Role,
                user.Department,
                user.AllowedAreas
            });
        }

        [Authorize] // tuỳ bạn có bắt buộc hay không
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
        {
            var username = User.FindFirstValue(ClaimTypes.Name);
            if (string.IsNullOrWhiteSpace(username)) return Unauthorized();

            var user = await _context.Users.SingleOrDefaultAsync(u => u.Username == username);
            if (user == null) return NotFound("User not found");

            var hasher = new PasswordHasher<User>();
            var result = hasher.VerifyHashedPassword(user, user.Password, dto.OldPassword);
            if (result != PasswordVerificationResult.Success) return BadRequest("Old password incorrect");

            user.Password = hasher.HashPassword(user, dto.NewPassword);
            await _context.SaveChangesAsync();
            return Ok("Password changed");
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user == null) return NotFound("User not found");

            var otp = new Random().Next(100000, 999999).ToString();
            // TTL 5 phút, tối đa 3 lần / 15 phút
            if (!OtpStore.TrySetOtp(user.Email, otp, TimeSpan.FromMinutes(5), 3, TimeSpan.FromMinutes(15), out var error))
            {
                return BadRequest(error);
            }
            await MailHelper.SendEmailAsync(user.Email, "OTP Reset Password", $"Your OTP is: {otp}");
            return Ok("OTP sent");
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user == null) return NotFound("User not found");

            if (!OtpStore.ValidateOtp(user.Email, dto.Otp)) return BadRequest("Invalid OTP");

            var hasher = new PasswordHasher<User>();
            user.Password = hasher.HashPassword(user, dto.NewPassword);
            await _context.SaveChangesAsync();
            OtpStore.RemoveOtp(user.Email);
            return Ok("Password reset successful");
        }

        [AllowAnonymous]
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest dto)
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.RefreshToken))
                return BadRequest("Invalid refresh token");

            var rt = await _context.RefreshTokens.SingleOrDefaultAsync(x => x.Token == dto.RefreshToken);
            if (rt == null || rt.IsRevoked || rt.ExpiresAt <= DateTime.UtcNow)
                return Unauthorized("Refresh token invalid");

            var user = await _context.Users.FindAsync(rt.UserId);
            if (user == null) return Unauthorized("User not found");

            // rotate refresh token (khuyến nghị)
            var newRt = new API_WEB.ModelsDB.RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.UserId,
                Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
                ExpiresAt = DateTime.UtcNow.AddDays(14),
                CreatedAt = DateTime.UtcNow
            };
            rt.IsRevoked = true;
            rt.RevokedAt = DateTime.UtcNow;
            rt.ReplacedByToken = newRt.Token;

            var newAccess = _tokenService.CreateAccessToken(user);

            _context.RefreshTokens.Add(newRt);
            await _context.SaveChangesAsync();

            return Ok(new { tokenType = "Bearer", accessToken = newAccess, refreshToken = newRt.Token });
        }

        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] RefreshRequest dto)
        {
            // Revoke refresh token (nếu client gửi kèm)
            if (!string.IsNullOrWhiteSpace(dto?.RefreshToken))
            {
                var rt = await _context.RefreshTokens.SingleOrDefaultAsync(x => x.Token == dto.RefreshToken);
                if (rt != null && !rt.IsRevoked)
                {
                    rt.IsRevoked = true;
                    rt.RevokedAt = DateTime.UtcNow;
                }
            }

            // Force out access token hiện tại bằng blacklist
            var jti = User.FindFirstValue(JwtRegisteredClaimNames.Jti);
            var expUnix = User.FindFirstValue(JwtRegisteredClaimNames.Exp);
            if (!string.IsNullOrEmpty(jti) && long.TryParse(expUnix, out var exp))
            {
                var expiresAt = DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime;
                _context.BlacklistedTokens.Add(new API_WEB.ModelsDB.BlacklistedToken
                {
                    Jti = jti,
                    ExpiresAt = expiresAt
                });
            }

            await _context.SaveChangesAsync();
            return Ok("Logged out");
        }

    }
}
