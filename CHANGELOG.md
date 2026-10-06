# Changelog

Format theo [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), version theo [SemVer](https://semver.org/).

## [0.0.1] - 2026-10-06
### Added
- Package UPM `com.raccoon.game-kit`, gom các module vào `Assets/RaccoonKit/`.
- Audio: `GameAudio` (SFX / music theo channel, duck, fade), `AudioLibrary`, `ButtonClickSound`.
- GameService: `GameFirebase` (Analytics, Crashlytics, Remote Config), `GameInappReview` (Google Play In-App Review / iOS StoreKit).
- Haptic: `GameHaptic` (Android `VibrationEffect`, iOS `UIFeedbackGenerator`), `ButtonClickHaptic`.
- Helpers: `SafeAreaCanvas`, `CanvasHelpers`, `LoadingDotsText`.
- Localization: `LocalizationManager` (CSV/JSON từ Resources), `LocalizedText`, `LocalizedFont`, `FontManager`, `LanguageSelector`, tool import Google Sheet.
- Purchase: `GameStoreController` (Unity IAP), `IAPProductData`, `ButtonPurchase`.
