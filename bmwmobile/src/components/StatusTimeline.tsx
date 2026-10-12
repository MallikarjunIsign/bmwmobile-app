import React from 'react';
import { View, Text, StyleSheet } from 'react-native';
import { COLORS, SPACING } from '../theme/colors';

interface TimelineStep {
  title: string;
  subtitle?: string;
  date?: string;
  completed: boolean;
  active: boolean;
}

interface StatusTimelineProps {
  steps: TimelineStep[];
}

export const StatusTimeline: React.FC<StatusTimelineProps> = ({ steps }) => {
  return (
    <View style={styles.container}>
      {steps.map((step, index) => {
        const isLast = index === steps.length - 1;
        return (
          <View key={index} style={styles.stepRow}>
            <View style={styles.indicatorCol}>
              <View style={[
                styles.dot,
                step.completed && styles.dotCompleted,
                step.active && styles.dotActive
              ]}>
                {step.completed ? (
                  <Text style={styles.check}>✓</Text>
                ) : step.active ? (
                  <View style={styles.innerDot} />
                ) : null}
              </View>
              {!isLast && (
                <View style={[
                  styles.line,
                  step.completed && styles.lineCompleted
                ]} />
              )}
            </View>

            <View style={styles.contentCol}>
              <Text style={[
                styles.stepTitle,
                (step.completed || step.active) && styles.stepTitleActive
              ]}>
                {step.title}
              </Text>
              {step.subtitle ? (
                <Text style={styles.stepSubtitle}>{step.subtitle}</Text>
              ) : null}
              {step.date ? (
                <Text style={styles.stepDate}>{step.date}</Text>
              ) : null}
            </View>
          </View>
        );
      })}
    </View>
  );
};

const styles = StyleSheet.create({
  container: {
    paddingVertical: SPACING.sm,
  },
  stepRow: {
    flexDirection: 'row',
    minHeight: 56,
  },
  indicatorCol: {
    alignItems: 'center',
    width: 32,
    marginRight: SPACING.md,
  },
  dot: {
    width: 24,
    height: 24,
    borderRadius: 12,
    backgroundColor: COLORS.card,
    borderWidth: 2,
    borderColor: COLORS.border,
    justifyContent: 'center',
    alignItems: 'center',
    zIndex: 2,
  },
  dotCompleted: {
    backgroundColor: COLORS.success,
    borderColor: COLORS.success,
  },
  dotActive: {
    backgroundColor: COLORS.primary,
    borderColor: COLORS.primaryGlow,
  },
  innerDot: {
    width: 8,
    height: 8,
    borderRadius: 4,
    backgroundColor: COLORS.text,
  },
  check: {
    color: COLORS.text,
    fontSize: 12,
    fontWeight: 'bold',
  },
  line: {
    width: 2,
    flex: 1,
    backgroundColor: COLORS.border,
    marginVertical: 2,
  },
  lineCompleted: {
    backgroundColor: COLORS.success,
  },
  contentCol: {
    flex: 1,
    paddingBottom: SPACING.md,
  },
  stepTitle: {
    color: COLORS.textMuted,
    fontSize: 14,
    fontWeight: '600',
  },
  stepTitleActive: {
    color: COLORS.text,
    fontWeight: '700',
  },
  stepSubtitle: {
    color: COLORS.textSecondary,
    fontSize: 12,
    marginTop: 2,
  },
  stepDate: {
    color: COLORS.primaryGlow,
    fontSize: 11,
    marginTop: 2,
    fontWeight: '500',
  },
});
