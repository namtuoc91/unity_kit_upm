#import <UIKit/UIKit.h>

// Native side of GameHaptic.cs, uses UIFeedbackGenerator (iOS 10+)
// Generators are kept alive and prepared so the first haptic has no latency

static UISelectionFeedbackGenerator* selectionGenerator = nil;
static UINotificationFeedbackGenerator* notificationGenerator = nil;
static UIImpactFeedbackGenerator* impactGenerators[3] = { nil, nil, nil };

static UIImpactFeedbackGenerator* GetImpactGenerator(int style)
{
    if (style < 0 || style > 2)
        style = 1;
    if (impactGenerators[style] == nil)
        impactGenerators[style] = [[UIImpactFeedbackGenerator alloc] initWithStyle:(UIImpactFeedbackStyle)style];
    return impactGenerators[style];
}

extern "C"
{
    void _GameHaptic_Prepare()
    {
        if (selectionGenerator == nil)
            selectionGenerator = [[UISelectionFeedbackGenerator alloc] init];
        if (notificationGenerator == nil)
            notificationGenerator = [[UINotificationFeedbackGenerator alloc] init];

        [selectionGenerator prepare];
        [notificationGenerator prepare];
        for (int i = 0; i < 3; i++)
            [GetImpactGenerator(i) prepare];
    }

    void _GameHaptic_Selection()
    {
        if (selectionGenerator == nil)
            selectionGenerator = [[UISelectionFeedbackGenerator alloc] init];
        [selectionGenerator selectionChanged];
        [selectionGenerator prepare];
    }

    void _GameHaptic_Impact(int style, float intensity)
    {
        UIImpactFeedbackGenerator* generator = GetImpactGenerator(style);
        if (@available(iOS 13.0, *))
            [generator impactOccurredWithIntensity:MAX(0.0f, MIN(1.0f, intensity))];
        else
            [generator impactOccurred];
        [generator prepare];
    }

    void _GameHaptic_Notification(int type)
    {
        if (notificationGenerator == nil)
            notificationGenerator = [[UINotificationFeedbackGenerator alloc] init];
        [notificationGenerator notificationOccurred:(UINotificationFeedbackType)type];
        [notificationGenerator prepare];
    }
}
