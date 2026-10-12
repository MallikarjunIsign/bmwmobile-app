# Run and test BidMyParts locally (Windows, Git Bash)

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
| Auction | `Admin:Usernames` | `[]` | usernames that may approve makes/models sellers request (`/api/catalog/pending`, `/approve`), e.g. env var `Admin__Usernames__0=alice` |

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
- **Rebuild needed now:** the app was renamed to **BidMyParts** (launcher label and the registered component name
  `BidMyParts` in `app.json` / `MainActivity.kt`). An old build with the new JavaScript shows
  *"BidMyParts has not been registered"* – run `npm run android -- --active-arch-only --no-packager` once.
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
| Databases | `identity` (users, OTP codes, trusted devices, refresh tokens, `UserLocations` – no longer used by the app), `auctions` (`Auctions`, `Items` with location, `PartRequests` = custom orders, `Purchases`), `bids` |

pgAdmin: right-click **Servers → Register → Server…**, name it e.g. `BidMyParts local`, fill the **Connection** tab as above.

New tables are created automatically when the Auction service starts (EF Core migrations `CustomOrders` and
`ListingModel` – they only **add** tables and columns; existing data is untouched):

- master data: `Categories`, `PartTypes` (synonyms, spec fields), `Makes`, `VehicleModels`, `VehicleConfigs`
- listings: new columns on `Items` (kind, fitment mode, part type, title, currency, delivery, …) and `Auctions`
  (bid count, seller contact); `PartDetails`, `PartFitments`, `PartNumbers`, `PartSpecs`, `VehicleDetails`
- `Orders` (with delivery method, address, courier, tracking number) and `OrderEvents` (one row per tracking step,
  migration `OrderTracking`), `GarageVehicles` (My Garage)
- identity database: `UserLocations` gets address columns (house/street, area, landmark, postal code – migration `UserAddresses`)

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

1. App opens on the dark **BidMyParts** splash screen; the launcher label is "BidMyParts".
2. ENTER MARKETPLACE → Create an account → code from section 5 → Android asks for location → parts list.
   There is **no** location screen any more.
3. Log out (Home → `👤 name · Log out`), log in again: with "Remember this device" ticked no code is asked.
4. Forgot password → code → new password → log in.

### Automatic location (mock positions on the emulator)

`adb emu geo fix <longitude> <latitude>` (longitude first!), then bring the app to the foreground again
(emulator Home button → reopen) so it reads the position:

| Place | Command | Expected |
|---|---|---|
| Hyderabad, India | `adb emu geo fix 78.4867 17.3850` | 60 km radius, ₹, distances in km |
| New York, USA | `adb emu geo fix -74.0060 40.7128` | 60 mi radius, $, distances in miles |
| Lagos, Nigeria | `adb emu geo fix 3.3792 6.5244` | 60 km, ₦ |
| Dublin, Ireland | `adb emu geo fix -6.2603 53.3498` | 60 km, € |

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
8. With another account, publish a matching part within 60 km (e.g. Swagger `POST http://localhost:7001/api/auctions` or the
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
35. **Location on Home**: under your name "📍 Kukatpally, Hyderabad, India ▾" (GPS city, or the address you chose).
    Tap it → **My locations**: *Use my current location* or a saved address (**Use this address**, Edit, Delete) →
    **+ Add new address** (city from search or *Use my current position*, house/street, area, landmark, PIN/ZIP code,
    Home/Work/Other). A chosen address is used for the 60 km / 60 mi search and as the default item location; GPS no
    longer changes it until you pick *Use my current location* again.
36. Cancel a live listing without bids → Cancelled; once it has bids the Cancel link is gone (the API refuses with "A listing with bids can't be cancelled").
