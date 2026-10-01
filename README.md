# UnsplashUWP

A native UWP Unsplash client for Windows 10 Mobile, created by Royalturd. The app searches Unsplash photos and supports OAuth login without embedding a browser engine or depending on Gecko.

## Features

- Search and view Unsplash photos.
- Download small, regular, full-resolution, or original photo images to a location you choose.
- Open a photo's full description, photographer, dimensions, tags, and source links by tapping its image.
- Browse Discover, Recent, and Saved as separate swipeable sections.
- Keep recently viewed and saved photos on the device between launches.
- Enter an Unsplash Access Key, Secret Key, and redirect URI in the app.
- Save, test, or clear credentials on the device.
- Toggle dark theme in Settings; the choice is remembered on the device.
- Sign in through the system web authentication flow.

## Requirements

- Visual Studio 2022 with the Universal Windows Platform development workload.
- Windows 10 SDK 10.0.19041.0 (the project build SDK).
- An Unsplash developer application with its OAuth redirect URI configured.
- A Windows 10 Mobile device running build 15063 or newer for deployment.

The package declares `10.0.15063.0` as its minimum OS version. The project compiles against the 19041 SDK; that does not raise the package minimum. The 15063 SDK reference assemblies are not included in this repository.

## Configure And Use

1. Open `UnsplashMobile-WebView-Prototype/UnsplashMobile.csproj` in Visual Studio.
2. Launch the app on an ARM device or emulator.
3. Enter the Unsplash Access Key, Secret Key, and registered redirect URI, then select **Save**.
4. Use the **Dark theme** switch to change and save the app appearance.
5. Select **Test** to check API access, **Sign in** for OAuth, or enter a search term and select **Search**.
6. Swipe between **Discover**, **Recent**, **Saved**, and **Settings**.
7. Tap a photo image for its full details. Use **Save** to add it to Saved, or **Download** to choose an image size and save location.

Credentials are saved in the app's local settings on the device. Do not share a package containing real credentials or commit API keys.

## Build An ARM Install Package

From PowerShell at the repository root, build a sideload package to `D:\UnsplashMobileBuild`:

```powershell
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
& $msbuild '.\UnsplashMobile-WebView-Prototype\UnsplashMobile.csproj' `
	/restore `
	/p:Configuration=Debug `
	/p:Platform=ARM `
	/p:TargetPlatformVersion=10.0.19041.0 `
	/p:AppxPackageDir='D:\UnsplashMobileBuild\' `
	/p:AppxPackage=true `
	/p:AppxBundle=Never `
	/p:UapAppxPackageBuildMode=SideloadOnly
```

The `.appx` is created under `D:\UnsplashMobileBuild\UnsplashMobile_1.0.0.0_ARM_Debug_Test`. MSBuild also creates intermediate executable files while packaging; the installable deliverable is the ARM `.appx`.

## Sign And Install

Sideload packages must be signed by a certificate trusted by the device. For local testing, create a development certificate whose subject matches the manifest publisher (`CN=UnsplashMobile`), export its public `.cer`, and sign the generated `.appx` with the Windows SDK `SignTool`. Keep the private signing key out of source control. Install/trust the public certificate on the phone before deploying the package. The package output includes ARM dependencies and Visual Studio sideload scripts.

For a local test build, run the following after building. Reuse the same certificate for later builds so the phone only needs to trust it once:

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

Copy `UnsplashMobile_Dev.cer` to the phone and trust/install it before deploying the `.appx` with Visual Studio device deployment or Windows Device Portal. Keep the certificate's private key in the local certificate store; do not export or commit it.

The repository does not contain a production signing certificate. A locally signed package is for development and sideloading only.
