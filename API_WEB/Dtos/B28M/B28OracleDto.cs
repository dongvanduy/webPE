namespace API_WEB.Dtos.B28M
{
    public class B28OracleDto
    {
        public string? SerialNumber { get; set; }
        public string? ModelName { get; set; }
        public string? Location { get; set; }
        public DateTime? InDate { get; set; }
        public string? InBy { get; set; }
        public string? Status { get; set; }
        public DateTime? BorrowTime { get; set; }
        public string? Borrower { get; set; }
        public string? WipGroup { get; set; }
        public string? ErrorFlag { get; set; }
        public string? MoNumber { get; set; }
        public string? ProductLine { get; set; }
    }
}
