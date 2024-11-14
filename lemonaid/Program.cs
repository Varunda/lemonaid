using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using lemonaid.Services;

namespace lemonaid {

    public class Program {

        public static void Main(string[] args) {
            Console.WriteLine("woah");

            IHostBuilder builder = Host.CreateDefaultBuilder();
            builder.ConfigureAppConfiguration((config) => {
                config.AddJsonFile("secrets.json");
            }).ConfigureLogging((logging) => {
                // i don't like any of the provided default loggers
                logging.AddConsole(options => options.FormatterName = "OneLineLogger")
                    .AddConsoleFormatter<OneLineLogger, OneLineFormatterOptions>(options => {

                    });
            });

            builder.ConfigureServices((context, services) => {
                services.Configure<DiscordOptions>(context.Configuration.GetSection("Discord"));

                services.AddSingleton<DiscordWrapper>();
                services.AddSingleton<PluralKitApi>();
                services.AddHostedService<DiscordService>();

                services.AddHostedService<App>();
            });

            using IHost host = builder.Build();
            host.Run();
        }

    }
}
