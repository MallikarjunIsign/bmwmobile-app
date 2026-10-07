import React, { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { AppState } from 'react-native';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { useAuth } from '../auth/AuthContext';
import { LocationApi } from '../services/locationApi';
import type { NearFilter } from '../services/api';
import type { MyLocations, SaveLocationRequest, UserLocation } from '../types/location';
import { haversineKm, MOVE_THRESHOLD_KM } from '../utils/geo';
import { getCurrentPosition } from '../utils/gps';

type LocationStatus = 'idle' | 'loading' | 'ready' | 'error';

type LocationContextValue = {
  /** 'ready' once the signed-in user's locations are loaded (guests stay 'idle'). */
  status: LocationStatus;
  autoUpdatePrimary: boolean;
  /** Primary first. */
  locations: UserLocation[];
  primary: UserLocation | null;
  /** The location the parts list is filtered by (primary unless the user picked another). */
  selected: UserLocation | null;
  /** Filter for the parts lists: within the selected location's radius (undefined for guests). */
  near: NearFilter | undefined;
  select: (id: string) => void;
  reload: () => Promise<void>;
  add: (body: SaveLocationRequest) => Promise<void>;
  update: (id: string, body: SaveLocationRequest) => Promise<void>;
  remove: (id: string) => Promise<void>;
  setPrimary: (id: string) => Promise<void>;
  /** "Use my current location": asks for permission, then makes GPS the primary (auto-update on). */
  useCurrentLocation: (radiusKm?: number) => Promise<void>;
  setAutoUpdate: (on: boolean) => Promise<void>;
};

const LocationContext = createContext<LocationContextValue | undefined>(undefined);

const selectedKey = (userId: string) => `@okoh/selected-location/${userId}`;

async function readSelected(userId: string): Promise<string | null> {
  try {
    return await AsyncStorage.getItem(selectedKey(userId));
  } catch {
    return null;
  }
}

export function LocationProvider({ children }: { children: React.ReactNode }) {
  const { status: authStatus, user } = useAuth();
  const userId = authStatus === 'signedIn' ? user?.id ?? null : null;

  const [status, setStatus] = useState<LocationStatus>('idle');
  const [data, setData] = useState<MyLocations>({ autoUpdatePrimary: true, locations: [] });
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const dataRef = useRef(data);
  dataRef.current = data;
  const syncing = useRef(false);

  const apply = useCallback((next: MyLocations) => setData(next), []);

  /** Auto-update ON: move the primary to the phone's position when it moved more than 1 km. Never prompts. */
  const syncWithGps = useCallback(async (latest?: MyLocations) => {
    const current = latest ?? dataRef.current;
    if (!userId || !current.autoUpdatePrimary || current.locations.length === 0 || syncing.current) return;
    syncing.current = true;
    try {
      const pos = await getCurrentPosition({ prompt: false });
      const gpsRow = current.locations.find(l => l.isCurrentLocation && l.isPrimary);
      if (gpsRow && haversineKm(gpsRow.latitude, gpsRow.longitude, pos.latitude, pos.longitude) < MOVE_THRESHOLD_KM) return;
      apply(await LocationApi.updateCurrent(pos.latitude, pos.longitude, { explicit: false }));
    } catch {
      // No permission, GPS off or offline: keep the saved locations as they are.
    } finally {
      syncing.current = false;
    }
  }, [userId, apply]);

  const load = useCallback(async (): Promise<MyLocations | null> => {
    if (!userId) return null;
    try {
      const next = await LocationApi.list();
      apply(next);
      setStatus('ready');
      return next;
    } catch {
      setStatus(s => (s === 'ready' ? 'ready' : 'error'));
      return null;
    }
  }, [userId, apply]);

  const reload = useCallback(async () => {
    await load();
  }, [load]);

  // Signed in: load the locations and the last selected one. Signed out: forget them.
  useEffect(() => {
    if (!userId) {
      setStatus('idle');
      setData({ autoUpdatePrimary: true, locations: [] });
      setSelectedId(null);
      return;
    }
    let alive = true;
    setStatus('loading');
    (async () => {
      const [saved, loaded] = await Promise.all([readSelected(userId), load()]);
      if (!alive) return;
      setSelectedId(saved);
      if (loaded) syncWithGps(loaded);
    })();
    return () => {
      alive = false;
    };
  }, [userId, load, syncWithGps]);

  // Each time the app comes back to the foreground.
  useEffect(() => {
    const sub = AppState.addEventListener('change', state => {
      if (state === 'active') syncWithGps();
    });
    return () => sub.remove();
  }, [syncWithGps]);

  const primary = data.locations.find(l => l.isPrimary) ?? data.locations[0] ?? null;
  const selected = data.locations.find(l => l.id === selectedId) ?? primary;
  const near = useMemo<NearFilter | undefined>(
    () => (selected ? { latitude: selected.latitude, longitude: selected.longitude, radiusKm: selected.radiusKm } : undefined),
    [selected?.latitude, selected?.longitude, selected?.radiusKm], // eslint-disable-line react-hooks/exhaustive-deps
  );

  const select = useCallback(
    (id: string) => {
      setSelectedId(id);
      if (userId) AsyncStorage.setItem(selectedKey(userId), id).catch(() => undefined);
    },
    [userId],
  );

  const add = useCallback(async (body: SaveLocationRequest) => apply(await LocationApi.add(body)), [apply]);
  const update = useCallback(async (id: string, body: SaveLocationRequest) => apply(await LocationApi.update(id, body)), [apply]);
  const remove = useCallback(async (id: string) => apply(await LocationApi.remove(id)), [apply]);
  const setPrimary = useCallback(async (id: string) => apply(await LocationApi.setPrimary(id)), [apply]);
  const setAutoUpdate = useCallback(async (on: boolean) => apply(await LocationApi.setAutoUpdate(on)), [apply]);

  const useCurrentLocation = useCallback(
    async (radiusKm?: number) => {
      const pos = await getCurrentPosition({ prompt: true });
      apply(await LocationApi.updateCurrent(pos.latitude, pos.longitude, { explicit: true, radiusKm }));
    },
    [apply],
  );

  const value = useMemo<LocationContextValue>(
    () => ({
      status,
      autoUpdatePrimary: data.autoUpdatePrimary,
      locations: data.locations,
      primary,
      selected,
      near,
      select,
      reload,
      add,
      update,
      remove,
      setPrimary,
      useCurrentLocation,
      setAutoUpdate,
    }),
    [status, data, primary, selected, near, select, reload, add, update, remove, setPrimary, useCurrentLocation, setAutoUpdate],
  );

  return <LocationContext.Provider value={value}>{children}</LocationContext.Provider>;
}

export function useLocations(): LocationContextValue {
  const ctx = useContext(LocationContext);
  if (!ctx) throw new Error('useLocations must be used inside <LocationProvider>');
  return ctx;
}
