using System;
using System.Collections.Generic;
using System.Text;

namespace HGV.Crystalys
{
    public interface ISteamAuthenticationProvider
    {
        string GetUserName();
        string GetPassword();
    }

    public class EnvironmentalAuthenticationProvider : ISteamAuthenticationProvider
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
