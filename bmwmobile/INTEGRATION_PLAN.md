# Integration Plan – Bringing BidMyWork auth + location features into the shared OKOH Parts project

Status: **Phase 1 (read-only analysis)**. No code has been changed. This file is the only file added.

- **Our code (read-only source):** `C:\Users\malli\Downloads\bmw-app\BidMyWork` (React Native CLI app) and `...\bidmywork-api` (.NET 8 API), branch `feature/location`.
- **Shared code (the base we will change):** `C:\Bmwmobile\bmwmobile` (mobile) and `C:\Bmwmobile\bmwapi` (backend).

> Note: the shared repo is currently on branch `develop`. There is **no `feature/auth-location` branch yet** (local or remote). In Phase 2 we create it from `develop`.

---

## 0. Big surprises (read these first)

1. **The shared mobile app is Expo (SDK 51, React Native 0.74, React 18), not React Native CLI.** Ours is RN CLI 0.87 / React 19. We **cannot copy our native setup or libraries as-is**. We use the Expo versions of the libraries instead (`expo-secure-store`, `expo-location`, `expo-apple-authentication`) and Expo config plugins.
2. **The shared mobile app has no real login yet.** Login, OTP and Profile screens are UI-only. They just navigate to the next screen. There are no tokens, no auth state, no token storage, and the user is hard-coded as `'demo_buyer'`. So we are **building** real auth into it, not replacing an existing one.
3. **The shared app has no `ios/` folder.** iOS needs `npx expo prebuild -p ios` on a Mac (or EAS Build). Apple sign-in cannot be tested without that and an Apple Developer account.
4. **The shared backend is Carsties.** We confirmed this (see section 1.2). One difference from what you expected: messaging uses **WolverineFx**, not MassTransit (still on RabbitMQ).
5. **Region mismatch.** The shared app is about **Nigeria** (Lagos ports, Apapa/Tin Can) and Germany. Our phone country list is IN/AE/GB/IE/US and our city catalog only has **Indian** cities. We need a Nigerian city list and a +234 default (question for the lead).
6. **Some "working" features in our API are dev-only:**
   - The OTP email is **only written to the log**. There is no real email sender.
   - There is no "terms accepted" field.
   - "Remember me" stores the **password** in the keychain.
   - The trusted device is a plain device-id string sent by the app.
   - There are no refresh tokens.
   - Apple sign-in does not check a nonce.

   The plan fixes these while porting, so we don't carry them over.

---

## 1. Overview of the shared code

### 1.1 Mobile – `C:\Bmwmobile\bmwmobile`

| Area | What is there today |
|---|---|
| Framework | Expo SDK 51 (`expo ~51.0.0`), RN 0.74.5, React 18.2, TypeScript (strict). Prebuilt `android/` folder, **no `ios/`**. Run with `expo run:android`. Hermes on, New Architecture **off**. |
| App id | `com.okoh.bmwmobile` (Android and iOS), app name "OKOH Parts", dark UI. |
| Navigation | React Navigation v6. One native stack in `src/navigation/AppNavigator.tsx`: `Splash → Login → Otp → ProfilePreferences → MainTabs`, plus about 15 detail screens. `MainTabs` = Home, Market, My Bids, Orders, Tracking (emoji tab icons). **No auth gating**: every route is always registered. There is also a "Continue as Guest" link. |
| Screens | 23 screens in `src/screens/` (Home, MarketplaceSearch, PartDetails, PlaceBidModal, MyBids, MyPurchases, ContainerTracking, Login, Otp, ProfilePreferences, Splash, …). |
| State | No global state. Local `useState` per screen. The mock arrays in `api.ts` act as the in-memory "store". |
| API client | `src/services/api.ts`: one axios instance, base URL hard-coded `http://10.0.2.2:6001` (the YARP gateway). No interceptors, no auth header. Every call falls back silently to mock data on error. Calls `/search`, `/auctions`, `/bids`, `/purchases`, `/containers/planned`, `/partrequests`. |
| Theme | `src/theme/colors.ts`: `COLORS` (navy `#07071C` background, card `#1A1A3A`, purple primary `#A100FF`, …), `SPACING`, `RADIUS`. No typography file; inline font sizes with heavy weights (700–900), many UPPERCASE call-to-action labels, emojis as icons. |
| Shared components | `Header` (title, subtitle, back button, right element), `Badge`, `AuctionCard`, `StatusTimeline`. **No shared Button / TextInput / Chip.** Each screen styles its own (`primaryBtn`, `inputWrap` + `TextInput`, height 48, radius 12). |
| Code style | Named exports `export const XScreen: React.FC<Props>`, `NativeStackScreenProps<RootStackParamList,'X'>`, `StyleSheet.create` at the bottom of each file, `SafeAreaView` from `react-native`, single quotes, 2-space indent, `{/* Section … */}` comments. No i18n. |
| Login today | `LoginScreen`: "Phone Number or Email" + password, "SEND VERIFICATION OTP", which goes to `Otp`. `OtpScreen`: one 6-digit input with a 45-second resend timer, then "VERIFY & CONTINUE" goes to `ProfilePreferences`. "Forgot password?" does nothing. No sign-up, no logout. |
| Tests | `tests/runAllScreenTests.ts`, a hand-written Node script (not Jest). Jest is installed but not configured. |

