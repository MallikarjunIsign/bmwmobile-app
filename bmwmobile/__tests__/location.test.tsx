import React from 'react';
import ReactTestRenderer from 'react-test-renderer';
import { LocationProvider, useLocations } from '../src/location/LocationContext';
import { ApiService } from '../src/services/api';
import { getCurrentPosition, GpsError } from '../src/utils/gps';

jest.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ status: 'signedIn', user: { id: 'u1', phoneNumber: '+12015550123' } }),
}));
jest.mock('../src/services/api', () => ({ ApiService: { reverseGeocode: jest.fn() } }));
jest.mock('../src/utils/gps', () => {
  const actual = jest.requireActual('../src/utils/gps');
  return { ...actual, getCurrentPosition: jest.fn(), openLocationSettings: jest.fn() };
});

const reverseGeocode = ApiService.reverseGeocode as jest.Mock;
const position = getCurrentPosition as jest.Mock;

const HYDERABAD = { latitude: 17.385, longitude: 78.4867 };
const hyderabadGeo = { ...HYDERABAD, city: 'Hyderabad', countryCode: 'IN', supported: true, radiusValue: 60, radiusUnit: 'km', radiusKm: 60 };

let ctx: ReturnType<typeof useLocations>;
const Probe = () => {
  ctx = useLocations();
  return null;
};

const render = async () => {
  await ReactTestRenderer.act(async () => {
    ReactTestRenderer.create(
      <LocationProvider>
        <Probe />
      </LocationProvider>,
    );
  });
  // Let the GPS read and the reverse geocode finish.
  await ReactTestRenderer.act(async () => {
    await new Promise(resolve => setTimeout(resolve, 0));
  });
};

beforeEach(() => {
  reverseGeocode.mockReset();
  position.mockReset();
});

test('after sign-in: asks for permission, sets city, country and the 60 km radius automatically', async () => {
  position.mockResolvedValue(HYDERABAD);
  reverseGeocode.mockResolvedValue(hyderabadGeo);

  await render();

  expect(position).toHaveBeenCalledWith({ prompt: true });
  expect(ctx.location).toMatchObject({ city: 'Hyderabad', countryCode: 'IN', radiusKm: 60, radiusUnit: 'km' });
  expect(ctx.near).toEqual({ latitude: 17.385, longitude: 78.4867, radiusKm: 60 });
  expect(ctx.country).toBe('IN');
  expect(ctx.locationOff).toBe(false);
});

test('USA: 60 miles and dollars', async () => {
  position.mockResolvedValue({ latitude: 40.71, longitude: -74.0 });
  reverseGeocode.mockResolvedValue({ latitude: 40.71, longitude: -74.0, city: 'New York', countryCode: 'US', supported: true, radiusValue: 60, radiusUnit: 'mi', radiusKm: 96.56 });

  await render();

  expect(ctx.location).toMatchObject({ city: 'New York', radiusValue: 60, radiusUnit: 'mi' });
  expect(ctx.near?.radiusKm).toBe(96.56);
  expect(ctx.country).toBe('US');
});

test('permission denied: banner + country-wide results from the phone number country', async () => {
  position.mockRejectedValue(new GpsError('Turn on location to see parts near you', 'denied'));

  await render();

  expect(ctx.locationOff).toBe(true);
  expect(ctx.near).toBeUndefined();
  expect(ctx.country).toBe('US'); // +1 phone number
  expect(reverseGeocode).not.toHaveBeenCalled();
});

test('a GPS timeout keeps the last location without the banner', async () => {
  position.mockRejectedValue(new GpsError('Finding your location took too long.', 'timeout'));

  await render();

  expect(ctx.locationOff).toBe(false);
});

test('small moves in the same city are ignored; moves over 5 km update the location', async () => {
  position.mockResolvedValue(HYDERABAD);
  reverseGeocode.mockResolvedValue(hyderabadGeo);
  await render();
  expect(reverseGeocode).toHaveBeenCalledTimes(1);

  // ~2.5 km away, still Hyderabad: location stays.
  position.mockResolvedValue({ latitude: 17.4075, longitude: 78.4867 });
  await ReactTestRenderer.act(async () => ctx.refresh());
  expect(ctx.location?.latitude).toBe(17.385);

  // ~11 km away: location moves.
  reverseGeocode.mockResolvedValue({ ...hyderabadGeo, latitude: 17.485, city: 'Hyderabad' });
  position.mockResolvedValue({ latitude: 17.485, longitude: 78.4867 });
  await ReactTestRenderer.act(async () => ctx.refresh());
  expect(ctx.location?.latitude).toBe(17.485);
});

test('backend down: keeps the coordinates with the phone country radius', async () => {
  position.mockResolvedValue(HYDERABAD);
  reverseGeocode.mockRejectedValue(new Error('Network Error'));

  await render();

  expect(ctx.location).toMatchObject({ latitude: 17.385, city: null, countryCode: null, radiusUnit: 'mi' });
  expect(ctx.near).toBeDefined();
});
