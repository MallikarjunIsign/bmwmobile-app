import { useEffect } from 'react';
import { useNavigation } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import type { RootStackParamList } from '../types/navigation';
import { onChatNotificationOpened } from '../services/push';

/**
 * Tapping a push notification: a chat opens the conversation, a review reminder opens Orders, a buyer's part request
 * opens Buyer requests (sellers), a matching part opens the listing (buyers).
 */
export function ChatPushOpener() {
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  useEffect(
    () =>
      onChatNotificationOpened(
        conversationId => navigation.navigate('Conversation', { conversationId }),
        () => navigation.navigate('MainTabs', { screen: 'MyPurchases' }),
        {
          buyerRequests: () => navigation.navigate('BuyerRequests'),
          listing: auctionId => navigation.navigate('PartDetails', { auctionId }),
        },
      ),
    [navigation],
  );
  return null;
}
