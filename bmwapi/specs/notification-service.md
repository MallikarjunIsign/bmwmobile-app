# NotificationService

## Summary

Thin fan-out bridge from RabbitMQ to the browser. Has no database and no REST API — it consumes domain events
and rebroadcasts them to all connected clients over a SignalR hub so the frontend can show live toasts and
update prices, statuses and countdowns without polling. It never forwards internal data: `AuctionCreated`
is converted to a public payload **without the reserve price**.

## Infrastructure

.Net Web API
SignalR Hub (WebSocket push to browser clients)
Service Bus - RabbitMQ (WolverineFx)
No database

## Nuget Packages

Mapster
WolverineFx.RabbitMQ
WolverineFx.RuntimeCompilation

## External (User)

None — no REST API. Clients connect to the SignalR hub directly.

## Queries handled

None.

## Events emitted

None (rebroadcasts happen over SignalR, not RabbitMQ).

## Events consumed → SignalR messages

| Event | SignalR message | Payload |
|---|---|---|
| **AuctionService.AuctionCreated** | `AuctionCreated` | **AuctionCreatedNotification** (public fields + `title`, no reserve) |
| **AuctionService.ContainerUpdated** | `ContainerUpdated` | event as-is |
| **ChatService.ChatMessageSent** | `ChatMessage` – **to the recipient only** | event as-is (conversation, sender, listing title, preview) |
| **AuctionService.ReviewRequested** | `Notify` (`type: reviewRequest`) – **to the buyer only** | 2 days after completion without a review |
| **ChatService.ChatReceipt** | `ChatReceipt` – **to the sender of the messages only** | `{ conversationId, state: Delivered\|Read, upTo }` |
| **BidService.BidPlaced** | `BidPlaced` | event as-is |
| **BidService.AuctionFinished** | `AuctionFinished` | event as-is (may arrive twice per auction: ReserveNotMet, then Sold/Unsold) |
| **BidService.AuctionExtended** | `AuctionExtended` | event as-is |
| **AuctionService.CustomOrderMatched** | `CustomOrderMatched` | event as-is, **only to the buyer** (`Clients.User(username)`): "A part matching your request is now available" → the app opens the listing |

### Personal notifications – `Notify`

Sent only to the people concerned (`Clients.User(username)`) as `Notify` with
**UserNotification** `{ type, auctionId, title, amount?, detail? }` (the title comes from the `AuctionTitles` cache filled from
`AuctionCreated`):

| Event | `type` | To |
|---|---|---|
| BidPlaced | `outbid` | the previous high bidder |
| BidPlaced | `newBid` | the seller |
| AuctionEndingSoon | `endingSoon` | the seller and every bidder |
| AuctionFinished (Sold) | `won` / `lost` / `sold` | the winner / the other bidders / the seller |
| AuctionFinished (no bids) | `endedNoBids` | the seller |
| CustomOrderMatched | `customOrderMatched` | the buyer |
| OrderStatusChanged | `orderUpdate` (`detail` = the step, e.g. "Dispatched") | the other party of the sale |

The app shows them as toasts on any screen (`NotificationToasts`).

## SignalR Hub

**`/notifications`** - `NotificationHub` (empty `Hub` subclass — server never receives client-invoked methods, it's push-only). Routed through GatewayService with a CORS policy allowing the frontend origin (needed because SignalR negotiation isn't a simple CORS request).

Connections may send the IdentityService access token as `?access_token=...` (validated with JWT Bearer against
`IdentityServiceUrl`); `UsernameUserIdProvider` maps the `username` claim to the SignalR user, so personal messages
(CustomOrderMatched) reach only that user. Anonymous connections still receive the public bid/auction messages.

## API Endpoints

None — only the SignalR hub endpoint above.

## Models

**AuctionCreatedNotification** (what browsers receive as `AuctionCreated`)

|Property Name|Property Type|Notes|
|---|---|---|
|Id|string||
|Title|string|Part: `"{PartName} (fits {Make} {Model})"`, e.g. "Front bumper (fits Toyota Hilux)"; Vehicle: `"{Year} {Make} {Model}"`|
|ItemType|string|Vehicle / Part|
|Seller|string||
|Make, Model|string||
|Year|int||
|PartName?|string||
|StartingPrice|int||
|AuctionEnd|DateTime||
|ImageUrl|string|primary image|

## Event Consumed Types

**AuctionService.AuctionCreated** - the full `AuctionSnapshot` (see auction-service.md); only the fields above are forwarded.

**AuctionService.ContainerUpdated** - `ContainerInfo` fields + `AuctionIds`

**BidService.AuctionFinished** - `ItemSold`, `AuctionId`, `Winner?`, `Seller`, `Amount?`, `Status?`, `EndReason?`, `SellerDecisionDeadline?`, `BidId?`

**BidService.AuctionExtended** - `AuctionId`, `AuctionEnd`

**BidService.BidPlaced** - `Id`, `AuctionId`, `Bidder`, `BidTime`, `Amount`, `BidStatus`
