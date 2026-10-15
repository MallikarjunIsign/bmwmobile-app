# Run and test ShopCarParts locally (Windows, Git Bash)

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

### Settings with secrets (placeholders – never commit real values)

| Service | Setting | Default | How to set it |
|---|---|---|---|
| Auction | `Geocoding:Provider` | `BuiltIn` (offline city list for India, USA, Nigeria, Ireland – no key) | `appsettings.json`; switch to `Google` for exact cities |
| Auction | `Geocoding:Google:ApiKey` | empty | only when `Provider = Google`: `cd /c/Bmwmobile/bmwapi/src/AuctionService && dotnet user-secrets set "Geocoding:Google:ApiKey" "<your-key>"`, or the env var `Geocoding__Google__ApiKey` (Docker/K8s) |
| Identity | `Email:Username` / `Email:Password` | empty | user-secrets (see above) |
| Auction | `ImageStorage:Provider` | `Local` (photos saved in `src/AuctionService/uploads`, served by the Gateway under `/images`) | `appsettings.json`; `GoogleCloudStorage` for the cloud bucket |
| Auction | `ImageStorage:Gcs:Bucket` | empty | only with `GoogleCloudStorage`: `dotnet user-secrets set "ImageStorage:Gcs:Bucket" "<bucket-name>"` or env var `ImageStorage__Gcs__Bucket`. Optional `ImageStorage:Gcs:PublicBaseUrl` (CDN), `ImageStorage:Gcs:ObjectPrefix` (`listings`) |
| Auction | Google credentials | – | **never in settings**: env var `GOOGLE_APPLICATION_CREDENTIALS=<path to service-account .json outside the repo>` (or the workload identity in Google Cloud). The account needs *Storage Object Admin* on the bucket; the bucket must allow public reads (or put a CDN in front) |
| Auction | `Admin:Usernames` | `[]` | usernames that may approve makes/models sellers request (`/api/catalog/pending`, `/approve`), e.g. env var `Admin__Usernames__0=alice`. Accounts with the **admin role** (section 5A) may do this too |
| Identity | `Seed:AdminEmail` / `Seed:AdminPassword` | empty (no admin) | the local admin account, **user-secrets or env vars only** (section 5A); Docker: `SEED_ADMIN_EMAIL` / `SEED_ADMIN_PASSWORD` in `bmwapi/.env` |
| Identity | `Seed:SellerEmail` / `Seed:SellerPassword` | empty (no test seller) | one test seller (India, Hyderabad), same way (section 5A); Docker: `SEED_SELLER_EMAIL` / `SEED_SELLER_PASSWORD` |
| Identity | `Otp:InviteExpiryHours` | `72` | how long a new seller's "set your password" code works (`appsettings.json`) |
| Identity | `RoleBackfill:AuctionServiceUrl` | `http://localhost:7001` | used once to make existing users with listings sellers (section 5A); empty = skip |
| Identity | `SellerApplications:NotifyEmails` | `[]` | extra addresses told about new seller applications (every **admin account** is always emailed); `appsettings.json` or env var `SellerApplications__NotifyEmails__0=ops@…` |
| Identity | `RateLimits:AccountPerMinute` / `RateLimits:SellerApplicationsPerMinute` | `20` / `5` | requests per minute per IP address for `/api/account` and the seller form |
| Auction | Shipping rates (table `ShippingRates`) | seeded **PLACEHOLDER** values per country and size class (`isPlaceholder = true`) | not a setting: an admin sets the real ones with `PUT /shipping/rates/{country}/{sizeClass}` (`src/AuctionService/Shipping.http`). Design doc §15 lists the placeholders |

The Google key needs the **Geocoding API** enabled in Google Cloud Console (APIs & Services). Results are cached per
~1 km for `Geocoding:CacheHours` (24), so phones in the same area don't call Google again.

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
  Rebuild (`npm run android ...`) only after adding a native package **or changing the app name**.
- **Rebuild needed after a rename:** the app is called **ShopCarParts** (launcher label and the registered component name
  `ShopCarParts` in `app.json` / `MainActivity.kt`). An old build with the new JavaScript shows
  *"ShopCarParts has not been registered"* – run `npm run android -- --active-arch-only --no-packager` once.
  The Android package id stays `com.bidmywork.app` (Google sign-in keeps working, the app updates in place).
  New JS-only package `@microsoft/signalr` (live updates) needs `npm install` but no native rebuild.
- **Rebuild needed again for "+ Sell":** the new native package `react-native-image-picker` (camera + gallery for
  listing photos) and the `CAMERA` permission. Run `npm install`, then `npm run android -- --active-arch-only --no-packager`.
  Without the rebuild the photo step shows *"RNImagePicker could not be found"* / the camera button does nothing.

**Real phone instead of the emulator:** connect it with USB debugging on, then

```bash
adb reverse tcp:8081 tcp:8081
adb reverse tcp:5001 tcp:5001
adb reverse tcp:6001 tcp:6001
```

and in `src/config.ts` change `DEV_HOST` to `'localhost'` (do not commit that change).

---

## 3A. UI tests on the emulator (Maestro)

One-time install (Git Bash): `mkdir -p ~/.maestro && cd ~/.maestro && curl -sSL -o maestro.zip https://github.com/mobile-dev-inc/maestro/releases/latest/download/maestro.zip && unzip -qo maestro.zip`

