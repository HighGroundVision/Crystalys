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
        private UserInfo GetUserInfo()
        {
            return new UserInfo() { Username = "Thantsking", Password = "aPhan3sah", Sentry = File.ReadAllBytes(".\\sentry.bin") };
        }

        private ulong GetMatchId()
        {
            return 3080313608;
        }

		[Fact]
		public async Task ConnectToSteam()
		{
            var userInfo = this.GetUserInfo();

            using (var client = new DotaGameClient())
			{
                await client.Connect(userInfo.Username, userInfo.Password, userInfo.Sentry);
            }
        }

        [Fact]
        public async Task DownloadMatchData()
        {
            var userInfo = this.GetUserInfo();
            var matchid = this.GetMatchId();

            using (var client = new DotaGameClient())
            {
                await client.Connect(userInfo.Username, userInfo.Password, userInfo.Sentry);

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
                await client.Connect(userInfo.Username, userInfo.Password, userInfo.Sentry);

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
                await client.Connect(userInfo.Username, userInfo.Password, userInfo.Sentry);

                var data = await client.DownloadReplay(matchid);
                Assert.NotNull(data);
            }
        }

    }
}
