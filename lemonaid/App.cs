using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace lemonaid {

    public class App : IHostedService {

        private readonly ILogger<App> _Logger;
        private readonly DiscordWrapper _Discord;

        public App(ILogger<App> logger,
            DiscordWrapper discord) {

            _Logger = logger;
            _Discord = discord;
        }

        public async Task StartAsync(CancellationToken cancellationToken) {
            _Logger.LogInformation("app started");
        }

        public async Task StopAsync(CancellationToken cancellationToken) {
            _Logger.LogInformation($"stopping");
        }

    }
}
