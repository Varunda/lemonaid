using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using lemonaid.Code.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace lemonaid.Discord {

    public class ReminderSlashCommand {

        public ILogger<ReminderSlashCommand> _Logger { set; private get; } = default!;

        public async Task RemindHere(InteractionContext ctx,
            [Option("Ping here?", "Will reminders about these messages be a ping in this channel, or in a DM?")] bool ping
        ) {

            await ctx.CreateDeferred(ephemeral: true);

            DiscordWebhookBuilder interactionBuilder = new();
            DiscordEmbedBuilder builder = new();

            builder.Title = $"Reminders setup";
            builder.Description = $"Whenever a message is sent from this account in this channel, a reminder will be created";
            builder.Timestamp = DateTimeOffset.UtcNow;
            builder.Color = DiscordColor.Green;
            interactionBuilder.AddEmbed(builder);

            await ctx.EditResponseAsync(interactionBuilder);
        }

    }
}
