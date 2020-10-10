using System;
using System.Collections.Generic;
using System.Text;

namespace HGV.Crystalys.Tests
{
    public class MoqSteamAuthenticationProvider : ISteamAuthenticationProvider
    {
        public string GetUserName()
        {
            return Environment.GetEnvironmentVariable("STEAM_USERNAME");
        }

        public string GetPassword()
        {
            return Environment.GetEnvironmentVariable("STEAM_PASSWORD");
        }
    }
}
