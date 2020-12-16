using Microsoft.Extensions.Logging;
using SteamKit2;
using SteamKit2.GC;
using SteamKit2.GC.Dota.Internal;
using SteamKit2.Internal;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HGV.Crystalys
{
    public interface IDotaServices
    {
        Task StartAsync();
        Task<CMsgDOTAMatch> GetMatchData(ulong id, CancellationToken token = default);
    }

    public sealed class DotaServices : IDotaServices, IDisposable
    {
        const int APPID = 570;

        private readonly ILogger<DotaServices> logger;
        private readonly ISteamAuthenticationProvider provider;
        private readonly SteamClient client;
        private readonly CallbackManager callbackManager;
        private readonly SteamUser userHandler;
        private readonly SteamGameCoordinator gameHandler;
        private readonly CancellationTokenSource callbackCancellation;
        private readonly Lazy<Task> callbackTask;
        private TaskCompletionSource<bool> pReady;
        private readonly ConcurrentDictionary<JobID, TaskCompletionSource<CMsgDOTAMatch>> jobsMatchDetails;

        public DotaServices(ILogger<DotaServices> logger, ISteamAuthenticationProvider provider, SteamClient client)
        {
            this.logger = logger;
            this.provider = provider;

            this.pReady = new TaskCompletionSource<bool>();
            
            this.client = client;
            this.userHandler = client.GetHandler<SteamUser>();
            this.gameHandler = client.GetHandler<SteamGameCoordinator>();

            this.callbackCancellation = new CancellationTokenSource();
            this.callbackTask = new Lazy<Task>(Factory);
            this.callbackManager = new CallbackManager(client);
            this.callbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
            this.callbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
            this.callbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedIn);
            this.callbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOut);
            this.callbackManager.Subscribe<SteamGameCoordinator.MessageCallback>(OnGameCoordinatorMessage);

            this.jobsMatchDetails = new ConcurrentDictionary<JobID, TaskCompletionSource<CMsgDOTAMatch>>();
        }

        public void Dispose()
        {
            this.client.Disconnect();

            this.callbackCancellation.Cancel();
            this.pReady.TrySetCanceled();
        }

        private Task Factory()
        {
            var token = this.callbackCancellation.Token;
            return new Task(Loop, token);
        }

        private async void Loop()
        {
            var token = this.callbackCancellation.Token;

            while (!token.IsCancellationRequested)
            {
                this.callbackManager.RunCallbacks();

                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }
        }

        public Task StartAsync()
        {
            this.callbackTask.Value.Start();

            return Connect();
        }

        private Task Connect()
        {
            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(60));
            cts.Token.Register(() => this.pReady.TrySetCanceled(), useSynchronizationContext: false);
            
            this.client.Connect();

            return pReady.Task;
        }

        private void Login()
        {
            var details = new SteamUser.LogOnDetails
            {
                Username = this.provider.GetUserName() ?? throw new InvalidOperationException("Invalid Username"),
                Password = this.provider.GetPassword() ?? throw new InvalidOperationException("Invalid Password"),
            };
            this.userHandler.LogOn(details);
        }

        private void OnConnected(SteamClient.ConnectedCallback callback)
        {
            this.logger.LogWarning("OnConnected()");

            this.Login();
        }

        private async void LunchDota()
        {
            // Connect Request
            var playGame = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);
            playGame.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed
            {
                game_id = new GameID(APPID),
            });

            // notice here we're sending this message directly using the SteamClient
            this.client.Send(playGame);

            await Task.Delay(TimeSpan.FromSeconds(5));

            // Inform the Dota GC that we want a session
            var msg = new ClientGCMsgProtobuf<CMsgClientHello>((uint)EGCBaseClientMsg.k_EMsgGCClientHello);
            msg.Body.engine = ESourceEngine.k_ESE_Source2;
            msg.Body.client_launcher = PartnerAccountType.PARTNER_NONE;
            msg.Body.secret_key = string.Empty;
            msg.Body.client_session_need = 104;

            this.gameHandler.Send(msg, APPID);
        }

        private void OnDisconnected(SteamClient.DisconnectedCallback callback)
        {
            this.logger.LogWarning($"OnDisconnected() UserInitiated: {callback.UserInitiated}");
            
            if(callback.UserInitiated)
                return;

            this.client.Connect();
        }

        private void OnLoggedIn(SteamUser.LoggedOnCallback callback)
        {
            this.logger.LogWarning($"OnLoggedIn() Reason: {callback.Result}");

            if(callback.Result == EResult.OK)
            {
                this.LunchDota();
            }
            else
            {
                var ex = new InvalidOperationException($"Invalid Login: {callback.Result}");
                this.pReady.TrySetException(ex);
            }
        }

        private void OnLoggedOut(SteamUser.LoggedOffCallback callback)
        {
            this.logger.LogWarning("OnLoggedOut() Reason: {0}", callback.Result);
        }

        private void OnGameCoordinatorMessage(SteamGameCoordinator.MessageCallback callback)
        {
            this.logger.LogWarning("OnGameCoordinatorMessage()");

            if (callback.EMsg == (uint)EGCBaseClientMsg.k_EMsgGCClientWelcome)
            {
                this.pReady.SetResult(true);
            }
            else if (callback.EMsg == (uint)EDOTAGCMsg.k_EMsgGCMatchDetailsResponse)
            {
                var msg = new ClientGCMsgProtobuf<CMsgGCMatchDetailsResponse>(callback.Message);

                if(jobsMatchDetails.TryGetValue(callback.JobID, out TaskCompletionSource<CMsgDOTAMatch> tcs))
                    tcs.SetResult(msg.Body.match);
            }
        }

        public Task<CMsgDOTAMatch> GetMatchData(ulong id, CancellationToken token = default)
        {
            var request = new ClientGCMsgProtobuf<CMsgGCMatchDetailsRequest>((uint)EDOTAGCMsg.k_EMsgGCMatchDetailsRequest);
            request.Body.match_id = id;
            var jobId = request.TargetJobID;

            var tcs = new TaskCompletionSource<CMsgDOTAMatch>();
            jobsMatchDetails.TryAdd(jobId, tcs);
            token.Register(() => tcs.TrySetCanceled(), useSynchronizationContext: false);

            this.gameHandler.Send(request, APPID);

            return tcs.Task.ContinueWith(t => { 
                this.jobsMatchDetails.TryRemove(jobId, out TaskCompletionSource<CMsgDOTAMatch> _);
                return t.Result;
            });

        }
    }
}
