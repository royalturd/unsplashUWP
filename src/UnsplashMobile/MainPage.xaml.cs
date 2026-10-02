using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using UnsplashMobile.Api;
using Windows.Data.Json;
using Windows.Data.Xml.Dom;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Notifications;

namespace UnsplashMobile
{
    public sealed partial class MainPage : Page
    {
        private const string ClientIdSettingName = "UnsplashClientId";
        private const string ClientSecretSettingName = "UnsplashClientSecret";
        private const string RedirectUriSettingName = "UnsplashRedirectUri";
        private const string DarkThemeSettingName = "UnsplashDarkTheme";
        private const string RecentPhotosFileName = "unsplash-recent.json";
        private const string SavedPhotosFileName = "unsplash-saved.json";
        private const string DailyTileDateSettingName = "UnsplashDailyTileDate";
        private const string DailyTileTopicSettingName = "UnsplashDailyTileTopic";
        private const string DailyTileEnabledSettingName = "UnsplashDailyTileEnabled";
        private const int SearchPageSize = 10;

        private readonly UnsplashApiClient _apiClient = new UnsplashApiClient();
        private readonly ObservableCollection<UnsplashPhoto> _recentPhotos = new ObservableCollection<UnsplashPhoto>();
        private readonly ObservableCollection<UnsplashPhoto> _savedPhotos = new ObservableCollection<UnsplashPhoto>();
        private readonly ObservableCollection<UnsplashPhoto> _searchResults = new ObservableCollection<UnsplashPhoto>();
        private string _activeSearchQuery = "travel";
        private int _currentSearchPage;
        private bool _hasMoreSearchResults;
        private bool _isSearchInProgress;
        private bool _isUpdatingDailyTile;
        private DispatcherTimer _dailyTileTimer;

        public MainPage()
        {
            InitializeComponent();
            PhotoList.ItemsSource = _searchResults;
            PhotoGridList.ItemsSource = _searchResults;
            ListLayoutRadio.Checked += PhotoLayoutRadio_Checked;
            GridLayoutRadio.Checked += PhotoLayoutRadio_Checked;
            LoadSavedCredentials();
            var settings = ApplicationData.Current.LocalSettings;
            DarkThemeToggle.IsOn = settings.Values[DarkThemeSettingName] is bool && (bool)settings.Values[DarkThemeSettingName];
            ApplyTheme(DarkThemeToggle.IsOn);
            DarkThemeToggle.Toggled += DarkThemeToggle_Toggled;
            DailyTileEnabledToggle.IsOn = !(settings.Values[DailyTileEnabledSettingName] is bool) || (bool)settings.Values[DailyTileEnabledSettingName];
            SetTileTopic(settings.Values[DailyTileTopicSettingName] as string ?? string.Empty);
            DailyTileEnabledToggle.Toggled += DailyTileEnabledToggle_Toggled;
            TileTopicPicker.SelectionChanged += TileTopicPicker_SelectionChanged;
            Loaded += MainPage_Loaded;
            SearchButton.Click += SearchButton_Click;
            LoadMoreButton.Click += LoadMoreButton_Click;
            LoginButton.Click += LoginButton_Click;
            SaveButton.Click += SaveButton_Click;
            TestButton.Click += TestButton_Click;
            ClearButton.Click += ClearButton_Click;
            SearchBox.KeyDown += SearchBox_KeyDown;
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            StartTurnstileEntrance();
            await LoadPhotoCollectionsAsync();
            ApplyCredentialsFromInputs();
            StartDailyTileRefreshTimer();
            var tileRefreshTask = UpdateDailyTileAsync();
            StatusText.Text = "Loading featured photos...";
            await SearchAsync("travel");
            await tileRefreshTask;
        }

        private void StartDailyTileRefreshTimer()
        {
            if (!DailyTileEnabledToggle.IsOn || _dailyTileTimer != null)
            {
                return;
            }

            _dailyTileTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
            _dailyTileTimer.Tick += async (sender, args) => await UpdateDailyTileAsync();
            _dailyTileTimer.Start();
        }

