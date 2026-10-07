# Changelog

Format theo [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), version theo [SemVer](https://semver.org/).

## [0.0.4] - 2026-10-07
### Added
- Module Save (`Raccoon.Save` + `.Editor`): `GameSave` lưu data local dạng JSON (primitive, class, `List`, `Dictionary`, struct Unity) trong `persistentDataPath`. Ghi atomic (`.tmp` → `.bak`), tự khôi phục khi file hỏng, mã hoá AES + HMAC (mặc định bật, Editor ghi plain), versioning + migration, tự save khi pause / mất focus / quit, `ImportFromPlayerPrefs`.
- `package.json` description liệt kê các dependency tùy chọn (Newtonsoft, Unity IAP, Firebase, Play Review) và define tương ứng.
- Define `USING_GAMESAVE`: tự bật khi có package `com.unity.nuget.newtonsoft-json`, không có thì `GameSave` compile nhưng không làm gì. Đã thêm vào **GameKit Setup**.
- Editor window **Raccoon → Save → Save Viewer...** (xem / sửa / xoá key, Play mode và Edit mode) và **Raccoon → Save → Clear Save**.
- Editor window **Raccoon → Build Report...**: tự đo size build sau mỗi lần build, so với build trước; size lệch quá ngưỡng (mặc định 1 MB) thì lưu lịch sử + tự mở report. Có asset nặng nhất, asset thay đổi, phân tích nội dung APK / AAB (size sau nén theo nhóm), Export CSV.

## [0.0.3] - 2026-10-06
### Added
- Editor window **Raccoon → GameKit Setup...** (`Raccoon.GameKit.Editor`): bật/tắt Scripting Define Symbols của kit theo build target, kiểm tra SDK đã có chưa, thêm/xoá define tùy ý.

## [0.0.2] - 2026-10-06
### Changed
- Firebase Remote Config tách riêng: chỉ bật khi có cả `USING_FIREBASE` và `USING_REMOTECONFIG` (tự bật khi cài package `com.google.firebase.remote-config`). Không có Remote Config thì `Get*` trả giá trị mặc định, `IsRemoteConfigReady` luôn `true`.
- Đổi define `USE_FIREBASE` → `USING_FIREBASE` (cùng quy ước `USING_*`).

## [0.0.1] - 2026-10-06
### Added
- Package UPM `com.raccoon.game-kit`, gom các module vào `Assets/RaccoonKit/`.
- Audio: `GameAudio` (SFX / music theo channel, duck, fade), `AudioLibrary`, `ButtonClickSound`.
- GameService: `GameFirebase` (Analytics, Crashlytics, Remote Config), `GameInappReview` (Google Play In-App Review / iOS StoreKit).
- Haptic: `GameHaptic` (Android `VibrationEffect`, iOS `UIFeedbackGenerator`), `ButtonClickHaptic`.
- Helpers: `SafeAreaCanvas`, `CanvasHelpers`, `LoadingDotsText`.
- Localization: `LocalizationManager` (CSV/JSON từ Resources), `LocalizedText`, `LocalizedFont`, `FontManager`, `LanguageSelector`, tool import Google Sheet.
- Purchase: `GameStoreController` (Unity IAP), `IAPProductData`, `ButtonPurchase`.
