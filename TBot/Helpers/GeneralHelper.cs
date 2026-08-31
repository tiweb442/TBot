using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tbot.Helpers {
	public static class GeneralHelper {
		public static bool ShouldSleep(DateTime time, DateTime goToSleep, DateTime wakeUp) {
			return Tbot.Common.Helpers.SleepModeHelper.ShouldSleep(time, goToSleep, wakeUp);
		}

		public static int ClampSystem(int system) {
			if (system < 1)
				system = 1;
			if (system > 499)
				system = 499;
			return system;
		}

		public static int WrapSystem(int system) {
			if (system > 499)
				system = 1;
			if (system < 1)
				system = 499;
			return system;
		}
	}
}
