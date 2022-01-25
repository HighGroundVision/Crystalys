using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Threading.Tasks;

namespace HGV.Crystalys.Tests
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            await Host.CreateDefaultBuilder(args)
                 .UseContentRoot(Directory.GetCurrentDirectory())
                 .ConfigureLogging(logger => 
                 { 
                     // TOOD: setup logger
                 })
                 .ConfigureAppConfiguration(config =>
                 {
                     config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                     config.AddUserSecrets<Program>();
                     config.AddEnvironmentVariables();
                     //config.AddCommandLine(args);
                 })
                 .ConfigureServices((hostContext, services) =>
                 {
                     services.AddHostedService<ConsoleHostedService>();
                     services.AddOptions<AuthenticationOptions>().Bind(hostContext.Configuration.GetSection("Steam"));
                     services.AddSingleton<ISteamAuthenticationProvider, OptionsAuthenticationProvider>();
                 })
                 .RunConsoleAsync();

            
        }
    }
}
