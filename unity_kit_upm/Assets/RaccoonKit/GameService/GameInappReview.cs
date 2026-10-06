// Google Play In-App Review only runs on Android devices with the Play Review package imported.
#if UNITY_ANDROID && !UNITY_EDITOR && USING_INAPPREVIEW
#define RACCOON_PLAY_REVIEW
#endif

using System;
using System.Collections;
#if RACCOON_PLAY_REVIEW
using Google.Play.Review;
#endif
using UnityEngine;

namespace Raccoon.GameService
{
    /// <summary>
    /// In-app review (rating) cho Android (Google Play In-App Review) và iOS (SKStoreReviewController).
    /// - Gọi TryShowReview() ở các thời điểm "vui" (thắng trận, hoàn thành tut...) — class tự check điều kiện.
    /// - Gọi ShowReviewNow() khi user chủ động bấm nút "Rate us" — mở thẳng trang store.
    /// Lưu ý: cả 2 store đều có quota riêng, popup native có thể không hiện dù API trả về thành công,
    /// nên không dùng native review cho nút bấm (Google và Apple đều khuyến cáo như vậy).
    /// Android cần import package Google Play Review (define USING_INAPPREVIEW), nếu không sẽ bỏ qua native review.
    /// </summary>
    public class GameInappReview : MonoBehaviour
    {
        private static GameInappReview instance;
        public static GameInappReview Instance => instance;

        /// Gọi ngay trước khi hiện popup review native (vd: tạm dừng app-open ad khi app bị pause/resume).
        public static event Action OnBeforeShowReview;

        /// Gọi sau khi flow review native kết thúc (luôn đi cặp với OnBeforeShowReview), tham số là kết quả API.
        public static event Action<bool> OnAfterShowReview;

        [Header("Store")]
        [Tooltip("Apple ID của app trên App Store (dãy số), dùng để mở trang store")]
        [SerializeField] private string iosAppStoreId = "";

        [Header("Conditions")]
        [SerializeField] private int minSessions = 3;
        [SerializeField] private float minDaysSinceInstall = 1f;
        [SerializeField] private float minDaysBetweenPrompts = 7f;
        [SerializeField] private int maxPrompts = 3;

        private const string KeyFirstLaunch = "iar_first_launch";
        private const string KeySessionCount = "iar_session_count";
        private const string KeyLastPrompt = "iar_last_prompt";
        private const string KeyPromptCount = "iar_prompt_count";
        private const string KeyDisabled = "iar_disabled";
        private const string KeyFirstSessionShown = "iar_first_session_shown";

        private bool isShowing;
        private bool nativeFlowStarted;
        private Action<bool> pendingOnComplete;

#if RACCOON_PLAY_REVIEW
        private ReviewManager reviewManager;
#endif

        public int SessionCount => PlayerPrefs.GetInt(KeySessionCount, 0);

        // Static state survives Play Mode when Domain Reload is disabled, so reset it explicitly.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            instance = null;
            OnBeforeShowReview = null;
            OnAfterShowReview = null;
        }

        private void Awake()
        {
            if (instance != null)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);

