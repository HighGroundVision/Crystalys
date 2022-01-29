using ProtoBuf;
using SteamKit2;
using SteamKit2.GC;
using SteamKit2.GC.Dota.Internal;
using SteamKit2.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Timers;

namespace HGV.Crystalys
{
    /// <summary>
    ///     This handler handles all Dota 2 GC lobbies interaction.
    /// </summary>
    public sealed partial class DotaGameCoordinatorHandler : ClientMsgHandler
    {
        // private readonly Timer _gcConnectTimer;
        private readonly Timer _gcConnectTimer;
        private bool _running;

        public DotaGameCoordinatorHandler(SteamClient client)
            : this(client, Games.DOTA2, ESourceEngine.k_ESE_Source2)
        {
        }

        /// <summary>
        ///     Internally create an instance of the GC handler.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="appId"></param>
        /// <param name="_engine"></param>
        public DotaGameCoordinatorHandler(SteamClient client, Games appId, ESourceEngine _engine)
        {
            GameId = appId;
            Engine = _engine;
            SteamClient = client;

            EconItems = new Dictionary<ulong, CSOEconItem>(300); // Usually we'd have around 200-600 items.
            MapLocationStates = new Dictionary<int, CSODOTAMapLocationState>(2); // Generally this seems to be 2
            ChatChannels = new List<CMsgDOTAJoinChatChannelResponse>(1); // Lobby

            _gcConnectTimer = new Timer(30000);
            _gcConnectTimer.AutoReset = true;
            _gcConnectTimer.Elapsed += (s,e) => SayHello();
        }

        /// <summary>
        ///     The Game ID the handler will use. Defaults to Main Client.
        /// </summary>
        public Games GameId { get; }

        /// <summary>
        ///     The engine to use.
        /// </summary>
        public ESourceEngine Engine { get; }

        /// <summary>
        /// Is the GC ready?
        /// </summary>
        public bool Ready { get; private set; }
        
        /// <summary>
        /// The underlying SteamClient.
        /// </summary>
        public SteamClient SteamClient { get; private set; }

        /// <summary>
        /// The current up to date lobby
        /// </summary>
        /// <value>The lobby.</value>
        public CSODOTALobby Lobby { get; private set; }

        /// <summary>
        /// Econ items.
        /// </summary>
        public Dictionary<ulong, CSOEconItem> EconItems { get; }

        /// <summary>
        /// Ping map view states.
        /// </summary>
        public Dictionary<int, CSODOTAMapLocationState> MapLocationStates { get; }

        /// <summary>
        /// Contains various information about our player.
        /// </summary>
        public CSOEconGameAccountClient GameAccountClient { get; private set; }

        /// <summary>
        /// Active Chat Channels
        /// </summary>
        public List<CMsgDOTAJoinChatChannelResponse> ChatChannels { get; private set; }

        /// <summary>
        /// Sends a game coordinator message.
        /// </summary>
        /// <param name="msg">The GC message to send.</param>
        public void Send(IClientGCMsg msg)
        {
            var clientMsg = new ClientMsgProtobuf<CMsgGCClient>(EMsg.ClientToGC);

            clientMsg.Body.msgtype = MsgUtil.MakeGCMsg(msg.MsgType, msg.IsProto);
            clientMsg.Body.appid = (uint)GameId;

            clientMsg.Body.payload = msg.Serialize();

            Client.Send(clientMsg);
        }

        /// <summary>
        ///     Start playing DOTA 2 and automatically request a GC session.
        /// </summary>
        public async Task Start()
        {
            _running = true;

            var launchEvent = new ClientMsg<MsgClientAppUsageEvent>();
            launchEvent.Body.AppUsageEvent = EAppUsageEvent.GameLaunch;
            launchEvent.Body.GameID = new GameID { AppID = (uint)GameId, AppType = SteamKit2.GameID.GameType.App };
            Client.Send(launchEvent);

            await Task.Delay(TimeSpan.FromSeconds(1));

            var playGame = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayedWithDataBlob);
            playGame.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed
            {
                game_id = (ulong)GameId,
                game_extra_info = "Dota 2",
                game_data_blob = null,
                streaming_provider_id = 0,
                game_flags = (uint)Engine,
                owner_id = Client.SteamID.AccountID
            });
            playGame.Body.client_os_type = 16;
            Client.Send(playGame);

