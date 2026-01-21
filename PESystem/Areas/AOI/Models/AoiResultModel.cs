using Newtonsoft.Json;
using System.Collections.Generic;

namespace PESystem.Areas.AOI.Models
{
    public class AoiResponseDto
    {
        [JsonProperty("detections")]
        public List<AoiDetectionDto> Detections { get; set; }

        [JsonProperty("image_base64")]
        public string ImageBase64 { get; set; }
    }

    public class AoiDetectionDto
    {
        [JsonProperty("x")]
        public float X { get; set; }

        [JsonProperty("y")]
        public float Y { get; set; }

        [JsonProperty("w")]
        public float W { get; set; }

        [JsonProperty("h")]
        public float H { get; set; }

        [JsonProperty("conf")]
        public float Conf { get; set; }

        [JsonProperty("class_id")]
        public int Class_Id { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; } // Đây sẽ chứa text OCR (ví dụ "C102")

        // Helper property để hiển thị màu
        public string Color
        {
            get
            {
                // Class 2 (Text) màu Xanh lá, Component màu Xanh dương, Polarity màu Đỏ
                if (Class_Id == 2) return "green";
                if (Class_Id == 0) return "blue";
                return "red";
            }
        }
    }
}