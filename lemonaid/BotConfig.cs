using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace lemonaid {

    public class BotConfig {

        public string Identity { get; set; } = "";

        public ulong DefaultChannelId { get; set; }

        public List<ulong> AfkChannelId { get; set; } = new();

        public ulong DiscordVoiceChannelId { get; set; }

        public static BotConfig Load() {
            string cwd = Environment.CurrentDirectory;

            string path = Path.Combine(cwd, ".config.json");

            if (File.Exists(path) == false) {
                return new BotConfig();
            }

            string contents = File.ReadAllText(path);
            JToken j = JToken.Parse(contents);

            BotConfig config = new();
            JToken? ident = j["Identity"];
            if (ident != null) { config.Identity = ident.Value<string>() ?? ""; }

            JToken? defaultChannelId = j["DefaultChannelId"];
            if (defaultChannelId != null) { config.DefaultChannelId = defaultChannelId.Value<ulong>(); }

            JToken? afkChannelId = j["AfkChannelId"];
            if (afkChannelId != null) {
                string channels = afkChannelId.Value<string>() ?? "";
                string[] parts = channels.Split(",");
                foreach (string part in parts) {
                    config.AfkChannelId.Add(ulong.Parse(part));
                }
            }

            JToken? discordVoiceChannelId = j["DiscordVoiceChannelId"];
            if (discordVoiceChannelId != null) {
                config.DiscordVoiceChannelId = discordVoiceChannelId.Value<ulong>();
            }

            return config;
        }

        public static async Task Save(BotConfig config) {
            JObject j = new();

            j["Identity"] = config.Identity;
            j["DefaultChannelId"] = config.DefaultChannelId;
            j["AfkChannelId"] = string.Join(",", config.AfkChannelId);
            j["DiscordVoiceChannelId"] = config.DiscordVoiceChannelId;

            string cwd = Environment.CurrentDirectory;

            string path = Path.Combine(cwd, ".config.json");

            File.WriteAllText(path, j.ToString());
        }


    }
}