### 1.2 Backend – `C:\Bmwmobile\bmwapi` (Carsties, confirmed)

The README says "Carsties — Microservices Udemy Course Repository (2026 version)". The solution is `Carsties.sln`, all projects target .NET 9 (Docker images use .NET 10), and messaging is WolverineFx on RabbitMQ.

| Service | Database | Auth | Notes |
|---|---|---|---|
| **IdentityService** | Postgres (ASP.NET Identity tables) | **Duende IdentityServer 7.4** + ASP.NET Identity. Issues **JWT access tokens** (Duende default). | Clients in `Config.cs`: `bruno` (password grant, for testing) and `nextApp` (authorization code with PKCE, 30-day access token). API scope: `auctionApp`. `CustomProfileService` adds claims `username`, `email`, `name`. Razor pages: Login, Register, Logout and others. **No** Google/Apple, no email sender, no forgot-password, no OTP, no refresh tokens. `ApplicationUser` has no extra fields. Full name is stored as a `name` claim. |
| **GatewayService** | – | YARP. Checks JWTs against Identity (`ValidateAudience=false`, `NameClaimType="username"`). | GET routes are anonymous. POST/PUT/DELETE on `/auctions`, `/containers`, and POST on `/bids`, need the `default` (signed-in) policy. `/partrequests`, `/purchases`, `/search`, `/notifications` are anonymous. **No route to IdentityService today.** |
| **AuctionService** | Postgres (EF) + gRPC server | JWT from Identity, `NameClaimType="username"` | Seller is stored as the **username**. PartRequests and Purchases are demo controllers with **no migrations**. |
| **BidService** | Postgres (Dapper + dbup) | JWT from Identity | Bidder is stored as the **username**. |
| **SearchService** | Meilisearch | none | Search index built from events. |
| **NotificationService** | – | none | SignalR hub `/notifications`. |
| **Contracts** | – | – | `AuctionCreated/Updated/Deleted/Finished/Extended`, `BidPlaced`, `ContainerUpdated`. |

**How authentication works today, in one sentence:** IdentityService signs JWT access tokens with its own key. The Gateway, AuctionService and BidService trust any token whose issuer is IdentityService, and they read the user from the **`username`** claim. Audience is not checked, and Search and Notification don't check tokens at all.

### 1.3 How to run both locally

**Backend (Docker):**
1. Start Docker Desktop.
2. In `C:\Bmwmobile\bmwapi`, run `docker compose build` and then `docker compose up -d`. The override file switches everything to localhost.
3. You get these ports:
   - Postgres `5433`, RabbitMQ `5672` (admin UI `15672`), Meilisearch `7700`
   - Identity `5001`, Gateway `6001`
   - Auction `7001` (gRPC `7777`), Search `7002`, Bid `7003`, Notify `7004`
4. To run one service from Visual Studio or `dotnet run` instead, use the ports from its `launchSettings.json` (same numbers). Run `dotnet tool restore` first for `dotnet-ef`.
5. Seed users are `alice` and `bob` (passwords are in `SeedData.cs`).

**Mobile (Android emulator):**
1. In `C:\Bmwmobile\bmwmobile`, run `npm install` and then `npx expo run:android`.
2. The app reaches the gateway at `10.0.2.2:6001`.
3. Tip: `adb reverse tcp:6001 tcp:6001` and `adb reverse tcp:5001 tcp:5001` let the emulator use `localhost`. That keeps the token issuer URL identical to what IdentityService expects.

---

## 2. Feature-by-feature comparison

| # | Our feature | Shared mobile | Shared backend | Status | What is different / missing |
|---|---|---|---|---|---|
| 1 | **Sign up** (full name, phone with country code, email, password with live rules, terms checkbox) | No sign-up screen | Razor Register page (email, username, full name, password). No phone, no terms. Has a bug: it shows success even when create fails. Sets `EmailConfirmed=true` without checking. | **Missing** (mobile) / **Partly there** (backend) | Need a native SignUp screen, a JSON register endpoint, phone (E.164) and terms-accepted date stored, live password checklist. **The shared system needs a `username`** (used as seller and bidder everywhere); our sign-up doesn't ask for one. |
| 2 | **Login + 6-digit email OTP** (10 min expiry, 5 tries, resend after 30 s), Remember me, Remember this device (30 days) | Login and OTP screens exist **as UI only** (45 s timer, no API) | Username/password only (Razor page or `bruno` password grant). No OTP, no trusted device, no refresh tokens. | **Partly there** (UI only) | Wire real API calls. Add an OTP table, trusted-device table, email sender and refresh tokens. Change the login field from "Phone or Email" to Email. Change the resend timer from 45 s to 30 s, driven by the server. |
| 3 | **Forgot / reset password with OTP** | Link with no action | None | **Missing** | New screens and endpoints. Reuse the OTP service from feature 2. |
| 4 | **Google sign-in** (Android + iOS, links by email); **Apple** on iOS only | None | None (Duende template ExternalLogin pages, unused) | **Missing** | Validate the Google/Apple ID token on the server, store the link in the built-in `AspNetUserLogins` table, issue normal Duende tokens. Needs a Google Cloud OAuth client for `com.okoh.bmwmobile` and an Apple Developer setup. |
| 5 | **Customer / Service Pro roles off by a setting** | None (only a "buyer type" choice on ProfilePreferences: Workshop / Wholesale / Individual) | `IdentityRole` tables exist but are unused. No role claim. | **Missing** | Add a `Features:RolesEnabled` setting in Identity and an `ENABLE_ROLES` flag in the app. Use ASP.NET Identity roles and add a `role` claim. Ask the lead how "buyer type" relates to roles. |
| 6 | **Location + radius** (one-time Set Location screen, GPS, city chips, city search, radius 5–200 km default 50, Home location chip, saved on the account, GeoMath helpers) | None. Only fixed pickup ports. ProfilePreferences has "preferred port". | None. No lat/lon anywhere. | **Missing** | New screen, new fields on the user, city catalog (Nigeria), `GeoMath` (Haversine + bounding box). The shared app's Splash already has a "Set Pickup Port & Garage" button. Decide how location relates to "pickup port". |

