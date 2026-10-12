import { useCallback, useEffect, useState } from 'react';
import { ApiService } from '../services/api';
import { useLiveEvent } from '../services/notifications';
import type { NextBid } from '../types/models';

/**
 * The next valid bid for an auction (from BidService), refreshed live whenever someone bids on it
 * or it ends. `next` is null while loading or when the server can't be reached.
 */
export function useNextBid(auctionId: string) {
  const [next, setNext] = useState<NextBid | null>(null);
  const [loading, setLoading] = useState(true);

  const reload = useCallback(async () => {
    const result = await ApiService.getNextBid(auctionId);
    setNext(result);
    setLoading(false);
    return result;
  }, [auctionId]);

  useEffect(() => {
    setLoading(true);
    reload();
  }, [reload]);

  const onAuctionEvent = useCallback(
    (message: { auctionId?: string; AuctionId?: string }) => {
      if ((message?.auctionId ?? message?.AuctionId) === auctionId) reload();
    },
    [auctionId, reload],
  );
  useLiveEvent('BidPlaced', onAuctionEvent);
  useLiveEvent('AuctionFinished', onAuctionEvent);
  useLiveEvent('AuctionExtended', onAuctionEvent);

  return { next, loading, reload };
}