        private async Task UpdateDailyTileAsync()
        {
            if (_isUpdatingDailyTile)
            {
                return;
            }

            _isUpdatingDailyTile = true;
            try
            {
                var today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var settings = ApplicationData.Current.LocalSettings;
                if (!DailyTileEnabledToggle.IsOn)
                {
                    TileUpdateManager.CreateTileUpdaterForApplication().Clear();
                    return;
                }

                var topic = GetSelectedTileTopic();
                var topicSuffix = string.IsNullOrWhiteSpace(topic) ? "featured" : topic.ToLowerInvariant();
                var fileName = "unsplash-tile-" + today + "-" + topicSuffix + ".jpg";
                if (string.Equals(settings.Values[DailyTileDateSettingName] as string, today, StringComparison.Ordinal) &&
                    string.Equals(settings.Values[DailyTileTopicSettingName] as string ?? string.Empty, topic, StringComparison.Ordinal))
                {
                    var currentFile = await ApplicationData.Current.LocalFolder.TryGetItemAsync(fileName);
                    if (currentFile is StorageFile)
                    {
                        return;
                    }
                }

                var featuredPhoto = await _apiClient.GetDailyFeaturedPhotoAsync(topic);
                var imageUrl = string.IsNullOrWhiteSpace(featuredPhoto.FullImageUrl)
                    ? featuredPhoto.RegularImageUrl
                    : featuredPhoto.FullImageUrl;
                if (string.IsNullOrWhiteSpace(imageUrl))
                {
                    throw new InvalidOperationException("Unsplash did not provide an image for today's tile.");
                }

                var imageBytes = await _apiClient.DownloadImageAsync(imageUrl);
                var imageFile = await ApplicationData.Current.LocalFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteBytesAsync(imageFile, imageBytes);

                var imageSource = "ms-appdata:///local/" + fileName;
                var tileXml = "<tile><visual version=\"2\">" +
                    "<binding template=\"TileSmall\" branding=\"none\"><image src=\"" + imageSource + "\" /></binding>" +
                    "<binding template=\"TileMedium\" branding=\"none\"><image src=\"" + imageSource + "\" placement=\"background\" /><text placement=\"overlay\">UnsplashUWP</text></binding>" +
                    "<binding template=\"TileWide\" branding=\"none\"><image src=\"" + imageSource + "\" placement=\"background\" /><text placement=\"overlay\">Today's featured photo</text></binding>" +
                    "</visual></tile>";
                var document = new XmlDocument();
                document.LoadXml(tileXml);
                TileUpdateManager.CreateTileUpdaterForApplication().Update(new TileNotification(document));
                settings.Values[DailyTileDateSettingName] = today;
                settings.Values[DailyTileTopicSettingName] = topic;

                var cachedFiles = await ApplicationData.Current.LocalFolder.GetFilesAsync();
                foreach (var cachedFile in cachedFiles)
                {
                    if (cachedFile.Name.StartsWith("unsplash-tile-", StringComparison.Ordinal) && cachedFile.Name != fileName)
                    {
                        await cachedFile.DeleteAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "Daily tile update unavailable: " + ex.Message;
            }
            finally
            {
                _isUpdatingDailyTile = false;
            }
        }

        private void DailyTileEnabledToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values[DailyTileEnabledSettingName] = DailyTileEnabledToggle.IsOn;
            if (DailyTileEnabledToggle.IsOn)
            {
                settings.Values.Remove(DailyTileDateSettingName);
                StartDailyTileRefreshTimer();
                _ = UpdateDailyTileAsync();
            }
            else
            {
                _dailyTileTimer?.Stop();
                _dailyTileTimer = null;
                TileUpdateManager.CreateTileUpdaterForApplication().Clear();
            }
        }

        private void TileTopicPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values[DailyTileTopicSettingName] = GetSelectedTileTopic();
            settings.Values.Remove(DailyTileDateSettingName);
            if (DailyTileEnabledToggle.IsOn)
            {
                _ = UpdateDailyTileAsync();
            }
        }

