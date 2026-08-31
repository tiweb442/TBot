using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Tbot.Common.Settings;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace TBot.Ogame.Infrastructure {
	public static class GoalsFocusHelper {
		private static readonly Buildables[] ResearchBuildOrder = {
			Buildables.EnergyTechnology,
			Buildables.CombustionDrive,
			Buildables.ImpulseDrive,
			Buildables.EspionageTechnology,
			Buildables.ComputerTechnology,
			Buildables.ArmourTechnology,
			Buildables.WeaponsTechnology,
			Buildables.ShieldingTechnology,
			Buildables.LaserTechnology,
			Buildables.IonTechnology,
			Buildables.HyperspaceTechnology,
			Buildables.HyperspaceDrive,
			Buildables.Astrophysics,
			Buildables.PlasmaTechnology,
			Buildables.IntergalacticResearchNetwork,
			Buildables.GravitonTechnology
		};

		private static readonly Buildables[] CompetingResearches = {
			Buildables.LaserTechnology,
			Buildables.IonTechnology,
			Buildables.PlasmaTechnology,
			Buildables.WeaponsTechnology,
			Buildables.ArmourTechnology,
			Buildables.IntergalacticResearchNetwork
		};

		private static readonly Dictionary<Buildables, string> ResearchMaxSettingPaths = new() {
			[Buildables.EnergyTechnology] = "Brain.AutoResearch.MaxEnergyTechnology",
			[Buildables.LaserTechnology] = "Brain.AutoResearch.MaxLaserTechnology",
			[Buildables.IonTechnology] = "Brain.AutoResearch.MaxIonTechnology",
			[Buildables.HyperspaceTechnology] = "Brain.AutoResearch.MaxHyperspaceTechnology",
			[Buildables.PlasmaTechnology] = "Brain.AutoResearch.MaxPlasmaTechnology",
			[Buildables.CombustionDrive] = "Brain.AutoResearch.MaxCombustionDrive",
			[Buildables.ImpulseDrive] = "Brain.AutoResearch.MaxImpulseDrive",
			[Buildables.HyperspaceDrive] = "Brain.AutoResearch.MaxHyperspaceDrive",
			[Buildables.EspionageTechnology] = "Brain.AutoResearch.MaxEspionageTechnology",
			[Buildables.ComputerTechnology] = "Brain.AutoResearch.MaxComputerTechnology",
			[Buildables.Astrophysics] = "Brain.AutoResearch.MaxAstrophysics",
			[Buildables.IntergalacticResearchNetwork] = "Brain.AutoResearch.MaxIntergalacticResearchNetwork",
			[Buildables.WeaponsTechnology] = "Brain.AutoResearch.MaxWeaponsTechnology",
			[Buildables.ShieldingTechnology] = "Brain.AutoResearch.MaxShieldingTechnology",
			[Buildables.ArmourTechnology] = "Brain.AutoResearch.MaxArmourTechnology"
		};

		private static readonly string[] PrioritizeSettingPaths = {
			"Brain.AutoResearch.PrioritizeAstrophysics",
			"Brain.AutoResearch.PrioritizePlasmaTechnology",
			"Brain.AutoResearch.PrioritizeEnergyTechnology",
			"Brain.AutoResearch.PrioritizeIntergalacticResearchNetwork"
		};

		public static JObject BuildFocusPatches(JObject preset, JObject root) {
			var patches = new JObject();
			var unlockTarget = preset["UnlockTarget"] as JObject;
			if (!HasUnlockTarget(unlockTarget))
				return patches;

			var requiredLevels = BuildableRequirements.GetUnlockResearchRequirements(unlockTarget, preset);

			foreach (var (research, settingPath) in ResearchMaxSettingPaths) {
				if (requiredLevels.TryGetValue(research, out var requiredLevel))
					patches[settingPath] = requiredLevel;
				else if (CompetingResearches.Contains(research))
					patches[settingPath] = 0;
				else
					patches[settingPath] = ReadCurrentMax(root, settingPath);
			}

			foreach (var settingPath in PrioritizeSettingPaths)
				patches[settingPath] = false;

			patches["Brain.AutoResearch.ForceResearchWhateverTheLabLevel"] = true;
			patches["Brain.AutoResearch.OptimizeForStart"] = false;
			patches["Brain.AutoResearch.EnsureExpoSlots"] = false;

			return patches;
		}

		public static JObject BuildMergedApply(JObject preset, JObject root) {
			var merged = preset["Apply"] is JObject apply ? (JObject) apply.DeepClone() : new JObject();
			var focusPatches = BuildFocusPatches(preset, root);

			foreach (var prop in focusPatches.Properties())
				merged[prop.Name] = prop.Value?.DeepClone();

			return merged;
		}

		public static Buildables GetNextMissingGoalResearch(Researches researches, JObject preset) {
			var unlockTarget = preset["UnlockTarget"] as JObject;
			if (!HasUnlockTarget(unlockTarget))
				return Buildables.Null;

			var requiredLevels = BuildableRequirements.GetUnlockResearchRequirements(unlockTarget, preset);
			foreach (var research in ResearchBuildOrder) {
				if (!requiredLevels.TryGetValue(research, out var requiredLevel))
					continue;
				if (researches.GetLevel(research) < requiredLevel)
					return research;
			}

			return Buildables.Null;
		}

		public static bool HasUnlockTarget(JObject? unlockTarget) {
			return unlockTarget != null
				&& !string.IsNullOrWhiteSpace(unlockTarget["Type"]?.Value<string>())
				&& !string.IsNullOrWhiteSpace(unlockTarget["Name"]?.Value<string>());
		}

		private static int ReadCurrentMax(JObject root, string settingPath) {
			var current = GoalsService.GetByPath(root, settingPath);
			if (current == null || current.Type == JTokenType.Null)
				return 0;

			if (current.Type == JTokenType.Integer || current.Type == JTokenType.Float)
				return (int) current;

			return int.TryParse(current.ToString(), out var parsed) ? parsed : 0;
		}
	}
}