---

## 3. Backend decision (most important)

### Option A – Add our features INTO the shared IdentityService (Duende) ✅ **Recommended**

Port our register / OTP / trusted device / forgot password / Google / Apple / location logic into `src/IdentityService`. Duende stays the **only** token issuer.

**Pros**
- Tokens are the same Duende JWTs as today, so **Gateway, Auction and Bid keep working with zero changes**. They already trust this issuer and read `username`.
- One user table (`AspNetUsers`). Seller and bidder names stay consistent with the web app (`nextApp`) and the seed users.
- ASP.NET Identity already gives us password hashing, lockout, `PhoneNumber`, `EmailConfirmed`, roles and an external-login table (`AspNetUserLogins`) for Google/Apple. We reuse these instead of our custom columns.
- Refresh tokens come for free from Duende, so "Remember me" can be done properly (no stored password).
- Nothing new to deploy: no extra container or k8s manifest.

**Cons**
- More work than "just run our API". Our code moves from a custom `User` (Guid id) to `ApplicationUser` (string id) and from our HS256 tokens to Duende.
- We have to learn Duende **extension grants**, a small, well-documented interface.
- Duende IdentityServer needs a **license** in production. The Community Edition is free for small companies; the lead should confirm.

### Option B – Run our `bidmywork-api` as a second auth service

**Pros**
- Our code runs almost unchanged.

**Cons** (these are serious)
- **Two user databases.** A user who signs up in the app does not exist for the web app or IdentityService, and the reverse.
- Our tokens are **HS256 with a shared secret key**. Every service would need that secret, and it would sit in many config files.
- Every service (Gateway, Auction, Bid) needs a second JWT scheme plus a policy that picks between them. That means touching code in 3+ services.
- Our tokens have no `username` claim, so seller and bidder names would break unless we fake one.
- Two places to fix security bugs, two password-reset flows, two sets of roles.

### Option C – Duende hosted login pages + OpenID Connect with PKCE (`expo-auth-session`)

Build our features as **Razor pages inside IdentityService**. The app opens them in a secure in-app browser using Authorization Code + PKCE, which is the textbook OAuth approach.

**Pros**
- Most standard and most secure approach.
- The web app (`nextApp`) would get the same new features for free.

**Cons**
- Sign-up, login, OTP and forgot-password screens would be **web pages in a browser sheet, not native screens**. They would not look like the shared app unless we restyle the Razor pages. This conflicts with the "UI must stay the same" rule, and the shared app already has native Login/OTP screens.

**Recommendation:** **Option A**, using Duende **extension grants** for the native screens. We could add Option C's Razor pages later for the web app, using the same services.

### 3.1 How the mobile app logs in with Option A

We add a new **public client** `mobileApp` (no secret, because a phone app cannot keep one) to `Config.cs`:
- Allowed grants: our custom grants plus `refresh_token`.
- Scopes: `openid profile auctionApp offline_access IdentityServerApi`.
- Access token: 1 hour.
- Refresh token: one-time use, sliding expiry of 30 days.

The app calls **IdentityService's standard token endpoint `POST /connect/token`** with custom grant types. It calls small JSON endpoints for everything that does not issue a token.

