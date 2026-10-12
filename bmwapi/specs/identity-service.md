# IdentityService

## Summary

OIDC/OAuth2 identity provider for the whole system, built on Duende IdentityServer over ASP.NET
Core Identity. Issues the JWTs that every other backend service validates, and is the only service
with its own login/consent/logout UI (Razor Pages). Seeds two demo users (`alice`, `bob`) on first
run. No REST API and no RabbitMQ involvement — it only speaks the OIDC protocol plus Razor Pages UI.

## Infrastructure

.Net Web API + Razor Pages
Duende IdentityServer (OIDC/OAuth2 provider)
ASP.NET Core Identity (user store)
Postgres DB
Entity Framework ORM
Serilog (console + diagnostic file logging)
No RabbitMQ

## Nuget Packages

Duende.IdentityServer.AspNetIdentity
Microsoft.AspNetCore.Diagnostics.EntityFrameworkCore
Microsoft.AspNetCore.Identity.EntityFrameworkCore
Microsoft.AspNetCore.Identity.UI
Microsoft.EntityFrameworkCore.Tools
Npgsql.EntityFrameworkCore.PostgreSQL
Serilog.AspNetCore

## External (User)

Not a REST API — "external" surface is the OIDC protocol plus Identity's Razor Pages UI (login, logout, consent, register, external login, device flow, CIBA, grants, server-side sessions, diagnostics).

## Queries handled

None (not a data API — see OIDC endpoints below).

## Events emitted

None (no RabbitMQ integration).

## Events consumed

None.

## OIDC configuration

**Identity resources**: `openid`, `profile`, `email`
**API scopes**: `auctionApp` — "Auction app full access"

**Clients**:

|ClientId|Grant Type|Notes|
|---|---|---|
|bruno|Resource Owner Password|For direct API testing via the Bruno client; scopes: openid, profile, email, auctionApp|
|nextApp|Authorization Code + PKCE|The Next.js frontend; redirect URI = `{ClientAppUrl}/api/auth/oauth2/callback/duende`; scopes: openid, profile, email, auctionApp; access token lifetime 30 days|
|mobileApp|`bmw_password`, `bmw_otp`, `bmw_external` (+ refresh tokens)|The React Native app. Public client (no secret). Scopes: openid, profile, auctionApp, IdentityServerApi, offline_access. Access token 1 hour; one-time refresh tokens, sliding 30 days (absolute 90). See "Mobile app sign-in" below.|

**Custom claims** (`CustomProfileService`): every issued token gets a `username` claim (the ASP.NET Identity username), `email`, `email_verified`, and — if set — a `name` claim. Downstream services (AuctionService, BidService, GatewayService) all set `TokenValidationParameters.NameClaimType = "username"`, so `User.Identity.Name` resolves to this claim, not the default `sub`/`name`.

**Seed data**: on first run (only if no users exist), creates `alice` (AliceSmith@example.com) and `bob` (BobSmith@example.com), both password `Pass123$`, with a `name` claim of "Alice Smith" / "Bob Smith".

## User locations

Saved locations also hold an optional address (migration `UserAddresses`): `addressLine` (house / flat, building,
street), `area`, `landmark`, `postalCode` – accepted on add / edit and returned in every location. The app shows the
chosen location under the user's name on Home ("My locations"); a chosen saved address replaces GPS until the user
picks "Use my current location" again, and is offered as the ship-to address on orders.

Like Home / Work in food-delivery apps. Each user has up to 10 locations (`UserLocations` table): `Label` (Home / Work / Other / custom), `PlaceName`, `Latitude`, `Longitude`, `RadiusKm` (5–200, default 50), `CountryCode`, `IsPrimary`, `IsCurrentLocation`. `AspNetUsers.AutoUpdatePrimaryLocation` (default true) is the "Auto-update primary from my current location" switch.

**Rules**
- Exactly one primary location (a filtered unique index). The first saved location becomes primary.
- Auto-update on: the primary is the "Current location" row, moved to the phone's GPS when the app opens and the phone moved more than 1 km.
- Choosing a primary by hand (`/primary`, or adding with `makePrimary`) turns auto-update off and removes the GPS row, so GPS never overwrites the choice. "Use my current location" (`/current` with `explicit=true`) turns it back on.
- Saved places never change by themselves. The primary can't be deleted.
- GPS points are named after the nearest catalog city: the city within 40 km, "Near {city}" within 150 km, otherwise "Current location" (exact coordinates are kept).

**City catalog** (built in, no maps API): India (Hyderabad, Bangalore, Delhi, Mumbai, Chennai, Pune popular + 14 more), USA (10 cities), Nigeria (9 cities). Distances use the shared `Contracts.GeoMath` (Haversine + bounding box).

|Method|Path|Notes|
|---|---|---|
|GET|`/api/locations/popular-cities?country=IN\|US\|NG`|6 popular cities (anonymous)|
|GET|`/api/locations/cities?q=&country=`|Search all three countries, given country first (anonymous)|
|GET|`/api/locations/nearest-city?lat=&lon=`|Name for a GPS point (anonymous)|
|GET|`/api/me/locations`|`{ autoUpdatePrimary, locations[] }`, primary first|
|POST|`/api/me/locations`|Add `{ label, placeName, latitude, longitude, radiusKm, countryCode, makePrimary }`|
|PUT|`/api/me/locations/{id}`|Edit (GPS row: radius only)|
|DELETE|`/api/me/locations/{id}`|Not the primary (409 `PRIMARY_CANNOT_BE_DELETED`)|
|POST|`/api/me/locations/{id}/primary`|Manual primary, auto-update off|
|PUT|`/api/me/locations/current`|`{ latitude, longitude, radiusKm?, explicit }` — phone GPS → primary|
|PUT|`/api/me/locations/settings`|`{ autoUpdatePrimary }`|

