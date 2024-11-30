using lemonaid.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace lemonaid.Services {

    public class ReminderRepository {

        private readonly ILogger<ReminderRepository> _Logger;

        private readonly Dictionary<string, Reminder> _Reminders = new();

        public ReminderRepository(ILogger<ReminderRepository> logger) {
            _Logger = logger;
        }

        /// <summary>
        /// insert or update (upsert) a <see cref="Reminder"/><br/>
        /// 
        /// In the case of an update, only <see cref="Reminder.SendAfter"/> is updated,
        ///     and <see cref="Reminder.StickySelfReminder"/> is set to <c>true</c>
        ///     if <paramref name="reminder"/> has that field as true, but it is not
        ///     cleared if <paramref name="reminder"/> has that field unset<br/>
        ///     
        /// all other fields remain the same
        /// </summary>
        /// <param name="reminder"></param>
        /// <returns></returns>
        public Task Upsert(Reminder reminder) {
            string key = $"{reminder.GuildID}.{reminder.ChannelID}.{reminder.TargetUserID}";

            if (_Reminders.ContainsKey(key)) {
                _Logger.LogInformation($"pushing reminder back [key={key}] [send after={reminder.SendAfter:u}]");
                _Reminders[key].SendAfter = reminder.SendAfter;
                _Reminders[key].StickySelfReminder |= reminder.StickySelfReminder;
            } else {
                _Logger.LogInformation($"reminder added [author={reminder.TargetUserID}] [timestamp={reminder.Timestamp:u}] [send after={reminder.SendAfter:u}]");
                _Reminders.Add(key, reminder);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        ///     get all <see cref="Reminder"/>s within the repository
        /// </summary>
        /// <returns></returns>
        public Task<List<Reminder>> GetAll() {
            return Task.FromResult(_Reminders.Values.ToList());
        }

        /// <summary>
        ///     get a <see cref="Reminder"/> by a combination of guild ID, channel ID, and target user ID
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public Task<Reminder?> GetByKey(string key) {
            return Task.FromResult(_Reminders.GetValueOrDefault(key));
        }

        /// <summary>
        ///     get all <see cref="Reminder"/>s that need to be sent
        /// </summary>
        /// <returns></returns>
        public Task<List<Reminder>> GetRemindersToSend() {
            List<Reminder> reminders = [];
            foreach (KeyValuePair<string, Reminder> iter in _Reminders) {
                Reminder reminder = iter.Value;

                if (DateTimeOffset.UtcNow < reminder.SendAfter || reminder.Sent == true) {
                    continue;
                }

                _Logger.LogInformation($"sending reminder [message ID={reminder.MessageID}]");
                reminder.Sent = true;
                reminders.Add(reminder);
            }

            return Task.FromResult(reminders);
        }

        /// <summary>
        ///     remove a <see cref="Reminder"/> from the repository
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public Task Remove(string key) {
            if (_Reminders.ContainsKey(key)) {
                Reminder? r = _Reminders.GetValueOrDefault(key);
                _Logger.LogInformation($"removed reminder [key={key}] [send after={r?.SendAfter:u}]");
            }
            _Reminders.Remove(key);

            return Task.CompletedTask;
        }

    }
}
