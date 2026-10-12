import { useCallback, useEffect, useRef } from 'react';
import { Alert, AppState } from 'react-native';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { useNavigation } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import type { RootStackParamList } from '../types/navigation';
import { useAuth } from '../auth/AuthContext';
import { ApiService } from '../services/api';
import { useLiveEvent, type CustomOrderMatchedMessage } from '../services/notifications';

export const PART_AVAILABLE_TITLE = 'A part matching your request is now available';

const seenKey = (userId: string) => `@bidmyparts/seen-matched-orders/${userId}`;

async function readSeen(userId: string): Promise<string[]> {
  try {
    return JSON.parse((await AsyncStorage.getItem(seenKey(userId))) ?? '[]');
  } catch {
    return [];
  }
}

/**
 * In-app notification "A part matching your request is now available" with a button that opens the listing.
 * Live through SignalR, and – for matches that happened while the app was closed – checked when the app opens or
 * comes back to the foreground. Each matched order is announced once. Renders nothing.
 */
export function CustomOrderAlerts() {
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const { user } = useAuth();
  const userId = user?.id ?? null;
  const showing = useRef(false);

  const announce = useCallback(
    async (orderId: string, auctionId: string, title: string) => {
      if (!userId || showing.current) return;
      const seen = await readSeen(userId);
      if (seen.includes(orderId)) return;
      await AsyncStorage.setItem(seenKey(userId), JSON.stringify([...seen, orderId].slice(-200))).catch(() => undefined);
      showing.current = true;
      Alert.alert(PART_AVAILABLE_TITLE, title, [
        { text: 'Later', style: 'cancel', onPress: () => (showing.current = false) },
        {
          text: 'View part',
          onPress: () => {
            showing.current = false;
            navigation.navigate('PartDetails', { auctionId });
          },
        },
      ]);
    },
    [userId, navigation],
  );

  const checkMatchedOrders = useCallback(async () => {
    if (!userId) return;
    const orders = await ApiService.getPartRequests();
    const matched = orders.find(o => o.status === 'Matched' && o.matchedAuctionId);
    if (matched) {
      const what = [matched.partName || matched.searchText, matched.make, matched.model].filter(Boolean).join(' ');
      await announce(matched.id, matched.matchedAuctionId!, `For your custom order: ${what}`);
    }
  }, [userId, announce]);

  useEffect(() => {
    checkMatchedOrders();
    const sub = AppState.addEventListener('change', state => {
      if (state === 'active') checkMatchedOrders();
    });
    return () => sub.remove();
  }, [checkMatchedOrders]);

  useLiveEvent('CustomOrderMatched', (message: CustomOrderMatchedMessage) => {
    announce(message.orderId, message.auctionId, message.title);
  });

  return null;
}
