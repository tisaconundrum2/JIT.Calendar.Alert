# JIT.Calendar.Alert

**JIT Alerts** is a native Android (Kotlin) app that monitors ICS/iCal calendar feeds and fires a local notification the moment a meeting begins (within a 1-minute window). A persistent foreground service polls each active calendar source every 60 seconds, so alerts arrive even when the app is in the background.

---

## Prerequisites

| Requirement | Minimum version |
|---|---|
| [Android Studio](https://developer.android.com/studio) | Hedgehog 2023.1.1+ |
| Android SDK | API 26 (Android 8.0) |
| Kotlin | 1.9.22 |
| Gradle | 8.4 |

---

## Building the Project

### 1. Clone the repository

```bash
git clone https://github.com/tisaconundrum2/JIT.Calendar.Alert.git
cd JIT.Calendar.Alert
```

### 2. Build from Android Studio

1. Open the project folder in **Android Studio**.
2. Let Gradle sync complete.
3. Select your target device or emulator from the device picker.
4. Press **Run** (Shift+F10) or click the green play button.

### 3. Build from command line

```bash
# Debug build
./gradlew assembleDebug

# Release build
./gradlew assembleRelease

# Install debug APK to connected device
./gradlew installDebug
```

The APK will be generated at:
- Debug: `app/build/outputs/apk/debug/app-debug.apk`
- Release: `app/build/outputs/apk/release/app-release.apk`

---

## Testing on Android Emulator

### 1. Create an Android Virtual Device (AVD)

1. Open **Android Studio** → **Device Manager**.
2. Create a device with **API 26 or higher** (Pixel 6 / API 34 recommended).
3. Start the emulator and confirm it appears in the device list:

```bash
adb devices
```

### 2. Run from Android Studio

1. Select your running emulator from the device picker in the toolbar.
2. Press **Run** (Shift+F10) to build, deploy, and launch the app.

### 3. Grant permissions on the emulator

The app requires several permissions that Android will prompt for at runtime:

- **Post Notifications** – required to show meeting alerts (Android 13+).

If notifications do not appear, open **Settings → Apps → JIT Alerts → Notifications** on the emulator and ensure they are enabled.

### 4. Verify the foreground service

1. Add a calendar ICS URL (e.g., a public Google Calendar feed) from the main screen.
2. A persistent "JIT Alerts Running" notification confirms the foreground service is active.
3. The service polls every 60 seconds. To test an alert quickly, add an ICS event whose start time is within the next 1–2 minutes.

---

## Deploying to a Physical Android Phone

### Option A — Direct deployment from Android Studio (recommended for development)

1. On your phone go to **Settings → About phone** and tap **Build number** seven times to enable Developer Options.
2. Open **Settings → Developer options** and enable **USB debugging**.
3. Connect your phone via USB. Accept the "Allow USB debugging" prompt on the device.
4. In Android Studio, select your phone from the device picker and press **Run**.

### Option B — Build a release APK and sideload it

#### 1. Build the APK

```bash
./gradlew assembleRelease
```

#### 2. Transfer and install

Copy the APK to your phone and install via the file manager, or use ADB:

```bash
adb install -r app/build/outputs/apk/release/app-release.apk
```

#### 3. Grant permissions on device

On Android 13 and above, open **Settings → Apps → JIT Alerts → Permissions** and confirm:

- **Notifications** → Allowed

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
| `WAKE_LOCK` | Required by WorkManager for background task execution |

---

## Project Structure

```
app/src/main/
├── java/com/justintimealerts/
│   ├── models/               # CalendarSource, MeetingEvent, UpcomingHourGroup
│   ├── services/             # ICS parser, calendar repository, alert engine
│   │   ├── AndroidNotificationService.kt   # Notification posting
│   │   ├── CalendarSourceRepository.kt     # Persisted calendar sources
│   │   ├── CalendarSyncWorker.kt           # WorkManager periodic task
│   │   ├── DebugLogService.kt              # In-memory logging
│   │   ├── IcsParserService.kt             # ICS fetching & parsing
│   │   ├── MeetingAlertForegroundService.kt # Foreground service
│   │   ├── MeetingAlertService.kt          # Core alert logic
│   │   ├── ProcessedMeetingCache.kt        # Dedup cache
│   │   └── BootReceiver.kt                 # Boot completed receiver
│   ├── viewmodels/           # MainViewModel
│   ├── ui/                   # MainActivity
│   └── JitApplication.kt     # Application class / DI container
└── res/
    ├── layout/               # XML layouts
    ├── values/               # Strings, colors, themes
    └── drawable/             # Vector drawables
```

---

## Key Features

1. **Foreground Service**: The app uses a foreground service with `dataSync` type to continuously monitor calendars even when the app is in the background.

2. **WorkManager Fallback**: A periodic WorkManager task runs every 15 minutes as a fallback if the foreground service is killed.

3. **Smart Caching**: The ICS parser implements multiple caching strategies:
   - Minimum 15-minute interval between HTTP fetches
   - ETag/If-Modified-Since conditional requests
   - SHA-256 content hash deduplication
   - Exponential back-off on failures

4. **Boot Persistence**: The service automatically restarts after device reboots via a BroadcastReceiver.

5. **Coroutine-based**: All background work uses Kotlin coroutines with proper scope management.
