namespace API_WEB.Dtos.DPU
{
    public class UpdateDpuFieldsRequest
    {
        public List<string> SerialNumbers { get; set; }
        public string? DDRToolResult { get; set; }
        public string? NV_Instruction { get; set; }
        public string? ReworkFXV { get; set; }
        public string? CurrentStatus { get; set; }
        public string? Remark { get; set; }
        public string? Remark2 { get; set; }
    }
}
