using SteamKit2;
using SteamKit2.GC.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HGV.Crystalys
{
	public class ConnectedEventArgs : EventArgs
	{
		public bool Result { get; set; }
	}

	public class DisconnectedEventArgs : EventArgs
	{
		public bool Reconnect { get; set; }
	}

	public class LoggedOnEventArgs : EventArgs
	{
		public bool Sucess { get; set; }

		public EResult Result { get; set;}
	}

	public class AccountLogonDeniedEventArgs : EventArgs
	{
		public string EmailDomain { get; set; }
    }

	public class ClientWelcomeEventArgs : EventArgs
	{
		public CMsgClientWelcome Message { get; set; }
	}
}
