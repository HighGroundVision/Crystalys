using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HGV.Crystalys
{
	public class TimeoutHandler
	{
		public static bool RetryUntilSuccessOrTimeout(TimeSpan timeSpan, Func<bool> task)
		{
			bool success = false;
			int elapsed = 0;
			while ((!success) && (elapsed < timeSpan.TotalMilliseconds))
			{
				Thread.Sleep(1000);
				elapsed += 1000;
				success = task();
			}
			return success;
		}

	}
}
