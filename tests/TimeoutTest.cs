using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace HGV.Crystalys.Tests
{
	public class TimeoutTest
	{
		[Fact]
		public void TimeoutReached()
		{
			var result = TimeoutHandler.RetryUntilSuccessOrTimeout(TimeSpan.FromSeconds(1), () => { return false; });
			Assert.False(result);
        }

		[Fact]
		public void PositiveResult()
		{
			var result = TimeoutHandler.RetryUntilSuccessOrTimeout(TimeSpan.FromSeconds(1), () => { return true; });
			Assert.True(result);
		}

		[Fact]
		public void LoopResult()
		{
			var count = 0;
			var result = TimeoutHandler.RetryUntilSuccessOrTimeout(TimeSpan.FromSeconds(60), () => { count++; return count == 10 ? true : false; });

			Assert.True(result);
			Assert.Equal(10, count);
		}
	}
}
