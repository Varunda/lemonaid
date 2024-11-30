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

        /// <summary>
        ///     when a reminder is triggered by the target user's message,
        ///     how many seconds to delay until sending that reminder?
        /// </summary>
        public int SelfReminderDelaySeconds { get; set; } = 60 * 60 * 3;

        /// <summary>
        ///     when a reminder is triggered by NOT the target user's message,
        ///     how many seconds to delay until sending that reminder?
        /// </summary>
        public int OtherReminderDelaySeconds { get; set; } = 60 * 10;

        /// <summary>
        ///     if a reminder is snoozed, how many seconds to wait before re-sending the reminder?
        /// </summary>
        public int SnoozeDelaySeconds { get; set; } = 60 * 60 * 1;

    }
}
