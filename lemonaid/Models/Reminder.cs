﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace lemonaid.Models {

    public class Reminder {

        public ulong GuildID { get; set; }

        public ulong ChannelID { get; set; }

        public ulong MessageID { get; set; }

        public ulong TargetUserID { get; set; }

        /// <summary>
        ///     when this reminder was created
        /// </summary>
        public DateTimeOffset Timestamp { get; set; }

        /// <summary>
        ///     the reminder will be sent at some point after this time
        /// </summary>
        public DateTimeOffset SendAfter { get; set; }

        /// <summary>
        ///     was this reminder sent?
        /// </summary>
        public bool Sent { get; set; }

        /// <summary>
        ///     will a DM be sent as a reminder instead of a ping?
        /// </summary>
        public bool SendDM { get; set; } = false;

    }
}