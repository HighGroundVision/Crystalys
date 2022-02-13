using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.GC.Dota.Internal;
using System.Linq;

namespace HGV.Crystalys.Tests
{
    internal class ConsoleHostedService : IHostedService
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

            //var apps = client.GetHandler<SteamApps>();
            //var steam = client.GetHandler<SteamGameCoordinator>();
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
                    Password = _provider.Password,
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
                    client.Connect();
                else
                    _cts.Cancel();
            });

            callbacks.Subscribe<SteamUser.LoggedOnCallback>(async (_) =>
            {
                _logger.LogInformation($"LoggedOn; Result: {Enum.GetName(_.Result.GetType(), _.Result)}");

                if (_.Result != EResult.OK)
                    return;

                _logger.LogInformation("Set Persona Status to Online...");

                friends.SetPersonaState(EPersonaState.Online);

                await dota.Start();
            });
            callbacks.Subscribe<GCWelcomeCallback>((_) =>
            {
                _logger.LogInformation("Dota Started!");
                _logger.LogInformation("Creating Lobby...");

                var key = Guid.NewGuid().ToString().Substring(0, 8);

                var details = new CMsgPracticeLobbySetDetails
                {
                    game_name = $"HGV TEST {key}",
                    pass_key = "789",
                    server_region = 2,
                    game_mode = 18,
                    allow_cheats = false,
                    fill_with_bots = false,
                    allow_spectating = true,
                    dota_tv_delay = LobbyDotaTVDelay.LobbyDotaTV_300,
                    pause_setting = LobbyDotaPauseSetting.LobbyDotaPauseSetting_Unlimited,
                    game_version = DOTAGameVersion.GAME_VERSION_CURRENT,
                    visibility = DOTALobbyVisibility.DOTALobbyVisibility_Public
                };
                //details.ability_draft_specific_details = new CMsgPracticeLobbySetDetails.AbilityDraftSpecificDetails
                //{
                //    shuffle_draft_order = false
                //};
                //details.requested_hero_ids.AddRange(new List<uint> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 });

                dota.CreateLobby(details);
            });

            callbacks.Subscribe<PracticeLobbyCreated>((_) =>
            {
                _logger.LogInformation("Lobby Active!");

                _logger.LogInformation("Kicking Bot From Slot...");
                dota.KickPlayerFromLobbyTeam(client.SteamID.AccountID);

                _logger.LogInformation("Joining Chat Channel");
                var name = $"Lobby_{dota.Lobby.lobby_id}";
                dota.JoinChatChannel(name, DOTAChatChannelType_t.DOTAChannelType_Lobby);

            });
            callbacks.Subscribe<JoinChatChannelResponse>((_) =>
            {
                var response = _.result;
                if (response.result != CMsgDOTAJoinChatChannelResponse.Result.JOIN_SUCCESS)
                {
                    var reason = Enum.GetName(typeof(CMsgDOTAJoinChatChannelResponse.Result), response.result);
                    _logger.LogError($"Failed to join chat: {reason}");
                    _cts.Cancel();
                    return;
                }

                _logger.LogInformation("Joined Chat Channel!");

                _logger.LogInformation("Sending Welcome Message...");
                dota.SendChannelMessage(response.channel_id, "Hello World! ");

                _logger.LogInformation("Start Chat Loop");
            });

            callbacks.Subscribe<ChatMessage>(async (_) =>
            {
                var cmd = _.result.text.ToUpper();
                switch (cmd)
                {
                    case "HELP":
                        SendHelpMessage(dota);
                        break;
                    case "START":
                        await StartMatch(dota);
                        break;
                    case "STOP":
                        await StopTest(dota);
                        break;
                    case "FLIP":
                        dota.PracticeLobbyFlip();
                        break;
                    case "SHUFFLE TEAMS":
                        dota.PracticeLobbyShuffleTeam();
                        break;
                    case "SHUFFLE PLAYERS":
                        dota.PracticeLobbyShuffleDraftOrder(true);
                        break;
                    default:
                        _logger.LogInformation($"Chatter: user {_.result.persona_name} said: {_.result.text}");
                        break;
                }

                if(cmd.StartsWith("SET HEROES"))
                {
                    var collection = cmd.Replace("SET HEROES", "");
                    var roster = collection.Split(",").Select(_ => uint.Parse(_)).ToList();
                    if(roster.Count == 12)
                        dota.PracticeLobbySetRoster(roster);
                }
            });
            callbacks.Subscribe<PracticeLobbySnapshot>((_) =>
            {
                SendHelpMessage(dota);

                var members = _.lobby.all_members.Select(_ => _.name).ToList();
                _logger.LogInformation($"Practice Lobby Updated! Members: {string.Join(", ", members)}");
            });

            client.Connect();

            while (_cts.Token.IsCancellationRequested == false)
            {
                callbacks.RunWaitAllCallbacks(TimeSpan.FromSeconds(1));
            }
        }

        private void SendHelpMessage(DotaGameCoordinatorHandler dota)
        {
            _logger.LogInformation("Sending Help Message to Lobby Chat Channel (if it exists)");

            var chanel = dota.ChatChannels.FirstOrDefault();
            if (chanel is not null)
            {
                dota.SendChannelMessage(chanel.channel_id, "Hello!");
                dota.SendChannelMessage(chanel.channel_id, "I accept following commands:");
                dota.SendChannelMessage(chanel.channel_id, "START, STOP, FLIP, SHUFFLE TEAMS, SHUFFLE PLAYERS");
            }
        }

        private async Task StartMatch(DotaGameCoordinatorHandler dota)
        {
            _logger.LogInformation("Count Down....");

            var chanel = dota.ChatChannels.Where(_ => _.channel_type == DOTAChatChannelType_t.DOTAChannelType_Lobby).FirstOrDefault();
            for (int i = 3; i > 0; i--)
            {
                dota.SendChannelMessage(chanel.channel_id, $"{i}");
                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            _logger.LogInformation("Lunching Game....");
            dota.LaunchLobby();

            await Task.Delay(TimeSpan.FromSeconds(10));

            _logger.LogInformation("Leaving Lobby...");
            dota.LeaveLobby();

            await Task.Delay(TimeSpan.FromSeconds(10));

            _logger.LogInformation("Stoping Dota...");
            dota.Stop();

            _logger.LogInformation("Disconnect...");
            dota.SteamClient.Disconnect();
        }

        private async Task StopTest(DotaGameCoordinatorHandler dota)
        {
            _logger.LogInformation("Destroy Lobby...");
            dota.DestroyLobby();

            await Task.Delay(TimeSpan.FromSeconds(10));

            _logger.LogInformation("Stoping Dota...");
            dota.Stop();

            _logger.LogInformation("Disconnect...");
            dota.SteamClient.Disconnect();
        }


        public Task StopAsync(CancellationToken cancellationToken)
        {
            _cts.Cancel();

            return Task.CompletedTask;
        }
        
    }
}