Run the search UI test (backend, Metro and emulator running): `cd /c/Bmwmobile/bmwmobile && bash e2e/run-search-e2e.sh`

It logs in as the e2e test buyer and seller (accounts from `bmwapi/.run/e2e.env`, created by the pre-demo check below), types
`bumper` on Home, taps the suggestion and checks that only bumpers are shown, then a category suggestion, a category tile and
Market. Then, as the buyer in **Houston** (USA), the radius test `e2e/radius.yaml`: the location shows **200 mi** → the far
shipping-only listing (Long Beach) is shown with "🚚 Ships to you" and the far pickup-only one (Round Rock) is shown → radius
**25 mi** on Home → Round Rock disappears, Long Beach stays, "See All (N)" matches the API at 200 and 25 mi → back to 200 mi.
The e2e accounts' saved locations are reset first (only these test accounts), so they start with the default radius.
Flows: `e2e/search-filters.yaml`, `e2e/radius.yaml`, `e2e/subflows/login.yaml`. Screenshots of a failed run: `~/.maestro/tests/<latest>`.

Seller application form ("Want to sell?", signed out from Sign up → stored → admin API lists it → admins notified):
`cd /c/Bmwmobile/bmwmobile && bash e2e/run-seller-application-e2e.sh` (needs the e2e admin – start the backend once with the pre-demo check below).

If a UI test seems stuck: every adb call and each Maestro run has a time limit (15 min), so it ends with FAILED and a reason.
"The emulator doesn't respond" → close the emulator and cold boot it (Device Manager → ⋮ → Cold Boot Now), then run again.
Leftover Maestro processes from an interrupted run are stopped automatically (`e2e/stop-stale-maestro.sh`).

---

## 3B. Pre-demo check – run this before every demo

```bash
cd /c/Bmwmobile/bmwmobile
bash e2e/pre-demo-check.sh
```

Needs Docker Desktop running and Maestro installed (3A); it starts the emulator (`Pixel_8`, or `AVD=<name>`) if none is running.
It starts from clean and tests search the way a user uses it (10–15 minutes):

1. stops the .NET services and Metro (data is kept)
2. backend search tests: every filter alone and combined with text, category counts = results, index settings re-applied after a restart
3. starts the backend with `./start-backend.sh` (fresh build) plus an e2e test seller; creates the e2e test buyer
4. live API tests: app → Gateway → Search → Meilisearch with the demo data (each category tile's count = what tapping it returns,
   far shipping listings shown / far pickup-only hidden in the USA and India, …)
5. app unit tests (`npx jest`)
6. Metro with `--reset-cache`, clears the app data, then the UI tests (3A): search as buyer **and** seller, the radius test as the buyer in Houston

It ends with `=> READY for the demo` or `=> NOT READY` and the failed step. Logs: `bmwmobile/.e2e-results/`.
The e2e accounts (`e2e.buyer@…`, `e2e.seller@…`) and their random passwords are in `bmwapi/.run/e2e.env` (git-ignored, never printed).
The backend keeps running afterwards for the demo.

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

## 5A. Accounts and roles: buyer, seller, admin

Every account has one role, stored in Identity and put in the access token as `"role"`:

| Role | How it is created | What the app shows | Backend rules |
|---|---|---|---|
| **buyer** | Sign up in the app (email, Google or Apple) – always a buyer, no role choice | Home, Market, listing, Place bid / Buy now, My Bids, Orders (purchases), Messages, Profile (locations, addresses, My Garage, "Want to sell? **Apply to sell**") | can bid, buy, chat, review |
| **seller** | Only by an admin through the API below (or an upgraded buyer) | everything a buyer sees **plus** the Sell tab, My Listings, Orders → **Sales** (mark ready / handed over / shipped), seller profile with reviews, accept & close bids | only sellers create / edit / publish / cancel / relist listings, add photos and accept bids; only the order's seller moves fulfilment steps. Others get **403** `{ "message": "Only seller accounts can list…", "code": "SELLER_ONLY" }` |
| **admin** | Seeded from user-secrets (below) | – (no admin screens yet: use Swagger or the `.http` file) | `/api/admin/*` on Identity; approves catalogue requests and hides reviews |

**Existing data (one-time migration, automatic at the first start of the new Identity):** every existing account
becomes a **buyer**; accounts that already have listings (asked once from the Auction service, retried while it
starts) become **sellers**. Identity log: `Gave the buyer role to N existing accounts` and
`Role backfill: N existing users with listings are now sellers`. It runs once (table `DataMigrations`).

### Local admin and test seller (secrets – never commit them)

Pick your own emails and passwords (passwords: 8+ characters with upper, lower, number and symbol):

```bash
cd /c/Bmwmobile/bmwapi/src/IdentityService
dotnet user-secrets set "Seed:AdminEmail"     "admin@your-test-domain"
dotnet user-secrets set "Seed:AdminPassword"  "<admin password>"
dotnet user-secrets set "Seed:SellerEmail"    "seller@your-test-domain"
dotnet user-secrets set "Seed:SellerPassword" "<seller password>"
```

