using System.Collections.Generic;

namespace API_WEB.Dtos.DPU
{
    public class AddDpuManagerRequest
    {
        public List<string> SerialNumbers { get; set; } = new();
        public string HbMb { get; set; } = string.Empty;
        public string TypeBonepile { get; set; } = string.Empty;
    }
}
