-- "My Bids": a user's bids across auctions.
CREATE INDEX IF NOT EXISTS ix_bids_bidder ON bids (bidder);
