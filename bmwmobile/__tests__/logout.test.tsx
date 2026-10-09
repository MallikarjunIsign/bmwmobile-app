import React from 'react';
import ReactTestRenderer from 'react-test-renderer';
import { AppNavigator } from '../src/navigation/AppNavigator';

// Rendering the whole navigator can be slow on a cold Jest cache.
jest.setTimeout(20000);

// Drive the navigator with a fake signed-in / signed-out state.
let mockAuthStatus: 'signedIn' | 'signedOut' = 'signedIn';
const mockUser = { id: 'u1', fullName: 'Test User', email: 't@example.com', phoneNumber: '+353851234567' };
const mockLocation = {
  id: 'l1', label: 'Home', placeName: 'Dublin', latitude: 53.3498, longitude: -6.2603,
  radiusKm: 25, countryCode: 'IE', isPrimary: true, isCurrentLocation: false,
};

jest.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ status: mockAuthStatus, user: mockAuthStatus === 'signedIn' ? mockUser : null, logout: jest.fn() }),
}));
jest.mock('../src/location/LocationContext', () => ({
  useLocations: () =>
    mockAuthStatus === 'signedIn'
      ? { status: 'ready', locations: [mockLocation], selected: mockLocation, primary: mockLocation, country: 'IE', near: undefined }
      : { status: 'idle', locations: [], selected: null, primary: null, country: 'IN', near: undefined },
}));
jest.mock('../src/services/api', () => ({
  ApiService: { getAuctions: jest.fn().mockResolvedValue([]) },
}));

const texts = (tree: ReactTestRenderer.ReactTestRenderer) =>
  tree.root
    .findAll(node => (node.type as unknown) === 'Text')
    .map(node => node.props.children)
    .flat(Infinity)
    .join(' ');

test('home header shows the user, then the selected place and country', async () => {
  mockAuthStatus = 'signedIn';
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(<AppNavigator />);
  });
  const t = texts(tree!);
  expect(t).toContain('Test User');
  expect(t).toContain('Dublin, Ireland');
  expect(t).not.toContain('Pickup Port');
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
  expect(t).not.toContain('Guest');
  expect(t).not.toContain('ENTER MARKETPLACE');
});
