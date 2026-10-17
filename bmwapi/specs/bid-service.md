# BidService

## Summary

Handles placing bids, tracking each auction's bid history and **ending auctions**. Keeps its own lightweight
read-model copy of an auction (id, end time, seller, starting price, reserve, increment, status) so it can
validate bids without calling AuctionService synchronously on every request. That copy is built from
`AuctionCreated`/`AuctionUpdated` events, with a gRPC call to AuctionService as a fallback if a bid arrives for an
auction it hasn't seen yet. It schedules a check for each auction's end time, decides the outcome (Sold /
ReserveNotMet / Unsold), runs the seller's grace period, and publishes `AuctionFinished`.

## Infrastructure

.Net Web API (Minimal APIs)
Postgres DB (`NpgsqlDataSource`, one pooled connection per operation)
Dapper (raw SQL) + dbup-postgresql (schema migrations)
Service Bus - RabbitMQ (WolverineFx) with the Wolverine Postgres outbox enlisted in the Dapper transactions
gRPC client (fallback auction lookups against AuctionService)

## Nuget Packages

Dapper
dbup-postgresql
Google.Protobuf
Grpc.Net.Client
Grpc.Tools
Mapster
Microsoft.AspNetCore.Authentication.JwtBearer
Npgsql
WolverineFx.Postgresql
WolverineFx.RabbitMQ
WolverineFx.RuntimeCompilation

## Bidding rules (`Services/BidRules`, authoritative)

| Rule | Detail |
|---|---|
| Bid amount | A bid is stored as the **total** (part price + the amount on top), so "highest amount wins" is unchanged |
| Current total | The highest accepted bid, or `StartingPrice` (the part price) before the first bid |
| Bid step | From the tier table in `BidRules.StepTiers` (the **only** place it is defined; the per-auction `BidIncrement` is ignored): below 1,000 → **+10**; 1,000–4,999 → **+20**; 5,000–9,999 → **+50**; 10,000–49,999 → **+100**; 50,000 and above → **+250** (listing currency units) |
| Minimum bid | `CurrentTotal + Step(CurrentTotal)` – e.g. 100 → 110, 1,000 → 1,020, 5,000 → 5,050; part price 300 with no bids → 310 |
| Too low | HTTP 400 `"Your bid is too low. The next valid bid is {amount}"` (e.g. `... is 5,100`). The bid is **not stored** and no event is published, so it can never change the high bid |
| Accepted | `Accepted` if amount ≥ `ReservePrice`, otherwise `AcceptedBelowReserve` |
| Own auction | HTTP 400 `"You can't bid on your own listing"` |
| Ended | Bids are only taken while `Status = Live` and before `AuctionEnd`; otherwise HTTP 400 `"Auction has finished"` (not stored) |
| Outbidding yourself | The current highest bidder can't bid again: HTTP 400 `"You're the highest bidder"` (not stored) |
| Reserve | No reserve in this phase: a listing's reserve = its part price, so any bid at the end sells |
| Concurrency | Placing, accepting and ending run in a transaction that locks the auction row (`SELECT ... FOR UPDATE`), so concurrent bids are validated one after the other against the latest high bid. Events are written to the Wolverine outbox in the same transaction and sent after commit |
| Anti-sniping | If `AntiSniping:Enabled` (default **false**) and an accepted bid arrives within `AntiSniping:WindowMinutes` (2) of the end, `AuctionEnd` moves `AntiSniping:ExtendMinutes` (2) later, a new `CheckAuctionEnded` is scheduled and **AuctionExtended** is published |

## How auctions end

**Seller accepts early** - `POST api/bids/accept` (seller only). Only the current highest accepted bid can be accepted; otherwise HTTP 409 `{ message: "A higher bid just came in", highBid }`. Below-reserve bids may be accepted when `Auction:AllowAcceptBelowReserve` (default **true**). The auction becomes `Sold` immediately (`EndReason = SellerAccepted`); the end check scheduled for the original end time then does nothing.

**Time runs out** - `CheckAuctionEnded` fires at `AuctionEnd`:

| High bid | Result |
|---|---|
| ≥ reserve | `Sold` (`EndReason = TimeEnded`) |
| < reserve | `ReserveNotMet`; `SellerDecisionDeadline = now + Auction:ReserveGracePeriodHours` (default **24**); `CheckSellerDecision` is scheduled for the deadline. During the grace period the seller can accept the high bid with the same endpoint → `Sold` (`EndReason = SellerAcceptedAfterEnd`); no new bids are taken |
| none | `Unsold` (`EndReason = TimeEnded`) |

`CheckSellerDecision` → if still `ReserveNotMet` at the deadline: `Unsold` (`EndReason = GracePeriodExpired`).

Each transition publishes **AuctionFinished**, so one auction can produce two of them (ReserveNotMet → Sold/Unsold). Scheduled checks are never cancelled: a check that finds the auction already ended, or an end time that has moved later (anti-sniping, an edit before the first bid), does nothing.

