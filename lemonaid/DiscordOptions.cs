using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace lemonaid {

    public class DiscordOptions {

        /// <summary>
        ///     What channel Spark will send messages to
        /// </summary>
        public List<ulong> ChannelIds { get; set; } = [];

        /// <summary>
        ///     What Guild Spark is at "home" in
        /// </summary>
        public ulong GuildId { get; set; }

        /// <summary>
        ///     what user will be pinged
        /// </summary>
        public ulong TargetUserId { get; set; }

        /// <summary>
        ///     Client key
        /// </summary>
        public string Token { get; set; } = "aaa";

        public int ReminderDelaySeconds { get; set; } = 60 * 60 * 3;

        public int SnoozeDelaySeconds { get; set; } = 60 * 60 * 1;

    }
}
