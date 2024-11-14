using DSharpPlus.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace lemonaid.Code.Extensions {

    public static class DiscordUserExtensions {

        public static string GetPing(this DiscordUser user) {
            return $"<@{user.Id}>";
        }

        public static string GetDisplay(this DiscordUser user) {
            return $"{user.Username}#{user.Discriminator} ({user.Id})";
        }

    }
}
