﻿using SteamKit2.GC;
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
                Username = "Thantsking",
                Password = "aPhan3sah"
            };
        }

        private ulong GetMatchId()
        {
            return 3111014659;
        }

        [Fact]
		public void ConnectToSteam()
		{
            var userInfo = this.GetUserInfo();
            var matchid = this.GetMatchId();

            using (var client = new DotaGameClient())
            {
                client.Connect(userInfo.Username, userInfo.Password);
            }
        }

        
        [Fact]
        public void DownloadMatchData()
        {
            var userInfo = this.GetUserInfo();
            var matchid = this.GetMatchId();

            using (var client = new DotaGameClient())
            {
                client.Connect(userInfo.Username, userInfo.Password);

                var data = client.DownloadMatchData(matchid);
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
                client.Connect(userInfo.Username, userInfo.Password);

                var data = client.DownloadMatchData(matchid);
                var meta = await client.DownloadMeta(matchid, data.cluster, data.replay_salt);
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
                client.Connect(userInfo.Username, userInfo.Password);

                var data = client.DownloadMatchData(matchid);
                var replay = await client.DownloadReplay(matchid, data.cluster, data.replay_salt);
                Assert.NotNull(data);
            }
        }
        
    }
}
