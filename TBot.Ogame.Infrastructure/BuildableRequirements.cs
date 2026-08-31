using System.Collections.Generic;
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
				Buildables.ShieldingTechnology => "Shield",
				Buildables.HyperspaceTechnology => "HST",
				Buildables.EnergyTechnology => "Energy",
				Buildables.ComputerTechnology => "Comp",
				Buildables.Astrophysics => "Astro",
				Buildables.Shipyard => "SY",
				_ => buildable.ToString()
			};
		}
	}
}
