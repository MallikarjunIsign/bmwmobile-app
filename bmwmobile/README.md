# bmwmobile
bidmywork mobile code – OKOH Parts app (React Native CLI 0.87, TypeScript).

## Requirements
- Node 22.11+
- JDK 17, Android SDK (compileSdk 37, build-tools 37.0.0, NDK 27.1.12297006)
- iOS only: macOS with Xcode, Ruby + CocoaPods

## Run on Android
```bash
npm install
npm start            # Metro bundler (keep it running)
npm run android      # builds and installs com.bidmywork.app on the emulator/device
```
The app talks to the backend gateway at `http://10.0.2.2:6001` (see `src/services/api.ts`).

## Run on iOS (Mac only)
```bash
npm install
cd ios && bundle install && bundle exec pod install && cd ..
npm run ios
```

## Checks
```bash
npx tsc --noEmit     # type-check
npm test             # Jest
npm run lint
```

## Notes
- Package / bundle id: `com.bidmywork.app`. Display name: "OKOH Parts".
- Debug builds are signed with the standard React Native `android/app/debug.keystore`
  (SHA-1 `5E:8F:16:06:2E:A3:CD:2C:4A:0D:54:78:76:BA:A6:F3:8C:AB:F6:25`, registered for Google sign-in).
- Release signing reads `BIDMYWORK_UPLOAD_*` values from `~/.gradle/gradle.properties` (never commit them).
