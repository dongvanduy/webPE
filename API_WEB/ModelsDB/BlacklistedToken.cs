namespace API_WEB.ModelsDB
{
    public class BlacklistedToken
    {
        public int Id { get; set; }
        public string Jti { get; set; } = default!;
        public DateTime ExpiresAt { get; set; } // = exp của access token bị block
    }
}
