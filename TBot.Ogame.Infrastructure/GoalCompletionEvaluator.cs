using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace TBot.Ogame.Infrastructure {
	public class GoalProgressEntry {
		public long Current { get; set; }
		public long Required { get; set; }
	}

	public static class GoalCompletionEvaluator {
		public static bool EvaluatePreset(JObject preset, Researches researches, IEnumerable<Celestial> celestials, IEnumerable<Fleet> fleets = null) {
			if (preset == null)
				return false;

			var unlockTarget = preset["UnlockTarget"] as JObject;
			if (HasUnlockTarget(unlockTarget))
				return EvaluateUnlockTarget(unlockTarget, preset, researches, celestials, fleets);

			return EvaluateCompleteWhen(preset["CompleteWhen"] as JObject, researches, celestials, fleets);
		}

		public static Dictionary<string, GoalProgressEntry> GetPresetProgress(JObject preset, Researches researches, IEnumerable<Celestial> celestials, IEnumerable<Fleet> fleets = null) {
			if (preset == null)
				return new Dictionary<string, GoalProgressEntry>();

			var unlockTarget = preset["UnlockTarget"] as JObject;
			if (HasUnlockTarget(unlockTarget))
				return GetUnlockProgress(unlockTarget, preset, researches, celestials, fleets);

			return GetProgress(preset["CompleteWhen"] as JObject, researches, celestials, fleets);
		}

		public static bool EvaluateCompleteWhen(JObject completeWhen, Researches researches, IEnumerable<Celestial> celestials, IEnumerable<Fleet> fleets = null) {
			if (completeWhen == null || !completeWhen.Properties().Any())
				return false;

			foreach (var condition in completeWhen.Properties()) {
				if (condition.Value is not JObject rule)
					return false;
				if (!EvaluateCondition(condition.Name, rule, researches, celestials, fleets))
					return false;
			}

			return true;
		}

		public static Dictionary<string, GoalProgressEntry> GetProgress(JObject completeWhen, Researches researches, IEnumerable<Celestial> celestials, IEnumerable<Fleet> fleets = null) {
			var result = new Dictionary<string, GoalProgressEntry>();
			if (completeWhen == null)
				return result;

			foreach (var condition in completeWhen.Properties()) {
				if (condition.Value is not JObject rule)
					continue;

				result[condition.Name] = new GoalProgressEntry {
					Current = GetActualValue(condition.Name, researches, celestials, fleets),
					Required = GetRequiredValue(rule)
				};
			}

			return result;
		}

		private static bool HasUnlockTarget(JObject? unlockTarget) {
			return unlockTarget != null
				&& !string.IsNullOrWhiteSpace(unlockTarget["Type"]?.Value<string>())
				&& !string.IsNullOrWhiteSpace(unlockTarget["Name"]?.Value<string>());
		}

		private static bool EvaluateUnlockTarget(JObject unlockTarget, JObject preset, Researches researches, IEnumerable<Celestial> celestials, IEnumerable<Fleet> fleets) {
			var type = unlockTarget["Type"]?.Value<string>() ?? "";
			var name = unlockTarget["Name"]?.Value<string>() ?? "";

			if (type.Equals("Ship", StringComparison.OrdinalIgnoreCase)) {
				if (!Enum.TryParse<Buildables>(name, out var ship))
					return false;
				return EvaluateShipUnlock(ship, preset, researches, celestials, fleets);
			}

			if (type.Equals("Research", StringComparison.OrdinalIgnoreCase)) {
				if (!Enum.TryParse<Buildables>(name, out var research))
					return false;
				var targetLevel = ResolveTargetLevel(unlockTarget, preset, name);
				return researches.GetLevel(research) >= targetLevel;
			}

			return false;
		}

		private static Dictionary<string, GoalProgressEntry> GetUnlockProgress(JObject unlockTarget, JObject preset, Researches researches, IEnumerable<Celestial> celestials, IEnumerable<Fleet> fleets) {
			var result = new Dictionary<string, GoalProgressEntry>();
			var type = unlockTarget["Type"]?.Value<string>() ?? "";
			var name = unlockTarget["Name"]?.Value<string>() ?? "";

			if (type.Equals("Ship", StringComparison.OrdinalIgnoreCase)) {
				if (!Enum.TryParse<Buildables>(name, out var ship))
					return result;

				var shipCount = GetTotalShipCount(ship, celestials, fleets);
				if (shipCount >= 1)
					return result;

				var reqs = BuildableRequirements.GetShipRequirements(ship);
				if (reqs == null)
					return result;

				foreach (var (research, required) in reqs.Research) {
					var current = researches.GetLevel(research);
					if (current < required)
						result[BuildableRequirements.GetShortName(research)] = new GoalProgressEntry {
							Current = current,
							Required = required
						};
				}

				var maxShipyard = GetMaxShipyard(celestials);
				if (maxShipyard < reqs.ShipyardLevel)
					result[BuildableRequirements.GetShortName(Buildables.Shipyard)] = new GoalProgressEntry {
						Current = maxShipyard,
						Required = reqs.ShipyardLevel
					};

				if (result.Count == 0)
					result[BuildableRequirements.GetShortName(ship)] = new GoalProgressEntry {
						Current = shipCount,
						Required = 1
					};

				return result;
			}

			if (type.Equals("Research", StringComparison.OrdinalIgnoreCase)) {
				if (!Enum.TryParse<Buildables>(name, out var research))
					return result;

				var targetLevel = ResolveTargetLevel(unlockTarget, preset, name);
				var current = researches.GetLevel(research);
				if (current < targetLevel)
					result[BuildableRequirements.GetShortName(research)] = new GoalProgressEntry {
						Current = current,
						Required = targetLevel
					};
			}

			return result;
		}

		private static bool EvaluateShipUnlock(Buildables ship, JObject preset, Researches researches, IEnumerable<Celestial> celestials, IEnumerable<Fleet> fleets) {
			if (GetTotalShipCount(ship, celestials, fleets) >= 1)
				return true;

			var completeWhen = preset["CompleteWhen"] as JObject;
			if (completeWhen != null && completeWhen.Properties().Any())
				return EvaluateCompleteWhen(completeWhen, researches, celestials, fleets);

			return false;
		}

		private static int ResolveTargetLevel(JObject unlockTarget, JObject preset, string researchName) {
			return BuildableRequirements.ResolveTargetLevel(unlockTarget, preset, researchName);
		}

		private static int GetMaxShipyard(IEnumerable<Celestial> celestials) {
			int max = 0;
			foreach (var celestial in celestials) {
				var shipyard = celestial.Facilities?.Shipyard ?? 0;
				if (shipyard > max)
					max = shipyard;
			}
			return max;
		}

		private static long GetTotalShipCount(Buildables ship, IEnumerable<Celestial> celestials, IEnumerable<Fleet> fleets) {
			long total = 0;
			foreach (var celestial in celestials)
				total += celestial.Ships?.GetAmount(ship) ?? 0;
			if (fleets != null) {
				foreach (var fleet in fleets)
					total += fleet.Ships?.GetAmount(ship) ?? 0;
			}
			return total;
		}

		private static bool EvaluateCondition(string path, JObject rule, Researches researches, IEnumerable<Celestial> celestials, IEnumerable<Fleet> fleets) {
			long actual = GetActualValue(path, researches, celestials, fleets);

			if (rule.TryGetValue("Gte", out var gte))
				return actual >= gte.Value<long>();
			if (rule.TryGetValue("Gt", out var gt))
				return actual > gt.Value<long>();
			if (rule.TryGetValue("Eq", out var eq))
				return actual == eq.Value<long>();
			if (rule.TryGetValue("Lte", out var lte))
				return actual <= lte.Value<long>();

			return false;
		}

		private static long GetRequiredValue(JObject rule) {
			if (rule.TryGetValue("Gte", out var gte))
				return gte.Value<long>();
			if (rule.TryGetValue("Gt", out var gt))
				return gt.Value<long>() + 1;
			if (rule.TryGetValue("Eq", out var eq))
				return eq.Value<long>();
			if (rule.TryGetValue("Lte", out var lte))
				return lte.Value<long>();

			return 0;
		}

		private static long GetActualValue(string path, Researches researches, IEnumerable<Celestial> celestials, IEnumerable<Fleet> fleets) {
			var parts = path.Split('.');
			if (parts.Length != 2)
				return 0;

			if (parts[0] == "Research") {
				var prop = typeof(Researches).GetProperty(parts[1]);
				return prop != null ? Convert.ToInt64(prop.GetValue(researches) ?? 0) : 0;
			}

			if (parts[0] == "Ships") {
				if (!Enum.TryParse<Buildables>(parts[1], out var ship))
					return 0;

				return GetTotalShipCount(ship, celestials, fleets);
			}

			return 0;
		}
	}
}
