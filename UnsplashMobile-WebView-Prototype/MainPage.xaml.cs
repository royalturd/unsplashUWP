using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using UnsplashMobile.Api;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;

namespace UnsplashMobile
{
    public sealed partial class MainPage : Page
    {
        private const string ClientIdSettingName = "UnsplashClientId";
        private const string ClientSecretSettingName = "UnsplashClientSecret";
        private const string RedirectUriSettingName = "UnsplashRedirectUri";
        private const string DarkThemeSettingName = "UnsplashDarkTheme";

        private readonly UnsplashApiClient _apiClient = new UnsplashApiClient();

        public MainPage()
        {
            InitializeComponent();
            LoadSavedCredentials();
            var settings = ApplicationData.Current.LocalSettings;
            DarkThemeToggle.IsOn = settings.Values[DarkThemeSettingName] is bool && (bool)settings.Values[DarkThemeSettingName];
            ApplyTheme(DarkThemeToggle.IsOn);
            DarkThemeToggle.Toggled += DarkThemeToggle_Toggled;
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
            StartTurnstileEntrance();
            ApplyCredentialsFromInputs();
            StatusText.Text = "Loading featured photos...";
            await SearchAsync("travel");
        }

        private void StartTurnstileEntrance()
        {
            var projection = new PlaneProjection { CenterOfRotationX = 0.15 };
            PageRoot.Projection = projection;
            PageRoot.Opacity = 0;

            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var rotation = new DoubleAnimation
            {
                From = -55,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(650),
                EasingFunction = easing
            };
            Storyboard.SetTarget(rotation, projection);
            Storyboard.SetTargetProperty(rotation, "RotationY");

            var fade = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(500),
                EasingFunction = easing
            };
            Storyboard.SetTarget(fade, PageRoot);
            Storyboard.SetTargetProperty(fade, "Opacity");

            var storyboard = new Storyboard();
            storyboard.Children.Add(rotation);
            storyboard.Children.Add(fade);
            storyboard.Begin();
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

        private void DarkThemeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            ApplyTheme(DarkThemeToggle.IsOn);
            ApplicationData.Current.LocalSettings.Values[DarkThemeSettingName] = DarkThemeToggle.IsOn;
        }

        private void ApplyTheme(bool isDark)
        {
            RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            var photo = (sender as Button)?.DataContext as UnsplashPhoto;
            if (photo == null)
            {
                return;
            }

            var sizePicker = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            sizePicker.Items.Add(new ComboBoxItem { Content = "Small (optimized)", Tag = "small" });
            sizePicker.Items.Add(new ComboBoxItem { Content = "Regular (recommended)", Tag = "regular" });
            sizePicker.Items.Add(new ComboBoxItem { Content = "Full resolution", Tag = "full" });
            sizePicker.Items.Add(new ComboBoxItem { Content = "Original image", Tag = "raw" });
            sizePicker.SelectedIndex = 1;

            var dialogContent = new StackPanel();
            dialogContent.Children.Add(new TextBlock { Text = "Choose image size", Margin = new Thickness(0, 0, 0, 8) });
            dialogContent.Children.Add(sizePicker);

            var dialog = new ContentDialog
            {
                Title = "Download photo",
                Content = dialogContent,
                PrimaryButtonText = "Choose location",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            var selectedSize = (sizePicker.SelectedItem as ComboBoxItem)?.Tag as string ?? "regular";
            var imageUrl = GetImageUrl(photo, selectedSize);
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                ShowMessageDialog("Download unavailable", "Unsplash did not provide that image size.");
                return;
            }

            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                SuggestedFileName = string.IsNullOrWhiteSpace(photo.Id) ? "unsplash-image" : photo.Id
            };
            picker.FileTypeChoices.Add("JPEG image", new List<string> { ".jpg" });

            var file = await picker.PickSaveFileAsync();
            if (file == null)
            {
                return;
            }

            try
            {
                await _apiClient.RegisterDownloadAsync(photo);
                var imageBytes = await _apiClient.DownloadImageAsync(imageUrl);
                await FileIO.WriteBytesAsync(file, imageBytes);
                StatusText.Text = "Image saved: " + file.Name;
            }
            catch (Exception ex)
            {
                StatusText.Text = "Download failed: " + ex.Message;
                ShowMessageDialog("Download failed", ex.Message);
            }
        }

        private static string GetImageUrl(UnsplashPhoto photo, string size)
        {
            switch (size)
            {
                case "small":
                    return photo.SmallImageUrl;
                case "full":
                    return photo.FullImageUrl;
                case "raw":
                    return photo.RawImageUrl;
                default:
                    return photo.RegularImageUrl;
            }
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
            ClientSecretBox.Password = string.Empty;
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
            _apiClient.ClientSecret = ClientSecretBox.Password.Trim();
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
            ClientSecretBox.Password = settings.Values[ClientSecretSettingName] as string ?? string.Empty;
            RedirectUriBox.Text = settings.Values[RedirectUriSettingName] as string ?? "https://localhost/unsplash";
            ApplyCredentialsFromInputs();
        }
    }
}