Restart Identity: the accounts are created if missing (log: `Seeded admin account …` – never the password).
Docker instead: put `SEED_ADMIN_EMAIL`, `SEED_ADMIN_PASSWORD`, `SEED_SELLER_EMAIL`, `SEED_SELLER_PASSWORD` in `bmwapi/.env`.
Both log in with the normal Login screen + OTP code (section 5).

### Admin API (Identity, `http://localhost:5001`)

Swagger: <http://localhost:5001/swagger> (Development) → **Authorize** → paste an admin access token.
Ready-made requests: `bmwapi/src/IdentityService/IdentityService.http` (section "Admin").

| Request | What it does |
|---|---|
| `POST /api/admin/sellers` `{ fullName, email, phoneNumber, countryCode, businessName?, city? }` | create a seller; emails a one-time code to set the password (test mode: the code is in the response as `devInviteCode` and in the Identity log: `DEV OTP for … (SellerInvite)`) |
| `POST /api/admin/sellers/{id}/invite` | send the code again (only before the seller has set a password; 30 s cooldown) |
| `PATCH /api/admin/sellers/{id}` `{ "disabled": true }` (or name, phone, country, city, businessName) | update, disable (signs them out everywhere, login refused) or enable |
| `GET /api/admin/sellers?search=ravi` | list / search sellers by name, email, username or business name |
| `GET /api/admin/users?search=ravi` | all accounts with their role (to find a buyer's id) |
| `GET /api/admin/seller-applications?status=New` | requests from the app's "Become a seller" form (name, email, phone, city, business type, what they sell, business name / address, tax number, message) |
| `PATCH /api/admin/seller-applications/{id}` `{ "status": "InReview" \| "Rejected", "adminNote": "…" }` | review a request; a rejection note is shown to the applicant (Profile) and they can apply again |
| `POST /api/admin/users/{id}/make-seller` | upgrade a buyer; the app picks up the new role at the next token refresh, or at once when it comes back to the foreground – no reinstall |

Get an admin token: the `.http` file signs in like the app (email + password, then the OTP code).
Anyone else gets **403** `{ "message": "Only admins can do this.", "code": "ADMIN_ONLY" }`; no token → 401.

**New seller in the app:** Login → **Got a seller invite? Set your password** → email + code from the invite + new
password → log in with email + password + OTP. Before that, logging in says "You haven't set a password yet…".

---

## 6. Database (pgAdmin)

| Setting | Value |
|---|---|
| Host | `localhost` |
| Port | `5433` |
| Username | `postgres` |
| Password | the `POSTGRES_PASSWORD` value in `bmwapi/docker-compose.yml` |
| Databases | `identity` (users, OTP codes, trusted devices, refresh tokens, `UserLocations` – no longer used by the app), `auctions` (`Auctions`, `Items` with location, `PartRequests` = custom orders, `Purchases`), `bids` |

pgAdmin: right-click **Servers → Register → Server…**, name it e.g. `ShopCarParts local`, fill the **Connection** tab as above.

New tables are created automatically when the Auction service starts (EF Core migrations `CustomOrders` and
`ListingModel` – they only **add** tables and columns; existing data is untouched):

- master data: `Categories`, `PartTypes` (synonyms, spec fields), `Makes`, `VehicleModels`, `VehicleConfigs`
- listings: new columns on `Items` (kind, fitment mode, part type, title, currency, delivery, …) and `Auctions`
  (bid count, seller contact); `PartDetails`, `PartFitments`, `PartNumbers`, `PartSpecs`, `VehicleDetails`
- `Orders` (with delivery method, address, courier, tracking number) and `OrderEvents` (one row per tracking step,
  migration `OrderTracking`), `GarageVehicles` (My Garage)
- identity database: `UserLocations` gets address columns (house/street, area, landmark, postal code – migration `UserAddresses`)
- auctions database (migration `ShippingCharges`): new table `ShippingRates` (placeholder rows seeded at start); `Orders` gets
  `ShippingSizeClass`, `ShippingDistance`, `ShippingDistanceUnit` (the charge itself stays in `ShippingFee`, now calculated and fixed when
  the buyer chooses Shipping); addresses get `Latitude` / `Longitude`. Listings that ship but had no size class get the part type's default.
- search index: documents get `_geo`, `listingType`, `makes`, `models`, `years` – the Search service re-fetches older documents once at start.
- identity database: roles `buyer` / `seller` / `admin` in `AspNetRoles` / `AspNetUserRoles`; `AspNetUsers` gets
  `BusinessName`, `CountryCode`, `City`, `InvitedAt`, `IsDisabled`; new table `DataMigrations` (migration `SellerAccounts`)
- identity database (migration `SellerApplications`): new table `SellerApplications` – the "Become a seller" requests from the app
  (contact and business details, status New / InReview / Approved / Rejected, admin note, the seller account made for it)

On the first start the master data is seeded and **17 demo listings** of all three kinds (bumpers, tyres,
batteries, a Hilux, …, with generated placeholder photos) are added in India, Nigeria, the USA and Ireland; older auctions are converted to the new model.
The Search service re-indexes documents that miss the new fields when it starts.

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

1. App opens on the dark **ShopCarParts** splash screen; the launcher label is "ShopCarParts".
2. ENTER MARKETPLACE → Create an account → code from section 5 → Android asks for location → parts list.
   There is **no** location screen any more.
3. Log out (Home → `👤 name · Profile` → **Log out**), log in again: with "Remember this device" ticked no code is asked.
4. Forgot password → code → new password → log in.

### Automatic location (mock positions on the emulator)

`adb emu geo fix <longitude> <latitude>` (longitude first!), then bring the app to the foreground again
(emulator Home button → reopen) so it reads the position:

| Place | Command | Expected |
|---|---|---|
| Hyderabad, India | `adb emu geo fix 78.4867 17.3850` | 200 km radius, ₹, distances in km |
| New York, USA | `adb emu geo fix -74.0060 40.7128` | 200 mi radius, $, distances in miles |
| Lagos, Nigeria | `adb emu geo fix 3.3792 6.5244` | 200 km, ₦ |
| Dublin, Ireland | `adb emu geo fix -6.2603 53.3498` | 200 km, € |

(200 is the default radius until the user chooses another one – see "Radius" below.)

- The location changes only after a move of more than ~5 km (or into another city).
- Check what the backend answers: `curl -s -H "Authorization: Bearer <token>" "http://localhost:6001/geo/reverse?lat=17.385&lon=78.4867"`.
- **Deny** the permission (or switch location off in the emulator's quick settings): a banner
  "Turn on location to see parts near you" appears on Home and Market, results are country-wide (country from the
  phone number). "Turn on" asks again, or opens Settings when Android won't ask any more.
  Reset the permission: `adb shell pm reset-permissions com.bidmywork.app` (or uninstall/reinstall).

### Step 1 – lead review changes

5. Home: no "Parts near you" block, no location text, no container banner; tabs are Home / Market / My Bids / Orders (no Tracking).
6. Market: tap a category pill → only that category; add a make/model pill → narrowed further; both show as chips in the search bar; "All" / "All Models" clear them; the count updates.
7. Search something that doesn't exist (e.g. `zzz`) → "There are no parts available for this search. Create a custom order …" → **Create custom order** (pre-filled) → Orders tab shows it as **OPEN**.
8. With another account, publish a matching part within the buyer's radius, or one that ships (e.g. Swagger `POST http://localhost:7001/api/auctions` or the
   API with the same part name/make/model) → the requester gets "A part matching your request is now available" (live, or the next time the app opens) → **View part** opens the listing; the order shows **MATCHED**.
9. Place Bid: "Current bid $1,000 + step $20 = Next bid $1,020" and "Part price … + your bid … = Total …"; quick bids next/+1/+2/+5 steps.
   Totals 100 → 110, 1,000 → 1,020, 5,000 → 5,050. A lower bid through the API is rejected with the next valid amount:
   `curl -X POST -H "Authorization: Bearer <token>" "http://localhost:6001/bids?auctionId=<id>&amount=1"`.
10. Bid from a second phone/account: the next-bid line on the first phone updates by itself (SignalR).

Use two accounts (A = seller, B = buyer). Rebuild the app first (section 3, image picker).

### Step 2 – "+ Sell"

11. **Sell** tab → three choices: *Common part*, *Part for a specific vehicle*, *Vehicle*.
12. Vehicle-specific part: type "bumper" → pick **Bumper** → *Fits which vehicles* → + Add vehicle Toyota / Hilux /
    2016–2020 (or "⧉ Copy fitment from my last listing") → Details (Genuine OEM, Used grade A, part number
    `52119-0K907`) → Photos → Location (pre-filled) → Auction (part price; the bid steps are shown read-only) → Review → **Publish**.
    The listing opens; its title is built automatically ("2016–2020 Toyota Hilux Front Bumper – Genuine OEM – Used (Grade A)").
13. Close the app halfway through a listing → **Sell tab → My Listings** (also linked from the Orders tab) → **Draft** → it reopens on the same step (autosave).
14. Publish with missing data → "Please fix these first" lists each problem with the step to fix.
15. Common part (e.g. Tyre): asks only for the size (width / profile / rim), no vehicle questions.
16. Vehicle: identity, condition, papers for your country (India: RC, insurance, PUC…), 8 guided photo angles; the VIN is shown masked to buyers.
17. Description with a phone number or e-mail → publish is refused ("Please don't put phone numbers or e-mail addresses …"); contact details are only shared after the sale.
18. Unknown make/model → type it in the picker → "➕ Other – request to add …" → usable on your listing at once, shown to others after an `Admin:Usernames` user approves it.

### Step 3 – photos

19. Photos step: camera and gallery; a progress bar per photo; parts 3–10, vehicles 8–20; set the cover; the photo is ≤ 1600 px with a 400 px thumbnail.
20. GPS/EXIF is removed: download a photo (`curl -o p.jpg <url>`) and check it has no location (e.g. Windows → Properties → Details).
21. After the first bid photos can only be **added**, not removed.
22. Cloud bucket (optional): set the Google Cloud Storage settings from section 0 → new photo URLs start with `https://storage.googleapis.com/<bucket>/listings/`.

### Step 4 – search

23. Home or Market search: type `hilux front bum` → suggestions grouped (Parts / Vehicles / Part numbers) → pick one → chips.
24. Type `265/65R17` → tyres of that size only; `52119 0K907` (with or without the dash/space) → the exact part first.
25. Home → **+ Add your vehicle / ⚙ My Garage** → add 2016 Toyota Hilux → Market shows the garage pill; results get "✓ Fits your 2016 Toyota Hilux"; universal parts show "Universal".
26. Filter sheet: condition, brand type, price, distance, sort (best / ending soon / price / newest / nearest); facet counts update.
27. List / grid toggle; the listing page shows the photo carousel, fitment table, details, bid history and the seller's area (not the address).

### Step 5 – bidding, end and orders

28. A opens their own listing → no Bid button ("This is your listing – you can't bid on it."); B bids → A gets "New bid" live; B's button then says "YOU'RE THE HIGHEST BIDDER".
29. A third account outbids B → B gets "You've been outbid" (toast, even on another screen).
30. **My Bids**: Winning / Outbid / Won / Lost tabs. **My Listings**: Draft / Live / Ended – no bids / Sold / Completed / Cancelled.
31. Seller: **ACCEPT $X AND CLOSE** → the auction ends now; B gets "You won! 🎉" (tap → the *Victory – auction won* screen), the other bidders "Auction lost" (tap → the *Auction lost* screen). An ended listing also shows **YOU WON · VIEW** / **AUCTION LOST · VIEW** to its bidders.
32. Orders: each order has a step bar and **Track order ▾** (timeline with dates). The steps depend on the delivery:
    - *Seller ships*: Part won → Packed → Dispatched (courier + tracking no., optional) → Out for delivery → Delivered
    - *Pickup*: Part won → Ready for pickup → Picked up
    - *Buyer's courier*: Part won → Packed → Handed to courier → Delivered

    If the listing offered several options, B first picks one ("How do you want to receive it?"; *Seller ships* asks
    which saved address). A taps **Mark as …** for each step, B taps **Confirm delivered / picked up**. Each step
    notifies the other side ("Order update: Dispatched"). Both see each other's phone/e-mail.
33. A listing ending with no bids → "Ended – no bids" → **RELIST** (same data, new end date).
34. One hour before the end, bidders get "Auction ending in 1 hour" (the reminder is scheduled when the listing goes live or its end time changes – a database edit in pgAdmin does
   **not** reschedule it). To test quickly, before any bid move the end to ~65 minutes from now:
   `curl -X PUT -H "Authorization: Bearer <seller token>" -H "Content-Type: application/json" -d '{"auctionEnd":"<UTC time>"}' http://localhost:6001/auctions/<id>`,
   then bid with B and wait ~5 minutes.
35. **Location on Home**: under your name "📍 Kukatpally, Hyderabad, India · 200 km ▾" (GPS city, or the address you chose,
    and its radius). Tap it → a sheet to change the radius or the location without leaving Home (see "Radius" below);
    **Manage locations** → **My locations**: *Use my current location* or a saved address (**Use this address**, Edit, Delete) →
    **+ Add new address** (city from search or *Use my current position*, house/street, area, landmark, PIN/ZIP code,
    radius, Home/Work/Other). A chosen address is used for the search with its own radius and as the default item location;
    GPS no longer changes it until you pick *Use my current location* again.
36. Cancel a live listing without bids → Cancelled; once it has bids the Cancel link is gone (the API refuses with "A listing with bids can't be cancelled").

### Roles – buyer, seller, admin (section 5A)

Accounts: **B** = a new buyer you sign up in the app; **S** = the seeded test seller (`Seed:SellerEmail`);
**A** = the seeded admin (`Seed:AdminEmail`). Your older test accounts with listings are now sellers, those without are buyers.

37. Sign up → no role choice; under the form **Want to sell on ShopCarParts? Apply to sell** opens the **Become a seller** form
    (contact: name, email, mobile, city; business: you are – individual / parts shop / dealer / garage / scrapyard / other, you want
    to sell – parts / vehicles / both, business name, address, GSTIN/EIN/TIN/VAT, message) → **SEND TO OUR TEAM** → "Thanks, …!".
    The admins get an email "New seller application: …" with all details (test mode without Gmail: in the Identity log,
    `grep -A12 "New seller application" /c/Bmwmobile/bmwapi/.run/logs/identity.log`). Sending again with the same email →
    "We already have your request from …".
38. Log in as **B**: tabs Home / Market / My Bids / Orders (no **Sell**). Orders shows **MY PURCHASES** only, no "My listings".
    Home → `👤 name · Profile`: badge **BUYER**, Saved locations & addresses, My Garage, **APPLY TO SELL** (form pre-filled with name, email, phone,
    city), Log out. After sending: "✅ Request sent on …". Admin: `GET /api/admin/seller-applications?status=New`, then
    `POST /api/admin/users/<userId>/make-seller` (or `POST /api/admin/sellers` for someone without an account) → the request
    shows **Approved**; `PATCH …/seller-applications/<id>` `{ "status": "Rejected", "adminNote": "…" }` → Profile shows the note.
39. **B** can bid on auctions and Buy now on direct sales as before. Through the API **B** can't list:
    `curl -X POST -H "Authorization: Bearer <B token>" -H "Content-Type: application/json" -d '{"kind":"CommonPart"}' http://localhost:6001/listings` → 403 `SELLER_ONLY`.
40. Log in as **S**: the **Sell** tab is there; Orders has **Purchases | Sales (n)** and "My listings →"; Profile shows
    **SELLER**, Sell an item, My Listings, My seller profile & reviews. On S's own auction with bids: **ACCEPT … AND CLOSE**.
41. Admin creates a seller (`.http` → Admin): `POST /api/admin/sellers` → the response has `devInviteCode` (test mode).
    In the app: Login → **Got a seller invite? Set your password** → email + code + new password → log in (OTP) → Sell tab.
42. Admin upgrades **B**: `GET /api/admin/users?search=<B's name>` → `POST /api/admin/users/<id>/make-seller`.
    In the app (still signed in as B): send the app to the background and reopen it → the **Sell** tab appears.
43. Admin disables that seller: `PATCH /api/admin/sellers/<id>` `{ "disabled": true }` → logging in says
    "This account has been disabled. Please contact support."; `{ "disabled": false }` lets them in again.
44. **B**'s token on `GET /api/admin/sellers` → 403 "Only admins can do this."

### Home filters, direct sale and search (Step 2)

Accounts: **B** (buyer) and **S** (seller with a few parts and one vehicle – e.g. the seeded test seller). No app rebuild needed (reload in Metro).
After pulling the change, restart the backend once so the Search service re-indexes (log: `Search index populated with N items`).

45. Home (as B): no "All vehicles" / "+ Add your vehicle" / garage chips. Under the search box: **[Parts] [Vehicles]** and
    **[All] [Auction] [Direct sale]**. Parts shows **SHOP BY CATEGORY** tiles with counts, e.g. "🛑 Brakes (3)"; categories with 0 are hidden.
46. Tap **Direct sale** → the tiles' counts and the list below show only direct sales; **Auction** → only auctions.
47. Change the location (Home → 📍) to another city / country → the counts change (pickup-only listings within the radius,
    listings that ship from anywhere in that country – see "Who sees which listings" below).
48. Tap a tile (e.g. Body – Exterior) → it is highlighted, a **Body – Exterior ✕** chip appears in the search box and the list below
    and "See All (n)" update at once (Home stays open). **See All →** opens Market with the same filter and the part types as chips with counts ("Bumper (2)").
49. **Vehicles** → "N vehicles near you", make chips with counts ("Toyota (2)"); tap one → only that make. Market shows models as chips.
50. **The search bug:** on **Home** (and on Market) choose **Direct sale** and a category, type `front bum` → pick a suggestion → a chip
    appears and the list updates at once, keeping Direct sale + the category (check the count). Pick a second suggestion (e.g. a part
    number) → its chip is **added**. Type `brake` → **CATEGORIES: Brakes** ("All parts in Brakes") → a Brakes chip, like the tile.
    Home and Market share one search: what you pick on one is on the other. With **Vehicles** chosen, typing `hilux` suggests only
    vehicles; with **Parts**, "Parts for Toyota Hilux …". The keyboard's search key searches the typed words with all chips.
51. ✕ in the search box clears the text and every chip in it (also the category chip) but keeps Direct sale / Auction;
    **Clear all** (Home and Market) clears everything.
52. My Garage: Profile → My Garage to add a vehicle; Market → ⚙ filters → **My Garage** section → pick it → results for that vehicle.
53. Direct sale (as S): Sell → **Direct sale** → price, quantity (default 1). As B the card says **Buy now – ₹4,500**; buy 1 → the order
    appears in Orders and the units left go down; at 0 the listing shows **SOLD OUT**. S can't buy their own item.

### Shipping charges (Step 3)

Use B and S in the same country (e.g. India). The rates are the seeded **placeholders** (design doc §15).

54. Sell (as S): Shipping ticked → **no fee field**; "No fee to enter …" and the **Size class** chips with the part type's default
    pre-selected (bumper → Large, headlamp → Medium, bulb → Small); vehicles show "Vehicle".
55. Listing (as B, location e.g. Kukatpally): "📍 Pickup in Hyderabad (free) · 🚚 Shipping" and
    "🚚 Shipping to Kukatpally: ~₹… (estimate)". Move the location to another country (e.g. `adb emu geo fix 3.3792 6.5244`, Lagos) →
    "Shipping not available to Lagos: Ships within India only".
56. Buy now → the confirmation says "Pickup: free. Shipping: ~₹… – the exact charge is shown when you choose it."
57. Orders → **Ship it to me** → **Next: shipping address** → the sheet shows **DELIVERY LOCATION** (your saved locations and
    "📡 Use my current location") and "Shipping charge: ₹… · about N km"; pick another location → the charge changes; fill in the
    address → **Ship here · ₹…**. The order shows "Part price ₹… + shipping ₹… (N km) = ₹…" on both phones.
58. Choose **I will pick it up · free** on another order → "… + pickup free = ₹…".
59. Too far: as S list a part with size class **Freight** in Hyderabad; as B choose a delivery location in Delhi (add it in My locations)
    → "Too far to ship: about 1,6xx km (up to 1,500 km for freight items)" and the button stays disabled.
60. Admin: `GET /shipping/rates?country=IN` (all `isPlaceholder: true`); `PUT /shipping/rates/IN/Small` with new values
    (`src/AuctionService/Shipping.http`) → new estimates change; the order from item 57 keeps its charge. A non-admin gets 403 `ADMIN_ONLY`.

### Part requests: buyers ask, sellers are told (custom orders)

Accounts: **B** (buyer) and **S** (seller), on two emulators/phones (or switch accounts). Push (app closed) needs Firebase
(`google-services.json` + ChatService `Push:Provider = Firebase`); without it everything below still works live while the app is open.

61. As B search for something nobody sells (e.g. `zzz bumper`) → **Create custom order** → fill part / vehicle → submit.
62. S (app open) gets a banner **"🔎 A buyer is looking for a part – Front bumper – 2016 Toyota Hilux (Hyderabad)"**; tapping it opens
    **Buyer requests** (also in Profile → Buyer requests). The list shows the part, vehicle, the buyer's city and budget – never their name.
    With push set up, S also gets it as a phone notification when the app is closed; tapping opens Buyer requests.
63. B (or any buyer) never gets this banner; a seller who makes a request doesn't get their own.
    **Only sellers near the buyer are told:** within the buyer's radius (200 km / 200 mi by default) when the buyer's location is on, otherwise
    anywhere in the buyer's country – **never in another country**. Try it: move S to Delhi (`adb emu geo fix 77.2090 28.6139`)
    → S no longer gets B's Hyderabad requests (and doesn't see them in Buyer requests); move S to Lagos → never India's requests.
    The seller's location is the app's current location (re-sent when it moves ~10 km) – for the live banner, the one in their
    sign-in token (updated at the next token refresh, at most an hour).
