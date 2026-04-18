using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace CraftHub.Services
{
    public static class NetManager
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        static NetManager()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "CraftHub-Updater");
        }
        
        public static async Task<HttpResponseMessage> Get(string url)
        {
            var response = await _httpClient.GetAsync(url);
            return response;
        }

        public static async Task<T> ParseHttpResponseMessage<T>(HttpResponseMessage response)
        {
            if (response == null)
                return default;

            var content = await response.Content.ReadAsStringAsync();
            var jsonData = JsonSerializer.Deserialize<T>(content);
            return jsonData;
        }

    }
}
