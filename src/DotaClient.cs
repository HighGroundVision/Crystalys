using Polly;
using Polly.Timeout;
using SteamKit2;
using SteamKit2.GC;
using SteamKit2.GC.Dota.Internal;
using SteamKit2.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HGV.Crystalys
{
    public interface IDotaClient
    {
        bool isConnected();
        Task<Stream> DownloadReplay(ulong matchId);
        Task<CDOTAMatchMetadata> DownloadMeta(ulong matchId);
    }

    public class DotaClient : IDotaClient, IDisposable
    {
        const int APPID = 570;

        private static readonly Object obj = new Object();

        // private readonly ISteamAuthenticationProvider provider;
        protected readonly SteamClient steamClient;
        protected readonly HttpClient httpClient;
        protected readonly CallbackManager cbm;
        private readonly SteamGameCoordinator cg;
        private readonly SteamUser user;
        private readonly SteamUser.LogOnDetails logOnDetails;

        private bool IsSteamConnected;
        private bool IsGCConnected;

        private CMsgDOTAMatch match;

        public DotaClient(ISteamAuthenticationProvider provider, IHttpClientFactory factory)
        {
            // this.provider = provider;
            this.httpClient = factory.CreateClient();
            this.steamClient = new SteamClient();

            this.logOnDetails = new SteamUser.LogOnDetails
            {
                Username = provider.GetUserName(),
                Password = provider.GetPassword()
            };

            this.cbm = new CallbackManager(this.steamClient);
            this.user = this.steamClient.GetHandler<SteamUser>();
            this.cg = this.steamClient.GetHandler<SteamGameCoordinator>();
           
            this.SubscribeSteamCallbacks();
            this.ConectToSteam();
        }
        private void SubscribeSteamCallbacks()
        {
            cbm.Subscribe<SteamClient.ConnectedCallback>(callback =>
            {
                this.user.LogOn(this.logOnDetails);
            });
            cbm.Subscribe<SteamClient.DisconnectedCallback>(callback =>
            {
                if(!callback.UserInitiated)
                {
                    this.steamClient.Connect();
                }
            });

            cbm.Subscribe<SteamUser.LoggedOnCallback>(callback =>
            {
                if (callback.Result == EResult.OK)
                {
                    this.IsSteamConnected = true;
                    this.ConnectToDota();
                }
                else if (callback.Result == EResult.AccountLoginDeniedNeedTwoFactor)
                {
                    // Log.Error("Failed to generate 2FA code. Make sure you have linked the authenticator via SteamBot.");
                    // logOnDetails.TwoFactorCode = SteamGuardAccount.GenerateSteamGuardCode();
                }
                else if (callback.Result == EResult.TwoFactorCodeMismatch)
                {
                    // logOnDetails.TwoFactorCode = SteamGuardAccount.GenerateSteamGuardCode();
                }
                else if (callback.Result == EResult.AccountLogonDenied)
                {
                    // Log.Interface("This account is SteamGuard enabled.");
                }
                else if (callback.Result == EResult.InvalidLoginAuthCode)
                {
                    // Log.Interface("The given SteamGuard code was invalid.");
                    // logOnDetails.AuthCode = Console.ReadLine();
                }
            });

            cbm.Subscribe<SteamUser.LoginKeyCallback>(callback =>
            {
                // What is a Key and why dose it matter?
            });

            cbm.Subscribe<SteamUser.WebAPIUserNonceCallback>(webCallback =>
            {
                // Log.Debug("Received new WebAPIUserNonce.");
            });

            cbm.Subscribe<SteamUser.UpdateMachineAuthCallback>(authCallback =>
            {
                // Log.Debug("Save a temp sentry file.");
            });

            cbm.Subscribe<SteamUser.LoggedOffCallback>(callback =>
            {
                // Log.Warn("Logged off Steam.  Reason: {0}", callback.Result);

                this.IsSteamConnected = false;
            });

            cbm.Subscribe<SteamGameCoordinator.MessageCallback>(callback =>
            {
                if (callback.EMsg == (uint)EGCBaseClientMsg.k_EMsgGCClientWelcome)
                {
                    this.IsGCConnected = true;
                    var msg = new ClientGCMsgProtobuf<CMsgClientWelcome>(callback.Message);
                }
                else if (callback.EMsg == (uint)EGCBaseClientMsg.k_EMsgGCClientHello)
                {
                    var msg = new ClientGCMsgProtobuf<CMsgClientHello>(callback.Message);
                }
                else if (callback.EMsg == (uint)EDOTAGCMsg.k_EMsgGCMatchDetailsResponse)
                {
                    var msg = new ClientGCMsgProtobuf<CMsgGCMatchDetailsResponse>(callback.Message);
                    if (msg.Body.result == (uint)EResult.OK)
                    {
                        this.match = msg.Body.match;
                    }
                }
                
            });
        }

        private void ConectToSteam()
        {
            lock (obj)
            {
                this.steamClient.Connect();

                this.WaitOnCallBacks(timeout: 10, condition: () => this.isConnected() == false);
            }
        }

        private void ConnectToDota()
        {
            var playGame = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);

            playGame.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed
            {
                game_id = new GameID(APPID),
            });

            // notice here we're sending this message directly using the SteamClient
            this.steamClient.Send(playGame);

            // delay a little to give steam some time to establish a GC connection to us
            Thread.Sleep(TimeSpan.FromSeconds(5));

            // inform the dota GC that we want a session
            var clientHello = new ClientGCMsgProtobuf<CMsgClientHello>((uint)EGCBaseClientMsg.k_EMsgGCClientHello);
            clientHello.Body.engine = ESourceEngine.k_ESE_Source2;
            clientHello.Body.client_launcher = PartnerAccountType.PARTNER_NONE;
            clientHello.Body.secret_key = "";
            clientHello.Body.client_session_need = 104;

            this.cg.Send(clientHello, APPID);
        }

        private void WaitOnCallBacks(Func<bool> condition, int timeout = 30)
        {
            var policy = Policy.Timeout(timeout, TimeoutStrategy.Pessimistic);
            policy.Execute(() =>
            {
                for (int i = 0; condition(); i++)
                {
                    this.cbm.RunCallbacks();
                }
            });
        }

        private void FetchMatch(ulong id, string type = "meta")
        {
            lock (obj)
            {
                var requestMatch = new ClientGCMsgProtobuf<CMsgGCMatchDetailsRequest>((uint)EDOTAGCMsg.k_EMsgGCMatchDetailsRequest);
                requestMatch.Body.match_id = id;
                this.cg.Send(requestMatch, APPID);

                this.match = null;
                this.WaitOnCallBacks(timeout: 5, condition: () => this.match == null);
            }
        }

        private async Task<Stream> DownloadData(ulong match_id, uint cluster, uint replay_salt, string type)
        {
            var url = string.Format("http://replay{0}.valve.net/{1}/{2}_{3}.{4}.bz2", cluster, APPID, match_id, replay_salt, type);
            return await httpClient.GetStreamAsync(url);
        }

        public void Dispose()
        {
            if(this.steamClient.IsConnected)
            {
                this.IsGCConnected = false;
                this.IsSteamConnected = false;
                this.steamClient.Disconnect();
            }
        }

        public bool isConnected()
        {
            return this.steamClient.IsConnected && this.IsSteamConnected && this.IsGCConnected;
        }

        public async Task<Stream> DownloadReplay(ulong matchId)
        {
            this.FetchMatch(matchId);

            var stream = await DownloadData(this.match.match_id, this.match.cluster, this.match.replay_salt, "dem");
            return stream;
        }

        public async Task<CDOTAMatchMetadata> DownloadMeta(ulong matchId)
        {
            this.FetchMatch(matchId);

            var stream = await DownloadData(this.match.match_id, this.match.cluster, this.match.replay_salt, "meta");
            var meta = ProtoBuf.Serializer.Deserialize<CDOTAMatchMetadataFile>(stream);
            return meta.metadata;
        }

        
    }
}
