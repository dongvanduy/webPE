namespace API_WEB.ModelsDB
{
    public class RefreshToken
    {
        public Guid Id { get; set; }
        public int UserId { get; set; }
        public string Token { get; set; } = default!;
        public DateTime ExpiresAt { get; set; }    // ví dụ: +14 ngày
        public DateTime CreatedAt { get; set; }
        public bool IsRevoked { get; set; }
        public DateTime? RevokedAt { get; set; }
        public string? ReplacedByToken { get; set; } // dùng khi rotate
    }

}
