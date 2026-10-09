# Run and test OKOH Parts locally (Windows, Git Bash)

This guide starts the backend (Docker + .NET services) and the React Native app on the Android emulator.
All commands are for **Git Bash**. Folder paths:

| What | Folder |
|---|---|
| Backend | `/c/Bmwmobile/bmwapi` (Windows: `C:\Bmwmobile\bmwapi`) |
| Mobile app | `/c/Bmwmobile/bmwmobile` (Windows: `C:\Bmwmobile\bmwmobile`) |

---

## 0. One-time setup

You need: Docker Desktop, .NET 10 SDK, Node 22.11+, JDK 17, Android Studio (SDK + an emulator, e.g. `Pixel_8`), Git Bash.
Keep at least **10 GB free** on C: (the Android build alone needs about 5 GB).

```bash
cd /c/Bmwmobile/bmwmobile
npm install
```

Optional – real OTP emails through Gmail (otherwise the code is shown in the app and in the Identity log, see section 5).
Create a Gmail **App Password** (Google account → Security → 2-Step Verification → App passwords), then:

```bash
cd /c/Bmwmobile/bmwapi/src/IdentityService
dotnet user-secrets set "Email:Username" "you@gmail.com"
dotnet user-secrets set "Email:Password" "your-16-letter-app-password"
```

These are stored only on your PC (user-secrets), never in the repo.

---

## 1. Start order (summary)

1. Docker Desktop (wait for "Engine running")
2. Backend – one command: `./start-backend.sh` (or by hand, one terminal per service – section 2B)
3. Android emulator
4. Metro (`npm start`)
5. `adb reverse tcp:8081 tcp:8081`
6. Build + install the app (`npm run android -- --active-arch-only --no-packager`)

---

## 2. Backend

### 2A. Quick way – one script

```bash
cd /c/Bmwmobile/bmwapi
./start-backend.sh
```

It starts the Docker containers, builds once, then starts every service in the background and waits for each one.
You should see:

```
== 1/3 Docker: postgres, rabbitmq, meilisearch
== 2/3 Building the solution (first time takes a few minutes)
== 3/3 Starting services
  identity  http://localhost:5001 ... ready
  auction   http://localhost:7001 ... ready
  search    http://localhost:7002 ... ready
  bid       http://localhost:7003 ... ready
  notify    http://localhost:7004 ... ready
  gateway   http://localhost:6001 ... ready
Backend is running.
```

Logs are in `/c/Bmwmobile/bmwapi/.run/logs/<service>.log`, e.g. `tail -f .run/logs/identity.log`.

### 2B. By hand – one terminal per service

Use this when you want to watch each service's output. Open one Git Bash window per row, **in this order**,
and wait for the "ready" line before starting the next one.

First, Docker (any terminal):

```bash
cd /c/Bmwmobile/bmwapi
docker compose up -d postgres rabbitmq meilisearch
docker compose ps        # wait until all three say (healthy)
```

| # | Service | Command | Ready when you see | Port |
|---|---|---|---|---|
| 1 | Identity (login) | `cd /c/Bmwmobile/bmwapi/src/IdentityService && dotnet run` | `Now listening on: http://localhost:5001` | 5001 |
| 2 | Auction | `cd /c/Bmwmobile/bmwapi/src/AuctionService && dotnet run` | `Now listening on: http://localhost:7001` | 7001 (+ gRPC 7777) |
| 3 | Search (needs Auction) | `cd /c/Bmwmobile/bmwapi/src/SearchService && dotnet run` | `Search index ...` then `Now listening on: http://localhost:7002` | 7002 |
| 4 | Bid | `cd /c/Bmwmobile/bmwapi/src/BidService && dotnet run` | `Now listening on: http://localhost:7003` | 7003 |
| 5 | Notification | `cd /c/Bmwmobile/bmwapi/src/NotificationService && dotnet run` | `Now listening on: http://localhost:7004` | 7004 |
| 6 | Gateway (the app talks to this) | `cd /c/Bmwmobile/bmwapi/src/GatewayService && dotnet run` | `Now listening on: http://localhost:6001` | 6001 |

Tip: if two services build at the same time they can block each other ("file is being used by another process").
Run `cd /c/Bmwmobile/bmwapi && dotnet build Carsties.sln` once first, then use `dotnet run --no-build` in each terminal.

The app only needs Identity (5001) and Gateway (6001) to be reachable; the gateway forwards to Auction, Search, Bid and Notification.

---

## 3. Mobile app (Android emulator)

### Terminal A – emulator

```bash
"$LOCALAPPDATA/Android/Sdk/emulator/emulator.exe" -list-avds
"$LOCALAPPDATA/Android/Sdk/emulator/emulator.exe" -avd Pixel_8 &
adb wait-for-device
```

