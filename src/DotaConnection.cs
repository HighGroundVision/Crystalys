using SteamKit2;
using SteamKit2.GC;
using SteamKit2.GC.Dota.Internal;
using SteamKit2.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace HGV.Crystalys
{
    public interface IDotaConnection
    {
        bool isConnected();
        Task<Stream> DownloadReplay(ulong matchId);
        Task<CDOTAMatchMetadata> DownloadMeta(ulong matchId);
    }

    public class DotaConnection : IDotaConnection
    {
        const int APPID = 570;

        protected readonly SteamClient steamClient;
        protected readonly HttpClient httpClient;
        private readonly SteamGameCoordinator cg;

        public DotaConnection(ISteamConnection connection, IHttpClientFactory factory)
        {
            this.steamClient = connection.GetClient();
            this.httpClient = factory.CreateClient();

            this.Connect();
        }

        public bool isConnected()
        {
            return this.steamClient.IsConnected;
        }

        public async Task<Stream> DownloadReplay(ulong matchId)
        {
            var match = GetMatch(matchId);
            if (match == null)
                throw new NullReferenceException($"Invalid Match: {matchId}");

            var data = await DownloadData(match.match_id, match.cluster, match.replay_salt, "dem");
            return data;
        }

        public async Task<CDOTAMatchMetadata> DownloadMeta(ulong matchId)
        {
            var match = GetMatch(matchId);
            if (match == null)
                throw new NullReferenceException($"Invalid Match: {matchId}");

            var stream = await DownloadData(match.match_id, match.cluster, match.replay_salt, "meta");
            var meta = ProtoBuf.Serializer.Deserialize<CDOTAMatchMetadataFile>(stream);
            if (match == null)
                throw new NullReferenceException($"Invalid Match: {matchId}");

            return meta.metadata;
        }

        private void Connect()
        {
            var playGame = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);

            playGame.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed
            {
                game_id = new GameID(APPID), // or game_id = APPID,
            });

            // notice here we're sending this message directly using the SteamClient
            steamClient.Send(playGame);

            // delay a little to give steam some time to establish a GC connection to us
            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(5));

            // inform the dota GC that we want a session
            var clientHello = new ClientGCMsgProtobuf<CMsgClientHello>((uint)EGCBaseClientMsg.k_EMsgGCClientHello);
            clientHello.Body.engine = ESourceEngine.k_ESE_Source2;
            this.cg.Send(clientHello, APPID);
        }

        private CMsgDOTAMatch GetMatch(ulong id, string type = "meta")
        {
            CMsgDOTAMatch match = null;

            var callbacks = new CallbackManager(this.steamClient);
            callbacks.Subscribe<SteamGameCoordinator.MessageCallback>(_ => {
                if (_.EMsg == (uint)EDOTAGCMsg.k_EMsgGCMatchDetailsResponse)
                {
                    var msg = new ClientGCMsgProtobuf<CMsgGCMatchDetailsResponse>(_.Message);
                    if (msg.Body.result == (uint)EResult.OK)
                    {
                        match = msg.Body.match;
                    }
                }
            });

            var requestMatch = new ClientGCMsgProtobuf<CMsgGCMatchDetailsRequest>((uint)EDOTAGCMsg.k_EMsgGCMatchDetailsRequest);
            requestMatch.Body.match_id = id;
            this.cg.Send(requestMatch, APPID);

            callbacks.RunWaitAllCallbacks(TimeSpan.FromSeconds(5));

            return match;
        }

        private async Task<Stream> DownloadData(ulong match_id, uint cluster, uint replay_salt, string type)
        {
            var url = string.Format("http://replay{0}.valve.net/{1}/{2}_{3}.{4}.bz2", cluster, APPID, match_id, replay_salt, type);
            return await httpClient.GetStreamAsync(url);
        }
    }
}
