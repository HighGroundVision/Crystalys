using SteamKit2;
using SteamKit2.Internal;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;

namespace HGV.Crystalys
{
    public interface ISteamAuthenticationProvider
    {
        string GetUserName();
        string GetPassword();
    }

    public interface ISteamConnection
    {
        bool isConnected();
        SteamClient GetClient();
    }

    public class SteamConnection : ISteamConnection
    {
        protected readonly SteamClient client;
        private readonly ISteamAuthenticationProvider provider;

        public SteamConnection(ISteamAuthenticationProvider provider)
        {
            this.provider = provider;
            this.client = new SteamClient();

            this.Connect();
        }

        private void Connect()
        {
            var user = this.client.GetHandler<SteamUser>();
            var callbacks = new CallbackManager(this.client);
            
            callbacks.Subscribe<SteamClient.ConnectedCallback>(_ => {
                var details = new SteamUser.LogOnDetails
                {
                    Username = this.provider.GetUserName(),
                    Password = this.provider.GetPassword(),
                };
                user.LogOn(details);
            });
            callbacks.Subscribe<SteamUser.LoggedOnCallback>(_ => {
                // Do Nothing
            });

            this.client.Connect();

            callbacks.RunWaitAllCallbacks(TimeSpan.FromSeconds(5));
        }

        public bool isConnected()
        {
            return this.client.IsConnected;
        }

        public SteamClient GetClient()
        {
            return this.client;
        }
    }
}
