using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tbot.Common.Helpers;
using Tbot.Common.Settings;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Models;
using TBot.WebUI.Models;
using TBot.WebUI.Services;

namespace TBot.WebUI.Controllers {
	public class GoalsController : Controller {
		private string GetCurrentDirectory() {
			return AppDomain.CurrentDomain.BaseDirectory;
		}

		private async Task<List<GoalsInstanceModel>> GetInstances() {
			var instances = new List<GoalsInstanceModel>();
			var settingsFile = await SettingsService.GetSettings(SettingsService.GlobalSettingsPath);

			if (!SettingsService.IsSettingSet(settingsFile, "Instances")) {
				instances.Add(new GoalsInstanceModel {
					Alias = "MAIN",
					SettingsFile = new FileInfo(SettingsService.GlobalSettingsPath).Name
				});
				return instances;
			}

			foreach (var instance in settingsFile.Instances) {
				instances.Add(new GoalsInstanceModel {
					Alias = (string) instance.Alias,
					SettingsFile = (string) instance.Settings
				});
			}

			return instances;
		}

		private static string FormatToken(JToken? token) {
			if (token == null || token.Type == JTokenType.Null)
				return "(not set)";
			if (token.Type == JTokenType.String)
				return token.Value<string>() ?? "";
			return token.ToString(Formatting.None);
		}

		private static Dictionary<string, string> TokenObjectToDisplay(JObject? obj) {
			var result = new Dictionary<string, string>();
			if (obj == null)
				return result;

			foreach (var prop in obj.Properties())
				result[prop.Name] = FormatToken(prop.Value);

			return result;
		}

		private static object ToSleepPayload(SleepModeStatus sleep) {
			return new {
				sleepModeActive = sleep.SleepModeActive,
				isSleeping = sleep.IsSleeping,
				goToSleep = sleep.GoToSleep,
				wakeUp = sleep.WakeUp,
				nextWakeUp = sleep.NextWakeUp?.ToString("o"),
				message = sleep.Message
			};
		}

		private static object ToSleepPayload(GoalsModel model) {
			return new {
				sleepModeActive = model.SleepModeActive,
				isSleeping = model.IsSleeping,
				goToSleep = model.SleepGoToSleep,
				wakeUp = model.SleepWakeUp,
				nextWakeUp = model.NextWakeUp,
				message = model.SleepMessage
			};
		}

		private static void ApplySleepToModel(GoalsModel model, SleepModeStatus sleep) {
			model.SleepModeActive = sleep.SleepModeActive;
			model.IsSleeping = sleep.IsSleeping;
			model.SleepGoToSleep = sleep.GoToSleep;
			model.SleepWakeUp = sleep.WakeUp;
			model.NextWakeUp = sleep.NextWakeUp?.ToString("o");
			model.SleepMessage = sleep.Message;
		}

		private async Task<(string host, int port, string user, string pass)> GetOgamedConnection(string settingsPath) {
			var settings = await SettingsService.GetSettings(settingsPath);
			var host = (string) settings.General.Host ?? "127.0.0.1";
			var port = int.TryParse((string) settings.General.Port, out var parsedPort) ? parsedPort : 8080;
			var basicAuthUser = "";
			var basicAuthPass = "";
			try {
				basicAuthUser = (string) settings.Credentials.BasicAuth.Username ?? "";
				basicAuthPass = (string) settings.Credentials.BasicAuth.Password ?? "";
			} catch {
				// Basic auth optional
			}
			return (host, port, basicAuthUser, basicAuthPass);
		}

		private async Task<DateTime> TryGetServerTime(string settingsPath) {
			try {
				var (host, port, user, pass) = await GetOgamedConnection(settingsPath);
				using var client = new OgamedLiveClient(host, port, user, pass);
				return await client.GetServerTimeAsync();
			} catch {
				return DateTime.Now;
			}
		}

