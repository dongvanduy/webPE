using System;

namespace API_WEB.Dtos.PdRepositorys
{
    public class PdStockOracleDto
    {
        public string? SerialNumber { get; set; }
        public string? ModelName { get; set; }
        public string? CartonNo { get; set; }
        public string? LocationStock { get; set; }
        public DateTime? EntryDate { get; set; }
        public string? EntryOp { get; set; }
        public string? WipGroup { get; set; }
        public string? ErrorFlag { get; set; }
        public string? MoNumber { get; set; }
        public string? ProductLine { get; set; }
    }
}
