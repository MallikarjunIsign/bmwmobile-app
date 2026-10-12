# ChatService

## Summary

Buyer–seller chat. A buyer asks the seller about a part or vehicle from the listing ("Chat with seller"); the seller
replies. One conversation per (listing, buyer); the seller is the listing's seller. After the sale the winner / buyer
and the seller keep using the same conversation (also reachable from the order). Only the two participants can read a
conversation – checked on every request (anyone else gets 404). Messages are kept; when a listing is cancelled its
chats stay readable but read-only.

Real-time delivery goes through NotificationService's SignalR hub (`ChatMessage`, `ChatReceipt`); phones in the
background get a push notification (Firebase Cloud Messaging, off until configured).

## Infrastructure

.Net Web API (controllers), port 7005 locally, `/chat/*` behind the Gateway (signed in)
Postgres database `chat` (EF Core, migrations applied at startup)
Service Bus - RabbitMQ (WolverineFx, durable outbox)
Firebase Admin SDK (push, optional)

## Nuget Packages

FirebaseAdmin
Microsoft.AspNetCore.Authentication.JwtBearer
Npgsql.EntityFrameworkCore.PostgreSQL
WolverineFx.EntityFrameworkCore, WolverineFx.Postgresql, WolverineFx.RabbitMQ

## Tables

| Table | Columns |
|---|---|
| Listings | Id (listing id), Seller, SellerName, Title, ImageUrl, StartingPrice, CurrentHighBid, Currency, Status, SaleType, Deleted – the chat's copy for the header |
| ListingBuyers | ListingId, Buyer – who won / bought it (the seller may message them) |
| Conversations | Id, ListingId, Buyer, Seller, CreatedAt, LastMessageAt, LastMessagePreview, LastMessageSender, BuyerUnread, SellerUnread; unique (ListingId, Buyer) |
| Messages | Id, ConversationId, Sender, Text (≤ 1000), Images (jsonb, ≤ 5 × {url, thumbnailUrl}), SentAt, DeliveredAt, ReadAt |
| Blocks | Blocker, Blocked, CreatedAt |
| Reports | Id, ConversationId, Reporter, ReportedUser, Reason (spam / abuse / scam / other), Details, Status, CreatedAt |
| PushDevices | Token (FCM), User, Platform, UpdatedAt |

## External (User)

| Verb | Path | Description | Auth |
|---|---|---|---|
| GET | api/chat/conversations?role=all\|buying\|selling | Inbox: conversations with messages, newest first – listing (photo, title, current price, status), other user, last message, time, unread count, block flags. Marks incoming messages delivered | Auth |
| GET | api/chat/unread | `{ count }` – the badge on the Messages icon | Auth |
| POST | api/chat/conversations | `{ listingId, buyer? }` – open (create) the conversation. Buyers: about any listing that isn't their own. Sellers: only with `buyer` = someone who already wrote or bought the item (403 otherwise; 400 without buyer or with themselves). 403 when the seller blocked the buyer; 400 for a removed listing | Auth |
| GET | api/chat/conversations/:id?before= | The conversation with the newest 50 messages (oldest first), `hasOlder`; `before` = the oldest loaded message's time for older pages. `readOnly`, `showSafetyTip` (until the item is sold), block flags | Participants |
| POST | api/chat/conversations/:id/messages | `{ text?, photos?: [{ url, thumbnailUrl }] }` – text ≤ 1000, ≤ 5 photos uploaded through `POST /media` (AuctionService; other URLs → 400). 403 blocked by the other user, 400 blocked them / listing removed, 429 more than 30 messages a minute. Publishes **ChatMessageSent** and queues the push | Participants |
| POST | api/chat/conversations/:id/read | The caller read the conversation: unread → 0, **ChatReceipt** (Read) to the other user | Participants |
| POST | api/chat/conversations/:id/delivered | The app received a live message: **ChatReceipt** (Delivered) | Participants |
| POST | api/chat/conversations/:id/report | `{ reason: spam\|abuse\|scam\|other, details? }` | Participants |
| GET / POST | api/chat/blocks | Users the caller blocked / `{ username }` block someone they have a chat with. Publishes **UserBlocked** | Auth |
| DELETE | api/chat/blocks/:username | Unblock; publishes **UserUnblocked** | Auth |
| POST / DELETE | api/chat/devices, api/chat/devices/:token | Register this phone's FCM token (`{ token, platform }`) after sign-in / remove it on sign-out | Auth |
| GET | api/chat/admin/reports, POST api/chat/admin/reports/:id/close | Open reports with the last 20 messages / close one | Admin (`Admin:Usernames`) |

Message states for the sender's ticks: `sent` (stored) → `delivered` (the recipient's app got it: inbox / conversation
opened, or the live message acknowledged) → `read` (the recipient opened the conversation).

## Events consumed

| Event | Effect |
|---|---|
| AuctionService.AuctionCreated / AuctionUpdated | Upsert the listing copy (title, photo, price, status, sale type) |
| AuctionService.AuctionDeleted | Listing `Deleted` – its chats become read-only |
| BidService.BidPlaced | Current price (highest accepted bid) |
| BidService.AuctionFinished | Status (Sold / Unsold / …) |
| AuctionService.OrderCreated | Adds the buyer (auction winner or direct-sale buyer) |

Listings not seen yet are fetched once from AuctionService (`GET api/auctions/{id}`).

## Events emitted

| Event | Consumers |
|---|---|
| ChatMessageSent | NotificationService → SignalR `ChatMessage` to the recipient |
| ChatReceipt | NotificationService → SignalR `ChatReceipt` to the sender of the messages |
| UserBlocked / UserUnblocked | BidService (no bids on the blocker's listings), AuctionService (no direct-sale purchases) |
| SendChatPush (local durable queue) | Push "New message from <username>" / "About <listing>: <text>" to the recipient's phones |

## Configuration

| Key | Default | |
|---|---|---|
| ConnectionStrings:DefaultConnection | – | Postgres `chat` |
| IdentityServiceUrl, AuctionServiceUrl, RabbitMQ:Host | | |
| Chat:MaxTextLength / MaxPhotos / MessagesPerMinute / PageSize | 1000 / 5 / 30 / 50 | |
| Chat:PhotoBaseUrl | http://localhost:6001 | The Gateway origin (same as AuctionService's ImageStorage:PublicBaseUrl) |
| Chat:RemotePhotoPrefixes | [] | e.g. the Google Cloud Storage bucket URL when photos are stored there |
| Push:Provider | None | `Firebase` to send pushes |
| Push:Firebase:CredentialsFile | – | Service-account key file (user-secrets / env, never the repo); else GOOGLE_APPLICATION_CREDENTIALS |
| Admin:Usernames | [] | Who can see reports |

## Ratings (Step 3)

Consumes **SellerRatingChanged** into `UserRatings`; the inbox rows and the conversation return the other person's
`otherUserRating` / `otherUserRatingCount`. Consumes **ReviewRequested** and pushes "How was your purchase?" to the
buyer's phones (data `type=review`, `orderId`).
