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
				await client.Connect();

				var data1 = await client.DownloadReplay(2115905708);
				Assert.NotEqual(0, data1.Length);
			}
        }
	}
}
