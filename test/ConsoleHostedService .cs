using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.GC.Dota.Internal;

namespace HGV.Crystalys.Tests
{
    internal sealed class ConsoleHostedService : IHostedService
    {
        private readonly ILogger _logger;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly ISteamAuthenticationProvider _provider;
        private readonly CancellationTokenSource _cts;

        public ConsoleHostedService(ILogger<ConsoleHostedService> logger, IHostApplicationLifetime appLifetime, ISteamAuthenticationProvider provider)
        {
            _logger = logger;
            _appLifetime = appLifetime;
            _provider = provider;
            _cts = new CancellationTokenSource();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug($"Starting with arguments: {string.Join(" ", Environment.GetCommandLineArgs())}");

            //_appLifetime.ApplicationStarted.Register(() => Task.Run(Main));
            _appLifetime.ApplicationStarted.Register(() => Task.Run(Main));

            return Task.CompletedTask;
        }

        private void Main()
        {
            try
            {
                DoWork();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception!");
            }
            finally
            {
                _appLifetime.StopApplication(); // Stop the application once the work is done
            }
        }

        private void DoWork()
        {
            var client = new SteamClient();
            client.AddHandler(new DotaGameCoordinatorHandler(client));

            var apps = client.GetHandler<SteamApps>();
            var steam = client.GetHandler<SteamGameCoordinator>();
            var user = client.GetHandler<SteamUser>();
            var friends = client.GetHandler<SteamFriends>();
            var dota = client.GetHandler<DotaGameCoordinatorHandler>();
            
            var callbacks = new CallbackManager(client);
            callbacks.Subscribe<SteamClient.ConnectedCallback>((_) =>
            {
                _logger.LogInformation($"Connected to Steam Network!");
                _logger.LogInformation($"Authenticating '{_provider.UserName}' with Steam...");

                user.LogOn(new SteamUser.LogOnDetails
                {
                    Username = _provider.UserName,
                    Password = _provider.Password
                });
            });
            callbacks.Subscribe<SteamUser.LoggedOffCallback>((_) =>
            {
                _logger.LogInformation($"Logged Off Reason: {Enum.GetName(_.Result.GetType(), _.Result)}");

                friends.SetPersonaState(EPersonaState.Offline);

                client.Disconnect();
            });

            callbacks.Subscribe<SteamClient.DisconnectedCallback>((_) =>
            {
                _logger.LogInformation($"Disconnected; User Initiated: {_.UserInitiated}");

                if (_.UserInitiated == false)
                {
                    client.Connect();
                }
            });

            callbacks.Subscribe<SteamUser.LoggedOnCallback>(async (_) => 
            {
                _logger.LogInformation($"LoggedOn; Result: {Enum.GetName(_.Result.GetType(), _.Result)}");

                if (_.Result != EResult.OK)
                    return;

                friends.SetPersonaState(EPersonaState.Online);

                await dota.Start();
            });

            callbacks.Subscribe<GCWelcomeCallback>(async (_) =>
            {
                _logger.LogInformation("Dota Started...");

                await Task.Delay(TimeSpan.FromSeconds(10));

                if (dota.Lobby != null)
                    return;

                _logger.LogInformation("Creating Lobby...");

                var details = new CMsgPracticeLobbySetDetails
                {
                    game_name = "TEST",
                    pass_key = "789",
                    server_region = 2,
                    game_mode = 18,
                    allow_cheats = false,
                    fill_with_bots = false,
                    allow_spectating = true,
                    dota_tv_delay = LobbyDotaTVDelay.LobbyDotaTV_300,
                    pause_setting = LobbyDotaPauseSetting.LobbyDotaPauseSetting_Unlimited,
                    game_version = DOTAGameVersion.GAME_VERSION_CURRENT,
                    visibility = DOTALobbyVisibility.DOTALobbyVisibility_Public,

                };
                details.ability_draft_specific_details = new CMsgPracticeLobbySetDetails.AbilityDraftSpecificDetails
                {
                    shuffle_draft_order = false
                };
                details.requested_hero_ids.AddRange(new List<uint> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 });
                dota.CreateLobby(details);
            });

            callbacks.Subscribe<PracticeLobbySnapshot>((_) =>
            {
                if(_.initialization)
                {
                    _logger.LogInformation("Lobby Active!");
                    _logger.LogInformation("Kicking Bot From Slot...");
                    dota.KickPlayerFromLobbyTeam(client.SteamID.AccountID);

                    _logger.LogInformation("Joining Chat Channel");
                    var name = $"Lobby_{dota.Lobby.lobby_id}";
                    dota.JoinChatChannel(name, DOTAChatChannelType_t.DOTAChannelType_Lobby);
                }
                else
                {
                    _logger.LogInformation("Lobby Snapshot...");
                }
                
            });

            callbacks.Subscribe<JoinChatChannelResponse>((_) => 
            {
                _logger.LogInformation("Joined Chat Channel!");
                _logger.LogInformation("Sending Welcome Message...");

                dota.SendChannelMessage(_.result.channel_id, "Hello World");
            });

            callbacks.Subscribe<ChatMessage>(async (_) =>
            {
                // _.result.account_id
                if(_.result.text == "START")
                {
                    _logger.LogInformation("Lunching Game....");
                    dota.LaunchLobby();

                    await Task.Delay(TimeSpan.FromSeconds(10));

                    _logger.LogInformation("Leaving Lobby...");
                    dota.LeaveLobby();

                    await Task.Delay(TimeSpan.FromSeconds(10));

                    _logger.LogInformation("Disconnect...");
                    dota.Stop();
                    client.Disconnect();

                    _cts.Cancel();
                }
                else if (_.result.text == "STOP")
                {
                    _logger.LogInformation("Destroy Lobby...");
                    dota.DestroyLobby();

                    await Task.Delay(TimeSpan.FromSeconds(10));

                    _logger.LogInformation("Disconnect...");
                    dota.Stop();
                    client.Disconnect();
                }
                else
                {
                    _logger.LogInformation("Chatter...");
                }
            });

            client.Connect();

            while (_cts.Token.IsCancellationRequested == false)
            {
                callbacks.RunWaitAllCallbacks(TimeSpan.FromSeconds(1));
            }

        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _cts.Cancel();

            return Task.CompletedTask;
        }
        
    }
}
