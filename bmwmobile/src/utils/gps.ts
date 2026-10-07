import { PermissionsAndroid, Platform } from 'react-native';
import Geolocation from '@react-native-community/geolocation';

// We ask for permission ourselves, only when the user taps "Use my current location".
Geolocation.setRNConfiguration({ skipPermissionRequests: true, authorizationLevel: 'whenInUse' });

/** Error with a message that is safe to show. `denied` = the user said no to location access. */
export class GpsError extends Error {
  denied: boolean;

  constructor(message: string, denied = false) {
    super(message);
    this.name = 'GpsError';
    this.denied = denied;
  }
}

const DENIED_MESSAGE = "No problem — without location access we can't use GPS. Pick a city instead.";

export type Coordinates = { latitude: number; longitude: number };

/** True when location is already allowed (never shows a prompt). */
export async function hasLocationPermission(): Promise<boolean> {
  if (Platform.OS !== 'android') return true; // iOS: getCurrentPosition fails quietly when not allowed.
  const fine = await PermissionsAndroid.check(PermissionsAndroid.PERMISSIONS.ACCESS_FINE_LOCATION);
  const coarse = await PermissionsAndroid.check(PermissionsAndroid.PERMISSIONS.ACCESS_COARSE_LOCATION);
  return fine || coarse;
}

async function requestPermission(): Promise<{ granted: boolean; precise: boolean }> {
  if (Platform.OS === 'android') {
    const result = await PermissionsAndroid.requestMultiple([
      PermissionsAndroid.PERMISSIONS.ACCESS_FINE_LOCATION,
      PermissionsAndroid.PERMISSIONS.ACCESS_COARSE_LOCATION,
    ]);
    const precise = result[PermissionsAndroid.PERMISSIONS.ACCESS_FINE_LOCATION] === PermissionsAndroid.RESULTS.GRANTED;
    const approximate = result[PermissionsAndroid.PERMISSIONS.ACCESS_COARSE_LOCATION] === PermissionsAndroid.RESULTS.GRANTED;
    return { granted: precise || approximate, precise };
  }
  return new Promise(resolve =>
    Geolocation.requestAuthorization(
      () => resolve({ granted: true, precise: true }),
      () => resolve({ granted: false, precise: false }),
    ),
  );
}

/**
 * The phone's position.
 * prompt=true: asks for permission first (user tapped "Use my current location").
 * prompt=false: only when already allowed (the automatic check when the app opens).
 */
export async function getCurrentPosition({ prompt }: { prompt: boolean }): Promise<Coordinates> {
  let highAccuracy = true;
  if (prompt) {
    const { granted, precise } = await requestPermission();
    if (!granted) throw new GpsError(DENIED_MESSAGE, true);
    highAccuracy = precise;
  } else if (!(await hasLocationPermission())) {
    throw new GpsError(DENIED_MESSAGE, true);
  }

  return new Promise((resolve, reject) => {
    Geolocation.getCurrentPosition(
      pos => resolve({ latitude: pos.coords.latitude, longitude: pos.coords.longitude }),
      err => {
        if (err.code === err.PERMISSION_DENIED) reject(new GpsError(DENIED_MESSAGE, true));
        else if (err.code === err.TIMEOUT) reject(new GpsError('Finding your location took too long. Try again, or pick a city.'));
        else reject(new GpsError("We couldn't find your location. Check that location is on, or pick a city."));
      },
      // The automatic check on app open needs a fresh fix (a cached one would hide a move); a tap may reuse a recent one.
      { enableHighAccuracy: highAccuracy, timeout: 15000, maximumAge: prompt ? 60000 : 5000 },
    );
  });
}
