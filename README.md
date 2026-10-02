# UnsplashUWP

UnsplashUWP is a native Universal Windows Platform client for Windows 10 Mobile, developed for Lumia by Royalturd. It searches Unsplash directly through its API and OAuth flow; it does not embed a browser engine or depend on Gecko.

## Features

- Search Unsplash and load additional results with **Load more**.
- Tap a photo to view its descriptions, photographer, dimensions, tags, likes, and source links.
- Save photos and review recently viewed photos in separate swipeable sections. Both collections persist on the device.
- Download a photo as a small, regular, full-resolution, or original image, then choose where to save it.
- Sign in with Unsplash OAuth and enter, test, save, or clear API credentials in the app.
- Toggle the AMOLED-black dark theme in Settings; the selection is remembered.
- View the **UnsplashUWP Beta** label and Lumia developer credit in Settings.

## Requirements

- Visual Studio 2022 with the Universal Windows Platform development workload.
- Windows 10 SDK 10.0.19041.0 for building this project.
- An Unsplash developer application with a registered OAuth redirect URI.
- A Windows 10 Mobile ARM device running build 15063 or later for deployment.

The package minimum OS version is `10.0.15063.0`. The project builds with SDK `10.0.19041.0`; the build SDK version does not raise the package minimum. The 15063 SDK reference assemblies are not included in this repository.

## Configure And Use

1. Open `src/UnsplashMobile/UnsplashMobile.csproj` in Visual Studio.
2. Deploy to an ARM device or emulator.
3. In **Settings**, enter the Unsplash Access Key, Secret Key, and registered Redirect URI, then select **Save keys**.
4. Use **Test API** to verify access or **Sign in** to authorize through OAuth.
5. In **Discover**, enter a query and select **Search**. Select **Load more photos** to fetch the next page.
6. Swipe between **Discover**, **Recent**, **Saved**, and **Settings**. Tap a photo image for its details; use **Save** or **Download** on a photo card as needed.

Credentials and photo collections are stored in the app's local data on the device. Do not commit API credentials or share builds containing real credentials.

## Build An ARM Package

From PowerShell at the repository root, build a sideload package into `D:\UnsplashMobileBuild`:

```powershell
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
& $msbuild '.\src\UnsplashMobile\UnsplashMobile.csproj' `
  /restore `
  /p:Configuration=Debug `
  /p:Platform=ARM `
  /p:TargetPlatformVersion=10.0.19041.0 `
  /p:AppxPackageDir='D:\UnsplashMobileBuild\' `
  /p:AppxPackage=true `
  /p:AppxBundle=Never `
  /p:UapAppxPackageBuildMode=SideloadOnly
```

The installable package is created under `D:\UnsplashMobileBuild\UnsplashMobile_1.0.0.0_ARM_Debug_Test`. MSBuild also creates an executable as an intermediate packaging input; the device deliverable is the ARM `.appx`. The output folder contains ARM dependencies and Visual Studio sideload scripts.

## Sign And Install

Sideloading requires a package certificate trusted by the device. For local testing, create a development certificate whose subject matches the manifest publisher (`CN=UnsplashMobile`), export its public `.cer`, and sign the `.appx` with the Windows SDK SignTool. Keep the private key out of source control. Reuse the same certificate for subsequent builds.

```powershell
$folder = 'D:\UnsplashMobileBuild\UnsplashMobile_1.0.0.0_ARM_Debug_Test'
$appx = Join-Path $folder 'UnsplashMobile_1.0.0.0_ARM_Debug.appx'
$cert = New-SelfSignedCertificate -Type Custom -Subject 'CN=UnsplashMobile' `
  -FriendlyName 'UnsplashMobile local sideload' `
  -KeyUsage DigitalSignature `
  -CertStoreLocation 'Cert:\CurrentUser\My' `
  -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
Export-Certificate -Cert $cert -FilePath (Join-Path $folder 'UnsplashMobile_Dev.cer')
$signTool = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\signtool.exe'
& $signTool sign /fd SHA256 /sha1 $cert.Thumbprint /s My $appx
& $signTool verify /pa /all $appx
```

Trust/install `UnsplashMobile_Dev.cer` on the device before deploying the `.appx` with Visual Studio device deployment or Windows Device Portal. The repository does not contain a production signing certificate; locally signed packages are for development and sideloading only.
