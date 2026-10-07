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
|images|`/images/{**catch-all}` (path unchanged)|GET, HEAD|AuctionService (static files)|Anon|
|search|`/search/{**catch-all}` → `api/search/{**catch-all}`|GET|SearchService|Anon|
|bidsWrite|`/bids/{**catch-all}` → `api/bids/{**catch-all}`|POST (place bid, `/bids/accept`)|BidService|Auth|
|bidsRead|`/bids/{**catch-all}` → `api/bids/{**catch-all}`|GET (bids, `/bids/{id}/next-min`)|BidService|Anon|
|notifications|`/notifications/{**catch-all}`|any (WebSocket upgrade)|NotificationService|Anon (CORS-scoped to the frontend origin, configured via `ClientApp`)|

Image uploads (`POST /auctions/{id}/images`, covered by `auctionsWrite`) can be up to 10 × 5 MB, so Kestrel's request body limit is raised to `MaxRequestBodySizeMB` (default 60). nginx-proxy (`local-deploy/proxy-buffers.conf`) allows `client_max_body_size 60m` for the same reason.

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
