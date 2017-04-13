using Noemax.Compression;
using SteamKit2;
using SteamKit2.GC;
using SteamKit2.GC.Dota.Internal;
using SteamKit2.GC.Internal;
using SteamKit2.Internal;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HGV.Crystalys
{
    public class DotaGameClient : IDisposable
    {
        #region Properties

        const int APPID = 570;

        private SteamClient SteamClient { get; set; }

        private string Username { get; set; }
        private string Password { get; set; }

        private SteamGameCoordinator Coordinator { get; set; }
        private CallbackManager Callbacks { get; set; }

        private ConcurrentDictionary<ulong, CMsgDOTAMatch> Matches { get; set; }

        #endregion

        #region Constructor

        public DotaGameClient()
        {
            this.Matches = new ConcurrentDictionary<ulong, CMsgDOTAMatch>();

            this.SteamClient = new SteamClient();

            // get the GC handler, which is used for messaging DOTA
            this.Coordinator = this.SteamClient.GetHandler<SteamGameCoordinator>();

            // register a few callbacks we're interested in
            this.Callbacks = new CallbackManager(this.SteamClient);

            // these are registered upon creation to a callback manager, which will then route the callbacks to the functions specified
            this.Callbacks.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
            this.Callbacks.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
            this.Callbacks.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
            this.Callbacks.Subscribe<SteamGameCoordinator.MessageCallback>((SteamGameCoordinator.MessageCallback callback) =>
            {
               
            });
        }

        #endregion

        #region Events

        void OnConnected(SteamClient.ConnectedCallback callback)
        {
            if (callback.Result == EResult.OK)
            {
                Trace.TraceInformation("Steam: Logging in '{0}'", this.Username);

                // get the steamuser handler, which is used for logging on after successfully connecting
                var UserHandler = this.SteamClient.GetHandler<SteamUser>();
                UserHandler.LogOn(new SteamUser.LogOnDetails
                {
                    Username = this.Username,
                    Password = this.Password,
                });

                this.Callbacks.RunWaitCallbacks();
            }
            else
            {
                Trace.TraceInformation("Steam: Failed to Connect. Unknown Error '{0}'", Enum.GetName(typeof(EResult), callback.Result));
                // error = true;
            }
        }

        void OnLoggedOn(SteamUser.LoggedOnCallback callback)
        {
            if (callback.Result == EResult.OK)
            {
                Trace.TraceInformation("Steam: LoggedOn");

                // we've logged into the account
                // now we need to inform the steam server that we're playing dota (in order to receive GC messages)
                // steamkit doesn't expose the "play game" message through any handler, so we'll just send the message manually
                var gameMsg = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);
                gameMsg.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed { game_id = new GameID(APPID) });

                // send it off - notice here we're sending this message directly using the SteamClient
                this.SteamClient.Send(gameMsg);

                // delay a little to give steam some time to establish a GC connection to us
                Thread.Sleep(TimeSpan.FromSeconds(1));

                // inform the dota GC that we want a session
                var helloMsg = new ClientGCMsgProtobuf<CMsgClientHello>((uint)EGCBaseClientMsg.k_EMsgGCClientHello);
                helloMsg.Body.engine = ESourceEngine.k_ESE_Source2;
                this.Coordinator.Send(helloMsg, APPID);

                while(true)
                {
                    this.Callbacks.RunWaitCallbacks();
                }
            }
            else if (callback.Result == SteamKit2.EResult.TryAnotherCM)
            {
                Thread.Sleep(TimeSpan.FromSeconds(5));

                SteamClient.Connect();

                this.Callbacks.RunWaitCallbacks();
            }
            else if (callback.Result == EResult.AccountLogonDenied)
            {
                Trace.TraceInformation("Steam: Guard code required. Use STEAM GUARD to generate sentry.");
            }
            else if (callback.Result == EResult.AccountLoginDeniedNeedTwoFactor)
            {
                Trace.TraceInformation("Steam: Two factor code required. Use STEAM GUARD to generate sentry.");
            }
            else
            {
                Trace.TraceInformation("Steam: Failed to Connect. Unknown Error '{0}'", Enum.GetName(typeof(EResult), callback.Result));
            }
        }

        void OnDisconnected(SteamClient.DisconnectedCallback callback)
        {
            Trace.TraceInformation("Steam: Disconnected.");

            Thread.Sleep(TimeSpan.FromSeconds(5));

            Trace.TraceInformation("Steam: Reconnecting.");

            this.SteamClient.Connect();

            this.Callbacks.RunWaitCallbacks();
        }

        void OnGCMessage(SteamGameCoordinator.MessageCallback callback)
        {
            if (callback.EMsg == (uint)EGCBaseClientMsg.k_EMsgGCClientWelcome)
            {
                var msg = new ClientGCMsgProtobuf<CMsgClientWelcome>(callback.Message);

                Trace.TraceInformation("Dota: GC Welcome.  Version: {0}", msg.Body.version);
            }
            else if (callback.EMsg == (uint)EDOTAGCMsg.k_EMsgGCMatchDetailsResponse)
            {
                Trace.TraceInformation("Dota: Match Data");

                var msg = new ClientGCMsgProtobuf<CMsgGCMatchDetailsResponse>(callback.Message);
                var matchDetails = msg.Body.match;
                this.Matches.TryAdd(matchDetails.match_id, matchDetails);
            }
            else
            {
                Trace.TraceInformation("Dota: Unknown Msg '{0}'", callback.EMsg);
            }
        }

        #endregion

        #region Connect

        public void Connect(string user, string password)
        {
            this.Username = user;
            this.Password = password;
            
            Trace.TraceInformation("Steam: Connect");

            // initiate the connection
            SteamClient.Connect();

            this.Callbacks.RunWaitCallbacks();
        }

        #endregion

        #region Disconnect

        public void Dispose()
        {
            if (this.SteamClient != null)
            {
                this.SteamClient.Disconnect();
                this.SteamClient = null;
            }
        }

        #endregion

        #region DOTA Functions

        public CMsgDOTAMatch DownloadMatchData(ulong matchId)
        {
            // Send Request
            var request = new ClientGCMsgProtobuf<CMsgGCMatchDetailsRequest>((uint)EDOTAGCMsg.k_EMsgGCMatchDetailsRequest);
            request.Body.match_id = matchId;
            this.Coordinator.Send(request, APPID);

            this.Callbacks.RunWaitCallbacks();

            CMsgDOTAMatch match = null;

            this.Matches.TryRemove(matchId, out match);

            return match;
        }

        public async Task<byte[]> DownloadReplay(ulong matchId, uint cluster, uint salt)
        {
            var data = await DownloadData(matchId, cluster, salt, "dem");
            return data;
        }

        public async Task<CDOTAMatchMetadata> DownloadMeta(ulong matchId, uint cluster, uint salt)
        {
            var data = await DownloadData(matchId, cluster, salt, "meta");

            using (var steam = new MemoryStream(data))
            {
                var meta = ProtoBuf.Serializer.Deserialize<CDOTAMatchMetadataFile>(steam);
                return meta.metadata;
            }
        }

        private async Task<byte[]> DownloadData(ulong match_id, uint cluster, uint replay_salt, string type)
        {
            using (var client = new WebClient())
            {
                var url = string.Format("http://replay{0}.valve.net/{1}/{2}_{3}.{4}.bz2", cluster, APPID, match_id, replay_salt, type);
                var compressedMatchData = await client.DownloadDataTaskAsync(url);
                return CompressionFactory.BZip2.Decompress(compressedMatchData);
            }
        }

        #endregion
    }
}
