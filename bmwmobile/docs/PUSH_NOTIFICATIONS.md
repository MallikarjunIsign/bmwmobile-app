# Push notifications for chat (Firebase)

New chat messages always arrive live while the app is open (SignalR: toast + unread badge). To also notify phones
when the app is in the background or closed ("New message from ravi123 – About Hilux front bumper: Is this still
available?"), connect a Firebase project. Until then everything works, just without background notifications.

## 1. Firebase project

1. https://console.firebase.google.com → **Add project** (e.g. "ShopCarParts").
2. **Add app → Android**, package name `com.bidmywork.app` → download **google-services.json** →
   put it in `bmwmobile/android/app/google-services.json`.
3. **Add app → iOS**, bundle id from Xcode → download **GoogleService-Info.plist** → add it to the
   `BidMyWork` target in Xcode (`bmwmobile/ios/BidMyWork/`).
4. iOS only: Project settings → **Cloud Messaging** → upload an **APNs auth key** (Apple Developer → Keys), and in
   Xcode enable the **Push Notifications** and **Background Modes → Remote notifications** capabilities.

Both files are only read when present: the Gradle plugin is applied and `FirebaseApp.configure()` is called only if
the file exists. Don't commit them if the repository is public.

## 2. Server (ChatService)

1. Firebase console → Project settings → **Service accounts** → **Generate new private key** (JSON). Keep it outside
   the repository.
2. Point ChatService at it and switch push on, e.g. with user-secrets:

   ```
   cd bmwapi/src/ChatService
   dotnet user-secrets set "Push:Provider" "Firebase"
   dotnet user-secrets set "Push:Firebase:CredentialsFile" "C:\\secrets\\shopcarparts-firebase.json"
   ```

   Docker / Kubernetes: `Push__Provider=Firebase` and mount the file, setting `Push__Firebase__CredentialsFile` (or
   `GOOGLE_APPLICATION_CREDENTIALS`) to its path.

## 3. App

Rebuild the native app (`npx react-native run-android`; iOS: `cd ios && pod install`, then build). After sign-in the
app asks for notification permission (Android 13+ / iOS) and registers the phone with ChatService
(`POST /chat/devices`); signing out removes it. Tapping a notification opens the conversation.

Android shows chat notifications in the **Messages** channel (created by the app).
