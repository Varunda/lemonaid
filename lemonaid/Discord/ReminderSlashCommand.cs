using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using lemonaid.Code.Extensions;
using lemonaid.Models;
using lemonaid.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace lemonaid.Discord {

    public class ReminderSlashCommand : ApplicationCommandModule {

        public ILogger<ReminderSlashCommand> _Logger { set; private get; } = default!;

        public ReminderRepository _ReminderRepository { set; private get; } = default!;

        [SlashCommand("remind-print", "print pending reminders")]
        public async Task RemindHere(InteractionContext ctx) {

            await ctx.CreateDeferred(ephemeral: true);

            DiscordWebhookBuilder interactionBuilder = new();
            DiscordEmbedBuilder builder = new();

            List<Reminder> reminders = await _ReminderRepository.GetAll();
            builder.Title = $"Reminders pending ({reminders.Count})";
            builder.Description = "";

            foreach (Reminder r in reminders) {
                builder.Description += $"guild:{r.GuildID} <#{r.ChannelID}> <@{r.TargetUserID}>: {r.SendAfter:u}\n";
            }

            builder.Timestamp = DateTimeOffset.UtcNow;
            builder.Color = DiscordColor.Green;
            interactionBuilder.AddEmbed(builder);

            await ctx.EditResponseAsync(interactionBuilder);
        }

    }
}
