using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using lemonaid.Code.Extensions;
using DSharpPlus.Exceptions;
using lemonaid.Models;
using Microsoft.Extensions.FileProviders;

namespace lemonaid.Services {

    public class DiscordService : BackgroundService {

        private readonly ILogger<DiscordService> _Logger;

        private readonly DiscordWrapper _Discord;
        private readonly PluralKitApi _pkApi;
        private IOptions<DiscordOptions> _DiscordOptions;

        private bool _IsConnected = false;
        private const string SERVICE_NAME = "discord";

        private Dictionary<ulong, ulong> _CachedMembership = new();

        private readonly TimeSpan REMINDER_DELAY = TimeSpan.FromSeconds(5);
        private readonly TimeSpan SNOOZE_DELAY = TimeSpan.FromSeconds(2);

        private readonly TimeSpan DELETE_PING_DELAY = TimeSpan.FromSeconds(5);

        private const ulong PK_APP_ID = 466378653216014359;

        private Dictionary<string, Reminder> _Reminders = new();

        private ulong _ChannelId {
            get { return _DiscordOptions.Value.ChannelId; }
        }

        private ulong _GuildId {
            get { return _DiscordOptions.Value.GuildId; }
        }

        public DiscordService(ILogger<DiscordService> logger, ILoggerFactory loggerFactory,
            IOptions<DiscordOptions> discordOptions, IServiceProvider services,
            PluralKitApi pkApi, DiscordWrapper discord) {

            _Logger = logger;

            _DiscordOptions = discordOptions;

            _Discord = discord;
            _pkApi = pkApi;

            _Discord.Get().Ready += Client_Ready;
            _Discord.Get().InteractionCreated += Generic_Interaction_Created;
            _Discord.Get().ContextMenuInteractionCreated += Generic_Interaction_Created;
            _Discord.Get().GuildAvailable += Guild_Available;
            _Discord.Get().MessageCreated += Message_Created;
            _Discord.Get().MessageReactionAdded += Reaction_Added;
            _Discord.Get().MessageDeleted += Message_Deleted;
            _Discord.Get().ComponentInteractionCreated += ComponentInteraction_Created;
        }

