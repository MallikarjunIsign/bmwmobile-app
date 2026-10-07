# Carsties — Microservices Udemy Course Repository (2026 version)

This is the repo for the remastered version of the Udemy course, released September 2026.

- Previous (2025) version: [carsties-2025](https://github.com/TryCatchLearn/carsties-2025)
- 2024 version: [carsties-2024](https://github.com/TryCatchLearn/carsties-2024)
- 2023 version: [carsties-2023](https://github.com/TryCatchLearn/Carsties)

You can see how this app was made by taking the [Udemy course](https://www.udemy.com/course/build-a-microservices-app-with-dotnet-and-nextjs-from-scratch/?couponCode=NEWCOURSEPROM).

## What changed (parts, containers, image upload, bidding rules)

Branch `feature/parts-containers`. Full details per service are in `specs/*.md`.

- **Vehicles and parts.** `Item.ItemType` is `Vehicle` or `Part`. Parts have `PartName`, `PartNumber`, `PartCategory`, `Condition`, `Quantity` and a "fits" range (`Make`/`Model`/`Year`..`YearTo`). Validation depends on the type.
- **Containers.** New `Containers` table and `/api/containers` CRUD (create: any logged-in user, policy `CanManageContainers`; edit/delete: creator only). Items can link a container. Changes publish `ContainerUpdated`, which SearchService applies to the linked auctions.
- **Image upload.** New `ItemImages` table; `ImageUrl` on DTOs/events is now the primary image and `Images` lists them all. Upload/delete/reorder at `/api/auctions/{id}/images` (seller only, 10 × 5 MB, jpg/png/webp checked by file signature). Files are stored by `LocalDiskImageStorage` behind `IImageStorage` and served at `/images/{file}` through the Gateway. In Docker they live on the `auction_uploads` volume.
- **Pricing.** `StartingPrice`, `ReservePrice` (hidden: only the seller sees it; everyone else gets `ReserveMet`) and `BidIncrement`. None of them can change after the first bid.
- **Bidding (BidService).** Minimum bid = starting price, then high bid + increment; too-low bids get 400 `"Minimum bid is 5,500"` and are not stored. Bids are serialized per auction with a row lock, and events go through the Wolverine outbox in the same transaction. New endpoints: `GET /api/bids/{id}/next-min` and `POST /api/bids/accept` (seller accepts the highest bid; 409 if outbid). Optional anti-sniping (`AntiSniping:Enabled`, off by default).
- **Ending.** Statuses are `Live`, `Sold`, `ReserveNotMet` and `Unsold` (old `Finished` = `Sold`). Below the reserve, the seller gets `Auction:ReserveGracePeriodHours` (24) to accept, otherwise the auction becomes `Unsold`. `AuctionFinished` carries `Status`/`EndReason` and can arrive twice; all consumers apply it idempotently.
- **Search.** New index `auctions` (the old `items` index is deleted on startup and rebuilt from AuctionService, because it held the reserve). New filters: `itemType`, `partCategory`, `condition`, `destination`.
- **Notifications.** `AuctionCreated` is forwarded as a public payload with a `title` (no reserve). `AuctionExtended` and `ContainerUpdated` are forwarded too.
- **Data.** EF migration `PartsContainersImagesPricing` and dbup script `0003_PricingAndEndRules.sql` upgrade existing data: items become vehicles, image URLs become `ItemImages`, prices default to 1 (so existing bidding behaves as before), and old `ReserveNotMet` auctions become `Sold`/`Unsold`. A fresh database is seeded with 10 vehicles, 6 parts and 2 containers.
- **Dev setup.** Host connection strings use Postgres on `localhost:5433`. `dotnet-ef` is a local tool (`dotnet tool restore`). New test project `tests/BidService.Tests`. Tests need Docker (Testcontainers).
- **New settings:** `ImageStorage:*`, `ImageUpload:*` (AuctionService), `AntiSniping:*`, `Auction:*` (BidService), `MaxRequestBodySizeMB` (Gateway). Defaults are in each service's `appsettings.json`.

Seed images for the parts come from Wikimedia Commons: [Broken car front bumper](https://commons.wikimedia.org/wiki/File:Broken_car_front_bumper.jpg) (CC BY-SA 4.0), [Car alternator on a workbench](https://commons.wikimedia.org/wiki/File:Car_alternator_on_a_workbench_surrounded_by_various_auto_parts_in_a_garage_setting.jpg) (CC BY 2.0), [Ventilated floating brake rotor](https://commons.wikimedia.org/wiki/File:Ventilated_2-Piece_Floating_Disc_Curved_Drilled_%26_Slotted_Brake_Rotor.jpg) (CC BY 4.0), [Car headlamp](https://commons.wikimedia.org/wiki/File:Car_headlamp.png) (CC BY-SA 4.0), [Wheel, IAA 2017](https://commons.wikimedia.org/wiki/File:Wheel,_IAA_2017,_Frankfurt_(1Y7A3040).jpg) (CC BY-SA 4.0), [Manual transmission in 2nd gear](https://commons.wikimedia.org/wiki/File:Manual_transmission_in_2nd-gear.jpg) (public domain).

## Running the app locally

1. Clone the repo onto your machine, in a user folder:

    ```
    git clone https://github.com/TryCatchLearn/carsties.git
    ```

2. Change into the project directory:

    ```
    cd carsties
    ```

3. Ensure Docker Desktop is installed. If not, download it and follow the install instructions for your OS [here](https://docs.docker.com/desktop/).

4. Build the services locally (this may take several minutes):

    ```
    docker compose build
    ```

5. Once the build completes, start the services:

    ```
    docker compose up -d
    ```

6. To run the app over HTTPS, install `mkcert` from [here](https://github.com/FiloSottile/mkcert). Then install the local Certificate Authority:

    ```
    mkcert -install
    ```

7. Generate a certificate and key to replace the ones used in the course. Change into the `devcerts` directory and run:

    ```
    cd devcerts
    mkcert -key-file carsties.local.key -cert-file carsties.local.crt app.carsties.test api.carsties.test id.carsties.test
    ```

8. Add an entry to your hosts file so you can reach the app by domain name (see [this guide](https://www.hostinger.com/tutorials/how-to-edit-hosts-file) if needed):

    ```
    127.0.0.1 id.carsties.test app.carsties.test api.carsties.test
    ```

9. You should now be able to browse to the app at https://app.carsties.test