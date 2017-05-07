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
	public class DotaClientTest
    {
        private UserInfo GetUserInfo()
        {
            return new UserInfo() {
                Username = ConfigurationManager.AppSettings["Steam:Username"],
                Password = ConfigurationManager.AppSettings["Steam:Password"]
            };
        }

        private ulong GetMatchId()
        {
            return ulong.Parse(ConfigurationManager.AppSettings["Dota:MatchId"]);
        }

        [Fact]
		public void ConnectToSteam()
		{
            var userInfo = this.GetUserInfo();
            var matchid = this.GetMatchId();

            var client = new DotaClient(userInfo.Username, userInfo.Password);
            client.Connect();
        }

        
        [Fact]
        public void DownloadMatchData()
        {
            var userInfo = this.GetUserInfo();
            var matchid = this.GetMatchId();

            var client = new DotaClient(userInfo.Username, userInfo.Password);
            client.Connect();

            var data = client.DownloadMatchData(matchid);
            Assert.NotNull(data);
        }

        [Fact]
        public async Task DownloadMeta()
        {
            var userInfo = this.GetUserInfo();
            var matchid = this.GetMatchId();

            var client = new DotaClient(userInfo.Username, userInfo.Password);
            client.Connect();

            var data = client.DownloadMatchData(matchid);
            Assert.NotNull(data);

            var meta = await client.DownloadMeta(matchid, data.cluster, data.replay_salt);
            Assert.NotNull(meta);
        }

        [Fact]
        public async Task DownloadReplay()
        {
            var userInfo = this.GetUserInfo();
            var matchid = this.GetMatchId();

            var client = new DotaClient(userInfo.Username, userInfo.Password);
            client.Connect();

            var data = client.DownloadMatchData(matchid);
            Assert.NotNull(data);

            var replay = await client.DownloadReplay(matchid, data.cluster, data.replay_salt);
            Assert.NotNull(replay);
        }


        [Fact]
        public void Test1()
        {
            var userInfo = this.GetUserInfo();
            var matchid = this.GetMatchId();

            var client = new DotaClient(userInfo.Username, userInfo.Password);
            client.Connect();

            var collection = Enumerable.Range((int)matchid, 100);
            foreach (ulong id in collection)
            {
                var data = client.DownloadMatchData(id);
                Assert.NotNull(data);
            }

            Assert.True(matchid != 0);
        }

    }
}
