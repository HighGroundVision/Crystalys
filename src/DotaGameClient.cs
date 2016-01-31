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

		private SteamClient Client { get; set; }
		private CallbackManager CBManager { get; set; }
		private SteamGameCoordinator GCHandler {get; set;}
		private SteamUser UserHandler {get; set;}

		private Dictionary<uint, Action<IPacketGCMsg>> GCMessageHandlers { get; set; }

		private bool Connected = false;
		private bool Waiting = true;

		private string Username { get; set; }
		private string Password { get; set; }
		private byte[] Sentry { get; set; }

		#endregion

		#region Constructor

		public DotaGameClient(string user, string password, byte[] sentry)
		{
			this.GCMessageHandlers = new Dictionary<uint, Action<IPacketGCMsg>>();

			this.Username = user;
			this.Password = password;
			this.Sentry = CryptoHelper.SHAHash(sentry); //sha-1 hash it

			// create our steamclient instance
			this.Client = new SteamClient();
			// create the callback manager which will route callbacks to function calls
			this.CBManager = new CallbackManager(Client);

			// get the steamuser handler, which is used for logging on after successfully connecting
			this.UserHandler = Client.GetHandler<SteamUser>();

			// get the GC handler, which is used for messaging DOTA
			this.GCHandler = this.Client.GetHandler<SteamGameCoordinator>();

			// register a few callbacks we're interested in
			// these are registered upon creation to a callback manager, 
			// which will then route the callbacks to the functions specified
			this.CBManager.Subscribe<SteamClient.ConnectedCallback>(ConnectedCallback);
			this.CBManager.Subscribe<SteamClient.DisconnectedCallback>(DisconnectedCallback);

			this.CBManager.Subscribe<SteamUser.LoggedOnCallback>(LoggedOnCallback);
			this.CBManager.Subscribe<SteamUser.LoggedOffCallback>(LoggedOffCallback);

			this.CBManager.Subscribe<SteamGameCoordinator.MessageCallback>(GCMessageCallback);
		}

		#endregion

		#region Connect / Disconnect

		public void Connect()
		{
			this.Connect(TimeSpan.FromSeconds(60));
		}

		public void Connect(TimeSpan timeout)
		{
			// initiate the connection
			Client.Connect();

			var result = TimeoutHandler.RetryUntilSuccessOrTimeout(timeout, () =>
			{
				// in order for the callbacks to get routed, they need to be handled by the manager
				this.CBManager.RunWaitCallbacks(TimeSpan.FromSeconds(1));

				return this.Connected;
			});
			if (result == false)
				throw new TimeoutException("Failed to Connect");
		}

		public void Dispose()
		{
			Client.Disconnect();
		}

		#endregion

		#region Events

		public event ConnectedCallbackHandler OnConnected;
		public delegate void ConnectedCallbackHandler(DotaGameClient obj, ConnectedEventArgs e);

		void ConnectedCallback(SteamClient.ConnectedCallback callback)
		{
			if (callback.Result == EResult.OK)
			{
				if (this.OnConnected != null)
					this.OnConnected(this, new ConnectedEventArgs() { Result = true });

				this.UserHandler.LogOn(new SteamUser.LogOnDetails
				{
					Username = this.Username,
					Password = this.Password,
					SentryFileHash = this.Sentry,
				});
			}
			else
			{
				if (this.OnConnected != null)
					this.OnConnected(this, new ConnectedEventArgs() { Result = false });
			}
		}

		public event DisconnectedCallbackHandler OnDisconnected;
		public delegate void DisconnectedCallbackHandler(DotaGameClient obj, DisconnectedEventArgs e);

		void DisconnectedCallback(SteamClient.DisconnectedCallback callback)
		{
			if (this.OnDisconnected != null)
			{
				var args = new DisconnectedEventArgs();
				this.OnDisconnected(this, args);
				if(args.Reconnect == true)
				{
					Thread.Sleep(TimeSpan.FromSeconds(1));
					this.Client.Connect();
				}

			}
		}

		public event LoggedOnbackHandler OnLoggedOn;
		public delegate void LoggedOnbackHandler(DotaGameClient obj, LoggedOnEventArgs e);

		public event AccountLogonDeniedHandler OnAccountLogonDenied;
		public delegate void AccountLogonDeniedHandler(DotaGameClient obj, AccountLogonDeniedEventArgs e);

		void LoggedOnCallback(SteamUser.LoggedOnCallback callback)
		{
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
				this.Client.Send(gameMsg);

				// delay a little to give steam some time to establish a GC connection to us
				Thread.Sleep(TimeSpan.FromSeconds(1));

				// inform the dota GC that we want a session
				var helloMsg = new ClientGCMsgProtobuf<CMsgClientHello>((uint)EGCBaseClientMsg.k_EMsgGCClientHello);
				helloMsg.Body.engine = ESourceEngine.k_ESE_Source2;
				this.GCHandler.Send(helloMsg, APPID);

				if (this.OnLoggedOn != null)
					this.OnLoggedOn(this, new LoggedOnEventArgs() { Sucess = true, Result = callback.Result });
			}
			else if (callback.Result == EResult.AccountLogonDenied)
			{
				if (this.OnAccountLogonDenied != null)
					this.OnAccountLogonDenied(this, new AccountLogonDeniedEventArgs() { EmailDomain = callback.EmailDomain });
			}
			else
			{
				if (this.OnLoggedOn != null)
					this.OnLoggedOn(this, new LoggedOnEventArgs() { Sucess = false, Result = callback.Result });
			}
		}

		public event LoggedOffHandler OnLoggedOff;
		public delegate void LoggedOffHandler(DotaGameClient obj, EventArgs e);

		void LoggedOffCallback(SteamUser.LoggedOffCallback callback)
		{
			if (this.OnLoggedOff != null)
				this.OnLoggedOff(this, new EventArgs());
		}

		void GCMessageCallback(SteamGameCoordinator.MessageCallback callback)
		{
			if (callback.EMsg == (uint)EGCBaseClientMsg.k_EMsgGCClientWelcome)
			{
				this.ClientWelcomeCallback(callback.Message);
			}
			else
			{
				Action<IPacketGCMsg> func;
				if (this.GCMessageHandlers.TryGetValue(callback.EMsg, out func) == true)
				{
					func(callback.Message);
				}
			}
		}

		public event ClientWelcomeHandler OnClientWelcome;
		public delegate void ClientWelcomeHandler(DotaGameClient obj, ClientWelcomeEventArgs e);

		void ClientWelcomeCallback(IPacketGCMsg packet)
		{
			var msg = new ClientGCMsgProtobuf<CMsgClientWelcome>(packet);
			
			if (this.OnClientWelcome != null)
				this.OnClientWelcome(this, new ClientWelcomeEventArgs() { Message = msg.Body });

			this.Connected = true;
		}

		#endregion

		#region DOTA Functions

		public byte[] DownloadReplay(long matchId)
		{
			return DownloadReplay(matchId, TimeSpan.FromSeconds(60));
		}

		public byte[] DownloadReplay(long matchId, TimeSpan timeout)
		{
			CMsgDOTAMatch matchDetails = null;

			// Add Handler for request's reponse
			this.GCMessageHandlers.Add((uint)EDOTAGCMsg.k_EMsgGCMatchDetailsResponse, (packet) => {
				var msg = new ClientGCMsgProtobuf<CMsgGCMatchDetailsResponse>(packet);

				if ((EResult)msg.Body.result == EResult.OK)
				{
					matchDetails = msg.Body.match;
				}

				this.Waiting = false;
			});

			// Send Request
			var request = new ClientGCMsgProtobuf<CMsgGCMatchDetailsRequest>((uint)EDOTAGCMsg.k_EMsgGCMatchDetailsRequest);
			request.Body.match_id = (ulong)matchId;
			this.GCHandler.Send(request, APPID);

			// Wait for handler or timeout
			var result = TimeoutHandler.RetryUntilSuccessOrTimeout(timeout, () =>
			{
				// in order for the callbacks to get routed, they need to be handled by the manager
				this.CBManager.RunWaitCallbacks(TimeSpan.FromSeconds(1));

				return !this.Waiting;
			});
			if (result == false)
				throw new TimeoutException("Failed to DownloadReplay");

			if (matchDetails == null)
				throw new ArgumentNullException(nameof(matchDetails));

			var url = string.Format("http://replay{0}.valve.net/{1}/{2}_{3}.dem.bz2", matchDetails.cluster, APPID, matchDetails.match_id, matchDetails.replay_salt);
			var webClient = new WebClient();
			var compressedMatchData = webClient.DownloadData(url);

			return CompressionFactory.BZip2.Decompress(compressedMatchData);
		}

		#endregion
	}
}
