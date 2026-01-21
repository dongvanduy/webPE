using System.Collections.Generic;

namespace API_WEB.Models.SmartFA
{
    public class SerialNumberRequest
    {
        public List<string> SerialNumbers { get; set; } = new List<string>();
    }

    public class RepairSuggestionRequest
    {
        public string SerialNumber { get; set; }
    }
}
