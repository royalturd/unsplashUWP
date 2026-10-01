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
        public string Description { get; set; }
        public string AltDescription { get; set; }
        public string User { get; set; }
        public string UserName { get; set; }
        public string UserProfileUrl { get; set; }
        public string ImageUrl { get; set; }
        public string SmallImageUrl { get; set; }
        public string RegularImageUrl { get; set; }
        public string FullImageUrl { get; set; }
        public string RawImageUrl { get; set; }
        public string DownloadLocation { get; set; }
        public string PhotoPageUrl { get; set; }
        public string CreatedAt { get; set; }
        public string Color { get; set; }
        public string Tags { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Likes { get; set; }
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

                JsonObject root;
                try
                {
                    root = JsonValue.Parse(body).GetObject();
                }
                catch (Exception ex)
                {
                    throw new HttpRequestException("Unsplash returned invalid JSON (HTTP " + (int)response.StatusCode + "): " + body, ex);
                }

                if (!root.ContainsKey("results") || root["results"].ValueType != JsonValueType.Array)
                {
                    var detail = root.ContainsKey("errors") ? root["errors"].ToString() : body;
                    throw new HttpRequestException("Unsplash response did not include photo results (HTTP " + (int)response.StatusCode + "): " + detail);
                }

                var results = root["results"].GetArray();
                var photos = new ObservableCollection<UnsplashPhoto>();

                foreach (var item in results)
                {
                    var photoObject = item.GetObject();
                    var urls = GetObjectOrEmpty(photoObject, "urls");
                    var user = GetObjectOrEmpty(photoObject, "user");
                    var links = GetObjectOrEmpty(photoObject, "links");
                    var userLinks = GetObjectOrEmpty(user, "links");
                    var smallUrl = urls.GetNamedString("small", string.Empty);
                    var regularUrl = urls.GetNamedString("regular", smallUrl);
                    var fullUrl = urls.GetNamedString("full", regularUrl);
                    var altDescription = photoObject.GetNamedString("alt_description", string.Empty);
                    var description = photoObject.GetNamedString("description", string.Empty);
                    var tags = new List<string>();

                    if (photoObject.ContainsKey("tags") && photoObject["tags"].ValueType == JsonValueType.Array)
                    {
                        foreach (var tag in photoObject["tags"].GetArray())
                        {
                            if (tag.ValueType != JsonValueType.Object)
                            {
                                continue;
                            }

                            var tagTitle = tag.GetObject().GetNamedString("title", string.Empty);
                            if (!string.IsNullOrWhiteSpace(tagTitle))
                            {
                                tags.Add(tagTitle);
                            }
                        }
                    }

                    photos.Add(new UnsplashPhoto
                    {
                        Id = photoObject.GetNamedString("id", string.Empty),
                        Title = string.IsNullOrWhiteSpace(altDescription) ? "Unsplash photo" : altDescription,
                        Description = description,
                        AltDescription = altDescription,
                        User = user.GetNamedString("name", "Unsplash"),
                        UserName = user.GetNamedString("username", string.Empty),
                        UserProfileUrl = userLinks.GetNamedString("html", string.Empty),
                        ImageUrl = smallUrl,
                        SmallImageUrl = smallUrl,
                        RegularImageUrl = regularUrl,
                        FullImageUrl = fullUrl,
                        RawImageUrl = urls.GetNamedString("raw", fullUrl),
                        DownloadLocation = links.GetNamedString("download_location", string.Empty),
                        PhotoPageUrl = links.GetNamedString("html", string.Empty),
                        CreatedAt = photoObject.GetNamedString("created_at", string.Empty),
                        Color = photoObject.GetNamedString("color", string.Empty),
                        Tags = string.Join(", ", tags),
                        Width = (int)photoObject.GetNamedNumber("width", 0),
                        Height = (int)photoObject.GetNamedNumber("height", 0),
                        Likes = (int)photoObject.GetNamedNumber("likes", 0)
                    });
                }

                return photos;
            }
        }

        private static JsonObject GetObjectOrEmpty(JsonObject parent, string name)
        {
            return parent.ContainsKey(name) && parent[name].ValueType == JsonValueType.Object
                ? parent[name].GetObject()
                : new JsonObject();
        }

        public Task<byte[]> DownloadImageAsync(string imageUrl)
        {
            if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) || !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The selected image URL is invalid.");
            }

            return _httpClient.GetByteArrayAsync(uri);
        }

        public async Task RegisterDownloadAsync(UnsplashPhoto photo)
        {
            if (photo == null || !Uri.TryCreate(photo.DownloadLocation, UriKind.Absolute, out var uri) || !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Unsplash did not provide a valid download link for this photo.");
            }

            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            {
                request.Headers.Add("Accept-Version", "v1");
                request.Headers.Add("Authorization", IsAuthenticated ? $"Bearer {AccessToken}" : $"Client-ID {ClientId}");

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    throw new HttpRequestException("Unsplash download registration failed: " + body);
                }
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