64. S taps **I have this – list it** → Sell → lists a matching part (auction or direct sale) within the buyer's radius → publish.
65. B gets **"A part matching your request is now available"** (live, plus a push when the app is closed; tap → the listing);
    B's Orders → MY CUSTOM ORDERS shows **MATCHED**. Every buyer with a matching open request is told.
66. API: `GET http://localhost:6001/partrequests/open` with S's token → the open requests; with B's token → 403 `SELLER_ONLY`.

### US shipping: USPS Ground Advantage (from 14 Oct 2026 all testing is US-first)

Use US accounts and locations: seller in **New York** (`adb emu geo fix -74.0060 40.7128`), buyer in **Philadelphia**
(`adb emu geo fix -75.1652 39.9526`, ~81 mi → zone 2) or **Los Angeles** (`adb emu geo fix -118.2437 34.0522`, zone 8).
Prices come from the official USPS Notice 123 list (effective 4 Oct 2026) – no USPS keys needed. Restart the backend once
(migration `UspsShipping` adds the package fields).

67. Sell (US seller, a part, Shipping ticked): **PACKAGE (FOR THE USPS PRICE)** with weight (lb) and box L × W × H (in) pre-filled
    for the part type (e.g. Dash cam 2 lb, 10 × 8 × 6). Change them → saved with the draft. India / Nigeria / Ireland listings don't show it.
