# JIT.Calendar.Alert

**JIT Alerts** is a .NET MAUI Android app that monitors ICS/iCal calendar feeds and fires a local notification the moment a meeting begins (within a 1-minute window). A persistent foreground service polls each active calendar source every 60 seconds, so alerts arrive even when the app is in the background.

---

## Prerequisites

| Requirement | Minimum version |
|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 9.0 |
| [.NET MAUI workload](https://learn.microsoft.com/dotnet/maui/get-started/installation) | 9.0 |
| Android SDK | API 26 (Android 8.0) |
| Visual Studio (Windows) or VS Code with MAUI extension | VS 2022 17.8+ |

Install the MAUI workload if you have not already:

```bash
dotnet workload install maui-android
```

---

## Testing Locally (Android Emulator)

### 1. Create an Android Virtual Device (AVD)

1. Open **Android Studio** (or the standalone **AVD Manager** from the Android SDK).
2. Create a device with **API 26 or higher** (Pixel 6 / API 34 recommended).
3. Start the emulator and confirm it appears in the device list:

```bash
adb devices
```

### 2. Run from Visual Studio

1. Open `JIT.Calendar.Alert.sln`.
2. Set **JustInTimeAlerts** as the startup project.
3. Select your running emulator from the device picker in the toolbar.
4. Press **F5** (Debug) or **Ctrl+F5** (run without debugging).

Visual Studio will build, deploy, and launch the app on the emulator automatically.

### 3. Run from the command line

```bash
cd JustInTimeAlerts
dotnet build -f net9.0-android -c Debug
dotnet run -f net9.0-android --no-build
```

Or deploy directly to a running emulator:

```bash
dotnet build -f net9.0-android -c Debug -t:Install
```

### 4. Grant permissions on the emulator

The app requires several permissions that Android will prompt for at runtime:

- **Post Notifications** – required to show meeting alerts (Android 13+).
- **Read External Storage** – needed if you import a local `.ics` file.

If notifications do not appear, open **Settings → Apps → JIT Alerts → Notifications** on the emulator and ensure they are enabled.

### 5. Verify the foreground service

1. Add a calendar ICS URL (e.g., a public Google Calendar feed) from the main screen.
2. Tap **Start Service**.
3. Pull down the notification shade — a persistent "JIT Alerts running" notification confirms the foreground service is active.
4. The service polls every 60 seconds. To test an alert quickly, add an ICS event whose start time is within the next 1–2 minutes.

---

## Deploying to a Physical Android Phone

### Option A — Direct deployment from Visual Studio (recommended for development)

1. On your phone go to **Settings → About phone** and tap **Build number** seven times to enable Developer Options.
2. Open **Settings → Developer options** and enable **USB debugging**.
3. Connect your phone via USB. Accept the "Allow USB debugging" prompt on the device.
4. In Visual Studio, select your phone from the device picker and press **F5**.

Visual Studio installs and launches the debug build directly.

### Option B — Build a release APK and sideload it

#### 1. Build the APK

```bash
cd JustInTimeAlerts
dotnet publish -f net9.0-android -c Release
```

The unsigned APK is written to:

```
JustInTimeAlerts\bin\Release\net9.0-android\publish\com.justintimealerts.app-Signed.apk
```

> **Note:** `dotnet publish` automatically signs with the debug keystore if no release keystore is configured. For distribution outside the Play Store this is sufficient for personal use.

#### 2. Transfer the APK to your phone

Copy the APK over USB or via a cloud service, then on your phone:

1. Open **Settings → Apps → Special app access → Install unknown apps**.
2. Allow the app you are using to open the file (e.g. Files, Chrome).
3. Open the `.apk` file and tap **Install**.

#### 3. Install via ADB (alternative)

```bash
adb install -r "JustInTimeAlerts\bin\Release\net9.0-android\publish\com.justintimealerts.app-Signed.apk"
```

The `-r` flag reinstalls and preserves any existing app data.

#### 4. Grant permissions on device

On Android 13 and above, open **Settings → Apps → JIT Alerts → Permissions** and confirm:

- **Notifications** → Allowed
- **Files and media** → Allowed (for local ICS import)

The foreground service and internet permissions are granted automatically at install time.

---

## Android Permissions Summary

| Permission | Purpose |
|---|---|
| `INTERNET` | Fetch ICS feeds from remote URLs |
| `ACCESS_NETWORK_STATE` | Check connectivity before fetching |
| `POST_NOTIFICATIONS` | Show meeting alert notifications |
| `FOREGROUND_SERVICE` | Keep the polling service alive in the background |
| `FOREGROUND_SERVICE_DATA_SYNC` | Required service type for API 34+ |
| `RECEIVE_BOOT_COMPLETED` | Restart the service after the device reboots |
| `READ_EXTERNAL_STORAGE` | Import local `.ics` files |

---

## Project Structure

```
JustInTimeAlerts/
├── Models/              # CalendarSource, MeetingEvent
├── Services/            # ICS parser, calendar repository, alert engine
├── ViewModels/          # MainViewModel (CommunityToolkit.Mvvm)
├── Platforms/Android/   # Foreground service, notification service, boot receiver
└── Converters/          # XAML value converters
```