The `/api/me/*` endpoints need an access token with the `IdentityServerApi` scope. Every change returns the full updated list. `UserDto.hasLocation` tells the app whether to show "Set your location".

## API Endpoints

The mobile account endpoints under `/api/account` (see "Mobile app sign-in") and the location endpoints (see "User locations"). Otherwise exposes IdentityServer's standard OIDC protocol endpoints (`/connect/authorize`, `/connect/token`, `/connect/userinfo`, `/connect/endsession`, discovery document, etc.) plus authenticated Razor Pages for account/consent/device/CIBA flows.

## Models

**ApplicationUser.cs**

| Property Name | Property Type | Default Value |
| -------------- | -------------- | -------------- |
| (all standard `IdentityUser` fields — Id, UserName, Email, PhoneNumber, etc.) | | |
| FullName | string? (100) | null (older accounts keep the name in the `name` claim) |
| TermsAcceptedAt | DateTime? | null |
| CreatedAt | DateTime? | null |
| LastLoginAt | DateTime? | null |

**OtpCode.cs** — 6-digit email codes: `UserId`, `Purpose` (Login / ResetPassword), `CodeHash` (SHA-256, never the code), `Attempts`, `CreatedAt`, `ExpiresAt`, `UsedAt`.

**TrustedDevice.cs** — "Remember this device": `UserId`, `DeviceId`, `TokenHash` (SHA-256 of a server-issued random token), `CreatedAt`, `ExpiresAt` (30 days), `LastUsedAt`.

Refresh tokens are stored by IdentityServer's operational store (`PersistedGrantDbContext`, table `PersistedGrants`).

## Mobile app sign-in

The app has its own native screens; tokens still come from IdentityServer, so the gateway and every service trust them unchanged.

**Username**: first word of the full name, lowercase letters and numbers only (`mallikarjun`, then `mallikarjun2`, ...). People log in with their **email**; the username is the public name on auctions and bids.

**Token endpoint grants** (`POST /connect/token`, `client_id=mobileApp`):

|grant_type|Parameters|Result|
|---|---|---|
|`bmw_password`|`username` (email), `password`, `device_id`, `device_token`|Trusted device → tokens. Otherwise a code is emailed and the answer is `400 invalid_grant` with `code=OTP_REQUIRED`, `challengeId`, `maskedEmail`, `resendAfterSeconds` (and `devOtp` in Development).|
|`bmw_otp`|`challenge_id`, `otp`, `device_id`, `remember_device`|Tokens + `user`; with `remember_device=true` also `deviceToken` (skip the code for 30 days).|
|`bmw_external`|`provider` (google/apple), `id_token`, `nonce` (Apple), `full_name`, `role`|Google / Apple sign-in. Links to an existing account with the same verified email, otherwise creates one.|
|`refresh_token`|`refresh_token`|New tokens ("Remember me").|

Errors keep the OAuth shape plus a `code` (e.g. `INVALID_CREDENTIALS`, `OTP_INVALID`, `OTP_EXPIRED`, `OTP_TOO_MANY_ATTEMPTS`, `LOCKED_OUT`, `USE_SOCIAL_LOGIN`, `SOCIAL_TOKEN_INVALID`, `ROLE_REQUIRED`).

**Rules**: code expires after 10 minutes, 5 wrong tries, resend after 30 seconds, only the newest code works. Wrong passwords use ASP.NET Identity lockout. A password reset forgets all trusted devices and revokes all refresh tokens.

**Account API** (`/api/account`, JSON, 20 requests/minute/IP):

|Method|Path|Notes|
|---|---|---|
|POST|`/register`|`fullName`, `email`, `phoneNumber` (E.164), `password`, `acceptTerms`, `role`* → 201 user|
|POST|`/otp/resend`|`challengeId`|
|POST|`/forgot-password`|`email` — same answer whether or not the account exists|
|POST|`/reset-password`|`email`, `otp`, `newPassword`|
|GET|`/me`|Bearer token with scope `IdentityServerApi`|

\* `role` (`customer`/`pro`) only when `Features:RolesEnabled` is true; then a `role` claim is also issued. Off by default: everyone is one plain user.

**Settings**: `Otp`, `TrustedDevice`, `Features:RolesEnabled`, `SocialAuth:GoogleClientIds`, `SocialAuth:AppleAudiences`, `Email:*`. Gmail credentials come only from user-secrets (`dotnet user-secrets set "Email:Username" ...` / `"Email:Password"`) or the env vars `Email__Username` / `Email__Password` (docker-compose reads them from a local `.env`, see `.env.example`). Without them the code is written to the log. Try it with `src/IdentityService/IdentityService.http`.

## DTOs

None (page-bound `InputModel`/`ViewModel` types per Razor Page, not a public API contract).

## Event Emitted Types

None.

## Event Consumed Types

None.

### Public profile

`GET api/users/:username/public` (anonymous) → `{ username, displayName, memberSince, city, countryCode }` for the seller
profile. The city comes from the primary location's place name (never an address; GPS rows like "Near Lagos" are left out).
