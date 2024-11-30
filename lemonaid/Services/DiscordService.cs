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
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.EventArgs;
using lemonaid.Discord;

namespace lemonaid.Services {

    public class DiscordService : BackgroundService {

        private readonly ILogger<DiscordService> _Logger;

        private readonly DiscordWrapper _Discord;
        private readonly PluralKitApi _pkApi;
        private IOptions<DiscordOptions> _DiscordOptions;
        private readonly ReminderRepository _ReminderRepository;

        private bool _IsConnected = false;
        private const string SERVICE_NAME = "discord";

        private Dictionary<ulong, ulong> _CachedMembership = new();

        private readonly TimeSpan SELF_REMINDER_DELAY;
        private readonly TimeSpan OTHER_REMINDER_DELAY;
        private readonly TimeSpan SNOOZE_DELAY;

        /// <summary>
        ///     ID of the pluralkit application
        /// </summary>
        private const ulong PK_APP_ID = 466378653216014359;

        private SlashCommandsExtension _SlashCommands;

        private List<ulong> _ChannelIds {
            get { return _DiscordOptions.Value.ChannelIds; }
        }

        private ulong _GuildId {
            get { return _DiscordOptions.Value.GuildId; }
        }

        private ulong _TargetUserId {
            get { return _DiscordOptions.Value.TargetUserId; }
        }

