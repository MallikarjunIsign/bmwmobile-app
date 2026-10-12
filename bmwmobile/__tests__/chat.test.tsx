import React from 'react';
import { Alert } from 'react-native';
import ReactTestRenderer from 'react-test-renderer';
import { ConversationScreen } from '../src/screens/chat/ConversationScreen';
import { InboxScreen } from '../src/screens/chat/InboxScreen';
import { ChatApi } from '../src/services/chatApi';
import { inboxTime, quickQuestions, ticks } from '../src/chat/chatFormat';
import type { Conversation, ConversationSummary } from '../src/types/chat';

jest.setTimeout(20000);

// Live events: the tests fire them by hand.
const mockHandlers: Record<string, ((m: any) => void)[]> = {};
jest.mock('../src/services/notifications', () => {
  const ReactActual = require('react');
  // Like the real hook: subscribed once while mounted, always calling the latest handler.
  return {
    useLiveEvent: (event: string, handler: (m: any) => void) => {
      const latest = ReactActual.useRef(handler);
      latest.current = handler;
      ReactActual.useEffect(() => {
        const h = (m: any) => latest.current(m);
        (mockHandlers[event] ??= []).push(h);
        return () => {
          mockHandlers[event] = mockHandlers[event].filter(x => x !== h);
        };
      }, [event]);
    },
  };
});
const fire = async (event: string, message: unknown) =>
  ReactTestRenderer.act(async () => (mockHandlers[event] ?? []).forEach(h => h(message)));

const mockChat = { unread: 0, refreshUnread: jest.fn(), activeConversation: { current: null as string | null } };
jest.mock('../src/chat/ChatContext', () => ({ useChat: () => mockChat }));
jest.mock('../src/location/GarageContext', () => ({
  useGarage: () => ({ selected: { id: 'g1', year: 2018, make: 'Toyota', model: 'Hilux' } }),
}));
jest.mock('../src/location/LocationContext', () => ({
  useLocations: () => ({ location: { city: 'Hyderabad' }, country: 'IN' }),
}));
jest.mock('../src/auth/session', () => ({
  currentUsername: () => 'alice',
  isCurrentUser: (name?: string | null) => name === 'alice',
}));
jest.mock('react-native-image-picker', () => ({ launchImageLibrary: jest.fn(), launchCamera: jest.fn() }));
jest.mock('../src/services/chatApi', () => ({
  ChatApi: {
    get: jest.fn(), start: jest.fn(), send: jest.fn(), read: jest.fn(), inbox: jest.fn(), block: jest.fn(), unblock: jest.fn(),
    report: jest.fn(), uploadPhotos: jest.fn(),
  },
}));
jest.mock('@react-navigation/native', () => ({
  ...jest.requireActual('@react-navigation/native'),
  useFocusEffect: (effect: () => void | (() => void)) => require('react').useEffect(effect, [effect]),
}));

const api = ChatApi as unknown as Record<string, jest.Mock>;

const textOf = (node: ReactTestRenderer.ReactTestInstance | string): string =>
  typeof node === 'string' ? node : node.children.map(textOf).join('');
const texts = (tree: ReactTestRenderer.ReactTestRenderer) =>
  tree.root.findAll(n => (n.type as unknown) === 'Text').map(textOf).join(' | ');
const press = async (tree: ReactTestRenderer.ReactTestRenderer, match: (t: string) => boolean) => {
  let node: ReactTestRenderer.ReactTestInstance | null = tree.root.find(n => (n.type as unknown) === 'Text' && match(textOf(n)));
  while (node && !node.props.onPress) node = node.parent;
  await ReactTestRenderer.act(async () => node!.props.onPress());
};

const navigation = { goBack: jest.fn(), navigate: jest.fn() } as any;

const conversation: Conversation = {
  id: 'c1',
  listing: { id: 'a1', title: '2016–2020 Toyota Hilux Front Bumper', imageUrl: null, price: 14000, currency: 'INR', status: 'Live', saleType: 'Auction', deleted: false },
  role: 'buying', buyer: 'alice', seller: 'bob', otherUser: 'bob', blockedByMe: false, blockedMe: false, readOnly: false, showSafetyTip: true,
  messages: [], hasOlder: false,
};

const mounted: ReactTestRenderer.ReactTestRenderer[] = [];
const render = async (params: object) => {
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(<ConversationScreen navigation={navigation} route={{ key: 'c', name: 'Conversation', params } as any} />);
  });
  mounted.push(tree!);
  return tree!;
};

afterEach(async () => {
  // Unmount, so lists don't update after the test is over.
  for (const tree of mounted.splice(0)) await ReactTestRenderer.act(async () => tree.unmount());
});

beforeEach(() => {
  Object.values(api).forEach(m => m.mockReset());
  Object.keys(mockHandlers).forEach(k => delete mockHandlers[k]);
  api.read.mockResolvedValue(undefined);
  navigation.navigate.mockReset();
});

