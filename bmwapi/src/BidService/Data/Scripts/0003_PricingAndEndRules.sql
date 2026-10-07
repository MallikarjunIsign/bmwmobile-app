-- Pricing rules and end-of-auction states for the local auction read model.
ALTER TABLE auctions ADD COLUMN IF NOT EXISTS startingPrice integer NOT NULL DEFAULT 1;
ALTER TABLE auctions ADD COLUMN IF NOT EXISTS bidIncrement integer NOT NULL DEFAULT 1;
-- 0 = Live, 1 = Sold, 2 = ReserveNotMet, 3 = Unsold (same values as AuctionService's Status)
ALTER TABLE auctions ADD COLUMN IF NOT EXISTS status integer NOT NULL DEFAULT 0;
ALTER TABLE auctions ADD COLUMN IF NOT EXISTS decisionDeadline timestamptz NULL;

-- "No reserve" (0) becomes reserve = starting price.
UPDATE auctions SET reservePrice = GREATEST(reservePrice, startingPrice);

-- Auctions that already finished: Sold if they had an Accepted (>= reserve) bid, otherwise Unsold.
UPDATE auctions a
SET status = CASE
    WHEN EXISTS (SELECT 1 FROM bids b WHERE b.auctionId = a.id AND b.bidStatus = 0) THEN 1
    ELSE 3
END
WHERE a.finished;

-- Highest-bid lookups per auction.
CREATE INDEX IF NOT EXISTS ix_bids_auctionId_amount ON bids (auctionId, amount DESC);