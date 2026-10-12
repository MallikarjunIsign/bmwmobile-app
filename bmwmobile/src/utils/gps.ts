import { Linking, PermissionsAndroid, Platform } from 'react-native';
import Geolocation from '@react-native-community/geolocation';

// We ask for permission ourselves (after sign-in, or when the user taps "Turn on location").
Geolocation.setRNConfiguration({ skipPermissionRequests: true, authorizationLevel: 'whenInUse' });

/**
 * Why the position couldn't be read:
 *  - denied: the user said no to location access ("blocked" = Android won't ask again, only Settings helps)
 *  - unavailable: location (GPS) is switched off
 *  - timeout: no fix in time (keep the last known location)
 */
export type GpsFailure = 'denied' | 'blocked' | 'unavailable' | 'timeout';

/** Error with a message that is safe to show. */
export class GpsError extends Error {
  reason: GpsFailure;

  constructor(message: string, reason: GpsFailure) {
    super(message);
    this.name = 'GpsError';
    this.reason = reason;
  }

  /** The user has to do something (allow access or switch location on) – show the banner. */
  get needsUser(): boolean {
    return this.reason !== 'timeout';
  }
}

const DENIED_MESSAGE = 'Turn on location to see parts near you';

export type Coordinates = { latitude: number; longitude: number };

/** True when location is already allowed (never shows a prompt). */
export async function hasLocationPermission(): Promise<boolean> {
  if (Platform.OS !== 'android') return true; // iOS: getCurrentPosition fails quietly when not allowed.
  const fine = await PermissionsAndroid.check(PermissionsAndroid.PERMISSIONS.ACCESS_FINE_LOCATION);
  const coarse = await PermissionsAndroid.check(PermissionsAndroid.PERMISSIONS.ACCESS_COARSE_LOCATION);
  return fine || coarse;
}

async function requestPermission(): Promise<{ granted: boolean; precise: boolean; blocked: boolean }> {
  if (Platform.OS === 'android') {
    const result = await PermissionsAndroid.requestMultiple([
      PermissionsAndroid.PERMISSIONS.ACCESS_FINE_LOCATION,
      PermissionsAndroid.PERMISSIONS.ACCESS_COARSE_LOCATION,
    ]);
    const fine = result[PermissionsAndroid.PERMISSIONS.ACCESS_FINE_LOCATION];
    const coarse = result[PermissionsAndroid.PERMISSIONS.ACCESS_COARSE_LOCATION];
    const precise = fine === PermissionsAndroid.RESULTS.GRANTED;
    const approximate = coarse === PermissionsAndroid.RESULTS.GRANTED;
    const blocked = fine === PermissionsAndroid.RESULTS.NEVER_ASK_AGAIN && coarse === PermissionsAndroid.RESULTS.NEVER_ASK_AGAIN;
    return { granted: precise || approximate, precise, blocked };
  }
  return new Promise(resolve =>
    Geolocation.requestAuthorization(
      () => resolve({ granted: true, precise: true, blocked: false }),
      () => resolve({ granted: false, precise: false, blocked: true }),
    ),
  );
}

/**
 * The phone's position.
 * prompt=true: asks for permission first (after sign-in / "Turn on location").
 * prompt=false: only when already allowed (the check when the app comes back to the foreground).
 */
export async function getCurrentPosition({ prompt }: { prompt: boolean }): Promise<Coordinates> {
  let highAccuracy = true;
  if (prompt) {
    const { granted, precise, blocked } = await requestPermission();
    if (!granted) throw new GpsError(DENIED_MESSAGE, blocked ? 'blocked' : 'denied');
    highAccuracy = precise;
  } else if (!(await hasLocationPermission())) {
    throw new GpsError(DENIED_MESSAGE, 'denied');
  }

  return new Promise((resolve, reject) => {
    Geolocation.getCurrentPosition(
      pos => resolve({ latitude: pos.coords.latitude, longitude: pos.coords.longitude }),
      err => {
        if (err.code === err.PERMISSION_DENIED) reject(new GpsError(DENIED_MESSAGE, 'denied'));
        else if (err.code === err.TIMEOUT) reject(new GpsError('Finding your location took too long.', 'timeout'));
        else reject(new GpsError(DENIED_MESSAGE, 'unavailable'));
      },
      // A recent fix (up to 1 minute old) is fine: we only care about moves of several km.
      { enableHighAccuracy: highAccuracy, timeout: 15000, maximumAge: 60000 },
    );
  });
}

/** "Turn on location": the app's settings page, or the system location switch when GPS is off (Android). */
export async function openLocationSettings(reason: GpsFailure | null): Promise<void> {
  if (Platform.OS === 'android' && reason === 'unavailable') {
    try {
      await Linking.sendIntent('android.settings.LOCATION_SOURCE_SETTINGS');
      return;
    } catch {
      // fall through to the app settings
    }
  }
  await Linking.openSettings();
}
