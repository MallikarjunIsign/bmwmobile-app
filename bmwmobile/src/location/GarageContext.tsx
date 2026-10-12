import React, { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { useAuth } from '../auth/AuthContext';
import { MarketApi } from '../services/marketApi';
import type { GarageVehicle } from '../types/listing';

type GarageValue = {
  vehicles: GarageVehicle[];
  /** The vehicle every search and Home are filtered by (null = all vehicles). */
  selected: GarageVehicle | null;
  select: (id: string | null) => void;
  add: (vehicle: GarageVehicle) => Promise<void>;
  remove: (id: string) => Promise<void>;
  reload: () => Promise<void>;
};

const GarageContext = createContext<GarageValue>({
  vehicles: [],
  selected: null,
  select: () => undefined,
  add: async () => undefined,
  remove: async () => undefined,
  reload: async () => undefined,
});

const selectedKey = (userId: string) => `@bidmyparts/garage-selected/${userId}`;

export const garageLabel = (v: GarageVehicle) =>
  v.nickname ? `${v.nickname} (${v.year} ${v.model})` : `${v.year} ${v.make} ${v.model}${v.trim ? ` ${v.trim}` : ''}`;

/** "My Garage": saved vehicles; choosing one filters every search and the Home list by that vehicle. */
export function GarageProvider({ children }: { children: React.ReactNode }) {
  const { status, user } = useAuth();
  const userId = status === 'signedIn' ? user?.id ?? null : null;
  const [vehicles, setVehicles] = useState<GarageVehicle[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const reload = useCallback(async () => {
    if (!userId) return;
    try {
      setVehicles(await MarketApi.garage());
    } catch {
      // Offline: keep what we have.
    }
  }, [userId]);

  useEffect(() => {
    if (!userId) {
      setVehicles([]);
      setSelectedId(null);
      return;
    }
    AsyncStorage.getItem(selectedKey(userId)).then(setSelectedId).catch(() => undefined);
    reload();
  }, [userId, reload]);

  const select = useCallback(
    (id: string | null) => {
      setSelectedId(id);
      if (userId) {
        (id ? AsyncStorage.setItem(selectedKey(userId), id) : AsyncStorage.removeItem(selectedKey(userId))).catch(() => undefined);
      }
    },
    [userId],
  );

  const add = useCallback(async (vehicle: GarageVehicle) => {
    const saved = await MarketApi.addGarageVehicle(vehicle);
    setVehicles(list => [...list, saved]);
  }, []);

  const remove = useCallback(
    async (id: string) => {
      await MarketApi.deleteGarageVehicle(id);
      setVehicles(list => list.filter(v => v.id !== id));
      if (selectedId === id) select(null);
    },
    [selectedId, select],
  );

  const selected = vehicles.find(v => v.id === selectedId) ?? null;
  const value = useMemo(() => ({ vehicles, selected, select, add, remove, reload }), [vehicles, selected, select, add, remove, reload]);
  return <GarageContext.Provider value={value}>{children}</GarageContext.Provider>;
}

export const useGarage = () => useContext(GarageContext);
