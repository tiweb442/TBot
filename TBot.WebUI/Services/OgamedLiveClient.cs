using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TBot.Ogame.Infrastructure.Models;
using TBot.Ogame.Infrastructure.Enums;

namespace TBot.WebUI.Services {
	public class OgamedLiveClient : IDisposable {
		private readonly HttpClient _client;

		public OgamedLiveClient(string host, int port, string basicAuthUsername = "", string basicAuthPassword = "") {
			_client = new HttpClient {
				BaseAddress = new Uri($"http://{host}:{port}/"),
				Timeout = TimeSpan.FromSeconds(30)
			};

			if (!string.IsNullOrEmpty(basicAuthUsername) && !string.IsNullOrEmpty(basicAuthPassword)) {
				_client.DefaultRequestHeaders.Authorization =
					new AuthenticationHeaderValue("Basic",
						Convert.ToBase64String(Encoding.ASCII.GetBytes($"{basicAuthUsername}:{basicAuthPassword}")));
			}
		}

		public async Task<Researches> GetResearchesAsync() {
			return await GetAsync<Researches>("/bot/get-research");
		}

		public async Task<List<Planet>> GetPlanetsAsync() {
			return await GetAsync<List<Planet>>("/bot/planets") ?? new List<Planet>();
		}

		public async Task<Ships> GetShipsAsync(int planetId) {
			return await GetAsync<Ships>($"/bot/planets/{planetId}/ships");
		}

		public async Task<List<Planet>> GetPlanetsWithShipsAsync() {
			var planets = await GetPlanetsAsync();
			foreach (var planet in planets)
				planet.Ships = await GetShipsAsync(planet.ID);
			return planets;
		}

		public async Task<Facilities> GetFacilitiesAsync(int planetId) {
			return await GetAsync<Facilities>($"/bot/planets/{planetId}/facilities");
		}

		public async Task<List<Planet>> GetPlanetsWithShipsAndFacilitiesAsync() {
			var planets = await GetPlanetsAsync();
			foreach (var planet in planets) {
				planet.Ships = await GetShipsAsync(planet.ID);
				planet.Facilities = await GetFacilitiesAsync(planet.ID);
			}
			return planets;
		}

		public async Task<List<Fleet>> GetFleetsAsync() {
			return await GetAsync<List<Fleet>>("/bot/fleets") ?? new List<Fleet>();
		}

		public async Task<DateTime> GetServerTimeAsync() {
			return await GetAsync<DateTime>("/bot/server/time");
		}

		private async Task<T> GetAsync<T>(string resource) {
			using var response = await _client.GetAsync(resource);
			response.EnsureSuccessStatusCode();
			var json = await response.Content.ReadAsStringAsync();
			var envelope = JsonConvert.DeserializeObject<OgamedResponse>(json);
			if (envelope?.Status != null) {
				if (envelope.Status != "ok")
					throw new InvalidOperationException($"Ogamed error: {envelope.Message}");
				if (envelope.Result is JObject jObject)
					return jObject.ToObject<T>()!;
				if (envelope.Result is JArray jArray)
					return jArray.ToObject<T>()!;
				if (envelope.Result == null)
					return default!;
				return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(envelope.Result))!;
			}

			return JsonConvert.DeserializeObject<T>(json)!;
		}

		public void Dispose() {
			_client.Dispose();
		}
	}
}
