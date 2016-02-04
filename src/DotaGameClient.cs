using Noemax.Compression;
using SteamKit2;
using SteamKit2.GC;
using SteamKit2.GC.Dota.Internal;
using SteamKit2.GC.Internal;
using SteamKit2.Internal;
using System;
using System.Collections.Generic;
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

		#endregion

		#region Constructor

		public DotaGameClient(string user, string password, byte[] sentry)
		{
			//this.GCMessageHandlers = new Dictionary<uint, Action<IPacketGCMsg>>();

			this.Username = user;
			this.Password = password;
			this.Sentry = CryptoHelper.SHAHash(sentry); //sha-1 hash it

			// create our steamclient instance
			this.SteamClient = new SteamClient();

		}

		#endregion

		#region Connect / Disconnect

		public Task<uint> Connect(bool autoReconect = true)
		{
			return Task.Run<uint>(() => {
				bool? completed = null;
				uint version = 0;

				// get the GC handler, which is used for messaging DOTA
				var gcHandler = this.SteamClient.GetHandler<SteamGameCoordinator>();

				// register a few callbacks we're interested in
				var cbManager = new CallbackManager(this.SteamClient);

				// these are registered upon creation to a callback manager, 
				// which will then route the callbacks to the functions specified
				cbManager.Subscribe<SteamClient.ConnectedCallback>((SteamClient.ConnectedCallback callback) => {
					if (callback.Result == EResult.OK)
					{
						// get the steamuser handler, which is used for logging on after successfully connecting
						var UserHandler = this.SteamClient.GetHandler<SteamUser>();
						UserHandler.LogOn(new SteamUser.LogOnDetails
						{
							Username = this.Username,
							Password = this.Password,
							SentryFileHash = this.Sentry,
						});
					}
					else
					{
						throw new Exception("Failed to Connect");
					}
				});

				cbManager.Subscribe<SteamClient.DisconnectedCallback>((SteamClient.DisconnectedCallback callback) => {
					// delay a little to give steam some time to finalize the DC
					Thread.Sleep(TimeSpan.FromSeconds(1));
					
					// reconect
					this.SteamClient.Connect();
				});

				cbManager.Subscribe<SteamUser.LoggedOnCallback>((SteamUser.LoggedOnCallback callback) => {
					if (callback.Result == EResult.OK)
					{
						// we've logged into the account
						// now we need to inform the steam server that we're playing dota (in order to receive GC messages)
						// steamkit doesn't expose the "play game" message through any handler, so we'll just send the message manually
						var gameMsg = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);
						gameMsg.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed
						{
							game_id = new GameID(APPID), // or game_id = APPID,
						});

						// send it off - notice here we're sending this message directly using the SteamClient
						this.SteamClient.Send(gameMsg);

						// delay a little to give steam some time to establish a GC connection to us
						Thread.Sleep(TimeSpan.FromSeconds(1));

						// inform the dota GC that we want a session
						var helloMsg = new ClientGCMsgProtobuf<CMsgClientHello>((uint)EGCBaseClientMsg.k_EMsgGCClientHello);
						helloMsg.Body.engine = ESourceEngine.k_ESE_Source2;
						gcHandler.Send(helloMsg, APPID);
					}
					else if (callback.Result == EResult.AccountLogonDenied)
					{
						throw new Exception(string.Format("Account {0}@{1} is denied.", this.Username, callback.EmailDomain));
					}
					else
					{
						throw new Exception("Failed to Login.");
					}
				});

				cbManager.Subscribe<SteamGameCoordinator.MessageCallback>((SteamGameCoordinator.MessageCallback callback) =>
				{
					if (callback.EMsg == (uint)EGCBaseClientMsg.k_EMsgGCClientWelcome)
					{
						var msg = new ClientGCMsgProtobuf<CMsgClientWelcome>(callback.Message);

						version = msg.Body.version;
						completed = true;
					}
					else
					{
						completed = false;
					}
				});

				// initiate the connection
				SteamClient.Connect();

				while(!completed.HasValue)
				{
					// in order for the callbacks to get routed, they need to be handled by the manager
					cbManager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
				}
				
				if(completed == false)
					throw new Exception("Failed to Connect");

				return version;
			});
		}

		public void Dispose()
		{
			SteamClient.Disconnect();
		}

		#endregion

		#region DOTA Functions

		public Task<byte[]> DownloadReplay(ulong matchId)
		{
			return Task.Run<byte[]>(() =>
			{
				CMsgDOTAMatch matchDetails = null;

				// get the GC handler, which is used for messaging DOTA
				var gcHandler = this.SteamClient.GetHandler<SteamGameCoordinator>();

				// register a few callbacks we're interested in
				var cbManager = new CallbackManager(this.SteamClient);
				
				cbManager.Subscribe<SteamGameCoordinator.MessageCallback>((SteamGameCoordinator.MessageCallback callback) =>
				{
					if (callback.EMsg == (uint)EDOTAGCMsg.k_EMsgGCMatchDetailsResponse)
					{
						var msg = new ClientGCMsgProtobuf<CMsgGCMatchDetailsResponse>(callback.Message);
						matchDetails = msg.Body.match;
					}
				});

				// Send Request
				var request = new ClientGCMsgProtobuf<CMsgGCMatchDetailsRequest>((uint)EDOTAGCMsg.k_EMsgGCMatchDetailsRequest);
				request.Body.match_id = matchId;
				gcHandler.Send(request, APPID);

				while (matchDetails == null)
				{
					// in order for the callbacks to get routed, they need to be handled by the manager
					cbManager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
				}

				if (matchDetails == null)
					throw new Exception("Failed to DownloadReplay");

				var url = string.Format("http://replay{0}.valve.net/{1}/{2}_{3}.dem.bz2", matchDetails.cluster, APPID, matchDetails.match_id, matchDetails.replay_salt);
				var webClient = new WebClient();
				var compressedMatchData = webClient.DownloadData(url);

				return CompressionFactory.BZip2.Decompress(compressedMatchData);
			});
		}

		#endregion
	}
}
