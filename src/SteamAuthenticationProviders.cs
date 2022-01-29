using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;

namespace HGV.Crystalys
{
    public class AuthenticationOptions
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public interface ISteamAuthenticationProvider
    {
        string UserName { get; }
        string Password { get; }
    }

    public class EnvironmentalAuthenticationProvider : ISteamAuthenticationProvider
    {
        public EnvironmentalAuthenticationProvider()
        {
            this.UserName = Environment.GetEnvironmentVariable("STEAM_USERNAME") ?? throw new ArgumentNullException("Missing STEAM_USERNAME Environmental Variable");
            this.Password = Environment.GetEnvironmentVariable("STEAM_PASSWORD") ?? throw new ArgumentNullException("Missing STEAM_PASSWORD Environmental Variable");
        }

        public string UserName { get; private set; }
        public string Password { get; private set; }
    }

    public class OptionsAuthenticationProvider : ISteamAuthenticationProvider
    {
        public OptionsAuthenticationProvider(IOptions<AuthenticationOptions> options)
        {
            this.UserName = options?.Value?.Username ?? throw new ArgumentNullException("Missing Username Authentication Option");
            this.Password = options?.Value?.Password ?? throw new ArgumentNullException("Missing Password Authentication Option");
        }

        public string UserName { get; private set; }
        public string Password { get; private set; }
    }

    public class TestAuthenticationProvider : ISteamAuthenticationProvider
    {
        public TestAuthenticationProvider(string username, string password)
        {
            this.UserName = username ?? throw new ArgumentNullException("Missing Username Authentication Option");
            this.Password = password ?? throw new ArgumentNullException("Missing Password Authentication Option");
        }

        public string UserName { get; private set; }
        public string Password { get; private set; }
    }
}
