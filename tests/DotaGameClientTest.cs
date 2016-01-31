using SteamKit2.GC;
using SteamKit2.GC.Dota.Internal;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace HGV.Crystalys.Tests
{
	public class DotaGameClientTest
	{

		[Fact]
		public async Task ConnectToSteam()
		{
			var service = new HGV.Radiance.SteamUserService();
			var userInfo = await service.GetNextAvailable();

			using (var client = new DotaGameClient(userInfo.Username, userInfo.Password, userInfo.Sentry))
			{
				client.OnConnected += (o, e) => {
					if(e.Result)
						Trace.TraceInformation("Connected to Steam, Logging in '{0}'", userInfo.Username);
					else
						Trace.TraceError("Unable to connect to Steam");
				};
				client.OnDisconnected += (o, e) => {
					e.Reconnect = true;

					Trace.TraceInformation("Disconnected from Steam.");
				};
				client.OnLoggedOn += (o, e) => {
					if(e.Sucess)
						Trace.TraceInformation("Successfully logged on!");
					else
						Trace.TraceError("Unable to logon to Steam: {0}", e.Result);
				};
				client.OnAccountLogonDenied += (o, e) => {
					Trace.TraceInformation("Auth code sent to the email at {0}@{1} is required. Regenerate Sentry File Hash.", userInfo.Username, e.EmailDomain);
				};
				client.OnLoggedOff += (o, e) => {
					Trace.TraceInformation("Logged off of Steam");
				};
				client.OnClientWelcome += (o, e) => {
					Trace.TraceInformation("GC is welcoming us. Version: {0}", e.Message.version);
				};

				client.Connect();

				var data = client.DownloadReplay(2115905708);
				Assert.NotEqual(0, data.Length);
			}
        }
	}
}
