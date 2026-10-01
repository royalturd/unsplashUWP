using System;
using System.Net.Http;
using System.Threading.Tasks;
using UnsplashMobile.Api;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace UnsplashMobile
{
    public sealed partial class MainPage : Page
    {
        private const string ClientIdSettingName = "UnsplashClientId";
        private const string ClientSecretSettingName = "UnsplashClientSecret";
        private const string RedirectUriSettingName = "UnsplashRedirectUri";

        private readonly UnsplashApiClient _apiClient = new UnsplashApiClient();

        public MainPage()
        {
            InitializeComponent();
            LoadSavedCredentials();
            Loaded += MainPage_Loaded;
            SearchButton.Click += SearchButton_Click;
            LoginButton.Click += LoginButton_Click;
            SaveButton.Click += SaveButton_Click;
            TestButton.Click += TestButton_Click;
            ClearButton.Click += ClearButton_Click;
            SearchBox.KeyDown += SearchBox_KeyDown;
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyCredentialsFromInputs();
            StatusText.Text = "Loading featured photos...";
            await SearchAsync("travel");
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyCredentialsFromInputs();
            await SearchAsync(SearchBox.Text);
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ApplyCredentialsFromInputs();
                var success = await _apiClient.LoginAsync();
                StatusText.Text = success ? "Connected to Unsplash" : "Login cancelled";
                if (success)
                {
                    await SearchAsync(SearchBox.Text);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "Login failed: " + ex.Message;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveCredentials();
            StatusText.Text = "Credentials saved locally";
        }

        private async void TestButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ApplyCredentialsFromInputs();
                var photos = await _apiClient.SearchPhotosAsync("nature");
                StatusText.Text = photos.Count > 0 ? "Connection works; sample photos loaded." : "Connection works; no sample photos returned.";
            }
            catch (HttpRequestException ex)
            {
                StatusText.Text = "Unsplash validation failed: " + ex.Message;
                ShowMessageDialog("Unsplash validation failed", ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                StatusText.Text = "Missing key data: " + ex.Message;
                ShowMessageDialog("Missing key data", ex.Message);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Test failed: " + ex.Message;
                ShowMessageDialog("Test failed", ex.Message);
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            ClientIdBox.Text = string.Empty;
            ClientSecretBox.Text = string.Empty;
            RedirectUriBox.Text = "https://localhost/unsplash";

            var settings = ApplicationData.Current.LocalSettings;
            settings.Values.Remove(ClientIdSettingName);
            settings.Values.Remove(ClientSecretSettingName);
            settings.Values.Remove(RedirectUriSettingName);

            ApplyCredentialsFromInputs();
            StatusText.Text = "Saved credentials cleared";
            ShowMessageDialog("Credentials cleared", "The saved Unsplash key and secret were removed from this device.");
        }

        private async void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                await SearchAsync(SearchBox.Text);
            }
        }

        private async Task SearchAsync(string query)
        {
            try
            {
                ApplyCredentialsFromInputs();
                var searchText = string.IsNullOrWhiteSpace(query) ? "travel" : query.Trim();
                StatusText.Text = "Loading photos...";
                var photos = await _apiClient.SearchPhotosAsync(searchText);
                PhotoList.ItemsSource = photos;
                StatusText.Text = photos.Count > 0 ? "Loaded " + photos.Count + " photos" : "No matching photos found";
            }
            catch (Exception ex)
            {
                StatusText.Text = "API error: " + ex.Message;
                ShowMessageDialog("Unsplash API error", ex.Message);
            }
        }

        private async void ShowMessageDialog(string title, string content)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = "OK"
            };

            await dialog.ShowAsync();
        }

        private void ApplyCredentialsFromInputs()
        {
            _apiClient.ClientId = ClientIdBox.Text.Trim();
            _apiClient.ClientSecret = ClientSecretBox.Text.Trim();
            _apiClient.RedirectUri = string.IsNullOrWhiteSpace(RedirectUriBox.Text)
                ? "https://localhost/unsplash"
                : RedirectUriBox.Text.Trim();
        }

        private void SaveCredentials()
        {
            ApplyCredentialsFromInputs();
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values[ClientIdSettingName] = _apiClient.ClientId;
            settings.Values[ClientSecretSettingName] = _apiClient.ClientSecret;
            settings.Values[RedirectUriSettingName] = _apiClient.RedirectUri;
        }

        private void LoadSavedCredentials()
        {
            var settings = ApplicationData.Current.LocalSettings;
            ClientIdBox.Text = settings.Values[ClientIdSettingName] as string ?? string.Empty;
            ClientSecretBox.Text = settings.Values[ClientSecretSettingName] as string ?? string.Empty;
            RedirectUriBox.Text = settings.Values[RedirectUriSettingName] as string ?? "https://localhost/unsplash";
            ApplyCredentialsFromInputs();
        }
    }
}