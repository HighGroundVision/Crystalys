using SteamKit2.GC;
using SteamKit2.GC.Dota.Internal;
using System;
using System.Collections.Generic;
using System.Configuration;
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
        private UserInfo GetUserInfo()
        {
            return new UserInfo() {
                Username = ConfigurationManager.AppSettings["Steam:Username"],
                Password = ConfigurationManager.AppSettings["Steam:Password"] 
            };
        }

        private long GetMatchId()
        {
            var value = ConfigurationManager.AppSettings["Dota:MatchId"];
            return long.Parse(value);
        }

		[Fact]
		public async Task ConnectToSteam()
		{
            var userInfo = this.GetUserInfo();

            using (var client = new DotaGameClient())
			{
                await client.Connect(userInfo.Username, userInfo.Password);
            }
        }

        [Fact]
        public async Task DownloadMatchData()
        {
            var userInfo = this.GetUserInfo();
            var matchid = this.GetMatchId();

            using (var client = new DotaGameClient())
            {
                await client.Connect(userInfo.Username, userInfo.Password);

                var data = await client.DownloadMatchData(matchid);
                Assert.NotNull(data);
            }
        }

        [Fact]
        public async Task DownloadMeta()
        {
            var userInfo = this.GetUserInfo();
            var matchid = this.GetMatchId();

            using (var client = new DotaGameClient())
            {
                await client.Connect(userInfo.Username, userInfo.Password);

                var data = await client.DownloadMeta(matchid);
                Assert.NotNull(data);
            }
        }

        [Fact]
        public async Task DownloadReplay()
        {
            var userInfo = this.GetUserInfo();
            var matchid = this.GetMatchId();

            using (var client = new DotaGameClient())
            {
                await client.Connect(userInfo.Username, userInfo.Password);

                var data = await client.DownloadReplay(matchid);
                Assert.NotNull(data);
            }
        }

    }
}
