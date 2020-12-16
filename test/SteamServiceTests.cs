using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging;
using Moq;
using SteamKit2;
using System.Threading.Tasks;

namespace HGV.Crystalys.Tests
{
    [TestClass]
    public class SteamServiceTests
    {
        [TestInitialize]
        public void LoadEnvironment()
        {
            dotenv.net.DotEnv.Config(throwOnError: false);
        }

        [TestMethod]
        public async Task ConnectToSteam()
        { 
            // Setup
            var id = 5738727313UL;
            var provider = new EnvironmentalAuthenticationProvider();
            var logger = new Mock<ILogger<DotaServices>>();

            // Act
            var service = new DotaServices(logger.Object, provider, new SteamClient());
            await service.StartAsync();
            var match = await service.GetMatchData(id);

            // Test
            Assert.IsNotNull(match);
            Assert.AreEqual(id, match.match_id);
        }
    }
}
