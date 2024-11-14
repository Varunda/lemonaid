using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace lemonaid.Models {
    public class PkMessage {

        public DateTime Timestamp { get; set; }

        public ulong MessageID { get; set; } 

        public ulong OriginalMessageID { get; set; }

        public ulong SenderMessageID { get; set; }

        public ulong ChannelID { get; set; }

        public ulong GuildID { get; set; }

    }
}
