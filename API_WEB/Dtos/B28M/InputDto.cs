namespace API_WEB.Dtos.B28M
{
    public class InputDto
    {
        public string SerialNumber { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string InBy { get; set; } = string.Empty;
        public string InDate { get; set; } = string.Empty;
        public string Status { get; set; } = "Available";

    }
}
