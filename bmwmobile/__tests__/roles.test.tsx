import React from 'react';
import { Linking } from 'react-native';
import ReactTestRenderer from 'react-test-renderer';
import { AppNavigator } from '../src/navigation/AppNavigator';
import { SignUpScreen } from '../src/screens/SignUpScreen';
import { ProfileScreen } from '../src/screens/ProfileScreen';
import { MyPurchasesScreen } from '../src/screens/MyPurchasesScreen';
import { MarketApi } from '../src/services/marketApi';
import { isSeller, normalizeRole, roleFromAccessToken } from '../src/utils/roles';
import { config } from '../src/config';
import type { Order } from '../src/types/listing';

jest.setTimeout(20000);

// Drive the screens with a fake signed-in buyer or seller.
let mockRole: string = 'buyer';
const mockUser = () => ({
  id: 'u1', username: 'ravi', fullName: 'Ravi Kumar', email: 'ravi@example.com', phoneNumber: '+919876543210',
  role: mockRole, emailVerified: true, hasLocation: true,
});
jest.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ status: 'signedIn', user: mockUser(), logout: jest.fn(), register: jest.fn(), login: jest.fn(), signInWith: jest.fn() }),
}));
const mockLocations = {
  status: 'ready', location: null, locationOff: false, country: 'IN', near: undefined, addresses: [],
};
jest.mock('../src/location/LocationContext', () => ({ useLocations: () => mockLocations }));
jest.mock('../src/services/api', () => ({
  ApiService: {
    getAuctions: jest.fn().mockResolvedValue([]),
    getPartRequests: jest.fn().mockResolvedValue([]),
    getFilterOptions: jest.fn().mockResolvedValue({ categories: [], makes: [], models: [], totalCount: 0 }),
  },
  ServerError: class extends Error {},
}));
jest.mock('../src/services/sellerApplicationApi', () => ({
  SellerApplicationApi: { mine: jest.fn().mockResolvedValue(null), submit: jest.fn() },
}));
jest.mock('../src/services/marketApi', () => ({
  MarketApi: { search: jest.fn().mockResolvedValue({ results: [], totalCount: 0 }), suggest: jest.fn().mockResolvedValue([]), orders: jest.fn() },
}));

// Screens rendered on their own (outside a navigator) load on mount instead of on focus.
jest.mock('@react-navigation/native', () => {
  const actual = jest.requireActual('@react-navigation/native');
  const { useEffect } = jest.requireActual('react');
  return { ...actual, useFocusEffect: (effect: () => void) => useEffect(effect, [effect]) };
});

const market = MarketApi as unknown as Record<string, jest.Mock>;

const textOf = (node: ReactTestRenderer.ReactTestInstance | string): string =>
  typeof node === 'string' ? node : node.children.map(textOf).join('');
const texts = (tree: ReactTestRenderer.ReactTestRenderer) =>
  tree.root.findAll(n => (n.type as unknown) === 'Text').map(textOf).join(' | ');
const press = async (tree: ReactTestRenderer.ReactTestRenderer, match: (t: string) => boolean) => {
  let node: ReactTestRenderer.ReactTestInstance | null = tree.root.find(n => (n.type as unknown) === 'Text' && match(textOf(n)));
  while (node && !node.props.onPress) node = node.parent;
  await ReactTestRenderer.act(async () => node!.props.onPress());
};

const mounted: ReactTestRenderer.ReactTestRenderer[] = [];
const mount = async (element: React.ReactElement) => {
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(element);
  });
  mounted.push(tree!);
  return tree!;
};
afterEach(async () => {
  for (const tree of mounted.splice(0)) await ReactTestRenderer.act(async () => tree.unmount());
  jest.restoreAllMocks();
});

const navigation = { navigate: jest.fn(), goBack: jest.fn(), replace: jest.fn() } as any;
const tabLabels = (tree: ReactTestRenderer.ReactTestRenderer) =>
  // Each label shows up twice in the test renderer's tree: keep the first of each.
  [...new Set(tree.root.findAll(n => (n.type as unknown) === 'Text').map(textOf).filter(t => ['Home', 'Market', 'Sell', 'My Bids', 'Orders'].includes(t)))];

