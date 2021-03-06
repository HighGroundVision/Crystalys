using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace HGV.Crystalys.Tests
{
    [TestClass]
    public class DotaClientTests
    {
        [TestInitialize]
        public void LoadEnvironment()
        {
            dotenv.net.DotEnv.Config(throwOnError: false);
        }

        [TestMethod]
        public void TestMethod1()
        {
            ISteamAuthenticationProvider provider = new MoqSteamAuthenticationProvider();

            var username = provider.GetUserName();
            var password = provider.GetPassword();

            Assert.IsNotNull(username);
            Assert.IsNotNull(password);
        }

        [TestMethod]
        public void TestMethod2()
        { 
            var provider = new MoqSteamAuthenticationProvider();
            var factory = new MoqHttpClientFactory();
            var client = new DotaClient(provider, factory);

            Assert.IsTrue(client.isConnected());

            client.Dispose();

            Assert.IsFalse(client.isConnected());
        }
    }
}
