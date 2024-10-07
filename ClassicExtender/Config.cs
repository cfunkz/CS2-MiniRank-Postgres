using CounterStrikeSharp.API.Core;
using System.Text.Json.Serialization;

namespace ClassicExtender
{
    public class ClassicExtenderConfig : BasePluginConfig
    {
        public override int Version { get; set; } = 1;

        [JsonPropertyName("DatabaseHost")]
		public string DatabaseHost { get; set; } = "localhost";

		[JsonPropertyName("DatabasePort")]
		public int DatabasePort { get; set; } = 5432;

        [JsonPropertyName("DatabaseUser")]
        public string DatabaseUser { get; set; } = "postgres";

        [JsonPropertyName("DatabasePassword")]
        public string DatabasePassword { get; set; } = "password";

        [JsonPropertyName("DatabaseName")]
        public string DatabaseName { get; set; } = "database";

        [JsonPropertyName("KillPoints")]
        public int KillPoints { get; set; } = 2;

        [JsonPropertyName("HSPoints")]
        public int HSPoints { get; set; } = 3;

        [JsonPropertyName("NSPoints")]
        public int NSPoints { get; set; } = 4;

        [JsonPropertyName("AssistPoints")]
        public int AssistPoints { get; set; } = 1;

        [JsonPropertyName("DeathPoints")]
        public int DeathPoints { get; set; } = 1;

        [JsonPropertyName("HEPoints")]
        public int HEPoints { get; set; } = 1;

        [JsonPropertyName("INCPoints")]
        public int MOPoints { get; set; } = 1;

    }
}