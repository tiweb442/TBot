using System;
using Newtonsoft.Json.Linq;

namespace Tbot.Common.Helpers {
	public sealed class SleepModeStatus {
		public bool SleepModeActive { get; init; }
		public bool IsSleeping { get; init; }
		public string? GoToSleep { get; init; }
		public string? WakeUp { get; init; }
		public DateTime? NextWakeUp { get; init; }
		public string? Message { get; init; }
	}

	public static class SleepModeHelper {
		/// <summary>
		/// Same window logic used by FleetScheduler / AutoDiscovery (bed time check).
		/// </summary>
		public static bool ShouldSleep(DateTime time, DateTime goToSleep, DateTime wakeUp) {
			if (time >= goToSleep) {
				if (time >= wakeUp) {
					return goToSleep >= wakeUp;
				}
				return true;
			}

			if (time >= wakeUp) {
				return false;
			}

			return goToSleep >= wakeUp;
		}

		public static DateTime GetNextWakeUp(DateTime time, DateTime goToSleep, DateTime wakeUp) {
			var wakeToday = new DateTime(time.Year, time.Month, time.Day, wakeUp.Hour, wakeUp.Minute, wakeUp.Second, wakeUp.Kind);
			if (ShouldSleep(time, goToSleep, wakeUp)) {
				return wakeToday > time ? wakeToday : wakeToday.AddDays(1);
			}

			// Awake: next wake is the WakeUp after the upcoming GoToSleep window.
			var sleepToday = new DateTime(time.Year, time.Month, time.Day, goToSleep.Hour, goToSleep.Minute, goToSleep.Second, goToSleep.Kind);
			if (sleepToday <= time)
				sleepToday = sleepToday.AddDays(1);

			var nextWake = new DateTime(sleepToday.Year, sleepToday.Month, sleepToday.Day, wakeUp.Hour, wakeUp.Minute, wakeUp.Second, wakeUp.Kind);
			if (nextWake <= sleepToday)
				nextWake = nextWake.AddDays(1);
			return nextWake;
		}

		public static SleepModeStatus ResolveFromSettings(JObject? root, DateTime now) {
			var sleep = root?["SleepMode"] as JObject;
			if (sleep == null) {
				return new SleepModeStatus {
					SleepModeActive = false,
					IsSleeping = false
				};
			}

			var active = sleep["Active"]?.Type == JTokenType.Boolean && sleep["Active"]!.Value<bool>();
			var goToSleepRaw = sleep["GoToSleep"]?.Value<string>();
			var wakeUpRaw = sleep["WakeUp"]?.Value<string>();

			if (!active) {
				return new SleepModeStatus {
					SleepModeActive = false,
					IsSleeping = false,
					GoToSleep = goToSleepRaw,
					WakeUp = wakeUpRaw
				};
			}

			if (!DateTime.TryParse(goToSleepRaw, out var goToSleep) || !DateTime.TryParse(wakeUpRaw, out var wakeUp)) {
				return new SleepModeStatus {
					SleepModeActive = true,
					IsSleeping = false,
					GoToSleep = goToSleepRaw,
					WakeUp = wakeUpRaw,
					Message = "Sleep mode active but GoToSleep/WakeUp could not be parsed."
				};
			}

			// Align date parts to "now" so ShouldSleep compares time-of-day correctly.
			goToSleep = new DateTime(now.Year, now.Month, now.Day, goToSleep.Hour, goToSleep.Minute, goToSleep.Second, now.Kind);
			wakeUp = new DateTime(now.Year, now.Month, now.Day, wakeUp.Hour, wakeUp.Minute, wakeUp.Second, now.Kind);

			var isSleeping = ShouldSleep(now, goToSleep, wakeUp);
			var nextWake = GetNextWakeUp(now, goToSleep, wakeUp);
			var wakeLabel = nextWake.ToString("HH:mm");

			return new SleepModeStatus {
				SleepModeActive = true,
				IsSleeping = isSleeping,
				GoToSleep = goToSleepRaw,
				WakeUp = wakeUpRaw,
				NextWakeUp = nextWake,
				Message = isSleeping
					? $"Sleeping — Goals paused until ~{wakeLabel} (not hung)."
					: $"Sleep window {goToSleepRaw}–{wakeUpRaw}."
			};
		}
	}
}