68. Listing as the Philadelphia buyer: "🚚 Shipping to Philadelphia: ~$12 (USPS Ground Advantage, estimate)" for a 2 lb dash cam.
    As the Los Angeles buyer the same item is zone 8 (~$20).
69. Buy now → Orders → Ship it to me → the sheet shows "Shipping charge: $12 · USPS Ground Advantage, zone 2 · 2 lb" → Ship here.
    The order: "Part price $120 + shipping $12 (USPS Ground Advantage, zone 2) = $132" for buyer and seller.
70. Heavy / big: set the weight to 90 lb (or list a bumper – its default box is over 130 in) → "~$287 (Freight, estimated rate)"
    New York → Philadelphia. Freight and vehicles are delivered anywhere in the US at US industry-average estimates
    (freight $250 + $0.35/road mi, vehicles $350 + $0.70/road mi), labelled **estimated rate** on the listing, at checkout and on the
    order. When a carrier is chosen: `PUT /shipping/rates/US/Freight` (and `/Vehicle`) with the carrier's numbers and a `source`
    → the label disappears (existing orders keep their charge).
71. A buyer outside the US → "Shipping not available: Ships within USA only".

### Who sees which listings: pickup inside the radius, shipping country-wide

Rule (Search service, design doc §7.7): **pickup only** → shown when the item is inside the buyer's radius; **shipping only** or
**pickup + shipping** → shown anywhere in the buyer's country; **never** a listing from another country. Restart the backend once:
the Search service re-indexes every listing (index version 3: `country`, `offersPickup`, `offersShipping`) and AuctionService adds
the far-away demo listings below. No app rebuild needed (Metro reload is enough).