		private async Task<GoalsModel> BuildGoalsModel(string? instanceSettings) {
			var instances = await GetInstances();
			var selectedFile = instanceSettings;

			if (string.IsNullOrWhiteSpace(selectedFile))
				selectedFile = instances.FirstOrDefault()?.SettingsFile ?? "";

			var model = new GoalsModel {
				Instances = instances,
				SelectedSettingsFile = selectedFile
			};

			if (string.IsNullOrWhiteSpace(selectedFile))
				return model;

			var settingsPath = Path.Combine(GetCurrentDirectory(), selectedFile);
			var contents = await SettingsService.GetSettingsFileContents(settingsPath);
			var root = JsonConvert.DeserializeObject<JObject>(contents) ?? new JObject();
			var now = await TryGetServerTime(settingsPath);
			ApplySleepToModel(model, SleepModeHelper.ResolveFromSettings(root, now));

			var goals = root["Goals"] as JObject;

			if (goals == null)
				return model;

			model.ActiveGoal = goals["ActiveGoal"]?.Type == JTokenType.Null ? null : goals["ActiveGoal"]?.Value<string>();
			model.ActivatedAt = goals["ActivatedAt"]?.Type == JTokenType.Null ? null : goals["ActivatedAt"]?.Value<string>();
			model.Baselines = TokenObjectToDisplay(goals["Baselines"] as JObject);

			var presets = goals["Presets"] as JObject;
			if (presets != null) {
				foreach (var presetProp in presets.Properties()) {
					var presetObj = presetProp.Value as JObject;
					if (presetObj == null)
						continue;

					var applyObj = presetObj["Apply"] as JObject;
					var apply = TokenObjectToDisplay(applyObj);
					var unlockTargetObj = presetObj["UnlockTarget"] as JObject;
					var unlockTarget = TokenObjectToDisplay(unlockTargetObj);
					var mergedApply = GoalsFocusHelper.BuildMergedApply(presetObj, root);
					var focusPreview = TokenObjectToDisplay(mergedApply);

					model.Presets.Add(new GoalPresetModel {
						Id = presetProp.Name,
						Order = presetObj["Order"]?.Value<int>() ?? int.MaxValue,
						Label = presetObj["Label"]?.Value<string>() ?? presetProp.Name,
						Description = presetObj["Description"]?.Value<string>() ?? "",
						UnlockTarget = unlockTarget,
						Apply = apply,
						FocusPreview = focusPreview
					});
				}

				model.Presets = model.Presets
					.OrderBy(p => p.Order)
					.ThenBy(p => p.Id, StringComparer.Ordinal)
					.ToList();
			}

			if (!string.IsNullOrWhiteSpace(model.ActiveGoal)) {
				var activePreset = model.Presets.FirstOrDefault(p => p.Id == model.ActiveGoal);
				model.ActiveGoalLabel = activePreset?.Label ?? model.ActiveGoal;

				if (activePreset != null) {
					foreach (var key in activePreset.FocusPreview.Keys)
						model.CurrentValues[key] = FormatToken(GoalsService.GetByPath(root, key));
				}
			} else {
				foreach (var preset in model.Presets) {
					foreach (var key in preset.FocusPreview.Keys) {
						if (!model.CurrentValues.ContainsKey(key))
							model.CurrentValues[key] = FormatToken(GoalsService.GetByPath(root, key));
					}
				}
			}

			return model;
		}

