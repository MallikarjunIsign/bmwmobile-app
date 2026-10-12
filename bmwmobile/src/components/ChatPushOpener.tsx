import { useEffect } from 'react';
import { useNavigation } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import type { RootStackParamList } from '../types/navigation';
import { onChatNotificationOpened } from '../services/push';

/** Tapping a push notification: a chat opens the conversation, a review reminder opens Orders. */
export function ChatPushOpener() {
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  useEffect(
    () =>
      onChatNotificationOpened(
        conversationId => navigation.navigate('Conversation', { conversationId }),
        () => navigation.navigate('MainTabs', { screen: 'MyPurchases' }),
      ),
    [navigation],
  );
  return null;
}
