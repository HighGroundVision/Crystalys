using Noemax.Compression;
using SteamKit2;
using SteamKit2.GC;
using SteamKit2.GC.Dota.Internal;
using SteamKit2.GC.Internal;
using SteamKit2.Internal;
using System;
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
        private byte[] Sentry { get; set; }

        private int Timeout { get; set; }

        #endregion

        #region Constructor

        public DotaGameClient(int timeout_seconds = 30)
        {
            this.Timeout = timeout_seconds;
            this.SteamClient = new SteamClient();
        }

        #endregion

        #region Connect

        public async Task<uint> Connect(string user, string password)
        {
            this.Username = user;
            this.Password = password;

            var guardian = new CancellationTokenSource(TimeSpan.FromSeconds(this.Timeout));

            Func<uint> HandshakeWithSteam = () =>
            {
                Trace.TraceInformation("Steam: Begin Handshake");

                bool completed = false;
                EResult error = EResult.OK;
                uint version = 0;

                // get the GC handler, which is used for messaging DOTA
                var gcHandler = this.SteamClient.GetHandler<SteamGameCoordinator>();

                // register a few callbacks we're interested in
                var cbManager = new CallbackManager(this.SteamClient);

                // these are registered upon creation to a callback manager, 
                // which will then route the callbacks to the functions specified
                cbManager.Subscribe<SteamClient.ConnectedCallback>((SteamClient.ConnectedCallback callback) =>
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
                    }
                    else
                    {
                        Trace.TraceInformation("Steam: Failed to Connect. Unknown Error '{0}'", Enum.GetName(typeof(EResult), callback.Result));
                        error = callback.Result;
                    }
                });

                cbManager.Subscribe<SteamClient.DisconnectedCallback>((SteamClient.DisconnectedCallback callback) =>
                {
                    Trace.TraceInformation("Steam: Disconnected.");

                    // delay a little to give steam some time to finalize the DC
                    Thread.Sleep(TimeSpan.FromSeconds(5));

                    // reconect
                    Trace.TraceInformation("Steam: Reconnecting.");
                    this.SteamClient.Connect();
                });

                cbManager.Subscribe<SteamUser.LoggedOnCallback>((SteamUser.LoggedOnCallback callback) =>
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
                        gcHandler.Send(helloMsg, APPID);
                    }
                    else if (callback.Result == SteamKit2.EResult.TryAnotherCM)
                    {
                        Thread.Sleep(TimeSpan.FromSeconds(5));
                        SteamClient.Connect();
                    }
                    else if (callback.Result == EResult.AccountLogonDenied)
                    {
                        Trace.TraceInformation("Steam: Guard code required. Use STEAM GUARD to generate sentry.");
                        error = callback.Result;
                    }
                    else if (callback.Result == EResult.AccountLoginDeniedNeedTwoFactor)
                    {
                        Trace.TraceInformation("Steam: Two factor code required. Use STEAM GUARD to generate sentry.");
                        error = callback.Result;
                    }
                    else
                    {
                        Trace.TraceInformation("Steam: Failed to Connect. Unknown Error '{0}'", Enum.GetName(typeof(EResult), callback.Result));
                        error = callback.Result;
                    }
                });

                cbManager.Subscribe<SteamGameCoordinator.MessageCallback>((SteamGameCoordinator.MessageCallback callback) =>
                {
                    if (callback.EMsg == (uint)EGCBaseClientMsg.k_EMsgGCClientWelcome)
                    {
                        var msg = new ClientGCMsgProtobuf<CMsgClientWelcome>(callback.Message);

                        version = msg.Body.version;

                        Trace.TraceInformation("Dota: GC Welcome");

                        completed = true;
                    }
                    else
                    {
                        Trace.TraceInformation("Dota: Unknown Msg '{0}'", callback.EMsg);
                    }
                });

                Trace.TraceInformation("Steam: Connect");

                
                // initiate the connection
                SteamClient.Connect();

                while (completed == false && error == EResult.OK)
                {
                    guardian.Token.ThrowIfCancellationRequested();

                    // in order for the callbacks to get routed, they need to be handled by the manager
                    cbManager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
                }

                if(error != EResult.OK)
                {
                    throw new Exception(string.Format("Unhandled Error: {0}", Enum.GetName(typeof(EResult), error)));
                }

                return version;
            };

            await SteamDirectory.Initialize();

            return await Task.Run<uint>(HandshakeWithSteam, guardian.Token);
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

        public async Task<CMsgDOTAMatch> DownloadMatchData(long matchId)
        {
            var guardian = new CancellationTokenSource(TimeSpan.FromSeconds(this.Timeout));

            Func<CMsgDOTAMatch> RequestMatchDetails = () =>
            {
                CMsgDOTAMatch matchDetails = null;

                // get the GC handler, which is used for messaging DOTA
                var gcHandler = this.SteamClient.GetHandler<SteamGameCoordinator>();

                // register a few callbacks we're interested in
                var cbManager = new CallbackManager(this.SteamClient);

                var sub = cbManager.Subscribe<SteamGameCoordinator.MessageCallback>((SteamGameCoordinator.MessageCallback callback) =>
                {
                    if (callback.EMsg == (uint)EDOTAGCMsg.k_EMsgGCMatchDetailsResponse)
                    {
                        Trace.TraceInformation("Dota: Match Data");

                        var msg = new ClientGCMsgProtobuf<CMsgGCMatchDetailsResponse>(callback.Message);
                        matchDetails = msg.Body.match;
                    }
                });

                // Send Request
                var request = new ClientGCMsgProtobuf<CMsgGCMatchDetailsRequest>((uint)EDOTAGCMsg.k_EMsgGCMatchDetailsRequest);
                request.Body.match_id = (ulong)matchId;
                gcHandler.Send(request, APPID);

                while (matchDetails == null)
                {
                    // in order for the callbacks to get routed, they need to be handled by the manager
                    cbManager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
                }

                return matchDetails;
            };

            return await Task.Run<CMsgDOTAMatch>(RequestMatchDetails, guardian.Token);
        }

        public async Task<byte[]> DownloadReplay(long matchId, int? cluster = null, int? salt = null)
        {
            if (!cluster.HasValue || !salt.HasValue)
            {
                var matchDetails = await DownloadMatchData(matchId);
                cluster = (int)matchDetails.cluster;
                salt = (int)matchDetails.replay_salt;
            }

            var data = await DownloadData((uint)cluster.GetValueOrDefault(), (ulong)matchId, (uint)salt.GetValueOrDefault(), "dem");
            return data;
        }

        public async Task<CDOTAMatchMetadata> DownloadMeta(long matchId, int? cluster = null, int? salt = null)
        {
            if(!cluster.HasValue || !salt.HasValue)
            {
                var matchDetails = await DownloadMatchData(matchId);
                cluster = (int)matchDetails.cluster;
                salt = (int)matchDetails.replay_salt;
            }
            
            var data = await DownloadData((uint)cluster.GetValueOrDefault(), (ulong)matchId, (uint)salt.GetValueOrDefault(), "meta");

            using (var steam = new MemoryStream(data))
            {
                var meta = ProtoBuf.Serializer.Deserialize<CDOTAMatchMetadataFile>(steam);
                return meta.metadata;
            }
        }

        private async Task<byte[]> DownloadData(uint cluster, ulong match_id, uint replay_salt, string type)
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
