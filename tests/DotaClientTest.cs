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
	public class DotaClientTest
	{
		private string Username = "Thantsking";
		private string Password = "aPhan3sah";

		[Fact]
		public void ConnectToSteam()
		{
			Assert.True(File.Exists("sentry.bin"));

			// if we have a saved sentry file, read 
			byte[] sentryFile = File.ReadAllBytes("sentry.bin");

			using (var client = new DotaClient(this.Username, this.Password, sentryFile))
			{
				client.OnConnected += (o, e) => {
					if(e.Result)
						Trace.TraceInformation("Connected to Steam, Logging in '{0}'", this.Username);
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
					Trace.TraceInformation("Auth code sent to the email at {0}@{1} is required. Regenerate Sentry File Hash.", this.Username, e.EmailDomain);
				};
				client.OnLoggedOff += (o, e) => {
					Trace.TraceInformation("Logged off of Steam");
				};
				client.OnClientWelcome += (o, e) => {
					Trace.TraceInformation("GC is welcoming us. Version: {0}", e.Message.version);
				};

				client.Connect(TimeSpan.FromMinutes(1));

				var data = client.DownloadReplay(1962101529, TimeSpan.FromMinutes(1));

				Assert.NotEqual(0, data.Length);
			}
        }
	}
}
