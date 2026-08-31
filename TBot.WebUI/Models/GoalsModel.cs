namespace TBot.WebUI.Models {
	public class GoalsInstanceModel {
		public string Alias { get; set; } = "";
		public string SettingsFile { get; set; } = "";
	}

	public class GoalsModel {
		public List<GoalsInstanceModel> Instances { get; set; } = new();
		public string SelectedSettingsFile { get; set; } = "";
		public string? ActiveGoal { get; set; }
		public string? ActiveGoalLabel { get; set; }
		public string? ActivatedAt { get; set; }
		public Dictionary<string, string> Baselines { get; set; } = new();
		public List<GoalPresetModel> Presets { get; set; } = new();
		public Dictionary<string, string> CurrentValues { get; set; } = new();
	}
}
