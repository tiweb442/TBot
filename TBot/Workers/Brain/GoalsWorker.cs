using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tbot.Common.Settings;
using Tbot.Helpers;
using TBot.Ogame.Infrastructure;
using Tbot.Services;
using TBot.Common.Logging;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;

namespace Tbot.Workers.Brain {
	public class GoalsWorker : WorkerBase {
		private readonly IOgameService _ogameService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;

		public GoalsWorker(ITBotMain parentInstance,
			IOgameService ogameService,
			ITBotOgamedBridge tbotOgameBridge) :
			base(parentInstance) {
			_ogameService = ogameService;
			_tbotOgameBridge = tbotOgameBridge;
		}

		protected override async Task Execute() {
			try {
				string activeGoalId = GetActiveGoalId();
				if (string.IsNullOrWhiteSpace(activeGoalId)) {
					DoLog(LogLevel.Debug, "No active goal. Skipping Goals check.");
					return;
				}

				DoLog(LogLevel.Information, $"Checking goal completion for '{activeGoalId}'...");

				var settingsPath = _tbotInstance.InstanceSettingsPath;
				var contents = await SettingsService.GetSettingsFileContents(Path.GetFullPath(settingsPath));
				var root = JsonConvert.DeserializeObject<JObject>(contents) ?? new JObject();
				var goals = root["Goals"] as JObject;
				if (goals == null) {
					DoLog(LogLevel.Warning, "Goals section missing from settings file.");
					return;
				}

				var preset = goals["Presets"]?[activeGoalId] as JObject;
				if (preset == null) {
					DoLog(LogLevel.Warning, $"Active goal preset '{activeGoalId}' was not found.");
					return;
				}

				var unlockTarget = preset["UnlockTarget"] as JObject;
				var completeWhen = preset["CompleteWhen"] as JObject;
				var hasUnlockTarget = unlockTarget != null
					&& !string.IsNullOrWhiteSpace(unlockTarget["Type"]?.Value<string>())
					&& !string.IsNullOrWhiteSpace(unlockTarget["Name"]?.Value<string>());
				if (!hasUnlockTarget && (completeWhen == null || !completeWhen.Properties().Any())) {
					DoLog(LogLevel.Warning, $"Preset '{activeGoalId}' has no UnlockTarget or CompleteWhen conditions.");
					return;
				}

				_tbotInstance.UserData.researches = await _ogameService.GetResearches();
				_tbotInstance.UserData.celestials = await _tbotOgameBridge.UpdateCelestials();

				var celestials = _tbotInstance.UserData.celestials.ToList();
				for (var i = 0; i < celestials.Count; i++) {
					if (celestials[i].Coordinate.Type == Celestials.Moon)
						continue;
					celestials[i] = await _tbotOgameBridge.UpdatePlanet(celestials[i], UpdateTypes.Techs);
				}
				_tbotInstance.UserData.celestials = celestials;
				var fleets = await _ogameService.GetFleets();

				if (!GoalCompletionEvaluator.EvaluatePreset(preset, _tbotInstance.UserData.researches, celestials, fleets)) {
					DoLog(LogLevel.Information, $"Goal '{activeGoalId}' is not complete yet.");
					return;
				}

				var presetLabel = preset["Label"]?.Value<string>() ?? activeGoalId;
				if (!GoalsService.TryRestoreActiveGoal(root, out var completedGoalId, out var restoreError)) {
					DoLog(LogLevel.Warning, $"Goal '{activeGoalId}' complete but restore failed: {restoreError}");
					return;
				}

				await SettingsService.WriteSettings(Path.GetFullPath(settingsPath), root.ToString(Formatting.Indented));
				DoLog(LogLevel.Information, $"Goal '{completedGoalId}' ({presetLabel}) completed. Settings restored to baseline.");
				await _tbotInstance.SendTelegramMessage($"Goal completed: <b>{presetLabel}</b> ({completedGoalId}). Settings restored.");
			} catch (Exception e) {
				DoLog(LogLevel.Error, $"GoalsWorker exception: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!_tbotInstance.UserData.isSleeping && IsWorkerEnabledBySettings()) {
					var time = await _tbotOgameBridge.GetDateTime();
					var interval = GetCheckIntervalMs();
					var newTime = time.AddMilliseconds(interval);
					ChangeWorkerPeriod(interval);
					DoLog(LogLevel.Information, $"Next Goals check at {newTime}");
				}
				await _tbotOgameBridge.CheckCelestials();
			}
		}

		private string GetActiveGoalId() {
			try {
				var activeGoal = _tbotInstance.InstanceSettings.Goals.ActiveGoal;
				if (activeGoal == null)
					return null;
				var goalId = activeGoal.ToString();
				return string.IsNullOrWhiteSpace(goalId) ? null : goalId;
			} catch (Exception) {
				return null;
			}
		}

		private long GetCheckIntervalMs() {
			try {
				return RandomizeHelper.CalcRandomInterval(
					(int) _tbotInstance.InstanceSettings.Goals.CheckIntervalMin,
					(int) _tbotInstance.InstanceSettings.Goals.CheckIntervalMax);
			} catch (Exception) {
				return RandomizeHelper.CalcRandomInterval(120, 300);
			}
		}

		public override bool IsWorkerEnabledBySettings() {
			return !string.IsNullOrWhiteSpace(GetActiveGoalId());
		}

		public override string GetWorkerName() {
			return "Goals";
		}

		public override Feature GetFeature() {
			return Feature.BrainGoals;
		}

		public override LogSender GetLogSender() {
			return LogSender.Goals;
		}
	}

}
