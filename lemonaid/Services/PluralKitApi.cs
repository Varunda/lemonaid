﻿using lemonaid.Code.Extensions;
using lemonaid.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace lemonaid.Services {

    public class PluralKitApi {

        private readonly ILogger<PluralKitApi> _Logger;
        private static readonly HttpClient _Http = new();

        private readonly JsonSerializerOptions _JsonOptions;

        public PluralKitApi(ILogger<PluralKitApi> logger) {
            _JsonOptions = new JsonSerializerOptions() {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            _Logger = logger;
            _Http.DefaultRequestHeaders.UserAgent.ParseAdd("lemonaid/0.1 varunda on discord");
        }

        public async Task<PkMessage?> GetMessage(ulong proxiedMessageID, CancellationToken cancel = default) {
            HttpResponseMessage res = await _Http.GetAsync($"https://api.pluralkit.me/v2/messages/{proxiedMessageID}");
            
            if (res.StatusCode == HttpStatusCode.NotFound) {
                return null;
            }

            string bodyStr = await res.Content.ReadAsStringAsync();

            if (res.StatusCode != HttpStatusCode.OK) {
                throw new Exception($"failed to get proxied message info: {res.StatusCode} {bodyStr}");
            }

            byte[] bytes = await res.Content.ReadAsByteArrayAsync(cancel);
            JsonElement elem = JsonSerializer.Deserialize<JsonElement>(bytes, _JsonOptions);

            PkMessage msg = new();

            msg.MessageID = ulong.Parse(elem.GetRequiredString("id"));
            msg.OriginalMessageID = ulong.Parse(elem.GetRequiredString("original"));
            msg.ChannelID = ulong.Parse(elem.GetRequiredString("channel"));
            msg.GuildID = ulong.Parse(elem.GetRequiredString("guild"));
            msg.SenderMessageID = ulong.Parse(elem.GetRequiredString("sender"));
            msg.Timestamp = DateTime.Parse(elem.GetRequiredString("timestamp"));

            return msg;
        }

    }
}