/** A JWT with this payload (the app only reads it, it doesn't verify it). */
const jwt = (payload: object) => {
  const b64 = (s: string) => Buffer.from(s).toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/[=]+$/, '');
  return `${b64('{"alg":"RS256"}')}.${b64(JSON.stringify(payload))}.signature`;
};

test('role from the token: buyer / seller / admin; older "user" sessions and missing roles are buyers', () => {
  expect(roleFromAccessToken(jwt({ sub: 'u1', role: 'seller', name: 'Ravi Kümar' }))).toBe('seller');
  expect(roleFromAccessToken(jwt({ sub: 'u1', role: 'admin' }))).toBe('admin');
  expect(roleFromAccessToken(jwt({ sub: 'u1' }))).toBeNull();
  expect(roleFromAccessToken('not-a-token')).toBeNull();
  expect(normalizeRole('user')).toBe('buyer');
  expect(normalizeRole('SELLER')).toBe('seller');
  expect(isSeller({ role: 'seller' })).toBe(true);
  expect(isSeller({ role: 'buyer' })).toBe(false);
  expect(isSeller({ role: 'admin' })).toBe(false);
});

test('buyer: tabs are Home, Market, My Bids and Orders – no Sell', async () => {
  mockRole = 'buyer';
  const tree = await mount(<AppNavigator />);
  expect(tabLabels(tree)).toEqual(['Home', 'Market', 'My Bids', 'Orders']);
});

test('seller: the Sell tab is there too', async () => {
  mockRole = 'seller';
  const tree = await mount(<AppNavigator />);
  expect(tabLabels(tree)).toEqual(['Home', 'Market', 'Sell', 'My Bids', 'Orders']);
});

test('sign up: no role picker, and "Want to sell? Apply to sell" opens the seller form (no email app)', async () => {
  const open = jest.spyOn(Linking, 'openURL').mockResolvedValue(undefined);
  const tree = await mount(<SignUpScreen navigation={navigation} route={{ key: 's', name: 'SignUp' } as any} />);

  const t = texts(tree);
  expect(t).not.toMatch(/Customer|Service Pro|Seller account/);
  expect(t).toContain(`Want to sell on ${config.APP_NAME}? Apply to sell`);

  await press(tree, x => x === 'Apply to sell');
  expect(navigation.navigate).toHaveBeenCalledWith('SellerApplication', { email: undefined });
  expect(open).not.toHaveBeenCalled();
});

test('profile: buyers get locations, garage and "Apply to sell"; no My Listings', async () => {
  mockRole = 'buyer';
  const tree = await mount(<ProfileScreen navigation={navigation} route={{ key: 'p', name: 'Profile' } as any} />);
  const t = texts(tree);
  expect(t).toContain('BUYER');
  expect(t).toContain('Saved locations & addresses');
  expect(t).toContain('My Garage');
  expect(t).toContain(`Want to sell on ${config.APP_NAME}?`);
  expect(t).toContain('APPLY TO SELL');
  expect(t).not.toContain('My Listings');
  expect(t).not.toContain('Sell an item');
});

test('profile: sellers get Sell, My Listings and their seller profile with reviews', async () => {
  mockRole = 'seller';
  const tree = await mount(<ProfileScreen navigation={navigation} route={{ key: 'p', name: 'Profile' } as any} />);
  const t = texts(tree);
  expect(t).toContain('SELLER');
  expect(t).toContain('My Listings');
  expect(t).not.toContain('APPLY TO SELL');

  await press(tree, x => x === 'My seller profile & reviews');
  expect(navigation.navigate).toHaveBeenCalledWith('SellerProfile', { seller: 'ravi', sellerName: 'Ravi Kumar' });
});

const order = (patch: Partial<Order>) => ({
  id: 'o', auctionId: 'a', seller: 'bob', buyer: 'ravi', otherParty: 'bob', title: 'Item', finalTotal: 1000,
  deliveryOptions: ['Pickup'], deliveryMethod: 'Pickup', deliveryChoices: [], status: 'SoldAwaitingHandover',
  statusLabel: 'Sold', tracking: [], actions: [], createdAt: '2026-10-12T10:00:00Z', ...patch,
}) as unknown as Order;