test('chat with seller: listing header, safety tip and quick questions; a chip fills the message, sending shows it with a tick', async () => {
  api.start.mockResolvedValue(conversation);
  api.send.mockResolvedValue({ id: 'm1', sender: 'alice', text: 'Does it fit my 2018 Toyota Hilux?', photos: [], sentAt: new Date().toISOString(), state: 'sent' });
  const tree = await render({ listingId: 'a1' });

  expect(api.start).toHaveBeenCalledWith('a1', undefined);
  let t = texts(tree);
  expect(t).toContain('2016–2020 Toyota Hilux Front Bumper');
  expect(t).toContain('₹14,000 · Live auction');
  expect(t).toContain('Keep payments and personal details inside the app until the item is sold.');
  expect(t).toContain('Is this still available?');
  expect(t).toContain('Is pickup / shipping possible to Hyderabad?');

  await press(tree, x => x === 'Does it fit my 2018 Toyota Hilux?');
  const send = tree.root.find(n => n.props.accessibilityLabel === 'Send' && n.props.onPress);
  await ReactTestRenderer.act(async () => send.props.onPress());

  expect(api.send).toHaveBeenCalledWith('c1', 'Does it fit my 2018 Toyota Hilux?', []);
  t = texts(tree);
  expect(t).toContain('Does it fit my 2018 Toyota Hilux?');
  expect(t).toMatch(/✓/);
  expect(api.read).toHaveBeenCalledWith('c1');

  // Tapping the header opens the listing.
  await press(tree, x => x === '2016–2020 Toyota Hilux Front Bumper');
  expect(navigation.navigate).toHaveBeenCalledWith('PartDetails', { auctionId: 'a1' });
});

test('ticks go to delivered / read when the other side receives / opens the chat; new messages appear live', async () => {
  const sentAt = new Date(Date.now() - 60000).toISOString();
  api.get.mockResolvedValue({ ...conversation, messages: [{ id: 'm1', sender: 'alice', text: 'Hello', photos: [], sentAt, state: 'sent' }] });
  const tree = await render({ conversationId: 'c1' });

  const tickText = () => tree.root.findAll(n => (n.type as unknown) === 'Text' && /✓/.test(textOf(n)) && !/Hello/.test(textOf(n))).map(textOf).pop();
  expect(tickText()).toContain('✓');
  expect(tickText()).not.toContain('✓✓');

  await fire('ChatReceipt', { conversationId: 'c1', notifyUser: 'alice', state: 'Read', upTo: new Date().toISOString() });
  expect(tickText()).toContain('✓✓');

  api.get.mockResolvedValue({
    ...conversation,
    messages: [
      { id: 'm1', sender: 'alice', text: 'Hello', photos: [], sentAt, state: 'read' },
      { id: 'm2', sender: 'bob', text: 'Yes, still available', photos: [], sentAt: new Date().toISOString(), state: 'sent' },
    ],
  });
  await fire('ChatMessage', { conversationId: 'c1', sender: 'bob', preview: 'Yes, still available' });
  expect(texts(tree)).toContain('Yes, still available');
  expect(api.read).toHaveBeenCalledTimes(2);
});

test('the seller only sees the buyer’s chat (no quick questions); "Message buyer" opens it with the buyer', async () => {
  api.start.mockResolvedValue({ ...conversation, role: 'selling', otherUser: 'alice', messages: [] });
  const tree = await render({ listingId: 'a1', buyer: 'alice' });

  expect(api.start).toHaveBeenCalledWith('a1', 'alice');
  expect(texts(tree)).not.toContain('Is this still available?');
});

test('block and report from the menu; a blocked or removed chat has no message box', async () => {
  api.get.mockResolvedValue(conversation);
  api.block.mockResolvedValue(undefined);
  api.report.mockResolvedValue(undefined);
  const alert = jest.spyOn(Alert, 'alert');
  const tree = await render({ conversationId: 'c1' });

  const menu = tree.root.find(n => n.props.accessibilityLabel === 'Block or report' && n.props.onPress);
  await ReactTestRenderer.act(async () => menu.props.onPress());
  const options = alert.mock.calls.at(-1)![2]!;
  expect(options.map(o => o.text)).toEqual(['Block bob', 'Report conversation', 'Cancel']);

  // Report → Scam.
  await ReactTestRenderer.act(async () => options[1].onPress!());
  const reasons = alert.mock.calls.at(-1)![2]!;
  expect(reasons.map(o => o.text)).toEqual(['Spam', 'Abuse', 'Scam', 'Other', 'Cancel']);
  await ReactTestRenderer.act(async () => reasons[2].onPress!());
  expect(api.report).toHaveBeenCalledWith('c1', 'scam');

  // Block → confirm.
  await ReactTestRenderer.act(async () => options[0].onPress!());
  const confirm = alert.mock.calls.at(-1)![2]!;
  await ReactTestRenderer.act(async () => confirm[1].onPress!());
  expect(api.block).toHaveBeenCalledWith('bob');
  expect(texts(tree)).toContain('You blocked bob. Unblock them in the ⋮ menu to send messages.');
  expect(tree.root.findAll(n => n.props.accessibilityLabel === 'Send')).toHaveLength(0);
  alert.mockRestore();

  api.get.mockResolvedValue({ ...conversation, readOnly: true, listing: { ...conversation.listing, deleted: true } });
  const removed = await render({ conversationId: 'c1' });
  expect(texts(removed)).toContain('This listing was removed. You can still read the chat.');
});

