using API_WEB.ModelsDB;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace API_WEB.Services.Authentication
{
    public class TokenService
    {
        private readonly IConfiguration _cfg;
        public TokenService(IConfiguration cfg) => _cfg = cfg;
        public string CreateAccessToken(User user)
        {
            var jwt = _cfg.GetSection("Jwt");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            // Tùy bạn: đưa Role, Department, AllowedAreas vào claims
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(ClaimTypes.Name, user.Username ?? string.Empty),
                new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
                new(ClaimTypes.Role, user.Role ?? string.Empty),
                new("fullName", user.FullName ?? string.Empty),
                new("department", user.Department ?? string.Empty),
                new("areas", user.AllowedAreas ?? string.Empty),
            };

            var token = new JwtSecurityToken(
                issuer: jwt["Issuer"],
                audience: jwt["Audience"],
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.AddMinutes(int.Parse(jwt["AccessTokenMinutes"] ?? "60")),
                signingCredentials: creds
            );
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
