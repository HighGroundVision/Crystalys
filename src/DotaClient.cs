using System;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.IO;
using System.Net;

using SteamKit2;
using SteamKit2.Internal;           // brings in our protobuf client messages
using SteamKit2.GC;                 // brings in the GC related classes
using SteamKit2.GC.Internal;        // brings the base GC protobuf messages
using SteamKit2.GC.Dota;            // brings in dota specific things
using SteamKit2.GC.Dota.Internal;   // brings in dota specific protobuf messages

namespace HGV.Crystalys
{
    public class DotaClient
    {
        #region Properties

        SteamClient client;

        SteamUser user;
        SteamGameCoordinator gameCoordinator;

        CallbackManager callbackMgr;

        string userName;
        string password;

        bool? connected = null;

        // dota2's appid
        const int APPID = 570;

        private ConcurrentDictionary<ulong, CMsgDOTAMatch> matches { get; set; }

        #endregion

        #region Constructor

        public DotaClient(string userName, string password)
        {
            this.matches = new ConcurrentDictionary<ulong, CMsgDOTAMatch>();

            this.userName = userName;
            this.password = password;

            this.client = new SteamClient();

            // get our handlers
            this.user = client.GetHandler<SteamUser>();
            this.gameCoordinator = client.GetHandler<SteamGameCoordinator>();

            // setup callbacks
            this.callbackMgr = new CallbackManager(client);
            this.callbackMgr.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
            this.callbackMgr.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
            this.callbackMgr.Subscribe<SteamGameCoordinator.MessageCallback>(OnGCMessage);
        }

        #endregion

        #region Events

        // called when the client successfully (or unsuccessfully) connects to steam
        void OnConnected(SteamClient.ConnectedCallback callback)
        {
            Trace.TraceInformation("Connected! Logging '{0}' into Steam...", userName);

            // we've successfully connected, so now attempt to logon
            user.LogOn(new SteamUser.LogOnDetails
            {
                Username = userName,
                Password = password,
            });
        }

        // called when the client successfully (or unsuccessfully) logs onto an account
        void OnLoggedOn(SteamUser.LoggedOnCallback callback)
        {
            if (callback.Result != EResult.OK)
            {
                // logon failed (password incorrect, steamguard enabled, etc)
                // an EResult of AccountLogonDenied means the account has SteamGuard enabled and an email containing the authcode was sent
                // in that case, you would get the auth code from the email and provide it in the LogOnDetails

                Trace.TraceInformation("Unable to logon to Steam: {0}", callback.Result);

                this.connected = false; // we didn't actually get the match details, but we need to jump out of the callback loop
                return;
            }

            Trace.TraceInformation("Logged in! Launching DOTA...");

            // we've logged into the account
            // now we need to inform the steam server that we're playing dota (in order to receive GC messages)

            // steamkit doesn't expose the "play game" message through any handler, so we'll just send the message manually
            var playGame = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);

            playGame.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed
            {
                game_id = new GameID(APPID), // or game_id = APPID,
            });

            // send it off
            // notice here we're sending this message directly using the SteamClient
            client.Send(playGame);

            // delay a little to give steam some time to establish a GC connection to us
            Thread.Sleep(5000);

            // inform the dota GC that we want a session
            var clientHello = new ClientGCMsgProtobuf<CMsgClientHello>((uint)EGCBaseClientMsg.k_EMsgGCClientHello);
            clientHello.Body.engine = ESourceEngine.k_ESE_Source2;
            gameCoordinator.Send(clientHello, APPID);
        }

        // called when a gamecoordinator (GC) message arrives
        // these kinds of messages are designed to be game-specific
        // in this case, we'll be handling dota's GC messages
        void OnGCMessage(SteamGameCoordinator.MessageCallback callback)
        {
            // setup our dispatch table for messages
            // this makes the code cleaner and easier to maintain
            var messageMap = new Dictionary<uint, Action<IPacketGCMsg>>
            {
                { ( uint )EGCBaseClientMsg.k_EMsgGCClientWelcome, OnClientWelcome },
                { ( uint )EDOTAGCMsg.k_EMsgGCMatchDetailsResponse, OnMatchDetails },
            };

            Action<IPacketGCMsg> func;
            if (!messageMap.TryGetValue(callback.EMsg, out func))
            {
                // this will happen when we recieve some GC messages that we're not handling
                // this is okay because we're handling every essential message, and the rest can be ignored
                return;
            }

            func(callback.Message);
        }

        // this message arrives when the GC welcomes a client
        // this happens after telling steam that we launched dota (with the ClientGamesPlayed message)
        // this can also happen after the GC has restarted (due to a crash or new version)
        void OnClientWelcome(IPacketGCMsg packetMsg)
        {
            // in order to get at the contents of the message, we need to create a ClientGCMsgProtobuf from the packet message we recieve
            // note here the difference between ClientGCMsgProtobuf and the ClientMsgProtobuf used when sending ClientGamesPlayed
            // this message is used for the GC, while the other is used for general steam messages
            var msg = new ClientGCMsgProtobuf<CMsgClientWelcome>(packetMsg);

            Trace.TraceInformation("GC is welcoming us. Version: {0}", msg.Body.version);

            // at this point, the GC is now ready to accept messages from us
            this.connected = true;
        }

        // this message arrives after we've requested the details for a match
        void OnMatchDetails(IPacketGCMsg packetMsg)
        {
            var msg = new ClientGCMsgProtobuf<CMsgGCMatchDetailsResponse>(packetMsg);

            EResult result = (EResult)msg.Body.result;
            if (result == EResult.OK)
            {
                var match = msg.Body.match;
                this.matches.TryAdd(match.match_id, match);
            }
            else
            {

            }
        }

        #endregion

        #region Connect

        public void Connect()
        {
            this.connected = null;

            Trace.TraceInformation("Connecting to Steam...");

            var guardian = new CancellationTokenSource(TimeSpan.FromSeconds(60));

            // begin the connection to steam
            client.Connect();

            while (this.connected.HasValue == false)
            {
                guardian.Token.ThrowIfCancellationRequested();

                callbackMgr.RunWaitCallbacks(TimeSpan.FromSeconds(1));
            }

            if (this.connected == false)
            {
                client.Disconnect();

                throw new Exception("Error Connecting to Steam");
            }
        }

        #endregion

        #region Dota Functions

        public CMsgDOTAMatch DownloadMatchData(ulong matchId)
        {
            Trace.TraceInformation("Requesting details of match {0}", matchId);

            var requestMatch = new ClientGCMsgProtobuf<CMsgGCMatchDetailsRequest>((uint)EDOTAGCMsg.k_EMsgGCMatchDetailsRequest);
            requestMatch.Body.match_id = matchId;
            gameCoordinator.Send(requestMatch, APPID);

            var guardian = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            while (this.matches.ContainsKey(matchId) == false)
            {
                guardian.Token.ThrowIfCancellationRequested();

                callbackMgr.RunWaitCallbacks(TimeSpan.FromSeconds(1));
            }

            CMsgDOTAMatch match = null;

            this.matches.TryRemove(matchId, out match);

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
                return compressedMatchData;

                //ICSharpCode.SharpZipLib.BZip2.BZip2.Decompress()
                //return CompressionFactory.BZip2.Decompress(compressedMatchData);
            }
        }

        #endregion
    }
}