Demo listings for this (US first):

| Listing | Where | Delivery | Houston buyer, 200 mi (default) | 25 mi |
|---|---|---|---|---|
| Silverado alternator (`demo-us-la-alternator`) | Long Beach, CA (~1,366 mi) | shipping only | **shown** – "🚚 Ships to you ~$… · 1,366 mi away" | shown |
| Camry radiator (`demo-us-austin-radiator`) | Round Rock, TX (~147 mi) | pickup only | **shown** – "📍 Pickup · 147 mi away" | hidden |
| F-150 starter (`demo-us-phoenix-starter`) | Tempe, AZ (~1,006 mi) | pickup only | hidden | hidden |
| Tyre 265/65 R17 (`demo-us-tyre-265-65-17`) | Dallas (~225 mi) | pickup + shipping | **shown** – "🚚 Ships to you …" | shown |
| F-150 headlamp, Silverado tail lamp | Houston / Pasadena | pickup + shipping | **shown** – "📍 Pickup · 11 mi away · 🚚 Shipping ~$…" | shown |

India (Hyderabad buyer, 200 km): `demo-in-bengaluru-alternator` (shipping only, ~500 km) and `demo-in-warangal-radiator` (pickup
only, ~135 km) are shown; `demo-in-mumbai-gearbox` (pickup only, ~620 km) isn't. With 50 km the Warangal radiator disappears.

