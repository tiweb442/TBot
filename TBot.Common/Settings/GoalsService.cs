using Newtonsoft.Json.Linq;

namespace Tbot.Common.Settings {
	public static class GoalsService {
		public static JToken? GetByPath(JObject root, string dotPath) {
			var parts = dotPath.Split('.');
			JToken? current = root;

			foreach (var part in parts) {
				if (current is not JObject obj)
					return null;
				if (!obj.TryGetValue(part, out current))
					return null;
			}

			return current;
		}

		public static void SetByPath(JObject root, string dotPath, JToken? value) {
			if (value == null || value.Type == JTokenType.Null) {
				RemoveByPath(root, dotPath);
				return;
			}

			var parts = dotPath.Split('.');
			JObject current = root;

			for (var i = 0; i < parts.Length - 1; i++) {
				var part = parts[i];
				if (current[part] is not JObject child) {
					child = new JObject();
					current[part] = child;
				}
				current = child;
			}

			current[parts[^1]] = value;
		}

		public static void RemoveByPath(JObject root, string dotPath) {
			var parts = dotPath.Split('.');
			JToken? current = root;

			for (var i = 0; i < parts.Length - 1; i++) {
				if (current is not JObject obj || !obj.TryGetValue(parts[i], out current))
					return;
			}

			if (current is JObject leaf)
				leaf.Remove(parts[^1]);
		}

		public static JObject SnapshotBaselines(JObject root, JObject apply) {
			var baselines = new JObject();
			foreach (var prop in apply.Properties()) {
				var current = GetByPath(root, prop.Name);
				baselines[prop.Name] = current?.DeepClone() ?? JValue.CreateNull();
			}
			return baselines;
		}

		public static void ApplyPatches(JObject root, JObject apply) {
			foreach (var prop in apply.Properties())
				SetByPath(root, prop.Name, prop.Value.DeepClone());
		}

		public static void RestoreBaselines(JObject root, JObject baselines) {
			foreach (var prop in baselines.Properties())
				SetByPath(root, prop.Name, prop.Value.DeepClone());
		}

		public static bool TryRestoreActiveGoal(JObject root, out string? completedGoalId, out string? error) {
			completedGoalId = null;
			error = null;

			var goals = root["Goals"] as JObject;
			if (goals == null) {
				error = "Goals section is missing from instance settings.";
				return false;
			}

			completedGoalId = goals["ActiveGoal"]?.Type == JTokenType.Null ? null : goals["ActiveGoal"]?.Value<string>();
			if (string.IsNullOrWhiteSpace(completedGoalId)) {
				error = "No active goal to restore.";
				return false;
			}

			var baselines = goals["Baselines"] as JObject;
			if (baselines == null || !baselines.Properties().Any()) {
				error = "No baselines to restore.";
				return false;
			}

			RestoreBaselines(root, baselines);
			goals["ActiveGoal"] = JValue.CreateNull();
			goals["ActivatedAt"] = JValue.CreateNull();
			goals["Baselines"] = new JObject();
			root["Goals"] = goals;
			return true;
		}
	}
}