(Or start it from Android Studio → Device Manager → ▶.) Wait until the emulator shows its home screen.

### Terminal B – Metro (keep it open)

```bash
cd /c/Bmwmobile/bmwmobile
npm start
```

Ready when you see: `Dev server ready. Press Ctrl+C to exit.` (port 8081)

### Terminal C – port forwarding, build and install

```bash
adb reverse tcp:8081 tcp:8081
cd /c/Bmwmobile/bmwmobile
npm run android -- --active-arch-only --no-packager
```

- `--no-packager` because Metro already runs in Terminal B. Without it the build stops and asks
  "Another process is running on port 8081. Use port 8082 instead?" – answer **No** if you ever see it.
- If the build fails with `'gradlew.bat' is not recognized as an internal or external command`, your Git Bash has
  the variable `NoDefaultCurrentDirectoryInExePath` set (some tools set it). Run the build like this instead:
  `env -u NoDefaultCurrentDirectoryInExePath npm run android -- --active-arch-only --no-packager`

- `adb reverse tcp:8081 tcp:8081` lets the app reach Metro. Run it **every time the emulator (re)starts**
  (the build command also runs it, but running it yourself is harmless).
- The API ports need **no** `adb reverse` on the emulator: the app calls `http://10.0.2.2:5001` (Identity) and
  `http://10.0.2.2:6001` (Gateway), and `10.0.2.2` is your PC as seen from the emulator.
- `--active-arch-only` builds only for the emulator's CPU – much faster and uses far less memory.
- First build: 5–15 minutes. Ready when you see `BUILD SUCCESSFUL` and the app opens on the emulator.
- After the first build you only need Metro: change code → the app reloads; or press `r` in the Metro window.
  Rebuild (`npm run android ...`) only after adding a native package.

**Real phone instead of the emulator:** connect it with USB debugging on, then

```bash
adb reverse tcp:8081 tcp:8081
adb reverse tcp:5001 tcp:5001
adb reverse tcp:6001 tcp:6001
```

and in `src/config.ts` change `DEV_HOST` to `'localhost'` (do not commit that change).

---

## 4. Check that each part is running

```bash
cd /c/Bmwmobile/bmwapi
docker compose ps                                                     # postgres, rabbitmq, meilisearch: (healthy)
curl -s http://localhost:5001/.well-known/openid-configuration | head -c 80; echo   # Identity
curl -s "http://localhost:7001/api/auctions" | head -c 80; echo                     # Auction
curl -s "http://localhost:7002/api/search?pageSize=1" | head -c 80; echo            # Search
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:7003/api/bids/ford-gt     # Bid (200)
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:7004/notifications        # Notification (any 4xx = up; 000 = down)
curl -s "http://localhost:6001/search?pageSize=1" | head -c 80; echo                # Gateway -> Search
curl -s "http://localhost:6001/search?lat=17.4849&lon=78.4138&radiusKm=25&orderBy=distance" | head -c 200; echo   # radius search
curl -s http://localhost:8081/status; echo                                         # Metro: packager-status:running
adb devices                                                           # emulator-5554  device
adb reverse --list                                                    # tcp:8081 tcp:8081
```

Web pages: RabbitMQ <http://localhost:15672>, Meilisearch <http://localhost:7700>.
User names and passwords for both are in `bmwapi/docker-compose.yml`.

---

## 5. Where the OTP code appears (test mode)

The backend runs in **Development**, so the 6-digit code is shown in three places:

1. **In the app** – a purple box on the OTP / Reset password screen: `💡 Test mode: your code is 123456`
2. **In the Identity log** – when Gmail is not set up:
   `Email is not configured - DEV OTP for you@example.com (Login): 123456`
   - script: `grep "DEV OTP" /c/Bmwmobile/bmwapi/.run/logs/identity.log | tail -1`
   - by hand: in the Identity terminal
3. **In your Gmail inbox** – only when you set the user-secrets in section 0.

Codes expire after 10 minutes, allow 5 tries, and can be resent after 30 seconds.

---

## 6. Database (pgAdmin)

| Setting | Value |
|---|---|
| Host | `localhost` |
| Port | `5433` |
| Username | `postgres` |
| Password | the `POSTGRES_PASSWORD` value in `bmwapi/docker-compose.yml` |
| Databases | `identity` (users, OTP codes, trusted devices, refresh tokens, `UserLocations`), `auctions` (`Auctions`, `Items` with location), `bids` |

pgAdmin: right-click **Servers → Register → Server…**, name it e.g. `OKOH local`, fill the **Connection** tab as above.

