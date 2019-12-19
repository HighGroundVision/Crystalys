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

    public class DefaultSteamAuthenticationProvider : ISteamAuthenticationProvider
    {
        public string GetPassword()
        {
            throw new NotImplementedException();
        }

        public string GetUserName()
        {
            throw new NotImplementedException();
        }
    }
}
