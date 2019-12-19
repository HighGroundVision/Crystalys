using System;
using System.Collections.Generic;
using System.Text;

namespace HGV.Crystalys.Tests
{
    public class MoqSteamAuthenticationProvider : ISteamAuthenticationProvider
    {
        public string GetPassword()
        {
            return Environment.GetEnvironmentVariable("SteamPassword");
        }

        public string GetUserName()
        {
            return Environment.GetEnvironmentVariable("SteamUsername");
        }
    }
}