| Step | Call | Result |
|---|---|---|
| Sign up | `POST /api/account/register` `{fullName, email, phoneNumber (E.164), password, termsAccepted, role?}` | 201 user. Then the app logs in normally, which sends the OTP. |
| Login step 1 | `POST /connect/token` with `grant_type=bmw_password`, `username`, `password`, `device_id`, `device_token?` | **Trusted device:** tokens straight away. **Otherwise:** error `otp_required` with custom fields `challenge_id`, `masked_email`, `resend_after` (Duende lets an extension grant add these). |
| Login step 2 | `POST /connect/token` with `grant_type=bmw_otp`, `challenge_id`, `otp`, `device_id`, `remember_device` | Tokens. If "remember device" was ticked, also a **server-generated `device_token`** (random value; only its hash is stored). |
| Resend OTP | `POST /api/account/otp/resend` `{challengeId}` | `{resendAfterSeconds}`, or 429 if called too soon. |
| Forgot password | `POST /api/account/forgot-password` `{email}` | Always the same generic message (doesn't reveal whether the email exists). |
| Reset password | `POST /api/account/reset-password` `{email, otp, newPassword}` | Uses Identity's own `ResetPasswordAsync`. Removes all trusted devices and revokes refresh tokens. |
| Google / Apple | `POST /connect/token` with `grant_type=bmw_external`, `provider`, `id_token`, `nonce?`, `full_name?`, `role?` | Tokens. The account is linked by verified email through `AspNetUserLogins`. |
| Refresh | `POST /connect/token` with `grant_type=refresh_token` | New tokens. |
| Me | `GET /api/account/me` (protected by Duende's built-in "local API" auth) | User profile incl. location. |
| Logout | `POST /connect/revocation` (revoke the refresh token) + clear storage | – |

**Remember me** (fixed, compared with our version):
- **On:** the refresh token is kept in `expo-secure-store`, so the user stays signed in for up to 30 days. Only the **email** is remembered, never the password.
- **Off:** the refresh token is kept in memory only, so the user signs in again after the app restarts.

**Where the app sends each call:**
- `/connect/token` and `/api/account/*` go **directly to IdentityService** (`:5001`, the issuer).
- Business APIs keep going through the **Gateway** (`:6001`), now with `Authorization: Bearer <access token>`.
- Optionally we add an `identity` cluster to YARP later, so the app only needs one base URL.

### 3.2 Where location and radius should live

**Recommendation: as columns on the Identity user (`ApplicationUser`)**, with `GET/PUT /api/profile/location` and `GET /api/locations/*` hosted in IdentityService.

**Why:**
- It is one small record per user.
- It is read at login (it comes back with the user), and the onboarding check ("has the user set a location?") happens right after login.
- A whole new profile microservice (new database, Dockerfile, k8s file, gateway route, CI workflow) is too much for 5 columns today.

**For later:** when "items within X km" is built, AuctionService items will need their own lat/lon. Meilisearch already supports geo search (`_geo` field + `_geoRadius` filter), so SearchService can do "within X km" without custom SQL. `GeoMath.BoundingBox` is useful for Postgres queries in Auction or Bid. If other services ever need the user's location, IdentityService can publish a `UserLocationUpdated` message over Wolverine/RabbitMQ.

**GeoMath placement:** put `GeoMath.cs` in IdentityService now (only the location endpoints use it). Move it to a small shared class library when AuctionService or SearchService needs it, so we don't keep two copies. On mobile it goes in `src/utils/geo.ts`.

---

## 4. Database changes (Option A, IdentityService only)

One new EF migration in `src/IdentityService/Data/Migrations`, e.g. `AddMobileAuthAndLocation`, built with the existing `buildschema` scripts or `dotnet ef migrations add`. **No other service database changes.**

**`AspNetUsers`: new columns on `ApplicationUser`, all nullable so existing rows and seed users keep working**

| Column | Type | Notes |
|---|---|---|
| `FullName` | varchar(100) | Also keep writing the `name` claim, because the existing profile service and web app read it. |
| `TermsAcceptedAt` | timestamptz | Set at sign-up. |
| `CreatedAt`, `LastLoginAt` | timestamptz | |
| `LocationName` | varchar(120) | |
| `Latitude`, `Longitude` | double precision | |
| `SearchRadiusKm` | int | 5–200, default 50, applied in code. |
| `LocationUpdatedAt` | timestamptz | |

**Reused, no change needed:**
- `PhoneNumber` stores E.164 such as `+2348012345678`.
- `EmailConfirmed` is set true after the first OTP or after Google/Apple.
- `AspNetUserLogins` holds the Google/Apple links (provider `google`/`apple`, key = token `sub`). This replaces our `GoogleId`/`AppleId` columns.
- `AspNetRoles` / `AspNetUserRoles` hold the roles `customer` and `pro`, seeded only when `Features:RolesEnabled=true`.

**New table `OtpCodes`** (ported from ours)
- `Id` (uuid PK), `UserId` (text FK → AspNetUsers, cascade delete), `Purpose` (`Login` / `ResetPassword`), `CodeHash` (varchar 64), `Attempts` (int), `CreatedAt`, `ExpiresAt`, `UsedAt`.
- Index on (`UserId`, `Purpose`, `CreatedAt`).
- `Id` doubles as the login `challenge_id`.

**New table `TrustedDevices`** (improved from ours)
- `Id`, `UserId` (FK), `DeviceId` (varchar 100), **`TokenHash`** (varchar 64; we store only the hash of the server-generated device token), `DeviceName` (optional), `CreatedAt`, `ExpiresAt` (now + 30 days), `LastUsedAt`.
- Unique index on (`UserId`, `DeviceId`).

**Refresh tokens** need Duende's **operational store** (`Duende.IdentityServer.EntityFramework`, `PersistedGrants` table), so tokens survive a restart and can be revoked. Today the grants are in memory. This adds Duende's own `PersistedGrantDbContext` migration. Alternative for a first version: keep the in-memory store and accept that a restart signs everyone out (ask the lead).

**Also to know:** the AuctionService `PartRequests` / `Purchases` tables have no migration today. That is not ours to fix, but it may confuse testing (see risks).

---

## 5. Step-by-step build plan

Each step is small and testable on its own. Backend first, then mobile. All work happens on new branch `feature/auth-location` from `develop`.

### Backend (all under `C:\Bmwmobile\bmwapi\src\IdentityService` unless noted)

**B1 – User fields + migration**
- Change: `Models/ApplicationUser.cs` (new properties), `Data/ApplicationDbContext.cs` (configure lengths, new `DbSet`s).
- Add: `Models/OtpCode.cs`, `Models/TrustedDevice.cs`, new migration.
- Test: `docker compose up`. The migration applies, and `alice`/`bob` still log in through the Razor page and `bruno`.

**B2 – Options and helpers**
- Add: `Options/AuthOptions.cs` (Otp, TrustedDevice, SocialAuth, Features, Location sections, same keys as ours).
- Add: `Services/PasswordRules.cs`. Better still, set `IdentityOptions.Password` in `HostingExtensions.cs` (RequireDigit, RequireUppercase, RequireLowercase, RequireNonAlphanumeric, RequiredLength 8) so Identity enforces the same rules everywhere.
- Add: `Services/GeoMath.cs` and `Services/CityCatalog.cs` (Nigerian city list).
- Change: `appsettings.json` (non-secret defaults only).
- Test: unit tests for GeoMath, CityCatalog and radius clamping (ported from our tests).

**B3 – Email sender**
- Add: `Services/IEmailSender.cs`, `Services/LoggingEmailSender.cs` (dev), `Services/SmtpEmailSender.cs` (or the provider the lead picks).
- Config key names only, e.g. `Email:Host` / `Email:From`. Secrets go in user-secrets or env vars, never in git.
- Test: the dev log shows the code.

**B4 – OTP service**
- Add: `Services/OtpService.cs`, ported from ours. Hashed codes, 10-minute expiry, 5 attempts, 30-second cooldown, only the newest code valid.
- Test: unit tests for expiry, attempts and cooldown.

**B5 – Account API (no tokens yet)**
- Add: `Controllers/AccountController.cs` with register, forgot-password, reset-password, otp/resend.
- Change: `HostingExtensions.cs` (`AddControllers`, `MapControllers`, ASP.NET rate limiter on `/api/account/*` and `/connect/token`, local API auth).
- Username rule: generated from the email (e.g. `jane.doe` + 4 digits if taken), unless the lead wants a username field.
- Test: a `.http` / Bruno file. Register, then a duplicate email gives 409, then forgot and reset.

**B6 – Custom grants**
- Add: `Grants/PasswordOtpGrantValidator.cs` (`bmw_password`), `Grants/OtpGrantValidator.cs` (`bmw_otp`), `Services/TrustedDeviceService.cs`.
- Change: `Config.cs` (new `mobileApp` client, `offline_access` + `IdentityServerApi` scopes), `HostingExtensions.cs` (`.AddExtensionGrantValidator<…>()`).
- Change: `Services/CustomProfileService.cs` (add `name` from FullName, `phone_number`, `role` when enabled; keep `username`).
- Test:
  1. Get a token with OTP from the log.
  2. Call `POST /auctions` through the Gateway with that token. It must succeed, which proves other services trust it.
  3. Log in again with `device_token`. No OTP is asked.
  4. A wrong code 5 times gives "too many attempts".

**B7 – Refresh tokens + logout**
- Change: `Config.cs` (refresh token settings).
- Optional: add Duende's EF operational store (`PersistedGrants` migration).
- Test: refresh works; a revoked refresh token fails.

**B8 – Google / Apple**
- Add: `Grants/ExternalGrantValidator.cs` (`bmw_external`), `Services/SocialTokenValidator.cs` (ported; **add Apple nonce check**).
- Uses `UserManager.AddLoginAsync`, and links by email only when the provider says the email is verified.
- NuGet: `Google.Apis.Auth`, and `Microsoft.IdentityModel.Protocols.OpenIdConnect` for Apple keys.
- Test: a real Google ID token from the Android app, and the "same email links to existing account" case.

**B9 – Roles switch**
- Change: register and the external grant read `Features:RolesEnabled`. Seed roles when it is on. Add a `role` claim.
- Test: flag off means no role claim; flag on means `customer`/`pro` is required.

**B10 – Location API**
- Add: `Controllers/ProfileController.cs` (`GET /api/account/me`, `PUT /api/profile/location`), `Controllers/LocationsController.cs` (`popular-cities`, `cities?q=`, `nearest-city`).
- Test: `.http` file; radius gets clamped; (0,0) is rejected.

**B11 – Gateway / compose / k8s (only if we want one base URL)**
- Change: `GatewayService/appsettings*.json` (an `identity` cluster for `/account/**`, `/profile/**`, `/locations/**`).
- Change: `docker-compose.yml` (new env var **names** for email and social settings), `k8s/services/identity-svc.yml`.
- Change: `specs/identity-service.md` and `specs/gateway-service.md` so the docs match the code.
- Test: existing routes unchanged; new routes work.

**B12 – Tests project**
- Add: `tests/IdentityService.Tests` (xUnit + Alba + Testcontainers, the same style as the other test projects) covering register, OTP, grants and location.

### Mobile (all under `C:\Bmwmobile\bmwmobile`)

Follow the shared style everywhere:
- `COLORS`, `SPACING` and `RADIUS` from `src/theme/colors.ts`.
- `StyleSheet.create` at the bottom of each file.
- `SafeAreaView` from `react-native`.
- The shared `Header` component.
- Emoji icons (no new icon library).
- UPPERCASE primary buttons styled exactly like the existing `primaryBtn` and inputs like `inputWrap`.

**M1 – Config + libraries**
- Add: `src/config.ts` (IDENTITY_URL, GATEWAY_URL, ENABLE_ROLES, Google client IDs; IDs are not secrets but stay in config).
- Install the libraries in section 6 with `npx expo install`, and add their config plugins to `app.json`.
- Run `npx expo run:android`.
- Test: the app still starts and looks identical.

**M2 – Auth API + token storage**
- Add: `src/services/authApi.ts` (register, token grants, resend, forgot, reset, me, location), `src/services/tokenStorage.ts` (`expo-secure-store`), `src/utils/deviceId.ts` (`expo-crypto` random UUID instead of our `Math.random`).
- Change: `src/services/api.ts`. An axios request interceptor adds the Bearer token. A 401 triggers one refresh attempt, then sign-out. Keep the existing mock fallbacks for business data.
- Test: a quick script or Jest test for the token flow.

**M3 – Auth state**
- Add: `src/context/AuthContext.tsx` (`status`, `user`, login / verifyOtp / socialLogin / logout / updateLocation).
- Change: `App.tsx` to wrap in `AuthProvider`.
- Change: replace the hard-coded `'demo_buyer'` (e.g. `AuctionCard.tsx`) with the signed-in `username`.

**M4 – Navigation gating**
- Change: `src/navigation/AppNavigator.tsx` and `src/types/navigation.ts`.
  - **Signed out:** Splash, Login, SignUp, Otp, ForgotPassword, ResetPassword.
  - **Signed in, no location:** SetLocation (no back button).
  - **Signed in:** ProfilePreferences (if still wanted), MainTabs and all existing screens, plus SetLocation (editing).
- Keep "Continue as Guest" only if the lead wants guest browsing.
- Test: kill and reopen the app. "Remember me" on keeps you signed in; off asks again.

**M5 – Sign up**
- Add: `src/screens/SignUpScreen.tsx`, `src/components/PhoneField.tsx` (country sheet; add +234 NG and +49 DE), `src/components/PasswordChecklist.tsx`, `src/utils/validation.ts`, `src/utils/phone.ts`.
- Restyle everything in the dark/purple theme, with a terms checkbox and links.
- Change: `SplashScreen` / `LoginScreen` get a "Create account" link.
- Test: live password rules; duplicate email shows an error on the field.

**M6 – Login + OTP**
- Change: `src/screens/LoginScreen.tsx`
  - Email field (not phone).
  - "Remember me" toggle.
  - Working "Forgot password?" link.
  - Uses `bmw_password`, and goes to OTP only when the server asks.
- Change: `src/screens/OtpScreen.tsx`
  - Params become `{challengeId, maskedEmail, resendAfterSeconds}`.
  - Real verify call; auto-submit at 6 digits.
  - Resend countdown comes from the server (30 s), replacing the 45-second constant.
  - "Remember this device" checkbox.
  - Error text for wrong code / tries left / expired.
- Test: the full happy path, the wrong-code path, and the trusted-device skip.

**M7 – Forgot / reset**
- Add: `src/screens/ForgotPasswordScreen.tsx`, `src/screens/ResetPasswordScreen.tsx`. These reuse the OTP input style and `PasswordChecklist`.
- Test: reset, then the old password fails and the new one works, and the trusted device is forgotten.

**M8 – Google / Apple**
- Add: `src/services/social.ts`, plus buttons on Login and SignUp styled as dark outlined buttons like "Set Pickup Port & Garage". The Apple button uses Apple's required native style, on iOS only.
- Test: Android Google sign-in, both a new account and linking to an existing email.

**M9 – Location**
- Add: `src/screens/SetLocationScreen.tsx` (GPS button, popular city chips, search with 300 ms debounce, radius chips 5/10/25/50/100/200, default 50), `src/utils/geo.ts` (haversine, nearest city, label helpers; port our tests), `src/utils/gps.ts` (`expo-location`).
- Add: `src/components/Chip.tsx`, matching the pill style on the Splash screen. Reuse `Badge` if it fits.
- Change: `src/screens/HomeScreen.tsx` gets a location chip (e.g. "📍 Lagos · 50 km") that opens SetLocation.
- Test: GPS allowed and denied, city search, radius saved after an app restart.

**M10 – Roles switch**
- `ENABLE_ROLES=false` hides the role choice. When true, show a Customer / Service Pro choice on SignUp and a picker for social sign-up (`ROLE_REQUIRED`).
- Test: both values of the flag.

**M11 – Tests + clean-up**
- Add `jest-expo` and a `jest` config, and port our `geo.test.ts` and validation tests.
- Update `README.md` with run steps.

---

## 6. Mobile libraries and native config

All installed with `npx expo install <pkg>`, so versions match **Expo SDK 51**.

| Need | Our app used | Shared app should use | Notes |
|---|---|---|---|
| Secure token storage | `react-native-keychain` | **`expo-secure-store`** | Expo-native; Keychain on iOS, Keystore on Android. |
| Device id | AsyncStorage + `Math.random` UUID | **`expo-crypto`** (`randomUUID`) + `expo-secure-store` | Cryptographically strong. |
| Location | `@react-native-community/geolocation` | **`expo-location`** | Has a config plugin for permission text. |
| Google sign-in | `@react-native-google-signin/google-signin` v16 | **same library, at a version that supports Expo SDK 51 / RN 0.74** (v16 may need a newer RN; check the compatibility table) | Has an Expo config plugin. **Does not work in Expo Go**; needs our own dev build, which we already use. |
| Apple sign-in | `@invertase/react-native-apple-authentication` | **`expo-apple-authentication`** | Expo-native, iOS only. |
| Icons / SVG | `lucide-react-native`, `react-native-svg` | **none** | The shared app uses emojis. A Google "G" can be a PNG or text to avoid adding SVG. |
| Phone country list | in-house | in-house (ported) | No new library. |

**Android changes:**
- `app.json`:
  - Add `scheme` (e.g. `okohparts`).
  - Add `plugins` (`expo-secure-store`, `expo-location` with permission text, `@react-native-google-signin/google-signin`, `expo-apple-authentication`).
  - Add `android.permissions` `ACCESS_FINE_LOCATION` and `ACCESS_COARSE_LOCATION`.
- The prebuild step writes these into `android/app/src/main/AndroidManifest.xml`.
- **Google Cloud:** create an Android OAuth client for package `com.okoh.bmwmobile` plus the SHA-1 of the debug and release keystores, and a Web client ID for the server to check the token audience.
- `google-services.json` is **not** needed when only Google Sign-In is used (no Firebase).

**iOS changes (needs a Mac):**
- Run `npx expo prebuild -p ios`.
- `NSLocationWhenInUseUsageDescription` (through the plugin).
- Google iOS client ID and its reversed URL scheme (through the plugin).
- The **Sign in with Apple** capability (`ios.usesAppleSignIn: true` in `app.json`).
- An Apple Developer team. The bundle id `com.okoh.bmwmobile` must be registered.
- App Store rule: if Google sign-in is offered on iOS, Apple sign-in must be offered too.

**Full rebuild needed? Yes.** All of these are native modules, so a JavaScript reload is not enough.
- Android: `npx expo prebuild -p android` (or `--clean`), then `npx expo run:android`.
- ⚠️ The `android/` folder is committed. Check whether the lead has hand-edited anything in it before running `--clean` (see risks).
- The committed `android/app/src/main/assets/index.android.bundle` is a stale bundle and will need regenerating or removing.

---

## 7. Risks, what could break, and questions for the lead

### Risks / things that could break

1. **Usernames.** Auction and Bid store the seller and bidder **username** from the token. Generated usernames for new users must be unique and must never change, or old auctions and bids lose their owner.
2. **Web app (`nextApp`) and `bruno`.** New columns are nullable and the existing clients are untouched, so they should keep working. Re-test the Razor login and register pages after B1 and B6.
3. **Token issuer URL.** In Docker the issuer is `http://localhost:5001` while services check against `http://identity-svc`. This already works for the web app. Use `adb reverse` so the emulator also calls `localhost:5001` and the issuer matches.
4. **Expo `android/` folder.** Running `expo prebuild --clean` can wipe hand edits, such as the signing config in `android/app/build.gradle`. Diff the folder before and after prebuild.
5. **Library versions on SDK 51.** Newer versions of Google sign-in may need a newer RN. Pin the versions `npx expo install` suggests. An Expo SDK upgrade is a separate, bigger task.
6. **Gateway auth middleware.** `GatewayService/Program.cs` never calls `UseAuthentication` / `UseAuthorization`. Before relying on it, verify that protected routes really reject anonymous calls today.
7. **Demo endpoints.** `PartRequests` / `Purchases` have no migration and no auth, so they rely on mock fallbacks. Once the app sends real tokens, behaviour there may change. Don't mix that up with auth bugs.
8. **Email delivery.** Without a real email provider, OTP only works in dev (log or `devOtp`). Production login is impossible until that exists.
9. **Rate limiting.** The token endpoint gets a new rate limit. Make sure it doesn't throttle the web app or `bruno` during testing.
10. **Duende license** for production use.
11. **Secrets already in git (file names only):**
    - Shared repo: `src/IdentityService/Config.cs` (client secrets), `src/IdentityService/SeedData.cs` and `specs/identity-service.md` (seed passwords), `src/IdentityService/keys/is-signing-key-*.json` (token signing keys), `docker-compose.yml` (DB, RabbitMQ and Meilisearch credentials), and the RabbitMQ fallback credentials in each service's `Program.cs`.
    - Shared mobile: `android/app/build.gradle` (keystore credentials).
    - Our repo: `bidmywork-api/src/BidMyWork.Api/appsettings.Development.json` and `bidmywork-api/docker-compose.yml`.

    We will **not** copy any of these. New settings (email password, etc.) go in user-secrets or env vars only.

### Questions to ask the lead before starting

1. Can I create branch `feature/auth-location` from `develop`? (It doesn't exist yet.)
2. **Option A** (extend Duende IdentityService with extension grants): OK? Or do you prefer hosted login pages (Option C)?
3. **Username:** generate one from the email, or add a username field to sign-up? (Bids and auctions use it as the public name.)
4. **Region:** default phone country +234 Nigeria, and a Nigerian (and German?) city list for "Set your location"? Which cities count as "popular"?
5. How does **location / radius** relate to the existing **"Pickup Port"** and **ProfilePreferences** (buyer type, port, garage)? Keep both screens, or merge them?
6. Keep **"Continue as Guest"** browsing?
7. How do **buyer type** (Workshop / Wholesale / Individual) and our **Customer / Service Pro** roles relate?
8. **Email provider** for OTP in staging and production (SMTP, SendGrid, AWS SES …), and who owns the credentials?
9. Who owns the **Google Cloud** project and **Apple Developer** account for `com.okoh.bmwmobile`? Is there a Mac for iOS builds?
10. Use refresh tokens with Duende's **database store** (survives restarts), or in-memory for now?
11. Has anyone hand-edited the Expo `android/` folder? Is it OK to regenerate it with `expo prebuild`?
12. Do we have a Duende license plan for production?
13. Should the Next.js web app also get the new sign-up and OTP flow (would need Razor page work), or is it mobile only for now?

---

## 8. How to test each step

| Step | How to test |
|---|---|
| B1 Migration | `docker compose up -d --build identity-svc`. Check `AspNetUsers`, `OtpCodes` and `TrustedDevices` in Postgres (port 5433). Log in as alice in the web or Razor login and via the `bruno` password grant; it must still work. |
| B2 Helpers | `dotnet test tests/IdentityService.Tests` (GeoMath, CityCatalog, radius clamp ported from our tests). |
| B3–B5 Account API | A `IdentityService.http` (or Bruno) file covering: register OK, duplicate email gives 409, weak password gives 400, forgot-password gives a generic message, the OTP is in the dev log, reset with a wrong code fails, reset with the right code then logging in with the new password works. |
| B4 OTP rules | Unit tests: expires after 10 minutes (fake clock); a 6th try is blocked; resend within 30 s gives 429; an old code is invalid after a resend. |
| B6 Grants | Token request with `bmw_password` gives `otp_required`, then `bmw_otp` gives tokens. Decode the token at jwt.io (local only): it has `username`, `name`, `email` and `iss`=localhost:5001. Then **call the Gateway `POST /auctions` with it; it must succeed (proves the services trust it).** Login again with `device_token`: no OTP. |
| B7 Refresh | A refresh token gives new tokens. After revocation, refresh fails. If using the DB store, it still works after restarting identity-svc. |
| B8 Social | Get a Google ID token from the Android dev build, then `bmw_external`. Test a new user, and an existing email that gets linked (one row in `AspNetUserLogins`, no duplicate user). Test Apple on an iOS device. |
| B9 Roles | Flag off: no role claim, and register without a role works. Flag on: register without a role gives 400; with `pro`, the role claim is `pro`. |
| B10 Location | `PUT /api/profile/location` with radius 500 gets clamped to 200. No radius gives 50. (0,0) gives 400. `GET /api/account/me` shows the location. |
| B11 Gateway | Existing routes still behave the same (anonymous GET /auctions works; POST without a token gives 401). New routes reach identity. |
| M1 | App builds with `npx expo run:android` and looks exactly the same as before. |
| M2–M4 | Sign in, kill the app, reopen: still signed in with "Remember me" on, asked again with it off. Force a 401 (expired token) and check that the refresh happens silently. Logout clears everything. |
| M5 Sign up | Each password rule turns green live. Can't submit without terms. Phone is sent as E.164 (check the server log). |
| M6 Login/OTP | Correct code works. Wrong code shows "N tries left". After 5 wrong codes you are blocked. Resend is disabled for 30 s. "Remember this device" skips OTP on the next login. |
| M7 Reset | Full forgot → code → new password → login. The old password fails. |
| M8 Social | Google on Android: new account and linking to existing. Apple and Google on iOS (Mac build). Cancelling shows no error. |
| M9 Location | First login shows SetLocation with no back button. GPS allowed fills a city; GPS denied shows a friendly message. City search works. Radius chip is saved. Home chip shows "📍 City · 50 km" and editing works. Restart: no onboarding again. |
| M10 Roles | Both flag values. |
| Visual check | For every new or changed screen, compare a screenshot side by side with an existing screen (e.g. `emulator_screen.png`, Login, Home): same background, fonts, button shapes, spacing, emoji icon style. |
| Regression | Browse Home, Market, Part details, place a bid (now with a real token), My Bids, Orders, Tracking: all still work. Run `dotnet test` for all existing backend test projects (Docker needed). |