---

## 7. Stop everything (keeps all data)

```bash
cd /c/Bmwmobile/bmwapi
./stop-backend.sh                 # stops the .NET services and the containers
./stop-backend.sh --keep-docker   # stops only the .NET services
```

- Started by hand (section 2B)? Press `Ctrl+C` in each service terminal, then `docker compose stop` in `bmwapi`.
- Metro: `Ctrl+C` in its terminal. Emulator: close its window, or `adb emu kill`.
- **Keeps your data:** `docker compose stop` / `./stop-backend.sh` only stop containers.
  **Never** run `docker compose down -v` (that deletes the database and search volumes).
  `docker compose down` (without `-v`) is also safe but removes the containers.

Next time start again with section 1.

---

## 8. Common errors and fixes

| Problem | Fix |
|---|---|
| `Docker is not running` / `error during connect` | Start Docker Desktop and wait for "Engine running". |
| A service says `address already in use` / port 5001, 600x, 700x busy | It is already running. `./stop-backend.sh --keep-docker`, then start again. Find the process: `netstat -ano \| grep :5001`. |
| `The process cannot access the file ... because it is being used by another process` during build | A service is still running (or two builds ran at once). Stop the services, then `dotnet build Carsties.sln` once. |
| Search service fails at start (cannot reach `localhost:7001`) | Start Auction first and wait for its "Now listening" line. |
| App shows **"Can't reach the server"** | Identity (5001) or Gateway (6001) is not running – check with section 4. On a real phone, add the `adb reverse` lines for 5001/6001. |
| App stays on a dark/white screen or red box **"Cannot connect to Metro"** | Metro not running, or missing `adb reverse tcp:8081 tcp:8081`. Run it, then press `r r` in the app (or `r` in Metro). |
| Blue bar **"Fast Refresh disconnected"** | Metro restarted. Run `adb reverse tcp:8081 tcp:8081` and reload. |
| `INSTALL_FAILED_...` or the install hangs | `adb kill-server && adb start-server`; if it still hangs, cold-boot the emulator (Device Manager → ⋮ → Cold Boot) and build again. |
| Build asks `Another process is running on port 8081. Use port 8082 instead?` | Choose **No** (Ctrl+C), then build with `--no-packager` as in section 3. |
| `'gradlew.bat' is not recognized as an internal or external command` | Build with `env -u NoDefaultCurrentDirectoryInExePath npm run android -- --active-arch-only --no-packager` (section 3). |
| Emulator shows "System UI isn't responding" right after a cold boot | Tap **Wait**; it settles within a minute. |
| `./start-backend.sh: No such file or directory` or `Permission denied` | Make sure you are in `/c/Bmwmobile/bmwapi`, then run `bash ./start-backend.sh` (same for `bash ./stop-backend.sh`). |
| Gradle `There is insufficient memory` / build daemon crashed | Close other apps, use `--active-arch-only`, and stop VS Code's Java/Gradle extension (it starts extra Gradle processes). |
| `No space left on device` | Free disk: `npm cache clean --force`, delete old `android/app/build` folders, or `rm -rf ~/.gradle/caches`. |
| Android asks "find, connect to... nearby devices" on first start | That is the local-network permission React Native adds to debug builds. Tap **Allow**. |
| Code expired / "Too many wrong tries" | Tap **Resend Code Now** (after 30 s). |
| Google sign-in fails on the emulator | Add a Google account to the emulator (Settings → Accounts) and use an image with Google Play. |
| Signed out after restarting the app | "Remember me" was off at login, or you logged out. Log in with Remember me ticked. |
| `.NET 9 framework not found` when running the old test projects | Use `DOTNET_ROLL_FORWARD=Major dotnet test` (only .NET 10 is installed). |
| `dotnet tool restore` says the file "came from another computer" | In PowerShell: `Unblock-File C:\Bmwmobile\bmwapi\dotnet-tools.json`. |

---

## 9. Quick test checklist

1. App opens on the dark OKOH Parts splash screen.
2. ENTER MARKETPLACE → Create an account → code from section 5 → **Set your location** → parts list.
3. Log out (Home → `👤 name · Log out`), log in again: with "Remember this device" ticked no code is asked.
4. Forgot password → code → new password → log in.
5. Home → location chip → Manage locations → add Home (Hyderabad, 25 km) and Work (Bangalore, 10 km); switch between them: the list changes and cards show "X km away".
6. GPS: `adb emu geo fix 78.4138 17.4849`, press the emulator Home button, reopen the app → primary follows GPS (auto-update on).
7. Set Bangalore as primary → auto switch turns off → move GPS again → primary stays Bangalore.
