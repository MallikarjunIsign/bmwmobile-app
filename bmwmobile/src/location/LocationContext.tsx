import React, { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { AppState } from 'react-native';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { useAuth } from '../auth/AuthContext';
import { ApiService, type NearFilter } from '../services/api';
import { LocationApi } from '../services/locationApi';
import { PlacesApi } from '../services/placesApi';
import { toSaveRequest } from '../utils/places';
import type { AutoLocation, CountryCode, MyLocations, PlaceDetails, SaveLocationRequest, SavedLocation } from '../types/location';
import {
  countryFromPhone, fallbackRadius, haversineKm, isCountryCode, MIN_MOVE_KM, MOVE_THRESHOLD_KM, radiusFor, savedRadius,
} from '../utils/geo';
import { getCurrentPosition, GpsError, type GpsFailure, openLocationSettings } from '../utils/gps';

type LocationStatus = 'idle' | 'ready';

type LocationContextValue = {
  /** 'ready' once the last known location has been read from the phone (signed-in users only). */
  status: LocationStatus;
  /** The automatic location (null until the first fix, or when location is off). */
  location: AutoLocation | null;
  /** Location permission denied or GPS off: show "Turn on location to see parts near you". */
  locationOff: boolean;
  /** The location and its radius (pickup-only listings inside it, shipping country-wide); undefined = country-wide. */
  near: NearFilter | undefined;
  /** km/miles and the currency: the location's country, else the phone number's. */
  country: CountryCode;
  /** Read the position again (prompt = ask for permission if needed). */
  refresh: (opts?: { prompt?: boolean }) => Promise<void>;
  /** The banner's button: ask again, or open the settings when Android won't ask any more / GPS is off. */
  turnOnLocation: () => Promise<void>;
  /** The user's saved addresses (not the GPS row). */
  addresses: SavedLocation[];
  /** True while the location follows the phone's GPS; false when a saved address is chosen. */
  usingCurrentLocation: boolean;
  /**
   * "Use my current location": GPS again (asks for permission), with the radius chosen on that screen (default 200).
   * Throws GpsError when it can't.
   */
  switchToCurrentLocation: (radiusValue?: number) => Promise<void>;
  /**
   * Change the radius of the location in use (in its unit: mi in the USA, km elsewhere). The lists and counts update at
   * once; saved on the server (the saved address, or the GPS location). Throws when the server refuses it.
   */
  setRadius: (radiusValue: number) => Promise<void>;
  /** Use a saved address as the location (GPS no longer changes it). */
  chooseAddress: (id: string) => Promise<void>;
  /** Add (and use) or edit an address. */
  saveAddress: (request: SaveLocationRequest, id?: string) => Promise<void>;
  removeAddress: (id: string) => Promise<void>;
  /**
   * Home → "Search location": use a searched place. A saved location of the same place is chosen again; otherwise it
   * is saved (named after its area / city) and used, keeping the radius number in use.
   */
  choosePlace: (place: PlaceDetails) => Promise<void>;
};

const LocationContext = createContext<LocationContextValue | undefined>(undefined);

const storageKey = (userId: string) => `@bidmyparts/auto-location/${userId}`;
const addressesKey = (userId: string) => `@bidmyparts/addresses/${userId}`;

/** The chosen saved address as the app's location, with its own radius. */
function fromAddress(a: SavedLocation, phoneCountry: CountryCode): AutoLocation {
  return {
    latitude: a.latitude,
    longitude: a.longitude,
    // Newer locations keep the city apart from the label ("Kukatpally, Hyderabad"); older ones: the place name.
    city: a.city ?? a.placeName,
    countryCode: a.countryCode,
    ...savedRadius(a, phoneCountry),
    updatedAt: new Date().toISOString(),
    addressId: a.id,
    label: a.label,
    area: a.area ?? null,
  };
}

async function readSaved(userId: string): Promise<AutoLocation | null> {
  try {
    const raw = await AsyncStorage.getItem(storageKey(userId));
    return raw ? (JSON.parse(raw) as AutoLocation) : null;
  } catch {
    return null;
  }
}

/**
 * Names the position through the backend: area + city ("Kukatpally, Hyderabad") from "Search location" (Google,
 * cached), else the city from /geo/reverse; offline, keeps the coordinates with the phone number's country.
 */
async function describe(latitude: number, longitude: number, phoneCountry: CountryCode): Promise<AutoLocation> {
  const now = new Date().toISOString();
  try {
    const place = await PlacesApi.reverse(latitude, longitude);
    const countryCode = place.supported && isCountryCode(place.countryCode) ? (place.countryCode.toUpperCase() as CountryCode) : null;
    return {
      latitude,
      longitude,
      city: place.city ?? null,
      area: place.area ?? null,
      countryCode,
      ...fallbackRadius(countryCode ?? phoneCountry),
      updatedAt: now,
    };
  } catch {
    // Older backend without /places, or the lookup limit: the city only.
  }
  try {
    const geo = await ApiService.reverseGeocode(latitude, longitude);
    const countryCode = isCountryCode(geo.countryCode) ? (geo.countryCode.toUpperCase() as CountryCode) : null;
    return {
      latitude,
      longitude,
      city: geo.city,
      countryCode,
      radiusKm: geo.radiusKm,
      radiusValue: geo.radiusValue,
      radiusUnit: geo.radiusUnit,
      updatedAt: now,
    };
  } catch {
    return { latitude, longitude, city: null, countryCode: null, ...fallbackRadius(phoneCountry), updatedAt: now };
  }
}

/**
 * The user's location: found automatically from GPS, or a saved address the user chose (Home → location line →
 * "My locations").
 *  - After sign-in: asks for location permission and reads the position (unless a saved address is chosen).
 *  - Each time the app comes back to the foreground: reads it again (without prompting).
 *  - The location only changes when the phone moved more than ~5 km or the city changed.
 *  - Permission denied / GPS off: "Turn on location" banner, country-wide results (phone number's country).
 */
export function LocationProvider({ children }: { children: React.ReactNode }) {
  const { status: authStatus, user } = useAuth();
  const userId = authStatus === 'signedIn' ? user?.id ?? null : null;
  const phoneCountry = countryFromPhone(user?.phoneNumber);

  const [status, setStatus] = useState<LocationStatus>('idle');
  const [location, setLocation] = useState<AutoLocation | null>(null);
  const [failure, setFailure] = useState<GpsFailure | null>(null);
  const locationRef = useRef<AutoLocation | null>(null);
  locationRef.current = location;
  const busy = useRef(false);
  // Saved addresses; the primary one (when it isn't the GPS row) replaces the GPS location.
  const [mine, setMine] = useState<MyLocations | null>(null);

  const save = useCallback(
    (next: AutoLocation) => {
      setLocation(next);
      if (userId) AsyncStorage.setItem(storageKey(userId), JSON.stringify(next)).catch(() => undefined);
    },
    [userId],
  );

  const refresh = useCallback(
    async ({ prompt = false }: { prompt?: boolean } = {}) => {
      if (!userId || busy.current) return;
      busy.current = true;
      try {
        const pos = await getCurrentPosition({ prompt });
        setFailure(null);
        const current = locationRef.current;
        const moved = current ? haversineKm(current.latitude, current.longitude, pos.latitude, pos.longitude) : Infinity;
        if (moved < MIN_MOVE_KM) return;

        const next = await describe(pos.latitude, pos.longitude, phoneCountry);
        // A small move only counts when it crossed into another city.
        if (current && moved < MOVE_THRESHOLD_KM && next.city === current.city && next.countryCode === current.countryCode) return;
        save(next);
      } catch (err) {
        // Timeout: keep the last known location. Denied / GPS off: banner + country-wide results.
        if (err instanceof GpsError && err.needsUser) setFailure(err.reason);
      } finally {
        busy.current = false;
      }
    },
    [userId, phoneCountry, save],
  );

  const keepAddresses = useCallback(
    (next: MyLocations) => {
      setMine(next);
      if (userId) AsyncStorage.setItem(addressesKey(userId), JSON.stringify(next)).catch(() => undefined);
    },
    [userId],
  );

  // Signed in: show the last known location at once, then ask for permission and read the position
  // (no permission prompt when the user chose a saved address – GPS isn't needed then).
  useEffect(() => {
    if (!userId) {
      setStatus('idle');
      setLocation(null);
      setFailure(null);
      setMine(null);
      return;
    }
    let alive = true;
    (async () => {
      const saved = await readSaved(userId);
      let addresses: MyLocations | null = null;
      try {
        const raw = await AsyncStorage.getItem(addressesKey(userId));
        addresses = raw ? (JSON.parse(raw) as MyLocations) : null;
      } catch {
        addresses = null;
      }
      if (!alive) return;
      setLocation(saved);
      locationRef.current = saved;
      setMine(addresses);
      setStatus('ready');
      const fresh = await LocationApi.list().catch(() => null);
      if (!alive) return;
      if (fresh) keepAddresses(fresh);
      const chosen = (fresh ?? addresses)?.locations.some(l => l.isPrimary && !l.isCurrentLocation);
      refresh({ prompt: !chosen });
    })();
    return () => {
      alive = false;
    };
  }, [userId]); // eslint-disable-line react-hooks/exhaustive-deps

  // Each time the app comes back to the foreground (also after the user switched location on in Settings).
  useEffect(() => {
    const sub = AppState.addEventListener('change', state => {
      if (state === 'active') refresh();
    });
    return () => sub.remove();
  }, [refresh]);

  const turnOnLocation = useCallback(async () => {
    if (failure === 'denied') {
      await refresh({ prompt: true });
      return;
    }
    await openLocationSettings(failure);
  }, [failure, refresh]);

  const addresses = useMemo(() => (mine?.locations ?? []).filter(l => !l.isCurrentLocation), [mine]);
  const addressesRef = useRef<SavedLocation[]>([]);
  addressesRef.current = addresses;
  const chosen = addresses.find(l => l.isPrimary) ?? null;

  // The GPS row on the server ("Use my current location"), which keeps the radius chosen for the GPS location.
  const gpsRow = useMemo(() => (mine?.locations ?? []).find(l => l.isCurrentLocation) ?? null, [mine]);

  const switchToCurrentLocation = useCallback(async (radiusValue?: number) => {
    // Throws GpsError (denied / GPS off) for the screen to explain.
    const pos = await getCurrentPosition({ prompt: true });
    setFailure(null);
    const next = await describe(pos.latitude, pos.longitude, phoneCountry);
    const radius = radiusValue ?? next.radiusValue ?? fallbackRadius(next.countryCode ?? phoneCountry).radiusValue;
    save({ ...next, ...radiusFor(radius, next.countryCode ?? phoneCountry) });
    keepAddresses(await LocationApi.setCurrent(pos.latitude, pos.longitude, radius));
  }, [phoneCountry, save, keepAddresses]);

  const chooseAddress = useCallback(async (id: string) => keepAddresses(await LocationApi.setPrimary(id)), [keepAddresses]);

  const saveAddress = useCallback(
    async (request: SaveLocationRequest, id?: string) => {
      const country = isCountryCode(request.countryCode) ? (request.countryCode as CountryCode) : phoneCountry;
      const body = { ...request, radiusValue: request.radiusValue ?? fallbackRadius(country).radiusValue };
      keepAddresses(id ? await LocationApi.update(id, body) : await LocationApi.add(body));
    },
    [phoneCountry, keepAddresses],
  );

  const removeAddress = useCallback(async (id: string) => keepAddresses(await LocationApi.remove(id)), [keepAddresses]);

  const choosePlace = useCallback(
    async (place: PlaceDetails) => {
      const same = place.placeId ? addressesRef.current.find(a => a.placeId === place.placeId) : undefined;
      if (same) {
        keepAddresses(await LocationApi.setPrimary(same.id));
        return;
      }
      await saveAddress(toSaveRequest(place, {
        label: (place.area || place.city || place.label).slice(0, 40),
        radiusValue: activeRef.current?.radiusValue,
        makePrimary: true,
      }));
    },
    [keepAddresses, saveAddress],
  );

  // A radius just chosen, shown at once (lists and counts update) until the server's answer arrives.
  const [pendingRadius, setPendingRadius] = useState<number | null>(null);

  // A chosen address wins over GPS; without one, GPS (or nothing while location is off). The GPS location uses the
  // radius saved with the GPS row, else the default (200 mi / 200 km) – never an older cached one.
  const locationOff = !chosen && failure !== null;
  const active = useMemo(() => {
    // The GPS radius number in the unit of where the phone is now (200 mi in the USA → 200 km after moving to India).
    const here = location?.countryCode ?? phoneCountry;
    const base = chosen ? fromAddress(chosen, phoneCountry) : locationOff || !location ? null
      : { ...location, ...(gpsRow ? radiusFor(savedRadius(gpsRow, here).radiusValue, here) : fallbackRadius(here)) };
    return base && pendingRadius != null ? { ...base, ...radiusFor(pendingRadius, base.countryCode ?? phoneCountry) } : base;
  }, [chosen, phoneCountry, locationOff, location, gpsRow, pendingRadius]);

  const setRadius = useCallback(
    async (radiusValue: number) => {
      if (!active) return;
      setPendingRadius(radiusValue);
      try {
        const next = chosen
          ? await LocationApi.setRadius(chosen.id, radiusValue)
          : gpsRow
            ? await LocationApi.setRadius(gpsRow.id, radiusValue)
            : await LocationApi.setCurrent(active.latitude, active.longitude, radiusValue);
        keepAddresses(next);
      } finally {
        setPendingRadius(null);
      }
    },
    [active, chosen, gpsRow, keepAddresses],
  );

  const activeRef = useRef<AutoLocation | null>(null);
  activeRef.current = active;

  const near = useMemo<NearFilter | undefined>(
    () => (active ? { latitude: active.latitude, longitude: active.longitude, radiusKm: active.radiusKm } : undefined),
    [active?.latitude, active?.longitude, active?.radiusKm], // eslint-disable-line react-hooks/exhaustive-deps
  );

  const country: CountryCode = active?.countryCode ?? phoneCountry;

  const value = useMemo<LocationContextValue>(
    () => ({
      status, location: active, locationOff, near, country, refresh, turnOnLocation,
      addresses, usingCurrentLocation: !chosen, switchToCurrentLocation, setRadius, chooseAddress, saveAddress, removeAddress, choosePlace,
    }),
    [status, active, locationOff, near, country, refresh, turnOnLocation, addresses, chosen, switchToCurrentLocation, setRadius, chooseAddress, saveAddress, removeAddress, choosePlace],
  );

  return <LocationContext.Provider value={value}>{children}</LocationContext.Provider>;
}

export function useLocations(): LocationContextValue {
  const ctx = useContext(LocationContext);
  if (!ctx) throw new Error('useLocations must be used inside <LocationProvider>');
  return ctx;
}
