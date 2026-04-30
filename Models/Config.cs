using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TaskbarOverlay.Models
{
    public class Config
    {
        [JsonPropertyName("layout")]
        public LayoutConfig Layout { get; set; } = new LayoutConfig();

        [JsonPropertyName("groups")]
        public List<GroupConfig> Groups { get; set; } = new List<GroupConfig>();
    }

    public class LayoutConfig
    {
        [JsonPropertyName("left_margin")]
        public double LeftMargin { get; set; } = 200;

        [JsonPropertyName("right_margin")]
        public double RightMargin { get; set; } = 300;

        [JsonPropertyName("height")]
        public double Height { get; set; } = 64;
    }

    public class GroupConfig
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("apps")]
        public List<AppConfig> Apps { get; set; } = new List<AppConfig>();
    }

    public class AppConfig
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;
    }
}