        public DiscordService(ILogger<DiscordService> logger, ILoggerFactory loggerFactory,
            IOptions<DiscordOptions> discordOptions, IServiceProvider services,
            PluralKitApi pkApi, DiscordWrapper discord, 
            ReminderRepository reminderRepository) {

            _Logger = logger;

            _DiscordOptions = discordOptions;

            SELF_REMINDER_DELAY = TimeSpan.FromSeconds(_DiscordOptions.Value.SelfReminderDelaySeconds);
            OTHER_REMINDER_DELAY = TimeSpan.FromSeconds(_DiscordOptions.Value.OtherReminderDelaySeconds);
            SNOOZE_DELAY = TimeSpan.FromSeconds(_DiscordOptions.Value.SnoozeDelaySeconds);
            _Logger.LogInformation($"settings [self reminder={SELF_REMINDER_DELAY}] [other reminder={OTHER_REMINDER_DELAY}] [snooze={SNOOZE_DELAY}]");

            _Discord = discord;
            _pkApi = pkApi;

            _Discord.Get().Ready += Client_Ready;
            _Discord.Get().InteractionCreated += Generic_Interaction_Created;
            _Discord.Get().ContextMenuInteractionCreated += Generic_Interaction_Created;
            _Discord.Get().GuildAvailable += Guild_Available;
            _Discord.Get().MessageCreated += Message_Created;
            _Discord.Get().MessageDeleted += Message_Deleted;
            _Discord.Get().ComponentInteractionCreated += ComponentInteraction_Created;

            _SlashCommands = _Discord.Get().UseSlashCommands(new SlashCommandsConfiguration() {
                Services = services
            });

            _SlashCommands.SlashCommandErrored += Slash_Command_Errored;

            _SlashCommands.RegisterCommands<ReminderSlashCommand>(_GuildId);
            _ReminderRepository = reminderRepository;
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
                    await Task.Delay(5000, stoppingToken); // check every 5 seconds for reminders to send

                    List<Reminder> toSend = await _ReminderRepository.GetRemindersToSend();

                    foreach (Reminder r in toSend) {
                        await Ping(r);
                    }
                } catch (Exception ex) when (stoppingToken.IsCancellationRequested == false) {
                    _Logger.LogError(ex, "error sending message");
                } catch (Exception) when (stoppingToken.IsCancellationRequested == true) {
                    _Logger.LogInformation($"stopping {SERVICE_NAME}");
                    break;
                }
            }
        }

        /// <summary>
        ///     when a message is removed, delete that reminder, UNLESS it was deleted by pluralkit
        ///     (we use the pk api to determine if the message was deleted by pk or not)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private async Task Message_Deleted(DiscordClient sender, MessageDeleteEventArgs args) {
            ulong? guildID = null;
            if (args.Guild == null) {
                guildID = args.Channel.GuildId;
            } else {
                guildID = args.Guild.Id;
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
            // if there is a PK message, that means that PK deleted the message,
            //      so we don't want to delete the reminder
            PkMessage? pkMsg = await _pkApi.GetMessage(args.Message.Id);
            if (pkMsg == null) {
                await _ReminderRepository.Remove(key);
            } else {
                _Logger.LogInformation($"message deleted by pk proxy, not removing reminder [key={key}]");
            }
        }

        /// <summary>
        ///     when a message is sent in a channel that is being tracked, we create a reminder.
        ///     there are 2 types of reminders, reminders from the target user, and to the target user.
        ///     a reminder from the target user is created if the target user is the one sending the message.
        ///     in this case, they will be reminded in a much longer delay.
        ///     if the reminder is from a different user, then a reminder is made for the target user
        ///     after a shorter delay
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private async Task Message_Created(DiscordClient sender, MessageCreateEventArgs args) {
            if (_ChannelIds.Contains(args.Channel.Id) == false) {
                return;
            }
            if (args.Guild?.Id != _GuildId) {
                return;
            }
            if (args.Author.Id == sender.CurrentUser.Id) { // ignore messages from this bot
                return;
            }

            Reminder r = new();
            r.MessageID = args.Message.Id;
            r.GuildID = args.Guild.Id;
            r.TargetUserID = _TargetUserId;
            r.ChannelID = args.Channel.Id;
            r.Timestamp = args.Message.Timestamp;

            // depending on the user that sent the message, the reminder delay is different
            ulong senderID = args.Author.Id;

            // for a pluralkit message, get the user ID of the sender
            if (args.Message.ApplicationId == PK_APP_ID) {
                _Logger.LogInformation($"pk message sent [msg id={args.Message.Id}]");
                PkMessage? msg = await _pkApi.GetMessage(args.Message.Id);
                if (msg == null) {
                    _Logger.LogWarning($"missing pk message [msg id={args.Message.Id}]");
                } else {
                    senderID = msg.SenderMessageID;
                }
            }

            if (senderID != _TargetUserId) {
                r.SendAfter = r.Timestamp + OTHER_REMINDER_DELAY;
            } else {
                r.SendAfter = r.Timestamp + SELF_REMINDER_DELAY;
                r.StickySelfReminder = true;
            }

            string key = $"{r.GuildID}.{r.ChannelID}.{r.TargetUserID}";
            Reminder? reminder = await _ReminderRepository.GetByKey(key);
            if (reminder != null && reminder.StickySelfReminder == true) {
                _Logger.LogInformation($"message is set to stick to self reminder duration [key={key}]");
                r.SendAfter = r.Timestamp + SELF_REMINDER_DELAY;
                r.StickySelfReminder = true;
            }

            await _ReminderRepository.Upsert(r);
        }

        /// <summary>
        ///     handles the button interactions when the reminder is sent (to either snooze or delete the reminder)
        /// </summary>
        /// <param name="client"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        private async Task ComponentInteraction_Created(DiscordClient client, ComponentInteractionCreateEventArgs args) {
            _Logger.LogInformation($"comp interaction! [id={args.Id}]");

            if (args.User.Id != _TargetUserId) {
                await args.Interaction.CreateImmediateText($"this can only be dismissed by @<{_TargetUserId}>", ephemeral: true);
                return;
            }

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

                Reminder? r = await _ReminderRepository.GetByKey(key);
                if (r != null) {
                    if (parts[0] == "@snooze") {
                        r.SendAfter = DateTimeOffset.UtcNow + SNOOZE_DELAY;
                        r.Sent = false;
                        await _ReminderRepository.Upsert(r);
                        _Logger.LogInformation($"snoozing reminder for 1h [key={key}]");
                    } else if (parts[0] == "@remove") {
                        await _ReminderRepository.Remove(key);
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

        /// <summary>
        ///     send the reminder
        /// </summary>
        /// <param name="reminder"></param>
        /// <returns></returns>
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
            builder.WithReply(reminder.MessageID, mention: false);
            builder.WithContent($"<@{reminder.TargetUserID}>"); // an embed cannot ping, must include the @ outside the embed
            builder.AddComponents(
                new DiscordButtonComponent(ButtonStyle.Primary, $"@snooze.{reminder.TargetUserID}", "Snooze (1h)"),
                new DiscordButtonComponent(ButtonStyle.Danger, $"@remove.{reminder.TargetUserID}", "Remove")
            );

            DiscordEmbedBuilder embed = new();
            embed.Title = "reminder!";
            embed.Color = DiscordColor.Gold;
            embed.Description = $"This is a reminder for <@{reminder.TargetUserID}>!\n";
            embed.Description += $"-# original message sent at <t:{reminder.Timestamp.ToUnixTimeSeconds()}:f>";

            builder.AddEmbed(embed);
            builder.AddMention(new UserMention(reminder.TargetUserID));

            DiscordMessage msg = await _Discord.Get().SendMessageAsync(channel, builder);
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
        private Task Client_Ready(DiscordClient sender, ReadyEventArgs args) {
            _Logger.LogInformation($"Discord client connected");

            _IsConnected = true;
            return Task.CompletedTask;
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

        /// <summary>
        ///     Event handler for when a slash command fails
        /// </summary>
        /// <param name="ext"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private async Task Slash_Command_Errored(SlashCommandsExtension ext, SlashCommandErrorEventArgs args) {
            if (args.Exception is SlashExecutionChecksFailedException failedCheck) {
                string feedback = "Check failed:\n";

                foreach (SlashCheckBaseAttribute check in failedCheck.FailedChecks) {
                    /*
                    if (check is RequiredRoleSlashAttribute role) {
                        _Logger.LogWarning($"{args.Context.User.GetDisplay()} attempted to use {args.Context.CommandName},"
                            + $" but lacks the Discord roles: {string.Join(", ", role.Roles)}");
                        feedback += $"You lack a required role: {string.Join(", ", role.Roles)}";
                    } else {
                        feedback += $"Unchecked check type: {check.GetType()}";
                        _Logger.LogError($"Unchecked check type: {check.GetType()}");
                    }
                    */
                    feedback += $"Unchecked check type: {check.GetType()}";
                    _Logger.LogError($"Unchecked check type: {check.GetType()}");
                }

                await args.Context.CreateImmediateText(feedback, true);

                return;
            }

            _Logger.LogError(args.Exception, $"error executing slash command: {args.Context.CommandName}");

            if (args.Exception is BadRequestException badRequest) {
                _Logger.LogError($"errors in request [url={badRequest.WebRequest.Url}] [errors={badRequest.Errors}]");
            }

            try {
                // if the response has already started, this won't be null, indicating to instead update the response
                DiscordMessage? msg = null;
                try {
                    msg = await args.Context.GetOriginalResponseAsync();
                } catch (NotFoundException) {
                    msg = null;
                }

                if (msg == null) {
                    // if it is null, then no respons has been started, so one is created
                    // if you attempt to create a response for one that already exists, then a 400 is thrown
                    await args.Context.CreateImmediateText($"Error executing slash command: {args.Exception.Message}", true);
                } else {
                    await args.Context.EditResponseText($"Error executing slash command: {args.Exception.Message}");
                }
            } catch (Exception ex) {
                _Logger.LogError(ex, $"error sending error message to Discord");
            }
        }

    }
}
