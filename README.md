# Unsplash on Windows 10 Mobile

The active project is the direct Unsplash API client in [`UnsplashMobile-WebView-Prototype/`](UnsplashMobile-WebView-Prototype/). The Gecko browser dependency and its build pipeline have been removed from the active workspace.

This app targets Windows 10 Mobile build 15063 and above, and it authenticates through the Unsplash OAuth flow instead of loading the website in a browser control.

What the app does:

- Opens the Unsplash OAuth login flow for authorized users.
- Uses the Unsplash API to request photos and search results.
- Keeps the app compatible with the Windows 10 Mobile UWP stack without requiring Gecko or a browser engine.

Setup:

1. Open the UWP project in Visual Studio 2022.
2. Set your Unsplash API key and secret in `UnsplashMobile-WebView-Prototype/Api/UnsplashApiClient.cs`.
3. Build for ARM, x86, or x64 and deploy to a device running Windows 10 Mobile 15063 or newer.

The old Gecko project and its dependency files have been removed from the active app path.
