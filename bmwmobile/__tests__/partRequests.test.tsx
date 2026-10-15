import React from 'react';
import ReactTestRenderer from 'react-test-renderer';
import { NotificationToasts, notificationText } from '../src/components/NotificationToasts';
import { BuyerRequestsScreen } from '../src/screens/BuyerRequestsScreen';
import { MarketApi } from '../src/services/marketApi';

jest.setTimeout(20000);

// The live "Notify" handler, captured so the test can deliver messages.
const mockHandlers: Record<string, (payload: any) => void> = {};
jest.mock('../src/services/notifications', () => ({
  useLiveEvent: (event: string, handler: (payload: any) => void) => {
    mockHandlers[event] = handler;
  },
}));
let mockRole = 'seller';
jest.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ user: { username: 'ravi', role: mockRole } }) }));
jest.mock('../src/auth/session', () => ({ isCurrentUser: (name?: string | null) => name === 'ravi', currentUsername: () => 'ravi' }));
jest.mock('../src/chat/ChatContext', () => ({ useChat: () => ({ activeConversation: { current: null } }) }));
jest.mock('../src/location/LocationContext', () => ({ useLocations: () => ({ country: 'IN' }) }));
const mockNavigate = jest.fn();
jest.mock('@react-navigation/native', () => {
  const actual = jest.requireActual('@react-navigation/native');
  const { useEffect } = jest.requireActual('react');
  return {
    ...actual,
    useNavigation: () => ({ navigate: mockNavigate }),
    useFocusEffect: (effect: () => void) => useEffect(effect, [effect]),
  };
});
jest.mock('../src/services/marketApi', () => ({ MarketApi: { openPartRequests: jest.fn() } }));

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
  mockNavigate.mockReset();
});

const request = { type: 'partRequest', auctionId: 'r1', title: 'Front bumper – 2016 Toyota Hilux', detail: 'Hyderabad', from: 'alice' };
const deliver = (n: object) => ReactTestRenderer.act(async () => mockHandlers.Notify(n));

test('the text of a buyer request', () => {
  expect(notificationText(request as any, n => `₹${n}`)).toEqual({
    title: '🔎 A buyer is looking for a part',
    body: 'Front bumper – 2016 Toyota Hilux (Hyderabad). Have one? Tap to see buyer requests.',
  });
});

test('a seller sees a buyer request live; tapping opens Buyer requests', async () => {
  mockRole = 'seller';
  const tree = await mount(<NotificationToasts />);
  await deliver(request);

  expect(texts(tree)).toContain('🔎 A buyer is looking for a part');
  await press(tree, t => t === '🔎 A buyer is looking for a part');
  expect(mockNavigate).toHaveBeenCalledWith('BuyerRequests');
});

test('buyers, and the seller who asked, are not shown buyer requests', async () => {
  mockRole = 'buyer';
  const buyer = await mount(<NotificationToasts />);
  await deliver(request);
  expect(texts(buyer)).not.toContain('A buyer is looking for a part');

  mockRole = 'seller';
  const own = await mount(<NotificationToasts />);
  await deliver({ ...request, from: 'ravi' });
  expect(texts(own)).not.toContain('A buyer is looking for a part');
});

test('Buyer requests: what buyers want, their city and budget – no names; "list it" opens Sell', async () => {
  (MarketApi.openPartRequests as jest.Mock).mockResolvedValue([
    { id: 'r1', title: 'Front bumper – 2016 Toyota Hilux', locationName: 'Hyderabad', maxBudget: 15000, createdAt: '2026-10-13T09:00:00Z' },
    { id: 'r2', title: 'Part no. 52119-0K935', partNumber: '52119-0K935', createdAt: '2026-10-12T09:00:00Z' },
  ]);
  const tree = await mount(<BuyerRequestsScreen navigation={{ navigate: mockNavigate, goBack: jest.fn() } as any} route={{ key: 'b', name: 'BuyerRequests' } as any} />);

  const t = texts(tree);
  expect(t).toContain('🔎 Front bumper – 2016 Toyota Hilux');
  expect(t).toContain('📍 Hyderabad');
  expect(t).toContain('Budget up to ₹15,000');
  expect(t).toContain('🔎 Part no. 52119-0K935');
  expect(t).not.toContain('alice');

  // One button per request.
  const buttons = tree.root.findAll(n => (n.type as unknown) === 'Text' && textOf(n) === 'I have this – list it');
  expect(buttons).toHaveLength(2);
  let node: ReactTestRenderer.ReactTestInstance | null = buttons[0];
  while (node && !node.props.onPress) node = node.parent;
  await ReactTestRenderer.act(async () => node!.props.onPress());
  expect(mockNavigate).toHaveBeenCalledWith('MainTabs', { screen: 'Sell' });
});
