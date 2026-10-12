import React from 'react';
import ReactTestRenderer from 'react-test-renderer';
import { AppNavigator } from '../src/navigation/AppNavigator';

// Rendering the whole navigator can be slow on a cold Jest cache.
jest.setTimeout(20000);

// Drive the navigator with a fake signed-in / signed-out state.
let mockAuthStatus: 'signedIn' | 'signedOut' = 'signedIn';
const mockUser = { id: 'u1', fullName: 'Test User', email: 't@example.com', phoneNumber: '+353851234567' };
const mockLocation = {
  latitude: 53.3498, longitude: -6.2603, city: 'Dublin', countryCode: 'IE',
  radiusKm: 60, radiusValue: 60, radiusUnit: 'km', updatedAt: '2026-10-10T00:00:00Z',
};

jest.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ status: mockAuthStatus, user: mockAuthStatus === 'signedIn' ? mockUser : null, logout: jest.fn() }),
}));
// The real context memoizes these objects, so the mock returns the same ones on every render.
const mockSignedIn = { status: 'ready', location: mockLocation, locationOff: false, country: 'IE', near: { latitude: 53.3498, longitude: -6.2603, radiusKm: 60 } };
const mockSignedOut = { status: 'idle', location: null, locationOff: false, country: 'IN', near: undefined };
jest.mock('../src/location/LocationContext', () => ({
  useLocations: () => (mockAuthStatus === 'signedIn' ? mockSignedIn : mockSignedOut),
}));
jest.mock('../src/services/api', () => ({
  ApiService: {
    getAuctions: jest.fn().mockResolvedValue([]),
    getPartRequests: jest.fn().mockResolvedValue([]),
    getFilterOptions: jest.fn().mockResolvedValue({ categories: [], makes: [], models: [], totalCount: 0 }),
  },
}));

const texts = (tree: ReactTestRenderer.ReactTestRenderer) =>
  tree.root
    .findAll(node => (node.type as unknown) === 'Text')
    .map(node => node.props.children)
    .flat(Infinity)
    .join(' ');

test('home: ShopCarParts header with the user and their location (tap to change); no "parts near", container or Tracking', async () => {
  mockAuthStatus = 'signedIn';
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(<AppNavigator />);
  });
  const t = texts(tree!);
  expect(t).toContain('SHOPCARPARTS');
  expect(t).toContain('Test User');
  // The location (city, country) is shown under the name; tapping it opens My locations.
  expect(t).toContain('Dublin, Ireland');
  const line = tree!.root.find(n => n.props.accessibilityLabel === 'Change location' && n.props.onPress);
  expect(line).toBeTruthy();
  expect(t).not.toContain('SHOWING PARTS NEAR');
  // Container / port logistics are hidden (FEATURES.containers = false).
  expect(t).not.toMatch(/container|pickup port|apapa|tracking/i);
  expect(t).not.toMatch(/okoh/i);
});

test('home: nothing listed → the "no parts available" message with a custom order button', async () => {
  mockAuthStatus = 'signedIn';
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(<AppNavigator />);
  });
  const t = texts(tree!);
  expect(t).toContain('There are no parts available for this search.');
  expect(t).toContain('Create a custom order for your part and get notified when the part is available.');
  expect(t).toContain('Create custom order');
});

test('after logging out the Login screen opens, with no guest option', async () => {
  mockAuthStatus = 'signedIn';
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(<AppNavigator />);
  });
  mockAuthStatus = 'signedOut';
  await ReactTestRenderer.act(async () => {
    tree!.update(<AppNavigator />);
  });
  const t = texts(tree!);
  expect(t).toContain('Welcome Back');
  expect(t).toContain('ShopCarParts');
  expect(t).not.toContain('Guest');
  expect(t).not.toContain('ENTER MARKETPLACE');
});