72. Buyer B, location **Houston** (`adb emu geo fix -95.3698 29.7604`) → Home: the Silverado alternator is in LIVE LISTINGS with
    "🚚 Ships to you ~$… · 1,366 mi away" (no pickup badge); the Houston headlamp shows "📍 Pickup · … mi away · 🚚 Shipping ~$…".
    No India / Nigeria / Ireland listings anywhere. The category tile counts, the vehicle total / make counts and "See All (N)" match the lists.
73. Market → filter sheet → **Delivery: Pickup** → only the listings you can pick up inside your radius (Houston, Pasadena, Round Rock
    at 200 mi); **Shipping** → every listing that ships in the USA (Long Beach, Dallas, Houston …). The counts next to Pickup / Shipping are what you get.
74. Sort **Nearest first** → Houston first, Long Beach last; every card shows its distance.
75. Type `84130` → the part-number suggestion **84130813** (the Long Beach alternator) appears; as a buyer in India it doesn't.
76. Open the Long Beach alternator → "🚚 Ships to you ~$… · 1,366 mi away", "Shipping to Houston: ~$… (USPS Ground Advantage, estimate)".
    Buy now → Orders: shipping only, the address sheet opens.
77. Checkout outside the radius: buy a **pickup + shipping** item outside 200 mi (the Dallas tyre) → Orders → "How do you want to
    receive it?" has **🚚 Ship it to me** pre-selected; "📍 I will pick it up" says "… 225 mi away, outside your 200 mi radius" and can still be chosen.
