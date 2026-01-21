namespace API_WEB.Dtos.Auth
{
    public class RegisterDto
    {
        public string Username { get; set; } = null!;
        public string Password { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Department { get; set; } = null!;
    }
}
