using System.ComponentModel.DataAnnotations.Schema;

namespace API_WEB.ModelsDB
{
    [Table("SwitchRepair")]
    public class SwitchRepair
    {
        public int Id { get; set; }
        public string SerialNumber { get; set; } = string.Empty;
        public string? ModelName { get; set; }
        public string? FailStation { get; set; }
        public string? EnterErrorCode { get; set; }
        public string? WipGroup { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorDesc { get; set; }
        public string? Fa { get; set; }
        public string? Status { get; set; }
        public string? Owner { get; set; }
        public string? CustomerOwner { get; set; }
        public DateTime TimeUpdate { get; set; }
    }
}