        private string GetSelectedTileTopic()
        {
            return (TileTopicPicker.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;
        }

        private void SetTileTopic(string topic)
        {
            foreach (var item in TileTopicPicker.Items)
            {
                var option = item as ComboBoxItem;
                if (option != null && string.Equals(option.Tag as string ?? string.Empty, topic, StringComparison.Ordinal))
                {
                    TileTopicPicker.SelectedItem = option;
                    return;
                }
            }

            TileTopicPicker.SelectedIndex = 0;
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

        private async void PhotoImage_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var photo = (sender as FrameworkElement)?.DataContext as UnsplashPhoto;
            if (photo == null)
            {
                return;
            }

            await RecordRecentPhotoAsync(photo);

            var details = new StackPanel();
            if (Uri.TryCreate(photo.RegularImageUrl, UriKind.Absolute, out var imageUri))
            {
                details.Children.Add(new Image
                {
                    Source = new BitmapImage(imageUri),
                    Height = 220,
                    Stretch = Stretch.UniformToFill,
                    Margin = new Thickness(0, 0, 0, 12)
                });
            }

            AddPhotoDetail(details, "Description", string.IsNullOrWhiteSpace(photo.Description) ? "No long description provided." : photo.Description);
            AddPhotoDetail(details, "Alt description", string.IsNullOrWhiteSpace(photo.AltDescription) ? "No alt description provided." : photo.AltDescription);
            AddPhotoDetail(details, "Photographer", photo.User);
            AddPhotoDetail(details, "Username", string.IsNullOrWhiteSpace(photo.UserName) ? "Not provided" : "@" + photo.UserName);
            AddPhotoDetail(details, "Dimensions", photo.Width + " x " + photo.Height);
            AddPhotoDetail(details, "Likes", photo.Likes.ToString());
            AddPhotoDetail(details, "Created", photo.CreatedAt);
            AddPhotoDetail(details, "Tags", string.IsNullOrWhiteSpace(photo.Tags) ? "No tags" : photo.Tags);
            AddPhotoDetail(details, "Color", photo.Color);
            AddPhotoDetail(details, "Photographer profile", photo.UserProfileUrl);
            AddPhotoDetail(details, "Unsplash photo page", photo.PhotoPageUrl);

            var dialog = new ContentDialog
            {
                Title = photo.Title,
                Content = new ScrollViewer { Content = details, MaxHeight = 520 },
                CloseButtonText = "Close"
            };

            await dialog.ShowAsync();
        }

        private async void SavePhoto_Click(object sender, RoutedEventArgs e)
        {
            var photo = (sender as Button)?.DataContext as UnsplashPhoto;
            if (photo == null)
            {
                return;
            }

            if (FindPhoto(_savedPhotos, photo.Id) != null)
            {
                StatusText.Text = "Photo is already saved";
                return;
            }

            _savedPhotos.Insert(0, photo);
            await SavePhotoCollectionAsync(SavedPhotosFileName, _savedPhotos);
            StatusText.Text = "Photo saved to your collection";
        }

        private async void RemoveSavedPhoto_Click(object sender, RoutedEventArgs e)
        {
            var photo = (sender as Button)?.DataContext as UnsplashPhoto;
            var savedPhoto = photo == null ? null : FindPhoto(_savedPhotos, photo.Id);
            if (savedPhoto == null)
            {
                return;
            }

            _savedPhotos.Remove(savedPhoto);
            await SavePhotoCollectionAsync(SavedPhotosFileName, _savedPhotos);
            StatusText.Text = "Photo removed from saved";
        }

        private async Task RecordRecentPhotoAsync(UnsplashPhoto photo)
        {
            var existingPhoto = FindPhoto(_recentPhotos, photo.Id);
            if (existingPhoto != null)
            {
                _recentPhotos.Remove(existingPhoto);
            }

            _recentPhotos.Insert(0, photo);
            while (_recentPhotos.Count > 30)
            {
                _recentPhotos.RemoveAt(_recentPhotos.Count - 1);
            }

            await SavePhotoCollectionAsync(RecentPhotosFileName, _recentPhotos);
        }

        private async Task LoadPhotoCollectionsAsync()
        {
            await LoadPhotoCollectionAsync(RecentPhotosFileName, _recentPhotos);
            await LoadPhotoCollectionAsync(SavedPhotosFileName, _savedPhotos);
            RecentPhotoList.ItemsSource = _recentPhotos;
            SavedPhotoList.ItemsSource = _savedPhotos;
        }

        private static async Task LoadPhotoCollectionAsync(string fileName, ObservableCollection<UnsplashPhoto> collection)
        {
            var item = await ApplicationData.Current.LocalFolder.TryGetItemAsync(fileName);
            var file = item as StorageFile;
            if (file == null)
            {
                return;
            }

            var json = await FileIO.ReadTextAsync(file);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var array = JsonArray.Parse(json);
            foreach (var value in array)
            {
                collection.Add(PhotoFromJson(value.GetObject()));
            }
        }

        private static async Task SavePhotoCollectionAsync(string fileName, IEnumerable<UnsplashPhoto> collection)
        {
            var array = new JsonArray();
            foreach (var photo in collection)
            {
                array.Add(PhotoToJson(photo));
            }

            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(file, array.Stringify());
        }

        private static JsonObject PhotoToJson(UnsplashPhoto photo)
        {
            var json = new JsonObject();
            AddJsonString(json, "Id", photo.Id);
            AddJsonString(json, "Title", photo.Title);
            AddJsonString(json, "Description", photo.Description);
            AddJsonString(json, "AltDescription", photo.AltDescription);
            AddJsonString(json, "User", photo.User);
            AddJsonString(json, "UserName", photo.UserName);
            AddJsonString(json, "UserProfileUrl", photo.UserProfileUrl);
            AddJsonString(json, "ImageUrl", photo.ImageUrl);
            AddJsonString(json, "SmallImageUrl", photo.SmallImageUrl);
            AddJsonString(json, "RegularImageUrl", photo.RegularImageUrl);
            AddJsonString(json, "FullImageUrl", photo.FullImageUrl);
            AddJsonString(json, "RawImageUrl", photo.RawImageUrl);
            AddJsonString(json, "DownloadLocation", photo.DownloadLocation);
            AddJsonString(json, "PhotoPageUrl", photo.PhotoPageUrl);
            AddJsonString(json, "CreatedAt", photo.CreatedAt);
            AddJsonString(json, "Color", photo.Color);
            AddJsonString(json, "Tags", photo.Tags);
            json.Add("Width", JsonValue.CreateNumberValue(photo.Width));
            json.Add("Height", JsonValue.CreateNumberValue(photo.Height));
            json.Add("Likes", JsonValue.CreateNumberValue(photo.Likes));
            return json;
        }

        private static UnsplashPhoto PhotoFromJson(JsonObject json)
        {
            return new UnsplashPhoto
            {
                Id = json.GetNamedString("Id", string.Empty),
                Title = json.GetNamedString("Title", "Unsplash photo"),
                Description = json.GetNamedString("Description", string.Empty),
                AltDescription = json.GetNamedString("AltDescription", string.Empty),
                User = json.GetNamedString("User", "Unsplash"),
                UserName = json.GetNamedString("UserName", string.Empty),
                UserProfileUrl = json.GetNamedString("UserProfileUrl", string.Empty),
                ImageUrl = json.GetNamedString("ImageUrl", string.Empty),
                SmallImageUrl = json.GetNamedString("SmallImageUrl", string.Empty),
                RegularImageUrl = json.GetNamedString("RegularImageUrl", string.Empty),
                FullImageUrl = json.GetNamedString("FullImageUrl", string.Empty),
                RawImageUrl = json.GetNamedString("RawImageUrl", string.Empty),
                DownloadLocation = json.GetNamedString("DownloadLocation", string.Empty),
                PhotoPageUrl = json.GetNamedString("PhotoPageUrl", string.Empty),
                CreatedAt = json.GetNamedString("CreatedAt", string.Empty),
                Color = json.GetNamedString("Color", string.Empty),
                Tags = json.GetNamedString("Tags", string.Empty),
                Width = (int)json.GetNamedNumber("Width", 0),
                Height = (int)json.GetNamedNumber("Height", 0),
                Likes = (int)json.GetNamedNumber("Likes", 0)
            };
        }

        private static void AddJsonString(JsonObject json, string name, string value)
        {
            json.Add(name, JsonValue.CreateStringValue(value ?? string.Empty));
        }

        private static UnsplashPhoto FindPhoto(IEnumerable<UnsplashPhoto> photos, string photoId)
        {
            foreach (var photo in photos)
            {
                if (string.Equals(photo.Id, photoId, StringComparison.Ordinal))
                {
                    return photo;
                }
            }

            return null;
        }

        private static void AddPhotoDetail(Panel panel, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 5, 0, 1)
            });
            panel.Children.Add(new TextBlock
            {
                Text = value,
                TextWrapping = TextWrapping.WrapWholeWords,
                IsTextSelectionEnabled = true
            });
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyCredentialsFromInputs();
            await SearchAsync(SearchBox.Text);
        }

        private void PhotoLayoutRadio_Checked(object sender, RoutedEventArgs e)
        {
            var selectedLayout = (sender as RadioButton)?.Tag as string;
            var showGrid = string.Equals(selectedLayout, "grid", StringComparison.Ordinal);
            PhotoGridList.Visibility = showGrid ? Visibility.Visible : Visibility.Collapsed;
            PhotoList.Visibility = showGrid ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void LoadMoreButton_Click(object sender, RoutedEventArgs e)
        {
            await SearchAsync(_activeSearchQuery, true);
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

        private async Task SearchAsync(string query, bool loadMore = false)
        {
            if (_isSearchInProgress || (loadMore && !_hasMoreSearchResults))
            {
                return;
            }

            if (!loadMore)
            {
                _activeSearchQuery = string.IsNullOrWhiteSpace(query) ? "travel" : query.Trim();
                _currentSearchPage = 0;
                _hasMoreSearchResults = true;
                _searchResults.Clear();
                LoadMoreButton.Visibility = Visibility.Collapsed;
            }

            _isSearchInProgress = true;
            SearchButton.IsEnabled = false;
            LoadMoreButton.IsEnabled = false;

            try
            {
                ApplyCredentialsFromInputs();
                var requestedPage = _currentSearchPage + 1;
                StatusText.Text = loadMore ? "Loading more photos..." : "Loading photos...";
                var photos = await _apiClient.SearchPhotosAsync(_activeSearchQuery, requestedPage);
                foreach (var photo in photos)
                {
                    if (!ContainsSearchPhoto(photo.Id))
                    {
                        _searchResults.Add(photo);
                    }
                }

                _currentSearchPage = requestedPage;
                _hasMoreSearchResults = photos.Count >= SearchPageSize;
                LoadMoreButton.Visibility = _hasMoreSearchResults ? Visibility.Visible : Visibility.Collapsed;

                if (photos.Count == 0 && requestedPage > 1)
                {
                    StatusText.Text = "No more photos";
                }
                else if (_searchResults.Count == 0)
                {
                    StatusText.Text = "No matching photos found";
                }
                else
                {
                    StatusText.Text = _hasMoreSearchResults
                        ? "Showing " + _searchResults.Count + " photos"
                        : "Loaded all " + _searchResults.Count + " photos";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "API error: " + ex.Message;
                ShowMessageDialog("Unsplash API error", ex.Message);
            }
            finally
            {
                _isSearchInProgress = false;
                SearchButton.IsEnabled = true;
                LoadMoreButton.IsEnabled = _hasMoreSearchResults;
            }
        }

        private bool ContainsSearchPhoto(string photoId)
        {
            foreach (var photo in _searchResults)
            {
                if (string.Equals(photo.Id, photoId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
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