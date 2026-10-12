-- Blocks from ChatService (UserBlocked / UserUnblocked): a blocked user can't bid on the blocker's listings.
CREATE TABLE IF NOT EXISTS userblocks (
    blocker text NOT NULL,
    blocked text NOT NULL,
    createdat timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (blocker, blocked)
);