test('a send that timed out but was saved on the server is shown as sent – no error, nothing to send twice', async () => {
  api.get.mockResolvedValueOnce(conversation);
  const { ServerError } = jest.requireActual('../src/services/api');
  api.send.mockRejectedValue(new ServerError("Can't reach the server. Check your connection and try again.", 0, {}));
  // The check after the failure finds the message.
  api.get.mockResolvedValueOnce({
    ...conversation,
    messages: [{ id: 'm1', sender: 'alice', text: 'Hello', photos: [], sentAt: new Date().toISOString(), state: 'sent' }],
  });
  const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);
  const tree = await render({ conversationId: 'c1' });

  const input = tree.root.find(n => (n.type as unknown) === 'TextInput');
  await ReactTestRenderer.act(async () => input.props.onChangeText('Hello'));
  const send = tree.root.find(n => n.props.accessibilityLabel === 'Send' && n.props.onPress);
  await ReactTestRenderer.act(async () => send.props.onPress());

  expect(alert).not.toHaveBeenCalledWith('Not sent', expect.anything());
  expect(tree.root.find(n => (n.type as unknown) === 'TextInput').props.value).toBe('');
  expect(texts(tree)).toContain('Hello');
  alert.mockRestore();
});

test('a failed send puts the text back and explains why', async () => {
  api.get.mockResolvedValue(conversation);
  const { ServerError } = jest.requireActual('../src/services/api');
  api.send.mockRejectedValue(new ServerError("You're sending messages too fast – wait a minute", 429, {}));
  const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);
  const tree = await render({ conversationId: 'c1' });

  const input = tree.root.find(n => (n.type as unknown) === 'TextInput');
  await ReactTestRenderer.act(async () => input.props.onChangeText('Hello'));
  const send = tree.root.find(n => n.props.accessibilityLabel === 'Send' && n.props.onPress);
  await ReactTestRenderer.act(async () => send.props.onPress());

  expect(alert).toHaveBeenCalledWith('Not sent', "You're sending messages too fast – wait a minute");
  expect(tree.root.find(n => (n.type as unknown) === 'TextInput').props.value).toBe('Hello');
  alert.mockRestore();
});

test('inbox: newest first with the other user, listing, last message, time and unread; filter Buying / Selling', async () => {
  const items: ConversationSummary[] = [
    { id: 'c2', listing: { ...conversation.listing, id: 'a2', title: 'Tyre 265/65 R17' }, role: 'selling', otherUser: 'carol', lastMessage: 'Can you send more photos?',
      lastMessageSender: 'carol', lastMessageAt: new Date().toISOString(), unreadCount: 2, blockedByMe: false, blockedMe: false },
    { id: 'c1', listing: conversation.listing, role: 'buying', otherUser: 'bob', lastMessage: 'Is this still available?',
      lastMessageSender: 'alice', lastMessageAt: '2026-10-01T10:00:00Z', unreadCount: 0, blockedByMe: false, blockedMe: false },
  ];
  api.inbox.mockResolvedValue(items);
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(<InboxScreen navigation={navigation} route={{ key: 'i', name: 'Inbox' } as any} />);
  });
  mounted.push(tree!);

  const t = texts(tree!);
  expect(t.indexOf('carol')).toBeLessThan(t.indexOf('bob'));
  expect(t).toContain('Can you send more photos?');
  expect(t).toContain('You: Is this still available?');
  expect(t).toContain('2');
  expect(t).toContain('1 Oct');

  api.inbox.mockResolvedValue([items[1]]);
  await press(tree!, x => x === 'Buying');
  expect(api.inbox).toHaveBeenLastCalledWith('buying');

  await press(tree!, x => x === 'bob  · Seller' || x.startsWith('bob'));
  expect(navigation.navigate).toHaveBeenCalledWith('Conversation', { conversationId: 'c1' });
});

test('chat formatting', () => {
  const now = new Date(2026, 9, 12, 15, 0);
  expect(inboxTime(new Date(2026, 9, 12, 9, 5).toISOString(), now)).toBe('9:05 am');
  expect(inboxTime(new Date(2026, 9, 11, 9, 5).toISOString(), now)).toBe('Yesterday');
  expect(inboxTime(new Date(2026, 8, 30).toISOString(), now)).toBe('30 Sep');
  expect([ticks('sent'), ticks('delivered'), ticks('read')]).toEqual(['✓', '✓✓', '✓✓']);
  expect(quickQuestions(null, null)).toContain('Does it fit my vehicle?');
});
