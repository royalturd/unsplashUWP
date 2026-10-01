using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Security.Authentication.Web;

namespace UnsplashMobile.Api
{
    public sealed class UnsplashPhoto
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string User { get; set; }
        public string ImageUrl { get; set; }
    }

    public sealed class UnsplashApiClient
    {
        public string ClientId { get; set; } = "YOUR_UNSPLASH_ACCESS_KEY";
        public string ClientSecret { get; set; } = "YOUR_UNSPLASH_SECRET_KEY";
        public string RedirectUri { get; set; } = "https://localhost/unsplash";
        public string AccessToken { get; private set; }

        private readonly HttpClient _httpClient = new HttpClient();

        public bool IsAuthenticated => !string.IsNullOrWhiteSpace(AccessToken);

        public async Task<bool> LoginAsync()
        {
            if (string.IsNullOrWhiteSpace(ClientId) || ClientId.StartsWith("YOUR_"))
            {
                throw new InvalidOperationException("Set your Unsplash access key and secret before enabling OAuth login.");
            }

            var authorizeUri = new Uri(
                $"https://unsplash.com/oauth/authorize?client_id={Uri.EscapeDataString(ClientId)}&redirect_uri={Uri.EscapeDataString(RedirectUri)}&response_type=code&scope=public+read_user+write_likes");

            var result = await WebAuthenticationBroker.AuthenticateAsync(
                WebAuthenticationOptions.None,
                authorizeUri,
                new Uri(RedirectUri));

            if (result.ResponseStatus != WebAuthenticationStatus.Success)
            {
                return false;
            }

            var code = ParseCode(result.ResponseData);
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new InvalidOperationException("Unsplash did not return an authorization code.");
            }

            var tokenResult = await ExchangeCodeForTokenAsync(code);
            AccessToken = tokenResult.GetNamedString("access_token", string.Empty);
            return !string.IsNullOrWhiteSpace(AccessToken);
        }

        public async Task<ObservableCollection<UnsplashPhoto>> SearchPhotosAsync(string query)
        {
            var actualQuery = string.IsNullOrWhiteSpace(query) ? "travel" : query.Trim();
            var uri = new Uri($"https://api.unsplash.com/search/photos?query={Uri.EscapeDataString(actualQuery)}&per_page=10&order_by=popular");

            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            {
                request.Headers.Add("Accept-Version", "v1");
                request.Headers.Add("Authorization", IsAuthenticated ? $"Bearer {AccessToken}" : $"Client-ID {ClientId}");

                var response = await _httpClient.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException("Unsplash query failed: " + body);
                }

                var root = JsonValue.Parse(body).GetObject();
                var results = root.GetNamedArray("results");
                var photos = new ObservableCollection<UnsplashPhoto>();

                foreach (var item in results)
                {
                    var photoObject = item.GetObject();
                    var urls = photoObject.GetNamedObject("urls");
                    var user = photoObject.GetNamedObject("user");

                    photos.Add(new UnsplashPhoto
                    {
                        Id = photoObject.GetNamedString("id", string.Empty),
                        Title = photoObject.GetNamedString("alt_description", "Unsplash photo"),
                        User = user.GetNamedString("name", "Unsplash"),
                        ImageUrl = urls.GetNamedString("small", urls.GetNamedString("regular", string.Empty))
                    });
                }

                return photos;
            }
        }

        private async Task<JsonObject> ExchangeCodeForTokenAsync(string code)
        {
            using (var payload = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", ClientId),
                new KeyValuePair<string, string>("client_secret", ClientSecret),
                new KeyValuePair<string, string>("redirect_uri", RedirectUri),
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("grant_type", "authorization_code")
            }))
            {
                var response = await _httpClient.PostAsync("https://unsplash.com/oauth/token", payload);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException("OAuth exchange failed: " + body);
                }

                return JsonValue.Parse(body).GetObject();
            }
        }

        private static string ParseCode(string responseUri)
        {
            if (string.IsNullOrWhiteSpace(responseUri))
            {
                return string.Empty;
            }

            var clean = responseUri.Trim();
            var questionIndex = clean.IndexOf('?');
            if (questionIndex < 0)
            {
                return string.Empty;
            }

            var query = clean.Substring(questionIndex + 1);
            foreach (var segment in query.Split('&'))
            {
                var keyValue = segment.Split(new[] { '=' }, 2);
                if (keyValue.Length == 2 && keyValue[0] == "code")
                {
                    return Uri.UnescapeDataString(keyValue[1]);
                }
            }

            return string.Empty;
        }
    }
}