78. Shipping not available: the seeded maximum distances cover every country (USA freight / vehicles 4,000 road mi, India 4,000 km,
    Nigeria 1,800 km, Ireland 700 km), so to see it an admin sets a short range: `PUT /shipping/rates/US/Freight`
    `{ "baseFee": 250, "ratePerUnit": 0.35, "minFee": 250, "maxFee": 1500, "maxDistance": 500 }`. As S list a **Freight**-size part in
    Long Beach with pickup off → as B (Houston) the card says "🚚 Shipping not available to your location"; Buy now / Place bid explain
    why and don't continue (pickup isn't offered). With pickup on they warn and offer "Buy anyway" / "Bid anyway". Put the rate back
    afterwards (maxDistance 4000, or delete the row and restart AuctionService to re-seed the estimate).
79. Seller profile (tap the seller on the Long Beach card) → only the listings this buyer can get; your own profile shows all of yours.
80. Part request: as B (Houston) request "Alternator – Chevrolet Silverado 2016"; as a seller anywhere in the USA list a matching part
    **with shipping** → B is told; a matching **pickup-only** part 1,000 mi away doesn't match.

### Radius: default 200, chosen per location (USA: miles)

One setting: `Contracts/RegionRules.cs` (backend: default 200, 5–1,000) and `src/utils/geo.ts` (app: `DEFAULT_RADIUS`,
`RADIUS_CHOICES`). The radius is in the location's unit – miles in the USA, km elsewhere – and is what the pickup rule above
uses. Restart the backend once: IdentityService runs migration `LocationRadius200` (saved locations with the old default –
60 km, or 60 mi in the USA – get 200; any other value, e.g. 50 km, is kept because it may have been chosen). No app rebuild
needed (Metro reload).

Accounts: B = the e2e buyer (`bmwapi/.run/e2e.env`) or a new buyer; S = the seeded test seller. Location: Houston
(`adb emu geo fix -95.3698 29.7604`).

81. Log in as a new buyer (or after clearing the app) → Home: "📍 Houston, …, USA · **200 mi** ▾". Same for a seller.
82. Tap the location line → **Location and radius** sheet: quick choices **25 / 50 / 100 / 200 / 500 mi** (200 selected) and
    "Or type a radius" (5–1,000; 1,200 or 3 → "Enter a radius from 5 to 1,000 mi."). Tap **25 mi** → **APPLY RADIUS** → the
    line shows "· 25 mi ▾" and, without leaving Home, the list and counts update: the Round Rock radiator (pickup only, ~147 mi)
    is gone, the Long Beach alternator (ships) stays. Back to **200 mi** → Round Rock comes back.
83. The same sheet: **Use my current location**, your saved addresses, **Manage locations / add an address →**.
84. My locations → **Use my current location** → the same radius sheet (the radius in use, 200 for a new user) →
    **USE MY CURRENT LOCATION**. The GPS card shows "Parts within 200 mi".
85. **+ Add new address** (or Edit) → **SEARCH RADIUS** with the same choices, 200 pre-selected; units follow the address's
    country (a Hyderabad address shows km). Save → the address card shows "Parts within … km / mi"; Edit → the radius is kept.
86. API: `PUT http://localhost:5001/api/me/locations/{id}/radius` `{ "radiusValue": 2000 }` → 400 `INVALID_RADIUS`
    "Choose a radius between 5 and 1,000 miles."
87. India check: location Hyderabad → "· 200 km ▾"; 50 km → the Warangal radiator (pickup only) disappears, the Bengaluru alternator (ships) stays.