test('orders: a buyer sees purchases only – no Sales, no My listings', async () => {
  mockRole = 'buyer';
  market.orders.mockResolvedValue([order({ id: 'p1', role: 'buyer', title: 'Brake pads' })]);
  const tree = await mount(<MyPurchasesScreen navigation={navigation} route={{ key: 'o', name: 'MyPurchases' } as any} />);

  const t = texts(tree);
  expect(t).toContain('MY PURCHASES');
  expect(t).toContain('Brake pads');
  expect(t).not.toContain('Sales');
  expect(t).not.toContain('My listings');
});

test('orders: a seller switches between Purchases and Sales to fulfil', async () => {
  mockRole = 'seller';
  market.orders.mockResolvedValue([
    order({ id: 'p1', role: 'buyer', title: 'Brake pads' }),
    order({ id: 's1', role: 'seller', seller: 'ravi', buyer: 'carol', otherParty: 'carol', title: 'Headlamp' }),
  ]);
  const tree = await mount(<MyPurchasesScreen navigation={navigation} route={{ key: 'o', name: 'MyPurchases' } as any} />);

  let t = texts(tree);
  expect(t).toContain('My listings');
  expect(t).toContain('Sales (1)');
  expect(t).toContain('Brake pads');
  expect(t).not.toContain('Headlamp');

  await press(tree, x => x === 'Sales (1)');
  t = texts(tree);
  expect(t).toContain('SALES TO FULFIL');
  expect(t).toContain('Headlamp');
  expect(t).not.toContain('Brake pads');
});

describe('Home header: role badge before the location', () => {
  // eslint-disable-next-line @typescript-eslint/no-var-requires
  const { HomeScreen } = require('../src/screens/HomeScreen');
  // eslint-disable-next-line @typescript-eslint/no-var-requires
  const { BrowseProvider } = require('../src/search/BrowseContext');
  const home = () => (
    <BrowseProvider>
      <HomeScreen navigation={navigation} route={{ key: 'h', name: 'Home' } as any} />
    </BrowseProvider>
  );
  const badge = (tree: ReactTestRenderer.ReactTestRenderer) => tree.root.find(n => n.props.testID === 'role-badge' && (n.type as unknown) === 'View');
  afterEach(() => {
    mockRole = 'buyer';
    mockLocations.location = null;
  });

  test.each([['buyer', 'BUYER'], ['seller', 'SELLER'], ['admin', 'ADMIN'], ['user', 'BUYER']])(
    'role %s → [%s] on the location line',
    async (role, label) => {
      mockRole = role;
      const tree = await mount(home());
      expect(textOf(badge(tree))).toBe(label);
    },
  );

  test('badge and location share one line; a long location is cut with "…", never the badge', async () => {
    (mockLocations as any).location = {
      area: 'Kukatpally Housing Board Colony Phase 9 Near the Very Long Landmark Road', city: 'Hyderabad', countryCode: 'IN',
      latitude: 17.49, longitude: 78.41, radiusKm: 60,
    };
    mockRole = 'seller';
    const tree = await mount(home());
    const pill = badge(tree);
    let row = pill.parent!;
    while (!(row.props.style && !Array.isArray(row.props.style) && row.props.style.flexDirection === 'row')) row = row.parent!;
    const location = row.find(n => (n.type as unknown) === 'Text' && textOf(n).startsWith('📍'));
    // The radius is at the end; a long place is cut in the middle so the radius stays visible.
    expect(textOf(location)).toBe('📍 Kukatpally Housing Board Colony Phase 9 Near the Very Long Landmark Road, Hyderabad, India · 60 km ▾');
    expect(location.props.numberOfLines).toBe(1);
    expect(location.props.ellipsizeMode).toBe('middle');
    // The badge never shrinks; the location takes the rest of the line.
    expect([pill.props.style].flat()).toEqual(expect.arrayContaining([expect.objectContaining({ flexShrink: 0 })]));
  });

  test('a role change (buyer upgraded to seller, picked up at the next token refresh) shows at once', async () => {
    const tree = await mount(home());
    expect(textOf(badge(tree))).toBe('BUYER');
    mockRole = 'seller'; // AuthContext puts the new token's role into user.role
    await ReactTestRenderer.act(async () => tree.update(home()));
    expect(textOf(badge(tree))).toBe('SELLER');
  });
});