            await Task.Delay(TimeSpan.FromSeconds(1));

            SayHello();
        }

        private void SayHello()
        {
            if (Ready) return;

            var clientHello = new ClientGCMsgProtobuf<CMsgClientHello>((uint)EGCBaseClientMsg.k_EMsgGCClientHello);
            clientHello.Body.client_launcher = PartnerAccountType.PARTNER_NONE;
            clientHello.Body.engine = Engine;
            clientHello.Body.secret_key = "";
            clientHello.Body.client_session_need = 104;
            Send(clientHello);
        }

        /// <summary>
        /// Stop playing DOTA 2.
        /// </summary>
        public void Stop()
        {
            _running = false;
            //_gcConnectTimer.Change(0, Timeout.Infinite);

            var playGame = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);
            Client.Send(playGame);
        }


        /// <summary>
        /// Attempt to leave a lobby
        /// </summary>
        public void LeaveLobby()
        {
            var leaveLobby = new ClientGCMsgProtobuf<CMsgPracticeLobbyLeave>((uint)EDOTAGCMsg.k_EMsgGCPracticeLobbyLeave);
            Send(leaveLobby);
        }

        /// <summary>
        /// Attempt to destroy a lobby
        /// </summary>
        public void DestroyLobby()
        {
            if (this.Lobby is null)
                return;

            foreach (var memeber in this.Lobby.all_members)
            {
                var id = new SteamID(memeber.id);
                if(id != this.Client.SteamID)
                {
                    this.KickPlayerFromLobby(id.AccountID);
                }
            }

            this.LeaveLobby();
        }

        /// <summary>
        /// Respond to a ping()
        /// </summary>
        public void Pong()
        {
            var pingResponse = new ClientGCMsgProtobuf<CMsgGCClientPing>((uint)EGCBaseClientMsg.k_EMsgGCPingResponse);
            Send(pingResponse);
        }

        /// <summary>
        /// Requests a subscription refresh for a specific cache ID.
        /// </summary>
        /// <param name="type">the type of the cache</param>
        /// <param name="id">the cache soid</param>
        public void RequestSubscriptionRefresh(uint type, ulong id)
        {
            var refresh = new ClientGCMsgProtobuf<CMsgSOCacheSubscriptionRefresh>((uint)ESOMsg.k_ESOMsg_CacheSubscriptionRefresh);
            refresh.Body.owner_soid = new CMsgSOIDOwner
            {
                id = id,
                type = type
            };
            Send(refresh);
        }


        /// <summary>
        /// Start the game
        /// </summary>
        public void LaunchLobby()
        {
            Send(new ClientGCMsgProtobuf<CMsgPracticeLobbyLaunch>((uint)EDOTAGCMsg.k_EMsgGCPracticeLobbyLaunch));
        }

        /// <summary>
        ///  Create a practice or tournament or custom lobby.
        /// </summary
        /// <param name="details">Lobby options.</param>
        public void CreateLobby(CMsgPracticeLobbySetDetails details)
        {
            var create = new ClientGCMsgProtobuf<CMsgPracticeLobbyCreate>((uint)EDOTAGCMsg.k_EMsgGCPracticeLobbyCreate);
            create.Body.lobby_details = details;
            create.Body.pass_key = details.pass_key;
            if (string.IsNullOrWhiteSpace(create.Body.search_key))
                create.Body.search_key = "";

            Send(create);
        }


        /// <summary>
        /// Invite someone to the existing lobby.
        /// </summary>
        /// <remarks>You need an existing lobby for this to work.</remarks>
        /// <param name="steam_id">steam ID to invite</param>
        public void InviteToLobby(ulong steam_id)
        {
            {
                var invite = new ClientGCMsgProtobuf<CMsgInviteToLobby>((uint)EGCBaseMsg.k_EMsgGCInviteToLobby);
                invite.Body.steam_id = steam_id;
                Send(invite);
            }
            if (Lobby != null)
            {
                var invite = new ClientMsgProtobuf<CMsgClientInviteToGame>(EMsg.ClientInviteToGame);
                invite.Body.steam_id_dest = steam_id;
                invite.Body.connect_string = "+invite " + Lobby.lobby_id;
                if (Engine == ESourceEngine.k_ESE_Source2)
                    invite.Body.connect_string += " -launchsource2";

                Client.Send(invite);
            }
        }

        /// <summary>
        /// Kick a player from the lobby
        /// </summary>
        /// <param name="accountId">Account ID of player to kick</param>
        public void KickPlayerFromLobby(uint accountId)
        {
            var kick = new ClientGCMsgProtobuf<CMsgPracticeLobbyKick>((uint)EDOTAGCMsg.k_EMsgGCPracticeLobbyKick);
            kick.Body.account_id = accountId;
            Send(kick);
        }

        /// <summary>
        ///  Kick a player from the lobby team they're in.
        /// </summary>
        /// <param name="accountId">Account ID of player to kick</param>
        public void KickPlayerFromLobbyTeam(uint accountId)
        {
            var kick = new ClientGCMsgProtobuf<CMsgPracticeLobbyKickFromTeam>((uint)EDOTAGCMsg.k_EMsgGCPracticeLobbyKickFromTeam);
            kick.Body.account_id = accountId;
            Send(kick);
        }

        /// <summary>
        ///  Joins a chat channel. Note that limited Steam accounts cannot join chat channels.
        /// </summary>
        /// <param name="name">Name of the chat channel</param>
        /// <param name="type">Type of the chat channel</param>
        public void JoinChatChannel(string name, DOTAChatChannelType_t type = DOTAChatChannelType_t.DOTAChannelType_Custom)
        {
            var joinChannel = new ClientGCMsgProtobuf<CMsgDOTAJoinChatChannel>((uint)EDOTAGCMsg.k_EMsgGCJoinChatChannel);
            joinChannel.Body.channel_name = name;
            joinChannel.Body.channel_type = type;
            Send(joinChannel);
        }

        /// <summary>
        /// Request a list of public chat channels from the GC.
        /// </summary>
        public void RequestChatChannelList()
        {
            Send(new ClientGCMsgProtobuf<CMsgDOTARequestChatChannelList>((uint)EDOTAGCMsg.k_EMsgGCRequestChatChannelList));
        }

        /// <summary>
        /// Request a match result
        /// </summary>
        /// <param name="matchId">Match id</param>
        public void RequestMatchResult(ulong matchId)
        {
            var requestMatch = new ClientGCMsgProtobuf<CMsgGCMatchDetailsRequest>((uint)EDOTAGCMsg.k_EMsgGCMatchDetailsRequest);            
            requestMatch.Body.match_id = matchId;
            Send(requestMatch);
        }

        /// <summary>
        /// Sends a message in a chat channel.
        /// </summary>
        /// <param name="channelid">Id of channel to join.</param>
        /// <param name="message">Message to send.</param>
        public void SendChannelMessage(ulong channelid, string message)
        {
            var chatMsg = new ClientGCMsgProtobuf<CMsgDOTAChatMessage>((uint)EDOTAGCMsg.k_EMsgGCChatMessage);
            chatMsg.Body.channel_id = channelid;
            chatMsg.Body.text = message;
            Send(chatMsg);
        }

        /// <summary>
        /// Leaves chat channel
        /// </summary>
        /// <param name="channelid">id of channel to leave</param>
        public void LeaveChatChannel(ulong channelid)
        {
            var leaveChannel = new ClientGCMsgProtobuf<CMsgDOTALeaveChatChannel>((uint)EDOTAGCMsg.k_EMsgGCLeaveChatChannel);
            leaveChannel.Body.channel_id = channelid;
            Send(leaveChannel);

            ChatChannels.RemoveAll(_ => _.channel_id == channelid);
        }

        /// <summary>
        /// Requests a lobby list with an optional password
        /// </summary>
        /// <param name="passKey">Pass key.</param>
        /// <param name="tournament"> Tournament games? </param>
        public void PracticeLobbyList(string passKey = null)
        {
            var list = new ClientGCMsgProtobuf<CMsgPracticeLobbyList>((uint)EDOTAGCMsg.k_EMsgGCPracticeLobbyList);
            list.Body.pass_key = passKey;
            Send(list);
        }

        /// <summary>
        /// Shuffle the current lobby
        /// </summary>
        public void PracticeLobbyShuffle()
        {
            var shuffle = new ClientGCMsgProtobuf<CMsgBalancedShuffleLobby>((uint)EDOTAGCMsg.k_EMsgGCBalancedShuffleLobby);
            Send(shuffle);
        }

        /// <summary>
        /// Flip the teams in the current lobby
        /// </summary>
        public void PracticeLobbyFlip()
        {
            var flip = new ClientGCMsgProtobuf<CMsgFlipLobbyTeams>((uint)EDOTAGCMsg.k_EMsgGCFlipLobbyTeams);
            Send(flip);
        }


        /// <summary>
        /// Packet GC message.
        /// </summary>
        /// <param name="eMsg"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        private static IPacketGCMsg GetPacketGCMsg(uint eMsg, byte[] data)
        {
            // strip off the protobuf flag
            var realEMsg = MsgUtil.GetGCMsg(eMsg);

            if (MsgUtil.IsProtoBuf(eMsg))
            {
                return new PacketClientGCMsgProtobuf(realEMsg, data);
            }
            else
            {
                return new PacketClientGCMsg(realEMsg, data);
            }
        }

        /// <summary>
        /// Handles a client message. This should not be called directly.
        /// </summary>
        /// <param name="packetMsg">The packet message that contains the data.</param>
        public override void HandleMsg(IPacketMsg packetMsg)
        {
            if (packetMsg.MsgType == EMsg.ClientFromGC)
            {
                var msg = new ClientMsgProtobuf<CMsgGCClient>(packetMsg);
                if (msg.Body.appid == (uint)GameId)
                {
                    var gcmsg = GetPacketGCMsg(msg.Body.msgtype, msg.Body.payload);
                    var messageMap = new Dictionary<uint, Action<IPacketGCMsg>>
                    {
                        {(uint) EGCBaseClientMsg.k_EMsgGCClientWelcome, HandleWelcome},
                        {(uint) EGCBaseClientMsg.k_EMsgGCPingRequest, HandlePingRequest},
                        {(uint) EGCBaseClientMsg.k_EMsgGCClientConnectionStatus, HandleConnectionStatus},
                        {(uint) ESOMsg.k_ESOMsg_UpdateMultiple, HandleUpdateMultiple},
                        {(uint) ESOMsg.k_ESOMsg_CacheSubscribed, HandleCacheSubscribed},
                        {(uint) ESOMsg.k_ESOMsg_CacheUnsubscribed, HandleCacheUnsubscribed},
                        {(uint) ESOMsg.k_ESOMsg_Destroy, HandleCacheDestroy},
                        {(uint) EDOTAGCMsg.k_EMsgGCPracticeLobbyListResponse, HandlePracticeLobbyListResponse},
                        {(uint) EDOTAGCMsg.k_EMsgGCJoinChatChannelResponse, HandleJoinChatChannelResponse},
                        {(uint) EDOTAGCMsg.k_EMsgGCRequestChatChannelListResponse, HandleChatChannelListResponse},
                        {(uint) EDOTAGCMsg.k_EMsgGCChatMessage, HandleChatMessage},
                        {(uint) EDOTAGCMsg.k_EMsgGCOtherJoinedChannel, HandleOtherJoinedChannel},
                        {(uint) EDOTAGCMsg.k_EMsgGCOtherLeftChannel, HandleOtherLeftChannel},
                        {(uint) EDOTAGCMsg.k_EMsgGCMatchDetailsResponse, HandleMatchDetailsResponse},  
                    };
                    Action<IPacketGCMsg> func;
                    if (!messageMap.TryGetValue(gcmsg.MsgType, out func))
                    {
                        Client.PostCallback(new UnhandledDotaGCCallback(gcmsg));
                        return;
                    }

                    func(gcmsg);
                }
            }
            else
            {
                switch (packetMsg.MsgType)
                {
                    case EMsg.ClientAuthListAck:
                        {
                            var msg = new ClientMsgProtobuf<CMsgClientAuthListAck>(packetMsg);
                            Client.PostCallback(new AuthListAck(msg.Body));
                        }
                        break;
                    case EMsg.ClientOGSBeginSessionResponse:
                        {
                            var msg = new ClientMsg<MsgClientOGSBeginSessionResponse>(packetMsg);
                            Client.PostCallback(new BeginSessionResponse(msg.Body));
                        }
                        break;
                    case EMsg.ClientRichPresenceInfo:
                        {
                            var msg = new ClientMsgProtobuf<CMsgClientRichPresenceInfo>(packetMsg);
                            Client.PostCallback(new RichPresenceUpdate(msg.Body));
                        }
                        break;
                }
            }
        }


        private void HandleCacheSubscribed(IPacketGCMsg obj)
        {
            var sub = new ClientGCMsgProtobuf<CMsgSOCacheSubscribed>(obj);
            foreach (var cache in sub.Body.objects)
            {
                HandleSubscribedType(cache);
            }
        }

        /// <summary>
        /// Handle various cache subscription types.
        /// </summary>
        /// <param name="cache"></param>
        private void HandleSubscribedType(CMsgSOCacheSubscribed.SubscribedType cache)
        {
            switch ((CSOTypes)cache.type_id)
            {
                case CSOTypes.ECON_ITEM:
                    HandleEconItemsSnapshot(cache.object_data);
                    break;
                case CSOTypes.ECON_GAME_ACCOUNT_CLIENT:
                    HandleGameAccountClientSnapshot(cache.object_data[0]);
                    break;
                case CSOTypes.MAP_LOCATION_STATE:
                    HandleMapLocationsSnapshot(cache.object_data);
                    break;
                case CSOTypes.LOBBY:
                    HandleLobbySnapshot(cache.object_data[0]);
                    break;
            }
        }

        /// <summary>
        /// Handle when a cache is destroyed.
        /// </summary>
        /// <param name="obj">Message</param>
        public void HandleCacheDestroy(IPacketGCMsg obj)
        {
            var dest = new ClientGCMsgProtobuf<CMsgSOSingleObject>(obj);
            if (Lobby != null && dest.Body.type_id == (int)CSOTypes.LOBBY)
            {
                Lobby = null;
                Client.PostCallback(new PracticeLobbyLeave(null));
            }
        }

        private void HandleCacheUnsubscribed(IPacketGCMsg obj)
        {
            var unSub = new ClientGCMsgProtobuf<CMsgSOCacheUnsubscribed>(obj);
            if (Lobby != null && unSub.Body.owner_soid.id == Lobby.lobby_id)
            {
                Lobby = null;
                Client.PostCallback(new PracticeLobbyLeave(unSub.Body));
            }
            else
            {
                Client.PostCallback(new CacheUnsubscribed(unSub.Body));
            }
        }

        private void HandleMapLocationsSnapshot(IEnumerable<byte[]> items)
        {
            foreach (var bitem in items)
            {
                using (var stream = new MemoryStream(bitem))
                {
                    var item = Serializer.Deserialize<CSODOTAMapLocationState>(stream);
                    MapLocationStates[item.location_id] = item;
                }
            }
        }

        private void HandleEconItemsSnapshot(IEnumerable<byte[]> items)
        {
            foreach (var bitem in items)
            {
                using (var stream = new MemoryStream(bitem))
                {
                    var item = Serializer.Deserialize<CSOEconItem>(stream);
                    EconItems[item.id] = item;
                }
            }
        }

        private void HandleLobbySnapshot(byte[] data)
        {
            using (var stream = new MemoryStream(data))
            {
                var inital = Lobby is null;
                var lobby = Serializer.Deserialize<CSODOTALobby>(stream);
                Lobby = lobby;

                if(inital) Client.PostCallback(new PracticeLobbyCreated(lobby));

                Client.PostCallback(new PracticeLobbySnapshot(lobby));
            }
        }

        private void HandleGameAccountClientSnapshot(byte[] data)
        {
            using (var stream = new MemoryStream(data))
            {
                GameAccountClient = Serializer.Deserialize<CSOEconGameAccountClient>(stream);
                Client.PostCallback(new GameAccountClientSnapshot(GameAccountClient));
            }
        }

        private void HandlePracticeLobbyListResponse(IPacketGCMsg obj)
        {
            var resp = new ClientGCMsgProtobuf<CMsgPracticeLobbyListResponse>(obj);
            Client.PostCallback(new PracticeLobbyListResponse(resp.Body));
        }

        private void HandlePingRequest(IPacketGCMsg obj)
        {
            var req = new ClientGCMsgProtobuf<CMsgGCClientPing>(obj);
            Pong();
            Client.PostCallback(new PingRequest(req.Body));
        }

        private void HandleJoinChatChannelResponse(IPacketGCMsg obj)
        {
            var resp = new ClientGCMsgProtobuf<CMsgDOTAJoinChatChannelResponse>(obj);

            ChatChannels.Add(resp.Body);

            Client.PostCallback(new JoinChatChannelResponse(resp.Body));
        }

        private void HandleChatChannelListResponse(IPacketGCMsg obj)
        {
            var resp = new ClientGCMsgProtobuf<CMsgDOTARequestChatChannelListResponse>(obj);
            Client.PostCallback(new ChatChannelListResponse(resp.Body));
        }

        private void HandleChatMessage(IPacketGCMsg obj)
        {
            var resp = new ClientGCMsgProtobuf<CMsgDOTAChatMessage>(obj);
            Client.PostCallback(new ChatMessage(resp.Body));
        }

        private void HandleMatchDetailsResponse(IPacketGCMsg obj)
        {
            var resp = new ClientGCMsgProtobuf<CMsgGCMatchDetailsResponse>(obj);
            Client.PostCallback(new MatchResultResponse(resp.Body));
        }

        private void HandleConnectionStatus(IPacketGCMsg obj)
        {
            if(_running)
            {
                var resp = new ClientGCMsgProtobuf<CMsgConnectionStatus>(obj);

                Ready = resp.Body.status == GCConnectionStatus.GCConnectionStatus_HAVE_SESSION;

                Client.PostCallback(new ConnectionStatus(resp.Body));

                if (resp.Body.status != GCConnectionStatus.GCConnectionStatus_HAVE_SESSION)
                {
                    _gcConnectTimer.Start();
                }                
            }
            else
            {
                Stop();
            }
        }

        private void HandleOtherJoinedChannel(IPacketGCMsg obj)
        {
            var resp = new ClientGCMsgProtobuf<CMsgDOTAOtherJoinedChatChannel>(obj);
            Client.PostCallback(new OtherJoinedChannel(resp.Body));
        }

        private void HandleOtherLeftChannel(IPacketGCMsg obj)
        {
            var resp = new ClientGCMsgProtobuf<CMsgDOTAOtherLeftChatChannel>(obj);
            Client.PostCallback(new OtherLeftChannel(resp.Body));
        }

        private void HandleUpdateMultiple(IPacketGCMsg obj)
        {
            var resp = new ClientGCMsgProtobuf<CMsgSOMultipleObjects>(obj);
            var handled = true;
            foreach (var mObj in resp.Body.objects_modified)
            {
                if (mObj.type_id == (int)CSOTypes.LOBBY)
                {
                    HandleLobbySnapshot(mObj.object_data);
                }
                else
                {
                    handled = false;
                }
            }
            if (!handled)
            {
                Client.PostCallback(new UnhandledDotaGCCallback(obj));
            }
        }

        //Initial message sent when connected to the GC
        private void HandleWelcome(IPacketGCMsg msg)
        {
            Ready = true;

            // Clear these; They will be updated in the subscriptions if they exist still.
            Lobby = null;

            var wel = new ClientGCMsgProtobuf<CMsgClientWelcome>(msg);
            Client.PostCallback(new GCWelcomeCallback(wel.Body));

            //Handle any cache subscriptions
            foreach (var cache in wel.Body.outofdate_subscribed_caches)
                foreach (var obj in cache.objects)
                    HandleSubscribedType(obj);
        }
    }

    /// <summary>
    /// Potential game IDs usable by DOTA GC handler.
    /// </summary>
    public enum Games : uint
    {
        /// <summary>
        /// Main DOTA 2 client.
        /// </summary>
        DOTA2 = 570,

        /// <summary>
        /// DOTA 2 test.
        /// </summary>
        DOTA2TEST = 205790
    }

    /// <summary>
    /// Cache types
    /// </summary>
    internal enum CSOTypes : int
    {
        /// <summary>
        /// An economy item.
        /// </summary>
        ECON_ITEM = 1,

        /// <summary>
        /// An econ item recipe.
        /// </summary>
        ITEM_RECIPE = 5,

        /// <summary>
        /// Game account client for Econ.
        /// </summary>
        ECON_GAME_ACCOUNT_CLIENT = 7,

        /// <summary>
        /// Selected item preset.
        /// </summary>
        SELECTED_ITEM_PRESET = 35,

        /// <summary>
        /// Item preset instance.
        /// </summary>
        ITEM_PRESET_INSTANCE = 36,

        /// <summary>
        /// Active drop rate bonus.
        /// </summary>
        DROP_RATE_BONUS = 38,

        /// <summary>
        /// Pass to view a league.
        /// </summary>
        LEAGUE_VIEW_PASS = 39,

        /// <summary>
        /// Event ticket.
        /// </summary>
        EVENT_TICKET = 40,

        /// <summary>
        /// Item tournament passport.
        /// </summary>
        ITEM_TOURNAMENT_PASSPORT = 42,

        /// <summary>
        /// DOTA 2 game account client.
        /// </summary>
        GAME_ACCOUNT_CLIENT = 2002,

        /// <summary>
        /// A Dota 2 party.
        /// </summary>
        PARTY = 2003,

        /// <summary>
        /// A Dota 2 lobby.
        /// </summary>
        LOBBY = 2004,

        /// <summary>
        /// A party invite.
        /// </summary>
        PARTYINVITE = 2006,

        /// <summary>
        /// Game hero favorites.
        /// </summary>
        GAME_HERO_FAVORITES = 2007,

        /// <summary>
        /// Ping map location state.
        /// </summary>
        MAP_LOCATION_STATE = 2008,

        /// <summary>
        /// Tournament.
        /// </summary>
        TOURNAMENT = 2009,

        /// <summary>
        /// A player challenge.
        /// </summary>
        PLAYER_CHALLENGE = 2010,

        /// <summary>
        /// A lobby invite, introduced in Reborn.
        /// </summary>
        LOBBYINVITE = 2011
    }
}