		[HttpGet]
		public async Task<IActionResult> Index(string? instanceSettings) {
			var model = await BuildGoalsModel(instanceSettings);
			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> GetState(string instanceSettings) {
			var model = await BuildGoalsModel(instanceSettings);
			return Json(new {
				activeGoal = model.ActiveGoal,
				activeGoalLabel = model.ActiveGoalLabel,
				activatedAt = model.ActivatedAt,
				baselines = model.Baselines,
				presets = model.Presets.Select(p => new {
					id = p.Id,
					order = p.Order,
					label = p.Label,
					description = p.Description,
					unlockTarget = p.UnlockTarget,
					apply = p.Apply,
					focusPreview = p.FocusPreview
				}),
				currentValues = model.CurrentValues,
				sleep = ToSleepPayload(model)
			});
		}

		[HttpGet]
		public async Task<IActionResult> GetPresetStatus(string instanceSettings) {
			if (string.IsNullOrWhiteSpace(instanceSettings))
				return Json(new { offline = true, presets = Array.Empty<object>(), sleep = ToSleepPayload(new SleepModeStatus()) });

			try {
				var settingsPath = Path.Combine(GetCurrentDirectory(), instanceSettings);
				var contents = await SettingsService.GetSettingsFileContents(settingsPath);
				var root = JsonConvert.DeserializeObject<JObject>(contents) ?? new JObject();
				var goals = root["Goals"] as JObject;
				var presetsObj = goals?["Presets"] as JObject;

				if (presetsObj == null) {
					var emptySleep = SleepModeHelper.ResolveFromSettings(root, DateTime.Now);
					return Json(new { offline = false, presets = Array.Empty<object>(), sleep = ToSleepPayload(emptySleep) });
				}

				var (host, port, basicAuthUser, basicAuthPass) = await GetOgamedConnection(settingsPath);

				Researches researches;
				List<Planet> planets;
				List<Fleet> fleets;
				DateTime serverTime;
				using (var client = new OgamedLiveClient(host, port, basicAuthUser, basicAuthPass)) {
					researches = await client.GetResearchesAsync();
					planets = await client.GetPlanetsWithShipsAndFacilitiesAsync();
					fleets = await client.GetFleetsAsync();
					serverTime = await client.GetServerTimeAsync();
				}

				var sleep = SleepModeHelper.ResolveFromSettings(root, serverTime);
				var celestials = planets.Cast<Celestial>().ToList();
				var presetStatuses = new List<object>();

				foreach (var presetProp in presetsObj.Properties()) {
					var presetObj = presetProp.Value as JObject;
					if (presetObj == null)
						continue;

					var unlockTarget = presetObj["UnlockTarget"] as JObject;
					var completeWhen = presetObj["CompleteWhen"] as JObject;
					var hasUnlockTarget = unlockTarget != null
						&& !string.IsNullOrWhiteSpace(unlockTarget["Type"]?.Value<string>())
						&& !string.IsNullOrWhiteSpace(unlockTarget["Name"]?.Value<string>());
					if (!hasUnlockTarget && (completeWhen == null || !completeWhen.Properties().Any())) {
						presetStatuses.Add(new {
							id = presetProp.Name,
							completed = false,
							progress = (object?) null
						});
						continue;
					}

					var completed = GoalCompletionEvaluator.EvaluatePreset(presetObj, researches, celestials, fleets);
					var progress = GoalCompletionEvaluator.GetPresetProgress(presetObj, researches, celestials, fleets)
						.ToDictionary(
							kvp => kvp.Key,
							kvp => new { current = kvp.Value.Current, required = kvp.Value.Required });

					presetStatuses.Add(new {
						id = presetProp.Name,
						completed,
						progress
					});
				}

				return Json(new { offline = false, presets = presetStatuses, sleep = ToSleepPayload(sleep) });
			} catch {
				try {
					var settingsPath = Path.Combine(GetCurrentDirectory(), instanceSettings);
					var contents = await SettingsService.GetSettingsFileContents(settingsPath);
					var root = JsonConvert.DeserializeObject<JObject>(contents) ?? new JObject();
					var sleep = SleepModeHelper.ResolveFromSettings(root, DateTime.Now);
					var fallbackPresets = await GetPresetIdsWithoutStatus(instanceSettings);
					return Json(new {
						offline = true,
						presets = fallbackPresets.Select(id => new { id, completed = false, progress = (object?) null }),
						sleep = ToSleepPayload(sleep)
					});
				} catch {
					var fallbackPresets = await GetPresetIdsWithoutStatus(instanceSettings);
					return Json(new {
						offline = true,
						presets = fallbackPresets.Select(id => new { id, completed = false, progress = (object?) null }),
						sleep = ToSleepPayload(new SleepModeStatus())
					});
				}
			}
		}

		private async Task<List<string>> GetPresetIdsWithoutStatus(string instanceSettings) {
			try {
				var settingsPath = Path.Combine(GetCurrentDirectory(), instanceSettings);
				var contents = await SettingsService.GetSettingsFileContents(settingsPath);
				var root = JsonConvert.DeserializeObject<JObject>(contents) ?? new JObject();
				var presetsObj = root["Goals"]?["Presets"] as JObject;
				if (presetsObj == null)
					return new List<string>();
				return presetsObj.Properties().Select(p => p.Name).ToList();
			} catch {
				return new List<string>();
			}
		}

		[HttpPost]
		public async Task<IActionResult> Activate(string instanceSettings, string presetId) {
			try {
				if (string.IsNullOrWhiteSpace(instanceSettings) || string.IsNullOrWhiteSpace(presetId))
					return Json(new { success = false, error = "Instance settings and preset are required." });

				var settingsPath = Path.Combine(GetCurrentDirectory(), instanceSettings);
				var contents = await SettingsService.GetSettingsFileContents(settingsPath);
				var root = JsonConvert.DeserializeObject<JObject>(contents) ?? new JObject();
				var goals = root["Goals"] as JObject;

				if (goals == null)
					return Json(new { success = false, error = "Goals section is missing from instance settings." });

				var activeGoal = goals["ActiveGoal"];
				if (activeGoal != null && activeGoal.Type != JTokenType.Null && !string.IsNullOrWhiteSpace(activeGoal.Value<string>()))
					return Json(new { success = false, error = "A goal is already active. Restore it before activating another." });

				var preset = goals["Presets"]?[presetId] as JObject;
				if (preset == null)
					return Json(new { success = false, error = $"Preset '{presetId}' was not found." });

				var apply = preset["Apply"] as JObject;
				if (apply == null || !apply.Properties().Any())
					return Json(new { success = false, error = "Preset has no Apply patches." });

				var mergedApply = GoalsFocusHelper.BuildMergedApply(preset, root);
				goals["Baselines"] = GoalsService.SnapshotBaselines(root, mergedApply);
				GoalsService.ApplyPatches(root, mergedApply);
				goals["ActiveGoal"] = presetId;
				goals["ActivatedAt"] = DateTime.UtcNow.ToString("o");
				root["Goals"] = goals;

				await SettingsService.WriteSettings(settingsPath, root.ToString(Formatting.Indented));
				return Json(new { success = true });
			} catch (Exception ex) {
				return Json(new { success = false, error = ex.Message });
			}
		}

		[HttpPost]
		public async Task<IActionResult> Restore(string instanceSettings) {
			try {
				if (string.IsNullOrWhiteSpace(instanceSettings))
					return Json(new { success = false, error = "Instance settings file is required." });

				var settingsPath = Path.Combine(GetCurrentDirectory(), instanceSettings);
				var contents = await SettingsService.GetSettingsFileContents(settingsPath);
				var root = JsonConvert.DeserializeObject<JObject>(contents) ?? new JObject();
				var goals = root["Goals"] as JObject;

				if (goals == null)
					return Json(new { success = false, error = "Goals section is missing from instance settings." });

				var baselines = goals["Baselines"] as JObject;
				if (baselines == null || !baselines.Properties().Any())
					return Json(new { success = false, error = "No baselines to restore." });

				if (!GoalsService.TryRestoreActiveGoal(root, out _, out var restoreError))
					return Json(new { success = false, error = restoreError });

				await SettingsService.WriteSettings(settingsPath, root.ToString(Formatting.Indented));
				return Json(new { success = true });
			} catch (Exception ex) {
				return Json(new { success = false, error = ex.Message });
			}
		}
	}
}
