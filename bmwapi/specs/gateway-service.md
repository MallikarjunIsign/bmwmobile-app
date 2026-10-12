# GatewayService

## Summary

Single public entry point for the frontend. A YARP reverse proxy that fronts AuctionService (auctions,
containers and uploaded images), SearchService, BidService and NotificationService behind one origin, applying JWT auth per-route
(only the write routes require a token) and a CORS policy scoped to the frontend origin for the
SignalR route. Holds no data and no business logic of its own.

## Infrastructure

.Net Web API
YARP reverse proxy
JWT Bearer auth (validated against IdentityService, enforced per-route)
CORS (frontend origin only, for the `/notifications` route)
No database

## Nuget Packages

Microsoft.AspNetCore.Authentication.JwtBearer
Yarp.ReverseProxy

## External (User)

None directly — all "external" operations are proxied through to the downstream services listed below.

## Queries handled

None directly — proxied.

## Events emitted

None.

## Events consumed

None.

## Routes (YARP)

|Route|Path|Methods|Downstream|Auth|
|---|---|---|---|---|
|auctionsRead|`/auctions/{**catch-all}` → `api/auctions/{**catch-all}`|GET|AuctionService|Anon|
|auctionsWrite|`/auctions/{**catch-all}` → `api/auctions/{**catch-all}`|POST, PUT, DELETE|AuctionService|Auth|
|containersRead|`/containers/{**catch-all}` → `api/containers/{**catch-all}`|GET|AuctionService|Anon|
|containersWrite|`/containers/{**catch-all}` → `api/containers/{**catch-all}`|POST, PUT, DELETE|AuctionService|Auth|
|partrequests|`/partrequests/{**catch-all}` → `api/partrequests/{**catch-all}`|any|AuctionService (custom orders)|Auth enforced by AuctionService|
|purchases|`/purchases/{**catch-all}` → `api/purchases/{**catch-all}`|any|AuctionService|Auth enforced by AuctionService|
|listings|`/listings/{**catch-all}` → `api/listings/{**catch-all}`|any|AuctionService (sell flow, My Listings)|Auth|
|catalogRead|`/catalog/{**catch-all}` → `api/catalog/{**catch-all}`|GET|AuctionService (master data, search dictionary)|Anon|
|catalogWrite|`/catalog/{**catch-all}` → `api/catalog/{**catch-all}`|POST, PUT, DELETE|AuctionService (request / approve makes & models)|Auth|
|orders|`/orders/{**catch-all}` → `api/orders/{**catch-all}`|any|AuctionService (sold → handed over → completed)|Auth|
|garage|`/garage/{**catch-all}` → `api/garage/{**catch-all}`|any|AuctionService (My Garage)|Auth|
|geo|`/geo/{**catch-all}` → `api/geo/{**catch-all}`|GET (`/geo/reverse?lat&lon`)|AuctionService (reverse geocoding)|Auth|
|images|`/images/{**catch-all}` (path unchanged)|GET, HEAD|AuctionService (static files)|Anon|
|search|`/search/{**catch-all}` → `api/search/{**catch-all}`|GET|SearchService|Anon|
|bidsWrite|`/bids/{**catch-all}` → `api/bids/{**catch-all}`|POST (place bid, `/bids/accept`)|BidService|Auth|
|bidsRead|`/bids/{**catch-all}` → `api/bids/{**catch-all}`|GET (bids, `/bids/{id}/next-minimum`, `/bids/steps`, `/bids/mine` – auth checked by BidService)|BidService|Anon|
|notifications|`/notifications/{**catch-all}`|any (WebSocket upgrade)|NotificationService|Anon (CORS-scoped to the frontend origin, configured via `ClientApp`); the app adds `?access_token=` so personal messages reach only that user|

Image uploads (`POST /auctions/{id}/images`, covered by `auctionsWrite`) can be up to 20 × 5 MB per item (the app resizes to 1600 px / JPEG 80 % first, so a batch is normally a few MB), so Kestrel's request body limit is raised to `MaxRequestBodySizeMB` (default 60). nginx-proxy (`local-deploy/proxy-buffers.conf`) allows `client_max_body_size 60m` for the same reason.

Clusters (destinations) are environment-specific — configured per-environment (`appsettings.Development.json` points at `localhost:7001/7002/7003/7004`; Docker Compose points at the container hostnames).

## API Endpoints

None of its own — see Routes table above; this service *is* the API surface the frontend talks to.

## Models

None.

## DTOs

None.

## Event Emitted Types

None.

## Event Consumed Types

None.

### Chat, media and shipping addresses

| Route | Cluster | Target | Auth |
|---|---|---|---|
| /chat/{**} | chat (ChatService, :7005) | api/chat/{**} | signed in |
| /media/{**} (POST) | auctions | api/media/{**} | signed in |
| /shipping-addresses/{**} | auctions | api/shipping-addresses/{**} | signed in |

### Reviews and seller profiles

| Route | Cluster | Target | Auth |
|---|---|---|---|
| /reviews/{**} (GET) | auctions | api/reviews/{**} | – |
| /reviews/{**} (POST, PUT, DELETE) | auctions | api/reviews/{**} | signed in |
| /sellers/{**} (GET) | auctions | api/sellers/{**} | – |
