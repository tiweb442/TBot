namespace TBot.WebUI.Models {
	public class GoalPresetModel {
		public string Id { get; set; } = "";
		public int Order { get; set; }
		public string Label { get; set; } = "";
		public string Description { get; set; } = "";
		public Dictionary<string, string> Apply { get; set; } = new();
	}
}