            TrackSession();
        }

        private void OnDisable()
        {
            // Unity stops coroutines on disable; release the flow so later calls are not blocked forever.
            if (isShowing) FinishShowing(false);
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void TrackSession()
        {
            if (!PlayerPrefs.HasKey(KeyFirstLaunch))
                PlayerPrefs.SetString(KeyFirstLaunch, DateTime.UtcNow.Ticks.ToString());

            PlayerPrefs.SetInt(KeySessionCount, SessionCount + 1);
            PlayerPrefs.Save();
        }

        /// <summary>Hiện review native nếu thoả điều kiện (session, số ngày, cooldown, số lần tối đa).</summary>
        public void TryShowReview(Action<bool> onComplete = null)
        {
            if (!CanShowReview())
            {
                onComplete?.Invoke(false);
                return;
            }
            StartReview(onComplete);
        }

        /// <summary>Chỉ hiện review ở session đầu tiên (1 lần), các session sau không hiện nữa. Bỏ qua điều kiện session/số ngày/cooldown.</summary>
        public void TryShowReviewFirstSession(Action<bool> onComplete = null)
        {
            if (!CanShowReviewFirstSession())
            {
                onComplete?.Invoke(false);
                return;
            }
            PlayerPrefs.SetInt(KeyFirstSessionShown, 1);
            StartReview(onComplete);
        }

        public bool CanShowReviewFirstSession()
        {
            return !isShowing
                && !IsAutoPromptBlocked()
                && SessionCount == 1
                && PlayerPrefs.GetInt(KeyFirstSessionShown, 0) == 0;
        }

        /// <summary>Dùng cho nút "Rate us": mở thẳng trang store, không phụ thuộc quota của native review.</summary>
        public void ShowReviewNow(Action<bool> onComplete = null)
        {
            bool opened = OpenStorePage();
            onComplete?.Invoke(opened);
        }

        [Obsolete("Dùng ShowReviewNow() — nút bấm giờ luôn mở trang store.")]
        public void ShowReviewNowTryStore(Action<bool> onComplete = null)
        {
            ShowReviewNow(onComplete);
        }

        /// <summary>Không bao giờ tự hiện review nữa (vd: user đã rate hoặc bấm "Không, cảm ơn").</summary>
        public void DisableAutoPrompt()
        {
            PlayerPrefs.SetInt(KeyDisabled, 1);
            PlayerPrefs.Save();
        }

        public bool CanShowReview()
        {
            if (isShowing) return false;
            if (IsAutoPromptBlocked()) return false;
            if (SessionCount < minSessions) return false;
            if (DaysSince(KeyFirstLaunch) < minDaysSinceInstall) return false;
            if (PlayerPrefs.HasKey(KeyLastPrompt) && DaysSince(KeyLastPrompt) < minDaysBetweenPrompts) return false;
            return true;
        }

        private bool IsAutoPromptBlocked()
        {
            return PlayerPrefs.GetInt(KeyDisabled, 0) == 1
                || PlayerPrefs.GetInt(KeyPromptCount, 0) >= maxPrompts;
        }

        private void StartReview(Action<bool> onComplete)
        {
            isShowing = true;
            pendingOnComplete = onComplete;
            StartCoroutine(ShowReviewRoutine());
        }

        private void BeginNativeFlow()
        {
            MarkPrompted();
            nativeFlowStarted = true;
            OnBeforeShowReview?.Invoke();
        }

        private void FinishShowing(bool success)
        {
            isShowing = false;
            if (nativeFlowStarted)
            {
                nativeFlowStarted = false;
                OnAfterShowReview?.Invoke(success);
            }

            var callback = pendingOnComplete;
            pendingOnComplete = null;
            callback?.Invoke(success);
        }

        private IEnumerator ShowReviewRoutine()
        {
            bool success = false;

#if RACCOON_PLAY_REVIEW
            reviewManager ??= new ReviewManager();

            // Request ReviewInfo right before launching: it is only valid for a short time.
            var requestFlow = reviewManager.RequestReviewFlow();
            yield return requestFlow;

            if (requestFlow.Error != ReviewErrorCode.NoError)
            {
                Debug.LogWarning($"[InAppReview] RequestReviewFlow error: {requestFlow.Error}");
            }
            else
            {
                BeginNativeFlow();
                var launchFlow = reviewManager.LaunchReviewFlow(requestFlow.GetResult());
                yield return launchFlow;
                success = launchFlow.Error == ReviewErrorCode.NoError;
                if (!success)
                    Debug.LogWarning($"[InAppReview] LaunchReviewFlow error: {launchFlow.Error}");
            }
#elif UNITY_IOS && !UNITY_EDITOR
            BeginNativeFlow();
            success = UnityEngine.iOS.Device.RequestStoreReview();
            yield return null;
#else
            Debug.Log("[InAppReview] Native review unavailable (Editor, unsupported platform or USING_INAPPREVIEW not defined) - skip");
            yield return null;
#endif

            FinishShowing(success);
        }

        /// <summary>Mở trang app trên store. Trả về false nếu không mở được (vd: thiếu iosAppStoreId).</summary>
        public bool OpenStorePage()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Play Store handles this https link itself when installed, otherwise the browser opens it.
            Application.OpenURL($"https://play.google.com/store/apps/details?id={Application.identifier}");
            return true;
#elif UNITY_IOS && !UNITY_EDITOR
            if (string.IsNullOrEmpty(iosAppStoreId))
            {
                Debug.LogWarning("[InAppReview] iosAppStoreId chưa được set");
                return false;
            }
            Application.OpenURL($"https://apps.apple.com/app/id{iosAppStoreId}?action=write-review");
            return true;
#else
            Debug.Log($"[InAppReview] OpenStorePage: {Application.identifier}");
            return false;
#endif
        }

        private void MarkPrompted()
        {
            PlayerPrefs.SetInt(KeyPromptCount, PlayerPrefs.GetInt(KeyPromptCount, 0) + 1);
            PlayerPrefs.SetString(KeyLastPrompt, DateTime.UtcNow.Ticks.ToString());
            PlayerPrefs.Save();
        }

        private static double DaysSince(string key)
        {
            if (!long.TryParse(PlayerPrefs.GetString(key, ""), out long ticks)) return 0;
            return (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalDays;
        }
    }
}
