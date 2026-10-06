# Raccoon Game Kit (`com.raccoon.game-kit`)

Bộ module dùng chung cho game mobile **Unity 6** (6000.0+): Audio, GameService (Firebase, In-App Review), Haptic, Helpers, Localization, Purchase.

## Cài đặt

Source nằm ở `unity_kit_upm/Assets/RaccoonKit/`; GitHub Action tách nó ra branch `upm` + tag `v<version>`.

Package Manager → `+` → **Add package from git URL...**:

```
https://github.com/namtuoc91/unity_kit_upm.git#v0.0.3
```

Hoặc bản mới nhất: `https://github.com/namtuoc91/unity_kit_upm.git#upm`

Hoặc thêm thẳng vào `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.raccoon.game-kit": "https://github.com/namtuoc91/unity_kit_upm.git#v0.0.3"
  }
}
```

## Module

| Module | Assembly | Phụ thuộc thêm | Define |
|---|---|---|---|
| Audio | `Raccoon.Audio` (+ `.Editor`) | — | — |
| GameService | `Raccoon.GameService` | Firebase SDK, Google Play Review (tùy chọn) | `USING_FIREBASE` (tự thêm), `USING_REMOTECONFIG` (tự bật khi có `com.google.firebase.remote-config`), `USING_INAPPREVIEW` (tự bật khi có `com.google.play.review`) |
| Haptic | `Raccoon.Haptic` | — | `USING_HAPTIC` (tự thêm) |
| Helpers | `Raccoon.Helpers` | TextMeshPro | — |
| Localization | `Raccoon.Localization` (+ `.Editor`) | TextMeshPro (tùy chọn) | `USING_TMP` (tự bật theo `com.unity.ugui` 2.0+) |
| Purchase | `Raccoon.Purchase` | Unity IAP `com.unity.purchasing` | `USING_PURCHASE` (tự bật khi có package) |

"Tự thêm" = thêm vào **Player Settings → Scripting Define Symbols**. Không có define thì API vẫn compile nhưng không làm gì (hoặc trả về giá trị mặc định).

Bật/tắt define nhanh qua menu **Raccoon → GameKit Setup...**: chọn build target (Android / iOS / Standalone), tick define của kit (cột trạng thái báo SDK đã có hay chưa), thêm/xoá define tùy ý, rồi bấm **Apply**. Nếu SDK cài bằng `.unitypackage` (không phải UPM) thì `versionDefines` không tự bật — dùng window này để thêm define.

## Sử dụng

### Audio
Thêm `GameAudio` vào scene đầu tiên. Editor tự tạo `Assets/GameKit/Audio/AudioLibraryData.asset` (menu tạo tay: **Create → Raccoon → Audio Library**) và gán vào component.

```csharp
GameAudio.SFX("coin");                  // sound theo id trong AudioLibrary
GameAudio.Music("bg_main", 0.5f);       // music theo id, fade 0.5s
GameAudio.SFXOneShot(clip);
GameAudio.ClickButton();
GameAudio.Instance.SetEnableMusic(false);
```

Gắn `ButtonClickSound` vào Button để tự phát tiếng click.

### Haptic
Thêm `GameHaptic` vào scene, thêm define `USING_HAPTIC`. Android dùng `VibrationEffect` (JNI), iOS dùng `UIFeedbackGenerator` (`Plugins/iOS/GameHaptic.mm`).

```csharp
GameHaptic.Light();
GameHaptic.Success();
GameHaptic.Vibrate(0.2f);
GameHaptic.Instance.SetEnableHaptic(false);
```

Gắn `ButtonClickHaptic` vào Button để rung khi bấm. Gọi từ main thread (callback SDK dùng `ContinueWithOnMainThread`).

### Localization
File ngôn ngữ đặt ở `Resources/Localization/` (`en.csv`, `vi.csv`... hoặc `.json`, cột đầu là key).

```csharp
LocalizationManager.Instance.SetLanguage(Language.Vietnamese);
string s = LocalizationManager.Instance.Get("MENU_PLAY");
LocalizationManager.OnLanguageChanged += lang => { /* refresh UI */ };
```

Component: `LocalizedText` (gắn key cho Text/TMP), `LocalizedFont`, `LanguageSelector`, `FontManager`.

Menu Editor **Raccoon → Localization**:
- **Import Localization...** — import từ Google Sheet / CSV.
- **Setup LocalizedText in Scene** — gắn `LocalizedText` hàng loạt.
- **Add LocalizedFont to Scene**.

### GameService
**Firebase** (cần import Firebase Analytics, Crashlytics + define `USING_FIREBASE`; Remote Config cần thêm package `com.google.firebase.remote-config`, không có thì `Get*` trả giá trị mặc định và `WhenRemoteConfigReady` gọi ngay): thêm `GameFirebase` vào scene.

```csharp
GameFirebase.SetRemoteConfigDefaults(new Dictionary<string, object> { { "ads_interval", 30 } });
GameFirebase.WhenRemoteConfigReady(() => {
    int interval = GameFirebase.GetInt("ads_interval", 30);
});
GameFirebase.SendEvent("level_complete", "level", "5");
```

**In-App Review** (Android cần package Google Play Review; iOS dùng `SKStoreReviewController`): thêm `GameInappReview` vào scene.

```csharp
GameInappReview.Instance.TryShowReview();        // ở thời điểm "vui" (thắng màn...), tự check điều kiện
GameInappReview.Instance.ShowReviewNowTryStore(); // nút "Rate us" — mở trang store
```

Popup native có quota của store, có thể không hiện dù API báo thành công — không dùng native review cho nút bấm.

### Purchase
Cần package `com.unity.purchasing`. Tạo sản phẩm bằng **Create → Raccoon → Purchase → ProductIAP**, kéo vào list `lstProduct` của `GameStoreController`.

```csharp
GameStoreController.BuyProductById("remove_ads", (success, id) => { /* ... */ });
string price = GameStoreController.GetPriceProductById("remove_ads");
bool sub = GameStoreController.Instance.IsSubscribedTo("vip_weekly");
```

Hoặc dùng component `ButtonPurchase` (event `onPurchaseSuccess` / `onPurchaseFailed`).

### Helpers
- `SafeAreaCanvas` — co panel theo safe area (tai thỏ).
- `CanvasHelpers` — set `CanvasScaler` theo hướng màn hình Portrait / Landscape.
- `LoadingDotsText` — hiệu ứng "Loading..." chạy dấu chấm.
