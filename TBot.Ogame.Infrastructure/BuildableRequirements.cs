using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TBot.Ogame.Infrastructure.Enums;

namespace TBot.Ogame.Infrastructure {
	public class ShipRequirements {
		public int ShipyardLevel { get; set; }
		public Dictionary<Buildables, int> Research { get; set; } = new();
	}

	public static class BuildableRequirements {
		private static readonly Dictionary<Buildables, ShipRequirements> ShipReqs = new() {
			[Buildables.LightFighter] = new ShipRequirements {
				ShipyardLevel = 1,
				Research = new Dictionary<Buildables, int> { [Buildables.CombustionDrive] = 1 }
			},
			[Buildables.SmallCargo] = new ShipRequirements {
				ShipyardLevel = 2,
				Research = new Dictionary<Buildables, int> { [Buildables.CombustionDrive] = 2 }
			},
			[Buildables.EspionageProbe] = new ShipRequirements {
				ShipyardLevel = 3,
				Research = new Dictionary<Buildables, int> {
					[Buildables.CombustionDrive] = 3,
					[Buildables.EspionageTechnology] = 2
				}
			},
			[Buildables.Recycler] = new ShipRequirements {
				ShipyardLevel = 4,
				Research = new Dictionary<Buildables, int> {
					[Buildables.CombustionDrive] = 6,
					[Buildables.ShieldingTechnology] = 2
				}
			},
			[Buildables.LargeCargo] = new ShipRequirements {
				ShipyardLevel = 4,
				Research = new Dictionary<Buildables, int> { [Buildables.CombustionDrive] = 6 }
			},
			[Buildables.ColonyShip] = new ShipRequirements {
				ShipyardLevel = 4,
				Research = new Dictionary<Buildables, int> { [Buildables.ImpulseDrive] = 3 }
			},
			[Buildables.Pathfinder] = new ShipRequirements {
				ShipyardLevel = 5,
				Research = new Dictionary<Buildables, int> { [Buildables.HyperspaceDrive] = 2 }
			}
		};

		public static ShipRequirements? GetShipRequirements(Buildables ship) {
			return ShipReqs.TryGetValue(ship, out var reqs) ? reqs : null;
		}

		public static string GetShortName(Buildables buildable) {
			return buildable switch {
				Buildables.CombustionDrive => "CD",
				Buildables.ImpulseDrive => "ID",
				Buildables.HyperspaceDrive => "HD",
				Buildables.EspionageTechnology => "Esp",
				Buildables.ShieldingTechnology => "ST",
				Buildables.HyperspaceTechnology => "HST",
				Buildables.EnergyTechnology => "Energy",
				Buildables.ComputerTechnology => "Comp",
				Buildables.Astrophysics => "Astro",
				Buildables.Shipyard => "SY",
				_ => buildable.ToString()
			};
		}

		public static IReadOnlyList<(Buildables Prerequisite, int MinLevel)> GetDirectPrerequisites(Buildables research) {
			return research switch {
				Buildables.LaserTechnology => [(Buildables.EnergyTechnology, 2)],
				Buildables.IonTechnology => [(Buildables.EnergyTechnology, 4), (Buildables.LaserTechnology, 5)],
				Buildables.HyperspaceTechnology => [(Buildables.EnergyTechnology, 5), (Buildables.ShieldingTechnology, 5)],
				Buildables.PlasmaTechnology => [(Buildables.EnergyTechnology, 8), (Buildables.LaserTechnology, 10), (Buildables.IonTechnology, 5)],
				Buildables.CombustionDrive => [(Buildables.EnergyTechnology, 1)],
				Buildables.ImpulseDrive => [(Buildables.EnergyTechnology, 1)],
				Buildables.HyperspaceDrive => [(Buildables.HyperspaceTechnology, 3)],
				Buildables.Astrophysics => [(Buildables.EspionageTechnology, 4), (Buildables.ImpulseDrive, 3)],
				Buildables.ShieldingTechnology => [(Buildables.EnergyTechnology, 3)],
				Buildables.IntergalacticResearchNetwork => [(Buildables.ComputerTechnology, 8), (Buildables.HyperspaceTechnology, 8)],
				_ => Array.Empty<(Buildables, int)>()
			};
		}

		public static Dictionary<Buildables, int> ExpandResearchRequirements(Dictionary<Buildables, int> directRequirements) {
			var expanded = new Dictionary<Buildables, int>();
			foreach (var (research, level) in directRequirements)
				MergeResearchRequirement(expanded, research, level);
			return expanded;
		}

		public static void MergeResearchRequirement(Dictionary<Buildables, int> requirements, Buildables research, int targetLevel) {
			if (targetLevel <= 0)
				return;

			if (requirements.TryGetValue(research, out var existing))
				requirements[research] = Math.Max(existing, targetLevel);
			else
				requirements[research] = targetLevel;

			foreach (var (prerequisite, minLevel) in GetDirectPrerequisites(research))
				MergeResearchRequirement(requirements, prerequisite, minLevel);
		}

		public static Dictionary<Buildables, int> GetUnlockResearchRequirements(JObject? unlockTarget, JObject? preset) {
			var requirements = new Dictionary<Buildables, int>();
			if (unlockTarget == null)
				return requirements;

			var type = unlockTarget["Type"]?.Value<string>() ?? "";
			var name = unlockTarget["Name"]?.Value<string>() ?? "";
			if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(name))
				return requirements;

			if (type.Equals("Ship", StringComparison.OrdinalIgnoreCase)) {
				if (!Enum.TryParse<Buildables>(name, out var ship))
					return requirements;

				var shipReqs = GetShipRequirements(ship);
				if (shipReqs == null)
					return requirements;

				return ExpandResearchRequirements(new Dictionary<Buildables, int>(shipReqs.Research));
			}

			if (type.Equals("Research", StringComparison.OrdinalIgnoreCase)) {
				if (!Enum.TryParse<Buildables>(name, out var research))
					return requirements;

				var targetLevel = ResolveTargetLevel(unlockTarget, preset, name);
				MergeResearchRequirement(requirements, research, targetLevel);
			}

			return requirements;
		}

		public static int ResolveTargetLevel(JObject unlockTarget, JObject? preset, string researchName) {
			if (unlockTarget.TryGetValue("TargetLevel", out var targetToken) && targetToken.Type != JTokenType.Null)
				return targetToken.Value<int>();

			var apply = preset?["Apply"] as JObject;
			if (apply != null) {
				var applyKey = $"Brain.AutoResearch.Max{researchName}";
				if (apply.TryGetValue(applyKey, out var applyValue) && applyValue.Type != JTokenType.Null)
					return applyValue.Value<int>();
			}

			return 1;
		}
	}
}