        public async override Task StartAsync(CancellationToken cancellationToken) {
            try {
                await _Discord.Get().ConnectAsync();
                await base.StartAsync(cancellationToken);
            } catch (Exception ex) {
                _Logger.LogError(ex, "Error in start up of DiscordService");
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            _Logger.LogInformation($"Started {SERVICE_NAME}");

            while (stoppingToken.IsCancellationRequested == false) {
                try {
                    await Task.Delay(5000, stoppingToken);

                    foreach (KeyValuePair<string, Reminder> iter in _Reminders) {
                        Reminder reminder = iter.Value;

                        if (DateTimeOffset.UtcNow < reminder.SendAfter || reminder.Sent == true) {
                            //_Logger.LogInformation($"not sending reminder: not enough time passed [send after={reminder.SendAfter:u}]");
                            continue;
                        }

                        _Logger.LogInformation($"sending reminder [message ID={reminder.MessageID}]");
                        reminder.Sent = true;
                        await Ping(reminder);
                    }
                } catch (Exception ex) when (stoppingToken.IsCancellationRequested == false) {
                    _Logger.LogError(ex, "error sending message");
                } catch (Exception) when (stoppingToken.IsCancellationRequested == true) {
                    _Logger.LogInformation($"stopping {SERVICE_NAME}");
                    break;
                }
            }
        }

        private async Task Message_Deleted(DiscordClient sender, MessageDeleteEventArgs args) {
            ulong? guildID = null;
            if (args.Guild == null) {
                guildID = args.Channel.GuildId;
            }
            if (args.Channel == null) {
                _Logger.LogWarning("channel is null?");
                return;
            }
            if (args.Message.Author == null) {
                _Logger.LogWarning($"author is null");
                return;
            }

            string key = $"{guildID}.{args.Channel.Id}.{args.Message.Author.Id}";
            if (_Reminders.ContainsKey(key)) {
                _Logger.LogInformation($"removed reminder [key={key}]");
            }
            _Reminders.Remove(key);
        }

        private async Task Message_Created(DiscordClient sender, MessageCreateEventArgs args) {
            if (args.Channel.Id != _ChannelId) {
                return;
            }
            if (args.Guild?.Id != _GuildId) {
                return;
            }
            if (args.Author.Id == sender.CurrentUser.Id) {
                return;
            }

            Reminder r = new();
            r.MessageID = args.Message.Id;
            r.GuildID = args.Guild.Id;
            r.TargetUserID = args.Author.Id;
            r.ChannelID = args.Channel.Id;
            r.Timestamp = args.Message.Timestamp;
            r.SendAfter = args.Message.Timestamp + REMINDER_DELAY;

            if (args.Message.ApplicationId == PK_APP_ID) {
                _Logger.LogInformation($"pk message sent");
                PkMessage? msg = await _pkApi.GetMessage(args.Message.Id);
                if (msg == null) {
                    _Logger.LogWarning($"missing pk message [msg id={args.Message.Id}]");
                } else {
                    r.TargetUserID = msg.SenderMessageID;
                }
            }

            string key = $"{r.GuildID}.{r.ChannelID}.{r.TargetUserID}";

            if (_Reminders.ContainsKey(key)) {
                _Logger.LogInformation($"pushing reminder back [key={key}] [send after={r.SendAfter:u}]");
            } else {
                _Logger.LogInformation($"reminder added [author={args.Author.Id}/{args.Author.Username}] [timestamp={args.Message.Timestamp:u}] [send after={r.SendAfter:u}]");
            }
            _Reminders[key] = r;

            return;
        }

        private async Task Reaction_Added(DiscordClient sender, MessageReactionAddEventArgs args) {
            _Logger.LogInformation($"reaction added [sender={args.User.Id}] [emoji={args.Emoji.Name}/{args.Emoji.GetDiscordName()}]");
            if (args.User.Id == sender.CurrentUser.Id) {
                return;
            }

            if (args.Message == null || args.Message.Author == null || args.Message.Author.Id != sender.CurrentUser.Id) {
                return;
            }

            if (args.Emoji.GetDiscordName() != ":x:") {
                return;
            }

            await args.Message.DeleteAsync();
        }

        private async Task ComponentInteraction_Created(DiscordClient client, ComponentInteractionCreateEventArgs args) {
            _Logger.LogInformation($"comp interaction! [id={args.Id}]");

            await args.Interaction.CreateDeferred(true);

            string[] parts = args.Id.Split(".");
            if (parts.Length < 1) {
                throw new Exception($"failed to split {args.Id} into an array of at least 1 length");
            }

            if (parts[0] == "@snooze" || parts[0] == "@remove") {
                if (parts.Length < 2) {
                    throw new Exception($"failed to split {args.Id} into an array of at least length 2");
                }

                string key = $"{args.Guild.Id}.{args.Channel.Id}.{parts[1]}";

                if (_Reminders.ContainsKey(key)) {
                    if (parts[0] == "@snooze") {
                        _Reminders[key].SendAfter = DateTimeOffset.UtcNow + SNOOZE_DELAY;
                        _Reminders[key].Sent = false;
                        _Logger.LogInformation($"snoozing reminder for 1h [key={key}]");
                    } else if (parts[0] == "@remove") {
                        _Reminders.Remove(key);
                        _Logger.LogInformation($"removing reminder [key={key}]");
                    } else {
                        _Logger.LogWarning($"unchecked command [command={parts[0]}]");
                        return;
                    }
                } else {
                    _Logger.LogWarning($"failed to find key [key={key}]");
                }

                try {
                    await args.Message.DeleteAsync();
                } catch (Exception ex) {
                    _Logger.LogError($"failed to delete reminder message {args.Message.Id}", ex);
                }
                await args.Interaction.DeleteOriginalResponseAsync();
            }
        }

        private async Task Ping(Reminder reminder) {
            DiscordGuild? guild = await _Discord.Get().TryGetGuild(reminder.GuildID);
            if (guild == null) {
                _Logger.LogError($"failed to send ping: failed to find guild [guildID={reminder.GuildID}]");
                return;
            }

            DiscordChannel? channel = guild.TryGetChannel(reminder.ChannelID);
            if (channel == null) {
                _Logger.LogError($"failed to send ping: failed to find channel [channelID={reminder.ChannelID}]");
                return;
            }

            DiscordMessageBuilder builder = new();
            builder.WithReply(reminder.MessageID, true);
            builder.AddComponents(
                new DiscordButtonComponent(ButtonStyle.Primary, $"@snooze.{reminder.TargetUserID}", "Snooze (1h)"),
                new DiscordButtonComponent(ButtonStyle.Danger, $"@remove.{reminder.TargetUserID}", "Remove")
            );

            DiscordEmbedBuilder embed = new();
            embed.Title = "reminder!";
            embed.Color = DiscordColor.Gold;
            embed.Description = $"This is a reminder for <@{reminder.TargetUserID}>!";

            builder.AddEmbed(embed);
            builder.AddMention(new UserMention(reminder.TargetUserID));

            DiscordMessage msg = await _Discord.Get().SendMessageAsync(channel, builder);
            //await msg.CreateReactionAsync(DiscordEmoji.FromName(_Discord.Get(), ":x:"));
            await Task.Delay(DELETE_PING_DELAY);
            try {
                //await msg.DeleteAsync();
            } catch (NotFoundException) {
                _Logger.LogInformation($"message was deleted before bot deleted it");
            }
        }

        /// <summary>
        ///     Get a <see cref="DiscordMember"/> from an ID
        /// </summary>
        /// <param name="memberID">ID of the Discord member to get</param>
        /// <returns>
        ///     The <see cref="DiscordMember"/> with the corresponding ID, or <c>null</c>
        ///     if the user could not be found in any guild the bot is a part of
        /// </returns>
        private async Task<DiscordMember?> GetDiscordMember(ulong memberID) {
            // check if cached
            if (_CachedMembership.TryGetValue(memberID, out ulong guildID) == true) {
                DiscordGuild? guild = await _Discord.Get().TryGetGuild(guildID);
                if (guild == null) {
                    _Logger.LogWarning($"Failed to get guild {guildID} from cached membership for member {memberID}");
                } else {
                    DiscordMember? member = await guild.TryGetMember(memberID);
                    // if the member is null, and was cached, then cache is bad
                    if (member == null) {
                        _Logger.LogWarning($"Failed to get member {memberID} from guild {guildID}");
                        _CachedMembership.Remove(memberID);
                    } else {
                        _Logger.LogDebug($"Found member {memberID} from guild {guildID} (cached)");
                        return member;
                    }
                }
            }

            // check each guild and see if it contains the target member
            foreach (KeyValuePair<ulong, DiscordGuild> entry in _Discord.Get().Guilds) {
                DiscordMember? member = await entry.Value.TryGetMember(memberID);

                if (member != null) {
                    _Logger.LogDebug($"Found member {memberID} from guild {entry.Value.Id}");
                    _CachedMembership[memberID] = entry.Value.Id;
                    return member;
                }
            }

            _Logger.LogWarning($"Cannot get member {memberID}, not cached and not in any guilds");

            return null;
        }

        /// <summary>
        ///     Event handler for when the client is ready
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private async Task Client_Ready(DiscordClient sender, ReadyEventArgs args) {
            _Logger.LogInformation($"Discord client connected");

            _IsConnected = true;
        }

        private Task Guild_Available(DiscordClient sender, GuildCreateEventArgs args) {
            DiscordGuild? guild = args.Guild;
            if (guild == null) {
                _Logger.LogDebug($"no guild");
                return Task.CompletedTask;
            }

            _Logger.LogDebug($"guild available: {guild.Id} / {guild.Name}");
            return Task.CompletedTask;
        }

        /// <summary>
        ///     Event handler for both types of interaction (slash commands and context menu)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private Task Generic_Interaction_Created(DiscordClient sender, InteractionCreateEventArgs args) {
            DiscordInteraction interaction = args.Interaction;
            string user = interaction.User.GetDisplay();

            string interactionMethod = "slash";

            DiscordUser? targetMember = null;
            DiscordMessage? targetMessage = null;

            if (args is ContextMenuInteractionCreateEventArgs contextArgs) {
                targetMember = contextArgs.TargetUser;
                targetMessage = contextArgs.TargetMessage;
                interactionMethod = "context menu";
            }

            string feedback = $"{user} used '{interaction.Data.Name}' (a {interaction.Type}) as a {interactionMethod}: ";

            if (targetMember != null) {
                feedback += $"[target member: (user) {targetMember.GetDisplay()}]";
            }
            if (targetMessage != null) {
                feedback += $"[target message: (channel) {targetMessage.Id}] [author: (user) {targetMessage.Author.GetDisplay()}]";
            }

            if (targetMessage == null && targetMember == null) {
                feedback += $"{interaction.Data.Name} {GetCommandString(interaction.Data.Options)}";
            }

            _Logger.LogDebug(feedback);

            return Task.CompletedTask;
        }