## External (User)

**PlaceBid** - Places a bid (rules above). If the auction isn't in the local read model, falls back to a gRPC call to AuctionService, caches the result and schedules its end check. Emits **BidPlaced** (and **AuctionExtended** when anti-sniping applies)

**AcceptBid** - The seller accepts the current highest bid (while Live, or during the ReserveNotMet grace period). Emits **AuctionFinished**

## Queries handled

**GetBidsForAuction** - Gets all stored bids for an auction, newest first. Returns list of **BidDto**

**GetNextMinimumBid** - `GET api/bids/{auctionId}/next-minimum` (old route `next-min` still works). Returns **NextMinimumBidDto**
`{ minimumBid, currentTotal, bidStep, partPrice, currentHighBid, bidCount, highBidder, quickBids, bidIncrement, reserveMet, auctionEnd, status }`
so the app can show "Current bid $1,000 + step $20 = Next bid $1,020", pre-fill the bid and offer quick bids
(next, next + 1, 2 and 5 steps). `bidIncrement` = `bidStep` for older clients. Never exposes the reserve itself

**GetBidSteps** - `GET api/bids/steps` → `[{ from, to, step }]`, the tier table (e.g. for the seller's review step)

**GetMyBids** - `GET api/bids/mine` → one row per auction the caller bid on: their highest bid, the current high bid
and bidder, bid count, end time, status and `state` = **Winning** / **Outbid** (Live) or **Won** / **Lost** (ended)
(`BidRules.MyBidState`)

## Events emitted

**BidPlaced** - When a bid is accepted, in response to PlaceBid
**AuctionFinished** - When an auction ends or the seller accepts (see *How auctions end*)
**AuctionExtended** - When anti-sniping moves an auction's end time (exchange `auction-extended`)
**AuctionEndingSoon** - One hour before the end of a Live auction: `{ auctionId, auctionEnd, seller, bidders, currentHighBid }` (exchange `auction-ending-soon`)

`BidPlaced` now also carries `BidCount`, `Seller` and `PreviousHighBidder` (who was just outbid); `AuctionFinished`
carries `Bidders` (everyone who bid), so NotificationService can tell each person what happened.

## Events consumed

**AuctionService.AuctionCreated** - Creates the local `Auction` read-model row (ignored if it already exists) and schedules `CheckAuctionEnded` at `AuctionEnd` (or `CheckSellerDecision` for an auction that is already `ReserveNotMet`)
**AuctionService.AuctionUpdated** - Applies `StartingPrice`, `ReservePrice`, `BidIncrement` and `AuctionEnd` (rescheduling the end check) — only while the auction is Live and has no accepted bids. Creates the row if it doesn't exist yet
**AuctionService.AuctionDeleted** - Deletes the auction and its bids from the read model

The `bid-auction-*` queues are also declared by AuctionService, so events published before BidService has ever started are kept.

## Internal scheduled messages

**CheckAuctionEnded(AuctionId)** - Wolverine durable scheduled message at the auction's end time. See *How auctions end*.
**CheckSellerDecision(AuctionId)** - Wolverine durable scheduled message at `SellerDecisionDeadline`. Moves a still-`ReserveNotMet` auction to `Unsold`.
**CheckAuctionEndingSoon(AuctionId, AuctionEnd)** - scheduled one hour before the end whenever an end check is scheduled; publishes **AuctionEndingSoon** if the auction is still Live with that end time.

## gRPC (client)

Calls **AuctionService.GrpcAuction/GetAuction(auctionId)** when a bid (or next-min request) arrives for an auction not present in the local read model. Uses `id`, `auctionEnd`, `seller`, `reservePrice`, `startingPrice`, `bidIncrement`, `status`, `sellerDecisionDeadline` (falls back to `finished` and safe defaults for older servers). The result is persisted locally and its checks are scheduled. Returns `null` on `NOT_FOUND` (→ 404).

Calls **AuctionService.GrpcAuction/GetBidderStanding(bidder)** on every bid (after the seller-block check). A non-empty
`blockedReason` (the bidder has 2 unpaid auction wins in 12 months – payment Step 2) rejects the bid with 400 and that
message ("You can't bid right now: …"). If AuctionService can't be reached the bid is allowed (fails open, logged).

## API Endpoints

|POST|api/bids|Place a bid (`auctionId`, `amount` as query params)|Auth|
|---|---|---|---|
|POST|api/bids/accept|Accept the highest bid: body `{ auctionId, bidId }` → 200 `BidDto`, 400, 403 (not the seller), 404, 409 `{ message, highBid }`|Auth (seller)|
|GET|api/bids/mine|My Bids with Winning / Outbid / Won / Lost|Auth|
|GET|api/bids/steps|Bid step tiers|Anon|
|GET|api/bids/:auctionId|Get bids for an auction, newest first|Anon|
|GET|api/bids/:auctionId/next-minimum|Next minimum bid (old route `next-min`)|Anon|

Auth is JWT Bearer, validated against IdentityService (`Authority` = IdentityServiceUrl), with `NameClaimType` mapped to the `username` claim. Errors (400/404) are returned as a JSON string message.

## Configuration

| Key | Default | Meaning |
|---|---|---|
| `AntiSniping:Enabled` | `false` | Extend auctions on late bids |
| `AntiSniping:WindowMinutes` | `2` | "Late" = within this many minutes of the end |
| `AntiSniping:ExtendMinutes` | `2` | Minutes added to the end time |
| `Auction:AllowAcceptBelowReserve` | `true` | Seller may accept a high bid below the reserve |
| `Auction:ReserveGracePeriodHours` | `24` | Seller's time to accept after ending below the reserve (fractions allowed) |

## Models

**Bid.cs**

| Property Name | Property Type | Default Value |
| -------------- | -------------- | -------------- |
| Id             | string (Guid string) | Guid.NewGuid().ToString() |
| AuctionId      | string          |                |
| Bidder         | string          |                |
| BidTime        | DateTime        | DateTime.UtcNow |
| Amount         | int             |                |
| BidStatus      | BidStatus       |                |

**Auction.cs** (local read model, table `auctions`)

| Property Name | Property Type | Default Value |
| -------------- | -------------- | -------------- |
| Id             | string          |                |
| AuctionEnd     | DateTime        |                |
| Seller         | string          |                |
| StartingPrice  | int             | 1              |
| ReservePrice   | int             |                |
| BidIncrement   | int             | 1              |
| Status         | AuctionStatus (`Live` 0, `Sold` 1, `ReserveNotMet` 2, `Unsold` 3) | Live |
| DecisionDeadline? | DateTime (while ReserveNotMet) |   |
| Finished       | bool (kept for compatibility: `Status != Live`) | false |

dbup script `0003_PricingAndEndRules.sql`: adds `startingPrice`, `bidIncrement`, `status`, `decisionDeadline`; sets `reservePrice = GREATEST(reservePrice, startingPrice)`; existing finished auctions become `Sold` (had an `Accepted` bid) or `Unsold`; adds index `(auctionId, amount DESC)`.

**BidStatus.cs (enum)**

|Accepted|— the bid was accepted and meets the reserve|
|---|---|
|AcceptedBelowReserve|— the bid was accepted, but is below the reserve|
|TooLow|— historical only: too-low bids are no longer stored|
|Finished|— historical only: bids on finished auctions are no longer stored|

## DTOs

**BidDto.cs**

|Property Name|Property Type|Default Value|
|---|---|---|
|Id|string||
|AuctionId|string||
|Bidder|string||
|BidTime|DateTime||
|Amount|int||
|BidStatus|string||

**AcceptBidDto.cs** - `AuctionId`, `BidId`

**NextMinimumBidDto.cs** - `MinimumBid` (int), `CurrentHighBid?` (int), `BidIncrement` (int), `ReserveMet` (bool), `AuctionEnd` (DateTime), `Status` (string)

## Event Emitted Types

**BidPlaced**

|Property Name|Property Type|Default Value|
|---|---|---|
|Id|string||
|AuctionId|string||
|Bidder|string||
|BidTime|DateTime||
|Amount|int||
|BidStatus|string||

**AuctionFinished**

|Property Name|Property Type|Notes|
|---|---|---|
|ItemSold|bool|true only when Status = Sold|
|AuctionId|string||
|Winner?|string|set when Sold|
|Seller|string||
|Amount?|int|winning bid, or the highest bid for ReserveNotMet/Unsold|
|Status|string|Sold / ReserveNotMet / Unsold|
|EndReason|string|TimeEnded / SellerAccepted / SellerAcceptedAfterEnd / GracePeriodExpired|
|SellerDecisionDeadline?|DateTime|when Status = ReserveNotMet|
|BidId?|string|the winning / highest bid|

**AuctionExtended**

|Property Name|Property Type|Default Value|
|---|---|---|
|AuctionId|string||
|AuctionEnd|DateTime|new end time|

## Event Consumed Types

**AuctionService.AuctionCreated** / **AuctionService.AuctionUpdated** - the full `AuctionSnapshot` (see auction-service.md). Only `Id`, `AuctionEnd`, `Seller`, `StartingPrice`, `ReservePrice`, `BidIncrement`, `Status` and `SellerDecisionDeadline` are used.

**AuctionService.AuctionDeleted** - `Id`

### Blocks (from ChatService)

Consumes **UserBlocked / UserUnblocked** into the `userblocks` table (script 0005). A bid from a user the seller blocked is rejected with 400 "You can't bid on this seller's listings".
