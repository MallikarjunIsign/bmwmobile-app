import React, { createContext, useContext, useEffect, useMemo, useRef } from 'react';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { config } from '../config';
import { getValidAccessToken } from '../auth/session';
import { useAuth } from '../auth/AuthContext';

/** Events sent by NotificationService (SignalR hub /notifications behind the Gateway). */
export type LiveEvent = 'BidPlaced' | 'AuctionFinished' | 'AuctionExtended' | 'CustomOrderMatched' | 'Notify' | 'ChatMessage' | 'ChatReceipt';

export type BidPlacedMessage = { id: string; auctionId: string; bidder: string; amount: number; bidStatus: string };
export type CustomOrderMatchedMessage = { orderId: string; user: string; auctionId: string; title: string; distanceKm?: number };

type Handler = (message: any) => void;

type NotificationsValue = {
  /** Subscribe to a live event; returns the unsubscribe function. */
  on: (event: LiveEvent, handler: Handler) => () => void;
};

const NotificationsContext = createContext<NotificationsValue>({ on: () => () => undefined });

const EVENTS: LiveEvent[] = ['BidPlaced', 'AuctionFinished', 'AuctionExtended', 'CustomOrderMatched', 'Notify', 'ChatMessage', 'ChatReceipt'];

/**
 * One SignalR connection while signed in. The access token is sent so personal messages (custom order matched)
 * reach only this user. Reconnects automatically; when the server is down the app just works without live updates.
 */
export function NotificationsProvider({ children }: { children: React.ReactNode }) {
  const { status, user } = useAuth();
  const handlers = useRef(new Map<LiveEvent, Set<Handler>>());
  const signedInAs = status === 'signedIn' ? user?.id ?? null : null;

  useEffect(() => {
    if (!signedInAs) return;
    let stopped = false;
    const connection: HubConnection = new HubConnectionBuilder()
      .withUrl(config.NOTIFICATIONS_URL, { accessTokenFactory: async () => (await getValidAccessToken().catch(() => null)) ?? '' })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.None)
      .build();

    EVENTS.forEach(event =>
      connection.on(event, (message: unknown) => handlers.current.get(event)?.forEach(h => h(message))),
    );

    const start = async (attempt = 0) => {
      if (stopped) return;
      try {
        await connection.start();
      } catch {
        // Server not reachable yet: try again a little later (at most every 30 s).
        setTimeout(() => start(attempt + 1), Math.min(30000, 2000 * (attempt + 1)));
      }
    };
    start();

    return () => {
      stopped = true;
      connection.stop().catch(() => undefined);
    };
  }, [signedInAs]);

  const value = useMemo<NotificationsValue>(
    () => ({
      on: (event, handler) => {
        const set = handlers.current.get(event) ?? new Set<Handler>();
        set.add(handler);
        handlers.current.set(event, set);
        return () => {
          set.delete(handler);
        };
      },
    }),
    [],
  );

  return <NotificationsContext.Provider value={value}>{children}</NotificationsContext.Provider>;
}

/** Calls `handler` for each live `event` while the component is mounted. */
export function useLiveEvent(event: LiveEvent, handler: Handler) {
  const { on } = useContext(NotificationsContext);
  const latest = useRef(handler);
  latest.current = handler;
  useEffect(() => on(event, message => latest.current(message)), [on, event]);
}