        /// <summary>
        ///     Transform the options used in an interaction into a string that can be viewed
        /// </summary>
        /// <param name="options"></param>
        private string GetCommandString(IEnumerable<DiscordInteractionDataOption>? options) {
            if (options == null) {
                options = new List<DiscordInteractionDataOption>();
            }

            string s = "";

            foreach (DiscordInteractionDataOption opt in options) {
                s += $"[{opt.Name}=";

                if (opt.Type == ApplicationCommandOptionType.Attachment) {
                    s += $"(Attachment)";
                } else if (opt.Type == ApplicationCommandOptionType.Boolean) {
                    s += $"(bool) {opt.Value}";
                } else if (opt.Type == ApplicationCommandOptionType.Channel) {
                    s += $"(channel) {opt.Value}";
                } else if (opt.Type == ApplicationCommandOptionType.Integer) {
                    s += $"(int) {opt.Value}";
                } else if (opt.Type == ApplicationCommandOptionType.Mentionable) {
                    s += $"(mentionable) {opt.Value}";
                } else if (opt.Type == ApplicationCommandOptionType.Number) {
                    s += $"(number) {opt.Value}";
                } else if (opt.Type == ApplicationCommandOptionType.Role) {
                    s += $"(role) {opt.Value}";
                } else if (opt.Type == ApplicationCommandOptionType.String) {
                    s += $"(string) '{opt.Value}'";
                } else if (opt.Type == ApplicationCommandOptionType.SubCommand) {
                    s += GetCommandString(opt.Options);
                } else if (opt.Type == ApplicationCommandOptionType.SubCommandGroup) {
                    s += GetCommandString(opt.Options);
                } else if (opt.Type == ApplicationCommandOptionType.User) {
                    s += $"(user) {opt.Value}";
                } else {
                    _Logger.LogError($"Unchecked {nameof(DiscordInteractionDataOption)}.{nameof(DiscordInteractionDataOption.Type)}: {opt.Type}, value={opt.Value}");
                    s += $"[{opt.Name}=(UNKNOWN {opt.Type}) {opt.Value}]";
                }

                s += "]";
            }

            return s;
        }

    }
}
