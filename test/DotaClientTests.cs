using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net.Http;

namespace HGV.Crystalys.Tests
{
    [TestClass]
    public class DotaClientTests
    {
        [TestInitialize]
        public void LoadEnvironment()
        {
            dotenv.net.DotEnv.Config();
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
            ISteamAuthenticationProvider provider = new MoqSteamAuthenticationProvider();
            IHttpClientFactory factory = new MoqHttpClientFactory();
            IDotaClient client = new DotaClient(provider, factory);

            Assert.IsTrue(client.isConnected());

            //var meta = client.DownloadMeta(5160263863);
        }
    }
}
